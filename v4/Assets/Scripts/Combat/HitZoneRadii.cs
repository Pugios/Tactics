namespace Tactics.Combat
{
    /// <summary>
    /// Damage rings are resolved by XZ distance from the rewound target position
    /// (<see cref="Tactics.Weapons.WeaponController"/>). The ground-ring visuals
    /// draw from these same values so the picture players see can never drift
    /// from the damage model.
    /// </summary>
    public static class HitZoneRadii
    {
        public const float Head = 0.5f;
        public const float Body = 0.75f;
        public const float Leg = 1f;
    }
}
