using UnityEngine;
using System;

namespace ValorantTrainer.Economy
{
    public class EconomyManager : MonoBehaviour
    {
        public static EconomyManager Instance { get; private set; }

        [Header("Settings")]
        [SerializeField] private int initialCreds = 800;
        [SerializeField] private int maxCreds = 9000;
        
        private int currentCreds;
        private int lossStreak = 0;

        public event Action<int> OnCredsChanged;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);
            
            currentCreds = initialCreds;
        }

        private void Start()
        {
            OnCredsChanged?.Invoke(currentCreds);
        }

        public bool CanAfford(int cost) => currentCreds >= cost;

        public void Spend(int amount)
        {
            currentCreds = Mathf.Max(0, currentCreds - amount);
            OnCredsChanged?.Invoke(currentCreds);
        }

        public void AddKillReward()
        {
            AddCreds(200);
        }

        public void AddPlantReward()
        {
            AddCreds(300);
        }

        public void AwardRoundWin()
        {
            AddCreds(3000);
            lossStreak = 0;
        }

        public void AwardRoundLoss(bool survived)
        {
            if (survived)
            {
                AddCreds(1000);
            }
            else
            {
                lossStreak++;
                int reward = 1900;
                if (lossStreak == 2) reward = 2400;
                else if (lossStreak >= 3) reward = 2900;
                
                AddCreds(reward);
            }
        }

        public void ResetForHalf()
        {
            currentCreds = 800;
            lossStreak = 0;
            OnCredsChanged?.Invoke(currentCreds);
        }

        private void AddCreds(int amount)
        {
            currentCreds = Mathf.Min(maxCreds, currentCreds + amount);
            OnCredsChanged?.Invoke(currentCreds);
        }

        public int GetCurrentCreds() => currentCreds;
    }
}
