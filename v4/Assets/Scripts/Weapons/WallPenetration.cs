using UnityEngine;

namespace Tactics.Weapons
{
    /// <summary>
    /// Pure-logic wall-bang model, no Unity scene state (mirrors
    /// <see cref="SpreadCalculator"/> and <see cref="DamageFalloff"/>). The
    /// Physics queries that find the wall segments stay in WeaponController,
    /// exactly as <see cref="MeleeCombat"/> leaves its Linecast there.
    ///
    /// Two independent axes, which is the heart of the measured Valorant model:
    ///   * the SURFACE decides how far a bullet may travel (WallSurfaceData.maxTravelMeters);
    ///   * the WEAPON's <see cref="PenetrationLevel"/> decides the damage curve
    ///     played out over that distance.
    /// A weapon's level therefore never changes which walls it can pierce, only
    /// what survives the trip - an Operator and a Classic get through exactly the
    /// same geometry, the Classic just arrives with far less.
    ///
    /// Damage never reaches zero. Every level bottoms out on a floor (30% for
    /// medium/high, 25% for low) rather than fading to nothing, which is what
    /// reproduces the measured minimums: Vandal 160 to 48, Sheriff 159 to 47,
    /// Spectre 78 to 19, Bucky 40 to 10.
    /// </summary>
    public static class WallPenetration
    {
        /// <summary>Travel budget for geometry with no WallSurface on it.</summary>
        public const float DefaultMaxTravelMeters = 0.8f;

        /// <summary>
        /// One contiguous stretch of Wall-layer geometry along the shot. Both
        /// distances are measured from the shot's origin along its direction, so
        /// an angled shot that enters a cube's face and leaves through its side
        /// measures its true path length just like a straight-through one.
        /// </summary>
        public struct WallSegment
        {
            public float entryDistance;
            public float exitDistance;
            public float maxTravelMeters;
        }

        /// <summary>
        /// Shape of one penetration level's damage curve over the normalized
        /// travel fraction t in [0, 1]:
        ///
        ///     u    = clamp01((t - head) / (1 - head - tail))
        ///     mult = floor + (start - floor) * (1 - u)^exponent
        ///
        /// <c>Head</c> is a flat stretch at the start - high-penetration weapons
        /// keep full damage through a little wall, which is the one thing that
        /// really separates them from medium. <c>Tail</c> is a flat stretch at
        /// the end, where low-penetration weapons sit on their floor early.
        /// </summary>
        private readonly struct Curve
        {
            public readonly float Start;
            public readonly float Floor;
            public readonly float Head;
            public readonly float Tail;
            public readonly float Exponent;

            public Curve(float start, float floor, float head, float tail, float exponent)
            {
                Start = start;
                Floor = floor;
                Head = head;
                Tail = tail;
                Exponent = exponent;
            }
        }

        // Fitted to the community damage-falloff graph (percentage damage vs.
        // travel between min and max penetration) to within ~3 percentage points
        // across the range, and exact on the floors that graph's author measured
        // directly. Two documented divergences from that testing:
        //   * The Operator is the table's lone outlier (225 to 92, a 0.41 ratio,
        //     where every other high-penetration weapon lands on 0.30). It reads
        //     as an artifact of a thinner test wall, so it floors at 0.30 here.
        //   * The rare "tail" to ~10% at the very tip is skipped: the author
        //     measured it at roughly 2 shots in 100 and calls the resulting
        //     inconsistency the mechanic's worst quality. Reproducing a bug that
        //     makes damage feel random is not worth the fidelity.
        private static readonly Curve HighCurve = new Curve(1.00f, 0.30f, 0.05f, 0.00f, 1.30f);
        private static readonly Curve MediumCurve = new Curve(0.99f, 0.30f, 0.00f, 0.00f, 1.50f);
        private static readonly Curve LowCurve = new Curve(0.80f, 0.25f, 0.00f, 0.15f, 1.30f);

        /// <summary>
        /// Collapses the segments a shot crossed into the single stretch of wall
        /// the damage model sees, per Valorant's own rule: from the first entry
        /// to the last exit, counting any air between two pierced objects as
        /// part of the wall. Note this only ever bites with two or more
        /// segments - with a single wall the last exit IS that wall's exit, so a
        /// long gap between the wall and a distant target costs nothing.
        ///
        /// When segments are made of different materials the most restrictive
        /// one governs the whole span, matching the finding that a denser
        /// material simply narrows the angle at which the same wall gives way.
        /// </summary>
        public static void GetTravel(WallSegment[] segments, out float spanMeters, out float maxTravelMeters)
        {
            spanMeters = 0f;
            maxTravelMeters = DefaultMaxTravelMeters;
            if (segments == null || segments.Length == 0) return;

            float firstEntry = float.PositiveInfinity;
            float lastExit = float.NegativeInfinity;
            float tightest = float.PositiveInfinity;

            for (int i = 0; i < segments.Length; i++)
            {
                WallSegment segment = segments[i];
                if (segment.entryDistance < firstEntry) firstEntry = segment.entryDistance;
                if (segment.exitDistance > lastExit) lastExit = segment.exitDistance;
                if (segment.maxTravelMeters < tightest) tightest = segment.maxTravelMeters;
            }

            spanMeters = Mathf.Max(0f, lastExit - firstEntry);
            maxTravelMeters = tightest;
        }

        /// <summary>
        /// Share of the surface's penetration budget the shot consumes. Greater
        /// than 1 means the bullet is stopped. A surface with a budget of zero or
        /// less is unpierceable by construction.
        /// </summary>
        public static float Fraction(float spanMeters, float maxTravelMeters)
        {
            if (spanMeters <= 0f) return 0f;
            if (maxTravelMeters <= 0f) return float.PositiveInfinity;
            return spanMeters / maxTravelMeters;
        }

        /// <summary>Convenience for callers that do not need the raw distances.</summary>
        public static float TravelFraction(WallSegment[] segments)
        {
            GetTravel(segments, out float span, out float maxTravel);
            return Fraction(span, maxTravel);
        }

        public static bool IsBlocked(float travelFraction) => travelFraction > 1f;

        /// <summary>
        /// Fraction of its normal damage a bullet of this penetration level keeps
        /// after crossing <paramref name="travelFraction"/> of the surface's
        /// budget. Returns 1 when no wall was crossed at all, and 0 when blocked.
        ///
        /// The step from "no wall" (1.00) to "a sliver of wall" is deliberately
        /// discontinuous for medium and low: medium loses damage immediately, and
        /// low drops a fixed 20% chunk however little wall it crosses. Only high
        /// penetration crosses a real amount of wall at full damage.
        /// </summary>
        public static float DamageMultiplier(PenetrationLevel level, float travelFraction)
        {
            if (travelFraction <= 0f) return 1f;
            if (IsBlocked(travelFraction)) return 0f;

            Curve curve = CurveFor(level);
            float span = 1f - curve.Head - curve.Tail;
            float u = span <= 0f ? 1f : Mathf.Clamp01((travelFraction - curve.Head) / span);
            return curve.Floor + (curve.Start - curve.Floor) * Mathf.Pow(1f - u, curve.Exponent);
        }

        private static Curve CurveFor(PenetrationLevel level)
        {
            switch (level)
            {
                case PenetrationLevel.High: return HighCurve;
                case PenetrationLevel.Medium: return MediumCurve;
                default: return LowCurve;
            }
        }
    }
}
