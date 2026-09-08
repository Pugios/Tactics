using NUnit.Framework;
using UnityEngine;
using Tactics.Player;
using Tactics.Weapons;

namespace Tactics.Tests.EditMode
{
    public class ShotgunLogicTests
    {
        private const int Seed = 54321;
        private const int PelletCount = 12;
        private const int ClassicAltPellets = 3;

        /// <summary>Judge-shaped weapon: 12 pellets, banded falloff damage, zero recoil.</summary>
        private static WeaponData MakeJudge()
        {
            WeaponData weapon = ScriptableObject.CreateInstance<WeaponData>();
            weapon.pelletCount = PelletCount;
            weapon.firstShotSpreadStanding = 8f;
            weapon.firstShotSpreadCrouched = 7f;
            weapon.maxSpreadStanding = 9.5f;
            weapon.maxSpreadCrouched = 8.5f;
            weapon.spreadPerShotDegrees = 0.5f;
            weapon.movePenaltyCrouchWalk = 0.5f;
            weapon.movePenaltyWalk = 1f;
            weapon.movePenaltyRun = 1f;
            weapon.movePenaltyAirborne = 4f;
            weapon.spreadDistanceExponent = 0.5f;
            weapon.recoilClimbDegrees = 0f;
            weapon.recoilSwayDegrees = 0f;
            weapon.damageRanges = new[]
            {
                new DamageRange { maxDistance = 10f, head = 34f, body = 17f, leg = 14f },
                new DamageRange { maxDistance = 15f, head = 20f, body = 10f, leg = 8f },
                new DamageRange { maxDistance = 50f, head = 14f, body = 7f, leg = 5f },
            };
            return weapon;
        }


        /// <summary>
        /// Classic-shaped weapon: single-pellet primary plus a 3-pellet shotgun
        /// alt-fire that owns its own spread column, movement penalties, cadence
        /// and pattern falloff. Mirrors Assets/Data/Weapons/Classic.asset.
        /// </summary>
        private static WeaponData MakeClassic()
        {
            WeaponData weapon = ScriptableObject.CreateInstance<WeaponData>();
            weapon.fireRate = 6.75f;
            weapon.magazineSize = 12;
            weapon.pelletCount = 1;
            weapon.firstShotSpreadStanding = 0.4f;
            weapon.firstShotSpreadCrouched = 0.3f;
            weapon.maxSpreadStanding = 1.8f;
            weapon.maxSpreadCrouched = 1.35f;
            weapon.spreadPerShotDegrees = 0.35f;
            weapon.movePenaltyCrouchWalk = 0.5f;
            weapon.movePenaltyWalk = 1.1f;
            weapon.movePenaltyRun = 2.3f;
            weapon.movePenaltyAirborne = 7f;
            weapon.spreadDistanceExponent = 1f;
            weapon.recoilClimbDegrees = 0f;
            weapon.recoilSwayDegrees = 0f;
            weapon.sprayDecayDelay = 0.2f;
            weapon.sprayDecayPerSecond = 15f;
            weapon.damageRanges = new[]
            {
                new DamageRange { maxDistance = 30f, head = 78f, body = 26f, leg = 22f },
                new DamageRange { maxDistance = 50f, head = 66f, body = 22f, leg = 18f },
            };
            weapon.altFireType = AltFireType.Shotgun;
            weapon.altFirstShotSpreadStanding = 1.9f;
            weapon.altFirstShotSpreadCrouched = 1.71f;
            weapon.altMaxSpreadStanding = 5.78f;
            weapon.altMaxSpreadCrouched = 5.21f;
            weapon.altPelletCount = ClassicAltPellets;
            weapon.altFireRate = 2.22f;
            weapon.altSpreadPerShotDegrees = 1.2933333f;
            weapon.altMovePenaltyCrouchWalk = 0f;
            weapon.altMovePenaltyWalk = 0.6f;
            weapon.altMovePenaltyRun = 1.5f;
            weapon.altMovePenaltyAirborne = 2.25f;
            weapon.altSpreadDistanceExponent = 0.5f;
            return weapon;
        }

