using UnityEngine;
using System.Collections.Generic;

namespace ValorantTrainer.Vision
{
    public class VisionSystem : MonoBehaviour
    {
        [Header("FOV Settings")]
        [SerializeField] private float maxViewDistance = 500f;
        [SerializeField, Range(0, 180)] private float viewAngle = 70f; // Opening angle of the 3D cone
        [SerializeField] private LayerMask obstacleMask = (1 << 6) | (1 << 7) | (1 << 0) | (1 << 9) | (1 << 10); // Layers Ground, Wall, Default, Dynamic, Prop
        [SerializeField] private LayerMask entityMask = (1 << 9); // Dynamic layer

        [Header("Near Vision (fills the gap between feet and cone)")]
        [SerializeField] private float nearVisionRadius = 5f;
        [SerializeField] private float nearVisionRayHeight = 0.3f; // Height above ground for the occlusion raycasts

        [Header("Mesh Generation")]
        [SerializeField] private int horizontalResolution = 40;
        [SerializeField] private int verticalResolution = 10;
        [SerializeField] private MeshFilter viewMeshFilter;
        [SerializeField] private Transform visionOrigin;

        [Header("Edge Refinement")]
        [Tooltip("Binary search steps used to localize occlusion edges between neighboring slices. 0 disables refinement.")]
        [SerializeField, Range(0, 8)] private int edgeResolveIterations = 5;
        [Tooltip("Hit distance difference (meters) between neighboring rays that counts as an occlusion edge.")]
        [SerializeField] private float edgeDistanceThreshold = 0.75f;

        [Header("Edge Sharpness")]
        [Tooltip("Degrees inside the horizontal FOV limit over which the mask edge transitions from sharp (at the limit) to fully blurred (interior). Keeps the cone's two side edges crisp while the base is diffused.")]
        [SerializeField] private float sharpEdgeFalloff = 8f;

        private Mesh viewMesh;
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Vector2> uvs = new List<Vector2>();
        private readonly List<int> triangles = new List<int>();
        private readonly List<VisibleEntity> seenEntities = new List<VisibleEntity>();
        private readonly List<Slice> slices = new List<Slice>();

        /// <summary>
        /// One vertical slice of the cone: all phi samples cast at a single theta.
        /// </summary>
        private struct Slice
        {
            public float theta;
            public SliceSample[] samples; // index 0 = innermost phi ring
        }

        private struct SliceSample
        {
            public bool hit;
            public float distance;
            public Vector3 worldPoint;
            public float softness; // 0 = on a sharp apex side edge, 1 = cone interior
        }

        private void Start()
        {
            if (viewMeshFilter != null)
            {
                viewMesh = new Mesh();
                viewMesh.name = "3D View Mesh";
                viewMeshFilter.mesh = viewMesh;
            }

            if (visionOrigin == null) visionOrigin = transform;
        }

        private void LateUpdate()
        {
            if (visionOrigin == null) return;

            DrawFieldOfView();
            FindVisibleEntities3D();
        }

        private void OnDrawGizmos()
        {
            if (visionOrigin == null) return;

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(visionOrigin.position, 0.2f);
        }

        private void DrawFieldOfView()
        {
            if (viewMesh == null) return;

            vertices.Clear();
            uvs.Clear();
            triangles.Clear();

            BuildViewCone();
            BuildNearVisionFan();

            viewMesh.Clear();
            viewMesh.SetVertices(vertices);
            viewMesh.SetUVs(0, uvs);
            viewMesh.SetTriangles(triangles, 0);
            viewMesh.RecalculateBounds();
        }

        /// <summary>
        /// 3D cone from the eye, built as vertical slices swept around the view axis.
        /// Rays that hit nothing collapse to the apex (no visible area). Where two
        /// neighboring slices disagree (an occlusion edge), extra slices are inserted
        /// via binary search so the mesh hugs real wall edges instead of forming
        /// wide stretched wedges across them.
        /// </summary>
        private void BuildViewCone()
        {
            // 1. Cast the base slices
            slices.Clear();
            float step = 360f / horizontalResolution;
            for (int s = 0; s < horizontalResolution; s++)
            {
                slices.Add(CastSlice(step * s));
            }

            // 2. Insert refined slices at occlusion edges (compare each pair, including wrap-around)
            if (edgeResolveIterations > 0)
            {
                for (int i = slices.Count - 1; i >= 0; i--)
                {
                    Slice a = slices[i];
                    Slice b = slices[(i + 1) % slices.Count];

                    if (SlicesDiffer(a, b))
                    {
                        FindEdgeSlices(a, b, out Slice edgeA, out Slice edgeB);
                        slices.Insert(i + 1, edgeB);
                        slices.Insert(i + 1, edgeA);
                    }
                }
            }

            // 3. Triangulate: apex + quad strips between consecutive slices
            int apexIdx = vertices.Count;
            vertices.Add(Vector3.zero); // Apex at local (0,0,0)
            uvs.Add(Vector2.zero);      // Apex is always sharp

            int sliceCount = slices.Count;
            int firstSliceVertex = vertices.Count;

            for (int i = 0; i < sliceCount; i++)
            {
                SliceSample[] samples = slices[i].samples;
                for (int r = 0; r < verticalResolution; r++)
                {
                    vertices.Add(visionOrigin.InverseTransformPoint(samples[r].worldPoint));
                    uvs.Add(new Vector2(samples[r].softness, 0f));
                }
            }

            for (int i = 0; i < sliceCount; i++)
            {
                int baseA = firstSliceVertex + i * verticalResolution;
                int baseB = firstSliceVertex + ((i + 1) % sliceCount) * verticalResolution;

                triangles.Add(apexIdx);
                triangles.Add(baseB);
                triangles.Add(baseA);

                for (int r = 0; r < verticalResolution - 1; r++)
                {
                    triangles.Add(baseA + r);
                    triangles.Add(baseB + r);
                    triangles.Add(baseB + r + 1);

                    triangles.Add(baseA + r);
                    triangles.Add(baseB + r + 1);
                    triangles.Add(baseA + r + 1);
                }
            }
        }

