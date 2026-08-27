namespace Tactics.Player
{
    /// <summary>
    /// Movement category a player occupies for accuracy purposes, ordered from
    /// most to least accurate. Derived from simulation state (grounded, crouch,
    /// horizontal speed) rather than raw input so the server can classify a
    /// shooter from its own authoritative snapshot — clients never report it.
    /// </summary>
    public enum MovementState
    {
        Stationary,
        CrouchWalking,
        Walking,
        Running,
        Airborne
    }

    public static class MovementClassifier
    {
        // Grounded movement snaps instantly to exactly 0 / crouch / walk / run
        // speed (no acceleration), so thresholds only need to split those bands.
        private const float StationarySpeedEpsilon = 0.05f;

        public static MovementState Classify(bool grounded, bool crouching, float horizontalSpeed,
            float runSpeed, float walkSpeedMultiplier)
        {
            if (!grounded) return MovementState.Airborne;
            if (horizontalSpeed < StationarySpeedEpsilon) return MovementState.Stationary;
            if (crouching) return MovementState.CrouchWalking;

            float walkRunThreshold = runSpeed * (walkSpeedMultiplier + 1f) * 0.5f;
            return horizontalSpeed > walkRunThreshold ? MovementState.Running : MovementState.Walking;
        }
    }
}
