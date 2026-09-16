using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Tactics.Map;
using Tactics.Objectives;
using Tactics.Vision;

namespace Tactics.Tests.EditMode
{
    public class SurfaceMapTests
    {
        private readonly List<Object> spawned = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in spawned) if (o != null) Object.DestroyImmediate(o);
            spawned.Clear();
        }

        private SurfaceData MakeSurface(string name, float maxTravel, bool plantSite)
        {
            var surface = ScriptableObject.CreateInstance<SurfaceData>();
            surface.name = name;
            surface.maxTravelMeters = maxTravel;
            surface.isPlantSite = plantSite;
            spawned.Add(surface);
            return surface;
        }

        /// <summary>
        /// A 1x1 floor quad at y = 0 as two triangles, each with its own surface,
        /// on the Ground layer with a real MeshCollider.
        /// </summary>
        private SurfaceMap MakeFloor(SurfaceData first, SurfaceData second)
        {
            var mesh = new Mesh
            {
                vertices = new[]
                {
                    new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 1f),
                    new Vector3(1f, 0f, 1f), new Vector3(1f, 0f, 0f),
                },
                triangles = new[] { 0, 1, 2, 0, 2, 3 },
            };
            mesh.RecalculateNormals();
            spawned.Add(mesh);

            var go = new GameObject("Floor") { layer = VisionLayerMasks.Ground };
            spawned.Add(go);
            go.AddComponent<MeshCollider>().sharedMesh = mesh;

            var map = go.AddComponent<SurfaceMap>();
            if (second == null || second == first)
            {
                map.palette = new[] { first };
                map.triangleSurface = new byte[0];
            }
            else
            {
                map.palette = new[] { first, second };
                map.triangleSurface = new byte[] { 0, 1 };
            }

            Physics.SyncTransforms();
            return map;
        }

        // --- Lookup ---

        [Test]
        public void SinglePalette_AnswersForEveryTriangle()
        {
            var stone = MakeSurface("Stone", 0.7f, false);
            var map = new GameObject("map").AddComponent<SurfaceMap>();
            spawned.Add(map.gameObject);
            map.palette = new[] { stone };
            map.triangleSurface = new byte[0];

            Assert.AreSame(stone, map.SurfaceAt(0));
            Assert.AreSame(stone, map.SurfaceAt(57));
            Assert.AreEqual(0.7f, map.MaxTravelMeters(3, 0.8f), 0.0001f);
            Assert.AreEqual("Stone", map.SurfaceName(3));
        }

        [Test]
        public void PerTriangleTags_ResolveIndependently()
        {
            var wood = MakeSurface("Wood", 1f, false);
            var metal = MakeSurface("Metal", 0.45f, false);
            var map = new GameObject("map").AddComponent<SurfaceMap>();
            spawned.Add(map.gameObject);
            map.palette = new[] { wood, metal };
            map.triangleSurface = new byte[] { 0, 1, 1 };

            Assert.AreSame(wood, map.SurfaceAt(0));
            Assert.AreSame(metal, map.SurfaceAt(1));
            Assert.AreEqual(0.45f, map.MaxTravelMeters(2, 0.8f), 0.0001f);
        }

        [Test]
        public void OutOfRangeOrEmpty_FallsBackSafely()
        {
            var wood = MakeSurface("Wood", 1f, false);
            var map = new GameObject("map").AddComponent<SurfaceMap>();
            spawned.Add(map.gameObject);
            map.palette = new[] { wood };
            map.triangleSurface = new byte[] { 0 };

            Assert.AreSame(wood, map.SurfaceAt(99), "a drifted index must not throw");
            Assert.AreSame(wood, map.SurfaceAt(-1));

            map.palette = new SurfaceData[0];
            Assert.IsNull(map.SurfaceAt(0));
            Assert.AreEqual(0.8f, map.MaxTravelMeters(0, 0.8f), 0.0001f);
            Assert.IsFalse(map.IsPlantSite(0));
        }

        // --- The physics contract the whole design rests on ---

        [Test]
        public void RaycastTriangleIndex_MatchesTheTagOrder()
        {
            var wood = MakeSurface("Wood", 1f, false);
            var metal = MakeSurface("Metal", 0.45f, false);
            SurfaceMap map = MakeFloor(wood, metal);

            // Centroids of triangle 0 and triangle 1 of the quad.
            var overFirst = new Vector3(1f / 3f, 0.5f, 2f / 3f);
            var overSecond = new Vector3(2f / 3f, 0.5f, 1f / 3f);

            Assert.IsTrue(Physics.Raycast(overFirst, Vector3.down, out RaycastHit hitA, 1f, VisionLayerMasks.GroundOnly));
            Assert.AreEqual(0, hitA.triangleIndex);
            Assert.AreSame(wood, map.SurfaceAt(hitA.triangleIndex));

            Assert.IsTrue(Physics.Raycast(overSecond, Vector3.down, out RaycastHit hitB, 1f, VisionLayerMasks.GroundOnly));
            Assert.AreEqual(1, hitB.triangleIndex);
            Assert.AreSame(metal, map.SurfaceAt(hitB.triangleIndex));
        }

        // --- Planting ---

        [Test]
        public void StandingOnAPlantSiteFace_AllowsPlanting()
        {
            var site = MakeSurface("PlantArea", 0.8f, true);
            MakeFloor(site, null);

            Assert.IsTrue(PlantSiteQuery.IsOnPlantSite(new Vector3(0.5f, 0f, 0.5f), out SurfaceData found));
            Assert.AreSame(site, found);
        }

        [Test]
        public void StandingOnAnOrdinaryFloor_DoesNotAllowPlanting()
        {
            var dirt = MakeSurface("Ground", 0.8f, false);
            MakeFloor(dirt, null);

            Assert.IsFalse(PlantSiteQuery.IsOnPlantSite(new Vector3(0.5f, 0f, 0.5f)));
        }

        [Test]
        public void HalfOfASiteFloor_PlantsOnlyOnThePaintedTriangle()
        {
            // The jagged-site case: one quad, one triangle painted as site.
            var site = MakeSurface("PlantArea", 0.8f, true);
            var plain = MakeSurface("Ground", 0.8f, false);
            MakeFloor(site, plain);

            Assert.IsTrue(PlantSiteQuery.IsOnPlantSite(new Vector3(1f / 3f, 0f, 2f / 3f)));
            Assert.IsFalse(PlantSiteQuery.IsOnPlantSite(new Vector3(2f / 3f, 0f, 1f / 3f)));
        }

        [Test]
        public void NoFloorWithinReach_DoesNotAllowPlanting()
        {
            var site = MakeSurface("PlantArea", 0.8f, true);
            MakeFloor(site, null);

            // Jumping over the site: the probe never reaches the floor.
            Assert.IsFalse(PlantSiteQuery.IsOnPlantSite(new Vector3(0.5f, 3f, 0.5f)));
            // Standing somewhere with no floor at all.
            Assert.IsFalse(PlantSiteQuery.IsOnPlantSite(new Vector3(40f, 0f, 40f)));
        }
    }
}
