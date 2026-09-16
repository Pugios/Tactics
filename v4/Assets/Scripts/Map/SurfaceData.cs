using UnityEngine;

namespace Tactics.Map
{
    /// <summary>
    /// What one Blender material means in the game. The asset's own name is the
    /// match key: a face painted with the Blender material "Stone" resolves to
    /// Stone.asset, which says how far a bullet gets through it and whether the
    /// spike can be planted on it.
    ///
    /// Materials with no asset are not an error: the importer falls back to
    /// Ground for up-facing faces and Wall for the rest, so a blockout works
    /// before anything is configured.
    ///
    /// A surface never decides how a face LOOKS. Every face keeps the material
    /// Blender exported, untouched, so the look is authored once and Unity
    /// holds no second copy of it.
    ///
    /// Keeping "how far" here and "how much damage survives that distance" in
    /// <see cref="Tactics.Weapons.WallPenetration"/> is the split Valorant's
    /// measured behaviour follows: a denser material does not dull the damage
    /// curve, it shortens the distance over which the same curve plays out.
    /// </summary>
    [CreateAssetMenu(fileName = "NewSurface", menuName = "Tactics/Surface")]
    public class SurfaceData : ScriptableObject
    {
        [Tooltip("Meters of this material a bullet may cross before it is stopped.")]
        public float maxTravelMeters = 0.8f;

        [Tooltip("Standing on a face of this surface allows planting the spike.")]
        public bool isPlantSite;
    }
}
