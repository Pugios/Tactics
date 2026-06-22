using UnityEngine;

namespace Tactics.Vision
{
    /// <summary>
    /// Named layer indices and default masks for vision queries.
    /// </summary>
    public static class VisionLayerMasks
    {
        public const int Default = 0;
        public const int Wall = 6;
        public const int Ground = 7;
        public const int VisionCone = 8;
        public const int Dynamic = 9;
        public const int Prop = 10;
        public const int VisionGround = 11;

        /// <summary>Wall, Ground, Prop, and Default — all block line of sight.</summary>
        public static LayerMask DefaultLos =>
            (1 << Wall) | (1 << Ground) | (1 << Prop) | (1 << Default);

        public static LayerMask GroundOnly => 1 << Ground;

        public static LayerMask WallOnly => 1 << Wall;

        public static LayerMask VisionGroundOnly => 1 << VisionGround;

        public static LayerMask DynamicOnly => 1 << Dynamic;

        /// <summary>Ground, Wall, and VisionGround — cone raycast targets.</summary>
        public static LayerMask DefaultCast =>
            GroundOnly | WallOnly | VisionGroundOnly;
    }
}
