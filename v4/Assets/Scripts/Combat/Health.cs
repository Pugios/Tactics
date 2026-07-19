using UnityEngine;
using System;
using Unity.Netcode;

namespace Tactics.Combat
{
    /// <summary>
    /// Server-authoritative health. Only the server may mutate it (the mutators
    /// are no-ops everywhere else); values replicate to every peer via
    /// NetworkVariables, and the events fire on every peer when the replicated
    /// values change — subscribe locally for HUD and death reactions.
    /// </summary>
    public class Health : NetworkBehaviour
    {
        [SerializeField] private int maxHealth = 100;

        private readonly NetworkVariable<int> currentHealth = new NetworkVariable<int>();
        private readonly NetworkVariable<int> currentShield = new NetworkVariable<int>();
        private readonly NetworkVariable<int> maxShield = new NetworkVariable<int>();

        public event Action<int, int> OnHealthChanged;
        public event Action<int, int> OnShieldChanged;
        public event Action OnDeath;

        public bool IsDead => currentHealth.Value <= 0;

        public override void OnNetworkSpawn()
        {
            if (IsServer) currentHealth.Value = maxHealth;

            currentHealth.OnValueChanged += HandleHealthChanged;
            currentShield.OnValueChanged += HandleShieldChanged;
        }

        public override void OnNetworkDespawn()
        {
            currentHealth.OnValueChanged -= HandleHealthChanged;
            currentShield.OnValueChanged -= HandleShieldChanged;
        }

        private void HandleHealthChanged(int previous, int current)
        {
            OnHealthChanged?.Invoke(current, maxHealth);
            if (current <= 0 && previous > 0) OnDeath?.Invoke();
        }

        private void HandleShieldChanged(int previous, int current)
        {
            OnShieldChanged?.Invoke(current, maxShield.Value);
        }

        public void TakeDamage(int amount)
        {
            if (!IsServer || IsDead) return;

            if (currentShield.Value > 0)
            {
                int shieldDamage = Mathf.Min(currentShield.Value, amount);
                currentShield.Value -= shieldDamage;
                amount -= shieldDamage;
            }

            if (amount > 0)
            {
                currentHealth.Value = Mathf.Max(0, currentHealth.Value - amount);
            }
        }

        public void AddShield(int amount)
        {
            if (!IsServer) return;
            maxShield.Value = amount;
            currentShield.Value = amount;
        }

        public void Heal(int amount)
        {
            if (!IsServer || IsDead) return;
            currentHealth.Value = Mathf.Min(maxHealth, currentHealth.Value + amount);
        }

        /// <summary>Restores full health and clears shields (respawn).</summary>
        public void ServerRevive()
        {
            if (!IsServer) return;
            currentShield.Value = 0;
            maxShield.Value = 0;
            currentHealth.Value = maxHealth;
        }

        public int GetCurrentHealth() => currentHealth.Value;
        public int GetCurrentShield() => currentShield.Value;
    }
}
