using UnityEngine;
using Tactics.Player;

namespace Tactics.Weapons
{
    /// <summary>
    /// Pure-logic bullet inaccuracy, fully deterministic: given the same spray
    /// index, stance, seed and shot number it always produces the same offset,
    /// so the server's resolution and any client-side prediction/UI can never
    /// disagree. No Unity scene state (mirrors <c>VisionEvaluator</c>).
    ///
    /// Two systems stack per shot, both expressed as angles off the shooter→aim
    /// axis and projected onto the ground at the aim point's distance
    /// (offset = distance · tan(angle), so error scales with range):
    ///
    /// 1. RECOIL — deterministic "T" pattern the shooter compensates for by
    ///    aiming short of the target: a backward climb (shots land beyond the
    ///    aim point, away from the shooter) that tops out after
    ///    <c>recoilClimbShots</c>, then a periodic left/right sway across the
    ///    top of the T.
    /// 2. SPREAD — a seeded-random cone (Valorant's published degree tables):
    ///    grows with consecutive shots from first-shot to max spread, with
    ///    stance picking the column and movement adding flat degree penalties.
    ///
    /// Offsets are returned as degrees in a Vector2: x = sideways (positive =
    /// shooter's right when facing the aim point), y = backward (positive =
    /// beyond the aim point).
    /// </summary>
    public static class SpreadCalculator
    {
        /// <summary>
        /// Bleeds the spray index back toward zero for time spent not shooting.
        /// A short grace period keeps normal full-auto cadence from decaying
        /// mid-spray; after it, recovery is gradual — a brief pause resumes the
        /// spray partway up the pattern instead of resetting it.
        /// </summary>
        public static float DecaySprayIndex(float sprayIndex, float secondsSinceLastShot,
            float decayDelay, float decayPerSecond)
        {
            if (secondsSinceLastShot <= decayDelay) return sprayIndex;
            return Mathf.Max(0f, sprayIndex - (secondsSinceLastShot - decayDelay) * decayPerSecond);
        }

        /// <summary>Deterministic recoil (the T): x = sway degrees, y = backward climb degrees.</summary>
        public static Vector2 ComputeRecoilDegrees(WeaponData weapon, float sprayIndex)
        {
            float climbShots = Mathf.Max(weapon.recoilClimbShots, 0.0001f);
            float climbT = Mathf.Clamp01(sprayIndex / climbShots);
            float back = weapon.recoilClimbCurve.Evaluate(climbT) * weapon.recoilClimbDegrees;

            // Sway starts once the climb tops out: ~half a period left, then
            // right, repeating — negative x is the shooter's left, where Valorant
            // sprays break first.
            float side = 0f;
            if (sprayIndex > climbShots && weapon.recoilSwayPeriodShots > 0f)
            {
                float swayPhase = (sprayIndex - climbShots) / weapon.recoilSwayPeriodShots;
                side = -Mathf.Sin(swayPhase * 2f * Mathf.PI) * weapon.recoilSwayDegrees;
            }

            return new Vector2(side, back);
        }

        /// <summary>
        /// Current random-cone radius in degrees: stance column (ADS swaps in
        /// the alt-fire first/max values), growth over the spray, and the
        /// movement penalty. The penalty intentionally stacks on top of the
        /// max-spread cap — running fire is worse than any spray.
        /// </summary>
        public static float ComputeSpreadDegrees(WeaponData weapon, float sprayIndex,
            bool crouched, bool ads, MovementState movement)
        {
            // Defensive: an ADS flag on a weapon without an ADS alt-fire is ignored.
            bool useAds = ads && weapon.altFireType == AltFireType.AimDownSight;
            float first = useAds
                ? (crouched ? weapon.adsFirstShotSpreadCrouched : weapon.adsFirstShotSpreadStanding)
                : (crouched ? weapon.firstShotSpreadCrouched : weapon.firstShotSpreadStanding);
            float max = useAds
                ? (crouched ? weapon.adsMaxSpreadCrouched : weapon.adsMaxSpreadStanding)
                : (crouched ? weapon.maxSpreadCrouched : weapon.maxSpreadStanding);
            float spread = Mathf.Min(first + weapon.spreadPerShotDegrees * sprayIndex, max);

            switch (movement)
            {
                case MovementState.CrouchWalking: spread += weapon.movePenaltyCrouchWalk; break;
                case MovementState.Walking: spread += weapon.movePenaltyWalk; break;
                case MovementState.Running: spread += weapon.movePenaltyRun; break;
                case MovementState.Airborne: spread += weapon.movePenaltyAirborne; break;
            }

            return spread;
        }

        /// <summary>
        /// Full per-shot offset: deterministic recoil + a spread roll drawn from
        /// (seed, shotNumber). Hashing instead of a sequential RNG means a shot
        /// the server rejects (cadence) can never shift the stream out of sync
        /// with a predicting client.
        /// </summary>
        public static Vector2 ComputeShotOffsetDegrees(WeaponData weapon, float sprayIndex,
            bool crouched, bool ads, MovementState movement, int seed, int shotNumber)
        {
            Vector2 offset = ComputeRecoilDegrees(weapon, sprayIndex);

            float spread = ComputeSpreadDegrees(weapon, sprayIndex, crouched, ads, movement);
            HashShot(seed, shotNumber, out float u1, out float u2);
            float rollAngle = u1 * 2f * Mathf.PI;
            float rollRadius = Mathf.Sqrt(u2) * spread; // sqrt → uniform over the cone's disc

            offset.x += Mathf.Cos(rollAngle) * rollRadius;
            offset.y += Mathf.Sin(rollAngle) * rollRadius;
            return offset;
        }

        /// <summary>
        /// Projects an angular offset into a displaced world-space aim point:
        /// distance · tan(angle) along the shooter→aim axis (backward) and its
        /// ground-plane perpendicular (sideways). Same angle misses by more at
        /// range, exactly like a first-person inaccuracy cone.
        /// </summary>
        public static Vector3 ApplyOffsetToAimPoint(Vector3 shooterPosition, Vector3 aimPoint, Vector2 offsetDegrees)
        {
            Vector3 toAim = aimPoint - shooterPosition;
            toAim.y = 0f;
            float distance = toAim.magnitude;
            if (distance < 0.001f) return aimPoint;

            Vector3 back = toAim / distance;
            Vector3 right = Vector3.Cross(Vector3.up, back);

            return aimPoint
                + back * (distance * Mathf.Tan(offsetDegrees.y * Mathf.Deg2Rad))
                + right * (distance * Mathf.Tan(offsetDegrees.x * Mathf.Deg2Rad));
        }

        /// <summary>
        /// Deterministic integer-mix hash of (seed, shotNumber) → two uniform
        /// floats in [0, 1). Pure integer math, so identical on every platform
        /// and process — unlike UnityEngine.Random or floating-point noise.
        /// </summary>
        public static void HashShot(int seed, int shotNumber, out float u1, out float u2)
        {
            uint h1 = Mix((uint)seed ^ Mix((uint)shotNumber * 0x9E3779B9u));
            uint h2 = Mix(h1 ^ 0x85EBCA6Bu);
            u1 = (h1 & 0xFFFFFF) / 16777216f;
            u2 = (h2 & 0xFFFFFF) / 16777216f;
        }

        private static uint Mix(uint h)
        {
            h ^= h >> 16;
            h *= 0x7FEB352Du;
            h ^= h >> 15;
            h *= 0x846CA68Bu;
            h ^= h >> 16;
            return h;
        }
    }
}
