using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Tactics.Map;
using Tactics.Vision;

namespace Tactics.EditorTools
{
    /// <summary>
    /// Turns a map exported from Blender into playable geometry on every
    /// (re-)import, so dropping the FBX into <see cref="MapFolder"/> is the whole
    /// workflow. Nothing is driven by object names: **the Blender material on a
    /// face decides everything about it**, resolved against the
    /// <see cref="SurfaceData"/> assets by name.
    ///
    /// One Blender object stays one object, and its ground/wall split — which the
    /// game consumes through layers (aim raycasts see only Ground, penetration
    /// and melee only Wall) — is computed from face normals
    /// (<see cref="SurfaceClassifier"/>) rather than hand-cut in Blender:
    ///
    ///   Visual object       MeshRenderer + materials, no collider, Default layer
    ///   ├─ GroundCollider   faces within 45° of up (walkable ramps included)   Ground (7)
    ///   └─ WallCollider     everything else, ceilings and undersides included  Wall (6)
    ///
    /// Both colliders carry a <see cref="SurfaceMap"/> recording the surface of
    /// every triangle, because materials are per face: one object can be a
    /// concrete building with a dirt floor on top.
    ///
    /// A material with no matching asset is not an error. Its faces are still
    /// split onto the right layers, and take the Ground surface when they point
    /// up and Wall otherwise, so only the surfaces you care about need assets.
    ///
    /// The look is never touched: every face keeps the material Blender gave it.
    /// </summary>
    public class MapImportPostprocessor : AssetPostprocessor
    {
        /// <summary>
        /// Only models under this folder are maps. Gating by folder leaves names
        /// completely free, never touches Spike.fbx and friends, and — unlike an
        /// asset label kept in the .meta — survives deleting and re-adding the FBX.
        /// </summary>
        public const string MapFolder = "Assets/Models/Maps/";

        public const string GroundColliderName = "GroundCollider";
        public const string WallColliderName = "WallCollider";

        private bool IsMap => assetPath.Replace('\\', '/').StartsWith(MapFolder, System.StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Part of every model's import dependency hash. Bump it whenever what this
        /// pass produces changes: Unity then re-imports the maps by itself, instead
        /// of keeping stale cached results and flagging the next manual reimport as
        /// an "inconsistent result".
        /// </summary>
        public override uint GetVersion() => 6;

        private void OnPreprocessModel()
        {
            if (!IsMap) return;

            // The importer's own "Generate Colliders" would put a whole-mesh
            // collider on every visual object, on top of the split ones.
            var importer = (ModelImporter)assetImporter;
            importer.addCollider = false;
        }

        private void OnPostprocessModel(GameObject root)
        {
            if (!IsMap) return;

            List<SurfaceData> surfaces = LoadSurfaces();
            var surfaceNames = new List<string>(surfaces.Count);
            foreach (SurfaceData surface in surfaces) surfaceNames.Add(surface.name);

            // A face whose material names no surface gets no surface at all. It is
            // still split onto the right layer; it simply has no penetration
            // budget of its own and cannot be a plant site.
            const int noSurface = -1;

            var unknownMaterials = new SortedSet<string>();
            int objects = 0, groundColliders = 0, wallColliders = 0, rebuiltMeshes = 0;
            var used = new SortedSet<string>();

            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) continue;
                objects++;

                if (ConfigureObject(root, filter, surfaces, surfaceNames, noSurface, noSurface,
                        unknownMaterials, used, out bool hasGround, out bool hasWall, out bool rebuilt))
                {
                    if (hasGround) groundColliders++;
                    if (hasWall) wallColliders++;
                    if (rebuilt) rebuiltMeshes++;
                }
            }

            Debug.Log($"[MapImport] {root.name}: {objects} objects → {groundColliders} ground + {wallColliders} wall colliders, "
                + $"{rebuiltMeshes} meshes rebuilt. Surfaces used: {string.Join(", ", used)}.");

            if (unknownMaterials.Count > 0)
            {
                Debug.LogWarning($"[MapImport] {root.name}: no SurfaceData asset for Blender material(s) "
                    + $"{string.Join(", ", unknownMaterials)} — those faces get no surface (they are still split onto the right layers).");
            }
        }

        private bool ConfigureObject(GameObject root, MeshFilter filter, List<SurfaceData> surfaces,
            List<string> surfaceNames, int groundFallback, int wallFallback,
            SortedSet<string> unknownMaterials, SortedSet<string> used,
            out bool hasGround, out bool hasWall, out bool rebuilt)
        {
            hasGround = hasWall = rebuilt = false;

            GameObject go = filter.gameObject;
            Mesh source = filter.sharedMesh;
            var renderer = go.GetComponent<MeshRenderer>();

            MeshCollider existing = go.GetComponent<MeshCollider>();
            if (existing != null) Object.DestroyImmediate(existing);

            // No collider here any more, so this layer only decides camera
            // culling — and every camera, the vision eye camera included, draws Default.
            go.layer = VisionLayerMasks.Default;

            // Unity imports one submesh per Blender material slot, and the slot's
            // material still carries the Blender material's name at this point.
            int subMeshCount = source.subMeshCount;
            var submeshTriangles = new int[subMeshCount][];
            var submeshSurface = new int[subMeshCount];
            Material[] slotMaterials = renderer != null ? renderer.sharedMaterials : new Material[0];

            for (int s = 0; s < subMeshCount; s++)
            {
                submeshTriangles[s] = source.GetTopology(s) == MeshTopology.Triangles
                    ? source.GetTriangles(s)
                    : new int[0];

                string materialName = s < slotMaterials.Length && slotMaterials[s] != null ? slotMaterials[s].name : null;
                submeshSurface[s] = MapSurfaceSplitter.MatchSurface(materialName, surfaceNames);
                if (submeshSurface[s] < 0 && !string.IsNullOrEmpty(materialName)) unknownMaterials.Add(materialName);
            }

            Matrix4x4 meshToRoot = root.transform.worldToLocalMatrix * go.transform.localToWorldMatrix;
            MapSurfaceSplitter.Result split = MapSurfaceSplitter.Split(source.vertices, submeshTriangles,
                submeshSurface, groundFallback, wallFallback, meshToRoot);

            foreach (int index in split.RenderSurfaces)
            {
                if (index >= 0 && index < surfaces.Count) used.Add(surfaces[index].name);
            }

            string path = HierarchyPath(root.transform, go.transform);
            ApplyVisual(filter, renderer, source, slotMaterials, split, path, ref rebuilt);

            Vector3[] vertices = source.vertices;
            hasGround = AddColliderChild(go, vertices, split.GroundTriangles, split.GroundTags, surfaces,
                GroundColliderName, VisionLayerMasks.Ground, path);
            hasWall = AddColliderChild(go, vertices, split.WallTriangles, split.WallTags, surfaces,
                WallColliderName, VisionLayerMasks.Wall, path);
            return true;
        }

