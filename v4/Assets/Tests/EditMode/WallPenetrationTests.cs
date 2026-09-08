using NUnit.Framework;
using UnityEngine;
using Tactics.Weapons;

namespace Tactics.Tests.EditMode
{
    public class WallPenetrationTests
    {
        private const float Tolerance = 0.0001f;

        /// <summary>One wall of the given thickness, entered at 1 m.</summary>
        private static WallPenetration.WallSegment[] OneWall(float thickness, float maxTravel)
        {
            return new[]
            {
                new WallPenetration.WallSegment
                {
                    entryDistance = 1f,
                    exitDistance = 1f + thickness,
                    maxTravelMeters = maxTravel,
                },
            };
        }

        // --- Damage curve: the measured floors ---

        // The floors are the only damage values the source testing pins down
        // exactly, so they are the curve's hard contract. Damage is truncated
        // once, at the end of the volley, so these compare the truncated result.
        [Test]
        public void DamageMultiplier_AtMaxTravel_ReproducesMeasuredFloors()
        {
            // Vandal: medium penetration, 160 head damage, bottoms out at 48.
            Assert.AreEqual(48, (int)(160f * WallPenetration.DamageMultiplier(PenetrationLevel.Medium, 1f)));
            // Sheriff: high penetration, 159 head damage, bottoms out at 47.
            Assert.AreEqual(47, (int)(159f * WallPenetration.DamageMultiplier(PenetrationLevel.High, 1f)));
            // Spectre / Classic: low penetration, 78 head damage, bottoms out at 19.
            Assert.AreEqual(19, (int)(78f * WallPenetration.DamageMultiplier(PenetrationLevel.Low, 1f)));
            // Bucky: low penetration, 40 head damage, bottoms out at 10.
            Assert.AreEqual(10, (int)(40f * WallPenetration.DamageMultiplier(PenetrationLevel.Low, 1f)));
        }

        [Test]
        public void DamageMultiplier_Floors_AreNeverZero()
        {
            Assert.AreEqual(0.30f, WallPenetration.DamageMultiplier(PenetrationLevel.High, 1f), Tolerance);
            Assert.AreEqual(0.30f, WallPenetration.DamageMultiplier(PenetrationLevel.Medium, 1f), Tolerance);
            Assert.AreEqual(0.25f, WallPenetration.DamageMultiplier(PenetrationLevel.Low, 1f), Tolerance);
        }

        // --- Damage curve: shape per level ---

        [Test]
        public void DamageMultiplier_NoWall_IsFullDamage()
        {
            Assert.AreEqual(1f, WallPenetration.DamageMultiplier(PenetrationLevel.High, 0f), Tolerance);
            Assert.AreEqual(1f, WallPenetration.DamageMultiplier(PenetrationLevel.Medium, 0f), Tolerance);
            Assert.AreEqual(1f, WallPenetration.DamageMultiplier(PenetrationLevel.Low, 0f), Tolerance);
        }

        [Test]
        public void DamageMultiplier_High_HoldsFullDamageThroughItsPlateau()
        {
            // High penetration crosses a real amount of wall at full damage —
            // the distinction the source calls the important one.
            Assert.AreEqual(1f, WallPenetration.DamageMultiplier(PenetrationLevel.High, 0.02f), Tolerance);
            Assert.AreEqual(1f, WallPenetration.DamageMultiplier(PenetrationLevel.High, 0.05f), Tolerance);
            // Just past the plateau it starts to fall.
            Assert.Less(WallPenetration.DamageMultiplier(PenetrationLevel.High, 0.06f), 1f);
        }

        [Test]
        public void DamageMultiplier_Medium_LosesDamageImmediately()
        {
            float sliver = WallPenetration.DamageMultiplier(PenetrationLevel.Medium, 0.001f);
            Assert.Less(sliver, 1f);
            Assert.Greater(sliver, 0.98f); // ~99% max, matching the Vandal's 159/160
            // Medium is always behind high at the same travel.
            Assert.Less(sliver, WallPenetration.DamageMultiplier(PenetrationLevel.High, 0.001f));
        }

        [Test]
        public void DamageMultiplier_Low_DropsAFixedChunkThenFlattens()
        {
            // Low penetration loses ~20% however little wall it crosses.
            Assert.AreEqual(0.80f, WallPenetration.DamageMultiplier(PenetrationLevel.Low, 0.0001f), 0.001f);
            // ...and sits on its floor through the last 15% of travel.
            Assert.AreEqual(0.25f, WallPenetration.DamageMultiplier(PenetrationLevel.Low, 0.85f), Tolerance);
            Assert.AreEqual(0.25f, WallPenetration.DamageMultiplier(PenetrationLevel.Low, 0.95f), Tolerance);
            Assert.AreEqual(0.25f, WallPenetration.DamageMultiplier(PenetrationLevel.Low, 1f), Tolerance);
        }

