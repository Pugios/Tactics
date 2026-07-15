using UnityEngine;
using UnityEngine.InputSystem;
using Tactics.Weapons;

namespace Tactics.Objectives
{
    public class SpikePickup : MonoBehaviour
    {
        [SerializeField] private float remotePickupRange = 2f;

        private InputAction interactAction;
        private GameObject player;
        private WeaponInventory playerInventory;

        private void Start()
        {
            interactAction = InputSystem.actions.FindAction("Interact");
            player = GameObject.FindWithTag("Player");
            if (player != null) playerInventory = player.GetComponent<WeaponInventory>();
        }

        private void Update()
        {
            if (playerInventory == null) return;
            if (interactAction == null || !interactAction.WasPressedThisFrame()) return;

            float distance = Vector3.Distance(transform.position, player.transform.position);
            if (distance <= remotePickupRange)
            {
                TryPickup(playerInventory);
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag("Player")) return;
            TryPickup(other.GetComponent<WeaponInventory>());
        }

        private void TryPickup(WeaponInventory inventory)
        {
            if (inventory != null && inventory.TryPickupSpike())
            {
                Destroy(gameObject);
            }
        }
    }
}
