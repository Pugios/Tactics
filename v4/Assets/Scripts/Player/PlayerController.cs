using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using Tactics.Vision;

namespace Tactics.Player
{
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : NetworkBehaviour
    {
        private UnityEngine.Camera mainCamera;

        private InputAction moveAction;
        private InputAction walkAction;
        private InputAction crouchAction;
        private InputAction jumpAction;
        private InputAction centerCameraAction;
        private InputAction altFireAction;

        private Tactics.Weapons.WeaponInventory weaponInventory;

        private Vector2 moveInput;
        private bool isWalking;
        private bool isCrouching;
        private int adsZoomLevel;
        private bool jumpQueued;

        public Vector2 MoveInput => moveInput;
        public bool IsWalking => isWalking;
        public bool IsCrouching => isCrouching;

        /// <summary>
        /// Owner-local ADS state: nonzero zoom level on an AimDownSight weapon —
        /// hold-mode weapons (Vandal) aim while right click is held, toggle-mode
        /// weapons (Operator) cycle levels per press. Single source of truth
        /// consumed by the movement tick (ADS speed), the weapon's client
        /// cadence gate, and the vision zoom.
        /// </summary>
        public bool IsAiming => adsZoomLevel > 0;

        /// <summary>Tan-space vision zoom for the current ADS level (1 = no zoom).</summary>
        public float CurrentZoomMultiplier =>
            Tactics.Weapons.AdsZoomLogic.ZoomForLevel(
                weaponInventory != null ? weaponInventory.GetActiveWeaponData() : null, adsZoomLevel);

        /// <summary>Drops back to no zoom (weapon switch, reload start).</summary>
        public void ResetAdsZoom() => adsZoomLevel = 0;

        /// <summary>Consumes a queued jump press. Latched between Updates so a tap that
        /// releases before the next network tick still gets seen.</summary>
        public bool ConsumeJumpQueued()
        {
            bool value = jumpQueued;
            jumpQueued = false;
            return value;
        }

        private void Awake()
        {
            mainCamera = UnityEngine.Camera.main;
            weaponInventory = GetComponent<Tactics.Weapons.WeaponInventory>();
            if (visionOrigin != null) visionOriginStandingLocalY = visionOrigin.localPosition.y;
        }

        private void Start()
        {
            moveAction = InputSystem.actions.FindAction("Move");
            walkAction = InputSystem.actions.FindAction("Walk");
            crouchAction = InputSystem.actions.FindAction("Crouch");
            jumpAction = InputSystem.actions.FindAction("Jump");
            centerCameraAction = InputSystem.actions.FindAction("CenterCamera");
            altFireAction = InputSystem.actions.FindAction("AltFire");

            // A toggle zoom must not survive into a different weapon (also fires
            // when a buy lands in the active slot).
            if (weaponInventory != null) weaponInventory.OnActiveWeaponChanged += ResetAdsZoom;
        }

        public override void OnDestroy()
        {
            if (weaponInventory != null) weaponInventory.OnActiveWeaponChanged -= ResetAdsZoom;
            base.OnDestroy();
        }

        public override void OnNetworkSpawn()
        {
            if (!IsOwner)
            {
                enabled = false;
                return;
            }

            var tdCam = UnityEngine.Camera.main != null
                ? UnityEngine.Camera.main.GetComponent<Tactics.Camera.TopDownCamera>()
                : null;
            if (tdCam != null) tdCam.SetTarget(transform);

            // You must always see yourself — a bad LOS ray to your own capsule
            // (e.g. crouched near a corner) shouldn't cull your own renderer.
            var visibleEntity = GetComponent<VisibleEntity>();
            if (visibleEntity != null) visibleEntity.SetAlwaysVisible(true);
        }

        private void Update()
        {
            HandleCenterCamera();
            HandleRotation();
            SampleInput();
        }

        private void HandleCenterCamera()
        {
            if (centerCameraAction != null && centerCameraAction.IsPressed())
            {
                // tdCam = Script attached to Main Camera | Handles Rotation/Movement
                var tdCam = mainCamera.GetComponent<Tactics.Camera.TopDownCamera>();
                if (tdCam != null)
                {
                    if (centerCameraAction.WasPressedThisFrame())
                    {
                        // 1. Capture intended direction BEFORE warping mouse
                        Vector3 targetDirection = transform.forward;
                        Ray ray = mainCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
                        Plane groundPlane = new Plane(Vector3.up, transform.position);
                        // Ray against virtual infinite ground plane to allow rotation even when aiming at nothing/walls
                        if (groundPlane.Raycast(ray, out float enter))
                        {
                            Vector3 hitPoint = ray.GetPoint(enter);
                            targetDirection = (hitPoint - transform.position).normalized;
                            targetDirection.y = 0;
                        }

                        float targetAngle = Quaternion.LookRotation(targetDirection).eulerAngles.y;

                        // 2. Warp mouse to top-center of screen
                        Vector2 warpPos = new Vector2(Screen.width * 0.5f, Screen.height * 0.9f);
                        Mouse.current.WarpCursorPosition(warpPos);

                        // 3. Smoothly align to that specific direction
                        tdCam.QuickAlign(targetAngle, 0.1f);
                    }
                    else
                    {
                        // Smoothly follow while holding
                        tdCam.SetTargetRotation(transform.eulerAngles.y);
                    }
                }
            }
        }

        [Header("Vision")]
        [SerializeField] private Transform visionOrigin;
        // 10cm below the top of the head: standing head-top is 2m (eyeHeight = 1.9),
        // crouched head-top is 1.5m (crouchEyeHeight = 1.4) — see PlayerCrouchVisuals'
        // crouchVisibilityHeight and PlayerMovementNetwork's crouchControllerHeight,
        // which define those same two head-top heights.
        [SerializeField] private float eyeHeight = 1.9f;
        [SerializeField] private float crouchEyeHeight = 1.4f;

        public Transform VisionOrigin => visionOrigin;

        /// <summary>
        /// Eye height above the player's feet for the given stance. The server
        /// measures wall penetration and draws tracers from here, so it reads the
        /// same two numbers the vision origin uses rather than duplicating them.
        /// </summary>
        public float EyeHeightFor(bool crouched) => crouched ? crouchEyeHeight : eyeHeight;

        private float visionOriginStandingLocalY;

        private Vector3 lookTarget;
        private Vector3 aimGroundPoint;

        public Vector3 LookTarget => lookTarget;
        public Vector3 AimGroundPoint => aimGroundPoint;

        private void HandleRotation()
        {
            Ray ray = mainCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
            if (!TryGetMouseGroundPoint(ray, out Vector3 groundPoint))
                return;

            float currentEyeHeight = isCrouching ? crouchEyeHeight : eyeHeight;

            if (visionOrigin != null)
            {
                Vector3 localPos = visionOrigin.localPosition;
                localPos.y = visionOriginStandingLocalY - (eyeHeight - currentEyeHeight);
                visionOrigin.localPosition = localPos;
            }

            lookTarget = groundPoint + Vector3.up * currentEyeHeight;
            aimGroundPoint = groundPoint;

            Vector3 direction = groundPoint - transform.position;
            direction.y = 0;

            if (direction.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.LookRotation(direction);

            if (visionOrigin != null)
            {
                Vector3 visionDir = lookTarget - visionOrigin.position;
                if (visionDir.sqrMagnitude > 0.01f)
                    visionOrigin.rotation = Quaternion.LookRotation(visionDir);
            }
        }

        private bool TryGetMouseGroundPoint(Ray ray, out Vector3 groundPoint)
        {
            if (Physics.Raycast(ray, out RaycastHit hit, 1000f, VisionLayerMasks.GroundOnly))
            {
                groundPoint = hit.point;
                return true;
            }

            Plane groundPlane = new Plane(Vector3.up, transform.position);
            if (groundPlane.Raycast(ray, out float enter))
            {
                groundPoint = ray.GetPoint(enter);
                return true;
            }

            groundPoint = Vector3.zero;
            return false;
        }

        private void SampleAdsInput()
        {
            var weapon = weaponInventory != null ? weaponInventory.GetActiveWeaponData() : null;
            int levelCount = Tactics.Weapons.AdsZoomLogic.LevelCount(weapon);

            if (levelCount == 0 || altFireAction == null)
            {
                adsZoomLevel = 0;
            }
            else if (weapon.adsMode == Tactics.Weapons.AdsMode.Hold)
            {
                adsZoomLevel = altFireAction.IsPressed() ? 1 : 0;
            }
            else if (altFireAction.WasPressedThisFrame() && !weaponInventory.IsEquipping)
            {
                adsZoomLevel = Tactics.Weapons.AdsZoomLogic.NextToggleLevel(adsZoomLevel, levelCount);
            }
        }

        private void SampleInput()
        {
            moveInput = moveAction.ReadValue<Vector2>();
            isWalking = walkAction.IsPressed();
            isCrouching = crouchAction.IsPressed();
            SampleAdsInput();
            if (jumpAction != null && jumpAction.WasPressedThisFrame()) jumpQueued = true;
        }
    }
}
