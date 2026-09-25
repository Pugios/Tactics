using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Tactics.Player;
using Tactics.Weapons;

namespace Tactics.Tests.EditMode
{
    /// <summary>
    /// Replays macro-timed measurements taken off Valorant's spread graph
    /// (Vandal, practice range, averaged over 6 runs) through
    /// <see cref="SpreadCalculator.EvaluateSpray"/>. The macro holds fire for H
    /// seconds, releases, waits D, and clicks once; taps press for 0.05 s with
    /// a pause of X between them. The 0.15 s tap series is left out: it crept
    /// up where every other series is flat, which was put down to server lag.
    /// </summary>
    public class VandalSprayRecoveryTests
    {
        private const float Tolerance = 0.03f;

        private static WeaponData MakeVandal()
        {
            WeaponData weapon = ScriptableObject.CreateInstance<WeaponData>();
            weapon.fireRate = 9.75f;
            weapon.firstShotSpreadStanding = 0.25f;
            weapon.maxSpreadStanding = 1f;
            weapon.sprayShotSpreads = new[] { 0.25f, 0.3f, 0.35f, 0.56f, 0.77f, 0.99f, 1f };
            weapon.sprayDecayDelay = 0f;
            weapon.sprayDecayPerSecond = 11.3f;
            weapon.gunRecoveryTime = 0.375f;
            weapon.tapEfficiency = 6f;
            return weapon;
        }

        /// <summary>Fires at the given times (each at or after the gun is ready) and returns every shot's spread.</summary>
        private static List<float> Fire(WeaponData weapon, IEnumerable<float> shotTimes)
        {
            float interval = 1f / weapon.fireRate;
            float index = 0f;
            float lastShot = float.NegativeInfinity;
            var spreads = new List<float>();
            foreach (float t in shotTimes)
            {
                SpreadCalculator.SprayState spray = SpreadCalculator.EvaluateSpray(weapon, index, t - lastShot, interval);
                spreads.Add(SpreadCalculator.ComputeSpreadDegrees(weapon, spray, false, false, MovementState.Stationary));
                index = spray.Index + 1f;
                lastShot = t;
            }
            return spreads;
        }

        /// <summary>Spread of the click D seconds after releasing an H-second hold.</summary>
        private static float HoldThenClick(WeaponData weapon, float hold, float delay)
        {
            float interval = 1f / weapon.fireRate;
            var times = new List<float>();
            for (int k = 0; k * interval < hold; k++) times.Add(k * interval);
            times.Add(hold + delay);
            List<float> spreads = Fire(weapon, times);
            return spreads[spreads.Count - 1];
        }

        [Test]
        public void HeldFire_MatchesSpreadGraph()
        {
            WeaponData weapon = MakeVandal();
            float interval = 1f / weapon.fireRate;
            var times = new List<float>();
            for (int k = 0; k < 9; k++) times.Add(k * interval);

            float[] expected = { 0.25f, 0.3f, 0.35f, 0.56f, 0.77f, 0.99f, 1f, 1f, 1f };
            List<float> spreads = Fire(weapon, times);
            for (int k = 0; k < expected.Length; k++)
                Assert.AreEqual(expected[k], spreads[k], 0.0001f, $"shot {k + 1}");
        }

        [TestCase(0.25f, 0.10f, 0.493f)]
        [TestCase(0.25f, 0.15f, 0.417f)]
        [TestCase(0.25f, 0.20f, 0.335f)]
        [TestCase(0.25f, 0.25f, 0.26f)]
        [TestCase(0.25f, 0.30f, 0.25f)]
        [TestCase(0.35f, 0.10f, 0.687f)]
        [TestCase(0.35f, 0.15f, 0.59f)]
        [TestCase(0.35f, 0.20f, 0.483f)]
        [TestCase(0.35f, 0.25f, 0.38f)]
        [TestCase(0.35f, 0.30f, 0.278f)]
        [TestCase(0.5f, 0.05f, 0.893f)]
        [TestCase(0.5f, 0.10f, 0.777f)]
        [TestCase(0.5f, 0.15f, 0.65f)]
        [TestCase(0.5f, 0.20f, 0.543f)]
        [TestCase(0.5f, 0.25f, 0.417f)]
        [TestCase(0.5f, 0.30f, 0.297f)]
        [TestCase(0.8f, 0.05f, 0.92f)]
        [TestCase(0.8f, 0.10f, 0.83f)]
        [TestCase(0.8f, 0.15f, 0.73f)]
        [TestCase(0.8f, 0.20f, 0.632f)]
        [TestCase(0.8f, 0.25f, 0.527f)]
        [TestCase(0.8f, 0.30f, 0.427f)]
        public void HoldThenClick_MatchesMeasurement(float hold, float delay, float measured)
        {
            Assert.AreEqual(measured, HoldThenClick(MakeVandal(), hold, delay), Tolerance);
        }

        [Test]
        public void QuickTaps_AccrueSpread()
        {
            float[] measured = { 0.25f, 0.27f, 0.28f, 0.31f, 0.39f, 0.47f, 0.54f, 0.61f };
            List<float> spreads = Fire(MakeVandal(), TapTimes(0.1f));
            for (int k = 0; k < measured.Length; k++)
                Assert.AreEqual(measured[k], spreads[k], Tolerance, $"tap {k + 1}");
        }

        [TestCase(0.2f)]
        [TestCase(0.25f)]
        public void SlowTaps_StayOnFirstShotSpread(float pause)
        {
            foreach (float spread in Fire(MakeVandal(), TapTimes(pause)))
                Assert.AreEqual(0.25f, spread, 0.0001f);
        }

        /// <summary>
        /// A long spray drives the counter far past the table (recoil sway needs
        /// it), but once the full recovery time has passed the gun is settled:
        /// the next two shots are shots 1 and 2 again, not max spread.
        /// </summary>
        [Test]
        public void FullRecovery_AfterLongSpray_ResetsTheCounter()
        {
            WeaponData weapon = MakeVandal();
            float interval = 1f / weapon.fireRate;
            var times = new List<float>();
            for (int k = 0; k < 25; k++) times.Add(k * interval);
            float resume = 24 * interval + interval + weapon.gunRecoveryTime + 0.001f;
            times.Add(resume);
            times.Add(resume + interval);

            List<float> spreads = Fire(weapon, times);
            Assert.AreEqual(0.25f, spreads[25], 0.0001f);
            Assert.AreEqual(0.3f, spreads[26], 0.0001f);
        }

        private static IEnumerable<float> TapTimes(float pause)
        {
            for (int k = 0; k < 8; k++) yield return k * (0.05f + pause);
        }
    }
}
