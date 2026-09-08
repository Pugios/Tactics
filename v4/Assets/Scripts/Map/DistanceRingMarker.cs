using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Tactics.Map
{
    /// <summary>
    /// Test-range measuring tool. Projects concentric distance rings (one every
    /// <see cref="ringSpacing"/> metres out to <see cref="maxRadius"/>) onto the
    /// ground around this transform, with optional "5m / 10m / ..." labels laid
    /// flat along one axis. Pure rendering: a <see cref="DecalProjector"/> plus
    /// legacy <see cref="TextMesh"/> labels on the Default layer with no
    /// colliders, so nothing here can be aimed at, walked on, or block line of
    /// sight. Rings measure XZ distance from this transform, which is exactly how
    /// the hit zones and damage falloff measure shooter-to-target distance.
    ///
    /// Runs in edit mode so the origin can be dragged around without entering
    /// Play Mode. Everything it creates carries <see cref="HideFlags.DontSave"/>
    /// (same convention as the vision eye camera) and never lands in the scene
    /// file; only this component and its settings are saved.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class DistanceRingMarker : MonoBehaviour
    {
        private const string ChildName = "DistanceRings";
        private const int MinResolution = 256;
        private const int MaxResolution = 8192;

        [Header("Rings")]
        [SerializeField, Min(0.5f)] private float ringSpacing = 5f;
        [SerializeField, Min(1f)] private float maxRadius = 80f;
        [Tooltip("Every Nth ring is drawn thicker in the major colour (4 = every 20 m at 5 m spacing). 0 disables.")]
        [SerializeField, Min(0)] private int majorRingEvery = 4;
        [SerializeField, Min(0.02f)] private float ringWidth = 0.15f;
        [SerializeField, Min(0.02f)] private float majorRingWidth = 0.3f;
        [SerializeField] private Color ringColor = new Color(0.5f, 0.5f, 0.5f, 1f);
        [SerializeField] private Color majorRingColor = new Color(1f, 0.85f, 0.2f, 1f);
        [SerializeField] private Color centerColor = new Color(1f, 0.3f, 0.2f, 1f);
        [SerializeField, Min(0.05f)] private float centerDotRadius = 0.25f;

        [Header("Labels")]
        [SerializeField] private bool showLabels = true;
        [Tooltip("XZ direction from the origin along which the distance labels are placed.")]
        [SerializeField] private Vector2 labelDirection = Vector2.down;
        [SerializeField, Min(0.1f)] private float labelHeight = 1f;
        [SerializeField] private Color labelColor = Color.black;
        [Tooltip("Optional. Falls back to Unity's built-in LegacyRuntime font.")]
        [SerializeField] private Font labelFont;

        [Header("Projection")]
        [Tooltip("A URP decal material (e.g. HitZoneRings.mat). Falls back to a fresh Shader Graphs/Decal material.")]
        [SerializeField] private Material decalMaterial;
        [Tooltip("Ring texture size in pixels. 4096 over a 160 m footprint is ~4 cm per pixel.")]
        [SerializeField] private int textureResolution = 4096;
        [Tooltip("How far below the origin the projector reaches, so rings land on sunken floors.")]
        [SerializeField, Min(1f)] private float projectionDepth = 12f;
        [Tooltip("How far above the origin the projector starts, so raised prop tops still get rings.")]
        [SerializeField, Min(0f)] private float aboveOriginMargin = 2f;

        private GameObject ringObject;
        private Texture2D ringTexture;
        private Material ringMaterial;
        private bool rebuildRequested;

        /// <summary>Half the projector footprint: the outer ring plus its own width and anti-alias falloff.</summary>
        private float FootprintHalfExtent => maxRadius + Mathf.Max(ringWidth, majorRingWidth) + 0.5f;

        /// <summary>Ring radii in metres: spacing, 2x spacing, ... up to and including maxRadius.</summary>
        public static List<float> ComputeRingRadii(float spacing, float maxRadius)
        {
            var radii = new List<float>();
            if (spacing <= 0f) return radii;
            // Tolerate float error so 16 x 5 m still includes the 80 m ring.
            int count = Mathf.FloorToInt(maxRadius / spacing + 1e-4f);
            for (int i = 1; i <= count; i++)
                radii.Add(i * spacing);
            return radii;
        }

        private void OnEnable()
        {
            Rebuild();
        }

        private void OnDisable()
        {
            Teardown();
        }

        private void OnValidate()
        {
            textureResolution = Mathf.Clamp(textureResolution, MinResolution, MaxResolution);
            if (labelDirection.sqrMagnitude < 1e-6f) labelDirection = Vector2.right;
            rebuildRequested = true;
#if UNITY_EDITOR
            // Objects can't be destroyed from inside OnValidate; defer to the
            // next editor tick so inspector edits refresh the rings immediately.
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this != null && isActiveAndEnabled && rebuildRequested) Rebuild();
            };
#endif
        }

        private void Update()
        {
            if (rebuildRequested) Rebuild();
        }

        private void LateUpdate()
        {
            if (ringObject == null) return;
            // World-locked: rings stay axis-aligned regardless of this transform's rotation.
            ringObject.transform.SetPositionAndRotation(transform.position, Quaternion.Euler(90f, 0f, 0f));
        }

        private void Rebuild()
        {
            rebuildRequested = false;
            Teardown();

            ringObject = new GameObject(ChildName)
            {
                hideFlags = HideFlags.DontSave | HideFlags.HideInHierarchy
            };
            ringObject.transform.SetParent(transform, worldPositionStays: false);
            ringObject.transform.SetPositionAndRotation(transform.position, Quaternion.Euler(90f, 0f, 0f));

            BuildProjector();
            if (showLabels) BuildLabels();
        }

        private void Teardown()
        {
            // A DontSave child can outlive our reference (domain reload, scene
            // reload), so also sweep by name to avoid stacking duplicates.
            if (ringObject == null)
            {
                Transform stale = transform.Find(ChildName);
                if (stale != null) ringObject = stale.gameObject;
            }

            DestroySafe(ringObject);
            DestroySafe(ringTexture);
            DestroySafe(ringMaterial);
            ringObject = null;
            ringTexture = null;
            ringMaterial = null;
        }

        private static void DestroySafe(Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) Destroy(obj);
            else DestroyImmediate(obj);
        }

        private void BuildProjector()
        {
            Material baseMaterial = decalMaterial;
            if (baseMaterial == null)
            {
                Shader decalShader = FindDecalShader();
                if (decalShader == null)
                {
                    Debug.LogWarning($"[DistanceRingMarker] No decal material assigned on {name} and the URP decal shader wasn't found; rings disabled. Assign a decal material (e.g. HitZoneRings.mat) in the inspector.");
                    return;
                }
                baseMaterial = new Material(decalShader);
            }

            ringTexture = GenerateRingTexture();
            ringMaterial = new Material(baseMaterial) { hideFlags = HideFlags.DontSave };
            ringMaterial.SetTexture("Base_Map", ringTexture);
            if (baseMaterial != decalMaterial) DestroySafe(baseMaterial);

            var projector = ringObject.AddComponent<DecalProjector>();
            projector.material = ringMaterial;
            float size = FootprintHalfExtent * 2f;
            projector.size = new Vector3(size, size, projectionDepth + aboveOriginMargin);
            // Local +Z is world down after the 90 degree X rotation: shift the volume
            // so it spans aboveOriginMargin above the origin and projectionDepth below.
            projector.pivot = new Vector3(0f, 0f, (projectionDepth - aboveOriginMargin) * 0.5f);
            // Cull steep surfaces so wall sides don't get striped; prop tops keep rings.
            projector.startAngleFade = 35f;
            projector.endAngleFade = 55f;
            projector.scaleMode = DecalScaleMode.ScaleInvariant;
            projector.drawDistance = 1000f;
        }

        /// <summary>
        /// Shader.Find only sees shaders already loaded or force-included, which
        /// holds in the main editor (HitZoneRings.mat loads the decal graph) but
        /// not in a fresh Multiplayer Play Mode virtual player. Fall back to
        /// loading the URP shader graph asset directly in the editor. Builds must
        /// assign decalMaterial explicitly.
        /// </summary>
        private static Shader FindDecalShader()
        {
            Shader shader = Shader.Find("Shader Graphs/Decal");
#if UNITY_EDITOR
            if (shader == null)
                shader = UnityEditor.AssetDatabase.LoadAssetAtPath<Shader>(
                    "Packages/com.unity.render-pipelines.universal/Shaders/Decal.shadergraph");
#endif
            return shader;
        }

        private void BuildLabels()
        {
            Font font = labelFont != null ? labelFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null)
            {
                Debug.LogWarning($"[DistanceRingMarker] No label font available on {name}; labels disabled.");
                return;
            }

            Vector2 dir = labelDirection.normalized;
            // Labels sit just outside each ring so they don't cover the line.
            float outward = labelHeight * 0.75f;
            // Rotate about the parent's local Z (world up) so the text's up vector
            // points away from the origin: read upright from the origin looking
            // outward along labelDirection, whatever direction that is.
            float angleDeg = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f;
            Quaternion labelRotation = Quaternion.Euler(0f, 0f, angleDeg);

            foreach (float radius in ComputeRingRadii(ringSpacing, maxRadius))
            {
                var labelObject = new GameObject($"Label {radius:0.#}m") { hideFlags = HideFlags.DontSave };
                labelObject.transform.SetParent(ringObject.transform, worldPositionStays: false);
                // Parent is rotated 90 degrees about X, so local (x, y) maps to world (x, z)
                // and the text lies flat on the ground, readable from the top-down camera.
                Vector2 offset = dir * (radius + outward);
                labelObject.transform.localPosition = new Vector3(offset.x, offset.y, 0f);
                labelObject.transform.localRotation = labelRotation;

                var text = labelObject.AddComponent<TextMesh>();
                text.font = font;
                text.text = $"{radius:0.#}m";
                text.anchor = TextAnchor.MiddleCenter;
                text.alignment = TextAlignment.Center;
                text.fontSize = 100;
                text.characterSize = labelHeight * 0.1f;
                text.color = labelColor;

                var renderer = labelObject.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = font.material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            }
        }

        private Texture2D GenerateRingTexture()
        {
            int size = textureResolution;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: true)
            {
                name = "DistanceRings",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 8,
                hideFlags = HideFlags.DontSave
            };

            int half = size / 2;
            float halfExtent = FootprintHalfExtent;
            float metersPerPixel = halfExtent / half;
            float antiAlias = 1.5f * metersPerPixel;
            float ringHalfWidth = ringWidth * 0.5f;
            float majorHalfWidth = majorRingWidth * 0.5f;
            int ringCount = ComputeRingRadii(ringSpacing, maxRadius).Count;

            var pixels = new Color32[size * size];
            var transparent = new Color(0f, 0f, 0f, 0f);

            // The pattern is radially symmetric, so compute one quadrant and
            // mirror it into the other three.
            for (int y = 0; y < half; y++)
            {
                float dy = (y + 0.5f) * metersPerPixel;
                for (int x = 0; x < half; x++)
                {
                    float dx = (x + 0.5f) * metersPerPixel;
                    float worldR = Mathf.Sqrt(dx * dx + dy * dy);

                    Color color = transparent;

                    // Nearest ring to this pixel; spacing always exceeds ring
                    // width so only that one can cover the pixel.
                    int ringIndex = Mathf.RoundToInt(worldR / ringSpacing);
                    if (ringIndex >= 1 && ringIndex <= ringCount)
                    {
                        bool isMajor = majorRingEvery > 0 && ringIndex % majorRingEvery == 0;
                        float distToRing = Mathf.Abs(worldR - ringIndex * ringSpacing);
                        float halfWidth = isMajor ? majorHalfWidth : ringHalfWidth;
                        float coverage = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(halfWidth - antiAlias, halfWidth + antiAlias, distToRing));
                        color = Color.Lerp(color, isMajor ? majorRingColor : ringColor, coverage);
                    }

                    float dotCoverage = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(centerDotRadius - antiAlias, centerDotRadius + antiAlias, worldR));
                    color = Color.Lerp(color, centerColor, dotCoverage);

                    Color32 c = color;
                    int xr = half + x, xl = half - 1 - x;
                    int yt = half + y, yb = half - 1 - y;
                    pixels[yt * size + xr] = c;
                    pixels[yt * size + xl] = c;
                    pixels[yb * size + xr] = c;
                    pixels[yb * size + xl] = c;
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(updateMipmaps: true, makeNoLongerReadable: true);
            return texture;
        }
    }
}
