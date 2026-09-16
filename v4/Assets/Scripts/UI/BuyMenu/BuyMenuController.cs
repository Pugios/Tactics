using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.InputSystem;
using Tactics.Economy;
using Tactics.Core;
using Tactics.Weapons;

namespace Tactics.UI
{
    public class BuyMenuController : MonoBehaviour, IGameMenu
    {
        [SerializeField] private UIDocument uiDocument;
        [SerializeField] private WeaponData judgeData;
        [SerializeField] private WeaponData vandalData;
        [SerializeField] private WeaponData operatorData;

        private VisualElement root;
        private Label credsLabel;
        private InputAction buyAction;
        private bool isMenuOpen = false;

        // Walking while shopping is normal in Valorant; only the Esc menu freezes.
        public bool FreezesPlayer => false;

        public void Cancel() => SetOpen(false);

        private void OnEnable()
        {
            root = uiDocument.rootVisualElement;
            root.style.display = DisplayStyle.None;
            isMenuOpen = false;

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
            if (EconomyManager.Instance != null) EconomyManager.Instance.OnCredsChanged -= UpdateCreds;
            MenuState.NotifyClosed(this);
        }

        private void Update()
        {
            if (buyAction.WasPressedThisFrame())
            {
                ToggleMenu();
            }

            // The shop closes itself when the buy phase ends, as in Valorant —
            // unless the Always Buy cheat keeps it open for business.
            if (isMenuOpen && !CanShopNow()) SetOpen(false);
        }

        private static bool CanShopNow() =>
            MatchRules.AlwaysBuy || GameManager.Instance.GetCurrentState() == GameState.BuyPhase;

        private void ToggleMenu()
        {
            if (!isMenuOpen && !CanShopNow())
            {
                Debug.Log("Cannot buy outside of Buy Phase!");
                return;
            }

            SetOpen(!isMenuOpen);
        }

        private void SetOpen(bool open)
        {
            if (open == isMenuOpen) return;
            // Another menu (the Esc menu) already has the screen.
            if (open && !MenuState.TryOpen(this)) return;
            if (!open) MenuState.NotifyClosed(this);

            isMenuOpen = open;
            root.style.display = isMenuOpen ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void TryBuy(WeaponData weapon)
        {
            if (weapon == null) return;

            if (MatchRules.InfiniteCredits)
            {
                EquipWeapon(weapon);
                Debug.Log("Bought " + weapon.weaponName + " (Infinite Credits)");
            }
            else if (EconomyManager.Instance.CanAfford(weapon.cost))
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
