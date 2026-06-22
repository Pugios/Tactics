using UnityEngine;
using System;

namespace Tactics.Combat
{
    public class Health : MonoBehaviour
    {
        [SerializeField] private int maxHealth = 100;
        private int currentHealth;
        private int currentShield;
        private int maxShield;

        public event Action<int, int> OnHealthChanged;
        public event Action<int, int> OnShieldChanged;
        public event Action OnDeath;

        private void Awake()
        {
            currentHealth = maxHealth;
        }

        public void TakeDamage(int amount)
        {
            if (currentShield > 0)
            {
                // Shield absorbs 66% or 100%? In Valorant, it absorbs a portion or full until depleted.
                // Light Shield (25), Heavy (50).
                int shieldDamage = Mathf.Min(currentShield, amount);
                currentShield -= shieldDamage;
                amount -= shieldDamage;
                OnShieldChanged?.Invoke(currentShield, maxShield);
            }

            if (amount > 0)
            {
                currentHealth = Mathf.Max(0, currentHealth - amount);
                OnHealthChanged?.Invoke(currentHealth, maxHealth);

                if (currentHealth <= 0)
                {
                    OnDeath?.Invoke();
                }
            }
        }

        public void AddShield(int amount)
        {
            maxShield = amount;
            currentShield = amount;
            OnShieldChanged?.Invoke(currentShield, maxShield);
        }

        public void Heal(int amount)
        {
            currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
            OnHealthChanged?.Invoke(currentHealth, maxHealth);
        }

        public int GetCurrentHealth() => currentHealth;
        public int GetCurrentShield() => currentShield;
    }
}
