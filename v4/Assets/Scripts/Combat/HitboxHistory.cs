using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Tactics.Combat
{
    /// <summary>
    /// Server-side ring buffer of this entity's authoritative position over the
    /// last ~second, sampled once per network tick. Lag compensation rewinds hit
    /// resolution through <see cref="GetPositionAt"/> so shots are judged against
    /// where the shooter actually saw the target, not where the server has moved
    /// it since. Records nothing on clients.
    /// </summary>
    public class HitboxHistory : NetworkBehaviour
    {
        /// <summary>Every server-side instance, for hit resolution to iterate.</summary>
        public static readonly List<HitboxHistory> All = new List<HitboxHistory>();

        private const int Capacity = 64; // > 2s at 30Hz

        private struct Sample
        {
            public float Time;
            public Vector3 Position;
        }

        private readonly List<Sample> samples = new List<Sample>();

        private Tactics.Player.PlayerMovementNetwork movement;
        private Health health;

        public Health Health => health;

        private void Awake()
        {
            movement = GetComponent<Tactics.Player.PlayerMovementNetwork>();
            health = GetComponent<Health>();
        }

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;
            All.Add(this);
            NetworkManager.NetworkTickSystem.Tick += OnServerTick;
            Record();
        }

        public override void OnNetworkDespawn()
        {
            All.Remove(this);
            if (NetworkManager != null && NetworkManager.NetworkTickSystem != null)
                NetworkManager.NetworkTickSystem.Tick -= OnServerTick;
            samples.Clear();
        }

        private void OnServerTick() => Record();

        private void Record()
        {
            samples.Add(new Sample { Time = Time.time, Position = CurrentPosition });
            if (samples.Count > Capacity) samples.RemoveAt(0);
        }

        /// <summary>
        /// Wipes the history and reseeds it at the current position. Called after
        /// hard teleports (respawn) so rewound shots can't hit the corpse spot a
        /// player no longer occupies.
        /// </summary>
        public void ServerReset()
        {
            if (!IsServer) return;
            samples.Clear();
            Record();
        }

        /// <summary>Authoritative position at the given server-local Time.time, clamped to the buffer.</summary>
        public Vector3 GetPositionAt(float time)
        {
            if (samples.Count == 0) return CurrentPosition;
            if (time >= samples[samples.Count - 1].Time) return samples[samples.Count - 1].Position;
            if (time <= samples[0].Time) return samples[0].Position;

            // Rewinds are short, so walk from the newest sample backwards.
            for (int i = samples.Count - 1; i > 0; i--)
            {
                Sample older = samples[i - 1];
                if (older.Time > time) continue;

                Sample newer = samples[i];
                float span = newer.Time - older.Time;
                if (span <= 0f) return newer.Position;
                return Vector3.Lerp(older.Position, newer.Position, (time - older.Time) / span);
            }

            return samples[0].Position;
        }

        // The player's transform doubles as a smoothed view on the server, so the
        // sim's published snapshot position is the authoritative one; everything
        // else (dummies) is driven by its transform directly.
        private Vector3 CurrentPosition => movement != null ? movement.AuthoritativePosition : transform.position;
    }
}
