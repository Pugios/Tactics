using System.Collections.Generic;
using UnityEngine;

namespace Tactics.Vision
{
    /// <summary>
    /// Builds the visible-area footprint mesh: 0° fan, sparse ±elevation ledge fans, then mesh merge.
    /// </summary>
    public class VisibleAreaBuilder
    {

        private const float AzimuthDuplicateTolerance = 0.15f;
        private struct RaySample
        {
            public float Azimuth;
            public float Elevation;
            public Vector3 Point;
            public float Distance;
        }

        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<int> indices = new List<int>();
        private readonly List<Vector3> worldPolygonVertices = new List<Vector3>();
        private readonly List<RaySample> fan0Boundary = new List<RaySample>();
        private readonly List<RaySample> upLedgeWinners = new List<RaySample>();
        private readonly List<RaySample> downLedgeWinners = new List<RaySample>();
        private readonly List<RaySample> ledgeWinners = new List<RaySample>();
        private readonly List<RaySample> ledgeRing = new List<RaySample>();
        private readonly List<RaySample> meshCandidates = new List<RaySample>();
        private readonly List<RaySample> fanScratch = new List<RaySample>();
        private readonly List<RaySample> edgeScratch = new List<RaySample>();

        public IReadOnlyList<Vector3> WorldPolygonVertices => worldPolygonVertices;

        public struct DebugMarchCast
        {
            public Vector3 From;
            public Vector3 To;
            public bool Missed;
        }
        private enum RayDebugSource { 
            Fan, 
            Refine, 
            Default 
        }

        public readonly List<DebugMarchCast> DebugMarchCasts = new List<DebugMarchCast>();
        public readonly List<Vector3> DebugVisionGroundHits = new List<Vector3>();
        public readonly List<Vector3> DebugSolidIntercepts = new List<Vector3>();
        public readonly List<(Vector3 origin, Vector3 end)> DebugBoundaryMissRays = new List<(Vector3, Vector3)>();

        public readonly List<(Vector3 origin, Vector3 end)> DebugFanRays = new List<(Vector3, Vector3)>();
        public readonly List<(Vector3 origin, Vector3 end)> DebugRefineRays = new List<(Vector3, Vector3)>();
        public readonly List<(Vector3 origin, Vector3 end)> DebugDefaultRays = new List<(Vector3, Vector3)>();
        public readonly List<(Vector3 origin, Vector3 end)> DebugGroundCheckRays = new List<(Vector3, Vector3)>();

        public void Build(Vector3 aimOrigin, Vector3 aimTarget, in VisionConfig config, Mesh mesh, Transform localSpace)
        {
            Cleanup(mesh);

            if (mesh == null || localSpace == null)
                return;

            // Normalized forward direction (if aimTarget is too short, default to Vector3.forward)
            Vector3 fanCenterTarget = aimTarget.sqrMagnitude < 0.0001f
                ? Vector3.forward
                : aimTarget.normalized;

            CastFan(
                aimOrigin,
                fanCenterTarget,
                0f,
                config.BoundaryRayCount,
                config,
                fanScratch);

            if (fanScratch.Count < 2)
            {
                mesh.Clear();
                return;
            }

            HorizontalEdgeRefinement(
                fanScratch,
                aimOrigin,
                fanCenterTarget,
                config,
                config.MaxHorizontalEdgeRefinements,
                fan0Boundary);
            
            CastFan(
                aimOrigin,
                fanCenterTarget,
                config.VerticalViewAngle * 0.5f,
                config.SparseLedgeRayCount,
                config,
                fanScratch);

            List<RaySample> upRaw = new List<RaySample>(fanScratch);

            CastFan(
                aimOrigin,
                fanCenterTarget,
                -config.VerticalViewAngle * 0.5f,
                config.SparseLedgeRayCount,
                config,
                fanScratch);

            List<RaySample> downRaw = new List<RaySample>(fanScratch);

            VerticalEdgeRefinement(fan0Boundary, upRaw, aimOrigin, fanCenterTarget, config, upLedgeWinners);
            VerticalEdgeRefinement(fan0Boundary, downRaw, aimOrigin, fanCenterTarget, config, downLedgeWinners);

            MergeLedgeWinnersByAzimuth(upLedgeWinners, downLedgeWinners, ledgeWinners);

            HorizontalEdgeRefinement(
                ledgeWinners,
                aimOrigin,
                fanCenterTarget,
                config,
                config.MaxHorizontalEdgeRefinements,
                ledgeRing);
            
            meshCandidates.AddRange(fan0Boundary);
            meshCandidates.AddRange(ledgeRing.Count > 0 ? ledgeRing : ledgeWinners);

            BuildMesh(meshCandidates, aimOrigin, mesh, localSpace, config);
        }

