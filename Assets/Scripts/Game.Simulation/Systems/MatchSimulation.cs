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
        public MatchNavigation Navigation { get; }
        public CommandDispatcher Dispatcher { get; }
        public ProductionSystem Production { get; }
        public OrderPlanner Planner { get; }
        public MovementSystem Movement { get; }
        public CombatSystem Combat { get; }
        public CaptureSystem Capture { get; }
        public FogOfWarSystem Vision { get; }
        public ReplayCommandLog CommandLog { get; } = new ReplayCommandLog();

        /// <summary>Доля следующего тика, уже накопленная кадрами; используется для интерполяции визуала.</summary>
        public float InterpolationAlpha => _accumulator / FixedDeltaSeconds;

        /// <summary>Вызывается после каждого тика, чтобы визуал зафиксировал новую позицию для интерполяции.</summary>
        public event Action StepCompleted;

        /// <param name="map">Карта; без неё движение и планирование приказов отключены (боевые unit-тесты).</param>
        /// <param name="fogOfWar">Туман войны для нового матча; восстановленный state хранит эту настройку сам.</param>
        public MatchSimulation(IReadOnlyDictionary<string, ModuleDefinition> catalog, MapDefinition map, MatchState state = null, bool fogOfWar = true)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            Map = map;
            State = state ?? (map != null ? CreateInitialState(map) : new MatchState());
            if (state == null) State.FogOfWarDisabled = !fogOfWar;
            Dispatcher = new CommandDispatcher(catalog);
            Production = new ProductionSystem(catalog);
            Combat = new CombatSystem(catalog);
            Capture = new CaptureSystem();
            if (map != null)
            {
                Navigation = new MatchNavigation(map);
                Movement = new MovementSystem(Navigation);
                Planner = new OrderPlanner(catalog, map, Navigation, Movement);
                if (State.FogOfWarDisabled)
                {
                    // Все проверки обзора трактуют отсутствие FogOfWar как «видно всё».
                    State.FogOfWar = null;
                }
                else
                {
                    Vision = new FogOfWarSystem(map);
                    Vision.Update(State);
                }
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
            Vision?.Update(State);
            Combat.Update(State, deltaSeconds);
            Capture.Update(State, deltaSeconds);

            State.MatchTimeSeconds += deltaSeconds;
            State.Tick++;
            StepCompleted?.Invoke();
        }

        private bool IsRunning => !State.IsPaused && State.Result == MatchResult.InProgress;
    }

    /// <summary>Детерминированная видимость обеих сторон; горы перекрывают обзор за собой.</summary>
    public sealed class FogOfWarSystem
    {
        public const float RobotRadius = 7f;
        public const float ElectronicsBonus = 3f;
        public const float BaseRadius = 10f;
        public const float FactoryRadius = 6f;

        private readonly MapDefinition _map;
        private readonly int[] _mountainPrefix;

        public FogOfWarSystem(MapDefinition map)
        {
            _map = map ?? throw new ArgumentNullException(nameof(map));
            int stride = map.Width + 1;
            _mountainPrefix = new int[stride * (map.Height + 1)];
            for (int y = 1; y <= map.Height; y++)
            {
                int rowMountains = 0;
                for (int x = 1; x <= map.Width; x++)
                {
                    if (map.GetTile(x - 1, y - 1) == TileType.Mountain) rowMountains++;
                    _mountainPrefix[y * stride + x] = _mountainPrefix[(y - 1) * stride + x] + rowMountains;
                }
            }
        }

        public void Update(MatchState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            FogOfWarState fog = state.FogOfWar;
            int count = _map.Width * _map.Height;
            if (fog == null || fog.Width != _map.Width || fog.Height != _map.Height || fog.TileSize != _map.TileSize ||
                fog.Player1Cells == null || fog.Player1Cells.Length != count || fog.Player2Cells == null || fog.Player2Cells.Length != count)
                state.FogOfWar = fog = new FogOfWarState(_map.Width, _map.Height, _map.TileSize);
            if (fog.Player1LastSeenEnemies == null) fog.Player1LastSeenEnemies = new List<LastSeenEnemyState>();
            if (fog.Player2LastSeenEnemies == null) fog.Player2LastSeenEnemies = new List<LastSeenEnemyState>();

            DemoteVisible(fog.Player1Cells);
            DemoteVisible(fog.Player2Cells);
            foreach (BuildingState building in state.Buildings.Values)
            {
                if (building.Owner != PlayerId.Player1 && building.Owner != PlayerId.Player2) continue;
                Mark(fog, building.Owner, building.Position, building.Type == BuildingType.Base ? BaseRadius : FactoryRadius);
            }
            foreach (RobotState robot in state.Robots.Values)
            {
                if (!robot.IsAlive || (robot.Owner != PlayerId.Player1 && robot.Owner != PlayerId.Player2)) continue;
                Mark(fog, robot.Owner, robot.Position, RobotRadius + (robot.Blueprint != null && robot.Blueprint.HasElectronics ? ElectronicsBonus : 0f));
            }
            UpdateLastSeenEnemies(state, fog, PlayerId.Player1);
            UpdateLastSeenEnemies(state, fog, PlayerId.Player2);
        }

        private static void UpdateLastSeenEnemies(MatchState state, FogOfWarState fog, PlayerId observer)
        {
            List<LastSeenEnemyState> contacts = fog.GetLastSeenEnemies(observer);
            foreach (RobotState enemy in state.Robots.Values)
            {
                if (!enemy.IsAlive || enemy.Owner == observer ||
                    (enemy.Owner != PlayerId.Player1 && enemy.Owner != PlayerId.Player2) ||
                    !fog.IsVisible(observer, enemy.Position)) continue;
                LastSeenEnemyState contact = null;
                for (int index = 0; index < contacts.Count; index++)
                    if (contacts[index].RobotId == enemy.Id) { contact = contacts[index]; break; }
                if (contact == null)
                {
                    contact = new LastSeenEnemyState { RobotId = enemy.Id };
                    contacts.Add(contact);
                }
                contact.Position = enemy.Position;
                contact.SeenTick = state.Tick;
            }
            for (int index = contacts.Count - 1; index >= 0; index--)
            {
                LastSeenEnemyState contact = contacts[index];
                if (!fog.IsVisible(observer, contact.Position)) continue;
                if (!state.Robots.TryGetValue(contact.RobotId.Value, out RobotState enemy) || !enemy.IsAlive ||
                    !fog.IsVisible(observer, enemy.Position)) contacts.RemoveAt(index);
            }
        }

        private static void DemoteVisible(byte[] cells)
        {
            for (int i = 0; i < cells.Length; i++) if (cells[i] == 2) cells[i] = 1;
        }

        private void Mark(FogOfWarState fog, PlayerId player, SimVector2 source, float radius)
        {
            byte[] cells = player == PlayerId.Player1 ? fog.Player1Cells : fog.Player2Cells;
            int originX = (int)Math.Floor(source.X / _map.TileSize);
            int originY = (int)Math.Floor(source.Y / _map.TileSize);
            int reach = (int)Math.Ceiling(radius / _map.TileSize) + 1;
            float radiusSq = radius * radius;
            for (int y = Math.Max(0, originY - reach); y <= Math.Min(_map.Height - 1, originY + reach); y++)
            {
                for (int x = Math.Max(0, originX - reach); x <= Math.Min(_map.Width - 1, originX + reach); x++)
                {
                    int index = y * _map.Width + x;
                    if (cells[index] == 2) continue;
                    SimVector2 center = new GridCoord(x, y).ToSimVector2(_map.TileSize);
                    if (SimVector2.SqrDistance(source, center) > radiusSq || IsOccluded(originX, originY, x, y)) continue;
                    cells[index] = 2;
                }
            }
        }

        private bool IsOccluded(int fromX, int fromY, int toX, int toY)
        {
            // Карта во время матча неизменна. Если во всём прямоугольнике луча нет скал,
            // точную трассировку выполнять незачем.
            if (fromX >= 0 && fromX < _map.Width && fromY >= 0 && fromY < _map.Height &&
                toX >= 0 && toX < _map.Width && toY >= 0 && toY < _map.Height)
            {
                int left = Math.Min(fromX, toX), right = Math.Max(fromX, toX) + 1;
                int bottom = Math.Min(fromY, toY), top = Math.Max(fromY, toY) + 1;
                int stride = _map.Width + 1;
                int mountains = _mountainPrefix[top * stride + right] - _mountainPrefix[bottom * stride + right]
                    - _mountainPrefix[top * stride + left] + _mountainPrefix[bottom * stride + left];
                if (mountains == 0) return false;
            }
            int x = fromX, y = fromY;
            int dx = Math.Abs(toX - fromX), dy = Math.Abs(toY - fromY);
            int stepX = fromX < toX ? 1 : -1, stepY = fromY < toY ? 1 : -1;
            int error = dx - dy;
            while (x != toX || y != toY)
            {
                int twice = error * 2;
                if (twice > -dy) { error -= dy; x += stepX; }
                if (twice < dx) { error += dx; y += stepY; }
                if ((x != toX || y != toY) && _map.GetTile(x, y) == TileType.Mountain) return true;
            }
            return false;
        }
    }
}
