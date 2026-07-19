using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.InputSystem;
using Tactics.Economy;
using Tactics.Core;
using Tactics.Weapons;

namespace Tactics.UI
{
    public class BuyMenuController : MonoBehaviour
    {
        [SerializeField] private UIDocument uiDocument;
        [SerializeField] private WeaponData judgeData;
        [SerializeField] private WeaponData vandalData;
        [SerializeField] private WeaponData operatorData;

        private VisualElement root;
        private Label credsLabel;
        private InputAction buyAction;
        private bool isMenuOpen = false;

        private void OnEnable()
        {
            root = uiDocument.rootVisualElement;
            root.style.display = DisplayStyle.None;

            credsLabel = root.Q<Label>("credsLabel");
            
            root.Q<Button>("judgeBtn").clicked += () => TryBuy(judgeData);
            root.Q<Button>("vandalBtn").clicked += () => TryBuy(vandalData);
            root.Q<Button>("operatorBtn").clicked += () => TryBuy(operatorData);
            root.Q<Button>("closeBtn").clicked += ToggleMenu;

            buyAction = InputSystem.actions.FindAction("Buy");
            
            if (EconomyManager.Instance != null)
            {
                EconomyManager.Instance.OnCredsChanged += UpdateCreds;
                UpdateCreds(EconomyManager.Instance.GetCurrentCreds());
            }
}

        private void OnDisable()
        {
            EconomyManager.Instance.OnCredsChanged -= UpdateCreds;
        }

        private void Update()
        {
            if (buyAction.WasPressedThisFrame())
            {
                ToggleMenu();
            }
        }

        private void ToggleMenu()
        {
            if (!isMenuOpen && GameManager.Instance.GetCurrentState() != GameState.BuyPhase)
            {
                Debug.Log("Cannot buy outside of Buy Phase!");
                return;
            }

            isMenuOpen = !isMenuOpen;
            root.style.display = isMenuOpen ? DisplayStyle.Flex : DisplayStyle.None;
            
            // Lock/Unlock player movement/rotation if needed
            // For now just toggle UI
        }

        private void TryBuy(WeaponData weapon)
        {
            if (weapon == null) return;

            if (EconomyManager.Instance.CanAfford(weapon.cost))
            {
                EconomyManager.Instance.Spend(weapon.cost);
                EquipWeapon(weapon);
                Debug.Log("Bought " + weapon.weaponName);
            }
            else
            {
                Debug.Log("Not enough credits!");
            }
        }

        private void EquipWeapon(WeaponData weapon)
        {
            var nm = NetworkManager.Singleton;
            var player = nm != null && nm.LocalClient.PlayerObject != null ? nm.LocalClient.PlayerObject.gameObject : null;
            if (player != null)
            {
                var inventory = player.GetComponent<WeaponInventory>();
                if (inventory != null) inventory.Equip(SlotForWeaponType(weapon.type), weapon);
            }
        }

        private static EquipSlot SlotForWeaponType(WeaponType type)
        {
            switch (type)
            {
                case WeaponType.Sidearm: return EquipSlot.Sidearm;
                case WeaponType.Melee: return EquipSlot.Melee;
                default: return EquipSlot.Primary;
            }
        }

        private void UpdateCreds(int amount)
        {
            if (credsLabel != null) credsLabel.text = "CREDS: " + amount;
        }
    }
}
