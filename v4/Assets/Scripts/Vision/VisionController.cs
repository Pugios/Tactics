using Tactics.Player;
using UnityEngine;
using UnityEngine.Serialization;
using static UnityEditorInternal.ReorderableList;

namespace Tactics.Vision
{
    /// <summary>
    /// Reads aim from PlayerController each frame, builds the visible-area mesh, and exposes visibility queries.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    [RequireComponent(typeof(EntityVisibilityDriver))]
    [DefaultExecutionOrder(100)]
    public class VisionController : MonoBehaviour
    {
        [Header("Cone")]
        [SerializeField, Range(1f, 180f)] private float horizontalViewAngle = 103f;
        [SerializeField, Range(1f, 180f)] private float verticalViewAngle = 70.53f;
        [SerializeField] private float maxViewDistance = 500f;

        [Header("Heights")]
        [SerializeField] private float eyeHeight = 1.5f;
        [SerializeField] private float enemyHeight = 2f;
        [SerializeField] private float enemyRadius = 0.5f;

        [Header("Line Of Sight")]
        [SerializeField] private LayerMask losMask = VisionLayerMasks.DefaultLos;
        [SerializeField] private float losSkinWidth = 0.05f;

        [Header("Sampling")]
        [SerializeField] private float meshOffset = 0.05f;

        [Header("Fog")]
        [SerializeField, Range(0f, 1f)] private float fogStrength = 0.9f;

        [Header("Footprint")]
        [SerializeField] private MeshFilter footprintMeshFilter;
        [SerializeField] private Material footprintDrawMaterial;

        [SerializeField, Range(2, 64)] private int boundaryRayCount = 10;
        [SerializeField, Range(0, 10)] private int maxHorizontalEdgeRefinements = 5;
        [SerializeField, Range(0, 4)] private int maxVerticalEdgeRefinements = 1;
        [SerializeField, Range(1, 32)] private int maxVisionGroundPasses = 10;
        [SerializeField, Range(3, 7)] private int sparseLedgeRayCount = 3;

        [SerializeField] private float edgeLengthDifferenceThreshold = 5f;
        [SerializeField] private float edgeLengthThresholdReferenceDistance = 10f;
        [SerializeField] private float edgeLengthDifferenceThresholdMin = 0.25f;
        [SerializeField] private float longSightRefineDistance = 20f;
        [SerializeField] private float edgeMatchTolerance = 5f;
        [SerializeField] private float verticalEdgeLengthDifferenceThreshold = 4f;
        [SerializeField] private float minLedgeMergeDistance = 2.5f;

        [Header("Debug")]
        [SerializeField] private bool drawDebugAim;
        [SerializeField] private bool drawDebugFootprint = true;
        [SerializeField] private bool drawDebugBoundaryResult;
        [SerializeField] private bool drawDebugRayMarch;
        [SerializeField] private bool debugShowMaskTexture;

        private PlayerController playerController;
        private Vector3 aimOrigin;
        private Vector3 aimTarget;
        private bool aimValid;
        private readonly VisibleAreaBuilder visibleAreaBuilder = new VisibleAreaBuilder();
        private Mesh footprintMesh;
        private MeshRenderer footprintRenderer;

        public static VisionController Active { get; private set; }

        public Vector3 AimOrigin => aimOrigin;
        public Vector3 AimTarget => aimTarget;
        public Vector3 FootprintMaskOrigin => aimValid ? transform.position : Vector3.zero;
        public float MaxViewDistance => maxViewDistance;
        public float FogStrength => fogStrength;
        public Mesh FootprintMesh => footprintMesh;
        public bool HasValidAim => aimValid;
        public bool DebugShowMaskTexture => debugShowMaskTexture;

        public VisionConfig Config => new VisionConfig(
            horizontalViewAngle,
            verticalViewAngle,
            maxViewDistance,
            eyeHeight,
            enemyHeight,
            enemyRadius,
            losMask,
            losSkinWidth,
            meshOffset,
            edgeLengthDifferenceThreshold,
            edgeLengthThresholdReferenceDistance,
            edgeLengthDifferenceThresholdMin,
            longSightRefineDistance,
            edgeMatchTolerance,
            maxHorizontalEdgeRefinements,
            maxVerticalEdgeRefinements,
            boundaryRayCount,
            sparseLedgeRayCount,
            verticalEdgeLengthDifferenceThreshold,
            minLedgeMergeDistance,
            maxVisionGroundPasses);

