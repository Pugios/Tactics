using UnityEngine;

namespace Tactics.Vision
{
    /// <summary>
    /// Runtime vision tuning passed to evaluators and the visible-area builder.
    /// </summary>
    public readonly struct VisionConfig
    {
        public float ViewAngle { get; }
        public float HorizontalHalfAngle => ViewAngle * 0.5f;
        public float VerticalHalfAngle { get; }
        public float MaxViewDistance { get; }
        public float EyeHeight { get; }
        public float EnemyHeight { get; }
        public float EnemyRadius { get; }
        public LayerMask LosMask { get; }
        public LayerMask GroundMask { get; }
        public LayerMask CastMask { get; }
        public float LosSkinWidth { get; }
        public int AzimuthSamples { get; }
        public int ElevationSamples { get; }
        public float MeshOffset { get; }
        public float FogStrength { get; }
        public float EdgeLengthDifferenceThreshold { get; }
        public float EdgeLengthThresholdReferenceDistance { get; }
        public float EdgeLengthDifferenceThresholdMin { get; }
        public float LongSightRefineDistance { get; }
        public float EdgeMatchTolerance { get; }
        public int MaxEdgeRefinements { get; }
        public int BoundaryRayCount { get; }

        public VisionConfig(
            float viewAngle,
            float verticalHalfAngle,
            float maxViewDistance,
            float eyeHeight,
            float enemyHeight,
            float enemyRadius,
            LayerMask losMask,
            LayerMask groundMask,
            LayerMask castMask,
            float losSkinWidth,
            int azimuthSamples,
            int elevationSamples,
            float meshOffset,
            float fogStrength,
            float edgeLengthDifferenceThreshold = 5f,
            float edgeLengthThresholdReferenceDistance = 10f,
            float edgeLengthDifferenceThresholdMin = 0.25f,
            float longSightRefineDistance = 20f,
            float edgeMatchTolerance = 5f,
            int maxEdgeRefinements = 3,
            int boundaryRayCount = 10)
        {
            ViewAngle = viewAngle;
            VerticalHalfAngle = verticalHalfAngle;
            MaxViewDistance = maxViewDistance;
            EyeHeight = eyeHeight;
            EnemyHeight = enemyHeight;
            EnemyRadius = enemyRadius;
            LosMask = losMask;
            GroundMask = groundMask;
            CastMask = castMask;
            LosSkinWidth = losSkinWidth;
            AzimuthSamples = azimuthSamples;
            ElevationSamples = elevationSamples;
            MeshOffset = meshOffset;
            FogStrength = fogStrength;
            EdgeLengthDifferenceThreshold = edgeLengthDifferenceThreshold;
            EdgeLengthThresholdReferenceDistance = edgeLengthThresholdReferenceDistance;
            EdgeLengthDifferenceThresholdMin = edgeLengthDifferenceThresholdMin;
            LongSightRefineDistance = longSightRefineDistance;
            EdgeMatchTolerance = edgeMatchTolerance;
            MaxEdgeRefinements = maxEdgeRefinements;
            BoundaryRayCount = boundaryRayCount;
        }
    }
}
