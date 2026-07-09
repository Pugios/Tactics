using UnityEngine;

namespace Tactics.Vision
{
    /// <summary>
    /// Runtime vision tuning passed to evaluators and the visible-area builder.
    /// </summary>
    public readonly struct VisionConfig
    {
        // Player FoV
        public float HorizontalViewAngle { get; }
        public float VerticalViewAngle { get; }
        public float MaxViewDistance { get; }

        // Player Proportions
        public float EyeHeight { get; }
        public float EnemyHeight { get; }
        public float EnemyRadius { get; }

        // FoV Raycast hits
        public LayerMask LosMask { get; }
        public float LosSkinWidth { get; }

        // FoV Building
        public float MeshOffset { get; }
        public int BoundaryRayCount { get; }
        public int MaxHorizontalEdgeRefinements { get; }
        public int MaxVerticalEdgeRefinements { get; }
        public int MaxVisionGroundPasses { get; }
        public int SparseLedgeRayCount { get; }

        // FoV Refinement Rules
        public float EdgeRefineSensitivity { get; }
        public float HiddenGeometryThreshold { get; }

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
            int boundaryRayCount,
            int maxHorizontalEdgeRefinements,
            int maxVerticalEdgeRefinements,
            int maxVisionGroundPasses,
            int sparseLedgeRayCount,
            float edgeRefineSensitivity,
            float hiddenGeometryThreshold)
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
            BoundaryRayCount = boundaryRayCount;
            MaxHorizontalEdgeRefinements = maxHorizontalEdgeRefinements;
            MaxVerticalEdgeRefinements = maxVerticalEdgeRefinements;
            MaxVisionGroundPasses = maxVisionGroundPasses;
            SparseLedgeRayCount = sparseLedgeRayCount;
            EdgeRefineSensitivity = edgeRefineSensitivity;
            HiddenGeometryThreshold = hiddenGeometryThreshold;
        }
    }
}
