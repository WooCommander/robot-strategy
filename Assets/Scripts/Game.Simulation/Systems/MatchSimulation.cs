using System;
using System.Collections.Generic;
using Game.Content.Definitions;
using Game.Core.Commands;
using Game.Core.Models;
using Game.Core.Types;
using Game.Simulation.Navigation;

namespace Game.Simulation.Systems
{
    /// <summary>Участник матча, который думает на каждом тике и отдаёт команды через диспетчер (например, ИИ).</summary>
    public interface IMatchAgent
    {
        void Think(MatchState state);
    }

    /// <summary>
    /// Единый цикл симуляции с фиксированным шагом. Сцена Unity и headless-прогон вызывают один и тот же
    /// <see cref="Step"/>, поэтому журнал команд воспроизводит матч независимо от FPS.
    /// </summary>
    public sealed class MatchSimulation
    {
        public const int TicksPerSecond = 20;
        public const float FixedDeltaSeconds = 1f / TicksPerSecond;
        private const int MaxStepsPerFrame = 5;

        private readonly List<IMatchAgent> _agents = new List<IMatchAgent>();
        private float _accumulator;

        public MatchState State { get; }
        public MapDefinition Map { get; }
        public NavigationGrid NavigationGrid { get; }
        public AStarPathfinder Pathfinder { get; }
        public CommandDispatcher Dispatcher { get; }
        public ProductionSystem Production { get; }
        public OrderPlanner Planner { get; }
        public MovementSystem Movement { get; }
        public CombatSystem Combat { get; }
        public CaptureSystem Capture { get; }
        public ReplayCommandLog CommandLog { get; } = new ReplayCommandLog();

        /// <summary>Доля следующего тика, уже накопленная кадрами; используется для интерполяции визуала.</summary>
        public float InterpolationAlpha => _accumulator / FixedDeltaSeconds;

        /// <summary>Вызывается после каждого тика, чтобы визуал зафиксировал новую позицию для интерполяции.</summary>
        public event Action StepCompleted;

        /// <param name="map">Карта; без неё движение и планирование приказов отключены (боевые unit-тесты).</param>
        public MatchSimulation(IReadOnlyDictionary<string, ModuleDefinition> catalog, MapDefinition map, MatchState state = null)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            Map = map;
            State = state ?? (map != null ? CreateInitialState(map) : new MatchState());
            Dispatcher = new CommandDispatcher(catalog);
            Production = new ProductionSystem(catalog);
            Combat = new CombatSystem(catalog);
            Capture = new CaptureSystem();
            if (map != null)
            {
                NavigationGrid = BuildingFootprint.CreateMatchGrid(map);
                Pathfinder = new AStarPathfinder(NavigationGrid);
                Movement = new MovementSystem(NavigationGrid);
                Planner = new OrderPlanner(catalog, map, NavigationGrid, Pathfinder, Movement);
            }
        }

        public static MatchState CreateInitialState(MapDefinition map)
        {
            var state = new MatchState();
            state.Player1.Resources[ResourceType.General] = EconomyRules.StartingGeneral;
            state.Player2.Resources[ResourceType.General] = EconomyRules.StartingGeneral;
            foreach (BuildingSpawnDefinition definition in map.Buildings)
            {
                state.Buildings[definition.EntityIdValue] = new BuildingState
                {
                    Id = new EntityId(definition.EntityIdValue),
                    Type = definition.Type,
                    Owner = definition.InitialOwner,
                    Position = definition.Position,
                    SpawnPoint = definition.SpawnPoint,
                    CaptureRadius = definition.CaptureRadius,
                    ProducedResource = definition.Type == BuildingType.Base ? ResourceType.General : definition.ProducedResource
                };
            }
            return state;
        }

        public void AddAgent(IMatchAgent agent)
        {
            if (agent == null) throw new ArgumentNullException(nameof(agent));
            _agents.Add(agent);
        }

        /// <summary>Команда игрока: применяется к текущему тику и попадает в журнал replay.</summary>
        public CommandResult Issue(ICommand command)
        {
            CommandResult result = Dispatcher.Dispatch(command, State);
            if (result.IsSuccess) CommandLog.Record(State.Tick, command);
            return result;
        }

        /// <summary>Накопить время кадра и выполнить нужное число фиксированных тиков.</summary>
        public int AdvanceFrame(float frameDeltaSeconds)
        {
            if (!IsRunning)
            {
                _accumulator = 0f;
                return 0;
            }

            _accumulator += Math.Min(Math.Max(0f, frameDeltaSeconds), MaxStepsPerFrame * FixedDeltaSeconds);
            int steps = 0;
            while (_accumulator >= FixedDeltaSeconds && IsRunning)
            {
                Step(FixedDeltaSeconds);
                _accumulator -= FixedDeltaSeconds;
                steps++;
            }
            return steps;
        }

        /// <summary>Один тик симуляции. При replay сначала применяются записанные команды этого тика.</summary>
        public void Step(float deltaSeconds, ReplayCommandLog replay = null)
        {
            if (deltaSeconds <= 0f) throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
            if (!IsRunning) return;

            if (replay != null)
            {
                foreach (ReplayCommandRecord record in replay.GetForTick(State.Tick))
                {
                    CommandResult result = Dispatcher.Dispatch(record.ToCommand(), State);
                    if (!result.IsSuccess) throw new InvalidOperationException($"Replay-команда на тике {State.Tick} отклонена: {result.ErrorReason}");
                }
            }

            foreach (IMatchAgent agent in _agents) agent.Think(State);
            Production.Update(State, deltaSeconds);
            Planner?.Update(State);
            Movement?.Update(State, deltaSeconds);
            Combat.Update(State, deltaSeconds);
            Capture.Update(State, deltaSeconds);

            State.MatchTimeSeconds += deltaSeconds;
            State.Tick++;
            StepCompleted?.Invoke();
        }

        private bool IsRunning => !State.IsPaused && State.Result == MatchResult.InProgress;
    }
}
