using UnityEngine;

namespace Tactics.Vision
{
    /// <summary>
    /// Runtime vision tuning passed to the CPU visibility evaluators.
    /// </summary>
    public readonly struct VisionConfig
    {
        // Player FoV
        public float HorizontalViewAngle { get; }
        public float VerticalViewAngle { get; }
        public float MaxViewDistance { get; }

        // Enemy Proportions
        public float EnemyHeight { get; }
        public float EnemyRadius { get; }

        // Line-of-sight raycasts
        public LayerMask LosMask { get; }
        public float LosSkinWidth { get; }

        public VisionConfig(
            float horizontalViewAngle,
            float verticalViewAngle,
            float maxViewDistance,
            float enemyHeight,
            float enemyRadius,
            LayerMask losMask,
            float losSkinWidth)
        {
            HorizontalViewAngle = horizontalViewAngle;
            VerticalViewAngle = verticalViewAngle;
            MaxViewDistance = maxViewDistance;
            EnemyHeight = enemyHeight;
            EnemyRadius = enemyRadius;
            LosMask = losMask;
            LosSkinWidth = losSkinWidth;
        }
    }
}
