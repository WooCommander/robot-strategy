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
    }
}
