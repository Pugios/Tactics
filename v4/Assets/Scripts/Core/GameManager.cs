using UnityEngine;
using System;
using System.Collections;
using Tactics.Economy;

namespace Tactics.Core
{
    public enum GameState { BuyPhase, RoundActive, RoundEnd }

    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        [Header("Timers")]
        [SerializeField] private float buyPhaseDuration = 30f;
        [SerializeField] private float roundDuration = 100f;
        [SerializeField] private float roundEndDuration = 7f;

        private GameState currentState;
        private float timer;
        private int roundNumber = 1;

        public event Action<GameState, float> OnStateChanged;
        public event Action<float> OnTimerUpdated;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);
        }

        private void Start()
        {
            StartCoroutine(GameLoop());
        }

        private IEnumerator GameLoop()
        {
            while (true)
            {
                // Buy Phase
                yield return StartState(GameState.BuyPhase, buyPhaseDuration);
                
                // Round Active
                yield return StartState(GameState.RoundActive, roundDuration);
                
                // Round End (Calculation/Transition)
                // For prototype, we just end the round
                AwardEndRoundRewards(true); // Default win for testing
                yield return StartState(GameState.RoundEnd, roundEndDuration);
                
                roundNumber++;
                if (roundNumber == 13) // Halftime
                {
                    EconomyManager.Instance.ResetForHalf();
                }
            }
        }

        private IEnumerator StartState(GameState state, float duration)
        {
            currentState = state;
            timer = duration;
            OnStateChanged?.Invoke(state, duration);

            while (timer > 0)
            {
                timer -= Time.deltaTime;
                OnTimerUpdated?.Invoke(timer);
                yield return null;
            }
        }

        private void AwardEndRoundRewards(bool won)
        {
            if (won)
            {
                EconomyManager.Instance.AwardRoundWin();
            }
            else
            {
                // In a real game, we'd check if the player survived
                EconomyManager.Instance.AwardRoundLoss(false);
            }
        }

        public GameState GetCurrentState() => currentState;
    }
}
