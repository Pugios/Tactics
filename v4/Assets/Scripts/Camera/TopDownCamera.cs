using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;

namespace ValorantTrainer.Camera
{
    public class TopDownCamera : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private float minHeight = 12f;
        [SerializeField] private float maxHeight = 100f;
        [SerializeField] private float maxZoomDistance = 25f;
        [SerializeField, Range(0, 1)] private float lookWeight = 0.4f;
        [SerializeField] private float smoothness = 0.125f;
        [SerializeField] private float rotationSmoothness = 0.1f;
        [SerializeField] private float zoomSensitivity = 5f;

        private Vector3 velocity = Vector3.zero;
        private float targetRotationY = 0f;
        private float currentRotationY = 0f;
        private float rotationVelocity = 0f;
        private UnityEngine.Camera cam;

        private InputAction zoomAction;
        private float lockedHeight;
        private Coroutine alignCoroutine;

        private void Awake()
        {
            cam = GetComponent<UnityEngine.Camera>();
            currentRotationY = transform.eulerAngles.y;
            targetRotationY = currentRotationY;
            lockedHeight = minHeight;
        }

        private void Start()
        {
            zoomAction = InputSystem.actions.FindAction("Zoom");
        }

        private void LateUpdate()
        {
            if (target == null || alignCoroutine != null) return;

            // 1. Find mouse world position
            Vector3 mouseWorldPos = GetMouseWorldPosition();
            
            // 2. Calculate camera target position (midpoint between player and mouse)
            Vector3 lookAtPoint = Vector3.Lerp(target.position, mouseWorldPos, lookWeight);
            
            // 3. Handle Zoom via Mouse Wheel
            if (zoomAction != null)
            {
                float scrollValue = zoomAction.ReadValue<Vector2>().y;
                if (Mathf.Abs(scrollValue) > 0.1f)
                {
                    // Scroll Up (Positive) -> Decrease Height (Zoom In)
                    // Scroll Down (Negative) -> Increase Height (Zoom Out)
                    lockedHeight -= scrollValue * zoomSensitivity;
                    lockedHeight = Mathf.Clamp(lockedHeight, minHeight, maxHeight);
                }
            }

            // 4. Smoothly move camera
            Vector3 targetPosition = new Vector3(lookAtPoint.x, lockedHeight, lookAtPoint.z);
            transform.position = Vector3.SmoothDamp(transform.position, targetPosition, ref velocity, smoothness);
            
            // 5. Smoothly rotate camera
            currentRotationY = Mathf.SmoothDampAngle(currentRotationY, targetRotationY, ref rotationVelocity, rotationSmoothness);
            transform.rotation = Quaternion.Euler(90f, currentRotationY, 0f);
        }

        private Vector3 GetMouseWorldPosition()
        {
            Ray ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
            Plane groundPlane = new Plane(Vector3.up, target.position);

            if (groundPlane.Raycast(ray, out float enter))
            {
                return ray.GetPoint(enter);
            }
            return target.position;
        }

        public void SetTargetRotation(float yRotation)
        {
            targetRotationY = yRotation;
        }

        public void QuickAlign(float targetAngle, float duration)
        {
            if (alignCoroutine != null) StopCoroutine(alignCoroutine);
            alignCoroutine = StartCoroutine(AlignRoutine(targetAngle, duration));
        }

        private IEnumerator AlignRoutine(float targetAngle, float duration)
        {
            float elapsed = 0f;
            Vector3 startPos = transform.position;
            float startRot = currentRotationY;

            while (elapsed < duration)
            {
                if (target == null) break;

                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0, 1, elapsed / duration);

                // Target rotation is the fixed angle we captured at the start
                currentRotationY = Mathf.LerpAngle(startRot, targetAngle, t);
                targetRotationY = targetAngle;

                // Target position derived from current player position and the intended target direction
                float zoomDist = maxZoomDistance * 0.5f;
                // Use the target angle to determine the "forward" vector for centering
                Vector3 targetForward = Quaternion.Euler(0, targetAngle, 0) * Vector3.forward;
                Vector3 mouseForwardWorld = target.position + targetForward * zoomDist;
                Vector3 lookAtPoint = Vector3.Lerp(target.position, mouseForwardWorld, lookWeight);
                Vector3 targetPosition = new Vector3(lookAtPoint.x, lockedHeight, lookAtPoint.z);

                transform.position = Vector3.Lerp(startPos, targetPosition, t);
                transform.rotation = Quaternion.Euler(90f, currentRotationY, 0f);

                yield return null;
            }

            velocity = Vector3.zero;
            rotationVelocity = 0f;
            alignCoroutine = null;
        }

        public void SetTarget(Transform newTarget) => target = newTarget;
    }
}
