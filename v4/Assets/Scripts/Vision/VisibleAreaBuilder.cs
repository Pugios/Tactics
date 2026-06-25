using System.Collections.Generic;
using UnityEngine;

namespace Tactics.Vision
{
    /// <summary>
    /// Builds the visible-area footprint mesh from evenly spaced boundary rays fanning around
    /// aimForward (pitch from VisionOrigin aim) with adaptive edge refinement at corners.
    /// </summary>
    public class VisibleAreaBuilder
    {
        private struct RaySample
        {
            public float Azimuth;
            public Vector3 Point;
            public float Distance;
        }

        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<int> indices = new List<int>();
        private readonly List<Vector3> worldPolygonVertices = new List<Vector3>();
        private readonly List<RaySample> raySamples = new List<RaySample>();
        private readonly List<RaySample> refinedBoundary = new List<RaySample>();
        private readonly List<RaySample> edgeRefinementScratch = new List<RaySample>();

        public IReadOnlyList<Vector3> WorldPolygonVertices => worldPolygonVertices;

#if UNITY_EDITOR
        public readonly List<Vector3> DebugAcceptedPoints = new List<Vector3>();
        public readonly List<(Vector3 origin, Vector3 end)> DebugHitRays = new List<(Vector3, Vector3)>();
        public readonly List<(Vector3 origin, Vector3 end)> DebugMissRays = new List<(Vector3, Vector3)>();
#endif

        public struct DebugMarchCast
        {
            public Vector3 From;
            public Vector3 To;
            public bool Missed;
        }

        public readonly List<DebugMarchCast> DebugMarchCasts = new List<DebugMarchCast>();
        public readonly List<Vector3> DebugVisionGroundHits = new List<Vector3>();
        public readonly List<Vector3> DebugSolidIntercepts = new List<Vector3>();
        public readonly List<(Vector3 origin, Vector3 end)> DebugBoundaryResultRays = new List<(Vector3, Vector3)>();
        public readonly List<(Vector3 origin, Vector3 end)> DebugBoundaryMissRays = new List<(Vector3, Vector3)>();
        public readonly List<Vector3> DebugDistinctEndpoints = new List<Vector3>();

        public void Build(Vector3 aimOrigin, Vector3 aimForward, in VisionConfig config, Mesh mesh, Transform localSpace)
        {
            vertices.Clear();
            indices.Clear();
            worldPolygonVertices.Clear();
#if UNITY_EDITOR
            DebugAcceptedPoints.Clear();
            DebugHitRays.Clear();
            DebugMissRays.Clear();
#endif
            DebugMarchCasts.Clear();
            DebugVisionGroundHits.Clear();
            DebugSolidIntercepts.Clear();
            DebugBoundaryResultRays.Clear();
            DebugBoundaryMissRays.Clear();
            DebugDistinctEndpoints.Clear();

            if (mesh == null || localSpace == null)
            {
                mesh?.Clear();
                return;
            }

            // Normalized forward direction (if aimforward is too small, default to Vector3.forward)
            Vector3 fanCenterForward = aimForward.sqrMagnitude < 0.0001f
                ? Vector3.forward
                : aimForward.normalized;

            // Cast initial boundary rays evenly spaced across the horizontal half-angle of the vision cone.
            float halfAngle = config.HorizontalHalfAngle;

            int boundaryRayCount = Mathf.Max(2, config.BoundaryRayCount);

            raySamples.Clear();
            for (int i = 0; i < boundaryRayCount; i++)
            {
                float t = boundaryRayCount > 1 ? (float)i / (boundaryRayCount - 1) : 0f;
                float azimuth = Mathf.Lerp(-halfAngle, halfAngle, t);
                raySamples.Add(CastSample(aimOrigin, azimuth, fanCenterForward, config));
            }

            if (raySamples.Count < 2)
            {
                mesh.Clear();
                return;
            }

            // Refine the boundary by adaptively adding samples between existing ones where the distance difference exceeds a threshold.
            BuildRefinedBoundary(raySamples, aimOrigin, fanCenterForward, config);

            if (refinedBoundary.Count < 2)
            {
                mesh.Clear();
                return;
            }

            // Build the mesh vertices and indices from the refined boundary.
            worldPolygonVertices.Add(aimOrigin);
            for (int i = 0; i < refinedBoundary.Count; i++)
                worldPolygonVertices.Add(refinedBoundary[i].Point);

            vertices.Add(localSpace.InverseTransformPoint(aimOrigin));
            for (int i = 0; i < refinedBoundary.Count; i++)
                vertices.Add(localSpace.InverseTransformPoint(refinedBoundary[i].Point));

            for (int i = 0; i < refinedBoundary.Count - 1; i++)
            {
                indices.Add(0);
                indices.Add(i + 1);
                indices.Add(i + 2);
            }

            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetTriangles(indices, 0);
            mesh.RecalculateBounds();
        }

