using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Tactics.Map;
using Tactics.Objectives;

namespace Tactics.EditorTools
{
    /// <summary>
    /// Takes over the whole hand-run checklist that used to follow every map
    /// re-export from Blender: colliders, layers, materials, the plant-area
    /// trigger volumes, and the penetration surface each wall is made of.
    /// Re-importing the FBX is now the entire workflow.
    ///
    /// Everything is driven by the Blender object name, because that is the one
    /// piece of authoring that survives the round trip intact — Blender materials
    /// are replaced by Unity ones as part of this very process, so they cannot
    /// carry the signal.
    ///
    ///   Wall_*                    Wall layer,   Walls.mat,     WallSurface
    ///   Prop_*                    Wall layer,   Walls.mat,     WallSurface
    ///   Ground_*SitePlantArea     Ground layer, PlantArea.mat
    ///   Ground_*                  Ground layer, Ground.mat
    ///   Trigger_*                 convex trigger MeshCollider, no renderer, SpikeSite
    ///
    /// A prop's walkable top face is a separate piece named Ground_*, not Prop_*,
    /// so it keeps landing on the Ground layer and stays aimable — the split the
    /// vision system relies on.
    ///
    /// The pass is self-gating: a model containing none of these prefixes is left
    /// completely alone, so Spike.fbx and the other non-map models are untouched
    /// without needing a path allowlist.
    /// </summary>
    public class MapImportPostprocessor : AssetPostprocessor
    {
        private const string WallPrefix = "Wall_";
        private const string PropPrefix = "Prop_";
        private const string GroundPrefix = "Ground_";
        private const string TriggerPrefix = "Trigger_";
        private const string PlantAreaSuffix = "SitePlantArea";

        private const string WallMaterialPath = "Assets/Materials/Walls.mat";
        private const string GroundMaterialPath = "Assets/Materials/Ground.mat";
        private const string PlantSiteMaterialPath = "Assets/Materials/PlantArea.mat";

        private void OnPostprocessModel(GameObject root)
        {
            var meshes = new List<MeshFilter>(root.GetComponentsInChildren<MeshFilter>(true));
            if (!LooksLikeMap(meshes)) return;

            Material wallMaterial = LoadMaterial(WallMaterialPath);
            Material groundMaterial = LoadMaterial(GroundMaterialPath);
            Material plantSiteMaterial = LoadMaterial(PlantSiteMaterialPath);
            List<WallSurfaceData> surfaces = LoadSurfaces();

            int walls = 0, grounds = 0, triggers = 0;

            foreach (MeshFilter meshFilter in meshes)
            {
                GameObject go = meshFilter.gameObject;
                string name = go.name;

                if (StartsWith(name, TriggerPrefix))
                {
                    ConfigureTrigger(go, meshFilter);
                    triggers++;
                }
                else if (StartsWith(name, WallPrefix) || StartsWith(name, PropPrefix))
                {
                    go.layer = Tactics.Vision.VisionLayerMasks.Wall;
                    AddMeshCollider(go, meshFilter, convex: false, isTrigger: false);
                    ApplyMaterial(go, wallMaterial);
                    ApplySurface(go, name, surfaces);
                    walls++;
                }
                else if (StartsWith(name, GroundPrefix))
                {
                    go.layer = Tactics.Vision.VisionLayerMasks.Ground;
                    AddMeshCollider(go, meshFilter, convex: false, isTrigger: false);
                    ApplyMaterial(go, EndsWith(name, PlantAreaSuffix) ? plantSiteMaterial : groundMaterial);
                    grounds++;
                }
            }

            Debug.Log($"[MapImport] {root.name}: {walls} wall/prop, {grounds} ground, {triggers} trigger objects configured.");
        }

