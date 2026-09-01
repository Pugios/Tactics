namespace Tactics.Player
{
    /// <summary>
    /// Movement category a player occupies for accuracy purposes, ordered from
    /// most to least accurate. Classified from the CHOSEN inputs (crouch/walk),
    /// not from the speed those inputs produced: speed modifiers that don't
    /// change the player's movement choice (ADS, future slows) must never shift
    /// the spread band. The server classifies from its own authoritative
    /// snapshot's input-derived flags — clients never report the category.
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
        // Actual velocity only decides moving vs not (e.g. pushing into a wall
        // counts as standing still); grounded movement snaps instantly to its
        // band speed, so a tiny epsilon suffices.
        private const float StationarySpeedEpsilon = 0.05f;

        public static MovementState Classify(bool grounded, bool crouching, bool walking, float horizontalSpeed)
        {
            if (!grounded) return MovementState.Airborne;
            if (horizontalSpeed < StationarySpeedEpsilon) return MovementState.Stationary;
            if (crouching) return MovementState.CrouchWalking;
            return walking ? MovementState.Walking : MovementState.Running;
        }
    }
}