        private void Cleanup(Mesh mesh)
        {
            vertices.Clear();
            indices.Clear();
            worldPolygonVertices.Clear();
            fan0Boundary.Clear();
            upLedgeWinners.Clear();
            downLedgeWinners.Clear();
            ledgeWinners.Clear();
            ledgeRing.Clear();
            meshCandidates.Clear();
            fanScratch.Clear();
            edgeScratch.Clear();

            DebugMarchCasts.Clear();
            DebugVisionGroundHits.Clear();
            DebugSolidIntercepts.Clear();
            DebugBoundaryMissRays.Clear();

            DebugFanRays.Clear();
            DebugRefineRays.Clear();
            DebugDefaultRays.Clear();
            DebugGroundCheckRays.Clear();

            mesh?.Clear();
        }

        private void CastFan(Vector3 aimOrigin, Vector3 fanCenterTarget, float elevationDegrees, int rayCount, in VisionConfig config, List<RaySample> fan)
        {
            fan.Clear();
            rayCount = Mathf.Max(2, rayCount);
            float halfAngle = config.HorizontalViewAngle * 0.5f;

            for (int i = 0; i < rayCount; i++)
            {
                float t = rayCount > 1 ? (float)i / (rayCount - 1) : 0f;
                float azimuth = Mathf.Lerp(-halfAngle, halfAngle, t);
                fan.Add(CastRay(aimOrigin, fanCenterTarget, elevationDegrees, azimuth, config, RayDebugSource.Fan));
            }
        }

        private RaySample CastRay(Vector3 origin, Vector3 target, float elevationDegrees, float azimuth, in VisionConfig config, RayDebugSource debugSource = RayDebugSource.Default)
        {
            Vector3 direction = GetRayDirection(azimuth, elevationDegrees, target);

            if (TryMarchRay(origin, direction, config, debugSource, out Vector3 hitPoint))
            {
                Vector3 point = hitPoint - direction * config.MeshOffset;
                return new RaySample
                {
                    Azimuth = azimuth,
                    Elevation = elevationDegrees,
                    Point = point,
                    Distance = HorizontalDistance(origin, point),
                };
            }

            return new RaySample
            {
                Azimuth = azimuth,
                Elevation = elevationDegrees,
                Point = origin + direction * config.MaxViewDistance,
                Distance = config.MaxViewDistance,
            };
        }

        private static Vector3 GetRayDirection(float azimuth, float elevationDegrees, Vector3 fanCenterTarget)
        {
            Vector3 up = Vector3.up;
            Vector3 yawedForward = (Quaternion.AngleAxis(azimuth, up) * fanCenterTarget).normalized;

            if (Mathf.Abs(elevationDegrees) < 0.001f)
                return yawedForward;

            Vector3 pitchAxis = Vector3.Cross(up, yawedForward);
            if (pitchAxis.sqrMagnitude < 0.001f)
                pitchAxis = Vector3.Cross(up, fanCenterTarget);

            pitchAxis.Normalize();
            return (Quaternion.AngleAxis(-elevationDegrees, pitchAxis) * yawedForward).normalized;
        }

