using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;

namespace Tactics.Camera
{
    // Runs after gameplay LateUpdates and before CrosshairController (160), so
    // the crosshair can read this frame's warped cursor position.
    [DefaultExecutionOrder(150)]
    public class TopDownCamera : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private float minHeight = 12f;
        [SerializeField] private float maxHeight = 100f;
        [SerializeField] private float maxZoomDistance = 25f;
        [SerializeField, Range(0, 1)] private float lookWeight = 0.4f;
        [Tooltip("Look weight the camera approaches at very high ADS zoom; actual weight scales with the weapon's zoom level.")]
        [SerializeField, Range(0, 1)] private float adsMaxLookWeight = 0.85f;
        [SerializeField] private float adsBiasSmoothTime = 0.15f;
        [Tooltip("The ADS bias is capped so the player model may touch the view edge but never leave the screen; this margin (world meters on the player's plane) accounts for the model's size.")]
        [SerializeField] private float playerVisibilityMarginMeters = 0.7f;
        [SerializeField] private float smoothness = 0.125f;
        [SerializeField] private float rotationSmoothness = 0.1f;
        [SerializeField] private float zoomSensitivity = 5f;

        [Header("ADS Cursor Compensation")]
        [Tooltip("Warp the OS cursor each frame so the aimed world point is unaffected by the ADS camera bias.")]
        [SerializeField] private bool compensateCursorDuringAds = true;
        [SerializeField] private float warpPixelThreshold = 0.5f;
        [SerializeField] private float screenEdgeMarginPixels = 2f;
        [Tooltip("Skip warping on frames where the bias displacement exceeds this (meters) — teleport-scale jumps.")]
        [SerializeField] private float biasDeltaSanityCap = 2f;

        private Tactics.Player.PlayerController targetController;
        private float currentLookWeight;
        private float lookWeightVelocity;

        // Where the camera would be with no ADS bias. While the look weight sits
        // at baseline this is a bit-exact copy of the real camera (so the bias
        // delta is exactly zero and no warp ever fires); it only simulates
        // independently while the weight is diverged.
        private Vector3 shadowPosition;
        private Vector3 shadowVelocity;
        private bool shadowDiverged;

        private Vector2 warpedCursorScreenPos;
        private int warpedCursorFrame = -1;

        private const float WeightSnapEpsilon = 1e-4f;
        private const float WeightVelSnapEpsilon = 1e-3f;
        private const float ResnapPosEpsilonSq = 1e-6f; // (1 mm)^2
        private const float ResnapVelEpsilonSq = 1e-6f;

        private Vector3 velocity = Vector3.zero;
        private float targetRotationY = 0f;
        private float currentRotationY = 0f;
        private float rotationVelocity = 0f;
        private UnityEngine.Camera cam;

        private InputAction zoomAction;
        private float lockedHeight;
        private Coroutine alignCoroutine;

        /// <summary>
        /// Screen position the cursor was warped to this frame, if any.
        /// Mouse.current.position won't reflect a warp until the next input
        /// update, so same-frame consumers (the crosshair) should prefer this
        /// when fresh to avoid a one-frame stutter during ADS transitions.
        /// </summary>
        public Vector2? WarpedCursorScreenPosThisFrame =>
            warpedCursorFrame == Time.frameCount ? warpedCursorScreenPos : (Vector2?)null;

        private void Awake()
        {
            cam = GetComponent<UnityEngine.Camera>();
            currentRotationY = transform.eulerAngles.y;
            targetRotationY = currentRotationY;
            lockedHeight = minHeight;
            currentLookWeight = lookWeight;
            shadowPosition = transform.position;
        }

        private void Start()
        {
            zoomAction = InputSystem.actions.FindAction("Zoom");
        }

        private void LateUpdate()
        {
            if (target == null || alignCoroutine != null) return;

            UpdateAdsLookWeight();
            UpdateDivergenceState();

            // One read serves both framing and compensation: this is the world
            // point under the cursor via the PRE-move camera.
            Vector3 mouseWorldPos = GetMouseWorldPosition();

            // Handle Zoom via Mouse Wheel (shared height input for both cameras)
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

            Vector3 shadowOld = shadowPosition;
            if (shadowDiverged)
            {
                shadowPosition = CameraFollowMath.Step(shadowPosition, ref shadowVelocity,
                    target.position, mouseWorldPos, lookWeight, lockedHeight, smoothness, Time.deltaTime);
            }

            Vector3 actualOld = transform.position;
            transform.position = CameraFollowMath.Step(transform.position, ref velocity,
                target.position, mouseWorldPos, ComputeAppliedLookWeight(mouseWorldPos), lockedHeight, smoothness, Time.deltaTime);

            if (!shadowDiverged)
            {
                shadowPosition = transform.position;
                shadowVelocity = velocity;
            }

            currentRotationY = Mathf.SmoothDampAngle(currentRotationY, targetRotationY, ref rotationVelocity, rotationSmoothness);
            transform.rotation = Quaternion.Euler(90f, currentRotationY, 0f);

            // After the transform is final, so WorldToScreenPoint uses the new pose.
            if (shadowDiverged)
                TryWarpCursor(mouseWorldPos, shadowPosition - shadowOld, transform.position - actualOld);
        }

