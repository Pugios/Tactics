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

        private void OnEnable()
        {
            var root = uiDocument.rootVisualElement;
            magAmmoLabel = root.Q<Label>("magAmmoLabel");
            reserveAmmoLabel = root.Q<Label>("reserveAmmoLabel");

            // In prototype, find the player's weapon controller
            // We'll search for it if not found in Start to handle cases where player is spawned late
            FindWeaponController();
        }

        private void Update()
        {
            if (weaponController == null)
            {
                FindWeaponController();
            }
        }

        private void FindWeaponController()
        {
            var player = GameObject.Find("Player");
            if (player != null)
            {
                weaponController = player.GetComponent<WeaponController>();
                if (weaponController != null)
                {
                    weaponController.OnAmmoChanged += UpdateAmmoDisplay;
                    UpdateAmmoDisplay();
                }
            }
        }

        private void OnDisable()
        {
            if (weaponController != null)
            {
                weaponController.OnAmmoChanged -= UpdateAmmoDisplay;
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
    }
}