        [Test]
        public void DamageMultiplier_IsMonotonicallyNonIncreasing()
        {
            foreach (PenetrationLevel level in new[]
                     { PenetrationLevel.Low, PenetrationLevel.Medium, PenetrationLevel.High })
            {
                float previous = WallPenetration.DamageMultiplier(level, 0.0001f);
                for (int i = 1; i <= 100; i++)
                {
                    float current = WallPenetration.DamageMultiplier(level, i / 100f);
                    Assert.LessOrEqual(current, previous + Tolerance, $"{level} rose at t={i / 100f}");
                    previous = current;
                }
            }
        }

        [Test]
        public void DamageMultiplier_HighIsNeverWorseThanMediumIsNeverWorseThanLow()
        {
            for (int i = 0; i <= 100; i++)
            {
                float t = i / 100f;
                float high = WallPenetration.DamageMultiplier(PenetrationLevel.High, t);
                float medium = WallPenetration.DamageMultiplier(PenetrationLevel.Medium, t);
                float low = WallPenetration.DamageMultiplier(PenetrationLevel.Low, t);
                Assert.GreaterOrEqual(high, medium - Tolerance, $"high < medium at t={t}");
                Assert.GreaterOrEqual(medium, low - Tolerance, $"medium < low at t={t}");
            }
        }

        [Test]
        public void DamageMultiplier_PastMaxTravel_IsBlocked()
        {
            Assert.AreEqual(0f, WallPenetration.DamageMultiplier(PenetrationLevel.High, 1.0001f), Tolerance);
            Assert.AreEqual(0f, WallPenetration.DamageMultiplier(PenetrationLevel.Low, 5f), Tolerance);
            Assert.AreEqual(0f, WallPenetration.DamageMultiplier(PenetrationLevel.Medium, float.PositiveInfinity), Tolerance);
        }

        // --- Travel geometry: the "one solid wall" rule ---

        [Test]
        public void GetTravel_NoSegments_IsNoTravel()
        {
            WallPenetration.GetTravel(null, out float span, out float budget);
            Assert.AreEqual(0f, span, Tolerance);
            Assert.AreEqual(WallPenetration.DefaultMaxTravelMeters, budget, Tolerance);

            WallPenetration.GetTravel(new WallPenetration.WallSegment[0], out span, out budget);
            Assert.AreEqual(0f, span, Tolerance);
        }

        [Test]
        public void GetTravel_SingleWall_IsItsOwnThickness()
        {
            WallPenetration.GetTravel(OneWall(0.35f, 0.7f), out float span, out float budget);
            Assert.AreEqual(0.35f, span, Tolerance);
            Assert.AreEqual(0.7f, budget, Tolerance);
        }

        [Test]
        public void GetTravel_SingleWall_LongGapToTargetCostsNothing()
        {
            // The last exit IS the wall's exit, so the 30 m of air between the
            // wall and a distant target must not be charged as wall.
            var segments = new[]
            {
                new WallPenetration.WallSegment
                {
                    entryDistance = 2f,
                    exitDistance = 2.4f,
                    maxTravelMeters = 0.8f,
                },
            };
            WallPenetration.GetTravel(segments, out float span, out _);
            Assert.AreEqual(0.4f, span, Tolerance);
        }

        [Test]
        public void GetTravel_TwoWalls_CountsTheAirBetweenThem()
        {
            // First entry 1.0 → last exit 2.2 is treated as one solid wall, so
            // the 0.8 m gap counts even though both walls are only 0.2 m thick.
            var segments = new[]
            {
                new WallPenetration.WallSegment { entryDistance = 1.0f, exitDistance = 1.2f, maxTravelMeters = 0.8f },
                new WallPenetration.WallSegment { entryDistance = 2.0f, exitDistance = 2.2f, maxTravelMeters = 0.8f },
            };
            WallPenetration.GetTravel(segments, out float span, out _);
            Assert.AreEqual(1.2f, span, Tolerance);
            // 1.2 m of an 0.8 m budget stops the bullet, where either wall alone
            // would have been trivial.
            Assert.IsTrue(WallPenetration.IsBlocked(WallPenetration.TravelFraction(segments)));
        }

        [Test]
        public void GetTravel_SegmentOrderDoesNotMatter()
        {
            var forward = new[]
            {
                new WallPenetration.WallSegment { entryDistance = 1.0f, exitDistance = 1.2f, maxTravelMeters = 0.8f },
                new WallPenetration.WallSegment { entryDistance = 2.0f, exitDistance = 2.2f, maxTravelMeters = 0.6f },
            };
            var reversed = new[] { forward[1], forward[0] };

            WallPenetration.GetTravel(forward, out float spanA, out float budgetA);
            WallPenetration.GetTravel(reversed, out float spanB, out float budgetB);
            Assert.AreEqual(spanA, spanB, Tolerance);
            Assert.AreEqual(budgetA, budgetB, Tolerance);
        }