        private static float HorizontalDistance(Vector3 origin, Vector3 point)
        {
            return Vector2.Distance(
                new Vector2(origin.x, origin.z),
                new Vector2(point.x, point.z));
        }

        private bool TryMarchRay(Vector3 origin, Vector3 direction, in VisionConfig config, RayDebugSource debugSource, out Vector3 hitPoint)
        {
            hitPoint = Vector3.zero;

            Vector3 dir = direction.normalized;

            Vector3 lastVisionGround = Vector3.zero;
            int visionGroundPasses = 0;

            Vector3 lastSolid = Vector3.zero;
            bool hasSolid = false;
            int solidLayer = -1;

            float epsilon = config.LosSkinWidth;

            // March the Ray continue on VisionGround hit, stop on anything Solid
            float alongRay = 0f;
            while (alongRay < config.MaxViewDistance)
            {
                float remaining = config.MaxViewDistance - alongRay;
                Vector3 castOrigin = origin + dir * alongRay;

                if (!Physics.Raycast(
                        castOrigin,
                        dir,
                        out RaycastHit hit,
                        remaining,
                        VisionLayerMasks.DefaultBoundaryRay,
                        QueryTriggerInteraction.Ignore))
                {
                    //RecordMarchCast(castOrigin, castOrigin + dir * remaining, missed: true);
                    break;
                }

                //RecordMarchCast(castOrigin, hit.point, missed: false);

                if (hit.collider.gameObject.layer == VisionLayerMasks.VisionGround)
                {
                    lastVisionGround = hit.point;
                    visionGroundPasses++;
                    DebugVisionGroundHits.Add(hit.point);

                    alongRay += hit.distance + epsilon;
                    if (visionGroundPasses >= config.MaxVisionGroundPasses)
                    {
                        Debug.LogWarning("Maximum VisionGround passes reached in boundary ray casting.");
                        break;
                    }

                    continue;
                }
                else
                {
                    lastSolid = hit.point;
                    hasSolid = true;
                    solidLayer = hit.collider.gameObject.layer;
                    DebugSolidIntercepts.Add(hit.point);
                    break;
                }
            }

            if (visionGroundPasses == 0 && !hasSolid)
            {
                DebugBoundaryMissRays.Add((origin, origin + dir * config.MaxViewDistance));
                return false;
            }

            if (hasSolid)
            {
                // Only terrain ground hits above eye level clip at the vision-ground proxy.
                // Walls and props always block at the solid intercept.
                if (solidLayer == VisionLayerMasks.Ground
                    && visionGroundPasses > 0
                    && TryGetGroundHeightBelow(lastSolid - dir * config.MeshOffset, config.MaxViewDistance, out float groundY)
                    && groundY >= origin.y)
                {
                    hitPoint = lastVisionGround;
                }
                else
                {
                    hitPoint = lastSolid;
                }

                RecordBoundaryResult(origin, hitPoint, debugSource);
                return true;
            }

            hitPoint = lastVisionGround;
            RecordBoundaryResult(origin, hitPoint, debugSource);
            return true;
        }

        private bool TryGetGroundHeightBelow(Vector3 hitPoint, float maxDistance, out float groundY)
        {
            if (Physics.Raycast(
                    hitPoint,
                    Vector3.down,
                    out RaycastHit hit,
                    maxDistance,
                    VisionLayerMasks.GroundOnly,
                    QueryTriggerInteraction.Ignore))
            {
                groundY = hit.point.y;
                DebugGroundCheckRays.Add((hitPoint, hit.point));
                return true;
            }

            groundY = 0f;
            return false;
        }

