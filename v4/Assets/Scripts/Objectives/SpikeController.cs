using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using Tactics.Core;
using Tactics.Weapons;

namespace Tactics.Objectives
{
    /// <summary>Which timed spike interaction is running, if any.</summary>
    public enum SpikeInteraction { None, Planting, Defusing }

    [RequireComponent(typeof(WeaponInventory))]
    public class SpikeController : NetworkBehaviour
    {
        [Header("Settings")]
        [SerializeField] private float plantTime = 4f;
        [SerializeField] private float defuseTime = 7f;
        [SerializeField] private GameObject spikePrefab;

        /// <summary>How close the defuser has to stand to the planted spike.</summary>
        private const float DefuseRange = 3f;

        private InputAction interactAction;
        private WeaponInventory inventory;
        private Tactics.Player.PlayerMovementNetwork movement;
        private Tactics.Player.PlayerController playerController;
        private float interactTimer;
        private Spike currentSpike;

        /// <summary>
        /// What holding Interact is currently achieving, if anything. Owner-local
        /// and purely for feedback: the HUD shows it on the same action bar a
        /// reload or a weapon draw uses.
        /// </summary>
        public SpikeInteraction Interaction { get; private set; }

        /// <summary>0..1 progress of the running plant/defuse; 1 when idle.</summary>
        public float InteractProgress01
        {
            get
            {
                float duration = Interaction == SpikeInteraction.Planting ? plantTime
                    : Interaction == SpikeInteraction.Defusing ? defuseTime
                    : 0f;
                if (duration <= 0f) return 1f;
                return Mathf.Clamp01(interactTimer / duration);
            }
        }

        private void Start()
        {
            interactAction = InputSystem.actions.FindAction("Interact");
            inventory = GetComponent<WeaponInventory>();
            movement = GetComponent<Tactics.Player.PlayerMovementNetwork>();
            playerController = GetComponent<Tactics.Player.PlayerController>();
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
                CancelInteraction();
            }

            // Pushed every frame rather than on change, so the lock can never be
            // left set by an interaction that ended some other way.
            if (playerController != null)
            {
                playerController.SetInteractionLock(Interaction != SpikeInteraction.None);
            }
        }

        private void OnDisable()
        {
            // Death, despawn, or losing ownership must not leave the player frozen.
            if (playerController != null) playerController.SetInteractionLock(false);
        }

        /// <summary>
        /// Anything that stops the action — releasing Interact, stepping off the
        /// site, walking out of defuse range, the round ending — drops the
        /// progress. A plant resumed from where it was interrupted would be both
        /// wrong and impossible to show honestly on the bar.
        /// </summary>
        private void CancelInteraction()
        {
            interactTimer = 0f;
            Interaction = SpikeInteraction.None;
        }

        private void HandleInteraction()
        {
            if (GameManager.Instance.GetCurrentState() != GameState.RoundActive)
            {
                CancelInteraction();
                return;
            }

            // Check for plant. The site is the floor itself: the face under
            // the player must be a plant-site surface, which the map importer
            // assigns from the Blender material painted on it.
            if (currentSpike == null && inventory != null && inventory.HasSpike &&
                PlantSiteQuery.IsOnPlantSite(movement != null ? movement.FeetPosition : transform.position))
            {
                Interaction = SpikeInteraction.Planting;
                interactTimer += Time.deltaTime;
                if (interactTimer >= plantTime)
                {
                    PlantSpike();
                }
            }
            // Check for defuse. Range is part of the condition rather than a nested
            // check so that stepping away cancels instead of freezing the timer.
            else if (currentSpike != null && !currentSpike.IsDefused() && !currentSpike.IsExploded()
                && Vector3.Distance(transform.position, currentSpike.transform.position) < DefuseRange)
            {
                Interaction = SpikeInteraction.Defusing;
                interactTimer += Time.deltaTime;
                if (interactTimer >= defuseTime)
                {
                    currentSpike.Defuse();
                    CancelInteraction();
                }
            }
            else
            {
                CancelInteraction();
            }
        }

        private void PlantSpike()
        {
            if (inventory == null || !inventory.ConsumeSpike()) return;

            PlantSpikeServerRpc();
            CancelInteraction();
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
            var spike = other.GetComponent<Spike>();
            if (spike != null) currentSpike = spike;
        }

        private void OnTriggerExit(Collider other)
        {
            // Only the spike uses a proximity trigger now; planting reads the floor.
            if (currentSpike != null && other.gameObject == currentSpike.gameObject) currentSpike = null;
        }
    }
}
