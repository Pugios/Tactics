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
        public const int Dynamic = 9;

        /// <summary>Wall, Ground, and Default — all block line of sight.</summary>
        public static LayerMask DefaultLos =>
            (1 << Wall) | (1 << Ground) | (1 << Default);

        public static LayerMask GroundOnly => 1 << Ground;

        /// <summary>Everything the eye depth camera renders: static occluders plus dynamic bodies.</summary>
        public static LayerMask EyeDepthCulling => DefaultLos | (1 << Dynamic);
    }
}