        private void HorizontalEdgeRefinement(IReadOnlyList<RaySample> samples, Vector3 aimOrigin, Vector3 aimTarget, in VisionConfig config, int maxRefinementSteps, List<RaySample> output)
        {
            output.Clear();
            if (samples.Count == 0)
                return;

            for (int i = 0; i < samples.Count; i++)
            {
                output.Add(samples[i]);

                if (i >= samples.Count - 1)
                    continue;

                if (!ShouldRefineEdge(samples[i], samples[i + 1], config))
                    continue;

                edgeScratch.Clear();
                RefineEdge(
                    samples[i],
                    samples[i + 1],
                    aimOrigin,
                    aimTarget,
                    config,
                    depth: 0,
                    maxRefinementSteps,
                    edgeScratch);
                output.AddRange(edgeScratch);
            }
        }

        private void VerticalEdgeRefinement(IReadOnlyList<RaySample> fan0Boundary, IReadOnlyList<RaySample> sparseFan, Vector3 aimOrigin, Vector3 aimTarget, in VisionConfig config, List<RaySample> output)
        {
            for (int i = 0; i < sparseFan.Count; i++)
            {
                RaySample elevated = sparseFan[i];
                RaySample baseSample = GetClosestSampleAtAzimuth(elevated.Azimuth, fan0Boundary);

                if (!ShouldRefineEdge(baseSample, elevated, config))
                    continue;

                edgeScratch.Clear();
                RefineEdge(
                    baseSample,
                    elevated,
                    aimOrigin,
                    aimTarget,
                    config,
                    depth: 0,
                    config.MaxVerticalEdgeRefinements,
                    edgeScratch);

                output.Add(PickFarthestSample(baseSample, elevated, edgeScratch));
            }
        }

        private static RaySample GetClosestSampleAtAzimuth(float azimuth, IReadOnlyList<RaySample> samples)
        {
            RaySample best = samples[0];
            float bestDelta = Mathf.Abs(best.Azimuth - azimuth);

            for (int i = 1; i < samples.Count; i++)
            {
                float delta = Mathf.Abs(samples[i].Azimuth - azimuth);
                if (delta < bestDelta)
                {
                    bestDelta = delta;
                    best = samples[i];
                }
            }

            return best;
        }

        private static bool ShouldRefineEdge(RaySample left, RaySample right, in VisionConfig config)
        {
            float lengthDiff = Mathf.Abs(left.Distance - right.Distance);
            float rayCellWidth = GetRayCellWidth(left, right);

            if (lengthDiff >= rayCellWidth * config.EdgeRefineSensitivity)
                return true;

            return rayCellWidth >= config.HiddenGeometryThreshold;
        }

        private static float GetRayCellWidth(RaySample left, RaySample right)
        {
            // Get expected distance of points at that distance and angle
            float azimuthDelta = Mathf.Abs(right.Azimuth - left.Azimuth) * Mathf.Deg2Rad;
            float elevationDelta = Mathf.Abs(right.Elevation - left.Elevation) * Mathf.Deg2Rad;

            float avgDistance = (left.Distance + right.Distance) * 0.5f;

            return avgDistance * Mathf.Max(azimuthDelta,elevationDelta);
        }

        private void RefineEdge(RaySample left, RaySample right, Vector3 aimOrigin, Vector3 aimTarget, in VisionConfig config, int depth, int maxDepth, List<RaySample> into)
        {
            if (depth >= maxDepth)
                return;

            RaySample mid = CastRay(
                aimOrigin,
                aimTarget,
                (left.Elevation + right.Elevation) * 0.5f,
                (left.Azimuth + right.Azimuth) * 0.5f,
                config,
                RayDebugSource.Refine);

            float tolerance = GetRayCellWidth(left, right) * config.EdgeRefineSensitivity;

            float toLeft = Mathf.Abs(mid.Distance - left.Distance);
            float toRight = Mathf.Abs(mid.Distance - right.Distance);
            bool closeToLeft = toLeft <= tolerance;
            bool closeToRight = toRight <= tolerance;

            if (closeToLeft && closeToRight)
            {
                into.Add(left.Distance >= right.Distance ? left : right);
                return;
            }

            if (closeToLeft && !closeToRight)
            {
                into.Add(mid);
                RefineEdge(mid, right, aimOrigin, aimTarget, config, depth + 1, maxDepth, into);
                return;
            }

            if (closeToRight && !closeToLeft)
            {
                RefineEdge(left, mid, aimOrigin, aimTarget, config, depth + 1, maxDepth, into);
                into.Add(mid);
                return;
            }

            if (toLeft > toRight)
            {
                RefineEdge(left, mid, aimOrigin, aimTarget, config, depth + 1, maxDepth, into);
                into.Add(mid);
            }
            else
            {
                into.Add(mid);
                RefineEdge(mid, right, aimOrigin, aimTarget, config, depth + 1, maxDepth, into);
            }
        }

