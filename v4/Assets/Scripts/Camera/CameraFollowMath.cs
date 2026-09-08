using UnityEngine;

namespace Tactics.Camera
{
    /// <summary>
    /// Pure camera-follow and ADS cursor-compensation math. The real camera and
    /// the no-bias "shadow" camera both advance through <see cref="Step"/>, so
    /// equal inputs produce bit-identical outputs by construction — which is
    /// what lets TopDownCamera detect "no ADS displacement" as an exact zero
    /// delta instead of trusting float tolerances. No Unity scene state;
    /// deltaTime is explicit so edit-mode tests can drive it.
    /// </summary>
    public static class CameraFollowMath
    {
        /// <summary>
        /// Look weight for a zoom multiplier: the bias fraction is the
        /// tan-space share of view the zoom trades away (1 - 1/zoom), so
        /// deeper zoom pushes the framing further toward the aim point.
        /// Zoom &lt;= 1 returns the base weight unchanged.
        /// </summary>
        public static float TargetLookWeight(float baseWeight, float adsMaxWeight, float zoomMultiplier)
        {
            float bias = 1f - 1f / Mathf.Max(1f, zoomMultiplier);
            return Mathf.Lerp(baseWeight, adsMaxWeight, bias);
        }

        /// <summary>The SmoothDamp goal: Lerp(player, mouseWorld, weight) on XZ at the locked height.</summary>
        public static Vector3 FollowTarget(Vector3 playerPos, Vector3 mouseWorld, float lookWeight, float height)
        {
            Vector3 lookAtPoint = Vector3.Lerp(playerPos, mouseWorld, lookWeight);
            return new Vector3(lookAtPoint.x, height, lookAtPoint.z);
        }

        /// <summary>
        /// One camera-position step toward <see cref="FollowTarget"/>. maxSpeed
        /// must stay Infinity: SmoothDamp is then per-component, which is what
        /// guarantees the shared height input can never leak into the XZ bias
        /// delta (a finite clamp couples the components).
        /// </summary>
        public static Vector3 Step(Vector3 position, ref Vector3 velocity, Vector3 playerPos,
            Vector3 mouseWorld, float lookWeight, float height, float smoothTime, float deltaTime)
        {
            Vector3 target = FollowTarget(playerPos, mouseWorld, lookWeight, height);
            return Vector3.SmoothDamp(position, target, ref velocity, smoothTime, Mathf.Infinity, deltaTime);
        }

        /// <summary>
        /// The invariant aim point: the pre-move aim carried forward by only the
        /// baseline (shadow) camera motion — the ADS bias contributes nothing.
        /// Warping the cursor to this point's screen position each frame makes
        /// aim behave exactly as it would with no bias. Per-frame delta on the
        /// live cursor, never a standing offset: a standing offset re-applied to
        /// an already-compensated cursor runs away.
        /// </summary>
        public static Vector3 CompensatedAimPoint(Vector3 aimBeforeMove, Vector3 shadowPositionDelta)
            => aimBeforeMove + shadowPositionDelta;

        /// <summary>
        /// Max look weight that keeps the player inside the view rectangle:
        /// the camera centers on Lerp(player, mouse, w), so the player sits
        /// w * (player - mouse) from screen center. Constrained per
        /// camera-yaw-aligned axis against the view half extents (world meters
        /// on the player's ground plane) minus a margin for the model's size.
        /// Returns [0, 1]; 1 when unconstrained.
        /// </summary>
        public static float MaxLookWeightForVisibility(Vector3 playerPos, Vector3 mouseWorld,
            float yawDegrees, float halfWidth, float halfHeight, float marginMeters)
        {
            Vector3 d = playerPos - mouseWorld;
            float yawRad = yawDegrees * Mathf.Deg2Rad;
            float cos = Mathf.Cos(yawRad), sin = Mathf.Sin(yawRad);
            // World XZ offset in the camera's yaw frame: screen-horizontal is
            // the camera right axis, screen-vertical the yaw-forward axis.
            float dx = Mathf.Abs(cos * d.x - sin * d.z);
            float dz = Mathf.Abs(sin * d.x + cos * d.z);

            float allowedX = Mathf.Max(0f, halfWidth - marginMeters);
            float allowedZ = Mathf.Max(0f, halfHeight - marginMeters);

            float maxW = 1f;
            if (dx > 1e-5f) maxW = Mathf.Min(maxW, allowedX / dx);
            if (dz > 1e-5f) maxW = Mathf.Min(maxW, allowedZ / dz);
            return Mathf.Max(0f, maxW);
        }
    }
}