        /// <summary>
        /// Casts all phi rays of one vertical slice at the given theta.
        /// Misses collapse to the apex position.
        /// </summary>
        private Slice CastSlice(float theta)
        {
            var slice = new Slice
            {
                theta = theta,
                samples = new SliceSample[verticalResolution],
            };

            float halfAngle = viewAngle / 2f;

            for (int r = 1; r <= verticalResolution; r++)
            {
                float phi = (halfAngle / verticalResolution) * r;
                Vector3 dir = DirFromSpherical(phi, theta);
                Vector3 worldDir = visionOrigin.TransformDirection(dir);
                float softness = ComputeEdgeSoftness(worldDir, halfAngle);

                // Slight offset so the ray doesn't start inside the player's own collider
                if (Physics.Raycast(visionOrigin.position + worldDir * 0.01f, worldDir, out RaycastHit hit, maxViewDistance - 0.01f, obstacleMask))
                {
                    slice.samples[r - 1] = new SliceSample
                    {
                        hit = true,
                        distance = hit.distance,
                        worldPoint = hit.point,
                        softness = softness,
                    };
                }
                else
                {
                    // Missed everything (sky/void): contribute no visible area.
                    slice.samples[r - 1] = new SliceSample
                    {
                        hit = false,
                        distance = 0f,
                        worldPoint = visionOrigin.position,
                        softness = softness,
                    };
                }
            }

            return slice;
        }

        /// <summary>
        /// 0 when the ray lies on the horizontal FOV limit (the cone's sharp side
        /// edges meeting the apex), ramping to 1 over sharpEdgeFalloff degrees
        /// toward the cone interior. Drives the selective blur in the fog shader.
        /// </summary>
        private float ComputeEdgeSoftness(Vector3 worldDir, float halfAngle)
        {
            Vector3 flatForward = visionOrigin.forward;
            flatForward.y = 0f;
            if (flatForward.sqrMagnitude < 0.001f) flatForward = transform.forward;

            Vector3 flatDir = worldDir;
            flatDir.y = 0f;
            if (flatDir.sqrMagnitude < 0.0001f) return 1f; // Near-vertical ray: interior

            float horizontalAngle = Vector3.Angle(flatForward, flatDir);
            return Mathf.Clamp01((halfAngle - horizontalAngle) / Mathf.Max(0.01f, sharpEdgeFalloff));
        }