        private void BuildRefinedBoundary(List<RaySample> initialSamples, Vector3 aimOrigin, Vector3 fanCenterForward, in VisionConfig config)
        {
            refinedBoundary.Clear();

            for (int i = 0; i < initialSamples.Count; i++)
            {
                refinedBoundary.Add(initialSamples[i]);

                if (i >= initialSamples.Count - 1)
                    continue;

                RaySample left = initialSamples[i];
                RaySample right = initialSamples[i + 1];
                if (!ShouldRefineEdge(left, right, config))
                    continue;

                edgeRefinementScratch.Clear();
                RefineBetween(
                    left,
                    right,
                    aimOrigin,
                    fanCenterForward,
                    config,
                    depth: 0,
                    edgeRefinementScratch);

                refinedBoundary.AddRange(edgeRefinementScratch);
            }
        }

        private void RefineBetween(RaySample left, RaySample right, Vector3 aimOrigin, Vector3 fanCenterForward, in VisionConfig config, int depth, List<RaySample> into)
        {
            if (depth >= config.MaxEdgeRefinements)
                return;

            RaySample mid = CastSample(
                aimOrigin,
                (left.Azimuth + right.Azimuth) * 0.5f,
                fanCenterForward,
                config);

            float tolerance = GetEffectiveMatchTolerance(left, right, config);
            float toLeft = Mathf.Abs(mid.Distance - left.Distance);
            float toRight = Mathf.Abs(mid.Distance - right.Distance);
            bool closeToLeft = toLeft <= tolerance;
            bool closeToRight = toRight <= tolerance;

            if (closeToLeft && closeToRight)
            {
                into.Add(mid);
                return;
            }

            if (closeToLeft && !closeToRight)
            {
                into.Add(mid);
                RefineBetween(mid, right, aimOrigin, fanCenterForward, config, depth + 1, into);
                return;
            }

            if (closeToRight && !closeToLeft)
            {
                RefineBetween(left, mid, aimOrigin, fanCenterForward, config, depth + 1, into);
                into.Add(mid);
                return;
            }

            if (toLeft > toRight)
            {
                RefineBetween(left, mid, aimOrigin, fanCenterForward, config, depth + 1, into);
                into.Add(mid);
            }
            else
            {
                into.Add(mid);
                RefineBetween(mid, right, aimOrigin, fanCenterForward, config, depth + 1, into);
            }
        }

        private static bool ShouldRefineEdge(RaySample left, RaySample right, in VisionConfig config)
        {
            float avgDistance = (left.Distance + right.Distance) * 0.5f;
            float lengthDiff = Mathf.Abs(left.Distance - right.Distance);
            float effectiveThreshold = GetEffectiveLengthDifferenceThreshold(avgDistance, config);

            if (lengthDiff >= effectiveThreshold)
                return true;

            if (avgDistance >= config.LongSightRefineDistance)
                return true;

            return false;
        }

        private static float GetEffectiveLengthDifferenceThreshold(float avgDistance, in VisionConfig config)
        {
            if (avgDistance <= 0.01f)
                return config.EdgeLengthDifferenceThreshold;

            float scaled = config.EdgeLengthDifferenceThreshold *
                           (config.EdgeLengthThresholdReferenceDistance / avgDistance);

            return Mathf.Clamp(
                scaled,
                config.EdgeLengthDifferenceThresholdMin,
                config.EdgeLengthDifferenceThreshold);
        }

        private static float GetEffectiveMatchTolerance(RaySample left, RaySample right, in VisionConfig config)
        {
            float avgDistance = (left.Distance + right.Distance) * 0.5f;
            if (avgDistance <= 0.01f)
                return config.EdgeMatchTolerance;

            float scaled = config.EdgeMatchTolerance *
                           (config.EdgeLengthThresholdReferenceDistance / avgDistance);

            return Mathf.Clamp(
                scaled,
                config.EdgeLengthDifferenceThresholdMin,
                config.EdgeMatchTolerance);
        }

