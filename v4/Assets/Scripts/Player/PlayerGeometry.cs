namespace Tactics.Player
{
    /// <summary>
    /// Where a player's parts sit relative to their transform. Pure arithmetic,
    /// no scene state (mirrors <see cref="Tactics.Weapons.SpreadCalculator"/>).
    ///
    /// The one thing worth remembering: **the player's origin is not their feet.**
    /// The CharacterController is centred on the transform (centre 0, height 2),
    /// so the origin sits at the waist and the soles are half a height below it.
    /// Heights authored against the body — <c>eyeHeight</c>, and anything a
    /// designer measures — are given from the FEET, because that is the number
    /// you can check against the world. Converting between the two is what this
    /// class exists for, and forgetting it puts shots a metre above the eyes and
    /// floor probes a metre above the floor.
    /// </summary>
    public static class PlayerGeometry
    {
        /// <summary>Used only if the CharacterController is somehow missing.</summary>
        public const float DefaultFeetToOrigin = 1f;

        /// <summary>
        /// Drop from the origin to the soles. Crouching keeps the feet planted and
        /// shrinks the capsule upward, so this stays put as the pose changes.
        /// </summary>
        public static float FeetToOrigin(float controllerHeight, float controllerCenterY)
        {
            return controllerHeight * 0.5f - controllerCenterY;
        }

        /// <summary>
        /// Rise from the origin to the eyes, given an eye height measured from the
        /// feet. Standing (height 2, centre 0, eye 1.5) this is +0.5 — exactly the
        /// VisionOrigin's local offset on the Player prefab.
        /// </summary>
        public static float EyeOffsetFromOrigin(float controllerHeight, float controllerCenterY,
            float eyeHeightAboveFeet)
        {
            return eyeHeightAboveFeet - FeetToOrigin(controllerHeight, controllerCenterY);
        }
    }
}
