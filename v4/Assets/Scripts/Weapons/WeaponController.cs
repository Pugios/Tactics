using UnityEngine;
using UnityEngine.InputSystem;
using ValorantTrainer.Combat;

namespace ValorantTrainer.Weapons
{
    public class WeaponController : MonoBehaviour
    {
        [SerializeField] private WeaponData currentWeapon;
        [SerializeField] private Transform shootPoint;
        [SerializeField] private LayerMask hitLayers;

        private InputAction attackAction;
        private InputAction reloadAction;
        private float lastFireTime;
        private int currentAmmo;
        private bool isReloading;
        private ValorantTrainer.Sound.SoundEmitter soundEmitter;

        public int CurrentAmmo => currentAmmo;
        public WeaponData CurrentWeapon => currentWeapon;
        public event System.Action OnAmmoChanged;

        private void Start()
        {
            attackAction = InputSystem.actions.FindAction("Attack");
            reloadAction = InputSystem.actions.FindAction("Reload");
            soundEmitter = GetComponent<ValorantTrainer.Sound.SoundEmitter>();
            if (currentWeapon != null)
            {
                currentAmmo = currentWeapon.magazineSize;
                OnAmmoChanged?.Invoke();
            }
        }

        private void Update()
        {
            if (isReloading) return;

            if (reloadAction != null && reloadAction.WasPressedThisFrame())
            {
                if (currentWeapon != null && currentAmmo < currentWeapon.magazineSize)
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
            currentAmmo = 0;
            OnAmmoChanged?.Invoke();

            yield return new WaitForSeconds(currentWeapon.reloadSpeed);

            currentAmmo = currentWeapon.magazineSize;
            isReloading = false;
            OnAmmoChanged?.Invoke();
        }

        public void TryShoot()
        {
            if (currentWeapon == null) return;
            if (Time.time < lastFireTime + (1f / currentWeapon.fireRate)) return;
            if (currentAmmo <= 0) return;

            Shoot();
        }

        private void Shoot()
        {
            lastFireTime = Time.time;
            currentAmmo--;
            OnAmmoChanged?.Invoke();

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

        public void SetWeapon(WeaponData weapon)
        {
            currentWeapon = weapon;
            currentAmmo = weapon.magazineSize;
        }
    }
}
