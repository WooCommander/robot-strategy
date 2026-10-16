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

        [Test]
        public void Think_ChoosesAnotherFactory_WhenNearestIsAlreadyCapturedByEnemy()
        {
            MatchState state = CreateMatch();
            state.Buildings[101].CapturingPlayer = PlayerId.Player1;
            state.Buildings[101].CaptureProgressSeconds = 6f;
            state.Buildings[102] = new BuildingState { Id = new EntityId(102), Type = BuildingType.Factory, Owner = PlayerId.Neutral, Position = new SimVector2(24f, 15f) };
            state.Robots[20] = CreateRobot(20, PlayerId.Player2, new SimVector2(40f, 15f));
            var ai = new AiCommander(new CommandDispatcher(_catalog), _catalog);

            ai.Think(state);

            Assert.AreEqual(OrderType.Capture, state.Robots[20].CurrentOrder.Type);
            Assert.AreEqual(new EntityId(102), state.Robots[20].CurrentOrder.TargetEntityId);
        }

        [Test]
        public void Think_ScoutsInsteadOfKnowingUnexploredEnemyBase()
        {
            var map = new MapDefinition { Width = 40, Height = 12, TileSize = 1f, Tiles = new TileType[40 * 12] };
            map.Buildings.Add(new BuildingSpawnDefinition { EntityIdValue = 1, Type = BuildingType.Base, InitialOwner = PlayerId.Player1, Position = new SimVector2(3.5f, 6.5f) });
            map.Buildings.Add(new BuildingSpawnDefinition { EntityIdValue = 2, Type = BuildingType.Base, InitialOwner = PlayerId.Player2, Position = new SimVector2(36.5f, 6.5f) });
            var simulation = new MatchSimulation(_catalog, map);
            MatchState state = simulation.State;
            state.Robots[20] = CreateRobot(20, PlayerId.Player2, new SimVector2(36.5f, 6.5f));
            simulation.Vision.Update(state);
            Assert.IsFalse(state.FogOfWar.IsExplored(PlayerId.Player2, state.Buildings[1].Position));

            new AiCommander(simulation.Dispatcher, _catalog).Think(state);

            Assert.AreEqual(OrderType.Scout, state.Robots[20].CurrentOrder.Type);
        }

        private static MatchState CreateMatch()
        {
            var state = new MatchState();
            state.Player1.Resources[ResourceType.General] = EconomyRules.StartingGeneral;
            state.Player2.Resources[ResourceType.General] = EconomyRules.StartingGeneral;
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
