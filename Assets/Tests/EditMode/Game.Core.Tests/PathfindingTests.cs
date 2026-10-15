using System.Collections.Generic;
using NUnit.Framework;
using Game.Content.Definitions;
using Game.Core.Models;
using Game.Core.Types;
using Game.Simulation.Navigation;

namespace Game.Core.Tests
{
    [TestFixture]
    public class PathfindingTests
    {
        [Test]
        public void A04_ShouldBypassRidgeThroughPass()
        {
            var map = DefaultMapFactory.CreatePrototypeMap();
            var nav = new NavigationGrid(map, MobilityClass.Walker);
            var pathfinder = new AStarPathfinder(nav);

            // От базы игрока за западный хребет, к центру карты.
            bool success = pathfinder.TryFindPath(new SimVector2(12.5f, 30.5f), new SimVector2(55.5f, 25.5f), out var waypoints);

            Assert.IsTrue(success, "Путь через проход в хребте должен быть найден");
            Assert.IsTrue(waypoints.Count > 1);
            foreach (var pt in waypoints)
            {
                var cell = nav.WorldToGrid(pt);
                Assert.IsTrue(nav.IsWalkable(cell.X, cell.Y), $"Точка пути {pt} ({cell}) не должна быть на непроходимом тайле");
            }
        }

        [Test]
        public void A04_StartOnMapEdge_ShouldFailForEveryChassis()
        {
            var map = DefaultMapFactory.CreatePrototypeMap();
            foreach (MobilityClass mobility in AllMobilities)
            {
                var pathfinder = new AStarPathfinder(new NavigationGrid(map, mobility));
                Assert.IsFalse(pathfinder.TryFindPath(new SimVector2(0.5f, 0.5f), new SimVector2(5f, 5f), out _), $"{mobility}: край карты непроходим");
            }
        }

        [Test]
        public void UnwalkableTarget_ShouldEndOnWalkableCell()
        {
            var map = DefaultMapFactory.CreatePrototypeMap();
            var nav = new NavigationGrid(map);
            var pathfinder = new AStarPathfinder(nav);

            bool success = pathfinder.TryFindPath(new SimVector2(4.5f, 10f), new SimVector2(-2f, 10f), out var waypoints);

            Assert.IsTrue(success);
            var end = nav.WorldToGrid(waypoints[waypoints.Count - 1]);
            Assert.IsTrue(nav.IsWalkable(end.X, end.Y), "Финиш должен оставаться в проходимой клетке.");
        }

        [Test]
        public void BlockCircle_MakesBuildingFootprintUnwalkable_EvenForHover()
        {
            var map = DefaultMapFactory.CreatePrototypeMap();
            var nav = BuildingFootprint.CreateMatchGrid(map, MobilityClass.Hover);

            foreach (BuildingSpawnDefinition building in map.Buildings)
            {
                GridCoord center = nav.WorldToGrid(building.Position);
                Assert.IsFalse(nav.IsWalkable(center.X, center.Y), $"Здание #{building.EntityIdValue} должно быть препятствием и для антиграва.");
            }
        }

        [Test]
        public void TerrainRules_DifferPerChassis()
        {
            Assert.AreEqual(0f, NavigationGrid.GetTerrainSpeed(TileType.Rough, MobilityClass.Walker), "Шагающие застревают на неровностях.");
            Assert.AreEqual(0f, NavigationGrid.GetTerrainSpeed(TileType.Crack, MobilityClass.Walker));
            Assert.Greater(NavigationGrid.GetTerrainSpeed(TileType.Rough, MobilityClass.Tracked), 0f, "Гусеницы проходят неровности.");
            Assert.AreEqual(0f, NavigationGrid.GetTerrainSpeed(TileType.Crack, MobilityClass.Tracked), "Гусеницы застревают на трещинах.");
            foreach (TileType tile in new[] { TileType.Crack, TileType.Rough, TileType.Water, TileType.Mountain })
            {
                Assert.AreEqual(1f, NavigationGrid.GetTerrainSpeed(tile, MobilityClass.Hover), $"Антиграв летит над {tile}.");
            }
        }

