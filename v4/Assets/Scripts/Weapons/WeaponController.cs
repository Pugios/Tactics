using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using Tactics.Combat;

namespace Tactics.Weapons
{
    /// <summary>
    /// Shooting is server-authoritative: the owner runs fire-rate/ammo checks for
    /// responsiveness and sends the aim point to the server, which re-validates
    /// the cadence, resolves the hit against its own world state, and applies
    /// damage through the replicated <see cref="Health"/>. Weapons cross the
    /// network as an index into <see cref="weaponRegistry"/> so the server reads
    /// stats from its own copy and a client can't invent damage values.
    /// Lag compensation: hits are resolved against each target's
    /// <see cref="HitboxHistory"/> rewound by the shooter's RTT + interpolation
    /// delay, so shots count where the shooter saw the target.
    /// </summary>
    [RequireComponent(typeof(WeaponInventory))]
    [DisallowMultipleComponent]
    public class WeaponController : NetworkBehaviour
    {
        private enum HitZone { Head, Body, Leg }

        [SerializeField] private Transform shootPoint;
        [SerializeField] private LayerMask hitLayers;
        [SerializeField] private WeaponData[] weaponRegistry;

        private const float MaxWallPenetrationMeters = 1f;
        private const float FireRateLeniency = 0.85f; // server cadence check tolerates network jitter
        private const float MaxRewindSeconds = 1f; // lag-comp favor-the-shooter cap

        private WeaponInventory inventory;
        private Health ownHealth;
        private Tactics.Player.PlayerMovementNetwork movementNetwork;
        private Tactics.Player.PlayerController playerController;
        private InputAction attackAction;
        private InputAction reloadAction;
        private float lastFireTime;
        private bool isReloading;
        private Tactics.Sound.SoundEmitter soundEmitter;

        // Server-side fire cadence tracking, one per player object.
        private double serverLastFireTime = double.NegativeInfinity;

        // Server-side spray state (SpreadCalculator inputs), per player object.
        // The seed is rolled once per spawn and the shot number never repeats,
        // so no two sprays ever roll the same spread offsets.
        private float serverSprayIndex;
        private int serverShotNumber;
        private int spreadSeed;
        private int serverLastWeaponId = -1;

        public int CurrentAmmo => inventory != null ? inventory.GetActiveAmmo() : 0;
        public WeaponData CurrentWeapon => inventory != null ? inventory.GetActiveWeaponData() : null;
        public bool IsReloading => isReloading;
        public event System.Action OnAmmoChanged;

