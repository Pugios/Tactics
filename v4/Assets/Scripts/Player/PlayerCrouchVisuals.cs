using UnityEngine;
using Tactics.Vision;

namespace Tactics.Player
{
    /// <summary>
    /// Drives the crouch presentation: shrinks the player's visual mesh and the
    /// capsule fed to the CPU-layer entity-visibility check. Runs for every
    /// instance (owner and remote copies alike) since every peer needs to see
    /// every player's crouch pose and evaluate their shrunk capsule.
    /// </summary>
    [RequireComponent(typeof(PlayerMovementNetwork))]
    [RequireComponent(typeof(CharacterController))]
    public class PlayerCrouchVisuals : MonoBehaviour
    {
        [SerializeField] private Transform visualRoot;
        [SerializeField] private VisibleEntity visibleEntity;
        [SerializeField] private float standingVisualScaleY = 1f;
        [SerializeField] private float crouchVisualScaleY = 0.6f;
        [SerializeField] private float crouchVisibilityHeight = 1.2f;

        private PlayerMovementNetwork movementNetwork;
        private CharacterController characterController;
        private Vector3 standingLocalPosition;

        // Captured once at Awake: the CharacterController's own height/center now
        // change dynamically for crouch collision (PlayerMovementNetwork.ApplyCrouchCollider),
        // so this can't read characterController.height/center live — it needs the
        // fixed standing baseline to compute the anchor.
        private float standingControllerHeight;
        private Vector3 standingControllerCenter;

        private void Awake()
        {
            movementNetwork = GetComponent<PlayerMovementNetwork>();
            characterController = GetComponent<CharacterController>();
            if (visualRoot != null) standingLocalPosition = visualRoot.localPosition;
            standingControllerHeight = characterController.height;
            standingControllerCenter = characterController.center;
        }

        private void LateUpdate()
        {
            bool crouching = movementNetwork.CurrentIsCrouching;

            if (visualRoot != null)
            {
                float scaleY = crouching ? crouchVisualScaleY : standingVisualScaleY;
                visualRoot.localScale = new Vector3(visualRoot.localScale.x, scaleY, visualRoot.localScale.z);

                float targetLocalY = standingLocalPosition.y;
                if (crouching)
                {
                    // Anchor the shrunk mesh's bottom to the CharacterController's
                    // standing ground contact point, not to the standing mesh's own
                    // (already elevated) bottom edge — otherwise it stays floating.
                    // The mesh's unscaled half-height equals the standing capsule's
                    // half-height (that's what standingVisualScaleY = 1 means), so
                    // after scaling by scaleY its half-height becomes that value
                    // times scaleY — NOT scaleY * 0.5, which only halves correctly
                    // if the unscaled mesh were already exactly 1m tall.
                    float groundLocalY = standingControllerCenter.y - standingControllerHeight * 0.5f;
                    float standingHalfHeight = standingControllerHeight * 0.5f / standingVisualScaleY;
                    targetLocalY = groundLocalY + standingHalfHeight * scaleY;
                }
                visualRoot.localPosition = new Vector3(standingLocalPosition.x, targetLocalY, standingLocalPosition.z);
            }

            if (visibleEntity != null)
                visibleEntity.SetHeightOverride(crouching ? crouchVisibilityHeight : -1f);
        }
    }
}