        /// <summary>Vandal-shaped ADS rifle: the alt column overrides first/max spread only.</summary>
        private static WeaponData MakeAdsRifle()
        {
            WeaponData weapon = ScriptableObject.CreateInstance<WeaponData>();
            weapon.fireRate = 5.4f;
            weapon.pelletCount = 1;
            weapon.firstShotSpreadStanding = 0.25f;
            weapon.firstShotSpreadCrouched = 0.21f;
            weapon.maxSpreadStanding = 1f;
            weapon.maxSpreadCrouched = 0.85f;
            weapon.spreadPerShotDegrees = 0.11f;
            weapon.movePenaltyCrouchWalk = 0.8f;
            weapon.movePenaltyWalk = 3f;
            weapon.movePenaltyRun = 6f;
            weapon.movePenaltyAirborne = 10f;
            weapon.spreadDistanceExponent = 1f;
            weapon.recoilClimbDegrees = 0f;
            weapon.recoilSwayDegrees = 0f;
            weapon.altFireType = AltFireType.AimDownSight;
            weapon.adsFireRateMultiplier = 0.9f;
            weapon.altFirstShotSpreadStanding = 0.157f;
            weapon.altFirstShotSpreadCrouched = 0.13f;
            weapon.altMaxSpreadStanding = 1.02f;
            weapon.altMaxSpreadCrouched = 0.87f;
            return weapon;
        }

        // --- Pellet offsets ---

        [Test]
        public void PelletOffsets_SameInputs_AreIdentical()
        {
            WeaponData weapon = MakeJudge();

            Vector2[] a = SpreadCalculator.ComputePelletOffsetsDegrees(weapon, 2f, false, false, MovementState.Stationary, Seed, 24, PelletCount);
            Vector2[] b = SpreadCalculator.ComputePelletOffsetsDegrees(weapon, 2f, false, false, MovementState.Stationary, Seed, 24, PelletCount);

            CollectionAssert.AreEqual(a, b);
        }

        [Test]
        public void PelletOffsets_ProducesTwelveDistinctRolls()
        {
            WeaponData weapon = MakeJudge();

            Vector2[] offsets = SpreadCalculator.ComputePelletOffsetsDegrees(weapon, 0f, false, false, MovementState.Stationary, Seed, 0, PelletCount);

            // Recoil is zero, so equal offsets would mean colliding hash rolls.
            Assert.AreEqual(PelletCount, offsets.Length);
            for (int i = 0; i < offsets.Length; i++)
            {
                for (int j = i + 1; j < offsets.Length; j++)
                {
                    Assert.AreNotEqual(offsets[i], offsets[j], $"pellets {i} and {j} rolled the same offset");
                }
            }
        }

        [Test]
        public void PelletOffsets_AllPelletsInsideSpreadCone()
        {
            WeaponData weapon = MakeJudge();
            float cone = SpreadCalculator.ComputeSpreadDegrees(weapon, 0f, false, false, MovementState.Stationary);

            for (int pull = 0; pull < 100; pull++)
            {
                Vector2[] offsets = SpreadCalculator.ComputePelletOffsetsDegrees(weapon, 0f, false, false,
                    MovementState.Stationary, Seed, pull * PelletCount, PelletCount);
                foreach (Vector2 offset in offsets)
                {
                    Assert.LessOrEqual(offset.magnitude, cone + 0.0001f);
                }
            }
        }

        [Test]
        public void PelletOffsets_ConsecutiveTriggerPulls_DiffersBetweenPulls()
        {
            WeaponData weapon = MakeJudge();

            Vector2[] first = SpreadCalculator.ComputePelletOffsetsDegrees(weapon, 0f, false, false, MovementState.Stationary, Seed, 0, PelletCount);
            Vector2[] second = SpreadCalculator.ComputePelletOffsetsDegrees(weapon, 0f, false, false, MovementState.Stationary, Seed, PelletCount, PelletCount);

            CollectionAssert.AreNotEqual(first, second);
        }

        // --- Judge spread profile ---