        private void Awake()
        {
            inventory = GetComponent<WeaponInventory>();
            ownHealth = GetComponent<Health>();
            movementNetwork = GetComponent<Tactics.Player.PlayerMovementNetwork>();
            playerController = GetComponent<Tactics.Player.PlayerController>();
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                spreadSeed = new System.Random().Next();
                // A fresh life starts with a settled gun regardless of how the
                // last one ended mid-spray.
                if (ownHealth != null) ownHealth.OnDeath += ServerResetSpray;
            }
            if (!IsOwner) enabled = false;
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer && ownHealth != null) ownHealth.OnDeath -= ServerResetSpray;
        }

        private void ServerResetSpray() => serverSprayIndex = 0f;

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

            int ammoBeforeReload = inventory.GetActiveAmmo();

            var weapon = CurrentWeapon;
            yield return new WaitForSeconds(weapon.reloadSpeed);

            int neededToFill = weapon.magazineSize - ammoBeforeReload;
            int toLoad = Mathf.Min(neededToFill, inventory.GetActiveReserve());

            inventory.SetActiveReserve(inventory.GetActiveReserve() - toLoad);
            inventory.SetActiveAmmo(ammoBeforeReload + toLoad);
            isReloading = false;
        }

        public void TryShoot()
        {
            var currentWeapon = CurrentWeapon;
            if (currentWeapon == null) return;
            // IsAiming is already gated on the active weapon having an ADS alt-fire.
            float effectiveFireRate = currentWeapon.fireRate
                * (playerController != null && playerController.IsAiming ? currentWeapon.adsFireRateMultiplier : 1f);
            if (Time.time < lastFireTime + (1f / effectiveFireRate)) return;
            if (!currentWeapon.infiniteAmmo && inventory.GetActiveAmmo() <= 0) return;

            Shoot(currentWeapon);
        }

        private void Shoot(WeaponData currentWeapon)
        {
            lastFireTime = Time.time;
            if (!currentWeapon.infiniteAmmo) inventory.SetActiveAmmo(inventory.GetActiveAmmo() - 1);

            if (soundEmitter != null) soundEmitter.EmitShootSound();

            // The aim point (world position under the mouse) can only be computed
            // on the owner's machine — the server has no camera. Everything past
            // this point is the server's job.
            if (UnityEngine.Camera.main == null) return;
            Ray cameraRay = UnityEngine.Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
            if (!Physics.Raycast(cameraRay, out RaycastHit cameraHit, 1000f)) return;
            Vector3 aimPoint = cameraHit.point;

            Vector3 startPoint = shootPoint != null ? shootPoint.position : transform.position;
            Debug.DrawRay(startPoint, aimPoint - startPoint, Color.red, 0.1f);

            int weaponId = System.Array.IndexOf(weaponRegistry, currentWeapon);
            if (weaponId < 0)
            {
                Debug.LogWarning($"[Weapon] {currentWeapon.name} is missing from the weapon registry; shot not sent.");
                return;
            }

            ShootServerRpc(aimPoint, weaponId);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void ShootServerRpc(Vector3 aimPoint, int weaponId)
        {
            if (weaponRegistry == null || weaponId < 0 || weaponId >= weaponRegistry.Length) return;
            WeaponData weapon = weaponRegistry[weaponId];

            // Cadence re-check on the server clock. Ammo/inventory ownership is
            // still trusted client-side until the buy/economy systems are networked.
            // ADS stance comes from the authoritative movement snapshot, never the
            // client. A press/release between shots can make the client's assumed
            // rate differ from the server's for one shot, but the worst mismatch
            // (0.9 × 0.85 = 0.765/rate required vs 0.9/rate fired) is inside the
            // leniency window, so legitimate shots are never swallowed.
            bool ads = movementNetwork != null && movementNetwork.AuthoritativeIsAds
                && weapon.altFireType == AltFireType.AimDownSight;
            double fireInterval = 1.0 / (weapon.fireRate * (ads ? weapon.adsFireRateMultiplier : 1f));
            double now = NetworkManager.ServerTime.Time;
            if (now - serverLastFireTime < fireInterval * FireRateLeniency) return;
            float secondsSinceLastShot = (float)(now - serverLastFireTime);
            serverLastFireTime = now;

            // The shooter fires from its predicted (current) position; the server's
            // best match for that is its own sim position, not the smoothed view
            // transform.
            Vector3 startPoint = movementNetwork != null ? movementNetwork.AuthoritativePosition : transform.position;

            // Inaccuracy: the client sends the point under its cursor untouched;
            // the server displaces it by the spray's recoil + spread, judging
            // stance/movement from its own authoritative snapshot.
            if (weaponId != serverLastWeaponId)
            {
                // Swapping weapons settles the gun (equip time gates abusing this).
                serverSprayIndex = 0f;
                serverLastWeaponId = weaponId;
            }
            serverSprayIndex = SpreadCalculator.DecaySprayIndex(serverSprayIndex, secondsSinceLastShot,
                weapon.sprayDecayDelay, weapon.sprayDecayPerSecond);
            bool crouched = movementNetwork != null && movementNetwork.AuthoritativeIsCrouching;
            Tactics.Player.MovementState movement = movementNetwork != null
                ? movementNetwork.GetAuthoritativeMovementState()
                : Tactics.Player.MovementState.Stationary;
            Vector2 offsetDegrees = SpreadCalculator.ComputeShotOffsetDegrees(weapon, serverSprayIndex,
                crouched, ads, movement, spreadSeed, serverShotNumber++);
            Vector3 spreadAimPoint = SpreadCalculator.ApplyOffsetToAimPoint(startPoint, aimPoint, offsetDegrees);
            serverSprayIndex += 1f;

            ResolveHitServer(weapon, startPoint, spreadAimPoint, ComputeRewindTime());
        }

        /// <summary>
        /// Lag compensation: the positions the shooter was looking at left the
        /// server one downstream trip ago and were rendered a further interpolation
        /// delay in the past, and the shot spent an upstream trip getting here.
        /// Rewind hit resolution by RTT + interpolation delay (server-measured, so
        /// clients can't spoof it), capped at MaxRewindSeconds.
        /// </summary>
        private float ComputeRewindTime()
        {
            float rtt = NetworkManager.NetworkConfig.NetworkTransport.GetCurrentRtt(OwnerClientId) / 1000f;
            float interpolationDelay = (movementNetwork != null ? movementNetwork.InterpolationDelayTicks : 3f)
                * NetworkManager.LocalTime.FixedDeltaTime;
            float rewind = Mathf.Min(rtt + interpolationDelay, MaxRewindSeconds);
            return Time.time - rewind;
        }

        private void ResolveHitServer(WeaponData weapon, Vector3 startPoint, Vector3 aimPoint, float rewindTime)
        {
            Vector3 direction = (aimPoint - startPoint).normalized;
            float distanceToAim = Vector3.Distance(startPoint, aimPoint);
            float totalThickness = ComputeWallThickness(startPoint, direction, distanceToAim, out Vector3 wallEntryPoint, out Vector3 wallEntryNormal);

            if (totalThickness > MaxWallPenetrationMeters)
            {
                // Decal should sit flush against the wall face, not tilt with
                // whatever angle the shot came in at — use the face's own normal.
                BroadcastImpact(startPoint, wallEntryPoint, -wallEntryNormal, isEnemyHit: false, target: null);
                return;
            }

            Health target = FindClosestTarget(aimPoint, rewindTime, out Vector3 targetPos, out bool targetGrounded);
            if (target == null)
            {
                // Ground/prop tops aren't always a flat world-up plane (angled
                // prop tops etc.), so sample the actual surface normal at the
                // impact point instead of assuming straight down.
                Vector3 groundNormal = Vector3.up;
                int groundMask = (1 << Tactics.Vision.VisionLayerMasks.Ground) | (1 << Tactics.Vision.VisionLayerMasks.Dynamic);
                if (Physics.Raycast(aimPoint + Vector3.up * 0.5f, Vector3.down, out RaycastHit groundHit, 1f, groundMask))
                {
                    groundNormal = groundHit.normal;
                }
                BroadcastImpact(startPoint, aimPoint, -groundNormal, isEnemyHit: false, target: null);
                return;
            }

            // Proximity damage rings on the XZ plane around the target's rewound
            // position — where the shooter saw them, not where they are now.
            float xzDistance = Vector2.Distance(new Vector2(aimPoint.x, aimPoint.z),
                                                new Vector2(targetPos.x, targetPos.z));

            float hitMultiplier = 0f;
            HitZone hitZone = HitZone.Body;

            if (xzDistance < HitZoneRadii.Head)
            {
                hitMultiplier = weapon.perfectMultiplier;
                hitZone = HitZone.Head;
            }
            else if (xzDistance < HitZoneRadii.Body)
            {
                hitMultiplier = weapon.mediumMultiplier;
                hitZone = HitZone.Body;
            }
            else if (xzDistance < HitZoneRadii.Leg)
            {
                hitMultiplier = weapon.lowMultiplier;
                hitZone = HitZone.Leg;
            }

            if (hitMultiplier <= 0f) return;

            // Linear falloff with in-wall distance: 0.5m of wall halves the damage,
            // MaxWallPenetrationMeters of accumulated wall stops the bullet.
            float finalDamage = weapon.headDamage * hitMultiplier * (1f - totalThickness / MaxWallPenetrationMeters);

            // Jump peeking is rewarded with a flat damage reduction while airborne,
            // judged at the same rewound moment as the rest of the hit resolution.
            if (!targetGrounded) finalDamage *= 0.5f;

            if (finalDamage <= 0f) return;

            int damage = (int)finalDamage;
            target.TakeDamage(damage);

            string wallbang = totalThickness > 0 ? $", wallbang (thickness {totalThickness:F2}m)" : "";
            Debug.Log($"[Damage] {damage} to {target.name} ({hitZone}{wallbang})");

            HitConfirmOwnerRpc(damage, hitZone);
            // Enemy decals land on top of the capsule (visible from the top-down
            // camera), so project straight down for the same reason as ground hits.
            BroadcastImpact(startPoint, aimPoint, Vector3.down, isEnemyHit: true, target: target);
        }

        /// <summary>
        /// Cosmetic-only: tells every peer where a shot ended up so they can spawn
        /// a tracer/decal. The point always reflects a server-decided outcome
        /// (blocked-by-wall, miss, or confirmed hit), so this carries no new
        /// combat trust — only what gets drawn.
        /// </summary>
        private void BroadcastImpact(Vector3 origin, Vector3 point, Vector3 decalDirection, bool isEnemyHit, Health target)
        {
            NetworkObjectReference targetRef = target != null
                ? new NetworkObjectReference(target.NetworkObject)
                : default;
            ShotImpactClientRpc(origin, point, decalDirection, isEnemyHit, targetRef);
        }

        [Rpc(SendTo.Everyone)]
        private void ShotImpactClientRpc(Vector3 origin, Vector3 point, Vector3 decalDirection, bool isEnemyHit, NetworkObjectReference targetRef)
        {
            if (Tactics.Weapons.HitFxSpawner.Instance == null) return;

            NetworkObject targetObject = null;
            if (isEnemyHit) targetRef.TryGet(out targetObject);

            Tactics.Weapons.HitFxSpawner.Instance.SpawnImpact(origin, point, decalDirection, isEnemyHit, targetObject != null ? targetObject.transform : null);
        }

        private Health FindClosestTarget(Vector3 aimPoint, float rewindTime, out Vector3 rewoundPosition, out bool rewoundGrounded)
        {
            // The damage model is purely positional (XZ rings around the aim
            // point), so lag compensation needs no physics-scene rewind — just
            // each candidate's recorded position at the rewind time.
            Health best = null;
            float bestXzDistance = HitZoneRadii.Leg; // widest damage ring
            rewoundPosition = Vector3.zero;
            rewoundGrounded = true;

            foreach (var history in HitboxHistory.All)
            {
                Health health = history.Health;
                if (health == null || health == ownHealth || health.IsDead) continue;

                Vector3 pos = history.GetPositionAt(rewindTime);
                float xzDistance = Vector2.Distance(new Vector2(aimPoint.x, aimPoint.z),
                                                    new Vector2(pos.x, pos.z));
                if (xzDistance < bestXzDistance)
                {
                    bestXzDistance = xzDistance;
                    best = health;
                    rewoundPosition = pos;
                    rewoundGrounded = history.GetGroundedAt(rewindTime);
                }
            }

            return best;
        }

        /// <summary>
        /// Total distance the shot travels inside Wall-layer geometry, measured as
        /// the true path length between each wall's entry and exit face — angled
        /// shots that enter the front of a cube and leave through a side measure
        /// the same way as straight-through shots. Returns +infinity when the
        /// bullet never exits a wall before reaching the target (stopped).
        /// </summary>
        private static float ComputeWallThickness(Vector3 startPoint, Vector3 direction, float distance, out Vector3 firstEntryPoint, out Vector3 firstEntryNormal)
        {
            int wallLayerMask = 1 << Tactics.Vision.VisionLayerMasks.Wall;

            firstEntryPoint = startPoint + direction * distance;
            firstEntryNormal = -direction;

            // Raycasts only report front faces, so exit faces are found by casting
            // the same segment in reverse from the target end.
            RaycastHit[] entries = Physics.RaycastAll(startPoint, direction, distance, wallLayerMask);
            if (entries.Length == 0) return 0f;
            Vector3 endPoint = startPoint + direction * distance;
            RaycastHit[] exits = Physics.RaycastAll(endPoint, -direction, distance, wallLayerMask);

            float closestEntryDistance = float.PositiveInfinity;
            float totalThickness = 0f;
            foreach (var entry in entries)
            {
                if (entry.distance < closestEntryDistance)
                {
                    closestEntryDistance = entry.distance;
                    firstEntryPoint = entry.point;
                    firstEntryNormal = entry.normal;
                }

                // Both distances measured from startPoint along the shot.
                float entryDistance = entry.distance;
                float exitDistance = -1f;
                foreach (var exit in exits)
                {
                    if (exit.collider != entry.collider) continue;
                    float candidate = distance - exit.distance;
                    if (candidate > entryDistance && candidate > exitDistance) exitDistance = candidate;
                }

                // No exit before the target: the bullet ends inside this wall.
                if (exitDistance < 0f) return float.PositiveInfinity;

                totalThickness += exitDistance - entryDistance;
            }

            return totalThickness;
        }

        [Rpc(SendTo.Owner)]
        private void HitConfirmOwnerRpc(int damage, HitZone hitZone)
        {
            Debug.Log($"[Damage] Hit confirmed: {damage} ({hitZone})");
        }
    }
}
