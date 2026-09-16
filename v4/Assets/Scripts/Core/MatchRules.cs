using System;
using Unity.Netcode;
using UnityEngine;

namespace Tactics.Core
{
    /// <summary>
    /// Match-wide rules owned by the host: whether cheats are allowed at all, and
    /// which ones are on. Cheats belong to the match, not to a player, so they
    /// live in server-written NetworkVariables — everyone reads the same values,
    /// and only the host can change them (clients see the Cheats tab read-only).
    ///
    /// <see cref="allowCheats"/> is the switch the future lobby will set before a
    /// match starts; it defaults to on while the game is being tested. Turning it
    /// off disables every cheat regardless of its own toggle.
    ///
    /// The cheats themselves are read by systems that are still client-trusted
    /// (ammo, credits, the buy menu), so today each peer applies them to its own
    /// copy. Once those move to the server, the server-side checks should read
    /// these same properties.
    /// </summary>
    public class MatchRules : NetworkBehaviour
    {
        public static MatchRules Instance { get; private set; }

        /// <summary>Raised on every peer whenever any rule changes.</summary>
        public static event Action Changed;

        [Tooltip("Whether cheats may be used in this match. The lobby will decide this; on while testing.")]
        [SerializeField] private bool allowCheats = true;

        [Header("Initial cheat state (host)")]
        [SerializeField] private bool infiniteAmmoAtStart;
        [SerializeField] private bool alwaysBuyAtStart;
        [SerializeField] private bool infiniteCreditsAtStart;

        private readonly NetworkVariable<bool> cheatsAllowed = new NetworkVariable<bool>();
        private readonly NetworkVariable<bool> infiniteAmmo = new NetworkVariable<bool>();
        private readonly NetworkVariable<bool> alwaysBuy = new NetworkVariable<bool>();
        private readonly NetworkVariable<bool> infiniteCredits = new NetworkVariable<bool>();

        public static bool CheatsAllowed => Instance != null && Instance.cheatsAllowed.Value;

        /// <summary>Magazines never drain, so no reload is ever needed.</summary>
        public static bool InfiniteAmmo => CheatsAllowed && Instance.infiniteAmmo.Value;

        /// <summary>The buy menu opens in any phase, not only the buy phase.</summary>
        public static bool AlwaysBuy => CheatsAllowed && Instance.alwaysBuy.Value;

        /// <summary>Purchases cost nothing.</summary>
        public static bool InfiniteCredits => CheatsAllowed && Instance.infiniteCredits.Value;

        /// <summary>Whether the local peer may change cheats: the host, in a match that allows them.</summary>
        public static bool CanEditCheats => CheatsAllowed && Instance.IsServer;

        public static void SetInfiniteAmmo(bool value) => SetCheat(Instance?.infiniteAmmo, value);
        public static void SetAlwaysBuy(bool value) => SetCheat(Instance?.alwaysBuy, value);
        public static void SetInfiniteCredits(bool value) => SetCheat(Instance?.infiniteCredits, value);

        private static void SetCheat(NetworkVariable<bool> cheat, bool value)
        {
            if (cheat == null || !CanEditCheats) return;
            cheat.Value = value;
        }

        public override void OnNetworkSpawn()
        {
            Instance = this;

            if (IsServer)
            {
                cheatsAllowed.Value = allowCheats;
                infiniteAmmo.Value = infiniteAmmoAtStart;
                alwaysBuy.Value = alwaysBuyAtStart;
                infiniteCredits.Value = infiniteCreditsAtStart;
            }

            cheatsAllowed.OnValueChanged += RaiseChanged;
            infiniteAmmo.OnValueChanged += RaiseChanged;
            alwaysBuy.OnValueChanged += RaiseChanged;
            infiniteCredits.OnValueChanged += RaiseChanged;
            Changed?.Invoke();
        }

        public override void OnNetworkDespawn()
        {
            cheatsAllowed.OnValueChanged -= RaiseChanged;
            infiniteAmmo.OnValueChanged -= RaiseChanged;
            alwaysBuy.OnValueChanged -= RaiseChanged;
            infiniteCredits.OnValueChanged -= RaiseChanged;

            if (Instance == this) Instance = null;
            Changed?.Invoke();
        }

        private static void RaiseChanged(bool previous, bool current) => Changed?.Invoke();
    }
}
