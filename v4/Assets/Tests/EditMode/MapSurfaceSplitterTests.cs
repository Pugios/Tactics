using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Tactics.Map;

namespace Tactics.Tests.EditMode
{
    public class MapSurfaceSplitterTests
    {
        private const int Wood = 0;
        private const int Stone = 1;
        private const int GroundFallback = 2;
        private const int WallFallback = 3;

        private GameObject cube;
        private Mesh cubeMesh;

        [SetUp]
        public void SetUp()
        {
            cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cubeMesh = cube.GetComponent<MeshFilter>().sharedMesh;
        }

        [TearDown]
        public void TearDown()
        {
            if (cube != null) Object.DestroyImmediate(cube);
        }

        /// <summary>The cube's 12 triangles cut into two submeshes of 6.</summary>
        private static int[][] TwoHalves(Mesh mesh)
        {
            int[] all = mesh.triangles;
            var first = new int[18];
            var second = new int[all.Length - 18];
            System.Array.Copy(all, 0, first, 0, 18);
            System.Array.Copy(all, 18, second, 0, second.Length);
            return new[] { first, second };
        }

        // --- Name matching ---

        [Test]
        public void MatchSurface_IsCaseInsensitive()
        {
            var names = new List<string> { "Wood", "Stone", "PlantArea" };
            Assert.AreEqual(0, MapSurfaceSplitter.MatchSurface("Wood", names));
            Assert.AreEqual(1, MapSurfaceSplitter.MatchSurface("stone", names));
            Assert.AreEqual(2, MapSurfaceSplitter.MatchSurface("PLANTAREA", names));
        }

        [Test]
        public void MatchSurface_IgnoresBlenderAndUnitySuffixes()
        {
            var names = new List<string> { "Wood", "Stone" };
            // Blender numbers duplicated materials; Unity renames runtime copies.
            Assert.AreEqual(0, MapSurfaceSplitter.MatchSurface("Wood.001", names));
            Assert.AreEqual(1, MapSurfaceSplitter.MatchSurface("Stone (Instance)", names));
            Assert.AreEqual(1, MapSurfaceSplitter.MatchSurface("Stone.003 (Instance)", names));
            Assert.AreEqual(0, MapSurfaceSplitter.MatchSurface("  Wood  ", names));
        }

        [Test]
        public void MatchSurface_UnknownOrEmpty_IsMinusOne()
        {
            var names = new List<string> { "Wood", "Stone" };
            Assert.AreEqual(-1, MapSurfaceSplitter.MatchSurface("Dirt", names));
            Assert.AreEqual(-1, MapSurfaceSplitter.MatchSurface(null, names));
            Assert.AreEqual(-1, MapSurfaceSplitter.MatchSurface("Wood", null));
        }

        [Test]
        public void MatchSurface_DoesNotConfusePrefixes()
        {
            var names = new List<string> { "Wood", "Woodplank" };
            Assert.AreEqual(1, MapSurfaceSplitter.MatchSurface("Woodplank", names));
        }

        // --- Painted submeshes ---

        [Test]
        public void PaintedSubmesh_TagsEveryFaceWithItsSurface()
        {
            var result = MapSurfaceSplitter.Split(cubeMesh.vertices, new[] { cubeMesh.triangles },
                new[] { Wood }, GroundFallback, WallFallback, Matrix4x4.identity);

            Assert.AreEqual(2, result.GroundTags.Count, "cube top");
            Assert.AreEqual(10, result.WallTags.Count, "sides and bottom");
            foreach (int tag in result.GroundTags) Assert.AreEqual(Wood, tag);
            foreach (int tag in result.WallTags) Assert.AreEqual(Wood, tag);

            // A wood floor is still wood: painting wins over the geometric fallback.
            CollectionAssert.AreEqual(new[] { Wood }, result.RenderSurfaces);
            CollectionAssert.AreEqual(new[] { 0 }, result.RenderSourceSubmesh);
            Assert.IsTrue(result.RenderMatchesSource, "one submesh in, one group out");
        }