        private static bool LooksLikeMap(List<MeshFilter> meshes)
        {
            foreach (MeshFilter meshFilter in meshes)
            {
                string name = meshFilter.gameObject.name;
                if (StartsWith(name, WallPrefix) || StartsWith(name, PropPrefix)
                    || StartsWith(name, GroundPrefix) || StartsWith(name, TriggerPrefix))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// The plant volume needs real vertical volume to overlap a standing
        /// CharacterController, which is why it is modelled as its own solid mesh
        /// in Blender rather than reusing the flat footprint: a zero-thickness
        /// mesh's convex hull is itself degenerate and OnTriggerEnter fires
        /// unreliably against it.
        /// </summary>
        private static void ConfigureTrigger(GameObject go, MeshFilter meshFilter)
        {
            AddMeshCollider(go, meshFilter, convex: true, isTrigger: true);

            MeshRenderer renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null) Object.DestroyImmediate(renderer);

            SpikeSite site = go.GetComponent<SpikeSite>();
            if (site == null) site = go.AddComponent<SpikeSite>();
            site.siteName = LastToken(go.name);
        }

        private static void AddMeshCollider(GameObject go, MeshFilter meshFilter, bool convex, bool isTrigger)
        {
            if (meshFilter.sharedMesh == null) return;

            MeshCollider collider = go.GetComponent<MeshCollider>();
            if (collider == null) collider = go.AddComponent<MeshCollider>();

            collider.sharedMesh = meshFilter.sharedMesh;
            // Order matters: a MeshCollider refuses to become a trigger while it
            // is still non-convex.
            collider.convex = convex;
            collider.isTrigger = isTrigger;
        }

        private static void ApplyMaterial(GameObject go, Material material)
        {
            if (material == null) return;

            MeshRenderer renderer = go.GetComponent<MeshRenderer>();
            if (renderer == null) return;

            // One material across every submesh: the map's look comes from these
            // few shared Unity materials, not from whatever Blender exported.
            var materials = new Material[Mathf.Max(1, renderer.sharedMaterials.Length)];
            for (int i = 0; i < materials.Length; i++) materials[i] = material;
            renderer.sharedMaterials = materials;
        }

        /// <summary>
        /// Scans every underscore-separated token of the name for a known surface,
        /// so Wall_Stone_01 and Prop_Crate_Wood_03 both resolve and token order is
        /// free. No match leaves the surface unset, which the runtime reads as the
        /// default travel budget.
        /// </summary>
        private static void ApplySurface(GameObject go, string name, List<WallSurfaceData> surfaces)
        {
            if (surfaces.Count == 0) return;

            WallSurfaceData match = null;
            string[] tokens = name.Split('_');
            foreach (string token in tokens)
            {
                foreach (WallSurfaceData surface in surfaces)
                {
                    if (string.Equals(token, surface.ResolvedName, System.StringComparison.OrdinalIgnoreCase))
                    {
                        match = surface;
                        break;
                    }
                }
                if (match != null) break;
            }

            if (match == null) return;

            WallSurface component = go.GetComponent<WallSurface>();
            if (component == null) component = go.AddComponent<WallSurface>();
            component.surface = match;
        }

        private static List<WallSurfaceData> LoadSurfaces()
        {
            var surfaces = new List<WallSurfaceData>();
            foreach (string guid in AssetDatabase.FindAssets("t:WallSurfaceData"))
            {
                var surface = AssetDatabase.LoadAssetAtPath<WallSurfaceData>(AssetDatabase.GUIDToAssetPath(guid));
                if (surface != null) surfaces.Add(surface);
            }
            return surfaces;
        }

        private static Material LoadMaterial(string path)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) Debug.LogWarning($"[MapImport] Material not found at {path}; leaving imported materials in place.");
            return material;
        }

        private static string LastToken(string name)
        {
            int split = name.LastIndexOf('_');
            return split >= 0 && split < name.Length - 1 ? name.Substring(split + 1) : name;
        }

        private static bool StartsWith(string name, string prefix) =>
            name.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase);

        private static bool EndsWith(string name, string suffix) =>
            name.EndsWith(suffix, System.StringComparison.OrdinalIgnoreCase);
    }
}
