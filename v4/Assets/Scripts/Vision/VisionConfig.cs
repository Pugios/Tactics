using UnityEngine;

namespace Tactics.Vision
{
    /// <summary>
    /// Runtime vision tuning passed to evaluators and the visible-area builder.
    /// </summary>
    public readonly struct VisionConfig
    {
        public float HorizontalViewAngle { get; }
        public float VerticalViewAngle { get; }

        public float HorizontalHalfAngle => HorizontalViewAngle * 0.5f;
        public float VerticalHalfAngle => VerticalViewAngle * 0.5f;

        public float MaxViewDistance { get; }
        public float EyeHeight { get; }
        public float EnemyHeight { get; }
        public float EnemyRadius { get; }
        public LayerMask LosMask { get; }
        public float LosSkinWidth { get; }
        public float MeshOffset { get; }
        public float EdgeLengthDifferenceThreshold { get; }
        public float EdgeLengthThresholdReferenceDistance { get; }
        public float EdgeLengthDifferenceThresholdMin { get; }
        public float LongSightRefineDistance { get; }
        public float EdgeMatchTolerance { get; }
        public int MaxHorizontalEdgeRefinements { get; }
        public int MaxVerticalEdgeRefinements { get; }
        public int BoundaryRayCount { get; }
        public int SparseLedgeRayCount { get; }
        public float VerticalEdgeLengthDifferenceThreshold { get; }
        public float MinLedgeMergeDistance { get; }
        public int MaxVisionGroundPasses { get; }

        public VisionConfig(
            float horizontalViewAngle,
            float verticalViewAngle,
            float maxViewDistance,
            float eyeHeight,
            float enemyHeight,
            float enemyRadius,
            LayerMask losMask,
            float losSkinWidth,
            float meshOffset,
            float edgeLengthDifferenceThreshold = 5f,
            float edgeLengthThresholdReferenceDistance = 10f,
            float edgeLengthDifferenceThresholdMin = 0.25f,
            float longSightRefineDistance = 20f,
            float edgeMatchTolerance = 5f,
            int maxHorizontalEdgeRefinements = 3,
            int maxVerticalEdgeRefinements = 1,
            int boundaryRayCount = 10,
            int sparseLedgeRayCount = 3,
            float verticalEdgeLengthDifferenceThreshold = 4f,
            float minLedgeMergeDistance = 2.5f,
            int maxVisionGroundPasses = 5)
        {
            HorizontalViewAngle = horizontalViewAngle;
            VerticalViewAngle = verticalViewAngle;
            MaxViewDistance = maxViewDistance;
            EyeHeight = eyeHeight;
            EnemyHeight = enemyHeight;
            EnemyRadius = enemyRadius;
            LosMask = losMask;
            LosSkinWidth = losSkinWidth;
            MeshOffset = meshOffset;
            EdgeLengthDifferenceThreshold = edgeLengthDifferenceThreshold;
            EdgeLengthThresholdReferenceDistance = edgeLengthThresholdReferenceDistance;
            EdgeLengthDifferenceThresholdMin = edgeLengthDifferenceThresholdMin;
            LongSightRefineDistance = longSightRefineDistance;
            EdgeMatchTolerance = edgeMatchTolerance;
            MaxHorizontalEdgeRefinements = maxHorizontalEdgeRefinements;
            MaxVerticalEdgeRefinements = maxVerticalEdgeRefinements;
            BoundaryRayCount = boundaryRayCount;
            SparseLedgeRayCount = sparseLedgeRayCount;
            VerticalEdgeLengthDifferenceThreshold = verticalEdgeLengthDifferenceThreshold;
            MinLedgeMergeDistance = minLedgeMergeDistance;
            MaxVisionGroundPasses = maxVisionGroundPasses;
        }
    }
}
