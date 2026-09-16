using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Tactics.Map;

namespace Tactics.Tests.EditMode
{
    public class SurfaceClassifierTests
    {
        /// <summary>
        /// A single triangle whose front face points along <paramref name="normal"/>,
        /// wound the way Unity expects (clockwise seen from the front).
        /// </summary>
        private static Vector3[] TriangleFacing(Vector3 normal)
        {
            normal.Normalize();
            Vector3 tangent = Vector3.Cross(normal, Mathf.Abs(normal.y) > 0.9f ? Vector3.forward : Vector3.up).normalized;
            Vector3 bitangent = Vector3.Cross(normal, tangent);
            // Cross(b - a, c - a) must equal +normal: Cross(tangent, Cross(normal, tangent)) = normal.
            return new[] { Vector3.zero, tangent, bitangent };
        }

        private static void Split(Vector3[] vertices, int[] triangles, Matrix4x4 meshToRoot,
            out List<int> ground, out List<int> wall)
        {
            ground = new List<int>();
            wall = new List<int>();
            SurfaceClassifier.SplitTriangles(vertices, triangles, meshToRoot, ground, wall);
        }

        private static bool ClassifiesAsGround(Vector3 normal)
        {
            Split(TriangleFacing(normal), new[] { 0, 1, 2 }, Matrix4x4.identity, out List<int> ground, out _);
            return ground.Count == 3;
        }

        /// <summary>Normal tilted <paramref name="degrees"/> away from straight up.</summary>
        private static Vector3 Tilted(float degrees) =>
            Quaternion.AngleAxis(degrees, Vector3.right) * Vector3.up;

        // --- Winding convention ---

        [Test]
        public void FaceNormal_MatchesUnityRecalculateNormals()
        {
            // The classifier is only right if its winding agrees with Unity's own.
            Vector3[] vertices = TriangleFacing(Vector3.up);
            var mesh = new Mesh();
            mesh.SetVertices(vertices);
            mesh.SetTriangles(new[] { 0, 1, 2 }, 0);
            mesh.RecalculateNormals();

            Vector3 face = SurfaceClassifier.FaceNormal(vertices[0], vertices[1], vertices[2]).normalized;
            Assert.Greater(Vector3.Dot(face, mesh.normals[0]), 0.999f);
            Object.DestroyImmediate(mesh);
        }

        // --- Slope classification ---

        [Test]
        public void FlatFloor_IsGround()
        {
            Assert.IsTrue(ClassifiesAsGround(Vector3.up));
        }

        [Test]
        public void VerticalWall_IsWall()
        {
            Assert.IsFalse(ClassifiesAsGround(Vector3.forward));
            Assert.IsFalse(ClassifiesAsGround(Vector3.left));
        }

        [Test]
        public void WalkableRamp_IsGround()
        {
            Assert.IsTrue(ClassifiesAsGround(Tilted(30f)));
        }

        [Test]
        public void SlopeLimit_IsGroundAtExactly45_WallJustPast()
        {
            Assert.IsTrue(ClassifiesAsGround(Tilted(45f)));
            Assert.IsFalse(ClassifiesAsGround(Tilted(50f)));
        }

        [Test]
        public void CeilingAndUnderside_AreWall()
        {
            Assert.IsFalse(ClassifiesAsGround(Vector3.down));
            Assert.IsFalse(ClassifiesAsGround(Tilted(170f)));
        }

        [Test]
        public void IsGround_AcceptsUnnormalizedNormals_RejectsZero()
        {
            Assert.IsTrue(SurfaceClassifier.IsGround(new Vector3(0f, 25f, 0f)));
            Assert.IsFalse(SurfaceClassifier.IsGround(Vector3.zero));
        }

        // --- Transforms ---

        [Test]
        public void BlenderAxisConversion_IsJudgedInRootSpace()
        {
            // Blender's FBX export commonly leaves a -90° X rotation on each object:
            // a face whose LOCAL normal is +Z is a floor in the map, and a face
            // whose local normal is +Y is a wall.
            Matrix4x4 blenderObject = Matrix4x4.Rotate(Quaternion.Euler(-90f, 0f, 0f));

            Split(TriangleFacing(Vector3.forward), new[] { 0, 1, 2 }, blenderObject, out List<int> ground, out _);
            Assert.AreEqual(3, ground.Count, "local +Z rotated -90° about X must face up");

            Split(TriangleFacing(Vector3.up), new[] { 0, 1, 2 }, blenderObject, out ground, out List<int> wall);
            Assert.AreEqual(0, ground.Count);
            Assert.AreEqual(3, wall.Count);
        }