        [Test]
        public void JudgeSpread_GrowsHalfDegreePerShot_MaxOnFourthShot()
        {
            WeaponData weapon = MakeJudge();

            Assert.AreEqual(8.0f, SpreadCalculator.ComputeSpreadDegrees(weapon, 0f, false, false, MovementState.Stationary), 0.0001f);
            Assert.AreEqual(8.5f, SpreadCalculator.ComputeSpreadDegrees(weapon, 1f, false, false, MovementState.Stationary), 0.0001f);
            Assert.AreEqual(9.0f, SpreadCalculator.ComputeSpreadDegrees(weapon, 2f, false, false, MovementState.Stationary), 0.0001f);
            Assert.AreEqual(9.5f, SpreadCalculator.ComputeSpreadDegrees(weapon, 3f, false, false, MovementState.Stationary), 0.0001f);
            Assert.AreEqual(9.5f, SpreadCalculator.ComputeSpreadDegrees(weapon, 4f, false, false, MovementState.Stationary), 0.0001f);

            Assert.AreEqual(7.0f, SpreadCalculator.ComputeSpreadDegrees(weapon, 0f, true, false, MovementState.Stationary), 0.0001f);
            Assert.AreEqual(7.5f, SpreadCalculator.ComputeSpreadDegrees(weapon, 1f, true, false, MovementState.Stationary), 0.0001f);
            Assert.AreEqual(8.0f, SpreadCalculator.ComputeSpreadDegrees(weapon, 2f, true, false, MovementState.Stationary), 0.0001f);
            Assert.AreEqual(8.5f, SpreadCalculator.ComputeSpreadDegrees(weapon, 3f, true, false, MovementState.Stationary), 0.0001f);
            Assert.AreEqual(8.5f, SpreadCalculator.ComputeSpreadDegrees(weapon, 4f, true, false, MovementState.Stationary), 0.0001f);
        }

        // --- Damage falloff bands ---

        [Test]
        public void GetZoneDamage_BandBoundaries()
        {
            WeaponData weapon = MakeJudge();

            Assert.AreEqual(34f, DamageFalloff.GetZoneDamage(weapon, 0f, HitZone.Head), 0.0001f);
            Assert.AreEqual(17f, DamageFalloff.GetZoneDamage(weapon, 0f, HitZone.Body), 0.0001f);
            Assert.AreEqual(14f, DamageFalloff.GetZoneDamage(weapon, 0f, HitZone.Leg), 0.0001f);

            // 10 m is still band 1; just past it is band 2.
            Assert.AreEqual(34f, DamageFalloff.GetZoneDamage(weapon, 10f, HitZone.Head), 0.0001f);
            Assert.AreEqual(20f, DamageFalloff.GetZoneDamage(weapon, 10.01f, HitZone.Head), 0.0001f);
            Assert.AreEqual(10f, DamageFalloff.GetZoneDamage(weapon, 10.01f, HitZone.Body), 0.0001f);
            Assert.AreEqual(8f, DamageFalloff.GetZoneDamage(weapon, 10.01f, HitZone.Leg), 0.0001f);
            Assert.AreEqual(20f, DamageFalloff.GetZoneDamage(weapon, 15f, HitZone.Head), 0.0001f);

            Assert.AreEqual(14f, DamageFalloff.GetZoneDamage(weapon, 25f, HitZone.Head), 0.0001f);
            Assert.AreEqual(7f, DamageFalloff.GetZoneDamage(weapon, 25f, HitZone.Body), 0.0001f);
            Assert.AreEqual(5f, DamageFalloff.GetZoneDamage(weapon, 25f, HitZone.Leg), 0.0001f);

            // Beyond the last band the last band keeps applying (no range cutoff).
            Assert.AreEqual(14f, DamageFalloff.GetZoneDamage(weapon, 50f, HitZone.Head), 0.0001f);
            Assert.AreEqual(14f, DamageFalloff.GetZoneDamage(weapon, 400f, HitZone.Head), 0.0001f);
        }

        [Test]
        public void GetZoneDamage_EmptyBands_MatchesLegacyMultipliers()
        {
            WeaponData weapon = ScriptableObject.CreateInstance<WeaponData>();
            weapon.headDamage = 40f;
            weapon.perfectMultiplier = 1f;
            weapon.mediumMultiplier = 0.25f;
            weapon.lowMultiplier = 0.21f;

            Assert.AreEqual(40f * 1f, DamageFalloff.GetZoneDamage(weapon, 12f, HitZone.Head));
            Assert.AreEqual(40f * 0.25f, DamageFalloff.GetZoneDamage(weapon, 12f, HitZone.Body));
            Assert.AreEqual(40f * 0.21f, DamageFalloff.GetZoneDamage(weapon, 12f, HitZone.Leg));
        }

        // --- Spread world radius & distance projection (crosshair circle) ---

        [Test]
        public void ComputeSpreadWorldRadius_MatchesDistanceTanProjection()
        {
            // Exponent 1 = pure angular cone: radius is distance · tan(spread).
            Assert.AreEqual(10f * Mathf.Tan(4f * Mathf.Deg2Rad),
                SpreadCalculator.ComputeSpreadWorldRadiusMeters(10f, 4f, 1f), 0.0001f);
            Assert.AreEqual(0f, SpreadCalculator.ComputeSpreadWorldRadiusMeters(0f, 4f, 1f), 0.0001f);
            Assert.AreEqual(0f, SpreadCalculator.ComputeSpreadWorldRadiusMeters(10f, 0f, 1f), 0.0001f);
        }

