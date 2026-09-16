using System.Collections.Generic;
using UnityEngine;

namespace Tactics.Map
{
    /// <summary>
    /// Pure logic behind the map importer: takes a mesh's submeshes plus the
    /// surface each one resolved to, and produces the two collider triangle sets
    /// (ground and wall, from <see cref="SurfaceClassifier"/>), a surface tag per
    /// collider triangle, and the render groups the visual mesh is rebuilt from.
    ///
    /// Submeshes are the unit of material assignment: Unity imports one submesh
    /// per Blender material slot. Each painted slot stays its own render group so
    /// it keeps the material Blender gave it — the look is authored once, in
    /// Blender, and Unity only decides behaviour.
    ///
    /// A submesh whose material matches no surface is not an error. Its faces are
    /// classified geometrically instead, ground faces taking the Ground surface
    /// and the rest taking Wall, which is what lets an unpainted blockout still
    /// read as floor and wall.
    /// </summary>
    public static class MapSurfaceSplitter
    {
        public sealed class Result
        {
            /// <summary>Vertex indices, three per triangle.</summary>
            public readonly List<int> GroundTriangles = new List<int>();
            public readonly List<int> WallTriangles = new List<int>();

            /// <summary>One surface index per triangle, aligned with the lists above.</summary>
            public readonly List<int> GroundTags = new List<int>();
            public readonly List<int> WallTags = new List<int>();

            /// <summary>Surface index of each render group.</summary>
            public readonly List<int> RenderSurfaces = new List<int>();

            /// <summary>
            /// Submesh each render group came from, so the importer can keep that
            /// slot's Blender material on it.
            /// </summary>
            public readonly List<int> RenderSourceSubmesh = new List<int>();

            public readonly List<List<int>> RenderTriangles = new List<List<int>>();

            /// <summary>
            /// True when the render groups are exactly the source submeshes in the
            /// same order, so the importer can keep the imported mesh instead of
            /// building a copy.
            /// </summary>
            public bool RenderMatchesSource;
        }

        /// <summary>
        /// <paramref name="submeshSurface"/> holds the resolved surface index per
        /// submesh, or -1 when its material matched nothing.
        /// </summary>
        public static Result Split(Vector3[] vertices, IReadOnlyList<int[]> submeshTriangles,
            IReadOnlyList<int> submeshSurface, int groundFallback, int wallFallback, Matrix4x4 meshToRoot)
        {
            var result = new Result();
            var ground = new List<int>();
            var wall = new List<int>();
            var dropped = new List<int>();
            bool oneGroupPerSubmesh = true;

            for (int submesh = 0; submesh < submeshTriangles.Count; submesh++)
            {
                int[] triangles = submeshTriangles[submesh];
                if (triangles == null || triangles.Length == 0)
                {
                    // An empty slot leaves no group, so the groups no longer line
                    // up with the submeshes and the mesh has to be rebuilt.
                    oneGroupPerSubmesh = false;
                    continue;
                }

                int surface = submesh < submeshSurface.Count ? submeshSurface[submesh] : -1;

                ground.Clear();
                wall.Clear();
                dropped.Clear();
                SurfaceClassifier.SplitTriangles(vertices, triangles, meshToRoot, ground, wall, dropped);

                if (surface >= 0)
                {
                    Append(result.GroundTriangles, result.GroundTags, ground, surface);
                    Append(result.WallTriangles, result.WallTags, wall, surface);
                    // Every face of a painted submesh keeps its material, slivers
                    // included: they are dropped from colliders, never from sight.
                    AddGroup(result, surface, submesh).AddRange(triangles);
                    continue;
                }

                // Unpainted: the geometry decides. Ground faces become floor,
                // everything else — including slivers, which have no meaningful
                // normal — becomes wall. Both halves keep the slot's own material.
                Append(result.GroundTriangles, result.GroundTags, ground, groundFallback);
                Append(result.WallTriangles, result.WallTags, wall, wallFallback);
                oneGroupPerSubmesh = false;

                if (ground.Count > 0) AddGroup(result, groundFallback, submesh).AddRange(ground);
                if (wall.Count > 0 || dropped.Count > 0)
                {
                    List<int> group = AddGroup(result, wallFallback, submesh);
                    group.AddRange(wall);
                    group.AddRange(dropped);
                }
            }

            result.RenderMatchesSource = oneGroupPerSubmesh
                && result.RenderSurfaces.Count == submeshTriangles.Count;
            return result;
        }

        private static List<int> AddGroup(Result result, int surface, int submesh)
        {
            var group = new List<int>();
            result.RenderSurfaces.Add(surface);
            result.RenderSourceSubmesh.Add(submesh);
            result.RenderTriangles.Add(group);
            return group;
        }

        private static void Append(List<int> triangles, List<int> tags, List<int> source, int surface)
        {
            triangles.AddRange(source);
            for (int i = 0; i < source.Count; i += 3) tags.Add(surface);
        }

        /// <summary>
        /// Index of the surface whose asset name matches a Blender material name,
        /// or -1. Blender appends ".001" to duplicated materials and Unity appends
        /// " (Instance)" to runtime copies; neither changes which surface is meant.
        /// </summary>
        public static int MatchSurface(string materialName, IReadOnlyList<string> surfaceNames)
        {
            if (string.IsNullOrEmpty(materialName) || surfaceNames == null) return -1;

            string cleaned = Normalize(materialName);
            for (int i = 0; i < surfaceNames.Count; i++)
            {
                if (string.Equals(cleaned, Normalize(surfaceNames[i]), System.StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
            return -1;
        }

        private static string Normalize(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;

            string trimmed = name.Trim();
            const string instance = " (Instance)";
            while (trimmed.EndsWith(instance, System.StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed.Substring(0, trimmed.Length - instance.Length).TrimEnd();
            }

            int dot = trimmed.LastIndexOf('.');
            if (dot > 0 && dot < trimmed.Length - 1)
            {
                bool digits = true;
                for (int i = dot + 1; i < trimmed.Length; i++)
                {
                    if (!char.IsDigit(trimmed[i])) { digits = false; break; }
                }
                if (digits) trimmed = trimmed.Substring(0, dot);
            }
            return trimmed;
        }
    }
}
