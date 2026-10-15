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
    public class MatchSimulationTests
    {
        private const int Player1BaseId = 1;
        private const int CentralFactoryId = 102;

        [Test]
        public void AdvanceFrame_RunsFixedTicksIndependentOfFrameRate()
        {
            var simulation = new MatchSimulation(DefaultContentCatalog.CreateDefaultModules(), DefaultMapFactory.CreatePrototypeMap());

            int steps = simulation.AdvanceFrame(0.12f);

            Assert.AreEqual(2, steps, "0,12 с при 20 тиках/с — два полных тика, остаток копится.");
            Assert.AreEqual(2, simulation.State.Tick);
            Assert.AreEqual(0.4f, simulation.InterpolationAlpha, 0.001f);
        }

        [Test]
        public void MoveCommand_RobotWalksAlongPathInsideSimulation()
        {
            var simulation = new MatchSimulation(DefaultContentCatalog.CreateDefaultModules(), DefaultMapFactory.CreatePrototypeMap());
            RobotState robot = SpawnFirstRobot(simulation);
            SimVector2 start = robot.Position;
            var destination = new SimVector2(start.X + 6f, start.Y);

            CommandResult result = simulation.Issue(new OrderMoveCommand { PlayerId = PlayerId.Player1, RobotIds = new List<EntityId> { robot.Id }, TargetPosition = destination });
            for (int tick = 0; tick < 200; tick++) simulation.Step(MatchSimulation.FixedDeltaSeconds);

            Assert.IsTrue(result.IsSuccess, result.ErrorReason);
            Assert.Less(SimVector2.Distance(robot.Position, destination), 0.6f, $"Робот остановился в {robot.Position}.");
            Assert.AreEqual(SimVector2.Zero, robot.Velocity, "После прибытия скорость обнуляется.");
        }

        [Test]
        public void DriveCommand_MovesRobotUntilDirectionReset()
        {
            var simulation = new MatchSimulation(DefaultContentCatalog.CreateDefaultModules(), DefaultMapFactory.CreatePrototypeMap());
            RobotState robot = SpawnFirstRobot(simulation);
            SimVector2 start = robot.Position;
            var ids = new List<EntityId> { robot.Id };

            simulation.Issue(new OrderDriveCommand { PlayerId = PlayerId.Player1, RobotIds = ids, Direction = new SimVector2(0f, 1f) });
            for (int tick = 0; tick < 20; tick++) simulation.Step(MatchSimulation.FixedDeltaSeconds);
            simulation.Issue(new OrderDriveCommand { PlayerId = PlayerId.Player1, RobotIds = ids, Direction = SimVector2.Zero });
            SimVector2 afterDrive = robot.Position;
            for (int tick = 0; tick < 10; tick++) simulation.Step(MatchSimulation.FixedDeltaSeconds);

            Assert.Greater(afterDrive.Y, start.Y + 1f);
            Assert.AreEqual(afterDrive, robot.Position, "Без направления робот стоит.");
            Assert.AreEqual(OrderType.Stop, robot.CurrentOrder.Type);
        }

        [Test]
        public void Replay_ReproducesMatchPlayedWithIrregularFrames()
        {
            Dictionary<string, ModuleDefinition> catalog = DefaultContentCatalog.CreateDefaultModules();
            MapDefinition map = DefaultMapFactory.CreatePrototypeMap();

            // Живой матч: неровные кадры, ИИ, заказ и приказ захвата от игрока.
            var live = new MatchSimulation(catalog, map);
            live.AddAgent(new AiCommander(live.Dispatcher, catalog));
            live.Issue(new EnqueueProductionCommand { PlayerId = PlayerId.Player1, BaseBuildingId = new EntityId(Player1BaseId), Blueprint = RobotPresetCatalog.Scout.CreateBlueprint() });
            float[] frames = { 0.016f, 0.033f, 0.051f, 0.008f, 0.1f, 0.022f };
            bool captureIssued = false;
            for (int frame = 0; live.State.Tick < 1200 && live.State.Result == MatchResult.InProgress; frame++)
            {
                if (!captureIssued && TryGetFirstRobot(live.State, PlayerId.Player1, out RobotState scout))
                {
                    captureIssued = live.Issue(new OrderCaptureCommand { PlayerId = PlayerId.Player1, RobotIds = new List<EntityId> { scout.Id }, TargetBuildingId = new EntityId(CentralFactoryId) }).IsSuccess;
                }
                live.AdvanceFrame(frames[frame % frames.Length]);
            }
            Assert.IsTrue(captureIssued, "Робот игрока должен был появиться и получить приказ.");

            // Повтор: тот же старт, тот же ИИ, только журнал команд игрока.
            MatchState replayState = MatchSimulation.CreateInitialState(map);
            var runner = new HeadlessMatchRunner(catalog, map, new AiCommander(new CommandDispatcher(catalog), catalog));
            runner.RunTicks(replayState, live.CommandLog, live.State.Tick);

            Assert.AreEqual(live.State.Tick, replayState.Tick);
            CollectionAssert.AreEqual(live.State.Player1.Resources.ToArray(), replayState.Player1.Resources.ToArray());
            CollectionAssert.AreEqual(live.State.Player2.Resources.ToArray(), replayState.Player2.Resources.ToArray());
            Assert.AreEqual(live.State.Robots.Count, replayState.Robots.Count);
            Assert.Greater(live.State.Robots.Count, 1, "В прогоне должны участвовать роботы обеих сторон.");
            foreach (RobotState expected in live.State.Robots.Values)
            {
                RobotState actual = replayState.Robots[expected.Id.Value];
                Assert.AreEqual(expected.Position.X, actual.Position.X, $"X робота #{expected.Id.Value}");
                Assert.AreEqual(expected.Position.Y, actual.Position.Y, $"Y робота #{expected.Id.Value}");
                Assert.AreEqual(expected.CurrentHealth, actual.CurrentHealth, $"HP робота #{expected.Id.Value}");
                Assert.AreEqual(expected.CurrentOrder.Type, actual.CurrentOrder.Type, $"Приказ робота #{expected.Id.Value}");
            }
            foreach (BuildingState expected in live.State.Buildings.Values)
            {
                BuildingState actual = replayState.Buildings[expected.Id.Value];
                Assert.AreEqual(expected.Owner, actual.Owner);
                Assert.AreEqual(expected.CaptureProgressSeconds, actual.CaptureProgressSeconds);
            }
        }

        private static RobotState SpawnFirstRobot(MatchSimulation simulation)
        {
            simulation.Issue(new EnqueueProductionCommand { PlayerId = PlayerId.Player1, BaseBuildingId = new EntityId(Player1BaseId), Blueprint = RobotPresetCatalog.Scout.CreateBlueprint() });
            for (int tick = 0; tick < 2000 && !TryGetFirstRobot(simulation.State, PlayerId.Player1, out _); tick++)
            {
                simulation.Step(MatchSimulation.FixedDeltaSeconds);
            }
            Assert.IsTrue(TryGetFirstRobot(simulation.State, PlayerId.Player1, out RobotState robot), "Робот не вышел с базы.");
            // Сбрасываем приказ обороны, чтобы робот стоял до команды теста.
            simulation.Issue(new OrderStopCommand { PlayerId = PlayerId.Player1, RobotIds = new List<EntityId> { robot.Id } });
            return robot;
        }

        private static bool TryGetFirstRobot(MatchState state, PlayerId owner, out RobotState robot)
        {
            foreach (RobotState candidate in state.Robots.Values)
            {
                if (candidate.IsAlive && candidate.Owner == owner)
                {
                    robot = candidate;
                    return true;
                }
            }
            robot = null;
            return false;
        }
    }
}
