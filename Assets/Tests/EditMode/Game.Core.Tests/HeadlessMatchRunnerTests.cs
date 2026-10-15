using System.Collections.Generic;
using NUnit.Framework;
using Game.Content.Definitions;
using Game.Core.Commands;
using Game.Core.Models;
using Game.Core.Types;
using Game.Simulation.Systems;

namespace Game.Core.Tests
{
    [TestFixture]
    public class HeadlessMatchRunnerTests
    {
        [Test]
        public void ReplayLog_RestoresCommandsAndProducesDeterministicCombatResult()
        {
            Dictionary<string, ModuleDefinition> catalog = DefaultContentCatalog.CreateDefaultModules();
            MatchState state = CreateMatch();
            var log = new ReplayCommandLog();
            log.Record(0, new OrderMoveCommand { PlayerId = PlayerId.Player1, SequenceNumber = 1, RobotIds = new List<EntityId> { new EntityId(1) }, TargetPosition = new SimVector2(2f, 3f) });
            log.Record(1, new OrderAttackCommand { PlayerId = PlayerId.Player1, SequenceNumber = 2, RobotIds = new List<EntityId> { new EntityId(1) }, TargetEntityId = new EntityId(2) });
            var runner = new HeadlessMatchRunner(catalog);

            // Выстрел на тике 1, снаряд летит 5 м / 18 м/с ≈ 0,28 с — к тику 5 он уже попал.
            runner.RunTicks(state, log, 5, 0.1f);

            Assert.AreEqual(5, state.Tick);
            Assert.AreEqual(0.5f, state.MatchTimeSeconds, 0.001f);
            Assert.AreEqual(OrderType.Attack, state.Robots[1].CurrentOrder.Type);
            Assert.AreEqual(65, state.Robots[2].CurrentHealth);
            int commandsAtTickOne = 0;
            foreach (ReplayCommandRecord ignored in log.GetForTick(1)) commandsAtTickOne++;
            Assert.AreEqual(1, commandsAtTickOne, "Журнал должен хранить команды без обращения к Unity.");
        }

        [Test]
        public void ReplayRecord_RoundTripsProductionBlueprint()
        {
            var source = new EnqueueProductionCommand
            {
                PlayerId = PlayerId.Player1,
                SequenceNumber = 7,
                BaseBuildingId = new EntityId(1),
                Blueprint = RobotPresetCatalog.Scout.CreateBlueprint()
            };

            ICommand restored = ReplayCommandRecord.FromCommand(5, source).ToCommand();

            var production = restored as EnqueueProductionCommand;
            Assert.NotNull(production);
            Assert.AreEqual(7, production.SequenceNumber);
            CollectionAssert.AreEqual(source.Blueprint.WeaponIds, production.Blueprint.WeaponIds);
            Assert.AreEqual(source.Blueprint.ChassisId, production.Blueprint.ChassisId);
        }

        private static MatchState CreateMatch()
        {
            var state = new MatchState();
            state.Robots[1] = CreateRobot(1, PlayerId.Player1, 0f);
            state.Robots[2] = CreateRobot(2, PlayerId.Player2, 5f);
            return state;
        }

        private static RobotState CreateRobot(int id, PlayerId owner, float x) => new RobotState
        {
            Id = new EntityId(id), Owner = owner, Position = new SimVector2(x, 0f), CurrentHealth = 100, MaxHealth = 100,
            Blueprint = new RobotBlueprint("bp_" + id, "Test", DefaultContentCatalog.ChassisBipod, new[] { DefaultContentCatalog.WeaponCannon }),
            CurrentOrder = new RobotOrder()
        };
    }
}
