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
        [SerializeField] private Transform shootPoint;
        [SerializeField] private WeaponData[] weaponRegistry;

        // Only used if the PlayerController is somehow missing; the real values
        // live there next to the vision origin's, so the two can't drift.
        private const float FallbackEyeHeight = 1.9f;
        private const float FireRateLeniency = 0.85f; // server cadence check tolerates network jitter
        private const float MaxRewindSeconds = 1f; // lag-comp favor-the-shooter cap

        private WeaponInventory inventory;
        private Health ownHealth;
        private Tactics.Player.PlayerMovementNetwork movementNetwork;
        private Tactics.Player.PlayerController playerController;
        private InputAction attackAction;
        private InputAction altFireAction;
        private InputAction reloadAction;
        private float lastFireTime;
        // Interval the last shot claimed, so a slow alt burst keeps the gun busy
        // for its own cadence even when the next pull is a fast primary.
        private float lastFireInterval;
        private bool isReloading;
        private float reloadStartTime;
        private float reloadDuration;
        private Tactics.Sound.SoundEmitter soundEmitter;

        // Server-side fire cadence tracking, one per player object.
        private double serverLastFireTime = double.NegativeInfinity;
        private double serverLastFireInterval;

        // Server-side spray state (SpreadCalculator inputs), per player object.
        // The seed is rolled once per spawn and the shot number never repeats,
        // so no two sprays ever roll the same spread offsets.
        private float serverSprayIndex;
        private int serverShotNumber;
        private int spreadSeed;
        private int serverLastWeaponId = -1;

        // Owner-local mirror of the server's spray index, advanced through the
        // same pure SpreadCalculator functions on the same events (fire, weapon
        // switch, death), so the crosshair can preview spread with zero latency.
        // Cosmetic only — the server never reads it, and a server-side cadence
        // rejection at worst makes this briefly OVER-estimate the cone.
        private float localSprayIndex;
        private float localLastFireTime = float.NegativeInfinity;

        public int CurrentAmmo => inventory != null ? inventory.GetActiveAmmo() : 0;
        public WeaponData CurrentWeapon => inventory != null ? inventory.GetActiveWeaponData() : null;
        public bool IsReloading => isReloading;

        /// <summary>0..1 progress of the current reload; 1 when not reloading.</summary>
        public float ReloadProgress01
        {
            get
            {
                if (!isReloading || reloadDuration <= 0f) return 1f;
                return Mathf.Clamp01((Time.time - reloadStartTime) / reloadDuration);
            }
        }

        public event System.Action OnAmmoChanged;

        // Registry index of the owner's active weapon, refreshed on every weapon
        // change. Rides in PlayerInputTick so the movement sim (owner prediction
        // AND server replay) can resolve per-weapon speed from its own registry.
        private int activeWeaponRegistryId = -1;

        public int ActiveWeaponRegistryId => activeWeaponRegistryId;

        /// <summary>
        /// Owner-local: the alt trigger is DOWN on a shotgun-alt weapon. Firing
        /// is one burst per press, but the crosshair previews the burst's cone
        /// for as long as the button is held, so this is the held state, not the
        /// press. Cosmetic/predictive only — the server reads the flag the shot
        /// RPC carries, never this.
        /// </summary>
        public bool AltFireHeld =>
            altFireAction != null && altFireAction.IsPressed()
            && FireModeStats.IsShotgunAlt(CurrentWeapon, true);

        /// <summary>Pellets the next trigger pull would throw (crosshair circle gate).</summary>
        public int CurrentPelletCount => FireModeStats.MaxPelletCount(CurrentWeapon, AltFireHeld);

        /// <summary>Pattern falloff the next trigger pull would use (crosshair radius).</summary>
        public float CurrentSpreadDistanceExponent =>
            FireModeStats.SpreadDistanceExponent(CurrentWeapon, AltFireHeld);

        /// <summary>Registry lookup with bounds validation; null for -1/invalid ids.</summary>
        public WeaponData GetRegistryWeapon(int weaponId)
        {
            if (weaponRegistry == null || weaponId < 0 || weaponId >= weaponRegistry.Length) return null;
            return weaponRegistry[weaponId];
        }

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
            if (IsOwner && ownHealth != null) ownHealth.OnDeath += ResetLocalSpray;
            if (!IsOwner) enabled = false;
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer && ownHealth != null) ownHealth.OnDeath -= ServerResetSpray;
            if (IsOwner && ownHealth != null) ownHealth.OnDeath -= ResetLocalSpray;
        }

        private void ServerResetSpray() => serverSprayIndex = 0f;

        private void ResetLocalSpray() => localSprayIndex = 0f;

        private void Start()
        {
            attackAction = InputSystem.actions.FindAction("Attack");
            // PlayerController reads the same action for ADS, but AdsZoomLogic.LevelCount
            // returns 0 for a Shotgun alt-fire, so its path stays inert on these weapons.
            altFireAction = InputSystem.actions.FindAction("AltFire");
            reloadAction = InputSystem.actions.FindAction("Reload");
            soundEmitter = GetComponent<Tactics.Sound.SoundEmitter>();

            inventory.OnActiveWeaponChanged += HandleActiveWeaponChanged;
            inventory.OnAmmoChanged += HandleAmmoChanged;

            // The inventory equips its defaults in its own Start (before this
            // subscription exists), so seed the cache instead of waiting for the
            // next weapon change.
            RefreshActiveWeaponRegistryId();
        }

        private void OnDestroy()
        {
            if (inventory == null) return;
            inventory.OnActiveWeaponChanged -= HandleActiveWeaponChanged;
            inventory.OnAmmoChanged -= HandleAmmoChanged;
        }

        private void HandleActiveWeaponChanged()
        {
            RefreshActiveWeaponRegistryId();
            // Mirrors the server's swap-settles-the-gun reset (any drift a
            // no-fire swap could cause is erased by decay during the equip delay).
            localSprayIndex = 0f;
            OnAmmoChanged?.Invoke();
        }

        private void HandleAmmoChanged() => OnAmmoChanged?.Invoke();

        private void RefreshActiveWeaponRegistryId()
        {
            var weapon = CurrentWeapon;
            activeWeaponRegistryId = weapon != null && weaponRegistry != null
                ? System.Array.IndexOf(weaponRegistry, weapon)
                : -1;
        }

        private void Update()
        {
            if (isReloading) return;
            if (inventory.IsEquipping) return; // no firing or reloading during the draw

            var currentWeapon = CurrentWeapon;

            if (reloadAction != null && reloadAction.WasPressedThisFrame())
            {
                if (currentWeapon != null && !currentWeapon.infiniteAmmo && inventory.GetActiveAmmo() < currentWeapon.magazineSize)
                {
                    StartCoroutine(Reload());
                    return;
                }
            }

            // A shotgun alt-fire and a heavy melee swing are both one action per
            // press; primary fire repeats while held. Alt is checked first so
            // holding both buttons can't fire twice on one frame.
            bool altPressed = altFireAction != null && altFireAction.WasPressedThisFrame()
                && (FireModeStats.IsShotgunAlt(currentWeapon, true)
                    || FireModeStats.IsMeleeAlt(currentWeapon, true));
            if (altPressed)
            {
                TryShoot(altShot: true);
            }
            else if (attackAction.IsPressed())
            {
                TryShoot(altShot: false);
            }
        }

        private System.Collections.IEnumerator Reload()
        {
            isReloading = true;
            // Reloading drops a toggle scope (Operator); a held hold-mode ADS
            // (Vandal) re-arms itself next frame from the still-pressed button.
            if (playerController != null) playerController.ResetAdsZoom();

            int ammoBeforeReload = inventory.GetActiveAmmo();

            var weapon = CurrentWeapon;
            reloadStartTime = Time.time;
            reloadDuration = weapon.reloadSpeed;
            yield return new WaitForSeconds(weapon.reloadSpeed);

            int neededToFill = weapon.magazineSize - ammoBeforeReload;
            int toLoad = Mathf.Min(neededToFill, inventory.GetActiveReserve());

            inventory.SetActiveReserve(inventory.GetActiveReserve() - toLoad);
            inventory.SetActiveAmmo(ammoBeforeReload + toLoad);
            isReloading = false;
        }

        public void TryShoot(bool altShot)
        {
            var currentWeapon = CurrentWeapon;
            if (currentWeapon == null) return;
            if (inventory.IsEquipping) return;

            // IsAiming is already gated on the active weapon having an ADS alt-fire.
            bool ads = playerController != null && playerController.IsAiming;
            float interval = FireModeStats.IntervalSeconds(currentWeapon, altShot, ads);
            if (Time.time < lastFireTime + FireModeStats.RequiredGapSeconds(lastFireInterval, interval)) return;

            int ammo = inventory.GetActiveAmmo();
            if (!currentWeapon.infiniteAmmo && ammo <= 0) return;

            // A burst off a near-empty magazine still fires — it just throws the
            // rounds it has, one pellet each.
            int pellets = FireModeStats.MaxPelletCount(currentWeapon, altShot);
            if (!currentWeapon.infiniteAmmo) pellets = Mathf.Min(pellets, ammo);

            Shoot(currentWeapon, altShot, interval, pellets);
        }

        /// <summary>
        /// The world point the next shot would fly toward: the first surface the
        /// camera ray through the mouse cursor hits. Shared by <see cref="Shoot"/>
        /// and the crosshair so what's drawn and what's fired can never disagree.
        /// </summary>
        public bool TryGetAimPoint(out Vector3 aimPoint)
        {
            aimPoint = default;
            if (UnityEngine.Camera.main == null) return false;
            Ray cameraRay = UnityEngine.Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
            if (!Physics.Raycast(cameraRay, out RaycastHit cameraHit, 1000f)) return false;
            aimPoint = cameraHit.point;
            return true;
        }

        /// <summary>
        /// The spread cone (degrees) the owner's next shot would get right now —
        /// same pure SpreadCalculator math as the server, fed from zero-latency
        /// local inputs and the owner's predicted movement state. Crosshair input.
        /// </summary>
        public float CurrentSpreadDegrees
        {
            get
            {
                var weapon = CurrentWeapon;
                if (weapon == null) return 0f;
                float sprayIndex = SpreadCalculator.DecaySprayIndex(localSprayIndex,
                    Time.time - localLastFireTime, weapon.sprayDecayDelay, weapon.sprayDecayPerSecond);
                bool crouched = playerController != null && playerController.IsCrouching;
                bool walking = playerController != null && playerController.IsWalking;
                // Whichever alt-fire this weapon has: a held right click for a
                // shotgun burst, the ADS stance for everything else.
                bool altFire = weapon.altFireType == AltFireType.Shotgun
                    ? AltFireHeld
                    : (playerController != null && playerController.IsAiming);
                bool grounded = movementNetwork == null || movementNetwork.PredictedGrounded;
                float horizontalSpeed = movementNetwork != null ? movementNetwork.PredictedHorizontalSpeed : 0f;
                Tactics.Player.MovementState movement = Tactics.Player.MovementClassifier.Classify(
                    grounded, crouched, walking, horizontalSpeed);
                return SpreadCalculator.ComputeSpreadDegrees(weapon, sprayIndex, crouched, altFire, movement);
            }
        }

        private void Shoot(WeaponData currentWeapon, bool altShot, float interval, int pellets)
        {
            lastFireTime = Time.time;
            lastFireInterval = interval;
            // One round per pellet: a 3-pellet burst costs 3.
            if (!currentWeapon.infiniteAmmo) inventory.SetActiveAmmo(inventory.GetActiveAmmo() - pellets);

            // A knife swing makes no gunshot: it must not raise the shoot-sound
            // signal enemies read off the SoundVisualizer.
            bool melee = FireModeStats.IsMelee(currentWeapon);
            if (!melee && soundEmitter != null) soundEmitter.EmitShootSound();

            // A melee swing has no aim point — it resolves off the player's own
            // position and facing, both of which the server already has
            // authoritatively. Deliberately resolved BEFORE TryGetAimPoint so a
            // cursor over the void can't swallow a swing.
            if (melee)
            {
                int meleeWeaponId = System.Array.IndexOf(weaponRegistry, currentWeapon);
                if (meleeWeaponId < 0)
                {
                    Debug.LogWarning($"[Weapon] {currentWeapon.name} is missing from the weapon registry; swing not sent.");
                    return;
                }
                MeleeSwingServerRpc(meleeWeaponId, altShot);
                return;
            }

            // The aim point (world position under the mouse) can only be computed
            // on the owner's machine — the server has no camera. Everything past
            // this point is the server's job.
            if (!TryGetAimPoint(out Vector3 aimPoint)) return;

            Vector3 startPoint = shootPoint != null ? shootPoint.position : transform.position;
            Debug.DrawRay(startPoint, aimPoint - startPoint, Color.red, 0.1f);

            int weaponId = System.Array.IndexOf(weaponRegistry, currentWeapon);
            if (weaponId < 0)
            {
                Debug.LogWarning($"[Weapon] {currentWeapon.name} is missing from the weapon registry; shot not sent.");
                return;
            }

            // Advance the local spray mirror in the same decay-then-increment
            // order the server applies, only for shots that are actually sent,
            // so the crosshair tracks serverSprayIndex.
            localSprayIndex = SpreadCalculator.DecaySprayIndex(localSprayIndex,
                Time.time - localLastFireTime, currentWeapon.sprayDecayDelay, currentWeapon.sprayDecayPerSecond) + 1f;
            localLastFireTime = Time.time;

            ShootServerRpc(aimPoint, weaponId, altShot, pellets);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void ShootServerRpc(Vector3 aimPoint, int weaponId, bool altShot, int pelletCount)
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
            //
            // altShot and pelletCount are the only things the client says about the
            // shot itself, and neither buys anything: the server derives the spread
            // column, growth, movement penalties, pattern exponent and cadence from
            // altShot out of its OWN registry copy, so those move together and no
            // claim beats honestly picking the better mode; pelletCount is clamped
            // to the weapon's own maximum, so it can only ever be reduced. They ride
            // the shot rather than the tick-quantized snapshot ADS uses precisely so
            // they can never disagree with each other by a tick — a desynced snapshot
            // flag could otherwise land a 3-pellet burst gated at the primary's rate.
            bool alt = FireModeStats.IsShotgunAlt(weapon, altShot);
            bool ads = movementNetwork != null && movementNetwork.AuthoritativeIsAds
                && weapon.altFireType == AltFireType.AimDownSight;
            double fireInterval = FireModeStats.IntervalSeconds(weapon, alt, ads);
            double requiredGap = FireModeStats.RequiredGapSeconds(
                (float)serverLastFireInterval, (float)fireInterval);
            double now = NetworkManager.ServerTime.Time;
            if (now - serverLastFireTime < requiredGap * FireRateLeniency) return;
            float secondsSinceLastShot = (float)(now - serverLastFireTime);
            serverLastFireTime = now;
            serverLastFireInterval = fireInterval;

            // The shooter fires from its predicted (current) position; the server's
            // best match for that is its own sim position, not the smoothed view
            // transform. The player transform's origin sits at the feet, so raise
            // it to eye height: a bullet path that hugs the floor measures the
            // base of every wall and is stopped by waist-high cover it should
            // sail over (the same reason IsMeleeBlockedByWall tests at chest
            // height). Spread is unaffected either way - ApplyOffsetToAimPoint
            // zeroes y and works off the horizontal distance.
            bool crouched = movementNetwork != null && movementNetwork.AuthoritativeIsCrouching;
            Vector3 startPoint = movementNetwork != null ? movementNetwork.AuthoritativePosition : transform.position;
            startPoint.y += playerController != null
                ? playerController.EyeHeightFor(crouched)
                : FallbackEyeHeight;

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
            Tactics.Player.MovementState movement = movementNetwork != null
                ? movementNetwork.GetAuthoritativeMovementState()
                : Tactics.Player.MovementState.Stationary;
            bool altColumn = alt || ads;
            int pellets = Mathf.Clamp(pelletCount, 1, FireModeStats.MaxPelletCount(weapon, alt));
            Vector2[] pelletOffsets = SpreadCalculator.ComputePelletOffsetsDegrees(weapon, serverSprayIndex,
                crouched, altColumn, movement, spreadSeed, serverShotNumber, pellets);
            serverShotNumber += pellets;
            serverSprayIndex += 1f;

            ResolveShotServer(weapon, startPoint, aimPoint, pelletOffsets,
                FireModeStats.SpreadDistanceExponent(weapon, alt), ComputeRewindTime());
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

        /// <summary>
        /// A melee swing. Carries no aim point: the server already owns the two
        /// things a swing needs — the attacker's authoritative position and
        /// facing — so the only client claim is which weapon and which mode,
        /// and both are re-derived against the server's own registry copy.
        /// </summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void MeleeSwingServerRpc(int weaponId, bool altShot)
        {
            if (weaponRegistry == null || weaponId < 0 || weaponId >= weaponRegistry.Length) return;
            WeaponData weapon = weaponRegistry[weaponId];
            // A client can't swing a rifle: the melee path is reachable only by a
            // weapon the server's own copy agrees is melee.
            if (!FireModeStats.IsMelee(weapon)) return;

            // Same shared timer every other weapon uses, so alternating swings
            // with a gun can't beat either one's cadence.
            double fireInterval = FireModeStats.IntervalSeconds(weapon, altShot, ads: false);
            double requiredGap = FireModeStats.RequiredGapSeconds(
                (float)serverLastFireInterval, (float)fireInterval);
            double now = NetworkManager.ServerTime.Time;
            if (now - serverLastFireTime < requiredGap * FireRateLeniency) return;
            serverLastFireTime = now;
            serverLastFireInterval = fireInterval;

            // A swing throws no bullets, so it settles the gun the same way a
            // weapon switch does rather than advancing the spray.
            serverSprayIndex = 0f;
            serverLastWeaponId = weaponId;

            ResolveMeleeServer(weapon, altShot, ComputeRewindTime());
        }

        /// <summary>
        /// Resolves a swing against the nearest target standing in the box
        /// straight ahead of the attacker. Shares the bullet path's lag
        /// compensation (rewound HitboxHistory) but none of its geometry: no
        /// spread, no falloff, no wall penetration, and no airborne penalty —
        /// that last one is a spread-era rule about punishing jump peeks.
        /// </summary>
        private void ResolveMeleeServer(WeaponData weapon, bool altShot, float rewindTime)
        {
            Vector3 origin = movementNetwork != null ? movementNetwork.AuthoritativePosition : transform.position;
            float yaw = movementNetwork != null ? movementNetwork.AuthoritativeYRotation : transform.eulerAngles.y;
            Vector3 forward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;

            HitboxHistory bestHistory = null;
            Vector3 bestPosition = Vector3.zero;
            float bestForwardDistance = float.MaxValue;

            foreach (var history in HitboxHistory.All)
            {
                Health health = history.Health;
                if (health == null || health == ownHealth || health.IsDead) continue;

                Vector3 position = history.GetPositionAt(rewindTime);
                if (!MeleeCombat.TryGetForwardDistance(origin, forward, position,
                        weapon.meleeRange, HitZoneRadii.Leg, out float forwardDistance))
                    continue;
                if (forwardDistance >= bestForwardDistance) continue;
                if (IsMeleeBlockedByWall(origin, position)) continue;

                bestForwardDistance = forwardDistance;
                bestHistory = history;
                bestPosition = position;
            }

            if (bestHistory == null) return;

            bool backHit = MeleeCombat.IsBackHit(origin, bestPosition, bestHistory.GetYRotationAt(rewindTime));
            int damage = (int)MeleeCombat.GetDamage(weapon, altShot, backHit);
            bestHistory.Health.TakeDamage(damage);

            Debug.Log($"[Damage] {damage} to {bestHistory.Health.name} (melee {(altShot ? "alt" : "primary")}, "
                + $"{(backHit ? "back" : "front")}, {bestForwardDistance:F2}m)");
            // HitZone's member order is wire format and means bullet ring, not
            // side struck — a swing reports Body and the side stays server-side.
            HitConfirmOwnerRpc(damage, HitZone.Body, 1, 1);
        }

        /// <summary>
        /// A knife can't reach through geometry. Unlike a bullet there is no
        /// thickness budget: any wall between attacker and target blocks it.
        /// Tested at chest height so a low lip of ground geometry doesn't count.
        /// </summary>
        private static bool IsMeleeBlockedByWall(Vector3 origin, Vector3 targetPosition)
        {
            const float ChestHeight = 1f;
            int wallLayerMask = 1 << Tactics.Vision.VisionLayerMasks.Wall;
            return Physics.Linecast(origin + Vector3.up * ChestHeight,
                targetPosition + Vector3.up * ChestHeight, wallLayerMask);
        }

        /// <summary>One pellet's server-side outcome, resolved but not yet applied.</summary>
        private struct PelletResolution
        {
            public Vector3 point;          // where the tracer ends / decal sits
            public Vector3 decalDirection;
            public Health target;          // null = environment (wall/ground)
            public float damage;           // pre-truncation; 0 for blocks/misses
            public HitZone zone;
            public string penetrationLog;  // null unless the pellet crossed a wall
        }

        /// <summary>
        /// Resolves one trigger pull: each pellet runs the full single-bullet
        /// pipeline (wall thickness, lag-comp target lookup, XZ damage rings,
        /// distance falloff) independently, but damage is aggregated per target —
        /// summed as floats and truncated once, so a 12-pellet volley doesn't
        /// round down 12 times — with one hit confirm per target and all impact
        /// FX batched into a single RPC.
        /// </summary>
        /// <summary>Per-target damage aggregation for one trigger pull, with a
        /// per-zone pellet breakdown for the damage logs.</summary>
        private struct TargetDamage
        {
            public Health target;
            public float damage;
            public HitZone zone;
            public int headPellets;
            public int bodyPellets;
            public int legPellets;
            public string penetrationLog;

            public int TotalPellets => headPellets + bodyPellets + legPellets;

            public void CountPellet(HitZone pelletZone)
            {
                if (pelletZone == HitZone.Head) headPellets++;
                else if (pelletZone == HitZone.Body) bodyPellets++;
                else legPellets++;
            }
        }

        private void ResolveShotServer(WeaponData weapon, Vector3 startPoint, Vector3 aimPoint,
            Vector2[] pelletOffsets, float spreadExponent, float rewindTime)
        {
            var impacts = new System.Collections.Generic.List<PelletImpact>(pelletOffsets.Length);
            var targetHits = new System.Collections.Generic.List<TargetDamage>();

            foreach (Vector2 offsetDegrees in pelletOffsets)
            {
                Vector3 pelletAimPoint = SpreadCalculator.ApplyOffsetToAimPoint(startPoint, aimPoint,
                    offsetDegrees, spreadExponent);
                PelletResolution pellet = ResolvePellet(weapon, startPoint, pelletAimPoint, rewindTime);

                bool isEnemyHit = pellet.target != null;
                // A pellet fully absorbed by wall thickness confirms nothing and
                // draws nothing (matches the old single-bullet behavior).
                if (isEnemyHit && pellet.damage <= 0f) continue;

                impacts.Add(new PelletImpact
                {
                    point = pellet.point,
                    decalDirection = pellet.decalDirection,
                    isEnemyHit = isEnemyHit,
                    targetRef = isEnemyHit ? new NetworkObjectReference(pellet.target.NetworkObject) : default,
                });

                if (!isEnemyHit) continue;

                int existing = targetHits.FindIndex(hit => hit.target == pellet.target);
                if (existing < 0)
                {
                    var hit = new TargetDamage
                    {
                        target = pellet.target,
                        damage = pellet.damage,
                        zone = pellet.zone,
                        penetrationLog = pellet.penetrationLog,
                    };
                    hit.CountPellet(pellet.zone);
                    targetHits.Add(hit);
                }
                else
                {
                    var hit = targetHits[existing];
                    hit.damage += pellet.damage;
                    if (pellet.zone < hit.zone) hit.zone = pellet.zone; // report the best zone any pellet found
                    // One pull's pellets share an origin and very nearly a path, so
                    // the first wall-crosser's numbers describe the volley well
                    // enough to calibrate against.
                    if (hit.penetrationLog == null) hit.penetrationLog = pellet.penetrationLog;
                    hit.CountPellet(pellet.zone);
                    targetHits[existing] = hit;
                }
            }

            foreach (TargetDamage hit in targetHits)
            {
                int damage = (int)hit.damage;
                hit.target.TakeDamage(damage);
                Debug.Log($"[Damage] {damage} to {hit.target.name} ({hit.zone}) — "
                    + $"{hit.TotalPellets}/{pelletOffsets.Length} pellets hit "
                    + $"(Head:{hit.headPellets} Body:{hit.bodyPellets} Leg:{hit.legPellets})"
                    + (hit.penetrationLog != null ? " — " + hit.penetrationLog : string.Empty));
                HitConfirmOwnerRpc(damage, hit.zone, hit.TotalPellets, pelletOffsets.Length);
            }

            ShotImpactsClientRpc(startPoint, impacts.ToArray());
        }

        private PelletResolution ResolvePellet(WeaponData weapon, Vector3 startPoint, Vector3 aimPoint, float rewindTime)
        {
            var result = new PelletResolution { point = aimPoint, decalDirection = Vector3.down };

            // Penetration is measured on a HORIZONTAL segment at eye height
            // rather than along the sloped line to the ground-level aim point.
            // The damage model is XZ-planar throughout - FindClosestTarget and
            // the hit rings ignore y entirely - so measuring the wall in that
            // same plane is what lets a surface's travel budget be read as a
            // plain horizontal thickness in meters and tuned by hand.
            Vector3 wallEnd = new Vector3(aimPoint.x, startPoint.y, aimPoint.z);
            Vector3 wallDirection = wallEnd - startPoint;
            float wallDistance = wallDirection.magnitude;
            if (wallDistance > 0.001f) wallDirection /= wallDistance;
            else wallDirection = transform.forward;

            WallPenetration.WallSegment[] segments = GatherWallSegments(startPoint, wallDirection, wallDistance,
                out Vector3 wallEntryPoint, out Vector3 wallEntryNormal, out bool stoppedInside, out string surfaceName);

            WallPenetration.GetTravel(segments, out float travelSpan, out float travelBudget);
            float travelFraction = stoppedInside
                ? float.PositiveInfinity
                : WallPenetration.Fraction(travelSpan, travelBudget);

            if (WallPenetration.IsBlocked(travelFraction))
            {
                // Decal should sit flush against the wall face, not tilt with
                // whatever angle the shot came in at — use the face's own normal.
                result.point = wallEntryPoint;
                result.decalDirection = -wallEntryNormal;
                return result;
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
                result.decalDirection = -groundNormal;
                return result;
            }

            // Proximity damage rings on the XZ plane around the target's rewound
            // position — where the shooter saw them, not where they are now.
            float xzDistance = Vector2.Distance(new Vector2(aimPoint.x, aimPoint.z),
                                                new Vector2(targetPos.x, targetPos.z));

            HitZone hitZone;
            if (xzDistance < HitZoneRadii.Head) hitZone = HitZone.Head;
            else if (xzDistance < HitZoneRadii.Body) hitZone = HitZone.Body;
            else hitZone = HitZone.Leg;

            // Base damage falls off with the shooter→target distance for banded
            // weapons (shotguns), then by how much of the surface's travel budget
            // the shot spent getting through it. Penetration never zeroes a hit:
            // each level bottoms out on its own floor (see WallPenetration).
            float xzTravel = Vector2.Distance(new Vector2(startPoint.x, startPoint.z),
                                              new Vector2(targetPos.x, targetPos.z));
            float penetrationMultiplier = WallPenetration.DamageMultiplier(weapon.wallPenetration, travelFraction);
            float damage = DamageFalloff.GetZoneDamage(weapon, xzTravel, hitZone) * penetrationMultiplier;

            // The one number the source testing could not supply is how far a
            // bullet may travel through each material, so a wall-bang reports its
            // own arithmetic and the surface assets get tuned against it.
            if (travelSpan > 0f)
            {
                result.penetrationLog = $"pen {surfaceName} {travelSpan:F2}/{travelBudget:F2}m "
                    + $"t={travelFraction:F2} x{penetrationMultiplier:F2}";
            }

            // Jump peeking is rewarded with a flat damage reduction while airborne,
            // judged at the same rewound moment as the rest of the hit resolution.
            if (!targetGrounded) damage *= 0.5f;

            result.target = target;
            result.damage = damage;
            result.zone = hitZone;
            // Enemy decals land on top of the capsule (visible from the top-down
            // camera), so project straight down for the same reason as ground hits.
            result.decalDirection = Vector3.down;
            return result;
        }

        /// <summary>
        /// Cosmetic-only: tells every peer where each pellet of a shot ended up
        /// so they can spawn tracers/decals. Every point reflects a
        /// server-decided outcome (blocked-by-wall, miss, or confirmed hit), so
        /// this carries no new combat trust — only what gets drawn. One batched
        /// RPC per trigger pull, whatever the pellet count.
        /// </summary>
        private struct PelletImpact : INetworkSerializable
        {
            public Vector3 point;
            public Vector3 decalDirection;
            public bool isEnemyHit;
            public NetworkObjectReference targetRef; // default when not an enemy hit

            public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
            {
                serializer.SerializeValue(ref point);
                serializer.SerializeValue(ref decalDirection);
                serializer.SerializeValue(ref isEnemyHit);
                serializer.SerializeValue(ref targetRef);
            }
        }

        [Rpc(SendTo.Everyone)]
        private void ShotImpactsClientRpc(Vector3 origin, PelletImpact[] impacts)
        {
            if (HitFxSpawner.Instance == null) return;

            // Shotgun volleys plaster surfaces, so their decals fade sooner.
            float decalLifetime = impacts.Length > 1 ? HitFxSpawner.PelletDecalLifetime : -1f;

            foreach (PelletImpact impact in impacts)
            {
                NetworkObject targetObject = null;
                if (impact.isEnemyHit) impact.targetRef.TryGet(out targetObject);

                HitFxSpawner.Instance.SpawnImpact(origin, impact.point, impact.decalDirection,
                    impact.isEnemyHit, targetObject != null ? targetObject.transform : null, decalLifetime);
            }
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
        /// Every contiguous stretch of Wall-layer geometry along the shot, each
        /// measured as the true path length between its entry and exit face, so
        /// an angled shot that enters a cube's front and leaves through its side
        /// measures the same way as a straight-through one. The arithmetic that
        /// turns these into a damage multiplier lives in <see cref="WallPenetration"/>;
        /// only the Physics queries belong here.
        /// </summary>
        /// <param name="stoppedInside">True when the bullet never exits a wall
        /// before reaching the target, which blocks it outright.</param>
        /// <param name="surfaceName">Name of the most restrictive surface crossed,
        /// for the calibration log.</param>
        private static WallPenetration.WallSegment[] GatherWallSegments(Vector3 startPoint, Vector3 direction,
            float distance, out Vector3 firstEntryPoint, out Vector3 firstEntryNormal,
            out bool stoppedInside, out string surfaceName)
        {
            int wallLayerMask = Tactics.Vision.VisionLayerMasks.WallOnly;

            firstEntryPoint = startPoint + direction * distance;
            firstEntryNormal = -direction;
            stoppedInside = false;
            surfaceName = "Default";

            // Raycasts only report front faces, so exit faces are found by casting
            // the same segment in reverse from the target end.
            RaycastHit[] entries = Physics.RaycastAll(startPoint, direction, distance, wallLayerMask);
            if (entries.Length == 0) return System.Array.Empty<WallPenetration.WallSegment>();
            Vector3 endPoint = startPoint + direction * distance;
            RaycastHit[] exits = Physics.RaycastAll(endPoint, -direction, distance, wallLayerMask);

            var segments = new WallPenetration.WallSegment[entries.Length];
            float closestEntryDistance = float.PositiveInfinity;
            float tightestBudget = float.PositiveInfinity;

            for (int i = 0; i < entries.Length; i++)
            {
                RaycastHit entry = entries[i];
                if (entry.distance < closestEntryDistance)
                {
                    closestEntryDistance = entry.distance;
                    firstEntryPoint = entry.point;
                    firstEntryNormal = entry.normal;
                }

                // Both distances measured from startPoint along the shot.
                float exitDistance = -1f;
                foreach (var exit in exits)
                {
                    if (exit.collider != entry.collider) continue;
                    float candidate = distance - exit.distance;
                    if (candidate > entry.distance && candidate > exitDistance) exitDistance = candidate;
                }

                // No exit before the target: the bullet ends inside this wall.
                if (exitDistance < 0f)
                {
                    stoppedInside = true;
                    return null;
                }

                float budget = WallPenetration.DefaultMaxTravelMeters;
                string surface = "Default";
                if (entry.collider.TryGetComponent(out Tactics.Map.WallSurface wallSurface))
                {
                    budget = wallSurface.MaxTravelMeters(WallPenetration.DefaultMaxTravelMeters);
                    surface = wallSurface.SurfaceName;
                }
                if (budget < tightestBudget)
                {
                    tightestBudget = budget;
                    surfaceName = surface;
                }

                segments[i] = new WallPenetration.WallSegment
                {
                    entryDistance = entry.distance,
                    exitDistance = exitDistance,
                    maxTravelMeters = budget,
                };
            }

            return segments;
        }

        [Rpc(SendTo.Owner)]
        private void HitConfirmOwnerRpc(int damage, HitZone hitZone, int pelletsHit, int pelletsFired)
        {
            Debug.Log($"[Damage] Hit confirmed: {damage} ({hitZone}, {pelletsHit}/{pelletsFired} pellets)");
        }
    }
}