        [Test]
        public void UnpaintedSubmesh_SplitsIntoGroundAndWallFallbacks()
        {
            var result = MapSurfaceSplitter.Split(cubeMesh.vertices, new[] { cubeMesh.triangles },
                new[] { -1 }, GroundFallback, WallFallback, Matrix4x4.identity);

            foreach (int tag in result.GroundTags) Assert.AreEqual(GroundFallback, tag);
            foreach (int tag in result.WallTags) Assert.AreEqual(WallFallback, tag);

            CollectionAssert.AreEquivalent(new[] { GroundFallback, WallFallback }, result.RenderSurfaces);
            // Both halves came from the one slot, so both keep its Blender material.
            CollectionAssert.AreEqual(new[] { 0, 0 }, result.RenderSourceSubmesh);
            Assert.IsFalse(result.RenderMatchesSource, "the mesh must be rebuilt to split floor from wall");

            int rendered = 0;
            foreach (List<int> group in result.RenderTriangles) rendered += group.Count;
            Assert.AreEqual(cubeMesh.triangles.Length, rendered, "no face may be lost");
        }

        [Test]
        public void TwoPaintedSubmeshes_KeepTagsAlignedWithTriangleOrder()
        {
            int[][] halves = TwoHalves(cubeMesh);
            var result = MapSurfaceSplitter.Split(cubeMesh.vertices, halves, new[] { Wood, Stone },
                GroundFallback, WallFallback, Matrix4x4.identity);

            Assert.IsTrue(result.RenderMatchesSource);
            CollectionAssert.AreEqual(new[] { Wood, Stone }, result.RenderSurfaces);

            // Every collider triangle must carry the surface of the submesh it came
            // from — this alignment is what RaycastHit.triangleIndex reads back.
            var wallVertices = new HashSet<int>();
            for (int i = 0; i < result.WallTriangles.Count; i += 3)
            {
                int tag = result.WallTags[i / 3];
                Assert.IsTrue(tag == Wood || tag == Stone);
                wallVertices.Add(result.WallTriangles[i]);
            }
            Assert.AreEqual((result.GroundTriangles.Count + result.WallTriangles.Count) / 3,
                result.GroundTags.Count + result.WallTags.Count, "one tag per triangle");
        }

        [Test]
        public void TwoSubmeshesSharingASurface_StaySeparateGroups()
        {
            // Same behaviour, but each slot keeps its own Blender material, so
            // the groups must not be merged.
            int[][] halves = TwoHalves(cubeMesh);
            var result = MapSurfaceSplitter.Split(cubeMesh.vertices, halves, new[] { Wood, Wood },
                GroundFallback, WallFallback, Matrix4x4.identity);

            CollectionAssert.AreEqual(new[] { Wood, Wood }, result.RenderSurfaces);
            CollectionAssert.AreEqual(new[] { 0, 1 }, result.RenderSourceSubmesh);
            Assert.IsTrue(result.RenderMatchesSource, "nothing moved, so the mesh can stay as imported");
        }

        [Test]
        public void Slivers_LeaveTheCollidersButStayVisible()
        {
            // A floor quad plus a zero-area T-junction sliver.
            var vertices = new[]
            {
                new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 1f), new Vector3(1f, 0f, 1f),
                new Vector3(2f, 0f, 0f), new Vector3(2.5f, 0f, 0f), new Vector3(3f, 0f, 0f),
            };
            var triangles = new[] { 0, 1, 2, 3, 4, 5 };

            var result = MapSurfaceSplitter.Split(vertices, new[] { triangles }, new[] { -1 },
                GroundFallback, WallFallback, Matrix4x4.identity);

            Assert.AreEqual(1, result.GroundTags.Count, "only the real floor triangle collides");
            Assert.AreEqual(0, result.WallTags.Count);

            int rendered = 0;
            foreach (List<int> group in result.RenderTriangles) rendered += group.Count;
            Assert.AreEqual(6, rendered, "the sliver is still drawn");
        }

        [Test]
        public void EmptySubmesh_IsSkipped()
        {
            var result = MapSurfaceSplitter.Split(cubeMesh.vertices,
                new[] { cubeMesh.triangles, new int[0] }, new[] { Wood, Stone },
                GroundFallback, WallFallback, Matrix4x4.identity);

            CollectionAssert.AreEqual(new[] { Wood }, result.RenderSurfaces);
            CollectionAssert.AreEqual(new[] { 0 }, result.RenderSourceSubmesh);
            Assert.IsFalse(result.RenderMatchesSource, "the empty slot is gone, so the mesh is rebuilt");
        }
    }
}
