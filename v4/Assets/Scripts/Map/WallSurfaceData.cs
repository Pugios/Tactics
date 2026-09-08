using UnityEngine;

namespace Tactics.Map
{
    /// <summary>
    /// The physical make-up of a piece of wall geometry, shared by every object
    /// built from it. Exactly one number matters to the bullet path: how far a
    /// bullet may travel inside this material before it is stopped.
    ///
    /// Keeping "how far" here and "how much damage survives that distance" in
    /// <see cref="Tactics.Weapons.WallPenetration"/> is the split Valorant's
    /// measured behaviour follows. A denser material does not dull the damage
    /// curve; it shortens the distance over which the same curve plays out,
    /// which in practice narrows the angle at which a given wall can be pierced
    /// at all. So a surface is one number, and the weapon supplies the shape.
    ///
    /// Because the penetration ray is horizontal at eye height, this is a true
    /// horizontal thickness in meters — which is what makes it tunable by hand
    /// against the [Damage] log.
    /// </summary>
    [CreateAssetMenu(fileName = "NewWallSurface", menuName = "Tactics/Wall Surface")]
    public class WallSurfaceData : ScriptableObject
    {
        /// <summary>
        /// Matched case-insensitively against the underscore-separated tokens of
        /// a Blender object's name on import (Wall_Stone_01 → "Stone"). Blank
        /// falls back to the asset's own file name, so naming the asset Stone is
        /// normally all it takes.
        /// </summary>
        public string surfaceName;

        [Tooltip("Meters of this material a bullet may cross before it is stopped.")]
        public float maxTravelMeters = 0.8f;

        public string ResolvedName => string.IsNullOrWhiteSpace(surfaceName) ? name : surfaceName;
    }
}
