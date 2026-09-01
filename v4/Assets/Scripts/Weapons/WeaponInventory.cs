using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using System;
using Tactics.Combat;

namespace Tactics.Weapons
{
    public enum EquipSlot { Primary, Sidearm, Melee, Spike }

    [RequireComponent(typeof(WeaponController))]
    [DisallowMultipleComponent]
    public class WeaponInventory : NetworkBehaviour
    {
        [SerializeField] private WeaponData defaultMelee;
        [SerializeField] private WeaponData defaultSidearm;
        [SerializeField] private GameObject spikePickupPrefab;

        private class SlotState
        {
            public WeaponData Data;
            public int Ammo;
            public int Reserve;
        }

        private readonly SlotState primary = new SlotState();
        private readonly SlotState sidearm = new SlotState();
        private readonly SlotState melee = new SlotState();

        private WeaponController weaponController;
        private InputAction slot1Action;
        private InputAction slot2Action;
        private InputAction slot3Action;
        private InputAction slot4Action;

        public EquipSlot ActiveSlot { get; private set; } = EquipSlot.Melee;
        public bool HasSpike { get; private set; }

        // Valorant-style equip time: after the active weapon changes, firing,
        // reloading, and toggle-zoom cycling are blocked until the new weapon's
        // equipSpeed has elapsed. Client-trusted, like ammo, until the
        // buy/economy systems are networked.
        private float equipReadyTime;

        public bool IsEquipping => Time.time < equipReadyTime;

        public event Action OnActiveWeaponChanged;
        public event Action OnInventoryChanged;
        public event Action OnAmmoChanged;
        public event Action<bool> OnSpikeChanged;

        private Health health;

        private void Awake()
        {
            weaponController = GetComponent<WeaponController>();
            health = GetComponent<Health>();
        }

        public override void OnNetworkSpawn()
        {
            if (!IsOwner) enabled = false;

            // Death drop is server-authoritative: the server instance's HasSpike
            // mirrors reality because pickups are granted server-side
            // (SpikePickup.TryGrant) and plants are consumed server-side
            // (SpikeController.PlantSpikeServerRpc).
            if (IsServer && health != null) health.OnDeath += ServerHandleDeathDrop;
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer && health != null) health.OnDeath -= ServerHandleDeathDrop;
        }

        private void Start()
        {
            slot1Action = InputSystem.actions.FindAction("Slot1");
            slot2Action = InputSystem.actions.FindAction("Slot2");
            slot3Action = InputSystem.actions.FindAction("Slot3");
            slot4Action = InputSystem.actions.FindAction("Slot4");

            if (defaultMelee != null) Equip(EquipSlot.Melee, defaultMelee);
            if (defaultSidearm != null) Equip(EquipSlot.Sidearm, defaultSidearm);
            ActiveSlot = EquipSlot.Melee;
            equipReadyTime = 0f; // spawning in doesn't count as drawing a weapon
            OnActiveWeaponChanged?.Invoke();
        }

        private void Update()
        {
            if (weaponController != null && weaponController.IsReloading) return;

            if (slot1Action != null && slot1Action.WasPressedThisFrame()) SwitchTo(EquipSlot.Primary);
            else if (slot2Action != null && slot2Action.WasPressedThisFrame()) SwitchTo(EquipSlot.Sidearm);
            else if (slot3Action != null && slot3Action.WasPressedThisFrame()) SwitchTo(EquipSlot.Melee);
            else if (slot4Action != null && slot4Action.WasPressedThisFrame()) SwitchTo(EquipSlot.Spike);
        }

        private SlotState GetState(EquipSlot slot)
        {
            switch (slot)
            {
                case EquipSlot.Primary: return primary;
                case EquipSlot.Sidearm: return sidearm;
                case EquipSlot.Melee: return melee;
                default: return null;
            }
        }

