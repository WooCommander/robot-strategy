using System;
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
        public void FogOfWar_RevealsAndRemembersTerrainButHidesEnemyAgain()
        {
            MapDefinition map = DefaultMapFactory.CreatePrototypeMap();
            var simulation = new MatchSimulation(DefaultContentCatalog.CreateDefaultModules(), map);
            MatchState state = simulation.State;
            BuildingState ownBase = state.Buildings[Player1BaseId];
            BuildingState enemyBase = null;
            foreach (BuildingState building in state.Buildings.Values)
                if (building.Type == BuildingType.Base && building.Owner == PlayerId.Player2) { enemyBase = building; break; }
            Assert.NotNull(enemyBase);
            Assert.IsTrue(state.FogOfWar.IsVisible(PlayerId.Player1, ownBase.Position));
            Assert.IsFalse(state.FogOfWar.IsExplored(PlayerId.Player1, enemyBase.Position));

            var scout = new RobotState { Id = new EntityId(900), Owner = PlayerId.Player1, CurrentHealth = 100, Position = enemyBase.Position };
            state.Robots[900] = scout;
            simulation.Vision.Update(state);
            Assert.IsTrue(state.FogOfWar.IsVisible(PlayerId.Player1, enemyBase.Position));
            state.Robots.Remove(900);
            simulation.Vision.Update(state);
            Assert.IsFalse(state.FogOfWar.IsVisible(PlayerId.Player1, enemyBase.Position));
            Assert.IsTrue(state.FogOfWar.IsExplored(PlayerId.Player1, enemyBase.Position));
        }

        [Test]
        public void FogOfWarDisabled_KeepsEverythingVisibleAndSurvivesRestore()
        {
            MapDefinition map = DefaultMapFactory.CreatePrototypeMap();
            var catalog = DefaultContentCatalog.CreateDefaultModules();
            var simulation = new MatchSimulation(catalog, map, fogOfWar: false);
            for (int tick = 0; tick < 5; tick++) simulation.Step(MatchSimulation.FixedDeltaSeconds);

            Assert.IsTrue(simulation.State.FogOfWarDisabled);
            Assert.IsNull(simulation.State.FogOfWar, "Без тумана проверки обзора должны видеть всё.");

            var restored = new MatchSimulation(catalog, map, simulation.State);
            Assert.IsNull(restored.State.FogOfWar, "Восстановленный матч берёт настройку из состояния, а не из параметра.");
        }

        [Test]
        public void SetFogOfWar_TogglesMidMatchAndReplaysTheSame()
        {
            MapDefinition map = DefaultMapFactory.CreatePrototypeMap();
            var catalog = DefaultContentCatalog.CreateDefaultModules();
            var live = new MatchSimulation(catalog, map);
            live.AddAgent(new AiCommander(live.Dispatcher, catalog));
            SimVector2 enemyBase = live.State.Buildings[2].Position;
            for (int tick = 0; tick < 30; tick++) live.Step(MatchSimulation.FixedDeltaSeconds);

            Assert.IsTrue(live.Issue(new SetFogOfWarCommand { PlayerId = PlayerId.Player1, Enabled = false }).IsSuccess);
            Assert.IsNull(live.State.FogOfWar, "Выключение сразу открывает всю карту.");
            Assert.IsFalse(live.Issue(new SetFogOfWarCommand { PlayerId = PlayerId.Player1, Enabled = false }).IsSuccess, "Повторное выключение отклоняется.");
            for (int tick = 0; tick < 30; tick++) live.Step(MatchSimulation.FixedDeltaSeconds);
            Assert.IsNull(live.State.FogOfWar, "Без тумана обзор не пересчитывается.");

            Assert.IsTrue(live.Issue(new SetFogOfWarCommand { PlayerId = PlayerId.Player1, Enabled = true }).IsSuccess);
            Assert.IsNotNull(live.State.FogOfWar, "Включённый туман строится в том же тике.");
            Assert.IsFalse(live.State.FogOfWar.IsVisible(PlayerId.Player1, enemyBase));
            for (int tick = 0; tick < 30; tick++) live.Step(MatchSimulation.FixedDeltaSeconds);

            MatchState replayState = MatchSimulation.CreateInitialState(map);
            var runner = new HeadlessMatchRunner(catalog, map, new AiCommander(new CommandDispatcher(catalog), catalog));
            runner.RunTicks(replayState, live.CommandLog, live.State.Tick);
            Assert.IsFalse(replayState.FogOfWarDisabled);
            CollectionAssert.AreEqual(live.State.FogOfWar.Player1Cells, replayState.FogOfWar.Player1Cells);
            CollectionAssert.AreEqual(live.State.FogOfWar.Player2Cells, replayState.FogOfWar.Player2Cells);
            Assert.AreEqual(live.State.Robots.Count, replayState.Robots.Count);
        }

        [Test]
        public void FogOfWar_MountainBlocksSightBehindIt()
        {
            var map = new MapDefinition { Width = 5, Height = 3, TileSize = 1f, Tiles = new TileType[15] };
            map.SetTile(2, 1, TileType.Mountain);
            map.Buildings.Add(new BuildingSpawnDefinition { EntityIdValue = 1, Type = BuildingType.Base, InitialOwner = PlayerId.Player1, Position = new SimVector2(0.5f, 1.5f) });
            var simulation = new MatchSimulation(DefaultContentCatalog.CreateDefaultModules(), map);
            FogOfWarState fog = simulation.State.FogOfWar;

            Assert.AreEqual(2, fog.GetCell(PlayerId.Player1, 2, 1), "Сама гора должна быть видна.");
            Assert.AreEqual(0, fog.GetCell(PlayerId.Player1, 4, 1), "За горой обзор закрыт.");
        }

        [Test]
        public void FogOfWar_FastVisibilityMatchesDirectRayChecksWithOverlappingScouts()
        {
            var map = new MapDefinition { Width = 13, Height = 11, TileSize = 1f, Tiles = new TileType[143] };
            foreach (GridCoord mountain in new[] { new GridCoord(6, 4), new GridCoord(6, 5), new GridCoord(6, 6), new GridCoord(2, 2), new GridCoord(10, 8) })
                map.SetTile(mountain.X, mountain.Y, TileType.Mountain);
            var first = new SimVector2(3.5f, 5.5f);
            var second = new SimVector2(9.5f, 5.5f);
            var simulation = new MatchSimulation(DefaultContentCatalog.CreateDefaultModules(), map);
            simulation.State.Robots[900] = new RobotState { Id = new EntityId(900), Owner = PlayerId.Player1, CurrentHealth = 100, Position = first };
            simulation.State.Robots[901] = new RobotState { Id = new EntityId(901), Owner = PlayerId.Player1, CurrentHealth = 100, Position = second };
            simulation.Vision.Update(simulation.State);

            for (int y = 0; y < map.Height; y++)
                for (int x = 0; x < map.Width; x++)
                {
                    bool expected = ReferenceSees(map, first, x, y) || ReferenceSees(map, second, x, y);
                    Assert.AreEqual(expected, simulation.State.FogOfWar.GetCell(PlayerId.Player1, x, y) == 2, $"Клетка {x},{y}");
                }
        }

        private static bool ReferenceSees(MapDefinition map, SimVector2 source, int targetX, int targetY)
        {
            SimVector2 center = new GridCoord(targetX, targetY).ToSimVector2(map.TileSize);
            if (SimVector2.SqrDistance(source, center) > FogOfWarSystem.RobotRadius * FogOfWarSystem.RobotRadius) return false;
            int x = (int)Math.Floor(source.X / map.TileSize), y = (int)Math.Floor(source.Y / map.TileSize);
            int dx = Math.Abs(targetX - x), dy = Math.Abs(targetY - y);
            int stepX = x < targetX ? 1 : -1, stepY = y < targetY ? 1 : -1;
            int error = dx - dy;
            while (x != targetX || y != targetY)
            {
                int twice = error * 2;
                if (twice > -dy) { error -= dy; x += stepX; }
                if (twice < dx) { error += dx; y += stepY; }
                if ((x != targetX || y != targetY) && map.GetTile(x, y) == TileType.Mountain) return false;
            }
            return true;
        }

        [Test]
        public void FogOfWar_LastSeenEnemyStaysUntilOldPositionIsChecked()
        {
            var map = new MapDefinition { Width = 40, Height = 3, TileSize = 1f, Tiles = new TileType[120] };
            var simulation = new MatchSimulation(DefaultContentCatalog.CreateDefaultModules(), map);
            MatchState state = simulation.State;
            var scout = new RobotState { Id = new EntityId(900), Owner = PlayerId.Player1, CurrentHealth = 100, Position = new SimVector2(10.5f, 1.5f) };
            var enemy = new RobotState { Id = new EntityId(901), Owner = PlayerId.Player2, CurrentHealth = 100, Position = new SimVector2(12.5f, 1.5f) };
            state.Robots[900] = scout;
            state.Robots[901] = enemy;
            simulation.Vision.Update(state);
            Assert.AreEqual(enemy.Position, state.FogOfWar.Player1LastSeenEnemies[0].Position);

            scout.Position = new SimVector2(0.5f, 1.5f);
            enemy.Position = new SimVector2(30.5f, 1.5f);
            simulation.Vision.Update(state);
            Assert.AreEqual(1, state.FogOfWar.Player1LastSeenEnemies.Count);
            Assert.AreEqual(new SimVector2(12.5f, 1.5f), state.FogOfWar.Player1LastSeenEnemies[0].Position);

            scout.Position = new SimVector2(12.5f, 1.5f);
            simulation.Vision.Update(state);
            Assert.IsEmpty(state.FogOfWar.Player1LastSeenEnemies, "Ложную отметку убираем после повторной разведки места.");

            scout.Position = new SimVector2(31.5f, 1.5f);
            state.Tick = 5;
            simulation.Vision.Update(state);
            Assert.AreEqual(enemy.Position, state.FogOfWar.Player1LastSeenEnemies[0].Position);
            Assert.AreEqual(5, state.FogOfWar.Player1LastSeenEnemies[0].SeenTick);
        }

        [Test]
        public void Hunt_ExploresWhenEnemyHiddenThenPursuesVisibleTarget()
        {
            var map = new MapDefinition { Width = 30, Height = 5, TileSize = 1f, Tiles = new TileType[150] };
            var simulation = new MatchSimulation(DefaultContentCatalog.CreateDefaultModules(), map);
            MatchState state = simulation.State;
            var hunter = new RobotState { Id = new EntityId(900), Owner = PlayerId.Player1, Blueprint = RobotPresetCatalog.Scout.CreateBlueprint(), CurrentHealth = 100, MaxHealth = 100, Position = new SimVector2(4.5f, 2.5f), MoveSpeed = 2f, CurrentOrder = RobotOrder.Hunt() };
            var enemy = new RobotState { Id = new EntityId(901), Owner = PlayerId.Player2, CurrentHealth = 100, MaxHealth = 100, Position = new SimVector2(24.5f, 2.5f) };
            state.Robots[900] = hunter;
            state.Robots[901] = enemy;
            simulation.Vision.Update(state);

            simulation.Planner.Update(state);
            Assert.IsFalse(hunter.CurrentOrder.TargetEntityId.IsValid);
            Assert.IsTrue(hunter.HasPath, "Охота должна искать противника, а не ждать неподвижно.");

            hunter.Position = new SimVector2(20.5f, 2.5f);
            simulation.Vision.Update(state);
            simulation.Planner.Update(state);
            Assert.AreEqual(enemy.Id, hunter.CurrentOrder.TargetEntityId);
        }

        [Test]
        public void AttackCommand_RejectsHiddenEnemyUntilScoutRevealsIt()
        {
            MapDefinition map = DefaultMapFactory.CreatePrototypeMap();
            var simulation = new MatchSimulation(DefaultContentCatalog.CreateDefaultModules(), map);
            BuildingState ownBase = simulation.State.Buildings[Player1BaseId];
            BuildingState enemyBase = null;
            foreach (BuildingState building in simulation.State.Buildings.Values)
                if (building.Type == BuildingType.Base && building.Owner == PlayerId.Player2) { enemyBase = building; break; }
            Assert.NotNull(enemyBase);
            var attacker = new RobotState { Id = new EntityId(900), Owner = PlayerId.Player1, CurrentHealth = 100, Position = ownBase.Position };
            var enemy = new RobotState { Id = new EntityId(901), Owner = PlayerId.Player2, CurrentHealth = 100, Position = enemyBase.Position };
            simulation.State.Robots[900] = attacker;
            simulation.State.Robots[901] = enemy;
            var command = new OrderAttackCommand { PlayerId = PlayerId.Player1, RobotIds = new List<EntityId> { attacker.Id }, TargetEntityId = enemy.Id };
            Assert.IsFalse(simulation.Issue(command).IsSuccess);

            attacker.Position = new SimVector2(enemy.Position.X + 2f, enemy.Position.Y);
            simulation.Vision.Update(simulation.State);
            Assert.IsTrue(simulation.Issue(command).IsSuccess);
        }

        [Test]
        public void Program_ExploresUntilForeignBuildingIsDiscovered()
        {
            MapDefinition map = DefaultMapFactory.CreatePrototypeMap();
            var simulation = new MatchSimulation(DefaultContentCatalog.CreateDefaultModules(), map);
            MatchState state = simulation.State;
            BuildingState ownBase = state.Buildings[Player1BaseId];
            BuildingState unknown = null;
            foreach (BuildingState building in state.Buildings.Values)
                if (building.Owner != PlayerId.Player1 && !state.FogOfWar.IsExplored(PlayerId.Player1, building.Position)) { unknown = building; break; }
            Assert.NotNull(unknown);
            var scout = new RobotState { Id = new EntityId(900), Owner = PlayerId.Player1, Blueprint = RobotPresetCatalog.Scout.CreateBlueprint(), CurrentHealth = 100, Position = ownBase.SpawnPoint, MoveSpeed = 2f };
            state.Robots[900] = scout;
            Assert.IsTrue(simulation.Issue(new ConfigureProgramCommand { PlayerId = PlayerId.Player1, RobotIds = new List<EntityId> { scout.Id }, CapturePriority = 1, AttackPriority = 2, DefendPriority = 3 }).IsSuccess);
            simulation.Planner.Update(state);
            Assert.AreEqual(ProgramAction.Explore, scout.ActiveProgramAction);
            Assert.IsTrue(scout.HasPath, "Робот должен искать ещё неизвестную проходимую клетку.");

            scout.Position = new SimVector2(unknown.Position.X + 2f, unknown.Position.Y);
            simulation.Vision.Update(state);
            simulation.Planner.Update(state);
            Assert.AreEqual(ProgramAction.Capture, scout.ActiveProgramAction);
            Assert.AreEqual(unknown.Id, scout.CurrentOrder.TargetEntityId);
        }

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
        public void Program_PrioritizesCaptureOrAttack_AndManualOrderOverridesIt()
        {
            var catalog = DefaultContentCatalog.CreateDefaultModules();
            var map = DefaultMapFactory.CreatePrototypeMap();
            MatchState state = MatchSimulation.CreateInitialState(map);
            BuildingState factory = state.Buildings[CentralFactoryId];
            var robot = new RobotState { Id = new EntityId(900), Owner = PlayerId.Player1, Blueprint = RobotPresetCatalog.Scout.CreateBlueprint(), CurrentHealth = 100, MaxHealth = 100, Position = new SimVector2(factory.Position.X + 2f, factory.Position.Y), MoveSpeed = 2f };
            var enemy = new RobotState { Id = new EntityId(901), Owner = PlayerId.Player2, Blueprint = RobotPresetCatalog.Scout.CreateBlueprint(), CurrentHealth = 100, MaxHealth = 100, Position = new SimVector2(robot.Position.X + 4f, robot.Position.Y), MoveSpeed = 2f };
            state.Robots[900] = robot;
            state.Robots[901] = enemy;
            var simulation = new MatchSimulation(catalog, map, state);
            var ids = new List<EntityId> { robot.Id };

            Assert.IsTrue(simulation.Issue(new ConfigureProgramCommand { PlayerId = PlayerId.Player1, RobotIds = ids, CapturePriority = 1, AttackPriority = 2, DefendPriority = 3 }).IsSuccess);
            simulation.Planner.Update(state);
            Assert.AreEqual(ProgramAction.Capture, robot.ActiveProgramAction);
            Assert.AreEqual(factory.Id, robot.CurrentOrder.TargetEntityId);
            simulation.Capture.Update(state, 1f);
            Assert.Greater(factory.CaptureProgressSeconds, 0f);

            Assert.IsTrue(simulation.Issue(new ConfigureProgramCommand { PlayerId = PlayerId.Player1, RobotIds = ids, AttackPriority = 1, CapturePriority = 2, DefendPriority = 3 }).IsSuccess);
            simulation.Planner.Update(state);
            Assert.AreEqual(ProgramAction.Attack, robot.ActiveProgramAction);
            Assert.AreEqual(enemy.Id, robot.CurrentOrder.TargetEntityId);

            enemy.Position = new SimVector2(state.Buildings[Player1BaseId].Position.X + 2f, state.Buildings[Player1BaseId].Position.Y);
            Assert.IsTrue(simulation.Issue(new ConfigureProgramCommand { PlayerId = PlayerId.Player1, RobotIds = ids, DefendPriority = 1, CapturePriority = 2, AttackPriority = 3 }).IsSuccess);
            simulation.Planner.Update(state);
            Assert.AreEqual(ProgramAction.Defend, robot.ActiveProgramAction);
            Assert.AreEqual(enemy.Id, robot.CurrentOrder.TargetEntityId);

            Assert.IsTrue(simulation.Issue(new OrderStopCommand { PlayerId = PlayerId.Player1, RobotIds = ids }).IsSuccess);
            Assert.AreEqual(OrderType.Stop, robot.CurrentOrder.Type);
        }

        [Test]
        public void Program_RejectsDuplicateRanksWithoutMutatingRobot()
        {
            var state = new MatchState();
            var robot = new RobotState { Id = new EntityId(1), Owner = PlayerId.Player1, CurrentHealth = 10 };
            state.Robots[1] = robot;
            var dispatcher = new CommandDispatcher(DefaultContentCatalog.CreateDefaultModules());

            CommandResult result = dispatcher.Dispatch(new ConfigureProgramCommand { PlayerId = PlayerId.Player1, RobotIds = new List<EntityId> { robot.Id }, DefendPriority = 1, AttackPriority = 1, CapturePriority = 3 }, state);

            Assert.IsFalse(result.IsSuccess);
            Assert.AreEqual(OrderType.None, robot.CurrentOrder.Type);
            Assert.IsFalse(dispatcher.Dispatch(new ConfigureProgramCommand { PlayerId = PlayerId.Player1, RobotIds = new List<EntityId> { robot.Id }, AttackRadius = 0f }, state).IsSuccess);
            Assert.IsFalse(dispatcher.Dispatch(new ConfigureProgramCommand { PlayerId = PlayerId.Player1, RobotIds = new List<EntityId> { robot.Id }, AttackMinHealthPercent = 101 }, state).IsSuccess);
            Assert.IsFalse(dispatcher.Dispatch(new ConfigureProgramCommand { PlayerId = PlayerId.Player1, RobotIds = new List<EntityId> { robot.Id }, CaptureResourceType = 4 }, state).IsSuccess);
            Assert.IsFalse(dispatcher.Dispatch(new ConfigureProgramCommand { PlayerId = PlayerId.Player1, RobotIds = new List<EntityId> { robot.Id }, AttackTargetId = robot.Id }, state).IsSuccess);
            Assert.AreEqual(OrderType.None, robot.CurrentOrder.Type);
        }

        [Test]
        public void Program_HealthThresholdSkipsAttackAndReturnsWhenRepaired()
        {
            var map = DefaultMapFactory.CreatePrototypeMap();
            MatchState state = MatchSimulation.CreateInitialState(map);
            BuildingState factory = state.Buildings[CentralFactoryId];
            var robot = new RobotState { Id = new EntityId(900), Owner = PlayerId.Player1, Blueprint = RobotPresetCatalog.Scout.CreateBlueprint(), MaxHealth = 100, CurrentHealth = 50, Position = new SimVector2(factory.Position.X + 2f, factory.Position.Y), MoveSpeed = 2f };
            var enemy = new RobotState { Id = new EntityId(901), Owner = PlayerId.Player2, MaxHealth = 100, CurrentHealth = 100, Position = new SimVector2(robot.Position.X + 3f, robot.Position.Y) };
            state.Robots[900] = robot;
            state.Robots[901] = enemy;
            var simulation = new MatchSimulation(DefaultContentCatalog.CreateDefaultModules(), map, state);
            Assert.IsTrue(simulation.Issue(new ConfigureProgramCommand { PlayerId = PlayerId.Player1, RobotIds = new List<EntityId> { robot.Id }, AttackPriority = 1, CapturePriority = 2, DefendPriority = 3, AttackMinHealthPercent = 75 }).IsSuccess);

            simulation.Planner.Update(state);
            Assert.AreEqual(ProgramAction.Capture, robot.ActiveProgramAction);
            Assert.IsTrue(simulation.Issue(new ConfigureProgramCommand { PlayerId = PlayerId.Player1, RobotIds = new List<EntityId> { robot.Id }, AttackPriority = 1, CapturePriority = 2, DefendPriority = 3, AttackMinHealthPercent = 75, CaptureMinHealthPercent = 75, DefendMinHealthPercent = 75 }).IsSuccess);
            simulation.Planner.Update(state);
            Assert.AreEqual(ProgramAction.None, robot.ActiveProgramAction, "Недостаток HP не должен отправлять робота на разведку.");
            robot.CurrentHealth = 75;
            simulation.Planner.Update(state);
            Assert.AreEqual(ProgramAction.Attack, robot.ActiveProgramAction);
            Assert.AreEqual(enemy.Id, robot.CurrentOrder.TargetEntityId);
        }

        [Test]
        public void Program_HealthThresholdsSurviveReplayRecord()
        {
            var command = new ConfigureProgramCommand
            {
                PlayerId = PlayerId.Player1, RobotIds = new List<EntityId> { new EntityId(900) },
                DefendMinHealthPercent = 25, AttackMinHealthPercent = 75, CaptureMinHealthPercent = 50, RetreatHealthPercent = 40,
                CaptureResourceType = (int)ResourceType.Missiles, CaptureResourceBelow = 5
            };
            var restored = (ConfigureProgramCommand)ReplayCommandRecord.FromCommand(12, command).ToCommand();
            Assert.AreEqual(25, restored.DefendMinHealthPercent);
            Assert.AreEqual(75, restored.AttackMinHealthPercent);
            Assert.AreEqual(50, restored.CaptureMinHealthPercent);
            Assert.AreEqual(40, restored.RetreatHealthPercent);
            Assert.AreEqual((int)ResourceType.Missiles, restored.CaptureResourceType);
            Assert.AreEqual(5, restored.CaptureResourceBelow);
        }

        [Test]
        public void Program_CapturesFactoryOnlyWhileItsResourceIsScarce()
        {
            var map = DefaultMapFactory.CreatePrototypeMap();
            MatchState state = MatchSimulation.CreateInitialState(map);
            BuildingState factory = state.Buildings[CentralFactoryId];
            factory.ProducedResource = ResourceType.Missiles;
            var robot = new RobotState { Id = new EntityId(900), Owner = PlayerId.Player1,
                Blueprint = RobotPresetCatalog.Scout.CreateBlueprint(), MaxHealth = 100, CurrentHealth = 100,
                Position = new SimVector2(factory.Position.X + 2f, factory.Position.Y), MoveSpeed = 2f };
            state.Robots[900] = robot;
            var simulation = new MatchSimulation(DefaultContentCatalog.CreateDefaultModules(), map, state);
            Assert.IsTrue(simulation.Issue(new ConfigureProgramCommand { PlayerId = PlayerId.Player1,
                RobotIds = new List<EntityId> { robot.Id }, CapturePriority = 1, AttackPriority = 2, DefendPriority = 3,
                CaptureTargetId = factory.Id, CaptureResourceType = (int)ResourceType.Missiles, CaptureResourceBelow = 5 }).IsSuccess);

            state.GetPlayer(PlayerId.Player1).Resources[ResourceType.Missiles] = 4;
            simulation.Planner.Update(state);
            Assert.AreEqual(ProgramAction.Capture, robot.ActiveProgramAction);
            Assert.AreEqual(factory.Id, robot.CurrentOrder.TargetEntityId);

            state.GetPlayer(PlayerId.Player1).Resources[ResourceType.Missiles] = 5;
            state.Tick += 8;
            simulation.Planner.Update(state);
            Assert.AreNotEqual(ProgramAction.Capture, robot.ActiveProgramAction);
        }

        [Test]
        public void Program_RetreatPreemptsRulesAndEndsAboveThreshold()
        {
            var map = DefaultMapFactory.CreatePrototypeMap();
            MatchState state = MatchSimulation.CreateInitialState(map);
            BuildingState home = state.Buildings[Player1BaseId];
            var robot = new RobotState { Id = new EntityId(900), Owner = PlayerId.Player1,
                Blueprint = RobotPresetCatalog.Scout.CreateBlueprint(), MaxHealth = 100, CurrentHealth = 30,
                Position = new SimVector2(home.Position.X + 5f, home.Position.Y), MoveSpeed = 2f };
            state.Robots[900] = robot;
            var simulation = new MatchSimulation(DefaultContentCatalog.CreateDefaultModules(), map, state);
            var ids = new List<EntityId> { robot.Id };
            Assert.IsFalse(simulation.Issue(new ConfigureProgramCommand { PlayerId = PlayerId.Player1, RobotIds = ids, RetreatHealthPercent = 101 }).IsSuccess);
            Assert.IsTrue(simulation.Issue(new ConfigureProgramCommand { PlayerId = PlayerId.Player1, RobotIds = ids, RetreatHealthPercent = 40 }).IsSuccess);

            simulation.Planner.Update(state);
            Assert.AreEqual(ProgramAction.Retreat, robot.ActiveProgramAction);
            Assert.AreEqual(home.Id, robot.CurrentOrder.TargetEntityId);
            Assert.AreEqual(home.SpawnPoint, robot.CurrentOrder.TargetPosition);

            robot.CurrentHealth = 60;
            state.Tick += 8;
            simulation.Planner.Update(state);
            Assert.AreNotEqual(ProgramAction.Retreat, robot.ActiveProgramAction);
        }

        [Test]
        public void Program_UsesConfiguredRadiusAndSpecificTargets()
        {
            var map = DefaultMapFactory.CreatePrototypeMap();
            MatchState state = MatchSimulation.CreateInitialState(map);
            BuildingState factory = state.Buildings[CentralFactoryId];
            var robot = new RobotState { Id = new EntityId(900), Owner = PlayerId.Player1, Blueprint = RobotPresetCatalog.Scout.CreateBlueprint(), CurrentHealth = 100, Position = new SimVector2(factory.Position.X + 2f, factory.Position.Y), MoveSpeed = 2f };
            var enemy = new RobotState { Id = new EntityId(901), Owner = PlayerId.Player2, CurrentHealth = 100, Position = new SimVector2(robot.Position.X + 4f, robot.Position.Y) };
            state.Robots[900] = robot;
            state.Robots[901] = enemy;
            var simulation = new MatchSimulation(DefaultContentCatalog.CreateDefaultModules(), map, state);
            var ids = new List<EntityId> { robot.Id };
            BuildingState otherFactory = null;
            foreach (BuildingState building in state.Buildings.Values)
                if (building.Id != factory.Id && building.Owner != PlayerId.Player1 && SimVector2.Distance(robot.Position, building.Position) > 3f) { otherFactory = building; break; }
            Assert.NotNull(otherFactory);
            GridCoord knownCell = GridCoord.FromSimVector2(otherFactory.Position, map.TileSize);
            state.FogOfWar.Player1Cells[knownCell.Y * map.Width + knownCell.X] = 1;

            Assert.IsTrue(simulation.Issue(new ConfigureProgramCommand { PlayerId = PlayerId.Player1, RobotIds = ids, CapturePriority = 1, AttackPriority = 2, DefendPriority = 3, CaptureRadius = 1f, AttackRadius = 1f, DefendRadius = 1f, CaptureTargetId = factory.Id }).IsSuccess);
            simulation.Planner.Update(state);
            Assert.AreEqual(ProgramAction.Explore, robot.ActiveProgramAction);

            Assert.IsTrue(simulation.Issue(new ConfigureProgramCommand { PlayerId = PlayerId.Player1, RobotIds = ids, CapturePriority = 1, AttackPriority = 2, DefendPriority = 3, CaptureRadius = 3f, AttackRadius = 1f, DefendRadius = 1f, CaptureTargetId = factory.Id }).IsSuccess);
            simulation.Planner.Update(state);
            Assert.AreEqual(ProgramAction.Capture, robot.ActiveProgramAction);
            Assert.AreEqual(factory.Id, robot.CurrentOrder.TargetEntityId);

            Assert.IsTrue(simulation.Issue(new ConfigureProgramCommand { PlayerId = PlayerId.Player1, RobotIds = ids, CapturePriority = 1, AttackPriority = 2, DefendPriority = 3, CaptureRadius = 3f, AttackRadius = 5f, DefendRadius = 1f, CaptureTargetId = otherFactory.Id, AttackTargetId = enemy.Id }).IsSuccess);
            simulation.Planner.Update(state);
            Assert.AreEqual(ProgramAction.Attack, robot.ActiveProgramAction);
            Assert.AreEqual(enemy.Id, robot.CurrentOrder.TargetEntityId);
        }

        [Test]
        public void Program_DefendOnlySpecifiedOwnedBuilding()
        {
            var map = DefaultMapFactory.CreatePrototypeMap();
            MatchState state = MatchSimulation.CreateInitialState(map);
            BuildingState baseBuilding = state.Buildings[Player1BaseId];
            BuildingState anotherOwned = state.Buildings[CentralFactoryId];
            anotherOwned.Owner = PlayerId.Player1;
            var robot = new RobotState { Id = new EntityId(900), Owner = PlayerId.Player1, Blueprint = RobotPresetCatalog.Scout.CreateBlueprint(), CurrentHealth = 100, Position = baseBuilding.Position, MoveSpeed = 2f };
            var enemy = new RobotState { Id = new EntityId(901), Owner = PlayerId.Player2, CurrentHealth = 100, Position = new SimVector2(baseBuilding.Position.X + 3f, baseBuilding.Position.Y) };
            state.Robots[900] = robot;
            state.Robots[901] = enemy;
            var simulation = new MatchSimulation(DefaultContentCatalog.CreateDefaultModules(), map, state);
            var ids = new List<EntityId> { robot.Id };

            Assert.IsTrue(simulation.Issue(new ConfigureProgramCommand { PlayerId = PlayerId.Player1, RobotIds = ids, DefendRadius = 5f, AttackRadius = 1f, CaptureRadius = 1f, DefendTargetId = anotherOwned.Id }).IsSuccess);
            simulation.Planner.Update(state);
            Assert.AreEqual(ProgramAction.Explore, robot.ActiveProgramAction);

            Assert.IsTrue(simulation.Issue(new ConfigureProgramCommand { PlayerId = PlayerId.Player1, RobotIds = ids, DefendRadius = 5f, AttackRadius = 1f, CaptureRadius = 1f, DefendTargetId = baseBuilding.Id }).IsSuccess);
            simulation.Planner.Update(state);
            Assert.AreEqual(ProgramAction.Defend, robot.ActiveProgramAction);
            Assert.AreEqual(enemy.Id, robot.CurrentOrder.TargetEntityId);
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
                    captureIssued = live.Issue(new OrderCaptureCommand { PlayerId = PlayerId.Player1, RobotIds = new List<EntityId> { scout.Id }, TargetBuildingId = EntityId.None }).IsSuccess;
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