        private static RaySample PickFarthestSample(RaySample a, RaySample b, List<RaySample> extras)
        {
            RaySample best = a.Distance >= b.Distance ? a : b;
            for (int i = 0; i < extras.Count; i++)
            {
                if (extras[i].Distance > best.Distance)
                    best = extras[i];
            }

            return best;
        }

        private static void MergeLedgeWinnersByAzimuth(List<RaySample> upLedgeWinners, List<RaySample> downLedgeWinners, List<RaySample> ledgeWinners)
        {
            ledgeWinners.Clear();

            int total = upLedgeWinners.Count + downLedgeWinners.Count;

            if (total <= 0)
                return;

            var combined = new List<RaySample>(total);
            combined.AddRange(upLedgeWinners);
            combined.AddRange(downLedgeWinners);

            CollapseDuplicateAzimuths(combined, ledgeWinners);
        }

        private static void CollapseDuplicateAzimuths(List<RaySample> samples, List<RaySample> output)
        {
            output.Clear();
            if (samples.Count <= 0)
                return;
            samples.Sort((a, b) => a.Azimuth.CompareTo(b.Azimuth));
            RaySample best = samples[0];

            for (int i = 1; i < samples.Count; i++)
            {
                RaySample sample = samples[i];
                if (Mathf.Abs(sample.Azimuth - best.Azimuth) <= AzimuthDuplicateTolerance)
                {
                    if (sample.Distance > best.Distance)
                        best = sample;
                    continue;
                }
                else
                {
                    output.Add(best);
                    best = sample;
                }
            }
            output.Add(best);
        }

        private void BuildMesh(List<RaySample> candidates, Vector3 aimOrigin, Mesh mesh, Transform localSpace, in VisionConfig config)
        {
            List<RaySample> boundary = new List<RaySample>();
            CollapseDuplicateAzimuths(candidates, boundary);


            if (boundary.Count < 2)
            {
                mesh.Clear();
                return;
            }

            worldPolygonVertices.Add(aimOrigin);
            for (int i = 0; i < boundary.Count; i++)
                worldPolygonVertices.Add(boundary[i].Point);

            vertices.Add(localSpace.InverseTransformPoint(aimOrigin));
            for (int i = 0; i < boundary.Count; i++)
                vertices.Add(localSpace.InverseTransformPoint(boundary[i].Point));

            for (int i = 0; i < boundary.Count - 1; i++)
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
        
        private void RecordMarchCast(Vector3 from, Vector3 to, bool missed)
        {
            DebugMarchCasts.Add(new DebugMarchCast
            {
                From = from,
                To = to,
                Missed = missed,
            });
        }

        private void RecordBoundaryResult(Vector3 rayOrigin, Vector3 endpoint, RayDebugSource source)
        {
            if (source == RayDebugSource.Fan)
                DebugFanRays.Add((rayOrigin, endpoint));
            else if (source == RayDebugSource.Refine)
                DebugRefineRays.Add((rayOrigin, endpoint));
            else
                DebugDefaultRays.Add((rayOrigin, endpoint));
        }
    }
}
