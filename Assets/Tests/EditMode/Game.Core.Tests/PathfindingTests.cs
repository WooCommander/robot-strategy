using NUnit.Framework;
using Game.Content.Definitions;
using Game.Core.Types;
using Game.Simulation.Navigation;

namespace Game.Core.Tests
{
    [TestFixture]
    public class PathfindingTests
    {
        [Test]
        public void A04_ShouldBypassMountainObstacle()
        {
            var map = DefaultMapFactory.CreatePrototypeMap();
            var nav = new NavigationGrid(map);
            var pathfinder = new AStarPathfinder(nav);

            // База Игрока 1 (слева, x=4.5, y=10) пытается дойти до центрального завода за горой (x=16.5, y=10)
            var start = new SimVector2(4.5f, 10.0f);
            var target = new SimVector2(16.5f, 10.0f);

            bool success = pathfinder.TryFindPath(start, target, out var waypoints);

            Assert.IsTrue(success, "Путь вокруг препятствий должен быть найден");
            Assert.IsTrue(waypoints.Count > 1, "Путь должен состоять из нескольких путевых точек");

            // Проверяем, что ни одна точка не лежит на скале
            foreach (var pt in waypoints)
            {
                var grid = nav.WorldToGrid(pt);
                Assert.IsTrue(nav.IsWalkable(grid.X, grid.Y), $"Точка пути {pt} ({grid}) не должна быть на непроходимом тайле");
            }
        }

        [Test]
        public void A04_UnreachableTarget_ShouldReturnFalseOrNearest()
        {
            var map = DefaultMapFactory.CreatePrototypeMap();
            var nav = new NavigationGrid(map);
            var pathfinder = new AStarPathfinder(nav);

            // Старт на внешней границе (скале)
            var startInsideRock = new SimVector2(0.5f, 0.5f);
            var target = new SimVector2(5.0f, 5.0f);

            bool success = pathfinder.TryFindPath(startInsideRock, target, out _);
            Assert.IsFalse(success, "Нельзя стартовать из непроходимой скалы");
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
        public void BlockCircle_MakesBuildingFootprintUnwalkable()
        {
            var map = DefaultMapFactory.CreatePrototypeMap();
            var nav = new NavigationGrid(map);
            var factory = map.Buildings[2];

            nav.BlockCircle(factory.Position, 1.65f);

            GridCoord center = nav.WorldToGrid(factory.Position);
            Assert.IsFalse(nav.IsWalkable(center.X, center.Y));
        }
    }
}
