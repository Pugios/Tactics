using UnityEngine;

namespace Tactics.Weapons
{
    /// <summary>
    /// Which numbers one trigger pull uses — interval, pellet count, pattern
    /// falloff — given the weapon and whether the pull was primary or alt.
    /// Pure logic, no Unity scene state (mirrors <c>SpreadCalculator</c>).
    ///
    /// This exists because the owner's responsiveness gate and the server's
    /// authoritative re-check must derive the cadence from identical arithmetic:
    /// if the client's interval is ever shorter than the server's, the server
    /// silently swallows shots the player already saw fire.
    /// </summary>
    public static class FireModeStats
    {
        /// <summary>True when this pull is a shotgun-burst alt-fire (Classic right click).</summary>
        public static bool IsShotgunAlt(WeaponData weapon, bool altShot)
            => altShot && weapon != null && weapon.altFireType == AltFireType.Shotgun;

        /// <summary>
        /// True for a melee weapon. Melee is discriminated off WeaponType rather
        /// than AltFireType because both of its modes are swings — right click is
        /// a heavier swing, not a different thing done with the same bullet.
        /// </summary>
        public static bool IsMelee(WeaponData weapon)
            => weapon != null && weapon.type == WeaponType.Melee;

        /// <summary>True when this pull is a melee weapon's heavy (right click) swing.</summary>
        public static bool IsMeleeAlt(WeaponData weapon, bool altShot)
            => altShot && IsMelee(weapon);

        /// <summary>Seconds one shot of the given mode occupies the gun for.</summary>
        public static float IntervalSeconds(WeaponData weapon, bool altShot, bool ads)
        {
            if (weapon == null) return 0f;
            // Both alt actions state an absolute rate rather than a multiplier.
            if (IsShotgunAlt(weapon, altShot) || IsMeleeAlt(weapon, altShot))
                return 1f / Mathf.Max(weapon.altFireRate, 0.0001f);

            float rate = weapon.fireRate
                * (ads && weapon.altFireType == AltFireType.AimDownSight ? weapon.adsFireRateMultiplier : 1f);
            return 1f / Mathf.Max(rate, 0.0001f);
        }

        /// <summary>
        /// Gap the gun must observe before the next shot: the longer of what the
        /// previous shot claimed and what this one needs. Without the previous
        /// shot's claim, a slow 3-pellet burst could be chased 0.15 s later by a
        /// fast primary — sustained DPS would be fine but burst damage would not.
        /// </summary>
        public static float RequiredGapSeconds(float previousShotInterval, float thisShotInterval)
            => Mathf.Max(previousShotInterval, thisShotInterval);

        /// <summary>
        /// Pellets a full trigger pull throws, before any short-magazine clamp
        /// (a burst fired off 2 remaining rounds throws 2 pellets).
        /// </summary>
        public static int MaxPelletCount(WeaponData weapon, bool altShot)
        {
            if (weapon == null) return 1;
            return IsShotgunAlt(weapon, altShot)
                ? Mathf.Max(1, weapon.altPelletCount)
                : Mathf.Max(1, weapon.pelletCount);
        }

        /// <summary>
        /// How this mode's pattern grows with range — see
        /// <see cref="WeaponData.spreadDistanceExponent"/>. The Classic's burst
        /// spreads sub-linearly like the Judge while its primary stays a true cone.
        /// </summary>
        public static float SpreadDistanceExponent(WeaponData weapon, bool altShot)
        {
            if (weapon == null) return 1f;
            return IsShotgunAlt(weapon, altShot)
                ? weapon.altSpreadDistanceExponent
                : weapon.spreadDistanceExponent;
        }
    }
}