        private void Awake()
        {
            playerController = GetComponent<PlayerController>();
            Active = this;

            if (footprintMeshFilter != null)
            {
                footprintMesh = footprintMeshFilter.sharedMesh;
                if (footprintMesh == null)
                {
                    footprintMesh = new Mesh { name = "Vision Footprint" };
                    footprintMeshFilter.sharedMesh = footprintMesh;
                }

                ConfigureFootprintRenderer(footprintMeshFilter);
            }
        }

        private void OnDestroy()
        {
            VisionFootprintPolygonPublisher.Clear();

            if (Active == this)
                Active = null;
        }

        private void OnValidate()
        {
            horizontalViewAngle = Mathf.Clamp(horizontalViewAngle, 1f, 180f);
            verticalViewAngle = Mathf.Clamp(verticalViewAngle, 1f, 180f);
            maxViewDistance = Mathf.Max(1f, maxViewDistance);
            enemyHeight = Mathf.Max(enemyRadius * 2f, enemyHeight);
            enemyRadius = Mathf.Max(0.01f, enemyRadius);
            meshOffset = Mathf.Max(0f, meshOffset);
            edgeLengthDifferenceThreshold = Mathf.Max(0f, edgeLengthDifferenceThreshold);
            edgeLengthThresholdReferenceDistance = Mathf.Max(0.1f, edgeLengthThresholdReferenceDistance);
            edgeLengthDifferenceThresholdMin = Mathf.Max(0f, edgeLengthDifferenceThresholdMin);
            longSightRefineDistance = Mathf.Max(0f, longSightRefineDistance);
            edgeMatchTolerance = Mathf.Max(0f, edgeMatchTolerance);
            maxHorizontalEdgeRefinements = Mathf.Max(0, maxHorizontalEdgeRefinements);
            maxVerticalEdgeRefinements = Mathf.Max(0, maxVerticalEdgeRefinements);
            boundaryRayCount = Mathf.Clamp(boundaryRayCount, 2, 64);
            sparseLedgeRayCount = Mathf.Clamp(sparseLedgeRayCount, 3, 7);
            verticalEdgeLengthDifferenceThreshold = Mathf.Max(0f, verticalEdgeLengthDifferenceThreshold);
            minLedgeMergeDistance = Mathf.Max(0f, minLedgeMergeDistance);
            maxVisionGroundPasses = Mathf.Clamp(maxVisionGroundPasses, 1, 32);
        }

