using UnityEngine;
using UnityEngine.InputSystem;
using ValorantTrainer.Core;

namespace ValorantTrainer.Objectives
{
    public class SpikeController : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] private float plantTime = 4f;
        [SerializeField] private float defuseTime = 7f;
        [SerializeField] private GameObject spikePrefab;

        private InputAction interactAction;
        private float interactTimer;
        private Spike currentSpike;
        private SpikeSite currentSite;

        private void Start()
        {
            interactAction = InputSystem.actions.FindAction("Interact");
        }

        private void Update()
        {
            if (interactAction.IsPressed())
            {
                HandleInteraction();
            }
            else
            {
                interactTimer = 0;
            }
        }

        private void HandleInteraction()
        {
            if (GameManager.Instance.GetCurrentState() != GameState.RoundActive) return;

            // Check for plant
            if (currentSite != null && currentSite.IsPlayerInSite() && currentSpike == null)
            {
                interactTimer += Time.deltaTime;
                if (interactTimer >= plantTime)
                {
                    PlantSpike();
                }
            }
            // Check for defuse
            else if (currentSpike != null && !currentSpike.IsDefused() && !currentSpike.IsExploded())
            {
                float dist = Vector3.Distance(transform.position, currentSpike.transform.position);
                if (dist < 3f) // Interaction range
                {
                    interactTimer += Time.deltaTime;
                    if (interactTimer >= defuseTime)
                    {
                        currentSpike.Defuse();
                    }
                }
            }
        }

        private void PlantSpike()
        {
            GameObject spikeObj = Instantiate(spikePrefab, transform.position, Quaternion.identity);
            currentSpike = spikeObj.GetComponent<Spike>();
            currentSpike.Plant();
            interactTimer = 0;
        }

        private void OnTriggerEnter(Collider other)
        {
            var site = other.GetComponent<SpikeSite>();
            if (site != null) currentSite = site;

            var spike = other.GetComponent<Spike>();
            if (spike != null) currentSpike = spike;
        }

        private void OnTriggerExit(Collider other)
        {
            if (currentSite != null && other.gameObject == currentSite.gameObject) currentSite = null;
            // We don't clear currentSpike on exit so we can still defuse if we are close? 
            // Actually, trigger is better for proximity.
            if (currentSpike != null && other.gameObject == currentSpike.gameObject) currentSpike = null;
        }
    }
}
