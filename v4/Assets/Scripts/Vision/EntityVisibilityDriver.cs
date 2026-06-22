using System.Collections.Generic;
using UnityEngine;

namespace Tactics.Vision
{
    /// <summary>
    /// Hides or shows VisibleEntity renderers each frame using the shared vision query.
    /// </summary>
    [RequireComponent(typeof(VisionController))]
    [DefaultExecutionOrder(101)]
    public class EntityVisibilityDriver : MonoBehaviour
    {
        private VisionController visionController;

        private void Awake()
        {
            visionController = GetComponent<VisionController>();
        }

        private void LateUpdate()
        {
            IReadOnlyList<VisibleEntity> entities = VisibleEntity.All;

            if (visionController == null || !visionController.HasValidAim)
            {
                HideAll(entities);
                return;
            }

            for (int i = 0; i < entities.Count; i++)
            {
                VisibleEntity entity = entities[i];
                if (entity == null)
                    continue;

                bool visible = visionController.IsEnemyVisibleAt(
                    entity.FeetPosition,
                    entity.HeightOverride,
                    entity.RadiusOverride);

                entity.SetVisible(visible);
            }
        }

        private static void HideAll(IReadOnlyList<VisibleEntity> entities)
        {
            for (int i = 0; i < entities.Count; i++)
            {
                if (entities[i] != null)
                    entities[i].SetVisible(false);
            }
        }
    }
}
