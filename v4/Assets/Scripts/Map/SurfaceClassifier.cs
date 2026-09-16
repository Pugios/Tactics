using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Tactics.Map
{
    /// <summary>
    /// Pure-logic ground/wall classification for map geometry (mirrors
    /// <see cref="Tactics.Weapons.SpreadCalculator"/>: arithmetic here, asset work
    /// in the Editor's MapImportPostprocessor).
    ///
    /// The whole game consumes the ground/wall split through layers — the aim
    /// raycast only sees Ground, penetration and melee only see Wall — but the
    /// split itself is geometric: anything a player could stand on is ground,
    /// everything else is wall. Classifying faces at import lets one Blender
    /// object stay one object, instead of being hand-cut into a Ground_ half and
    /// a Wall_ half purely so the layer can be labelled.
    /// </summary>
    public static class SurfaceClassifier
    {
        /// <summary>
        /// Steepest face that still counts as ground. Deliberately the same value
        /// as the Player's CharacterController slope limit (Player.prefab), so
        /// "aimable ground" means exactly "somewhere you could stand": a walkable
        /// ramp is ground, anything steeper is wall.
        /// </summary>
        public const float GroundSlopeLimitDegrees = 45f;

        // A face at exactly the slope limit is ground; the tolerance keeps float
        // rounding in a transformed normal from flipping it either way.
        private const float BoundaryTolerance = 1e-4f;

        // A triangle whose height is under this fraction of its longest edge is a
        // sliver: Blender's n-gon triangulation leaves zero-area ones along
        // T-junctions, their normals are numerical noise that lands in either
        // class at random, and PhysX strips them anyway — failing to cook a
        // collider half made only of them. Judged relative to the triangle's own
        // size, because exported meshes carry arbitrary unit scale (Blender's
        // cm-scaled FBX objects are 100x smaller in mesh space than in the world),
        // so no absolute area threshold is right for every map.
        private const float SliverHeightRatio = 1e-5f;
        private const float SliverCrossRatioSq = 4f * SliverHeightRatio * SliverHeightRatio;

        private static readonly float MinGroundNormalY = Mathf.Cos(GroundSlopeLimitDegrees * Mathf.Deg2Rad);

        /// <summary>
        /// True when a face with this (not necessarily normalized) normal is
        /// ground. Ceilings and undersides point down, so they are wall.
        /// </summary>
        public static bool IsGround(Vector3 faceNormal)
        {
            float magnitude = faceNormal.magnitude;
            if (magnitude <= 0f) return false;
            return faceNormal.y / magnitude >= MinGroundNormalY - BoundaryTolerance;
        }

        /// <summary>
        /// Front-face normal of triangle (a, b, c) under Unity's clockwise winding —
        /// the same convention Mesh.RecalculateNormals uses. Not normalized; its
        /// length is twice the triangle's area.
        /// </summary>
        public static Vector3 FaceNormal(Vector3 a, Vector3 b, Vector3 c) => Vector3.Cross(b - a, c - a);

        /// <summary>
        /// Sorts every triangle of a mesh into ground or wall, appending its three
        /// indices to the matching list.
        ///
        /// Three details matter:
        ///   * FACE normals, never vertex normals — a smooth-shaded export averages
        ///     normals across a box's edges, which would drag its rims into the
        ///     wrong class.
        ///   * Positions are judged in map-root space via <paramref name="meshToRoot"/>:
        ///     Blender's FBX axis conversion commonly leaves a −90° X rotation on
        ///     each object, so a mesh's local "up" is not the map's up.
        ///   * A mirrored transform (negative determinant) reverses the winding, so
        ///     the normal is flipped back.
        /// Degenerate (zero-area) triangles belong to neither list.
        /// </summary>
        /// <param name="dropped">Optional: receives the sliver triangles that
        /// belong to neither class. They are useless as collision but must still
        /// be drawn, so the importer keeps them in the render mesh.</param>
        public static void SplitTriangles(Vector3[] vertices, int[] triangles, Matrix4x4 meshToRoot,
            List<int> ground, List<int> wall, List<int> dropped = null)
        {
            bool mirrored = meshToRoot.determinant < 0f;

            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                int ia = triangles[i];
                int ib = triangles[i + 1];
                int ic = triangles[i + 2];

                Vector3 a = meshToRoot.MultiplyPoint3x4(vertices[ia]);
                Vector3 b = meshToRoot.MultiplyPoint3x4(vertices[ib]);
                Vector3 c = meshToRoot.MultiplyPoint3x4(vertices[ic]);

                Vector3 normal = FaceNormal(a, b, c);
                if (IsSliver(a, b, c, normal))
                {
                    if (dropped != null)
                    {
                        dropped.Add(ia);
                        dropped.Add(ib);
                        dropped.Add(ic);
                    }
                    continue;
                }
                if (mirrored) normal = -normal;

                List<int> target = IsGround(normal) ? ground : wall;
                target.Add(ia);
                target.Add(ib);
                target.Add(ic);
            }
        }

        /// <summary>
        /// True for zero-area and needle-thin triangles, judged by height relative
        /// to the longest edge so the verdict is the same at any unit scale.
        /// |cross| = 2·area = longestEdge·height, so height/longest = |cross|/longest².
        /// </summary>
        private static bool IsSliver(Vector3 a, Vector3 b, Vector3 c, Vector3 cross)
        {
            float longestSq = Mathf.Max((b - a).sqrMagnitude, Mathf.Max((c - b).sqrMagnitude, (a - c).sqrMagnitude));
            if (longestSq <= 0f) return true;
            // Compared in squared form to avoid square roots: (|cross|/longest²)² vs ratio².
            return cross.sqrMagnitude <= SliverCrossRatioSq * 0.25f * longestSq * longestSq;
        }

        /// <summary>
        /// Builds a collider mesh from a subset of a source mesh's triangles,
        /// keeping only the vertices those triangles reference. Positions stay in
        /// the source mesh's local space, so the collider belongs on an object at
        /// identity local transform under the source.
        /// </summary>
        public static Mesh BuildMesh(Vector3[] vertices, List<int> triangles, string name)
        {
            var remap = new Dictionary<int, int>(triangles.Count);
            var positions = new List<Vector3>();
            var indices = new int[triangles.Count];

            for (int i = 0; i < triangles.Count; i++)
            {
                int source = triangles[i];
                if (!remap.TryGetValue(source, out int compact))
                {
                    compact = positions.Count;
                    remap.Add(source, compact);
                    positions.Add(vertices[source]);
                }
                indices[i] = compact;
            }

            var mesh = new Mesh { name = name };
            if (positions.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(positions);
            mesh.SetTriangles(indices, 0, true);
            return mesh;
        }
    }
}