        [Test]
        public void ProjectionExponentOne_MatchesLegacyDistanceTan()
        {
            // Every existing weapon uses exponent 1; the 4-arg overload must be
            // bit-identical to the legacy 3-arg projection.
            Vector3 shooter = new Vector3(1f, 0f, 2f);
            Vector2 offset = new Vector2(3f, -2f);
            foreach (float distance in new[] { 0.5f, 5f, 10f, 40f })
            {
                Vector3 aim = shooter + new Vector3(distance, 0f, 0f);
                Assert.AreEqual(SpreadCalculator.ApplyOffsetToAimPoint(shooter, aim, offset),
                    SpreadCalculator.ApplyOffsetToAimPoint(shooter, aim, offset, 1f));
            }
        }

        [Test]
        public void ProjectionExponentHalf_GrowsWithSqrtDistance()
        {
            // At the 10 m reference the pattern matches the pure cone exactly...
            Assert.AreEqual(10f * Mathf.Tan(8f * Mathf.Deg2Rad),
                SpreadCalculator.ComputeSpreadWorldRadiusMeters(10f, 8f, 0.5f), 0.0001f);

            // ...and 4× the distance only doubles the radius (√d, not linear).
            float at10 = SpreadCalculator.ComputeSpreadWorldRadiusMeters(10f, 8f, 0.5f);
            float at40 = SpreadCalculator.ComputeSpreadWorldRadiusMeters(40f, 8f, 0.5f);
            Assert.AreEqual(2f * at10, at40, 0.0001f);
        }

        [Test]
        public void ComputeSpreadWorldRadius_BoundsEveryPelletLanding()
        {
            // The crosshair draws this radius around the aim point; every pellet
            // ApplyOffsetToAimPoint can produce must land inside it — including
            // under the Judge's √d pattern projection.
            WeaponData weapon = MakeJudge();
            Vector3 shooter = new Vector3(3f, 0f, -2f);
            Vector3 aimPoint = new Vector3(11f, 0f, 4f);
            float xzDistance = new Vector2(aimPoint.x - shooter.x, aimPoint.z - shooter.z).magnitude;

            for (int pull = 0; pull < 50; pull++)
            {
                float spreadDegrees = SpreadCalculator.ComputeSpreadDegrees(weapon, pull % 5,
                    false, false, MovementState.Running);
                float radius = SpreadCalculator.ComputeSpreadWorldRadiusMeters(xzDistance, spreadDegrees,
                    weapon.spreadDistanceExponent);
                Vector2[] offsets = SpreadCalculator.ComputePelletOffsetsDegrees(weapon, pull % 5,
                    false, false, MovementState.Running, Seed, pull * PelletCount, PelletCount);

                foreach (Vector2 offset in offsets)
                {
                    Vector3 landing = SpreadCalculator.ApplyOffsetToAimPoint(shooter, aimPoint, offset,
                        weapon.spreadDistanceExponent);
                    float miss = new Vector2(landing.x - aimPoint.x, landing.z - aimPoint.z).magnitude;
                    Assert.LessOrEqual(miss, radius + 0.0001f);
                }
            }
        }

        // --- Classic alt-fire: spread column routing ---

        [Test]
        public void ClassicAlt_FirstShot_UsesAltColumn()
        {
            WeaponData weapon = MakeClassic();

            Assert.AreEqual(1.9f, SpreadCalculator.ComputeSpreadDegrees(weapon, 0f, false, true, MovementState.Stationary), 0.0001f);
            Assert.AreEqual(1.71f, SpreadCalculator.ComputeSpreadDegrees(weapon, 0f, true, true, MovementState.Stationary), 0.0001f);

            Assert.AreEqual(0.4f, SpreadCalculator.ComputeSpreadDegrees(weapon, 0f, false, false, MovementState.Stationary), 0.0001f);
            Assert.AreEqual(0.3f, SpreadCalculator.ComputeSpreadDegrees(weapon, 0f, true, false, MovementState.Stationary), 0.0001f);
        }

