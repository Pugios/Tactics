using UnityEngine;
using UnityEngine.InputSystem;
using Tactics.Vision;

namespace Tactics.Player
{
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        [Header("Movement Settings")]
        [SerializeField] private float runSpeed = 5.4f; // Typical Valorant speed
        [SerializeField] private float walkSpeedMultiplier = 0.5f;
        [SerializeField] private float crouchSpeedMultiplier = 0.3f;
        [SerializeField] private float gravity = -9.81f;

        private CharacterController characterController;
        private UnityEngine.Camera mainCamera;

        private InputAction moveAction;
        private InputAction walkAction;
        private InputAction crouchAction;
        private InputAction centerCameraAction;

        private Vector2 moveInput;
        private bool isWalking;
        private bool isCrouching;
        private float verticalVelocity;

        private Tactics.Sound.SoundEmitter soundEmitter;

        private void Awake()
        {
            characterController = GetComponent<CharacterController>();
            mainCamera = UnityEngine.Camera.main;
            soundEmitter = GetComponent<Tactics.Sound.SoundEmitter>();
        }

        private void Start()
        {
            moveAction = InputSystem.actions.FindAction("Move");
            walkAction = InputSystem.actions.FindAction("Walk");
            crouchAction = InputSystem.actions.FindAction("Crouch");
            centerCameraAction = InputSystem.actions.FindAction("CenterCamera");
        }

        private void Update()
        {
            HandleCenterCamera();
            HandleRotation();
            HandleMovement();
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
        [SerializeField] private float eyeHeight = 1.5f;

        public Transform VisionOrigin => visionOrigin;
        
        private Vector3 lookTarget;
        public Vector3 LookTarget => lookTarget;

        private void HandleRotation()
        {
            int AimRaycastMask = Physics.AllLayers & ~VisionLayerMasks.VisionGroundOnly;
            Ray ray = mainCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
            // Raycast against everything to find the 3D point under mouse
            if (Physics.Raycast(ray, out RaycastHit hit, 1000f, AimRaycastMask))
            {
                lookTarget = hit.point + Vector3.up * eyeHeight;
                
                // Body rotation (Y only)
                Vector3 direction = hit.point - transform.position;
                direction.y = 0;

                if (direction.sqrMagnitude > 0.01f)
                {
                    transform.rotation = Quaternion.LookRotation(direction);
                }

                // Vision origin rotation (3D)
                if (visionOrigin != null)
                {
                    Vector3 visionDir = lookTarget - visionOrigin.position;
                    if (visionDir.sqrMagnitude > 0.01f)
                    {
                        visionOrigin.rotation = Quaternion.LookRotation(visionDir);
                    }
                }
            }
            else
            {
                // Fallback to virtual infinite ground plane if no hit
                Plane groundPlane = new Plane(Vector3.up, transform.position);
                if (groundPlane.Raycast(ray, out float enter))
                {
                    Vector3 hitPoint = ray.GetPoint(enter);
                    lookTarget = hitPoint + Vector3.up * eyeHeight;
                    
                    Vector3 direction = hitPoint - transform.position;
                    direction.y = 0;

                    if (direction.sqrMagnitude > 0.01f)
                    {
                        transform.rotation = Quaternion.LookRotation(direction);
                    }
                    
                    if (visionOrigin != null)
                    {
                        Vector3 visionDir = lookTarget - visionOrigin.position;
                        if (visionDir.sqrMagnitude > 0.01f)
                        {
                            visionOrigin.rotation = Quaternion.LookRotation(visionDir);
                        }
                    }
                }
            }
        }

        private void HandleMovement()
        {
            moveInput = moveAction.ReadValue<Vector2>();
            isWalking = walkAction.IsPressed();
            isCrouching = crouchAction.IsPressed();

            float currentSpeed = runSpeed;
            if (isCrouching) currentSpeed *= crouchSpeedMultiplier;
            else if (isWalking) currentSpeed *= walkSpeedMultiplier;

            // Move relative to player facing
            Vector3 movement = (transform.forward * moveInput.y + transform.right * moveInput.x).normalized;

            if (characterController.isGrounded)
            {
                verticalVelocity = -0.5f; // Keep grounded
                
                // Sound logic
                if (movement.sqrMagnitude > 0.01f && !isWalking && !isCrouching)
                {
                    if (soundEmitter != null) soundEmitter.EmitMoveSound(true);
                }
            }
            else
            {
                verticalVelocity += gravity * Time.deltaTime;
            }

            Vector3 finalMove = movement * currentSpeed + Vector3.up * verticalVelocity;
            characterController.Move(finalMove * Time.deltaTime);
        }
    }
}
