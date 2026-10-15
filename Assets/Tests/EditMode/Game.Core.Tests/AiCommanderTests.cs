using System.Collections.Generic;
using NUnit.Framework;
using Game.AI;
using Game.Content.Definitions;
using Game.Core.Commands;
using Game.Core.Models;
using Game.Core.Types;
using Game.Simulation.Systems;

namespace Game.Core.Tests
{
    [TestFixture]
    public class AiCommanderTests
    {
        private Dictionary<string, ModuleDefinition> _catalog;

        [SetUp]
        public void SetUp()
        {
            _catalog = DefaultContentCatalog.CreateDefaultModules();
        }

        [Test]
        public void Think_QueuesProductionAndCapturesNearestNeutralFactory_ThroughDispatcher()
        {
            MatchState state = CreateMatch();
            state.Robots[20] = CreateRobot(20, PlayerId.Player2, new SimVector2(40f, 15f));
            var ai = new AiCommander(new CommandDispatcher(_catalog), _catalog);

            ai.Think(state);

            Assert.AreEqual(1, state.Buildings[2].ProductionQueue.Count);
            Assert.AreEqual(OrderType.Capture, state.Robots[20].CurrentOrder.Type);
            Assert.AreEqual(new EntityId(101), state.Robots[20].CurrentOrder.TargetEntityId);
        }

        [Test]
        public void Think_DefendsBaseWhenEnemyIsNearby()
        {
            MatchState state = CreateMatch();
            state.Robots[20] = CreateRobot(20, PlayerId.Player2, new SimVector2(40f, 15f));
            state.Robots[10] = CreateRobot(10, PlayerId.Player1, new SimVector2(38f, 15f));
            var ai = new AiCommander(new CommandDispatcher(_catalog), _catalog);

            ai.Think(state);

            Assert.AreEqual(OrderType.Defend, state.Robots[20].CurrentOrder.Type);
            Assert.AreEqual(state.Buildings[2].Position, state.Robots[20].CurrentOrder.TargetPosition);
        }

        private static MatchState CreateMatch()
        {
            var state = new MatchState();
            state.Player1.Metal = 600;
            state.Player2.Metal = 600;
            state.Buildings[1] = new BuildingState { Id = new EntityId(1), Type = BuildingType.Base, Owner = PlayerId.Player1, Position = new SimVector2(4f, 15f) };
            state.Buildings[2] = new BuildingState { Id = new EntityId(2), Type = BuildingType.Base, Owner = PlayerId.Player2, Position = new SimVector2(43f, 15f) };
            state.Buildings[101] = new BuildingState { Id = new EntityId(101), Type = BuildingType.Factory, Owner = PlayerId.Neutral, Position = new SimVector2(30f, 15f) };
            return state;
        }

        private static RobotState CreateRobot(int id, PlayerId owner, SimVector2 position) => new RobotState
        {
            Id = new EntityId(id), Owner = owner, Position = position, CurrentHealth = 100, MaxHealth = 100, CurrentOrder = new RobotOrder()
        };
    }
}
