using UnityEngine;

namespace Tactics.Vision
{
    /// <summary>
    /// VisionGround colliders exist only for cone raycasts. They must not block CharacterControllers or rigidbodies.
    /// </summary>
    public static class VisionPhysicsSetup
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ApplyAtRuntime()
        {
            ApplyVisionGroundCollisionIgnore();
        }

        public static void ApplyVisionGroundCollisionIgnore()
        {
            int visionGround = VisionLayerMasks.VisionGround;
            for (int layer = 0; layer < 32; layer++)
                Physics.IgnoreLayerCollision(visionGround, layer, true);
        }
    }
}