        [Test]
        public void GetTravel_MixedMaterials_TheMostRestrictiveGoverns()
        {
            var segments = new[]
            {
                new WallPenetration.WallSegment { entryDistance = 1.0f, exitDistance = 1.2f, maxTravelMeters = 1.0f },
                new WallPenetration.WallSegment { entryDistance = 1.2f, exitDistance = 1.4f, maxTravelMeters = 0.45f },
            };
            WallPenetration.GetTravel(segments, out float span, out float budget);
            Assert.AreEqual(0.4f, span, Tolerance);
            Assert.AreEqual(0.45f, budget, Tolerance);
        }

        // --- Fraction and the block boundary ---

        [Test]
        public void Fraction_ScalesSpanAgainstTheSurfaceBudget()
        {
            Assert.AreEqual(0.5f, WallPenetration.Fraction(0.35f, 0.7f), Tolerance);
            Assert.AreEqual(1f, WallPenetration.Fraction(0.7f, 0.7f), Tolerance);
            Assert.AreEqual(0f, WallPenetration.Fraction(0f, 0.7f), Tolerance);
        }

        [Test]
        public void Fraction_ZeroBudgetSurface_IsUnpierceable()
        {
            Assert.IsTrue(WallPenetration.IsBlocked(WallPenetration.Fraction(0.01f, 0f)));
        }

        [Test]
        public void TravelFraction_AtTheBoundary_PenetratesAtTheFloorThenBlocks()
        {
            // Exactly at max travel the shot still lands, on the floor value.
            // Halves are exactly representable, so this tests the boundary rule
            // rather than float rounding a few ULPs either side of it.
            float atMax = WallPenetration.TravelFraction(OneWall(0.5f, 0.5f));
            Assert.IsFalse(WallPenetration.IsBlocked(atMax));
            Assert.AreEqual(0.30f, WallPenetration.DamageMultiplier(PenetrationLevel.Medium, atMax), Tolerance);

            // A hair past it, nothing gets through.
            float pastMax = WallPenetration.TravelFraction(OneWall(0.51f, 0.5f));
            Assert.IsTrue(WallPenetration.IsBlocked(pastMax));
            Assert.AreEqual(0f, WallPenetration.DamageMultiplier(PenetrationLevel.Medium, pastMax), Tolerance);
        }

        // --- End to end: surface reach is shared, damage is not ---

        [Test]
        public void SameWall_EveryLevelPenetrates_ButKeepsADifferentShare()
        {
            // A 0.35 m stone wall (budget 0.7 m) is half the budget for everyone.
            float t = WallPenetration.TravelFraction(OneWall(0.35f, 0.7f));
            Assert.IsFalse(WallPenetration.IsBlocked(t));

            float high = WallPenetration.DamageMultiplier(PenetrationLevel.High, t);
            float medium = WallPenetration.DamageMultiplier(PenetrationLevel.Medium, t);
            float low = WallPenetration.DamageMultiplier(PenetrationLevel.Low, t);

            Assert.Greater(high, medium);
            Assert.Greater(medium, low);

            // Operator (high, 255 head) vs Classic (low, 78 head) through the
            // identical wall: both get through, which is the point.
            Assert.Greater((int)(255f * high), 0);
            Assert.Greater((int)(78f * low), 0);
        }

        [Test]
        public void DenserSurface_BlocksWhatASofterOneLetsThrough()
        {
            // The same 0.5 m of wall: wood (1.0 m budget) gives, metal (0.45) does not.
            Assert.IsFalse(WallPenetration.IsBlocked(WallPenetration.TravelFraction(OneWall(0.5f, 1.0f))));
            Assert.IsTrue(WallPenetration.IsBlocked(WallPenetration.TravelFraction(OneWall(0.5f, 0.45f))));
        }

        [Test]
        public void PenetrationMultiplier_ComposesWithDistanceFalloff()
        {
            // Wall-bang damage is the falloff band value scaled by penetration,
            // not a replacement for it — the two are independent factors.
            WeaponData judge = ScriptableObject.CreateInstance<WeaponData>();
            judge.damageRanges = new[]
            {
                new DamageRange { maxDistance = 10f, head = 34f, body = 17f, leg = 14f },
                new DamageRange { maxDistance = 50f, head = 14f, body = 7f, leg = 5f },
            };
            judge.wallPenetration = PenetrationLevel.Low;

            float t = WallPenetration.TravelFraction(OneWall(0.35f, 0.7f));
            float multiplier = WallPenetration.DamageMultiplier(judge.wallPenetration, t);

            Assert.AreEqual(34f, DamageFalloff.GetZoneDamage(judge, 5f, HitZone.Head), Tolerance);
            Assert.AreEqual(14f, DamageFalloff.GetZoneDamage(judge, 20f, HitZone.Head), Tolerance);

            float near = DamageFalloff.GetZoneDamage(judge, 5f, HitZone.Head) * multiplier;
            float far = DamageFalloff.GetZoneDamage(judge, 20f, HitZone.Head) * multiplier;

            // Distance still picks the band; penetration only scales whatever it picked.
            Assert.Greater(near, far);
            Assert.Less(near, 34f);
            Assert.Greater(near, 0f);
        }
    }
}