        // While aiming, bias the framing toward the crosshair so the player model
        // drifts toward the screen edge; deeper zoom (Operator level 2) pushes
        // closer to adsMaxLookWeight. SmoothDamp is asymptotic, so snap back to
        // baseline once settled — divergence (and cursor warping) must formally end.
        private void UpdateAdsLookWeight()
        {
            float zoom = targetController != null ? targetController.CurrentZoomMultiplier : 1f;
            float targetWeight = CameraFollowMath.TargetLookWeight(lookWeight, adsMaxLookWeight, zoom);
            currentLookWeight = Mathf.SmoothDamp(currentLookWeight, targetWeight, ref lookWeightVelocity, adsBiasSmoothTime);

            if (targetWeight == lookWeight
                && Mathf.Abs(currentLookWeight - lookWeight) < WeightSnapEpsilon
                && Mathf.Abs(lookWeightVelocity) < WeightVelSnapEpsilon)
            {
                currentLookWeight = lookWeight;
                lookWeightVelocity = 0f;
            }
        }

        // Cap the ADS bias so the player model never leaves the screen. Applied
        // at use time (not on the smoothed weight) so it reacts instantly when
        // the cursor swings outward; the position SmoothDamp smooths the result.
        // Floored at the base lookWeight so the non-ADS exact-copy regime (and
        // its never-warp guarantee) stays bit-identical.
        private float ComputeAppliedLookWeight(Vector3 mouseWorldPos)
        {
            GetViewHalfExtents(out float halfWidth, out float halfHeight);
            float maxW = CameraFollowMath.MaxLookWeightForVisibility(target.position, mouseWorldPos,
                currentRotationY, halfWidth, halfHeight, playerVisibilityMarginMeters);
            return Mathf.Min(currentLookWeight, Mathf.Max(maxW, lookWeight));
        }

        // View half extents on the player's ground plane, from the framing goal
        // height (lockedHeight) rather than the lagging transform Y.
        private void GetViewHalfExtents(out float halfWidth, out float halfHeight)
        {
            if (cam.orthographic)
            {
                halfHeight = cam.orthographicSize;
            }
            else
            {
                float planeDistance = Mathf.Max(0f, lockedHeight - target.position.y);
                halfHeight = planeDistance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            }
            halfWidth = halfHeight * cam.aspect;
        }

        private void UpdateDivergenceState()
        {
            if (currentLookWeight != lookWeight)
            {
                shadowDiverged = true;
                return;
            }
            if (!shadowDiverged) return;

            // Weight is back at baseline; re-unify once the shadow has caught up
            // so the steady state returns to the exact-copy (never-warp) regime.
            if ((shadowPosition - transform.position).sqrMagnitude < ResnapPosEpsilonSq
                && (shadowVelocity - velocity).sqrMagnitude < ResnapVelEpsilonSq)
            {
                ResyncShadowToActual();
            }
        }

        private void ResyncShadowToActual()
        {
            shadowPosition = transform.position;
            // Copy, don't zero — keeps the shared height axis bit-identical.
            shadowVelocity = velocity;
            shadowDiverged = false;
        }

        /// <summary>
        /// Warps the OS cursor so the aimed world point moves only by the
        /// shadow (no-bias) camera's motion — the ADS bias never shifts aim.
        /// Per-frame delta on the live cursor, re-derived from absolute world
        /// positions each frame, so compensation error can never accumulate.
        /// </summary>
        private void TryWarpCursor(Vector3 aimBeforeMove, Vector3 shadowDelta, Vector3 actualDelta)
        {
            if (!compensateCursorDuringAds) return;
            if (Mouse.current == null || !Application.isFocused) return;
            // Never warp a visible OS cursor out from under a menu.
            if (Tactics.UI.CrosshairController.MenuOpen) return;
            if ((actualDelta - shadowDelta).sqrMagnitude > biasDeltaSanityCap * biasDeltaSanityCap) return;

            Vector3 desired = CameraFollowMath.CompensatedAimPoint(aimBeforeMove, shadowDelta);
            Vector3 screen = cam.WorldToScreenPoint(desired);
            if (screen.z <= 0f)
            {
                ResyncShadowToActual();
                return;
            }

            // Clamped remainder simply drifts as it would without compensation;
            // the cursor must never leave the window in windowed mode.
            Vector2 clamped = new Vector2(
                Mathf.Clamp(screen.x, screenEdgeMarginPixels, Screen.width - screenEdgeMarginPixels),
                Mathf.Clamp(screen.y, screenEdgeMarginPixels, Screen.height - screenEdgeMarginPixels));

            if ((clamped - Mouse.current.position.ReadValue()).sqrMagnitude
                < warpPixelThreshold * warpPixelThreshold) return;

            Mouse.current.WarpCursorPosition(clamped);
            warpedCursorScreenPos = clamped;
            warpedCursorFrame = Time.frameCount;
        }

        private Vector3 GetMouseWorldPosition()
        {
            if (Mouse.current == null) return target.position;

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
                Vector3 lookAtPoint = Vector3.Lerp(target.position, mouseForwardWorld, currentLookWeight);
                Vector3 targetPosition = new Vector3(lookAtPoint.x, lockedHeight, lookAtPoint.z);

                transform.position = Vector3.Lerp(startPos, targetPosition, t);
                transform.rotation = Quaternion.Euler(90f, currentRotationY, 0f);

                yield return null;
            }

            velocity = Vector3.zero;
            rotationVelocity = 0f;
            // The align moved the camera outside the follow sim (and CenterCamera
            // warped the cursor itself) — restart compensation from a clean slate.
            ResyncShadowToActual();
            alignCoroutine = null;
        }

        public void SetTarget(Transform newTarget)
        {
            target = newTarget;
            targetController = newTarget != null
                ? newTarget.GetComponent<Tactics.Player.PlayerController>()
                : null;
            ResyncShadowToActual();
        }
    }
}
