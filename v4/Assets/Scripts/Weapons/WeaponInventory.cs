using UnityEngine;
using UnityEngine.InputSystem;
using System;
using Tactics.Combat;

namespace Tactics.Weapons
{
    public enum EquipSlot { Primary, Sidearm, Melee, Spike }

    [RequireComponent(typeof(WeaponController))]
    [DisallowMultipleComponent]
    public class WeaponInventory : MonoBehaviour
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

        public event Action OnActiveWeaponChanged;
        public event Action OnInventoryChanged;
        public event Action OnAmmoChanged;
        public event Action<bool> OnSpikeChanged;

        private void Awake()
        {
            weaponController = GetComponent<WeaponController>();

            var health = GetComponent<Health>();
            if (health != null) health.OnDeath += () => TryDropSpikeOnDeath(transform.position);
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

            if (slot == ActiveSlot) OnActiveWeaponChanged?.Invoke();
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
            OnActiveWeaponChanged?.Invoke();
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

        public bool ConsumeSpike()
        {
            if (!HasSpike) return false;
            HasSpike = false;
            OnSpikeChanged?.Invoke(false);
            FallBackFromSpikeSlot();
            return true;
        }

        public bool TryDropSpikeOnDeath(Vector3 position)
        {
            if (!HasSpike) return false;
            HasSpike = false;
            OnSpikeChanged?.Invoke(false);
            FallBackFromSpikeSlot();

            if (spikePickupPrefab != null) Instantiate(spikePickupPrefab, position, Quaternion.identity);
            return true;
        }

        private void FallBackFromSpikeSlot()
        {
            if (ActiveSlot == EquipSlot.Spike)
            {
                ActiveSlot = EquipSlot.Melee;
                OnActiveWeaponChanged?.Invoke();
            }
        }
    }
}
