using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using Tactics.Weapons;

namespace Tactics.Objectives
{
    /// <summary>
    /// A spike lying in the world. Pickup is server-authoritative: only the server
    /// detects the pickup (walk-over trigger, or a client's interact-key request),
    /// grants the spike to the picking player's owner, and despawns this object
    /// everywhere. Clients never destroy it locally.
    /// </summary>
    public class SpikePickup : NetworkBehaviour
    {
        [SerializeField] private float remotePickupRange = 2f;

        private InputAction interactAction;
        private GameObject player;
        private WeaponInventory playerInventory;
        private bool consumed; // server-side: first pickup wins

        private void Start()
        {
            interactAction = InputSystem.actions.FindAction("Interact");
        }

        private void FindLocalPlayer()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || nm.LocalClient.PlayerObject == null) return;
            player = nm.LocalClient.PlayerObject.gameObject;
            playerInventory = player.GetComponent<WeaponInventory>();
        }

        private void Update()
        {
            // Interact-key convenience path, runs on each client for its own player.
            if (playerInventory == null)
            {
                FindLocalPlayer();
                if (playerInventory == null) return;
            }
            if (interactAction == null || !interactAction.WasPressedThisFrame()) return;
            if (playerInventory.HasSpike) return;

            float distance = Vector3.Distance(transform.position, player.transform.position);
            if (distance <= remotePickupRange)
            {
                RequestPickupServerRpc();
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!IsServer || consumed) return;
            if (!other.CompareTag("Player")) return;
            TryGrant(other.GetComponent<WeaponInventory>());
        }

        [Rpc(SendTo.Server)]
        private void RequestPickupServerRpc(RpcParams rpcParams = default)
        {
            if (consumed) return;

            ulong sender = rpcParams.Receive.SenderClientId;
            if (!NetworkManager.ConnectedClients.TryGetValue(sender, out var client) || client.PlayerObject == null) return;

            // Validate against the server's view of that player, with slack for the
            // remote view trailing the true simulated position slightly.
            float distance = Vector3.Distance(transform.position, client.PlayerObject.transform.position);
            if (distance > remotePickupRange + 1f) return;

            TryGrant(client.PlayerObject.GetComponent<WeaponInventory>());
        }

        private void TryGrant(WeaponInventory inventory)
        {
            if (inventory == null) return;

            // Dead players can't pick up the spike. Crucially, the death drop
            // spawns the pickup inside the dying player's own collider, and their
            // corpse would instantly re-grant itself the spike before the respawn
            // teleport moves them away.
            var health = inventory.GetComponent<Tactics.Combat.Health>();
            if (health != null && health.IsDead) return;

            if (!inventory.TryPickupSpike()) return; // server-side bookkeeping; refuses if already carrying

            consumed = true;
            inventory.GrantSpikeOwnerRpc();
            NetworkObject.Despawn(true);
        }
    }
}
