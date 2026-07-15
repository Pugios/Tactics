using UnityEngine;
using UnityEngine.UIElements;
using Tactics.Weapons;

namespace Tactics.UI
{
    public class HUDController : MonoBehaviour
    {
        [SerializeField] private UIDocument uiDocument;
        private Label magAmmoLabel;
        private Label reserveAmmoLabel;
        private WeaponController weaponController;
        private WeaponInventory weaponInventory;

        private VisualElement slotPrimary;
        private VisualElement slotSidearm;
        private VisualElement slotMelee;
        private VisualElement slotSpike;
        private Label slotPrimaryLabel;
        private Label slotSidearmLabel;
        private Label slotMeleeLabel;

        private System.Action<bool> onSpikeChangedHandler;

        private void OnEnable()
        {
            var root = uiDocument.rootVisualElement;
            magAmmoLabel = root.Q<Label>("magAmmoLabel");
            reserveAmmoLabel = root.Q<Label>("reserveAmmoLabel");

            slotPrimary = root.Q<VisualElement>("slotPrimary");
            slotSidearm = root.Q<VisualElement>("slotSidearm");
            slotMelee = root.Q<VisualElement>("slotMelee");
            slotSpike = root.Q<VisualElement>("slotSpike");
            slotPrimaryLabel = root.Q<Label>("slotPrimaryLabel");
            slotSidearmLabel = root.Q<Label>("slotSidearmLabel");
            slotMeleeLabel = root.Q<Label>("slotMeleeLabel");

            // In prototype, find the player's weapon controller
            // We'll search for it if not found in Start to handle cases where player is spawned late
            FindWeaponController();
        }

        private void Update()
        {
            if (weaponController == null || weaponInventory == null)
            {
                FindWeaponController();
            }
        }

        private void FindWeaponController()
        {
            var player = GameObject.Find("Player");
            if (player == null) return;

            if (weaponController == null)
            {
                weaponController = player.GetComponent<WeaponController>();
                if (weaponController != null)
                {
                    weaponController.OnAmmoChanged += UpdateAmmoDisplay;
                    UpdateAmmoDisplay();
                }
            }

            if (weaponInventory == null)
            {
                weaponInventory = player.GetComponent<WeaponInventory>();
                if (weaponInventory != null)
                {
                    weaponInventory.OnActiveWeaponChanged += UpdateWeaponSlots;
                    weaponInventory.OnInventoryChanged += UpdateWeaponSlots;
                    onSpikeChangedHandler = _ => UpdateWeaponSlots();
                    weaponInventory.OnSpikeChanged += onSpikeChangedHandler;
                    UpdateWeaponSlots();
                }
            }
        }

        private void OnDisable()
        {
            if (weaponController != null)
            {
                weaponController.OnAmmoChanged -= UpdateAmmoDisplay;
            }

            if (weaponInventory != null)
            {
                weaponInventory.OnActiveWeaponChanged -= UpdateWeaponSlots;
                weaponInventory.OnInventoryChanged -= UpdateWeaponSlots;
                if (onSpikeChangedHandler != null) weaponInventory.OnSpikeChanged -= onSpikeChangedHandler;
            }
        }

        private void UpdateAmmoDisplay()
        {
            if (weaponController != null && weaponController.CurrentWeapon != null)
            {
                magAmmoLabel.text = weaponController.CurrentAmmo.ToString();
                reserveAmmoLabel.text = weaponController.CurrentWeapon.reserveAmmo.ToString();
            }
            else
            {
                magAmmoLabel.text = "-";
                reserveAmmoLabel.text = "-";
            }
        }

        private void UpdateWeaponSlots()
        {
            if (weaponInventory == null) return;

            SetSlot(slotPrimary, slotPrimaryLabel, weaponInventory.GetSlotData(EquipSlot.Primary), EquipSlot.Primary);
            SetSlot(slotSidearm, slotSidearmLabel, weaponInventory.GetSlotData(EquipSlot.Sidearm), EquipSlot.Sidearm);
            SetSlot(slotMelee, slotMeleeLabel, weaponInventory.GetSlotData(EquipSlot.Melee), EquipSlot.Melee);

            slotSpike.EnableInClassList("weapon-slot--active", weaponInventory.ActiveSlot == EquipSlot.Spike);
            slotSpike.EnableInClassList("weapon-slot--empty", !weaponInventory.HasSpike);
        }

        private void SetSlot(VisualElement slotElement, Label label, WeaponData data, EquipSlot slot)
        {
            label.text = data != null ? data.weaponName.ToUpperInvariant() : "-";
            slotElement.EnableInClassList("weapon-slot--active", weaponInventory.ActiveSlot == slot);
            slotElement.EnableInClassList("weapon-slot--empty", data == null);
        }
    }
}
