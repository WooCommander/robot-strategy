using System.IO;
using NUnit.Framework;
using Game.Core.Models;
using Game.Core.Types;
using Game.Persistence;

namespace Game.Core.Tests
{
    [TestFixture]
    public class MatchPersistenceServiceTests
    {
        [Test]
        public void SerializeAndDeserialize_RestoresMatchStateIncludingQueueAndOrders()
        {
            MatchState source = CreateState();
            var service = new MatchPersistenceService();

            MatchState restored = service.Deserialize(service.Serialize(source));

            Assert.AreEqual(42, restored.Tick);
            Assert.AreEqual(777, restored.Player1.Metal);
            Assert.AreEqual(2, restored.Robots.Count);
            Assert.AreEqual(new EntityId(101), restored.Robots[10].CurrentOrder.TargetEntityId);
            Assert.AreEqual(1, restored.Buildings[1].ProductionQueue.Count);
            Assert.AreEqual("order-1", restored.Buildings[1].ProductionQueue[0].OrderId);
            Assert.AreEqual(7.5f, restored.Buildings[1].ProductionQueue[0].RemainingBuildTimeSeconds);
            Assert.AreEqual(1, restored.Projectiles.Count);
            Assert.AreEqual(0.2f, restored.Projectiles[0].RemainingTravelSeconds);
        }

        [Test]
        public void SerializeAndDeserialize_RestoresMovementStateAndIdCounters()
        {
            MatchState source = CreateState();
            source.NextRobotId = 1050;
            source.NextProductionOrderId = 9;
            RobotState robot = source.Robots[10];
            robot.SetPath(new System.Collections.Generic.List<SimVector2> { new SimVector2(5f, 9.5f), new SimVector2(6f, 9.5f) });
            robot.PathIndex = 1;
            robot.DriveDirection = new SimVector2(0f, 1f);
            robot.NextManeuverSeconds = 12.5f;
            var service = new MatchPersistenceService();

            MatchState restored = service.Deserialize(service.Serialize(source));

            RobotState restoredRobot = restored.Robots[10];
            Assert.AreEqual(2, restoredRobot.Path.Count);
            Assert.AreEqual(new SimVector2(6f, 9.5f), restoredRobot.Path[1]);
            Assert.AreEqual(1, restoredRobot.PathIndex);
            Assert.AreEqual(new SimVector2(0f, 1f), restoredRobot.DriveDirection);
            Assert.AreEqual(12.5f, restoredRobot.NextManeuverSeconds);
            Assert.AreEqual(1050, restored.NextRobotId);
            Assert.AreEqual(9, restored.NextProductionOrderId);
        }

        [Test]
        public void ManualAndAutoSlots_RoundTripToSpecifiedDirectory()
        {
            string directory = Path.Combine(Path.GetTempPath(), "robot-strategy-tests", System.Guid.NewGuid().ToString("N"));
            try
            {
                var service = new MatchPersistenceService(directory);
                MatchState state = CreateState();
                service.SaveManual(1, state);
                service.SaveAuto(state);

                Assert.IsTrue(service.HasManualSave(1));
                Assert.IsTrue(service.HasAutoSave());
                Assert.AreEqual(42, service.LoadManual(1).Tick);
                Assert.AreEqual(42, service.LoadAuto().Tick);
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        private static MatchState CreateState()
        {
            var state = new MatchState { Tick = 42, MatchTimeSeconds = 91.5f, IsPaused = true };
            state.Player1 = new PlayerState { Id = PlayerId.Player1, Metal = 777, MetalFraction = 0.25f, ActiveRobotsCount = 1, QueuedRobotsCount = 1 };
            state.Player2 = new PlayerState { Id = PlayerId.Player2, Metal = 321 };
            state.Robots[10] = new RobotState { Id = new EntityId(10), Owner = PlayerId.Player1, CurrentHealth = 90, MaxHealth = 120, Position = new SimVector2(4.5f, 9.5f), CurrentOrder = RobotOrder.Capture(new EntityId(101)) };
            state.Robots[20] = new RobotState { Id = new EntityId(20), Owner = PlayerId.Player2, CurrentHealth = 120, MaxHealth = 120, Position = new SimVector2(30f, 9.5f), CurrentOrder = RobotOrder.Defend(new SimVector2(31f, 10f)) };
            state.Projectiles.Add(new ProjectileState { SourceRobotId = new EntityId(10), TargetRobotId = new EntityId(20), Owner = PlayerId.Player1, WeaponType = WeaponType.DirectCannon, Damage = 35, ImpactPosition = new SimVector2(30f, 9.5f), RemainingTravelSeconds = 0.2f });
            var baseBuilding = new BuildingState { Id = new EntityId(1), Type = BuildingType.Base, Owner = PlayerId.Player1, Position = new SimVector2(3f, 8f), SpawnPoint = new SimVector2(5f, 8f) };
            baseBuilding.ProductionQueue.Add(new ProductionOrder { OrderId = "order-1", Cost = 350, TotalBuildTimeSeconds = 8f, RemainingBuildTimeSeconds = 7.5f, Status = OrderProductionStatus.InProgress });
            state.Buildings[1] = baseBuilding;
            state.Buildings[101] = new BuildingState { Id = new EntityId(101), Type = BuildingType.Factory, Owner = PlayerId.Neutral, Position = new SimVector2(16f, 8f) };
            return state;
        }
    }
}
