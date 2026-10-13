using NUnit.Framework;
using Game.Core.Models;
using Game.Core.Types;
using Game.Simulation.Systems;

namespace Game.Core.Tests
{
    [TestFixture]
    public class CaptureSystemTests
    {
        [Test]
        public void CaptureOrder_CapturesFactoryAfterFifteenSeconds()
        {
            var state = CreateState();
            var factory = CreateBuilding(10, BuildingType.Factory, PlayerId.Neutral);
            state.Buildings[10] = factory;
            state.Robots[1] = CreateCapturingRobot(1, PlayerId.Player1, factory.Id);

            new CaptureSystem().Update(state, 15f);

            Assert.AreEqual(PlayerId.Player1, factory.Owner);
            Assert.AreEqual(PlayerId.Neutral, factory.CapturingPlayer);
            Assert.AreEqual(0f, factory.CaptureProgressSeconds);
        }

        [Test]
        public void EnemyPresence_PausesCapture()
        {
            var state = CreateState();
            var factory = CreateBuilding(10, BuildingType.Factory, PlayerId.Neutral);
            state.Buildings[10] = factory;
            state.Robots[1] = CreateCapturingRobot(1, PlayerId.Player1, factory.Id);
            state.Robots[2] = CreateCapturingRobot(2, PlayerId.Player2, factory.Id);

            new CaptureSystem().Update(state, 5f);

            Assert.IsTrue(factory.IsContested);
            Assert.AreEqual(0f, factory.CaptureProgressSeconds);
        }

        [Test]
        public void RobotWithoutCaptureOrder_DoesNotCaptureBuilding()
        {
            var state = CreateState();
            var factory = CreateBuilding(10, BuildingType.Factory, PlayerId.Neutral);
            state.Buildings[10] = factory;
            state.Robots[1] = new RobotState
            {
                Id = new EntityId(1), Owner = PlayerId.Player1, CurrentHealth = 100, MaxHealth = 100,
                Position = factory.Position, CurrentOrder = RobotOrder.Stop()
            };

            new CaptureSystem().Update(state, 20f);

            Assert.AreEqual(PlayerId.Neutral, factory.Owner);
            Assert.AreEqual(0f, factory.CaptureProgressSeconds);
        }

        private static MatchState CreateState() => new MatchState();

        private static BuildingState CreateBuilding(int id, BuildingType type, PlayerId owner) => new BuildingState
        {
            Id = new EntityId(id), Type = type, Owner = owner,
            Position = new SimVector2(5f, 5f), CaptureRadius = 3f
        };

        private static RobotState CreateCapturingRobot(int id, PlayerId owner, EntityId target) => new RobotState
        {
            Id = new EntityId(id), Owner = owner, CurrentHealth = 100, MaxHealth = 100,
            Position = new SimVector2(5f, 5f), CurrentOrder = RobotOrder.Capture(target)
        };
    }
}
