using UnityEngine;
using System;

namespace ValorantTrainer.Objectives
{
    public class Spike : MonoBehaviour
    {
        [SerializeField] private float timerDuration = 45f;
        private float currentTimer;
        private bool isPlanted;
        private bool isExploded;
        private bool isDefused;

        public event Action<float> OnTimerUpdate;
        public event Action OnExploded;
        public event Action OnDefused;

        public void Plant()
        {
            isPlanted = true;
            currentTimer = timerDuration;
            Debug.Log("Spike Planted!");
        }

        private void Update()
        {
            if (isPlanted && !isExploded && !isDefused)
            {
                currentTimer -= Time.deltaTime;
                OnTimerUpdate?.Invoke(currentTimer);

                if (currentTimer <= 0)
                {
                    Explode();
                }
            }
        }

        private void Explode()
        {
            isExploded = true;
            Debug.Log("BOOM! Spike Exploded.");
            OnExploded?.Invoke();
        }

        public void Defuse()
        {
            if (isExploded) return;
            isDefused = true;
            Debug.Log("Spike Defused!");
            OnDefused?.Invoke();
        }

        public bool IsDefused() => isDefused;
        public bool IsExploded() => isExploded;
    }
}
