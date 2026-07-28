using System.Collections.Generic;
using UnityEngine;

namespace Tactics.Player
{
    public enum SpawnTeam
    {
        Attacker,
        Defender
    }

    /// <summary>
    /// Scene marker for a place a player can spawn/respawn. Placed per-map so each
    /// scene defines its own spawn layout instead of sharing the one hardcoded
    /// position baked into the Player prefab. Team is recorded for a future
    /// team-aware pick but selection currently draws from the combined pool of
    /// every marker in the scene, since players aren't assigned to a team yet.
    /// </summary>
    public class PlayerSpawnPoint : MonoBehaviour
    {
        [SerializeField] private SpawnTeam team;

        public SpawnTeam Team => team;

        private static readonly List<PlayerSpawnPoint> all = new List<PlayerSpawnPoint>();

        public static IReadOnlyList<PlayerSpawnPoint> All => all;

        private void Awake()
        {
            all.Add(this);
        }

        private void OnDestroy()
        {
            all.Remove(this);
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = team == SpawnTeam.Attacker ? Color.red : Color.cyan;
            Gizmos.DrawWireSphere(transform.position, 0.5f);
            Gizmos.DrawLine(transform.position, transform.position + transform.forward);
        }
    }
}
