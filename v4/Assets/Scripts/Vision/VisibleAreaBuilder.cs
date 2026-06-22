using System.Collections.Generic;
using UnityEngine;

namespace Tactics.Vision
{
    /// <summary>
    /// Builds the visible-area footprint mesh from evenly spaced horizontal boundary rays
    /// with adaptive edge refinement at corners.
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

        public void Build(
            Vector3 aimOrigin,
            Vector3 aimForward,
            in VisionConfig config,
            Mesh mesh,
            Transform localSpace)
        {
            vertices.Clear();
            indices.Clear();
            worldPolygonVertices.Clear();
#if UNITY_EDITOR
            DebugAcceptedPoints.Clear();
            DebugHitRays.Clear();
            DebugMissRays.Clear();
#endif

            if (mesh == null || localSpace == null)
            {
                mesh?.Clear();
                return;
            }

            Vector3 horizontalForward = aimForward;
            horizontalForward.y = 0f;
            if (horizontalForward.sqrMagnitude < 0.0001f)
                horizontalForward = Vector3.forward;
            horizontalForward.Normalize();

            float halfAngle = config.HorizontalHalfAngle;

            int boundaryRayCount = Mathf.Max(2, config.BoundaryRayCount);

            raySamples.Clear();
            for (int i = 0; i < boundaryRayCount; i++)
            {
                float t = boundaryRayCount > 1 ? (float)i / (boundaryRayCount - 1) : 0f;
                float azimuth = Mathf.Lerp(-halfAngle, halfAngle, t);
                raySamples.Add(CastSample(aimOrigin, azimuth, horizontalForward, config));
            }

            if (raySamples.Count < 2)
            {
                mesh.Clear();
                return;
            }

            BuildRefinedBoundary(raySamples, aimOrigin, horizontalForward, config);

            if (refinedBoundary.Count < 2)
            {
                mesh.Clear();
                return;
            }

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

        private void BuildRefinedBoundary(
            List<RaySample> initialSamples,
            Vector3 aimOrigin,
            Vector3 horizontalForward,
            in VisionConfig config)
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
                    horizontalForward,
                    config,
                    depth: 0,
                    edgeRefinementScratch);

