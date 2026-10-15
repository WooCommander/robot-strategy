using System;
using System.Collections.Generic;
using Game.Content.Definitions;
using Game.Core.Models;
using Game.Core.Types;
using Game.Simulation.Navigation;

namespace Game.Simulation.Systems
{
    /// <summary>
    /// Превращает приказы роботов в маршруты: захват, оборона, разведка, движение и боевой манёвр.
    /// Работает в тиках симуляции, поэтому одинаково ведёт себя в сцене и в headless-прогоне.
    /// </summary>
    public sealed class OrderPlanner
    {
        /// <summary>Стратегические приказы пересчитываются раз в 0,4 с при 20 тиках в секунду.</summary>
        public const int PlanningIntervalTicks = 8;
        private const float DefaultManeuverIntervalSeconds = 0.5f;
        private const float MinManeuverIntervalSeconds = 0.05f;
        private const float DefendEngageRadius = 8f;
        private const float MoveArrivalDistance = 0.5f;

        private readonly IReadOnlyDictionary<string, ModuleDefinition> _catalog;
        private readonly MapDefinition _map;
        private readonly NavigationGrid _grid;
        private readonly AStarPathfinder _pathfinder;
        private readonly MovementSystem _movement;
        private readonly List<GridCoord> _captureCandidates = new List<GridCoord>();

        public OrderPlanner(IReadOnlyDictionary<string, ModuleDefinition> catalog, MapDefinition map, NavigationGrid grid, AStarPathfinder pathfinder, MovementSystem movement)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _map = map ?? throw new ArgumentNullException(nameof(map));
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
            _pathfinder = pathfinder ?? throw new ArgumentNullException(nameof(pathfinder));
            _movement = movement ?? throw new ArgumentNullException(nameof(movement));
        }

        public void Update(MatchState state)
        {
            if (state == null || state.IsPaused || state.Result != MatchResult.InProgress) return;
            bool strategicTick = state.Tick % PlanningIntervalTicks == 0;

            foreach (RobotState robot in state.Robots.Values)
            {
                if (!robot.IsAlive) continue;
                switch (robot.CurrentOrder.Type)
                {
                    case OrderType.Attack:
                        if (state.Robots.TryGetValue(robot.CurrentOrder.TargetEntityId.Value, out RobotState attackTarget) && attackTarget.IsAlive)
                        {
                            UpdateCombatManeuver(robot, attackTarget, state);
                        }
                        break;
                    case OrderType.Hunt:
                        UpdateHunt(robot, state);
                        break;
                    case OrderType.Capture:
                        if (strategicTick) UpdateCapture(robot, state);
                        break;
                    case OrderType.Defend:
                        if (strategicTick) UpdateDefend(robot, state);
                        break;
                    case OrderType.Scout:
                        if (strategicTick) UpdateScout(robot);
                        break;
                    case OrderType.Move:
                        if (strategicTick) UpdateMove(robot);
                        break;
                }
            }
        }

        public static SimVector2 GetScoutDestination(RobotState robot, MapDefinition map, int patrolStep = 0)
        {
            // Разведка ведёт в сектора у противника, но не к скалам карты и не под базу.
            float x = robot.Owner == PlayerId.Player1 ? map.Width - 9f : 8f;
            float y = 6f + ((robot.Id.Value + patrolStep) % 3) * 9f;
            return new SimVector2(x, y);
        }

        private void UpdateCapture(RobotState robot, MatchState state)
        {
            BuildingState target;
            if (robot.CurrentOrder.TargetEntityId.IsValid &&
                state.Buildings.TryGetValue(robot.CurrentOrder.TargetEntityId.Value, out BuildingState commandedTarget) &&
                commandedTarget.Owner != robot.Owner)
            {
                target = commandedTarget;
            }
            else
            {
                target = FindNearestForeignBuilding(robot, state);
            }

            if (target == null)
            {
                robot.ClearPath();
                return;
            }

            bool targetChanged = robot.CurrentOrder.TargetEntityId != target.Id;
            robot.CurrentOrder.TargetEntityId = target.Id;
            if (CaptureSystem.IsInCaptureRange(robot, target))
            {
                robot.ClearPath();
                return;
            }
            if (targetChanged || !robot.HasPath) RouteToCapture(robot, target, state);
        }

        private void UpdateDefend(RobotState robot, MatchState state)
        {
            robot.ClearPath();
            RobotState enemy = FindNearestEnemy(robot, state);
            robot.CurrentOrder.TargetEntityId = enemy != null && SimVector2.SqrDistance(robot.Position, enemy.Position) <= DefendEngageRadius * DefendEngageRadius
                ? enemy.Id
                : EntityId.None;
        }

        private void UpdateScout(RobotState robot)
        {
            bool targetIsAtMapEdge = robot.CurrentOrder.TargetPosition.X <= 2f ||
                                     robot.CurrentOrder.TargetPosition.X >= _map.Width - 3f;
            bool reachedTarget = SimVector2.SqrDistance(robot.Position, robot.CurrentOrder.TargetPosition) < 0.8f * 0.8f;
            if (targetIsAtMapEdge || reachedTarget)
            {
                if (reachedTarget) robot.ScoutPatrolStep++;
                robot.CurrentOrder.TargetPosition = GetScoutDestination(robot, _map, robot.ScoutPatrolStep);
                robot.ClearPath();
            }

            if (!robot.HasPath) RouteTo(robot, robot.CurrentOrder.TargetPosition);
        }

