using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using Tactics.Core;
using Tactics.Weapons;

namespace Tactics.Objectives
{
    [RequireComponent(typeof(WeaponInventory))]
    public class SpikeController : NetworkBehaviour
    {
        [Header("Settings")]
        [SerializeField] private float plantTime = 4f;
        [SerializeField] private float defuseTime = 7f;
        [SerializeField] private GameObject spikePrefab;

        private InputAction interactAction;
        private WeaponInventory inventory;
        private float interactTimer;
        private Spike currentSpike;
        private SpikeSite currentSite;

        private void Start()
        {
            interactAction = InputSystem.actions.FindAction("Interact");
            inventory = GetComponent<WeaponInventory>();
        }

        public override void OnNetworkSpawn()
        {
            if (!IsOwner) enabled = false;
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
            if (currentSite != null && currentSite.IsPlayerInSite() && currentSpike == null &&
                inventory != null && inventory.HasSpike)
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
            if (inventory == null || !inventory.ConsumeSpike()) return;

            PlantSpikeServerRpc();
            interactTimer = 0;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void PlantSpikeServerRpc()
        {
            // Server-spawned so every client sees the planted spike. currentSpike is
            // picked up on each client via OnTriggerEnter when the replicated copy
            // appears inside the proximity trigger.
            GameObject spikeObj = Instantiate(spikePrefab, transform.position, Quaternion.identity);
            spikeObj.GetComponent<NetworkObject>().Spawn();
            spikeObj.GetComponent<Spike>().Plant();

            // Keep the server's copy of the inventory truthful (the owner already
            // consumed its local copy before sending this RPC).
            GetComponent<WeaponInventory>()?.ServerClearSpike();
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
