using UnityEngine;
using UnityEngine.InputSystem;
using Tactics.Combat;

namespace Tactics.Weapons
{
    [RequireComponent(typeof(WeaponInventory))]
    [DisallowMultipleComponent]
    public class WeaponController : MonoBehaviour
    {
        [SerializeField] private Transform shootPoint;
        [SerializeField] private LayerMask hitLayers;

        private WeaponInventory inventory;
        private InputAction attackAction;
        private InputAction reloadAction;
        private float lastFireTime;
        private bool isReloading;
        private Tactics.Sound.SoundEmitter soundEmitter;

        public int CurrentAmmo => inventory != null ? inventory.GetActiveAmmo() : 0;
        public WeaponData CurrentWeapon => inventory != null ? inventory.GetActiveWeaponData() : null;
        public bool IsReloading => isReloading;
        public event System.Action OnAmmoChanged;

        private void Awake()
        {
            inventory = GetComponent<WeaponInventory>();
        }

        private void Start()
        {
            attackAction = InputSystem.actions.FindAction("Attack");
            reloadAction = InputSystem.actions.FindAction("Reload");
            soundEmitter = GetComponent<Tactics.Sound.SoundEmitter>();

            inventory.OnActiveWeaponChanged += HandleActiveWeaponChanged;
            inventory.OnAmmoChanged += HandleAmmoChanged;
        }

        private void OnDestroy()
        {
            if (inventory == null) return;
            inventory.OnActiveWeaponChanged -= HandleActiveWeaponChanged;
            inventory.OnAmmoChanged -= HandleAmmoChanged;
        }

        private void HandleActiveWeaponChanged() => OnAmmoChanged?.Invoke();
        private void HandleAmmoChanged() => OnAmmoChanged?.Invoke();

        private void Update()
        {
            if (isReloading) return;

            var currentWeapon = CurrentWeapon;

            if (reloadAction != null && reloadAction.WasPressedThisFrame())
            {
                if (currentWeapon != null && !currentWeapon.infiniteAmmo && inventory.GetActiveAmmo() < currentWeapon.magazineSize)
                {
                    StartCoroutine(Reload());
                    return;
                }
            }

            if (attackAction.IsPressed())
            {
                TryShoot();
            }
        }

        private System.Collections.IEnumerator Reload()
        {
            isReloading = true;

            // "Removing the bullets from the magazine"
            inventory.SetActiveAmmo(0);

            var weapon = CurrentWeapon;
            yield return new WaitForSeconds(weapon.reloadSpeed);

            inventory.SetActiveAmmo(weapon.magazineSize);
            isReloading = false;
        }

        public void TryShoot()
        {
            var currentWeapon = CurrentWeapon;
            if (currentWeapon == null) return;
            if (Time.time < lastFireTime + (1f / currentWeapon.fireRate)) return;
            if (!currentWeapon.infiniteAmmo && inventory.GetActiveAmmo() <= 0) return;

            Shoot(currentWeapon);
        }

        private void Shoot(WeaponData currentWeapon)
        {
            lastFireTime = Time.time;
            if (!currentWeapon.infiniteAmmo) inventory.SetActiveAmmo(inventory.GetActiveAmmo() - 1);

            if (soundEmitter != null) soundEmitter.EmitShootSound();

            // 1. Mouse Target Detection
            if (UnityEngine.Camera.main == null) return;
            Ray cameraRay = UnityEngine.Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
            Health target = null;
            Vector3 mouseWorldPosition = Vector3.zero;

            if (Physics.Raycast(cameraRay, out RaycastHit cameraHit, 1000f))
            {
                mouseWorldPosition = cameraHit.point;
                target = cameraHit.collider.GetComponent<Health>();
            }

            Vector3 startPoint = shootPoint != null ? shootPoint.position : transform.position;

            // 2. Trajectory & Wall Penetration
            if (target != null)
            {
                Vector3 direction = (mouseWorldPosition - startPoint).normalized;
                float distanceToTarget = Vector3.Distance(startPoint, mouseWorldPosition);

                int wallLayer = 6;
                int wallLayerMask = 1 << wallLayer;
                RaycastHit[] wallHits = Physics.RaycastAll(startPoint, direction, distanceToTarget, wallLayerMask);

                float totalThickness = 0f;
                foreach (var hit in wallHits)
                {
                    // Secondary raycast from the 'back' of the wall
                    // Use a slightly larger distance than max penetration (1.0m) to find the exit
                    Vector3 backStart = hit.point + direction * 2f;

                    // Shoot backwards and find all hits to identify the exit point of the same collider
                    RaycastHit[] backHits = Physics.RaycastAll(backStart, -direction, 2f, wallLayerMask);
                    foreach (var backHit in backHits)
                    {
                        if (backHit.collider == hit.collider)
                        {
                            totalThickness += Vector3.Distance(hit.point, backHit.point);
                            break;
                        }
                    }
                }

                if (totalThickness <= 1.0f)
                {
                    // 3. Proximity Damage (XZ plane)
                    Vector3 targetPos = target.transform.position;
                    float xzDistance = Vector2.Distance(new Vector2(mouseWorldPosition.x, mouseWorldPosition.z),
                                                        new Vector2(targetPos.x, targetPos.z));

                    float hitMultiplier = 0f;
                    bool isPerfect = false;

                    // "Head" Perfect Shot
                    if (xzDistance < 0.5f)
                    {
                        hitMultiplier = currentWeapon.perfectMultiplier;
                        isPerfect = true;
                    }
                    // "Body" Medium Shot
                    else if (xzDistance < 0.75f)
                    {
                        hitMultiplier = currentWeapon.mediumMultiplier;
                    }
                    // "Leg" Low Shot
                    else if (xzDistance < 1f)
                    {
                        hitMultiplier = currentWeapon.lowMultiplier;
                    }

                    if (hitMultiplier > 0)
                    {
                        // 4. Damage Calculation
                        float finalDamage = currentWeapon.headDamage * hitMultiplier * (1.0f - totalThickness);
                        if (finalDamage > 0)
                        {
                            int damage = (int)finalDamage;
                            target.TakeDamage(damage);

                            string hitType = isPerfect ? "perfect" : "body";
                            string wallbang = totalThickness > 0 ? $", wallbang (thickness {totalThickness:F2}m)" : "";
                            Debug.Log($"[Damage] {damage} to {target.name} ({hitType}{wallbang})");
                        }
                    }
                }

                // 5. Visual Feedback
                Debug.DrawRay(startPoint, direction * distanceToTarget, Color.red, 0.1f);
            }
            else
            {
                // Fallback visual feedback if no target found
                Vector3 direction = (mouseWorldPosition != Vector3.zero) ? (mouseWorldPosition - startPoint).normalized : transform.forward;
                float distance = (mouseWorldPosition != Vector3.zero) ? Vector3.Distance(startPoint, mouseWorldPosition) : 100f;
                Debug.DrawRay(startPoint, direction * distance, Color.black, 0.1f);
            }
        }
    }
}