        private void LateUpdate()
        {
            RefreshAimState();
            RebuildFootprint();
            PublishFootprintPolygon();
            DrawAimDebugLines();
            DrawBoundaryResultDebugLines();
            DrawRayMarchDebugLines();
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

        private void RebuildFootprint()
        {
            if (footprintMesh == null || footprintMeshFilter == null || !aimValid)
                return;

            visibleAreaBuilder.Build(
                aimOrigin,
                aimTarget,
                Config,
                footprintMesh,
                footprintMeshFilter.transform);
        }

        private void PublishFootprintPolygon()
        {
            if (!aimValid || visibleAreaBuilder.WorldPolygonVertices.Count < 3)
            {
                VisionFootprintPolygonPublisher.Clear();
                return;
            }

            VisionFootprintPolygonPublisher.Publish(visibleAreaBuilder.WorldPolygonVertices);
        }

        private void ConfigureFootprintRenderer(MeshFilter meshFilter)
        {
            if (meshFilter == null)
                return;

            GameObject footprintObject = meshFilter.gameObject;
            footprintObject.layer = VisionLayerMasks.VisionCone;

            MeshRenderer renderer = footprintObject.GetComponent<MeshRenderer>();
            if (renderer == null)
                return;

            footprintRenderer = renderer;

            if (footprintDrawMaterial != null)
                renderer.sharedMaterial = footprintDrawMaterial;

            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.enabled = false;
        }

        private void DrawBoundaryResultDebugLines()
        {
            if (!drawDebugBoundaryResult || !Application.isPlaying)
                return;

            Color missColor = new Color(1f, 0.35f, 0.35f, 0.35f);

            for (int i = 0; i < visibleAreaBuilder.DebugBoundaryMissRays.Count; i++)
            {
                (Vector3 start, Vector3 end) = visibleAreaBuilder.DebugBoundaryMissRays[i];
                Debug.DrawLine(start, end, missColor);
            }
        }

        private void DrawRayMarchDebugLines()
        {
            if (!drawDebugRayMarch || !Application.isPlaying)
                return;

            Color marchHitColor = new Color(0.2f, 0.85f, 1f, 0.95f);
            Color marchMissColor = new Color(0.2f, 0.85f, 1f, 0.35f);

            Color pathFanColor = Color.darkRed;
            Color pathRefineColor = Color.darkBlue;
            Color pathDefaultColor = Color.grey;
            Color pathGroundCheckColor = Color.brown;

            Color visionGroundColor = new Color(1f, 0.55f, 0.1f, 0.95f);
            Color solidColor = new Color(1f, 0.25f, 0.25f, 0.95f);
            Color endpointColor = new Color(0.2f, 1f, 0.35f, 0.95f);

            for (int i = 0; i < visibleAreaBuilder.DebugMarchCasts.Count; i++)
            {
                VisibleAreaBuilder.DebugMarchCast cast = visibleAreaBuilder.DebugMarchCasts[i];
                Debug.DrawLine(cast.From, cast.To, cast.Missed ? marchMissColor : marchHitColor);
            }

            for (int i = 0; i < visibleAreaBuilder.DebugFanRays.Count; i++)
            {
                (Vector3 start, Vector3 end) = visibleAreaBuilder.DebugFanRays[i];
                Debug.DrawLine(start, end, pathFanColor);
            }
            for (int i = 0; i < visibleAreaBuilder.DebugRefineRays.Count; i++)
            {
                (Vector3 start, Vector3 end) = visibleAreaBuilder.DebugRefineRays[i];
                Debug.DrawLine(start, end, pathRefineColor);
            }
            for (int i = 0; i < visibleAreaBuilder.DebugDefaultRays.Count; i++)
            {
                (Vector3 start, Vector3 end) = visibleAreaBuilder.DebugDefaultRays[i];
                Debug.DrawLine(start, end, pathDefaultColor);
            }
            for (int i = 0; i < visibleAreaBuilder.DebugGroundCheckRays.Count; i++)
            {
                (Vector3 start, Vector3 end) = visibleAreaBuilder.DebugGroundCheckRays[i];
                Debug.DrawLine(start, end, pathGroundCheckColor);
            }

            for (int i = 0; i < visibleAreaBuilder.DebugVisionGroundHits.Count; i++)
                DebugDrawWireSphere(visibleAreaBuilder.DebugVisionGroundHits[i], 0.18f, visionGroundColor);

            for (int i = 0; i < visibleAreaBuilder.DebugSolidIntercepts.Count; i++)
                DebugDrawWireSphere(visibleAreaBuilder.DebugSolidIntercepts[i], 0.12f, solidColor);


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
            Vector3 prevX = center + Vector3.right * radius;

            for (int i = 1; i <= segments; i++)
            {
                float angle = step * i * Mathf.Deg2Rad;
                Vector3 point = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                Debug.DrawLine(prevX, point, color);
                prevX = point;
            }

            prevX = center + Vector3.up * radius;
            for (int i = 1; i <= segments; i++)
            {
                float angle = step * i * Mathf.Deg2Rad;
                Vector3 point = center + new Vector3(0f, Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                Debug.DrawLine(prevX, point, color);
                prevX = point;
            }
        }

        private void DrawRayMarchGizmos()
        {
            Color marchHitColor = new Color(0.2f, 0.85f, 1f, 0.95f);
            Color marchMissColor = new Color(0.2f, 0.85f, 1f, 0.35f);
            Color visionGroundColor = new Color(1f, 0.55f, 0.1f, 0.95f);
            Color solidColor = new Color(1f, 0.25f, 0.25f, 0.95f);
            Color endpointColor = new Color(0.2f, 1f, 0.35f, 0.95f);

            for (int i = 0; i < visibleAreaBuilder.DebugMarchCasts.Count; i++)
            {
                VisibleAreaBuilder.DebugMarchCast cast = visibleAreaBuilder.DebugMarchCasts[i];
                Gizmos.color = cast.Missed ? marchMissColor : marchHitColor;
                Gizmos.DrawLine(cast.From, cast.To);
            }

            Gizmos.color = visionGroundColor;
            for (int i = 0; i < visibleAreaBuilder.DebugVisionGroundHits.Count; i++)
                Gizmos.DrawSphere(visibleAreaBuilder.DebugVisionGroundHits[i], 0.18f);

            Gizmos.color = solidColor;
            for (int i = 0; i < visibleAreaBuilder.DebugSolidIntercepts.Count; i++)
                Gizmos.DrawSphere(visibleAreaBuilder.DebugSolidIntercepts[i], 0.12f);

        }

        private void DrawBoundaryResultGizmos()
        {
            Gizmos.color = new Color(1f, 0.35f, 0.35f, 0.35f);
            for (int i = 0; i < visibleAreaBuilder.DebugBoundaryMissRays.Count; i++)
            {
                (Vector3 start, Vector3 end) = visibleAreaBuilder.DebugBoundaryMissRays[i];
                Gizmos.DrawLine(start, end);
            }
        }

        private void DrawFootprintGizmos(Transform meshTransform, Vector3[] verts)
        {
            Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.85f);

            for (int submesh = 0; submesh < footprintMesh.subMeshCount; submesh++)
            {
                MeshTopology topology = footprintMesh.GetTopology(submesh);
                int[] indices = footprintMesh.GetIndices(submesh);

                if (topology == MeshTopology.Triangles)
                {
                    for (int i = 0; i < indices.Length; i += 3)
                    {
                        Vector3 a = meshTransform.TransformPoint(verts[indices[i]]);
                        Vector3 b = meshTransform.TransformPoint(verts[indices[i + 1]]);
                        Vector3 c = meshTransform.TransformPoint(verts[indices[i + 2]]);
                        Gizmos.DrawLine(a, b);
                        Gizmos.DrawLine(b, c);
                        Gizmos.DrawLine(c, a);
                    }
                }
                else if (topology == MeshTopology.Lines)
                {
                    for (int i = 0; i < indices.Length; i += 2)
                    {
                        Vector3 a = meshTransform.TransformPoint(verts[indices[i]]);
                        Vector3 b = meshTransform.TransformPoint(verts[indices[i + 1]]);
                        Gizmos.DrawLine(a, b);
                    }
                }
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

            if (drawDebugAim && playerController != null)
            {
                Vector3 groundPoint = playerController.AimGroundPoint;
                Vector3 targetPoint = playerController.LookTarget;

                Gizmos.color = Color.magenta;
                Gizmos.DrawLine(origin.position, targetPoint);
                Gizmos.DrawWireSphere(targetPoint, 0.15f);

                Gizmos.color = Color.cyan;
                Gizmos.DrawLine(groundPoint, targetPoint);
                Gizmos.DrawWireSphere(groundPoint, 0.2f);
            }

            if (drawDebugRayMarch)
                DrawRayMarchGizmos();

            if (drawDebugBoundaryResult)
                DrawBoundaryResultGizmos();

            if (footprintMesh == null || footprintMeshFilter == null)
                return;

            if (drawDebugFootprint)
            {
                Transform meshTransform = footprintMeshFilter.transform;
                DrawFootprintGizmos(meshTransform, footprintMesh.vertices);
            }

#if UNITY_EDITOR
            if (footprintMesh != null && drawDebugFootprint)
            {
                Gizmos.color = Color.white;
                Gizmos.DrawWireSphere(AimOrigin, 0.05f);
                UnityEditor.Handles.Label(
                    AimOrigin + Vector3.up * 0.35f,
                    $"Footprint verts: {footprintMesh.vertexCount}, submeshes: {footprintMesh.subMeshCount}");
            }
#endif
        }
    }
}