        private void UpdateMove(RobotState robot)
        {
            // Пустой путь означает новый приказ; исчерпанный путь — робот уже дошёл.
            if (robot.HasPath || robot.Path.Count > 0) return;
            if (SimVector2.SqrDistance(robot.Position, robot.CurrentOrder.TargetPosition) <= MoveArrivalDistance * MoveArrivalDistance) return;
            if (!_pathfinder.TryFindPath(robot.Position, robot.CurrentOrder.TargetPosition, out List<SimVector2> path))
            {
                robot.CurrentOrder = RobotOrder.Stop();
                return;
            }
            robot.SetPath(path);
        }

        private void UpdateHunt(RobotState robot, MatchState state)
        {
            if (state.MatchTimeSeconds < robot.NextManeuverSeconds) return;
            RobotState target = FindNearestEnemy(robot, state);
            if (target == null) return;
            robot.CurrentOrder.TargetEntityId = target.Id;
            UpdateCombatManeuver(robot, target, state);
        }

        private void UpdateCombatManeuver(RobotState robot, RobotState target, MatchState state)
        {
            if (state.MatchTimeSeconds < robot.NextManeuverSeconds) return;
            if (!CombatRangePlanner.TryGetManeuverDestination(robot, target, _catalog, out SimVector2 destination)) return;

            float interval = DefaultManeuverIntervalSeconds;
            if (robot.Blueprint?.ControlModuleId != null && _catalog.TryGetValue(robot.Blueprint.ControlModuleId, out ModuleDefinition controller))
            {
                interval = Math.Max(MinManeuverIntervalSeconds, controller.CombatManeuverIntervalSeconds);
            }
            robot.NextManeuverSeconds = state.MatchTimeSeconds + interval;

            bool destinationChanged = SimVector2.SqrDistance(robot.CurrentOrder.TargetPosition, destination) > 1f;
            robot.CurrentOrder.TargetPosition = destination;
            if (destinationChanged || !robot.HasPath) RouteTo(robot, destination);
        }

        private void RouteTo(RobotState robot, SimVector2 target)
        {
            if (_pathfinder.TryFindPath(robot.Position, target, out List<SimVector2> path)) robot.SetPath(path);
        }

        private void RouteToCapture(RobotState robot, BuildingState building, MatchState state)
        {
            // Выбираем реальную проходимую клетку внутри зоны, а не точку на дуге от текущей позиции.
            // Кандидаты упорядочены по расстоянию, поэтому A* запускается только до первого успеха.
            float clearance = BuildingFootprint.GetClearance(building.Type);
            float minSq = clearance * clearance;
            float maxSq = building.CaptureRadius * building.CaptureRadius;
            GridCoord min = _grid.WorldToGrid(new SimVector2(building.Position.X - building.CaptureRadius, building.Position.Y - building.CaptureRadius));
            GridCoord max = _grid.WorldToGrid(new SimVector2(building.Position.X + building.CaptureRadius, building.Position.Y + building.CaptureRadius));

            _captureCandidates.Clear();
            for (int y = Math.Max(0, min.Y); y <= Math.Min(_grid.Height - 1, max.Y); y++)
            {
                for (int x = Math.Max(0, min.X); x <= Math.Min(_grid.Width - 1, max.X); x++)
                {
                    if (!_grid.IsWalkable(x, y)) continue;
                    float radiusSq = SimVector2.SqrDistance(_grid.GridToWorld(new GridCoord(x, y)), building.Position);
                    if (radiusSq < minSq || radiusSq > maxSq) continue;
                    _captureCandidates.Add(new GridCoord(x, y));
                }
            }

            SimVector2 from = robot.Position;
            _captureCandidates.Sort((left, right) =>
            {
                int byDistance = SimVector2.SqrDistance(_grid.GridToWorld(left), from).CompareTo(SimVector2.SqrDistance(_grid.GridToWorld(right), from));
                if (byDistance != 0) return byDistance;
                return left.Y != right.Y ? left.Y.CompareTo(right.Y) : left.X.CompareTo(right.X);
            });

            foreach (GridCoord cell in _captureCandidates)
            {
                SimVector2 point = _grid.GridToWorld(cell);
                if (!_movement.IsPositionFree(robot, point, state)) continue;
                if (!_pathfinder.TryFindPath(robot.Position, point, out List<SimVector2> path)) continue;
                robot.SetPath(path);
                return;
            }
            robot.ClearPath();
        }

        private static BuildingState FindNearestForeignBuilding(RobotState robot, MatchState state)
        {
            BuildingState nearest = null;
            float nearestDistance = float.MaxValue;
            foreach (BuildingState building in state.Buildings.Values)
            {
                if (building.Owner == robot.Owner) continue;
                float distance = SimVector2.SqrDistance(robot.Position, building.Position);
                if (distance < nearestDistance)
                {
                    nearest = building;
                    nearestDistance = distance;
                }
            }
            return nearest;
        }

        private static RobotState FindNearestEnemy(RobotState robot, MatchState state)
        {
            RobotState nearest = null;
            float nearestDistance = float.MaxValue;
            foreach (RobotState candidate in state.Robots.Values)
            {
                if (!candidate.IsAlive || candidate.Owner == robot.Owner) continue;
                float distance = SimVector2.SqrDistance(robot.Position, candidate.Position);
                if (distance < nearestDistance)
                {
                    nearest = candidate;
                    nearestDistance = distance;
                }
            }
            return nearest;
        }
    }
}
