using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.InputSystem;
using ValorantTrainer.Economy;
using ValorantTrainer.Core;
using ValorantTrainer.Weapons;

namespace ValorantTrainer.UI
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
            // Find the local player's WeaponController
            // In prototype, we assume it's on the object named "Player"
            var player = GameObject.Find("Player");
            if (player != null)
            {
                var wc = player.GetComponent<WeaponController>();
                if (wc != null) wc.SetWeapon(weapon);
            }
        }

        private void UpdateCreds(int amount)
        {
            if (credsLabel != null) credsLabel.text = "CREDS: " + amount;
        }
    }
}