        /// <summary>
        /// Leaves the look entirely to Blender. Nothing is assigned unless the
        /// mesh has to be rebuilt — when an unpainted submesh splits into floor
        /// and wall, or when a slot is empty — and even then the renderer just
        /// gets the same slot materials re-ordered to match the new submeshes.
        /// </summary>
        private void ApplyVisual(MeshFilter filter, MeshRenderer renderer, Mesh source, Material[] slotMaterials,
            MapSurfaceSplitter.Result split, string path, ref bool rebuilt)
        {
            // Untouched mesh, untouched materials.
            if (split.RenderMatchesSource || split.RenderSurfaces.Count == 0) return;

            {
                // Instantiate keeps every vertex attribute (normals, UVs, tangents,
                // colors); only the index buffers are regrouped.
                Mesh visual = Object.Instantiate(source);
                visual.name = source.name;
                visual.subMeshCount = split.RenderTriangles.Count;
                for (int i = 0; i < split.RenderTriangles.Count; i++)
                {
                    visual.SetTriangles(split.RenderTriangles[i], i, i == split.RenderTriangles.Count - 1);
                }

                context.AddObjectToAsset($"{path}/Visual", visual);
                filter.sharedMesh = visual;
                rebuilt = true;
            }

            if (renderer == null) return;

            // The submeshes moved, so each group takes the material of the slot
            // its faces came from.
            var materials = new Material[split.RenderSurfaces.Count];
            for (int i = 0; i < materials.Length; i++)
            {
                int slot = split.RenderSourceSubmesh[i];
                materials[i] = slot >= 0 && slot < slotMaterials.Length ? slotMaterials[slot] : null;
            }
            renderer.sharedMaterials = materials;
        }

        private bool AddColliderChild(GameObject parent, Vector3[] vertices, List<int> triangles, List<int> tags,
            List<SurfaceData> surfaces, string childName, int layer, string path)
        {
            if (triangles.Count == 0) return false;

            Mesh mesh = SurfaceClassifier.BuildMesh(vertices, triangles, $"{parent.name}_{childName}");
            // Meshes created during import only persist if they are registered as
            // part of the imported asset; otherwise the collider comes back empty on
            // reload. The hierarchy path keeps the identifier stable across re-imports.
            context.AddObjectToAsset($"{path}/{childName}", mesh);

            var child = new GameObject(childName) { layer = layer };
            child.transform.SetParent(parent.transform, false);
            child.AddComponent<MeshCollider>().sharedMesh = mesh;

            AddSurfaceMap(child, tags, surfaces);
            return true;
        }

        /// <summary>
        /// Records the surface of every collider triangle, compacting the palette
        /// so a single-surface collider stores no per-triangle array at all.
        /// </summary>
        private static void AddSurfaceMap(GameObject child, List<int> tags, List<SurfaceData> surfaces)
        {
            var palette = new List<SurfaceData>();
            var paletteIndex = new Dictionary<int, int>();
            var perTriangle = new byte[tags.Count];

            for (int i = 0; i < tags.Count; i++)
            {
                int surface = tags[i];
                if (!paletteIndex.TryGetValue(surface, out int local))
                {
                    local = palette.Count;
                    paletteIndex.Add(surface, local);
                    palette.Add(surface >= 0 && surface < surfaces.Count ? surfaces[surface] : null);
                }
                perTriangle[i] = (byte)local;
            }

            if (palette.Count == 0) return;

            var map = child.AddComponent<SurfaceMap>();
            map.palette = palette.ToArray();
            map.triangleSurface = palette.Count > 1 ? perTriangle : new byte[0];
        }

        private static string HierarchyPath(Transform root, Transform target)
        {
            var parts = new List<string>();
            for (Transform t = target; t != null && t != root; t = t.parent) parts.Add(t.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        private static List<SurfaceData> LoadSurfaces()
        {
            var surfaces = new List<SurfaceData>();
            foreach (string guid in AssetDatabase.FindAssets("t:SurfaceData"))
            {
                var surface = AssetDatabase.LoadAssetAtPath<SurfaceData>(AssetDatabase.GUIDToAssetPath(guid));
                if (surface != null) surfaces.Add(surface);
            }
            return surfaces;
        }
    }
}