        [Test]
        public void ClassicAlt_GrowsPerBurst_CapsAtAltMax()
        {
            WeaponData weapon = MakeClassic();

            Assert.AreEqual(1.9f, SpreadCalculator.ComputeSpreadDegrees(weapon, 0f, false, true, MovementState.Stationary), 0.0001f);
            Assert.AreEqual(3.1933f, SpreadCalculator.ComputeSpreadDegrees(weapon, 1f, false, true, MovementState.Stationary), 0.001f);
            Assert.AreEqual(4.4867f, SpreadCalculator.ComputeSpreadDegrees(weapon, 2f, false, true, MovementState.Stationary), 0.001f);
            Assert.AreEqual(5.78f, SpreadCalculator.ComputeSpreadDegrees(weapon, 3f, false, true, MovementState.Stationary), 0.001f);
            Assert.AreEqual(5.78f, SpreadCalculator.ComputeSpreadDegrees(weapon, 9f, false, true, MovementState.Stationary), 0.0001f);

            Assert.AreEqual(5.21f, SpreadCalculator.ComputeSpreadDegrees(weapon, 3f, true, true, MovementState.Stationary), 0.0001f);
        }

        [Test]
        public void ClassicPrimary_GrowsPerShot_CapsAtPrimaryMax()
        {
            WeaponData weapon = MakeClassic();

            Assert.AreEqual(0.4f, SpreadCalculator.ComputeSpreadDegrees(weapon, 0f, false, false, MovementState.Stationary), 0.0001f);
            Assert.AreEqual(1.8f, SpreadCalculator.ComputeSpreadDegrees(weapon, 4f, false, false, MovementState.Stationary), 0.0001f);
            Assert.AreEqual(1.8f, SpreadCalculator.ComputeSpreadDegrees(weapon, 9f, false, false, MovementState.Stationary), 0.0001f);
            Assert.AreEqual(1.35f, SpreadCalculator.ComputeSpreadDegrees(weapon, 3f, true, false, MovementState.Stationary), 0.0001f);
        }

        /// <summary>
        /// The shotgun alt owns its movement penalties outright. Each is asserted
        /// to DIFFER from the primary penalty at the same movement state, so
        /// wiring the shared hip-fire penalties by mistake fails here.
        /// </summary>
        [Test]
        public void ClassicAlt_MovementPenalties_AreTheAltColumn()
        {
            WeaponData weapon = MakeClassic();

            Assert.AreEqual(1.71f + 0f, SpreadCalculator.ComputeSpreadDegrees(weapon, 0f, true, true, MovementState.CrouchWalking), 0.0001f);
            Assert.AreEqual(1.9f + 0.6f, SpreadCalculator.ComputeSpreadDegrees(weapon, 0f, false, true, MovementState.Walking), 0.0001f);
            Assert.AreEqual(1.9f + 1.5f, SpreadCalculator.ComputeSpreadDegrees(weapon, 0f, false, true, MovementState.Running), 0.0001f);
            Assert.AreEqual(1.9f + 2.25f, SpreadCalculator.ComputeSpreadDegrees(weapon, 0f, false, true, MovementState.Airborne), 0.0001f);

            foreach (MovementState state in new[]
            {
                MovementState.CrouchWalking, MovementState.Walking,
                MovementState.Running, MovementState.Airborne,
            })
            {
                bool crouched = state == MovementState.CrouchWalking;
                float altPenalty = SpreadCalculator.ComputeSpreadDegrees(weapon, 0f, crouched, true, state)
                    - SpreadCalculator.ComputeSpreadDegrees(weapon, 0f, crouched, true, MovementState.Stationary);
                float hipPenalty = SpreadCalculator.ComputeSpreadDegrees(weapon, 0f, crouched, false, state)
                    - SpreadCalculator.ComputeSpreadDegrees(weapon, 0f, crouched, false, MovementState.Stationary);
                Assert.AreNotEqual(hipPenalty, altPenalty, state + " must use the alt penalty, not the hip one");
            }
        }

        [Test]
        public void ClassicPrimary_UnaffectedByAltColumn()
        {
            WeaponData weapon = MakeClassic();

            Assert.AreEqual(0.3f + 0.5f, SpreadCalculator.ComputeSpreadDegrees(weapon, 0f, true, false, MovementState.CrouchWalking), 0.0001f);
            Assert.AreEqual(0.4f + 1.1f, SpreadCalculator.ComputeSpreadDegrees(weapon, 0f, false, false, MovementState.Walking), 0.0001f);
            Assert.AreEqual(0.4f + 2.3f, SpreadCalculator.ComputeSpreadDegrees(weapon, 0f, false, false, MovementState.Running), 0.0001f);
            Assert.AreEqual(0.4f + 7f, SpreadCalculator.ComputeSpreadDegrees(weapon, 0f, false, false, MovementState.Airborne), 0.0001f);
        }