        private RaySample CastSample(Vector3 aimOrigin, float azimuth, Vector3 fanCenterForward, in VisionConfig config)
        {
            // Rotates fanCenter around Y-Axis (preserve up tilt, while fanning Rays)
            Vector3 direction = (Quaternion.AngleAxis(azimuth, Vector3.up) * fanCenterForward).normalized;

            if (TryCastBoundaryRay(aimOrigin, direction, config, out Vector3 hitPoint))
            {
                // Move back point tiny bit to stop z-fighting on walls
                Vector3 point = hitPoint - direction * config.MeshOffset;
                return new RaySample
                {
                    Azimuth = azimuth,
                    Point = point,
                    Distance = HorizontalDistance(aimOrigin, point),
                };
            }

            return new RaySample
            {
                Azimuth = azimuth,
                Point = aimOrigin + direction * config.MaxViewDistance,
                Distance = config.MaxViewDistance,
            };
        }

        private static float HorizontalDistance(Vector3 origin, Vector3 point)
        {
            return Vector2.Distance(
                new Vector2(origin.x, origin.z),
                new Vector2(point.x, point.z));
        }

        private bool TryCastBoundaryRay(Vector3 origin, Vector3 direction, in VisionConfig config, out Vector3 hitPoint)
        {
            hitPoint = Vector3.zero;
            Vector3 dir = direction.normalized;
            float epsilon = config.LosSkinWidth;
            float alongRay = 0f;
            int visionGroundPasses = 0;

            Vector3 lastVisionGround = Vector3.zero;
            bool hasVisionGround = false;

            while (alongRay < config.MaxViewDistance - epsilon)
            {
                float remaining = config.MaxViewDistance - alongRay;
                Vector3 castOrigin = origin + dir * alongRay;

                // Cast a ray to find the next hit along the boundary ray
                if (!Physics.Raycast(
                        castOrigin,
                        dir,
                        out RaycastHit hit,
                        remaining,
                        VisionLayerMasks.DefaultBoundaryRay,
                        QueryTriggerInteraction.Ignore))
                {
                    // No hit found, record the march cast and exit
                    RecordMarchCast(castOrigin, castOrigin + dir * remaining, missed: true);
                    break;
                }

                // Hit found, record the march cast
                RecordMarchCast(castOrigin, hit.point, missed: false);

                // If hit was on VisionGround, record the hit and continue marching along the ray
                if (hit.collider.gameObject.layer == VisionLayerMasks.VisionGround)
                {
                    lastVisionGround = hit.point;
                    hasVisionGround = true;
                    DebugVisionGroundHits.Add(hit.point);

                    alongRay += hit.distance + epsilon;
                    visionGroundPasses++;
                    if (visionGroundPasses >= config.MaxVisionGroundPasses)
                    {
                        Debug.Log("Warning: Maximum VisionGround passes reached. Possible infinite loop in boundary ray casting.");
                        break;
                    }

                    continue;
                }

                // If hit was on a solid object, choose between the solid hit and the last VisionGround hit
                // based on whether the solid surface is below the eye line at aim origin height.
                hitPoint = hasVisionGround && (hit.point.y - config.EyeHeight) >= origin.y
                    ? lastVisionGround
                    : hit.point;
                if (hasVisionGround)
                    DebugSolidIntercepts.Add(hit.point);

                RecordBoundaryResult(origin, hitPoint, hasVisionGround, lastVisionGround);
#if UNITY_EDITOR
                DebugAcceptedPoints.Add(hitPoint);
                DebugHitRays.Add((origin, hitPoint));
#endif
                return true;
            }

            if (hasVisionGround)
            {
                hitPoint = lastVisionGround;
                RecordBoundaryResult(origin, hitPoint, hasVisionGround: true, lastVisionGround);
#if UNITY_EDITOR
                DebugAcceptedPoints.Add(hitPoint);
                DebugHitRays.Add((origin, hitPoint));
#endif
                return true;
            }

            DebugBoundaryMissRays.Add((origin, origin + dir * config.MaxViewDistance));
#if UNITY_EDITOR
            DebugMissRays.Add((origin, origin + dir * config.MaxViewDistance));
#endif
            return false;
        }

        private void RecordMarchCast(Vector3 from, Vector3 to, bool missed)
        {
            DebugMarchCasts.Add(new DebugMarchCast
            {
                From = from,
                To = to,
                Missed = missed,
            });
        }

        private void RecordBoundaryResult(Vector3 rayOrigin, Vector3 endpoint, bool hasVisionGround, Vector3 lastVisionGround)
        {
            DebugBoundaryResultRays.Add((rayOrigin, endpoint));

            if (!hasVisionGround || (endpoint - lastVisionGround).sqrMagnitude > 0.0001f)
                DebugDistinctEndpoints.Add(endpoint);
        }
    }
}