        [Test]
        public void MirroredTransform_StillClassifiesCorrectly()
        {
            // Negative scale on X reverses the winding; the floor must stay a floor.
            Matrix4x4 mirrored = Matrix4x4.Scale(new Vector3(-1f, 1f, 1f));
            Split(TriangleFacing(Vector3.up), new[] { 0, 1, 2 }, mirrored, out List<int> ground, out _);
            Assert.AreEqual(3, ground.Count);

            // Mirroring through Y genuinely turns a floor into a ceiling.
            Matrix4x4 flipped = Matrix4x4.Scale(new Vector3(1f, -1f, 1f));
            Split(TriangleFacing(Vector3.up), new[] { 0, 1, 2 }, flipped, out ground, out List<int> wall);
            Assert.AreEqual(0, ground.Count);
            Assert.AreEqual(3, wall.Count);
        }

        [Test]
        public void UniformScaleAndTranslation_DoNotChangeTheClass()
        {
            Matrix4x4 placed = Matrix4x4.TRS(new Vector3(40f, 3f, -12f), Quaternion.Euler(0f, 37f, 0f), Vector3.one * 5f);
            Split(TriangleFacing(Tilted(30f)), new[] { 0, 1, 2 }, placed, out List<int> ground, out _);
            Assert.AreEqual(3, ground.Count);
        }

        [Test]
        public void DegenerateTriangle_IsDropped()
        {
            var vertices = new[] { Vector3.zero, Vector3.right, Vector3.right * 2f }; // collinear
            Split(vertices, new[] { 0, 1, 2 }, Matrix4x4.identity, out List<int> ground, out List<int> wall);
            Assert.AreEqual(0, ground.Count);
            Assert.AreEqual(0, wall.Count);
        }

        [Test]
        public void SliverTriangle_IsDroppedAtAnyUnitScale()
        {
            // A T-junction sliver: 1 m long, 1 nm tall. Its normal is noise, so it
            // must belong to neither class — at Blender's cm-scaled mesh space and
            // at a 100x world scale alike.
            var sliver = new[] { Vector3.zero, new Vector3(1f, 0f, 0f), new Vector3(0.5f, 1e-9f, 0f) };
            foreach (float scale in new[] { 0.01f, 1f, 100f, 2287f })
            {
                Split(sliver, new[] { 0, 1, 2 }, Matrix4x4.Scale(Vector3.one * scale),
                    out List<int> ground, out List<int> wall);
                Assert.AreEqual(0, ground.Count, $"scale {scale}");
                Assert.AreEqual(0, wall.Count, $"scale {scale}");
            }
        }

        [Test]
        public void SmallButWellShapedTriangle_IsKept()
        {
            // Tiny in absolute terms (mesh space of a cm-scaled export) but a real
            // face: it must not be mistaken for a sliver.
            Vector3[] floor = TriangleFacing(Vector3.up);
            for (int i = 0; i < floor.Length; i++) floor[i] *= 0.0001f;

            Split(floor, new[] { 0, 1, 2 }, Matrix4x4.identity, out List<int> ground, out _);
            Assert.AreEqual(3, ground.Count);
        }

        // --- Whole meshes ---

        [Test]
        public void UnityCube_SplitsTopFromSidesAndBottom()
        {
            // Unity's own cube is authored with front-facing clockwise winding,
            // which makes it an independent check on the whole pipeline.
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Mesh mesh = cube.GetComponent<MeshFilter>().sharedMesh;

            Split(mesh.vertices, mesh.triangles, Matrix4x4.identity, out List<int> ground, out List<int> wall);

            Assert.AreEqual(2 * 3, ground.Count, "top face: 2 triangles");
            Assert.AreEqual(10 * 3, wall.Count, "four sides + bottom: 10 triangles");
            foreach (int index in ground)
            {
                Assert.AreEqual(0.5f, mesh.vertices[index].y, 0.0001f, "only top-face vertices are ground");
            }

            Object.DestroyImmediate(cube);
        }

        [Test]
        public void BuildMesh_KeepsOnlyReferencedVertices()
        {
            var vertices = new[]
            {
                new Vector3(0f, 0f, 0f), // unused
                new Vector3(0f, 1f, 0f),
                new Vector3(0f, 1f, 1f),
                new Vector3(1f, 1f, 0f),
                new Vector3(9f, 9f, 9f), // unused
            };
            var triangles = new List<int> { 1, 2, 3 };

            Mesh mesh = SurfaceClassifier.BuildMesh(vertices, triangles, "Top");

            Assert.AreEqual("Top", mesh.name);
            Assert.AreEqual(3, mesh.vertexCount);
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, mesh.triangles);
            CollectionAssert.AreEqual(new[] { vertices[1], vertices[2], vertices[3] }, mesh.vertices);

            Object.DestroyImmediate(mesh);
        }

        [Test]
        public void BuildMesh_SharedVerticesAreNotDuplicated()
        {
            var vertices = new[] { Vector3.zero, Vector3.forward, Vector3.right, Vector3.forward + Vector3.right };
            var triangles = new List<int> { 0, 1, 2, 2, 1, 3 }; // a quad sharing an edge

            Mesh mesh = SurfaceClassifier.BuildMesh(vertices, triangles, "Quad");

            Assert.AreEqual(4, mesh.vertexCount);
            Assert.AreEqual(6, mesh.triangles.Length);
            Object.DestroyImmediate(mesh);
        }
    }
}