        /// <summary>
        /// Regression lock on the two-type split: an ADS alt-fire takes only the
        /// first/max values from the alt column and keeps the primary's growth
        /// and movement penalties, so poisoning the Shotgun-only fields must
        /// change nothing for a rifle.
        /// </summary>
        [Test]
        public void AdsWeapon_IgnoresAltGrowthAndAltMovePenalties()
        {
            WeaponData clean = MakeAdsRifle();
            WeaponData poisoned = MakeAdsRifle();
            poisoned.altSpreadPerShotDegrees = 99f;
            poisoned.altMovePenaltyCrouchWalk = 99f;
            poisoned.altMovePenaltyWalk = 99f;
            poisoned.altMovePenaltyRun = 99f;
            poisoned.altMovePenaltyAirborne = 99f;

            foreach (MovementState state in System.Enum.GetValues(typeof(MovementState)))
            {
                for (float spray = 0f; spray <= 4f; spray += 1f)
                {
                    Assert.AreEqual(
                        SpreadCalculator.ComputeSpreadDegrees(clean, spray, false, true, state),
                        SpreadCalculator.ComputeSpreadDegrees(poisoned, spray, false, true, state),
                        0.0001f, state + " at spray " + spray);
                }
            }
        }

        [Test]
        public void AltFlag_OnWeaponWithoutAltFire_IsIgnored()
        {
            WeaponData weapon = MakeJudge(); // altFireType stays None

            Assert.AreEqual(
                SpreadCalculator.ComputeSpreadDegrees(weapon, 2f, false, false, MovementState.Walking),
                SpreadCalculator.ComputeSpreadDegrees(weapon, 2f, false, true, MovementState.Walking),
                0.0001f);
        }

        // --- Classic alt-fire: pellets ---

        [Test]
        public void ClassicAlt_ThreePellets_AllDistinct_AllInsideAltCone()
        {
            WeaponData weapon = MakeClassic();
            float cone = SpreadCalculator.ComputeSpreadDegrees(weapon, 0f, false, true, MovementState.Running);

            for (int pull = 0; pull < 100; pull++)
            {
                Vector2[] offsets = SpreadCalculator.ComputePelletOffsetsDegrees(weapon, 0f, false, true,
                    MovementState.Running, Seed, pull * ClassicAltPellets, ClassicAltPellets);

                Assert.AreEqual(ClassicAltPellets, offsets.Length);
                Assert.AreNotEqual(offsets[0], offsets[1]);
                Assert.AreNotEqual(offsets[1], offsets[2]);
                Assert.AreNotEqual(offsets[0], offsets[2]);
                foreach (Vector2 offset in offsets) Assert.LessOrEqual(offset.magnitude, cone + 0.0001f);
            }
        }

        /// <summary>
        /// Every pellet of one pull shares the pull's recoil vector and differs
        /// only by its disc roll — which is why the Classic carries a small
        /// recoilClimbDegrees: a large one would shove the whole burst backward
        /// as a rigid group rather than scattering it.
        /// </summary>
        [Test]
        public void ClassicAlt_PelletsShareOneRecoilVector()
        {
            WeaponData weapon = MakeClassic();
            weapon.recoilClimbDegrees = 3f;
            weapon.altFirstShotSpreadStanding = 0f;
            weapon.altMaxSpreadStanding = 0f;

            Vector2[] offsets = SpreadCalculator.ComputePelletOffsetsDegrees(weapon, 2f, false, true,
                MovementState.Stationary, Seed, 0, ClassicAltPellets);
            Vector2 recoil = SpreadCalculator.ComputeRecoilDegrees(weapon, 2f);

            foreach (Vector2 offset in offsets)
            {
                Assert.AreEqual(recoil.x, offset.x, 0.0001f);
                Assert.AreEqual(recoil.y, offset.y, 0.0001f);
            }
        }

        // --- Classic damage bands (shared by both fire modes) ---

