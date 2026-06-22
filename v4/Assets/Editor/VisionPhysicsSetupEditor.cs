using UnityEditor;
using UnityEngine;
using Tactics.Vision;

namespace Tactics.Editor
{
    /// <summary>
    /// Persists VisionGround as a raycast-only layer in the project physics matrix.
    /// </summary>
    [InitializeOnLoad]
    public static class VisionPhysicsSetupEditor
    {
        static VisionPhysicsSetupEditor()
        {
            ApplyIfNeeded();
        }

        [MenuItem("Tactics/Vision/Apply Vision Ground Physics")]
        public static void ApplyFromMenu()
        {
            ApplyIfNeeded(force: true);
        }

        internal static void ApplyIfNeeded(bool force = false)
        {
            int visionGround = VisionLayerMasks.VisionGround;
            bool changed = false;

            for (int layer = 0; layer < 32; layer++)
            {
                if (!force && Physics.GetIgnoreLayerCollision(visionGround, layer))
                    continue;

                Physics.IgnoreLayerCollision(visionGround, layer, true);
                changed = true;
            }

            if (!changed)
                return;

            Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/DynamicsManager.asset");
            if (assets != null && assets.Length > 0)
                EditorUtility.SetDirty(assets[0]);
        }
    }
}