        /// <summary>
        /// True if the two slices straddle an occlusion edge: any phi sample flips
        /// between hit and miss, or its hit distance jumps beyond the threshold.
        /// </summary>
        private bool SlicesDiffer(Slice a, Slice b)
        {
            for (int r = 0; r < verticalResolution; r++)
            {
                if (a.samples[r].hit != b.samples[r].hit)
                    return true;

                if (a.samples[r].hit && Mathf.Abs(a.samples[r].distance - b.samples[r].distance) > edgeDistanceThreshold)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Binary search in theta between two differing slices. Returns the two
        /// closest slices on either side of the occlusion edge.
        /// </summary>
        private void FindEdgeSlices(Slice a, Slice b, out Slice edgeA, out Slice edgeB)
        {
            edgeA = a;
            edgeB = b;

            for (int i = 0; i < edgeResolveIterations; i++)
            {
                // Lerp handles the wrap-around pair (e.g. 351 -> 0) via the original step
                float midTheta = edgeA.theta + Mathf.DeltaAngle(edgeA.theta, edgeB.theta) * 0.5f;
                Slice mid = CastSlice(midTheta);

                if (SlicesDiffer(a, mid))
                    edgeB = mid;
                else
                    edgeA = mid;
            }
        }

        /// <summary>
        /// Short flat wedge on the ground in front of the player's feet.
        /// Fills the visual gap between the player and where the elevated 3D cone
        /// first reaches the ground. Same horizontal angle as the cone, blocked by
        /// walls/props, and never extends behind the player.
        /// </summary>
        private void BuildNearVisionFan()
        {
            if (nearVisionRadius <= 0f) return;

            // Find the ground directly below the eye to anchor the fan
            if (!Physics.Raycast(visionOrigin.position, Vector3.down, out RaycastHit groundHit, 5f, obstacleMask))
                return; // Mid-air (e.g. over a pit): no near vision this frame

            Vector3 fanCenter = groundHit.point + Vector3.up * 0.05f;
            Vector3 rayOrigin = groundHit.point + Vector3.up * nearVisionRayHeight;

            // Horizontal facing: flatten the look direction; fall back to body forward
            // when looking straight up/down.
            Vector3 flatForward = visionOrigin.forward;
            flatForward.y = 0f;
            if (flatForward.sqrMagnitude < 0.001f) flatForward = transform.forward;
            flatForward.Normalize();

            int centerIdx = vertices.Count;
            vertices.Add(visionOrigin.InverseTransformPoint(fanCenter));
            uvs.Add(Vector2.zero); // The wedge tip at the player stays sharp

            float halfAngle = viewAngle / 2f;
            int segments = Mathf.Max(8, horizontalResolution / 2);

            for (int i = 0; i <= segments; i++)
            {
                float angle = Mathf.Lerp(-halfAngle, halfAngle, (float)i / segments);
                Vector3 dir = Quaternion.Euler(0f, angle, 0f) * flatForward;

                Vector3 worldPoint;
                if (Physics.Raycast(rayOrigin, dir, out RaycastHit hit, nearVisionRadius, obstacleMask))
                {
                    worldPoint = fanCenter + dir * Mathf.Max(0f, hit.distance - 0.02f);
                }
                else
                {
                    worldPoint = fanCenter + dir * nearVisionRadius;
                }

                vertices.Add(visionOrigin.InverseTransformPoint(worldPoint));
                float softness = Mathf.Clamp01((halfAngle - Mathf.Abs(angle)) / Mathf.Max(0.01f, sharpEdgeFalloff));
                uvs.Add(new Vector2(softness, 0f));

                if (i > 0)
                {
                    int currentIdx = vertices.Count - 1;
                    triangles.Add(centerIdx);
                    triangles.Add(currentIdx);
                    triangles.Add(currentIdx - 1);
                }
            }
        }

        private Vector3 DirFromSpherical(float phi, float theta)
        {
            float phiRad = phi * Mathf.Deg2Rad;
            float thetaRad = theta * Mathf.Deg2Rad;

            // Cone pointing along local Z
            float x = Mathf.Sin(phiRad) * Mathf.Cos(thetaRad);
            float y = Mathf.Sin(phiRad) * Mathf.Sin(thetaRad);
            float z = Mathf.Cos(phiRad);

            return new Vector3(x, y, z);
        }

        private void FindVisibleEntities3D()
        {
            foreach (var entity in seenEntities)
            {
                if (entity != null) entity.SetVisible(false);
            }
            seenEntities.Clear();

            Collider[] entitiesInRadius = Physics.OverlapSphere(visionOrigin.position, maxViewDistance, entityMask);

            float halfAngle = viewAngle / 2f;

            for (int i = 0; i < entitiesInRadius.Length; i++)
            {
                Transform entityTransform = entitiesInRadius[i].transform;
                // Aim at eye level of the target for the LOS test
                Vector3 targetPos = entityTransform.position + Vector3.up * 1.5f;
                Vector3 dirToEntity = (targetPos - visionOrigin.position).normalized;

                bool inCone = Vector3.Angle(visionOrigin.forward, dirToEntity) < halfAngle;
                bool inNearVision = IsInNearVision(entityTransform.position);

                if (!inCone && !inNearVision) continue;

                float dstToEntity = Vector3.Distance(visionOrigin.position, targetPos);

                // Slightly shortened so the ray doesn't hit the entity itself
                if (!Physics.Raycast(visionOrigin.position, dirToEntity, dstToEntity - 0.05f, obstacleMask))
                {
                    VisibleEntity visibleEntity = entityTransform.GetComponent<VisibleEntity>();
                    if (visibleEntity != null)
                    {
                        visibleEntity.SetVisible(true);
                        seenEntities.Add(visibleEntity);
                    }
                }
            }
        }

        /// <summary>
        /// Matches the near-vision fan: within its radius, in front of the player horizontally.
        /// </summary>
        private bool IsInNearVision(Vector3 worldPosition)
        {
            if (nearVisionRadius <= 0f) return false;

            Vector3 toTarget = worldPosition - visionOrigin.position;
            toTarget.y = 0f;
            if (toTarget.magnitude > nearVisionRadius) return false;

            Vector3 flatForward = visionOrigin.forward;
            flatForward.y = 0f;
            if (flatForward.sqrMagnitude < 0.001f) flatForward = transform.forward;

            return Vector3.Angle(flatForward, toTarget) < viewAngle / 2f;
        }
    }
}