        [Test]
        public void ClassicDamage_BandBoundaries()
        {
            WeaponData weapon = MakeClassic();

            Assert.AreEqual(78f, DamageFalloff.GetZoneDamage(weapon, 0f, HitZone.Head), 0.0001f);
            Assert.AreEqual(26f, DamageFalloff.GetZoneDamage(weapon, 30f, HitZone.Body), 0.0001f);
            Assert.AreEqual(22f, DamageFalloff.GetZoneDamage(weapon, 30f, HitZone.Leg), 0.0001f);

            Assert.AreEqual(66f, DamageFalloff.GetZoneDamage(weapon, 30.01f, HitZone.Head), 0.0001f);
            Assert.AreEqual(22f, DamageFalloff.GetZoneDamage(weapon, 50f, HitZone.Body), 0.0001f);
            // Past the last band it keeps applying — range never zeroes a hit.
            Assert.AreEqual(18f, DamageFalloff.GetZoneDamage(weapon, 400f, HitZone.Leg), 0.0001f);
        }

        [Test]
        public void ClassicAlt_BurstDamage_IsThreeTimesOnePellet()
        {
            WeaponData weapon = MakeClassic();

            Assert.AreEqual(234f, ClassicAltPellets * DamageFalloff.GetZoneDamage(weapon, 5f, HitZone.Head), 0.0001f);
            Assert.AreEqual(78f, ClassicAltPellets * DamageFalloff.GetZoneDamage(weapon, 5f, HitZone.Body), 0.0001f);
        }

        // --- FireModeStats ---

        [Test]
        public void Interval_PrimaryVsAlt()
        {
            WeaponData weapon = MakeClassic();

            Assert.AreEqual(1f / 6.75f, FireModeStats.IntervalSeconds(weapon, false, false), 0.0001f);
            Assert.AreEqual(1f / 2.22f, FireModeStats.IntervalSeconds(weapon, true, false), 0.0001f);
        }

        [Test]
        public void Interval_AltFlagOnNonShotgunWeapon_FallsBackToPrimary()
        {
            WeaponData rifle = MakeAdsRifle();

            Assert.AreEqual(FireModeStats.IntervalSeconds(rifle, false, false),
                FireModeStats.IntervalSeconds(rifle, true, false), 0.0001f);
        }

        [Test]
        public void Interval_AdsMultiplier_StillApplies()
        {
            WeaponData rifle = MakeAdsRifle();
            Assert.AreEqual(1f / (5.4f * 0.9f), FireModeStats.IntervalSeconds(rifle, false, true), 0.0001f);

            // The Classic has no ADS, so an ads flag must not touch its cadence.
            WeaponData classic = MakeClassic();
            Assert.AreEqual(1f / 6.75f, FireModeStats.IntervalSeconds(classic, false, true), 0.0001f);
        }

        [Test]
        public void RequiredGap_TakesTheLongerInterval()
        {
            float primary = 1f / 6.75f;
            float alt = 1f / 2.22f;

            Assert.AreEqual(alt, FireModeStats.RequiredGapSeconds(alt, primary), 0.0001f);
            Assert.AreEqual(alt, FireModeStats.RequiredGapSeconds(primary, alt), 0.0001f);
            Assert.AreEqual(primary, FireModeStats.RequiredGapSeconds(primary, primary), 0.0001f);
        }

        /// <summary>
        /// The reason primary and alt share one fire timer. Exhaustively walks
        /// every 10-pull sequence of the two modes through the same gap rule the
        /// client and server use, and asserts no mix out-damages simply picking
        /// the better single mode. Per-mode timers would let a player run both
        /// cadences at once and fail this outright. Magazine size is deliberately
        /// not modelled — this tests the cadence gate, not the ammo gate.
        /// </summary>
        [Test]
        public void InterleavedFireModes_CannotBeatSingleModeDps()
        {
            WeaponData weapon = MakeClassic();
            const int pulls = 10;

            float pureAlt = BestDps(weapon, pulls, allowAlt: true, allowPrimary: false);
            float purePrimary = BestDps(weapon, pulls, allowAlt: false, allowPrimary: true);
            float mixed = BestDps(weapon, pulls, allowAlt: true, allowPrimary: true);

            Assert.LessOrEqual(mixed, Mathf.Max(pureAlt, purePrimary) + 0.0001f,
                "interleaving fire modes must not beat the better single mode");
        }

        private static float BestDps(WeaponData weapon, int pulls, bool allowAlt, bool allowPrimary)
            => BestDps(weapon, pulls, 0f, 0f, 0f, allowAlt, allowPrimary);