                refinedBoundary.AddRange(edgeRefinementScratch);
            }
        }

        /// <summary>
        /// Appends samples between left and right (exclusive), in ascending azimuth order.
        /// </summary>
        private void RefineBetween(
            RaySample left,
            RaySample right,
            Vector3 aimOrigin,
            Vector3 horizontalForward,
            in VisionConfig config,
            int depth,
            List<RaySample> into)
        {
            if (depth >= config.MaxEdgeRefinements)
                return;

            RaySample mid = CastSample(
                aimOrigin,
                (left.Azimuth + right.Azimuth) * 0.5f,
                horizontalForward,
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
                RefineBetween(mid, right, aimOrigin, horizontalForward, config, depth + 1, into);
                return;
            }

            if (closeToRight && !closeToLeft)
            {
                RefineBetween(left, mid, aimOrigin, horizontalForward, config, depth + 1, into);
                into.Add(mid);
                return;
            }

            if (toLeft > toRight)
            {
                RefineBetween(left, mid, aimOrigin, horizontalForward, config, depth + 1, into);
                into.Add(mid);
            }
            else
            {
                into.Add(mid);
                RefineBetween(mid, right, aimOrigin, horizontalForward, config, depth + 1, into);
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

        private static float GetEffectiveMatchTolerance(
            RaySample left,
            RaySample right,
            in VisionConfig config)
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

        private RaySample CastSample(
            Vector3 aimOrigin,
            float azimuth,
            Vector3 horizontalForward,
            in VisionConfig config)
        {
            Vector3 direction = Quaternion.AngleAxis(azimuth, Vector3.up) * horizontalForward;

            if (TryCastHorizontalRay(aimOrigin, direction, config, out Vector3 hitPoint))
            {
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

        private bool TryCastHorizontalRay(
            Vector3 origin,
            Vector3 direction,
            in VisionConfig config,
            out Vector3 hitPoint)
        {
            hitPoint = Vector3.zero;

            if (!Physics.Raycast(
                    origin,
                    direction,
                    out RaycastHit hit,
                    config.MaxViewDistance,
                    config.LosMask,
                    QueryTriggerInteraction.Ignore))
            {
#if UNITY_EDITOR
                DebugMissRays.Add((origin, origin + direction * config.MaxViewDistance));
#endif
                return false;
            }

            hitPoint = hit.point;
#if UNITY_EDITOR
            DebugAcceptedPoints.Add(hit.point);
            DebugHitRays.Add((origin, hit.point));
#endif
            return true;
        }

        /*
        // --- Previous mesh creation (adaptive frustum grid + horizontal strips) ---

        private const int MinBaseAzimuthSamples = 8;
        private const int MinBaseElevationSamples = 3;
        private const int MaxRefinementPasses = 2;
        private const float DistanceRefineThreshold = 2f;
        private const float HeightRefineThreshold = 1f;

        private struct FootprintSample
        {
            public float Azimuth;
            public float Elevation;
            public Vector3 FeetWorld;
            public bool Valid;
        }

        private readonly List<float> azimuthAngles = new List<float>();
        private readonly List<float> elevationAngles = new List<float>();
        private readonly List<List<FootprintSample>> sampleGrid = new List<List<FootprintSample>>();

        private void CollectAdaptiveFrustumSamples(
            Vector3 aimOrigin,
            Vector3 aimForward,
            in VisionConfig config)
        {
            sampleGrid.Clear();
            azimuthAngles.Clear();
            elevationAngles.Clear();

            VisionEvaluator.GetConeBasis(aimForward, out Vector3 right, out Vector3 up);
            float horizontalHalf = config.HorizontalHalfAngle;
            float verticalHalf = config.VerticalHalfAngle;

            int baseAzimuth = Mathf.Max(MinBaseAzimuthSamples, config.AzimuthSamples / 4);
            int baseElevation = Mathf.Max(MinBaseElevationSamples, config.ElevationSamples / 2);
            int maxAzimuth = config.AzimuthSamples + 1;
            int maxElevation = config.ElevationSamples + 1;

            FillUniformAngles(azimuthAngles, baseAzimuth, -horizontalHalf, horizontalHalf);
            FillUniformAngles(elevationAngles, baseElevation, -verticalHalf, verticalHalf);
            RebuildSampleGrid(aimOrigin, aimForward, right, up, config);

            for (int pass = 0; pass < MaxRefinementPasses; pass++)
            {
                bool refined = false;

                for (int a = azimuthAngles.Count - 2; a >= 0; a--)
                {
                    if (azimuthAngles.Count >= maxAzimuth)
                        break;

                    if (!ShouldRefineAzimuthGap(a))
                        continue;

                    float midAzimuth = (azimuthAngles[a] + azimuthAngles[a + 1]) * 0.5f;
                    azimuthAngles.Insert(a + 1, midAzimuth);

                    for (int e = 0; e < sampleGrid.Count; e++)
                    {
                        FootprintSample mid = CastSample(
                            aimOrigin,
                            aimForward,
                            right,
                            up,
                            midAzimuth,
                            elevationAngles[e],
                            config);
                        sampleGrid[e].Insert(a + 1, mid);
                    }

                    refined = true;
                }

                for (int e = elevationAngles.Count - 2; e >= 0; e--)
                {
                    if (elevationAngles.Count >= maxElevation)
                        break;

                    if (!ShouldRefineElevationGap(e))
                        continue;

                    float midElevation = (elevationAngles[e] + elevationAngles[e + 1]) * 0.5f;
                    elevationAngles.Insert(e + 1, midElevation);

                    var newRow = new List<FootprintSample>(azimuthAngles.Count);
                    for (int a = 0; a < azimuthAngles.Count; a++)
                    {
                        newRow.Add(CastSample(
                            aimOrigin,
                            aimForward,
                            right,
                            up,
                            azimuthAngles[a],
                            midElevation,
                            config));
                    }

                    sampleGrid.Insert(e + 1, newRow);
                    refined = true;
                }

                if (!refined)
                    break;
            }
        }

        private static void FillUniformAngles(List<float> angles, int intervalCount, float min, float max)
        {
            angles.Clear();
            if (intervalCount <= 0)
            {
                angles.Add(0f);
                return;
            }

            for (int i = 0; i <= intervalCount; i++)
                angles.Add(Mathf.Lerp(min, max, (float)i / intervalCount));
        }

        private void RebuildSampleGrid(
            Vector3 aimOrigin,
            Vector3 aimForward,
            Vector3 right,
            Vector3 up,
            in VisionConfig config)
        {
            sampleGrid.Clear();

            for (int e = 0; e < elevationAngles.Count; e++)
            {
                var row = new List<FootprintSample>(azimuthAngles.Count);
                float elevation = elevationAngles[e];

                for (int a = 0; a < azimuthAngles.Count; a++)
                {
                    row.Add(CastSample(
                        aimOrigin,
                        aimForward,
                        right,
                        up,
                        azimuthAngles[a],
                        elevation,
                        config));
                }

                sampleGrid.Add(row);
            }
        }

        private bool ShouldRefineAzimuthGap(int azimuthIndex)
        {
            for (int e = 0; e < sampleGrid.Count; e++)
            {
                if (ShouldRefinePair(sampleGrid[e][azimuthIndex], sampleGrid[e][azimuthIndex + 1]))
                    return true;
            }

            return false;
        }

        private bool ShouldRefineElevationGap(int elevationIndex)
        {
            List<FootprintSample> lower = sampleGrid[elevationIndex];
            List<FootprintSample> upper = sampleGrid[elevationIndex + 1];

            for (int a = 0; a < lower.Count; a++)
            {
                if (ShouldRefinePair(lower[a], upper[a]))
                    return true;
            }

            return false;
        }

        private static bool ShouldRefinePair(FootprintSample left, FootprintSample right)
        {
            if (!left.Valid && !right.Valid)
                return false;

            if (left.Valid != right.Valid)
                return true;

            Vector3 delta = right.FeetWorld - left.FeetWorld;
            if (delta.sqrMagnitude > DistanceRefineThreshold * DistanceRefineThreshold)
                return true;

            if (Mathf.Abs(delta.y) > HeightRefineThreshold)
                return true;

            return false;
        }

        private FootprintSample CastSample(
            Vector3 aimOrigin,
            Vector3 aimForward,
            Vector3 right,
            Vector3 up,
            float azimuth,
            float elevation,
            in VisionConfig config)
        {
            Vector3 direction = VisionEvaluator.GetOblongConeDirection(
                aimForward, right, up, azimuth, elevation);

            if (!TryCastConeRay(aimOrigin, direction, config, out Vector3 feet))
            {
                return new FootprintSample
                {
                    Azimuth = azimuth,
                    Elevation = elevation,
                    Valid = false,
                };
            }

            return new FootprintSample
            {
                Azimuth = azimuth,
                Elevation = elevation,
                FeetWorld = feet,
                Valid = true,
            };
        }

        private bool TryCastConeRay(
            Vector3 eye,
            Vector3 direction,
            in VisionConfig config,
            out Vector3 feet)
        {
            feet = Vector3.zero;
            Vector3 origin = eye;
            Vector3 dir = direction.normalized;
            float remaining = config.MaxViewDistance;
            float skin = config.LosSkinWidth;
            Vector3 lastAccepted = Vector3.zero;
            bool hasAccepted = false;

#if UNITY_EDITOR
            Vector3 debugStart = eye;
#endif

            while (remaining > skin)
            {
                if (!Physics.Raycast(
                        origin,
                        dir,
                        out RaycastHit hit,
                        remaining,
                        config.CastMask,
                        QueryTriggerInteraction.Ignore))
                {
#if UNITY_EDITOR
                    DebugMissRays.Add((debugStart, origin + dir * remaining));
#endif
                    break;
                }

                int layer = hit.collider.gameObject.layer;

                if (IsLayer(layer, config.GroundMask))
                {
                    lastAccepted = hit.point;
                    hasAccepted = true;
#if UNITY_EDITOR
                    DebugAcceptedPoints.Add(hit.point);
                    DebugHitRays.Add((debugStart, hit.point));
#endif
                    break;
                }

                if (layer == VisionLayerMasks.Wall)
                {
                    if (TryDowncastToGround(hit.point, config, out Vector3 groundVertex) &&
                        VisionEvaluator.HasLineOfSight(eye, groundVertex, config.LosMask, config.LosSkinWidth))
                    {
                        lastAccepted = groundVertex;
                        hasAccepted = true;
#if UNITY_EDITOR
                        DebugAcceptedPoints.Add(groundVertex);
#endif
                    }

#if UNITY_EDITOR
                    DebugHitRays.Add((debugStart, hit.point));
#endif
                    break;
                }

                if (layer == VisionLayerMasks.VisionGround)
                {
                    if (TryDowncastToGround(hit.point, config, out Vector3 groundVertex))
                    {
                        lastAccepted = groundVertex;
                        hasAccepted = true;
#if UNITY_EDITOR
                        DebugAcceptedPoints.Add(groundVertex);
#endif
                    }

                    float advance = hit.distance + skin;
                    origin = hit.point + dir * skin;
                    remaining -= advance;
                    continue;
                }

#if UNITY_EDITOR
                DebugHitRays.Add((debugStart, hit.point));
#endif
                break;
            }

            if (!hasAccepted)
                return false;

            feet = lastAccepted;
            return true;
        }

        private static bool TryDowncastToGround(Vector3 from, in VisionConfig config, out Vector3 groundVertex)
        {
            if (Physics.Raycast(
                    from + Vector3.up * 0.05f,
                    Vector3.down,
                    out RaycastHit hit,
                    100f,
                    config.GroundMask,
                    QueryTriggerInteraction.Ignore))
            {
                groundVertex = hit.point;
                return true;
            }

            groundVertex = Vector3.zero;
            return false;
        }

        private static bool IsLayer(int layer, LayerMask mask)
        {
            return (mask.value & (1 << layer)) != 0;
        }

        private void BuildHorizontalStripMesh(in VisionConfig config, Transform localSpace)
        {
            float meshOffset = config.MeshOffset;
            Vector3 offset = Vector3.up * meshOffset;

            for (int e = 0; e < sampleGrid.Count; e++)
                TriangulateContiguousRuns(sampleGrid[e], localSpace, offset, meshOffset);
        }

        private void TriangulateContiguousRuns(
            List<FootprintSample> row,
            Transform localSpace,
            Vector3 offset,
            float meshOffset)
        {
            int index = 0;
            while (index < row.Count)
            {
                while (index < row.Count && !row[index].Valid)
                    index++;

                int start = index;
                while (index < row.Count && row[index].Valid)
                    index++;

                int end = index - 1;
                int count = end - start + 1;
                if (count < 2)
                    continue;

                if (count >= 3)
                    AddArcFan(row, start, end, localSpace, offset);
                else
                    AddThickGroundSegment(row[start].FeetWorld, row[end].FeetWorld, localSpace, meshOffset);
            }
        }

        private void AddArcFan(
            List<FootprintSample> row,
            int start,
            int end,
            Transform localSpace,
            Vector3 offset)
        {
            int anchorIdx = vertices.Count;
            vertices.Add(localSpace.InverseTransformPoint(row[start].FeetWorld + offset));

            int firstIdx = vertices.Count;
            for (int i = start + 1; i <= end; i++)
                vertices.Add(localSpace.InverseTransformPoint(row[i].FeetWorld + offset));

            for (int i = 0; i < end - start - 1; i++)
            {
                triangles.Add(anchorIdx);
                triangles.Add(firstIdx + i + 1);
                triangles.Add(firstIdx + i);
            }
        }

        private void AddThickGroundSegment(
            Vector3 feetA,
            Vector3 feetB,
            Transform localSpace,
            float meshOffset)
        {
            Vector2 dir = new Vector2(feetB.x - feetA.x, feetB.z - feetA.z);
            float length = dir.magnitude;
            if (length < 0.001f)
                return;

            dir /= length;
            Vector2 perp = new Vector2(-dir.y, dir.x) * 0.35f;
            float y = (feetA.y + feetB.y) * 0.5f + meshOffset;

            Vector3 v0 = new Vector3(feetA.x + perp.x, y, feetA.z + perp.y);
            Vector3 v1 = new Vector3(feetA.x - perp.x, y, feetA.z - perp.y);
            Vector3 v2 = new Vector3(feetB.x - perp.x, y, feetB.z - perp.y);
            Vector3 v3 = new Vector3(feetB.x + perp.x, y, feetB.z + perp.y);

            int i0 = vertices.Count;
            vertices.Add(localSpace.InverseTransformPoint(v0));
            int i1 = vertices.Count;
            vertices.Add(localSpace.InverseTransformPoint(v1));
            int i2 = vertices.Count;
            vertices.Add(localSpace.InverseTransformPoint(v2));
            int i3 = vertices.Count;
            vertices.Add(localSpace.InverseTransformPoint(v3));

            triangles.Add(i0);
            triangles.Add(i2);
            triangles.Add(i1);
            triangles.Add(i0);
            triangles.Add(i3);
            triangles.Add(i2);
        }
        */
    }
}
