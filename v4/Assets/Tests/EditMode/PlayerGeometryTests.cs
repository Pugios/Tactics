using NUnit.Framework;
using Tactics.Player;

namespace Tactics.Tests.EditMode
{
    public class PlayerGeometryTests
    {
        // The Player prefab as authored: capsule centred on the transform, so the
        // origin is the waist, and body heights measured from the soles.
        private const float StandingHeight = 2f;
        private const float StandingCenterY = 0f;
        private const float StandingEye = 1.5f;

        // PlayerMovementNetwork.ApplyCrouchCollider shrinks the capsule upward,
        // keeping its bottom where it was: centre moves to -1 + 1.5/2.
        private const float CrouchHeight = 1.5f;
        private const float CrouchCenterY = -0.25f;
        private const float CrouchEye = 1f;

        [Test]
        public void Standing_OriginSitsAtTheWaist()
        {
            Assert.AreEqual(1f, PlayerGeometry.FeetToOrigin(StandingHeight, StandingCenterY), 0.0001f);
        }

        [Test]
        public void EyeOffset_MatchesTheVisionOriginOnThePrefab()
        {
            // VisionOrigin's local position on Player.prefab is (0, 0.5, 0); the
            // arithmetic has to agree with it or the eye and the shot disagree.
            Assert.AreEqual(0.5f,
                PlayerGeometry.EyeOffsetFromOrigin(StandingHeight, StandingCenterY, StandingEye), 0.0001f);
        }

        [Test]
        public void Crouching_KeepsTheFeetPlanted()
        {
            // The capsule shrinks upward, so the drop to the soles never changes.
            Assert.AreEqual(1f, PlayerGeometry.FeetToOrigin(CrouchHeight, CrouchCenterY), 0.0001f);
            Assert.AreEqual(0f,
                PlayerGeometry.EyeOffsetFromOrigin(CrouchHeight, CrouchCenterY, CrouchEye), 0.0001f);
        }

        [Test]
        public void EyeHeightIsNotAnOriginOffset()
        {
            // The bug this class exists to prevent: using the feet-relative eye
            // height straight off the origin puts the shot a metre above the eyes.
            float wrong = StandingEye;
            float right = PlayerGeometry.EyeOffsetFromOrigin(StandingHeight, StandingCenterY, StandingEye);
            Assert.AreEqual(1f, wrong - right, 0.0001f);
        }

        [Test]
        public void ControllerAuthoredAtTheFeet_NeedsNoCorrection()
        {
            // centre y = half height puts the capsule bottom on the origin.
            Assert.AreEqual(0f, PlayerGeometry.FeetToOrigin(2f, 1f), 0.0001f);
            Assert.AreEqual(StandingEye, PlayerGeometry.EyeOffsetFromOrigin(2f, 1f, StandingEye), 0.0001f);
        }
    }
}
