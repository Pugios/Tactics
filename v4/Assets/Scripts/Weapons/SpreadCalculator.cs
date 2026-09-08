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
        /// Spread degrees are calibrated at this distance: a weapon's pattern
        /// radius here is always distance · tan(spread°), whatever its
        /// spreadDistanceExponent. At exponent 1 the reference cancels out.
        /// </summary>
        public const float SpreadReferenceDistanceMeters = 10f;

        /// <summary>
        /// Effective projection distance for a spread pattern: d at exponent 1
        /// (pure angular cone), √(REF·d) at exponent 0.5 (shotgun pattern that
        /// grows sub-linearly so ring hits decay ~1/d instead of 1/d²).
        /// </summary>
        private static float ProjectionScale(float distance, float distanceExponent)
        {
            if (Mathf.Approximately(distanceExponent, 1f)) return distance;
            return Mathf.Pow(SpreadReferenceDistanceMeters, 1f - distanceExponent)
                * Mathf.Pow(distance, distanceExponent);
        }

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
        /// Current random-cone radius in degrees: stance column, growth over the
        /// spray, and the movement penalty. The penalty intentionally stacks on
        /// top of the max-spread cap — running fire is worse than any spray.
        ///
        /// How much of the weapon's alt-fire column an alt shot takes over
        /// depends on the alt-fire's type:
        ///   AimDownSight — first/max spread only; growth and movement penalties
        ///                  stay on the primary column (aiming a rifle doesn't
        ///                  change what running costs you).
        ///   Shotgun      — the whole column: first/max, per-shot growth, and the
        ///                  movement penalties, because the burst is its own gun.
        /// </summary>
        public static float ComputeSpreadDegrees(WeaponData weapon, float sprayIndex,
            bool crouched, bool altFire, MovementState movement)
        {
            // Defensive: an alt flag on a weapon without an alt-fire is ignored.
            bool useAltColumn = altFire && weapon.altFireType != AltFireType.None;
            bool altOwnsMovement = useAltColumn && weapon.altFireType == AltFireType.Shotgun;

            float first = useAltColumn
                ? (crouched ? weapon.altFirstShotSpreadCrouched : weapon.altFirstShotSpreadStanding)
                : (crouched ? weapon.firstShotSpreadCrouched : weapon.firstShotSpreadStanding);
            float max = useAltColumn
                ? (crouched ? weapon.altMaxSpreadCrouched : weapon.altMaxSpreadStanding)
                : (crouched ? weapon.maxSpreadCrouched : weapon.maxSpreadStanding);
            float growth = altOwnsMovement ? weapon.altSpreadPerShotDegrees : weapon.spreadPerShotDegrees;
            float spread = Mathf.Min(first + growth * sprayIndex, max);

            switch (movement)
            {
                case MovementState.CrouchWalking:
                    spread += altOwnsMovement ? weapon.altMovePenaltyCrouchWalk : weapon.movePenaltyCrouchWalk;
                    break;
                case MovementState.Walking:
                    spread += altOwnsMovement ? weapon.altMovePenaltyWalk : weapon.movePenaltyWalk;
                    break;
                case MovementState.Running:
                    spread += altOwnsMovement ? weapon.altMovePenaltyRun : weapon.movePenaltyRun;
                    break;
                case MovementState.Airborne:
                    spread += altOwnsMovement ? weapon.altMovePenaltyAirborne : weapon.movePenaltyAirborne;
                    break;
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
            bool crouched, bool altFire, MovementState movement, int seed, int shotNumber)
        {
            Vector2 offset = ComputeRecoilDegrees(weapon, sprayIndex);

            float spread = ComputeSpreadDegrees(weapon, sprayIndex, crouched, altFire, movement);
            HashShot(seed, shotNumber, out float u1, out float u2);
            float rollAngle = u1 * 2f * Mathf.PI;
            float rollRadius = Mathf.Sqrt(u2) * spread; // sqrt → uniform over the cone's disc

            offset.x += Mathf.Cos(rollAngle) * rollRadius;
            offset.y += Mathf.Sin(rollAngle) * rollRadius;
            return offset;
        }

        /// <summary>
        /// All pellet offsets for one trigger pull (shotguns): every pellet
        /// shares the pull's deterministic recoil and spread radius but draws
        /// its own disc roll from a consecutive shot number, so the hash
        /// stream stays strictly increasing and reject-safe — the caller
        /// advances its shot counter by pelletCount per pull.
        /// </summary>
        public static Vector2[] ComputePelletOffsetsDegrees(WeaponData weapon, float sprayIndex,
            bool crouched, bool altFire, MovementState movement, int seed, int baseShotNumber, int pelletCount)
        {
            var offsets = new Vector2[pelletCount];
            for (int i = 0; i < pelletCount; i++)
            {
                offsets[i] = ComputeShotOffsetDegrees(weapon, sprayIndex, crouched, altFire, movement,
                    seed, baseShotNumber + i);
            }
            return offsets;
        }

        /// <summary>
        /// Projects an angular offset into a displaced world-space aim point:
        /// distance · tan(angle) along the shooter→aim axis (backward) and its
        /// ground-plane perpendicular (sideways). Same angle misses by more at
        /// range, exactly like a first-person inaccuracy cone.
        /// </summary>
        public static Vector3 ApplyOffsetToAimPoint(Vector3 shooterPosition, Vector3 aimPoint, Vector2 offsetDegrees)
        {
            return ApplyOffsetToAimPoint(shooterPosition, aimPoint, offsetDegrees, 1f);
        }

        /// <summary>
        /// Exponent-aware projection: the angular offset is scaled by
        /// <see cref="ProjectionScale"/> instead of the raw distance, letting a
        /// weapon's pattern grow sub-linearly with range (see
        /// WeaponData.spreadDistanceExponent). Exponent 1 is the classic
        /// distance · tan(angle) cone.
        /// </summary>
        public static Vector3 ApplyOffsetToAimPoint(Vector3 shooterPosition, Vector3 aimPoint,
            Vector2 offsetDegrees, float distanceExponent)
        {
            Vector3 toAim = aimPoint - shooterPosition;
            toAim.y = 0f;
            float distance = toAim.magnitude;
            if (distance < 0.001f) return aimPoint;

            Vector3 back = toAim / distance;
            Vector3 right = Vector3.Cross(Vector3.up, back);

            float scale = ProjectionScale(distance, distanceExponent);
            return aimPoint
                + back * (scale * Mathf.Tan(offsetDegrees.y * Mathf.Deg2Rad))
                + right * (scale * Mathf.Tan(offsetDegrees.x * Mathf.Deg2Rad));
        }

        /// <summary>
        /// World-space radius (meters) the spread pattern covers around an aim
        /// point at the given horizontal shooter→aim distance — the same
        /// scale · tan(angle) projection <see cref="ApplyOffsetToAimPoint"/>
        /// uses, so anything drawn with this radius (crosshair circle) exactly
        /// bounds where pellets can land.
        /// </summary>
        public static float ComputeSpreadWorldRadiusMeters(float xzDistance, float spreadDegrees,
            float distanceExponent)
        {
            return ProjectionScale(xzDistance, distanceExponent) * Mathf.Tan(spreadDegrees * Mathf.Deg2Rad);
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