        [Test]
        public void PrototypeMap_HasTerrainForEveryChassisRule()
        {
            var map = DefaultMapFactory.CreatePrototypeMap();
            var counts = new Dictionary<TileType, int>();
            foreach (TileType tile in map.Tiles) counts[tile] = counts.TryGetValue(tile, out int count) ? count + 1 : 1;

            Assert.AreEqual(128, map.Width);
            Assert.AreEqual(80, map.Height);
            foreach (TileType tile in new[] { TileType.Crack, TileType.Rough, TileType.Water, TileType.Bridge, TileType.Mountain, TileType.Road })
            {
                Assert.IsTrue(counts.ContainsKey(tile), $"На карте нет поверхности {tile}.");
            }
        }

        /// <summary>Требование ТЗ: обе стороны имеют путь к каждому объекту — даже на самом ограниченном шасси.</summary>
        [Test]
        public void EveryChassis_ReachesEveryBuildingCaptureZone_FromBothBases()
        {
            var map = DefaultMapFactory.CreatePrototypeMap();
            foreach (MobilityClass mobility in AllMobilities)
            {
                NavigationGrid grid = BuildingFootprint.CreateMatchGrid(map, mobility);
                var pathfinder = new AStarPathfinder(grid);
                foreach (BuildingSpawnDefinition from in map.Buildings)
                {
                    if (from.Type != BuildingType.Base) continue;
                    foreach (BuildingSpawnDefinition target in map.Buildings)
                    {
                        if (target == from) continue;
                        Assert.IsTrue(TryReachCaptureZone(grid, pathfinder, from.SpawnPoint, target),
                            $"{mobility}: с базы #{from.EntityIdValue} нет пути к зоне захвата здания #{target.EntityIdValue}.");
                    }
                }
            }
        }

        [Test]
        public void ObstaclesChangeRoutes_HoverShortest_WalkerLongest()
        {
            var map = DefaultMapFactory.CreatePrototypeMap();
            var start = new SimVector2(12.5f, 64.5f);   // северо-запад, за оврагом y=66 — фазерный завод южнее
            var end = new SimVector2(64.5f, 64.5f);     // к ядерному броду

            float walker = PathLength(map, MobilityClass.Walker, start, end);
            float tracked = PathLength(map, MobilityClass.Tracked, start, end);
            float hover = PathLength(map, MobilityClass.Hover, start, end);

            Assert.Less(tracked, walker, "Гусеницы идут через заваленный неровностями северный проход, шагающие — в обход.");
            Assert.Less(hover, tracked, "Антиграв летит над хребтом и рекой.");
        }

        private static readonly MobilityClass[] AllMobilities = { MobilityClass.Walker, MobilityClass.Tracked, MobilityClass.Hover };

        private static bool TryReachCaptureZone(NavigationGrid grid, AStarPathfinder pathfinder, SimVector2 start, BuildingSpawnDefinition target)
        {
            float clearance = BuildingFootprint.GetClearance(target.Type);
            for (int y = 0; y < grid.Height; y++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    if (!grid.IsWalkable(x, y)) continue;
                    SimVector2 point = grid.GridToWorld(new GridCoord(x, y));
                    float distance = SimVector2.Distance(point, target.Position);
                    if (distance < clearance || distance > target.CaptureRadius) continue;
                    if (pathfinder.TryFindPath(start, point, out var path) && SimVector2.Distance(path[path.Count - 1], point) < 0.01f) return true;
                }
            }
            return false;
        }

        private static float PathLength(MapDefinition map, MobilityClass mobility, SimVector2 start, SimVector2 end)
        {
            var pathfinder = new AStarPathfinder(BuildingFootprint.CreateMatchGrid(map, mobility));
            Assert.IsTrue(pathfinder.TryFindPath(start, end, out var path), $"{mobility}: путь не найден");
            float length = 0f;
            SimVector2 previous = start;
            foreach (SimVector2 point in path)
            {
                length += SimVector2.Distance(previous, point);
                previous = point;
            }
            return length;
        }
    }
}