        public void Equip(EquipSlot slot, WeaponData data)
        {
            var state = GetState(slot);
            if (state == null || data == null) return;

            state.Data = data;
            state.Ammo = data.magazineSize;
            state.Reserve = data.reserveAmmo;

            if (slot == ActiveSlot)
            {
                // A buy landing in the hands counts as drawing the new weapon.
                BeginEquipDelay();
                OnActiveWeaponChanged?.Invoke();
            }
            OnInventoryChanged?.Invoke();
        }

        public WeaponData GetSlotData(EquipSlot slot) => GetState(slot)?.Data;

        public void SwitchTo(EquipSlot slot)
        {
            if (slot == ActiveSlot) return;

            if (slot == EquipSlot.Spike)
            {
                if (!HasSpike) return;
            }
            else if (GetState(slot).Data == null)
            {
                return;
            }

            ActiveSlot = slot;
            BeginEquipDelay();
            OnActiveWeaponChanged?.Invoke();
        }

        private void BeginEquipDelay()
        {
            var data = GetActiveWeaponData();
            equipReadyTime = Time.time + (data != null ? data.equipSpeed : 0f);
        }

        public WeaponData GetActiveWeaponData()
        {
            var state = GetState(ActiveSlot);
            return state?.Data;
        }

        public int GetActiveAmmo()
        {
            var state = GetState(ActiveSlot);
            return state?.Ammo ?? 0;
        }

        public void SetActiveAmmo(int ammo)
        {
            var state = GetState(ActiveSlot);
            if (state == null) return;
            state.Ammo = ammo;
            OnAmmoChanged?.Invoke();
        }

        public int GetActiveReserve()
        {
            var state = GetState(ActiveSlot);
            return state?.Reserve ?? 0;
        }

        public void SetActiveReserve(int reserve)
        {
            var state = GetState(ActiveSlot);
            if (state == null) return;
            state.Reserve = reserve;
            OnAmmoChanged?.Invoke();
        }

        public bool TryPickupSpike()
        {
            if (HasSpike) return false;
            HasSpike = true;
            OnSpikeChanged?.Invoke(true);
            return true;
        }

        /// <summary>
        /// Called by the server when it awards this player a spike pickup, so the
        /// owning client's local inventory (which drives HUD and plant checks)
        /// learns about it. Executes locally when the owner is the host.
        /// </summary>
        [Rpc(SendTo.Owner)]
        public void GrantSpikeOwnerRpc()
        {
            TryPickupSpike();
        }

        public bool ConsumeSpike()
        {
            if (!HasSpike) return false;
            HasSpike = false;
            OnSpikeChanged?.Invoke(false);
            FallBackFromSpikeSlot();
            return true;
        }

        /// <summary>
        /// Server-side bookkeeping when the spike leaves this player without the
        /// owner initiating it locally (plant confirmation, death). Safe to call
        /// redundantly — no-op when the spike is already gone.
        /// </summary>
        public void ServerClearSpike()
        {
            ClearSpikeLocal();
        }

        [Rpc(SendTo.Owner)]
        private void ClearSpikeOwnerRpc()
        {
            ClearSpikeLocal();
        }

        private void ClearSpikeLocal()
        {
            if (!HasSpike) return;
            HasSpike = false;
            OnSpikeChanged?.Invoke(false);
            FallBackFromSpikeSlot();
        }

        private void ServerHandleDeathDrop()
        {
            if (!HasSpike) return;

            Vector3 dropPosition = transform.position;
            ServerClearSpike();
            ClearSpikeOwnerRpc(); // owner's local copy drives HUD and plant checks

            if (spikePickupPrefab != null)
            {
                // The prefab root's rotation carries the upright correction for the
                // model's Blender axes — don't overwrite it with identity.
                GameObject pickup = Instantiate(spikePickupPrefab, dropPosition, spikePickupPrefab.transform.rotation);
                pickup.GetComponent<NetworkObject>().Spawn();
            }
        }

        private void FallBackFromSpikeSlot()
        {
            if (ActiveSlot == EquipSlot.Spike)
            {
                ActiveSlot = EquipSlot.Melee;
                BeginEquipDelay();
                OnActiveWeaponChanged?.Invoke();
            }
        }
    }
}
