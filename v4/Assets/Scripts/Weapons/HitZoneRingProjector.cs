using Tactics.Combat;
using Tactics.Vision;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Tactics.Weapons
{
    /// <summary>
    /// Projects three filled hit-zone rings (head/body/leg, radii from
    /// <see cref="HitZoneRadii"/>) onto the ground under this entity so players
    /// can see where to click to land each zone. Uses a deep downward
    /// <see cref="DecalProjector"/>: since the damage model is pure XZ distance
    /// with no height check, rings landing on the floor far below a jumping or
    /// overhanging entity are an accurate picture of where a shot connects.
    /// The projector is created at runtime (same convention as the vision eye
    /// camera) and registered with <see cref="VisibleEntity"/> so enemy rings
    /// hide with fog of war and can't leak positions.
    /// </summary>
    [DisallowMultipleComponent]
    public class HitZoneRingProjector : NetworkBehaviour
    {
        [SerializeField] private Material ringMaterial;
        [SerializeField] private bool showOnLocalPlayer = false;
        [SerializeField] private Color headColor = new Color(1f, 0.25f, 0.2f, 0.55f);
        [SerializeField] private Color bodyColor = new Color(1f, 0.6f, 0.1f, 0.45f);
        [SerializeField] private Color legColor = new Color(1f, 1f, 1f, 0.3f);
        [SerializeField] private float projectionDepth = 20f; // covers jump apex and long falls
        [SerializeField] private float aboveFeetMargin = 0.5f;

        // The leg ring plus a margin so the outermost edge isn't clipped by the
        // projector bounds or its anti-aliased falloff.
        private const float FootprintHalfExtent = HitZoneRadii.Leg * 1.1f;
        private const int TextureSize = 512;

        // One texture + material shared by every entity's projector.
        private static Texture2D sharedRingTexture;
        private static Material sharedRingMaterial;

        private GameObject ringObject;
        private VisibleEntity visibleEntity;

        public override void OnNetworkSpawn()
        {
            if (!IsClient) return; // headless server draws nothing
            if (IsLocalPlayer && !showOnLocalPlayer) return; // dummies are never the local player

            if (ringMaterial == null)
            {
                Debug.LogWarning($"[HitZoneRingProjector] No ring material assigned on {name}; rings disabled.");
                return;
            }

            visibleEntity = GetComponent<VisibleEntity>();

            ringObject = new GameObject("HitZoneRings");
            ringObject.transform.SetParent(transform, worldPositionStays: false);

            DecalProjector projector = ringObject.AddComponent<DecalProjector>();
            projector.material = GetSharedMaterial();
            projector.size = new Vector3(FootprintHalfExtent * 2f, FootprintHalfExtent * 2f, projectionDepth);
            // Volume spans aboveFeetMargin above the feet to the rest of the
            // depth below, so rings land on ground at any airborne height.
            projector.pivot = new Vector3(0f, 0f, projectionDepth * 0.5f - aboveFeetMargin);
            // Cull steep/vertical surfaces (wall sides). Prop tops are on the
            // Ground layer and genuinely clickable, so they keep their rings.
            projector.startAngleFade = 35f;
            projector.endAngleFade = 55f;
            projector.scaleMode = DecalScaleMode.ScaleInvariant;
            projector.drawDistance = 1000f;

            PinToFeet();

            // Register the projector with the fog-of-war visibility toggling and
            // inherit the entity's current hidden/visible state immediately.
            if (visibleEntity != null)
                visibleEntity.RefreshRenderers();
        }

        public override void OnNetworkDespawn()
        {
            if (ringObject != null)
                Destroy(ringObject);
        }

        private void LateUpdate()
        {
            if (ringObject == null) return;
            PinToFeet();
        }

        private void PinToFeet()
        {
            // World-locked straight down from the feet, ignoring body yaw.
            Vector3 feet = visibleEntity != null ? visibleEntity.FeetPosition : transform.position;
            ringObject.transform.SetPositionAndRotation(feet, Quaternion.Euler(90f, 0f, 0f));
        }

        private Material GetSharedMaterial()
        {
            if (sharedRingMaterial != null) return sharedRingMaterial;

            if (sharedRingTexture == null)
                sharedRingTexture = GenerateRingTexture();

            sharedRingMaterial = new Material(ringMaterial);
            sharedRingMaterial.SetTexture("Base_Map", sharedRingTexture);
            return sharedRingMaterial;
        }

        private Texture2D GenerateRingTexture()
        {
            var texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, mipChain: true)
            {
                name = "HitZoneRings",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave
            };

            float halfSize = TextureSize * 0.5f;
            float metersPerPixel = FootprintHalfExtent / halfSize;
            float antiAlias = 1.5f * metersPerPixel;
            var transparent = new Color(0f, 0f, 0f, 0f);
            var pixels = new Color[TextureSize * TextureSize];

            for (int y = 0; y < TextureSize; y++)
            {
                for (int x = 0; x < TextureSize; x++)
                {
                    float dx = (x + 0.5f - halfSize) * metersPerPixel;
                    float dy = (y + 0.5f - halfSize) * metersPerPixel;
                    float worldR = Mathf.Sqrt(dx * dx + dy * dy);

                    // Stack the filled discs largest-first so each inner zone
                    // paints over the one below, with an anti-aliased edge.
                    Color color = transparent;
                    color = Color.Lerp(color, legColor, DiscCoverage(worldR, HitZoneRadii.Leg, antiAlias));
                    color = Color.Lerp(color, bodyColor, DiscCoverage(worldR, HitZoneRadii.Body, antiAlias));
                    color = Color.Lerp(color, headColor, DiscCoverage(worldR, HitZoneRadii.Head, antiAlias));
                    pixels[y * TextureSize + x] = color;
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(updateMipmaps: true, makeNoLongerReadable: true);
            return texture;
        }

        private static float DiscCoverage(float worldR, float radius, float antiAlias)
        {
            return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(radius - antiAlias, radius + antiAlias, worldR));
        }
    }
}
