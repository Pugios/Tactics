using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Tactics.Vision
{
    /// <summary>
    /// Renders the footprint mesh (VisionCone layer) into a world-aligned mask RenderTexture via a hidden GPU camera.
    /// </summary>
    public class VisionFootprintMaskTarget
    {
        private const int DefaultMaskResolution = 512;
        private const float CameraLift = 500f;

        private RenderTexture maskTexture;
        private UnityEngine.Camera maskCamera;
        private GameObject maskCameraObject;
        private int maskResolution = DefaultMaskResolution;
        private float maskHalfExtent = 64f;

        public RenderTexture MaskTexture => maskTexture;
        public float MaskHalfExtent => maskHalfExtent;
        public bool HasValidMask { get; private set; }

        public void Configure(Transform ownerTransform, Material drawMaterial, int resolution, float worldHalfExtent)
        {
            maskResolution = Mathf.Max(64, resolution);
            maskHalfExtent = Mathf.Max(8f, worldHalfExtent);
            EnsureResources();
            EnsureCamera();
            maskCamera.orthographicSize = maskHalfExtent;
        }

        public void Release()
        {
            if (maskTexture != null)
            {
                maskTexture.Release();
                Object.Destroy(maskTexture);
                maskTexture = null;
            }

            if (maskCameraObject != null)
            {
                Object.Destroy(maskCameraObject);
                maskCameraObject = null;
                maskCamera = null;
            }

            HasValidMask = false;
            PublishGlobals(null, Vector3.zero, 0f);
        }

        public void Render(Mesh mesh, Matrix4x4 localToWorld, Vector3 maskOrigin)
        {
            EnsureResources();
            EnsureCamera();

            if (mesh == null || mesh.vertexCount == 0)
            {
                ClearMask();
                return;
            }

            maskCamera.orthographicSize = maskHalfExtent;
            maskCamera.targetTexture = maskTexture;
            maskCamera.transform.SetPositionAndRotation(
                maskOrigin + Vector3.up * CameraLift,
                Quaternion.LookRotation(Vector3.down, Vector3.forward));
            maskCamera.Render();

            HasValidMask = true;
            PublishGlobals(maskTexture, maskOrigin, maskHalfExtent);
        }

        public void ClearMask()
        {
            EnsureResources();
            EnsureCamera();

            maskCamera.targetTexture = maskTexture;
            maskCamera.Render();

            HasValidMask = false;
            PublishGlobals(maskTexture, Vector3.zero, maskHalfExtent);
            Shader.SetGlobalFloat(VisionShaderGlobals.HasValidMask, 0f);
        }

        private void EnsureResources()
        {
            if (maskTexture != null && maskTexture.width == maskResolution)
                return;

            if (maskTexture != null)
            {
                maskTexture.Release();
                Object.Destroy(maskTexture);
            }

            maskTexture = new RenderTexture(maskResolution, maskResolution, 16, RenderTextureFormat.ARGB32)
            {
                name = "VisionFootprintMask",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                autoGenerateMips = false,
            };
            maskTexture.Create();
        }

        private void EnsureCamera()
        {
            if (maskCamera != null)
                return;

            maskCameraObject = new GameObject("VisionFootprintMaskCamera")
            {
                hideFlags = HideFlags.HideAndDontSave,
            };

            maskCamera = maskCameraObject.AddComponent<UnityEngine.Camera>();
            maskCamera.enabled = false;
            maskCamera.orthographic = true;
            maskCamera.aspect = 1f;
            maskCamera.orthographicSize = maskHalfExtent;
            maskCamera.nearClipPlane = 0.01f;
            maskCamera.farClipPlane = CameraLift + 100f;
            maskCamera.clearFlags = CameraClearFlags.SolidColor;
            maskCamera.backgroundColor = Color.black;
            maskCamera.cullingMask = 1 << VisionLayerMasks.VisionCone;
            maskCamera.depth = -100;
            maskCamera.useOcclusionCulling = false;

            UniversalAdditionalCameraData urpData = maskCameraObject.AddComponent<UniversalAdditionalCameraData>();
            urpData.renderType = CameraRenderType.Base;
        }

        private static void PublishGlobals(RenderTexture mask, Vector3 maskOrigin, float halfExtent)
        {
            Shader.SetGlobalTexture(VisionShaderGlobals.FootprintMask, mask != null ? mask : Texture2D.blackTexture);
            Shader.SetGlobalVector(VisionShaderGlobals.MaskOrigin, maskOrigin);
            Shader.SetGlobalFloat(VisionShaderGlobals.MaskHalfExtent, halfExtent);
            Shader.SetGlobalFloat(VisionShaderGlobals.HasValidMask, mask != null ? 1f : 0f);
        }
    }
}
