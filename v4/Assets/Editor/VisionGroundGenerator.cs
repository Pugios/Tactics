using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Tactics.Vision;

namespace Tactics.Editor
{
    public static class VisionGroundGenerator
    {
        private const string RootName = "VisionGround";
        private const float RaiseHeight = 2f;

        [MenuItem("Tactics/Vision/Generate Vision Ground")]
        public static void GenerateFromScene()
        {
            Generate(FindGroundColliders(null));
        }

        [MenuItem("Tactics/Vision/Generate Vision Ground From Selection")]
        public static void GenerateFromSelection()
        {
            Generate(FindGroundColliders(Selection.gameObjects));
        }

        [MenuItem("Tactics/Vision/Clear Vision Ground")]
        public static void ClearVisionGround()
        {
            Transform existing = FindExistingRoot();
            if (existing == null)
            {
                Debug.Log("No VisionGround root found in the open scene.");
                return;
            }

            Undo.DestroyObjectImmediate(existing.gameObject);
            MarkSceneDirty();
            Debug.Log("Removed VisionGround root.");
        }

        private static void Generate(IReadOnlyList<Collider> sources)
        {
            if (sources.Count == 0)
            {
                Debug.LogWarning("No Ground-layer colliders found. Assign colliders to the Ground layer first.");
                return;
            }

            Transform root = FindExistingRoot();
            if (root != null)
                Undo.DestroyObjectImmediate(root.gameObject);

            var rootObject = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(rootObject, "Generate Vision Ground");

            int created = 0;
            for (int i = 0; i < sources.Count; i++)
            {
                Collider source = sources[i];
                if (source == null)
                    continue;

                if (CreateRaisedProxy(source, rootObject.transform))
                    created++;
            }

            VisionPhysicsSetupEditor.ApplyIfNeeded(force: true);
            MarkSceneDirty();
            Debug.Log($"Generated {created} VisionGround collider(s) under '{RootName}'.");
        }

        private static bool CreateRaisedProxy(Collider source, Transform root)
        {
            var proxyObject = new GameObject($"VisionGround_{source.gameObject.name}");
            Undo.RegisterCreatedObjectUndo(proxyObject, "Generate Vision Ground");
            proxyObject.transform.SetParent(root, false);
            proxyObject.layer = VisionLayerMasks.VisionGround;
            proxyObject.transform.position = source.transform.position + Vector3.up * RaiseHeight;
            proxyObject.transform.rotation = source.transform.rotation;
            proxyObject.transform.localScale = source.transform.lossyScale;

            switch (source)
            {
                case MeshCollider meshCollider when meshCollider.sharedMesh != null:
                {
                    var proxyCollider = proxyObject.AddComponent<MeshCollider>();
                    proxyCollider.sharedMesh = meshCollider.sharedMesh;
                    proxyCollider.convex = meshCollider.convex;
                    return true;
                }
                case BoxCollider boxCollider:
                {
                    var proxyCollider = proxyObject.AddComponent<BoxCollider>();
                    proxyCollider.center = boxCollider.center;
                    proxyCollider.size = boxCollider.size;
                    return true;
                }
                case SphereCollider sphereCollider:
                {
                    var proxyCollider = proxyObject.AddComponent<SphereCollider>();
                    proxyCollider.center = sphereCollider.center;
                    proxyCollider.radius = sphereCollider.radius;
                    return true;
                }
                case CapsuleCollider capsuleCollider:
                {
                    var proxyCollider = proxyObject.AddComponent<CapsuleCollider>();
                    proxyCollider.center = capsuleCollider.center;
                    proxyCollider.radius = capsuleCollider.radius;
                    proxyCollider.height = capsuleCollider.height;
                    proxyCollider.direction = capsuleCollider.direction;
                    return true;
                }
                default:
                {
                    Bounds bounds = source.bounds;
                    var proxyCollider = proxyObject.AddComponent<BoxCollider>();
                    proxyObject.transform.SetParent(root, false);
                    proxyObject.transform.position = bounds.center + Vector3.up * RaiseHeight;
                    proxyObject.transform.rotation = Quaternion.identity;
                    proxyObject.transform.localScale = Vector3.one;
                    proxyCollider.center = Vector3.zero;
                    proxyCollider.size = bounds.size;
                    return true;
                }
            }
        }

        private static List<Collider> FindGroundColliders(GameObject[] selectionRoots)
        {
            var results = new List<Collider>();
            int groundMask = VisionLayerMasks.GroundOnly;

            if (selectionRoots != null && selectionRoots.Length > 0)
            {
                for (int i = 0; i < selectionRoots.Length; i++)
                {
                    if (selectionRoots[i] == null)
                        continue;

                    Collider[] colliders = selectionRoots[i].GetComponentsInChildren<Collider>(true);
                    for (int c = 0; c < colliders.Length; c++)
                    {
                        Collider collider = colliders[c];
                        if (collider == null || collider.gameObject.layer != VisionLayerMasks.Ground)
                            continue;

                        if (!results.Contains(collider))
                            results.Add(collider);
                    }
                }

                return results;
            }

            Collider[] sceneColliders = Object.FindObjectsByType<Collider>(FindObjectsSortMode.None);
            for (int i = 0; i < sceneColliders.Length; i++)
            {
                Collider collider = sceneColliders[i];
                if (collider == null || collider.gameObject.layer != VisionLayerMasks.Ground)
                    continue;

                if (collider.gameObject.name.StartsWith("VisionGround_"))
                    continue;

                results.Add(collider);
            }

            return results;
        }

        private static Transform FindExistingRoot()
        {
            GameObject existing = GameObject.Find(RootName);
            return existing != null ? existing.transform : null;
        }

        private static void MarkSceneDirty()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.IsValid())
                EditorSceneManager.MarkSceneDirty(scene);
        }
    }
}
