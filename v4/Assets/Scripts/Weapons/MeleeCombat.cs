using UnityEngine;

namespace Tactics.Weapons
{
    /// <summary>
    /// Melee swing geometry and damage, pure logic with no Unity scene state
    /// (mirrors SpreadCalculator / DamageFalloff / FireModeStats).
    ///
    /// A swing shares nothing with the bullet path: no spread, no pellets, no
    /// distance falloff, no wall penetration. It reaches a fixed distance
    /// straight ahead, and the only modifier is which side of the target it
    /// landed on — a hit from the target's rear hemisphere does the back number.
    /// Everything here works on the XZ plane, like the rest of the damage model.
    /// </summary>
    public static class MeleeCombat
    {
        /// <summary>
        /// Whether the target is inside the swing volume, and if so how far
        /// ahead of the attacker it sits (the tiebreaker for picking the
        /// nearest victim).
        ///
        /// The volume is a box rather than a capsule: the component along the
        /// attacker's forward must fall within [0, range] and the sideways
        /// component within halfWidth. A capsule would round off the far end
        /// and let the swing reach past `range` to a target dead ahead.
        /// Callers pass the target's body radius as halfWidth, so this asks
        /// "does the target's body overlap the line I'm facing down".
        /// </summary>
        public static bool TryGetForwardDistance(Vector3 attackerPos, Vector3 attackerForward,
            Vector3 targetPos, float range, float halfWidth, out float forwardDistance)
        {
            forwardDistance = 0f;

            Vector2 forward = new Vector2(attackerForward.x, attackerForward.z);
            if (forward.sqrMagnitude < 0.0001f) return false;
            forward.Normalize();

            Vector2 toTarget = new Vector2(targetPos.x - attackerPos.x, targetPos.z - attackerPos.z);

            float along = Vector2.Dot(toTarget, forward);
            if (along < 0f || along > range) return false;

            // Perpendicular distance from the forward axis.
            float lateral = Mathf.Abs(toTarget.x * forward.y - toTarget.y * forward.x);
            if (lateral > halfWidth) return false;

            forwardDistance = along;
            return true;
        }

        /// <summary>
        /// Whether the attacker struck the target's back — true when the
        /// attacker stands anywhere in the target's rear hemisphere, i.e.
        /// behind the plane through its shoulders. The sides count as a front
        /// hit; exactly perpendicular is a front hit.
        /// </summary>
        public static bool IsBackHit(Vector3 attackerPos, Vector3 targetPos, float targetYawDegrees)
        {
            Vector2 toTarget = new Vector2(targetPos.x - attackerPos.x, targetPos.z - attackerPos.z);
            if (toTarget.sqrMagnitude < 0.0001f) return false; // co-located: no meaningful side

            float yawRadians = targetYawDegrees * Mathf.Deg2Rad;
            Vector2 targetForward = new Vector2(Mathf.Sin(yawRadians), Mathf.Cos(yawRadians));

            // Attacker → target pointing the same way the target faces means
            // the attacker is standing behind it.
            return Vector2.Dot(targetForward, toTarget.normalized) > 0f;
        }

        /// <summary>Damage one swing deals, by fire mode and which side it landed on.</summary>
        public static float GetDamage(WeaponData weapon, bool altShot, bool backHit)
        {
            if (weapon == null) return 0f;
            if (altShot) return backHit ? weapon.altMeleeBackDamage : weapon.altMeleeFrontDamage;
            return backHit ? weapon.meleeBackDamage : weapon.meleeFrontDamage;
        }
    }
}
