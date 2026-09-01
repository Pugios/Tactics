using System.Collections.Generic;
using Tactics.Player;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Tactics.Vision
{
    /// <summary>
    /// Owns the vision depth map ("eye as a spotlight" shadow mapping):
    /// drives a hidden perspective camera at the player's eye whose depth buffer
    /// answers, per screen pixel, whether a standing enemy would be visible.
    /// Also exposes the CPU visibility queries used by EntityVisibilityDriver.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    [RequireComponent(typeof(EntityVisibilityDriver))]
    [DefaultExecutionOrder(100)]
    public class VisionController : NetworkBehaviour
    {
        [Header("Field of View")]
        [SerializeField, Range(1f, 170f)] private float horizontalViewAngle = 103f;
        [SerializeField, Range(1f, 170f)] private float verticalViewAngle = 70.53f;
        [SerializeField] private float maxViewDistance = 500f;
        [Tooltip("SmoothDamp time (seconds) for the ADS zoom transition on the vision cone.")]
        [SerializeField] private float adsZoomSmoothTime = 0.15f;

        [Header("Enemy Proportions")]
        [SerializeField] private float enemyHeight = 2f;
        [SerializeField] private float enemyRadius = 0.5f;

        [Header("Line Of Sight (CPU queries)")]
        [SerializeField] private LayerMask losMask = VisionLayerMasks.DefaultLos;
        [SerializeField] private float losSkinWidth = 0.05f;

        [Header("Fog")]
        [SerializeField, Range(0f, 1f)] private float fogStrength = 0.9f;

        [Header("Vision Depth Map")]
        [SerializeField] private int depthMapResolution = 2048;
        [Tooltip("How far (in meters) a sample point may sit behind the recorded occluder and still count as visible. Prevents shadow acne.")]
        [SerializeField] private float depthBiasMeters = 0.05f;
        [SerializeField] private float eyeNearClip = 0.3f;

        [Header("Debug")]
        [SerializeField] private bool drawDebugAim;
        [SerializeField] private bool debugShowMaskTexture;
        [Tooltip("Draws the raw eye depth map in the corner of the game view (bright = close, black = far/empty).")]
        [SerializeField] private bool debugShowEyeDepth;
        [Tooltip("Draws the eye camera's normal color view in the corner of the game view")]
        [SerializeField] private bool debugShowEyeView;
        [Tooltip("Draws every capsule sample point checked against each VisibleEntity, green if that point passes the cone+LOS test, red if it doesn't.")]
        [SerializeField] private bool debugShowVisibilitySamplePoints;
        [SerializeField] private float debugSamplePointRadius = 0.05f;

        /// <summary>Body sample points as fractions of enemy height: head, chest, knees, feet.</summary>
        private static readonly Vector4 SampleHeightFractions = new Vector4(0.95f, 0.55f, 0.25f, 0.075f);

        private PlayerController playerController;
        private Vector3 aimOrigin;
        private Vector3 aimTarget;
        private bool aimValid;

        // ADS zoom: the vision cone narrows in tan-space by the active weapon's
        // current zoom level (PlayerController.CurrentZoomMultiplier). Only the
        // smoothed multiplier changes at runtime — the serialized base angles
        // are never mutated.
        private float currentZoomMultiplier = 1f;
        private float zoomVelocity;

        private UnityEngine.Camera eyeCamera;
        private RenderTexture eyeTargetTexture;
        private RenderTexture eyeDepthTexture;
        private RTHandle eyeDepthHandle;

        private readonly List<Renderer> selfRendererScratch = new List<Renderer>();
        private readonly List<Renderer> disabledSelfRenderers = new List<Renderer>();
        private bool warnedCaptureNotRunning;
        private int captureWarningGraceFrame = 120;
        private Matrix4x4 eyeViewProjection;
        private Matrix4x4 eyeViewMatrix;

        public static VisionController Active { get; private set; }

        public Vector3 AimOrigin => aimOrigin;
        public Vector3 AimTarget => aimTarget;
        public float MaxViewDistance => maxViewDistance;
        public float FogStrength => fogStrength;
        public bool HasValidAim => aimValid;
        public bool DebugShowMaskTexture => debugShowMaskTexture;

        /// <summary>The hidden camera rendering the world from the player's eye.</summary>
        public UnityEngine.Camera EyeCamera => eyeCamera;

        /// <summary>Persistent R32F texture holding the eye's device depth, sampled by the fog composite shader.</summary>
        public RTHandle EyeDepthHandle => eyeDepthHandle;

        public Texture EyeDepthTexture => eyeDepthTexture;

        /// <summary>Shader inputs for the fog composite pass. Snapshotted when the main camera records its render graph pass.</summary>
        public VisionShaderState GetShaderState()
        {
            if (!aimValid || eyeDepthTexture == null || eyeCamera == null)
                return VisionShaderState.Invalid;

            Matrix4x4 view = eyeCamera.worldToCameraMatrix;
            Matrix4x4 viewProjection = VisionEyeDepthCapturePass.LastEyeViewProjection != Matrix4x4.zero
                ? VisionEyeDepthCapturePass.LastEyeViewProjection
                : GL.GetGPUProjectionMatrix(eyeCamera.projectionMatrix, true) * view;

            return new VisionShaderState(
                true,
                viewProjection,
                view,
                aimOrigin,
                eyeCamera.transform.forward,
                ComputeZBufferParams(eyeNearClip, maxViewDistance),
                new Vector4(1f / depthMapResolution, depthBiasMeters, 0f, 0f),
                SampleHeightFractions * enemyHeight,
                eyeDepthTexture);
        }

        public VisionConfig Config => new VisionConfig(
            EffectiveViewAngle(horizontalViewAngle),
            EffectiveViewAngle(verticalViewAngle),
            maxViewDistance,
            enemyHeight,
            enemyRadius,
            losMask,
            losSkinWidth);

        private void Awake()
        {
            playerController = GetComponent<PlayerController>();
        }

        /// <summary>
        /// Base view angle narrowed by the current (smoothed) zoom in tan-space —
        /// the same math a camera zoom uses, so 1.25× maps 103°→90.3° and
        /// 70.53°→58.9°. Drives the CPU cone (Config) and GPU eye camera alike.
        /// </summary>
        private float EffectiveViewAngle(float baseAngle)
        {
            if (currentZoomMultiplier <= 1.0001f) return baseAngle;
            return 2f * Mathf.Atan(Mathf.Tan(baseAngle * 0.5f * Mathf.Deg2Rad) / currentZoomMultiplier) * Mathf.Rad2Deg;
        }

        private void UpdateAdsZoom()
        {
            float targetZoom = playerController != null ? playerController.CurrentZoomMultiplier : 1f;

            currentZoomMultiplier = Mathf.SmoothDamp(currentZoomMultiplier, targetZoom, ref zoomVelocity, adsZoomSmoothTime);
            if (Mathf.Abs(currentZoomMultiplier - targetZoom) < 0.001f)
            {
                currentZoomMultiplier = targetZoom;
                zoomVelocity = 0f;
            }
        }

        public override void OnNetworkSpawn()
        {
            if (!IsOwner) { enabled = false; return; }

            Active = this;
            CreateEyeResources();
            // The player spawns on connect, not scene load — the capture-health
            // check must measure from here, not from application startup.
            captureWarningGraceFrame = Time.frameCount + 120;
        }

        public override void OnNetworkDespawn()
        {
            if (Active == this)
                Active = null;
        }

        private void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
            RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;

            if (eyeCamera != null)
                eyeCamera.enabled = false;

            Shader.SetGlobalFloat(VisionShaderGlobals.HasValidMask, 0f);
        }

        private void OnDestroy()
        {
            ReleaseEyeResources();
            Shader.SetGlobalFloat(VisionShaderGlobals.HasValidMask, 0f);

            if (Active == this)
                Active = null;
        }

        private void OnValidate()
        {
            horizontalViewAngle = Mathf.Clamp(horizontalViewAngle, 1f, 170f);
            verticalViewAngle = Mathf.Clamp(verticalViewAngle, 1f, 170f);
            adsZoomSmoothTime = Mathf.Max(0f, adsZoomSmoothTime);
            maxViewDistance = Mathf.Max(1f, maxViewDistance);
            enemyRadius = Mathf.Max(0.01f, enemyRadius);
            enemyHeight = Mathf.Max(enemyRadius * 2f, enemyHeight);
            depthMapResolution = Mathf.Clamp(depthMapResolution, 256, 4096);
            depthBiasMeters = Mathf.Max(0f, depthBiasMeters);
            eyeNearClip = Mathf.Clamp(eyeNearClip, 0.01f, 5f);
        }

        private void LateUpdate()
        {
            RefreshAimState();
            UpdateAdsZoom();
            UpdateEyeCamera();
            DrawAimDebugLines();
        }

        public bool IsPointVisible(Vector3 worldPoint)
        {
            return aimValid && VisionEvaluator.IsPointVisible(aimOrigin, aimTarget, worldPoint, Config);
        }

        public bool IsEnemyVisibleAt(Vector3 feetPosition, float heightOverride = -1f, float radiusOverride = -1f)
        {
            return aimValid &&
                   VisionEvaluator.IsEnemyVisibleAt(
                       aimOrigin,
                       aimTarget,
                       feetPosition,
                       Config,
                       heightOverride,
                       radiusOverride);
        }

        private void RefreshAimState()
        {
            aimValid = false;

            if (playerController == null)
                return;

            Transform origin = playerController.VisionOrigin;
            if (origin == null)
                return;

            aimOrigin = origin.position;
            aimTarget = origin.forward;
            aimValid = true;
        }

        private void CreateEyeResources()
        {
            var cameraObject = new GameObject("Vision Eye Camera")
            {
                hideFlags = HideFlags.DontSave,
            };
            cameraObject.transform.SetParent(transform, false);

            eyeCamera = cameraObject.AddComponent<UnityEngine.Camera>();
            eyeCamera.enabled = false;
            eyeCamera.depth = -100f;
            eyeCamera.clearFlags = CameraClearFlags.SolidColor;
            eyeCamera.backgroundColor = Color.black;
            eyeCamera.cullingMask = VisionLayerMasks.EyeDepthCulling;
            eyeCamera.nearClipPlane = eyeNearClip;
            eyeCamera.farClipPlane = maxViewDistance;
            eyeCamera.fieldOfView = verticalViewAngle;
            eyeCamera.allowHDR = false;
            eyeCamera.allowMSAA = false;
            eyeCamera.useOcclusionCulling = false;

            var targetDescriptor = new RenderTextureDescriptor(
                depthMapResolution,
                depthMapResolution,
                RenderTextureFormat.ARGB32,
                32);
            eyeTargetTexture = new RenderTexture(targetDescriptor) { name = "VisionEyeTarget" };
            eyeTargetTexture.Create();
            eyeCamera.targetTexture = eyeTargetTexture;

            eyeDepthTexture = new RenderTexture(depthMapResolution, depthMapResolution, 0, RenderTextureFormat.RFloat)
            {
                name = "VisionEyeDepth",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
            };
            eyeDepthTexture.Create();
            eyeDepthHandle = RTHandles.Alloc(eyeDepthTexture);

            // Clear to "background depth" so a missing capture pass reads as fully open rather than garbage.
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture.active = eyeDepthTexture;
            GL.Clear(false, true, Color.clear);
            RenderTexture.active = previousActive;

            UniversalAdditionalCameraData cameraData = eyeCamera.GetUniversalAdditionalCameraData();
            cameraData.renderType = CameraRenderType.Base;
            cameraData.requiresDepthTexture = true;
            cameraData.requiresColorTexture = false;
            cameraData.renderShadows = false;
            cameraData.renderPostProcessing = false;
            cameraData.antialiasing = AntialiasingMode.None;

            Shader.SetGlobalTexture(VisionShaderGlobals.EyeDepth, eyeDepthTexture);
        }

        private void ReleaseEyeResources()
        {
            if (eyeDepthHandle != null)
            {
                eyeDepthHandle.Release();
                eyeDepthHandle = null;
            }

            if (eyeDepthTexture != null)
            {
                eyeDepthTexture.Release();
                Destroy(eyeDepthTexture);
                eyeDepthTexture = null;
            }

            if (eyeTargetTexture != null)
            {
                eyeTargetTexture.Release();
                Destroy(eyeTargetTexture);
                eyeTargetTexture = null;
            }

            if (eyeCamera != null)
            {
                Destroy(eyeCamera.gameObject);
                eyeCamera = null;
            }
        }

        private void UpdateEyeCamera()
        {
            if (eyeCamera == null)
                return;

            eyeCamera.enabled = aimValid;
            Shader.SetGlobalFloat(VisionShaderGlobals.HasValidMask, aimValid ? 1f : 0f);

            if (!aimValid)
                return;

            Vector3 up = Mathf.Abs(Vector3.Dot(aimTarget, Vector3.up)) > 0.999f ? Vector3.forward : Vector3.up;
            eyeCamera.transform.SetPositionAndRotation(aimOrigin, Quaternion.LookRotation(aimTarget, up));

            float effectiveVertical = EffectiveViewAngle(verticalViewAngle);
            float effectiveHorizontal = EffectiveViewAngle(horizontalViewAngle);
            eyeCamera.nearClipPlane = eyeNearClip;
            eyeCamera.farClipPlane = maxViewDistance;
            eyeCamera.fieldOfView = effectiveVertical;
            eyeCamera.aspect =
                Mathf.Tan(effectiveHorizontal * 0.5f * Mathf.Deg2Rad) /
                Mathf.Tan(effectiveVertical * 0.5f * Mathf.Deg2Rad);

            eyeViewMatrix = eyeCamera.worldToCameraMatrix;
            eyeViewProjection = VisionEyeDepthCapturePass.LastEyeViewProjection != Matrix4x4.zero
                ? VisionEyeDepthCapturePass.LastEyeViewProjection
                : GL.GetGPUProjectionMatrix(eyeCamera.projectionMatrix, true) * eyeViewMatrix;

            // Keep globals for any non-RenderGraph paths; composite pass binds these on its material directly.
            Shader.SetGlobalMatrix(VisionShaderGlobals.EyeVP, eyeViewProjection);
            Shader.SetGlobalMatrix(VisionShaderGlobals.EyeView, eyeViewMatrix);

            WarnIfCaptureNotRunning();

            Shader.SetGlobalVector(VisionShaderGlobals.EyePosition, aimOrigin);
            Shader.SetGlobalVector(VisionShaderGlobals.EyeForward, eyeCamera.transform.forward);
            Shader.SetGlobalVector(VisionShaderGlobals.EyeZBufferParams, ComputeZBufferParams(eyeNearClip, maxViewDistance));
            Shader.SetGlobalVector(VisionShaderGlobals.EyeDepthParams, new Vector4(1f / depthMapResolution, depthBiasMeters, 0f, 0f));
            Shader.SetGlobalVector(VisionShaderGlobals.SampleHeights, SampleHeightFractions * enemyHeight);
        }

        private void WarnIfCaptureNotRunning()
        {
            if (warnedCaptureNotRunning || Time.frameCount < captureWarningGraceFrame)
                return;

            if (VisionEyeDepthCapturePass.LastCaptureFrameCount < Time.frameCount - 60)
            {
                warnedCaptureNotRunning = true;
                Debug.LogWarning(
                    "[Vision] The eye depth capture pass has not run in the last 60 frames. " +
                    "The fog will treat the whole FoV as open ground. Check that the VisionFogRendererFeature " +
                    "on the active URP renderer has 'Depth Copy Shader' assigned (Tactics/VisionEyeDepthCopy) " +
                    "and that the Vision Eye Camera is rendering (look for it under the Player in the hierarchy).");
            }
        }

        /// <summary>Mirrors Unity's _ZBufferParams so the shader can convert raw eye depth to linear meters.</summary>
        private static Vector4 ComputeZBufferParams(float near, float far)
        {
            float farOverNear = far / near;
            return SystemInfo.usesReversedZBuffer
                ? new Vector4(farOverNear - 1f, 1f, (farOverNear - 1f) / far, 1f / far)
                : new Vector4(1f - farOverNear, farOverNear, (1f - farOverNear) / far, farOverNear / far);
        }

        // The eye sits inside the player's own model, so the player must not occlude their own vision.
        // Renderers are toggled only for the eye camera's render, then restored.
        private void OnBeginCameraRendering(ScriptableRenderContext context, UnityEngine.Camera camera)
        {
            if (camera != eyeCamera)
                return;

            selfRendererScratch.Clear();
            GetComponentsInChildren(false, selfRendererScratch);

            disabledSelfRenderers.Clear();
            for (int i = 0; i < selfRendererScratch.Count; i++)
            {
                Renderer selfRenderer = selfRendererScratch[i];
                if (selfRenderer != null && selfRenderer.enabled)
                {
                    selfRenderer.enabled = false;
                    disabledSelfRenderers.Add(selfRenderer);
                }
            }
        }

        private void OnEndCameraRendering(ScriptableRenderContext context, UnityEngine.Camera camera)
        {
            if (camera != eyeCamera)
                return;

            for (int i = 0; i < disabledSelfRenderers.Count; i++)
            {
                if (disabledSelfRenderers[i] != null)
                    disabledSelfRenderers[i].enabled = true;
            }

            disabledSelfRenderers.Clear();
        }

        private void DrawAimDebugLines()
        {
            if (!drawDebugAim || !Application.isPlaying || playerController == null)
                return;

            Transform origin = playerController.VisionOrigin;
            if (origin == null)
                return;

            Vector3 aimStart = origin.position;
            Vector3 groundPoint = playerController.AimGroundPoint;
            Vector3 targetPoint = playerController.LookTarget;

            Debug.DrawLine(aimStart, targetPoint, Color.magenta);
            Debug.DrawLine(groundPoint, targetPoint, Color.cyan);
            DebugDrawWireSphere(groundPoint, 0.2f, new Color(0.2f, 1f, 1f, 0.9f));
            DebugDrawWireSphere(targetPoint, 0.15f, Color.magenta);
        }

        private static void DebugDrawWireSphere(Vector3 center, float radius, Color color)
        {
            const int segments = 12;
            float step = 360f / segments;
            Vector3 prev = center + Vector3.right * radius;

            for (int i = 1; i <= segments; i++)
            {
                float angle = step * i * Mathf.Deg2Rad;
                Vector3 point = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                Debug.DrawLine(prev, point, color);
                prev = point;
            }

            prev = center + Vector3.up * radius;
            for (int i = 1; i <= segments; i++)
            {
                float angle = step * i * Mathf.Deg2Rad;
                Vector3 point = center + new Vector3(0f, Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                Debug.DrawLine(prev, point, color);
                prev = point;
            }
        }

        private void OnGUI()
        {
            float nextY = 10f;

            if (debugShowEyeDepth && eyeDepthTexture != null)
            {
                const float size = 320f;
                var rect = new Rect(10f, nextY, size, size);
                GUI.DrawTexture(rect, eyeDepthTexture, ScaleMode.ScaleToFit, false);
                GUI.Label(new Rect(rect.x, rect.yMax + 2f, size, 20f), "Vision eye depth (bright = close)");
                nextY = rect.yMax + 24f;
            }

            if (debugShowEyeView && eyeTargetTexture != null && eyeCamera != null)
            {
                // eyeTargetTexture is a square pixel buffer, but the camera's projection uses a
                // non-square aspect (from horizontal/vertical view angle) — size the rect to match
                // eyeCamera.aspect and stretch-fill, or the image reads as horizontally squashed.
                const float height = 320f;
                float width = height * eyeCamera.aspect;
                var rect = new Rect(10f, nextY, width, height);
                GUI.DrawTexture(rect, eyeTargetTexture, ScaleMode.StretchToFill, false);
                GUI.Label(new Rect(rect.x, rect.yMax + 2f, width, 20f), "Vision eye view (first-person)");
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (playerController == null)
                playerController = GetComponent<PlayerController>();

            Transform origin = playerController != null ? playerController.VisionOrigin : null;
            if (origin == null)
                return;

            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(origin.position, 0.15f);
            Gizmos.DrawRay(origin.position, origin.forward * 3f);
        }

        private readonly List<Vector3> debugSampleBuffer = new List<Vector3>(16);

        // Unity calls OnDrawGizmos on every instance regardless of the component's
        // enabled state, but only the owner's aimValid ever gets set (RefreshAimState
        // runs in LateUpdate, which non-owner instances never tick since they disable
        // themselves in OnNetworkSpawn) — so this naturally only draws for the local player.
        private void OnDrawGizmos()
        {
            if (!debugShowVisibilitySamplePoints || !Application.isPlaying || !aimValid)
                return;

            VisionConfig config = Config;
            IReadOnlyList<VisibleEntity> entities = VisibleEntity.All;

            for (int i = 0; i < entities.Count; i++)
            {
                VisibleEntity entity = entities[i];
                if (entity == null || entity.AlwaysVisible)
                    continue;

                float height = entity.HeightOverride > 0f ? entity.HeightOverride : config.EnemyHeight;
                float radius = entity.RadiusOverride > 0f ? entity.RadiusOverride : config.EnemyRadius;

                // Must match IsEnemyVisibleAt's basis exactly (position-only, not aimTarget)
                // or this visualization would lie about what the real query does.
                Vector3 capsuleCenter = entity.FeetPosition + Vector3.up * (height * 0.5f);
                Vector3 toTarget = capsuleCenter - aimOrigin;
                VisionEvaluator.CollectCapsuleSamplePoints(entity.FeetPosition, radius, height, toTarget, debugSampleBuffer);

                for (int p = 0; p < debugSampleBuffer.Count; p++)
                {
                    Vector3 point = debugSampleBuffer[p];
                    bool inCone = VisionEvaluator.IsInCone(aimOrigin, aimTarget, point, config.HorizontalViewAngle * 0.5f);
                    bool visible = inCone && VisionEvaluator.HasLineOfSight(aimOrigin, point, config.LosMask, config.LosSkinWidth);

                    if (visible)
                    {
                        Gizmos.color = Color.green;
                        Gizmos.DrawSphere(point, debugSamplePointRadius);
                        Gizmos.DrawLine(aimOrigin, point);
                        continue;
                    }

                    Gizmos.color = !inCone ? new Color(1f, 0.5f, 0f) : Color.red;
                    Gizmos.DrawSphere(point, debugSamplePointRadius);

                    // Blocked-by-geometry (not out-of-cone) points get a line to the
                    // actual hit, so you can see exactly which collider is in the way.
                    if (inCone)
                    {
                        Vector3 delta = point - aimOrigin;
                        float distance = delta.magnitude;
                        if (distance > config.LosSkinWidth &&
                            Physics.Raycast(aimOrigin, delta / distance, out RaycastHit hit, distance - config.LosSkinWidth, config.LosMask, QueryTriggerInteraction.Ignore))
                        {
                            Gizmos.color = Color.yellow;
                            Gizmos.DrawLine(aimOrigin, hit.point);
                            Gizmos.DrawSphere(hit.point, debugSamplePointRadius * 1.5f);
                        }
                    }
                }
            }
        }
    }
}