        private static float BestDps(WeaponData weapon, int pullsLeft, float elapsed,
            float lastInterval, float damage, bool allowAlt, bool allowPrimary)
        {
            if (pullsLeft == 0) return elapsed > 0f ? damage / elapsed : 0f;

            float best = 0f;
            if (allowAlt) best = Mathf.Max(best, StepDps(weapon, pullsLeft, elapsed, lastInterval, damage, true, allowAlt, allowPrimary));
            if (allowPrimary) best = Mathf.Max(best, StepDps(weapon, pullsLeft, elapsed, lastInterval, damage, false, allowAlt, allowPrimary));
            return best;
        }

        private static float StepDps(WeaponData weapon, int pullsLeft, float elapsed, float lastInterval,
            float damage, bool altShot, bool allowAlt, bool allowPrimary)
        {
            float interval = FireModeStats.IntervalSeconds(weapon, altShot, false);
            float gap = FireModeStats.RequiredGapSeconds(lastInterval, interval);
            float pullDamage = FireModeStats.MaxPelletCount(weapon, altShot)
                * DamageFalloff.GetZoneDamage(weapon, 0f, HitZone.Body);
            return BestDps(weapon, pullsLeft - 1, elapsed + gap, interval, damage + pullDamage,
                allowAlt, allowPrimary);
        }

        [Test]
        public void MaxPelletCount_AltVsPrimary()
        {
            WeaponData classic = MakeClassic();
            Assert.AreEqual(1, FireModeStats.MaxPelletCount(classic, false));
            Assert.AreEqual(ClassicAltPellets, FireModeStats.MaxPelletCount(classic, true));

            // The Judge is a shotgun in every mode and has no alt-fire, so the
            // alt flag must not change what one pull throws.
            WeaponData judge = MakeJudge();
            Assert.AreEqual(PelletCount, FireModeStats.MaxPelletCount(judge, false));
            Assert.AreEqual(PelletCount, FireModeStats.MaxPelletCount(judge, true));
        }

        [Test]
        public void SpreadDistanceExponent_AltVsPrimary()
        {
            WeaponData classic = MakeClassic();
            Assert.AreEqual(1f, FireModeStats.SpreadDistanceExponent(classic, false), 0.0001f);
            Assert.AreEqual(0.5f, FireModeStats.SpreadDistanceExponent(classic, true), 0.0001f);

            WeaponData rifle = MakeAdsRifle();
            Assert.AreEqual(1f, FireModeStats.SpreadDistanceExponent(rifle, false), 0.0001f);
            Assert.AreEqual(1f, FireModeStats.SpreadDistanceExponent(rifle, true), 0.0001f);
        }

        // --- Spray decay interaction with the two cadences ---

        /// <summary>
        /// Documents a deliberate consequence of the shipped tuning: the gap
        /// between alt bursts (0.45 s) far exceeds the decay grace, so the spray
        /// index is back to zero before every burst. Pure right-click play sits
        /// permanently at first-shot spread, and altMaxSpread only bites a player
        /// who was just spraying primary. sprayDecayDelay is the knob that
        /// changes this.
        /// </summary>
        [Test]
        public void ClassicAlt_ConsecutiveBursts_DecayFullyResetsSprayIndex()
        {
            WeaponData weapon = MakeClassic();
            float altGap = 1f / weapon.altFireRate;

            Assert.AreEqual(0f, SpreadCalculator.DecaySprayIndex(1f, altGap,
                weapon.sprayDecayDelay, weapon.sprayDecayPerSecond), 0.0001f);
        }

        /// <summary>
        /// Why the Classic carries sprayDecayDelay 0.2 rather than the usual
        /// 0.15: its 6.75/s gap is 0.148 s, only 1.3 ms inside the shorter grace,
        /// so frame timing would decide whether a spray grew at all.
        /// </summary>
        [Test]
        public void ClassicPrimary_MaxCadence_DoesNotDecay()
        {
            WeaponData weapon = MakeClassic();
            float primaryGap = 1f / weapon.fireRate;

            Assert.AreEqual(3f, SpreadCalculator.DecaySprayIndex(3f, primaryGap,
                weapon.sprayDecayDelay, weapon.sprayDecayPerSecond), 0.0001f);
            // One frame of jitter past the old 0.15 s grace would have bled the spray.
            Assert.Less(SpreadCalculator.DecaySprayIndex(3f, primaryGap + 0.017f, 0.15f, 15f), 3f);
        }
    }
}
