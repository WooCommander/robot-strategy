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
        private const float DefaultManeuverIntervalSeconds = DefaultContentCatalog.BaseManeuverIntervalSeconds;
        private const float MinManeuverIntervalSeconds = 0.05f;
        private const float DefendEngageRadius = 8f;
        private const float MoveArrivalDistance = 0.5f;

        private readonly IReadOnlyDictionary<string, ModuleDefinition> _catalog;
        private readonly MapDefinition _map;
        private readonly MatchNavigation _navigation;
        private readonly MovementSystem _movement;
        private readonly List<GridCoord> _captureCandidates = new List<GridCoord>();
        private readonly List<GridCoord> _exploreCandidates = new List<GridCoord>();

        public OrderPlanner(IReadOnlyDictionary<string, ModuleDefinition> catalog, MapDefinition map, MatchNavigation navigation, MovementSystem movement)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _map = map ?? throw new ArgumentNullException(nameof(map));
            _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
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
                        if (state.Robots.TryGetValue(robot.CurrentOrder.TargetEntityId.Value, out RobotState attackTarget) && attackTarget.IsAlive && IsEnemyVisible(robot, attackTarget, state))
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
                    case OrderType.Program:
                        UpdateProgram(robot, state, strategicTick);
                        break;
                }
            }
        }

        public static SimVector2 GetScoutDestination(RobotState robot, MapDefinition map, int patrolStep = 0)
        {
            // Разведка идёт по трём магистралям у противника (северной, центральной, южной), не под саму базу.
            float x = robot.Owner == PlayerId.Player1 ? map.Width - 13.5f : 13.5f;
            int lane = (robot.Id.Value + patrolStep) % 3;
            float y = (float)Math.Floor(map.Height * (0.275f + 0.225f * lane)) + 0.5f;
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
                if (state.FogOfWar != null) RouteToExplore(robot, state);
                else robot.ClearPath();
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
            if (!Pathfinder(robot).TryFindPath(robot.Position, robot.CurrentOrder.TargetPosition, out List<SimVector2> path))
            {
                robot.CurrentOrder = RobotOrder.Stop();
                return;
            }
            robot.SetPath(path);
        }

        private void UpdateHunt(RobotState robot, MatchState state)
        {
            RobotState target = FindNearestEnemy(robot, state);
            if (target == null)
            {
                robot.CurrentOrder.TargetEntityId = EntityId.None;
                if (state.FogOfWar != null) RouteToExplore(robot, state);
                return;
            }
            if (robot.CurrentOrder.TargetEntityId != target.Id)
            {
                robot.ClearPath();
                robot.NextManeuverSeconds = 0f;
            }
            if (state.MatchTimeSeconds < robot.NextManeuverSeconds) return;
            robot.CurrentOrder.TargetEntityId = target.Id;
            UpdateCombatManeuver(robot, target, state);
        }

        private void UpdateProgram(RobotState robot, MatchState state, bool strategicTick)
        {
            bool emergencyRetreat = robot.RetreatHealthPercent > 0 && robot.MaxHealth > 0 &&
                robot.CurrentHealth * 100L <= (long)robot.RetreatHealthPercent * robot.MaxHealth;
            if (strategicTick || robot.ActiveProgramAction == ProgramAction.None ||
                (emergencyRetreat && robot.ActiveProgramAction != ProgramAction.Retreat))
            {
                ProgramAction choice = ProgramAction.None;
                EntityId targetId = EntityId.None;
                BuildingState refuge = emergencyRetreat ? FindRetreatBuilding(robot, state) : null;
                if (refuge != null) { choice = ProgramAction.Retreat; targetId = refuge.Id; }
                else
                {
                    RobotState defenseTarget = FindThreatToOwnedBuilding(robot, state);
                    RobotState attackTarget = FindProgramAttackTarget(robot, state);
                    BuildingState captureTarget = null;
                    if (robot.ActiveProgramAction == ProgramAction.Capture &&
                        state.Buildings.TryGetValue(robot.CurrentOrder.TargetEntityId.Value, out BuildingState currentBuilding) &&
                        IsProgramCaptureCandidate(robot, currentBuilding, state))
                        captureTarget = currentBuilding;
                    else captureTarget = FindProgramCaptureTarget(robot, state);

                    for (int rank = 1; rank <= 3; rank++)
                    {
                        if (robot.DefendPriority == rank && defenseTarget != null && MeetsHealthCondition(robot, robot.DefendMinHealthPercent)) { choice = ProgramAction.Defend; targetId = defenseTarget.Id; break; }
                        if (robot.AttackPriority == rank && attackTarget != null && MeetsHealthCondition(robot, robot.AttackMinHealthPercent)) { choice = ProgramAction.Attack; targetId = attackTarget.Id; break; }
                        if (robot.CapturePriority == rank && captureTarget != null && MeetsHealthCondition(robot, robot.CaptureMinHealthPercent)) { choice = ProgramAction.Capture; targetId = captureTarget.Id; break; }
                    }
                    if (choice == ProgramAction.None && state.FogOfWar != null && MeetsHealthCondition(robot, robot.CaptureMinHealthPercent))
                        choice = ProgramAction.Explore;
                }

                if (choice != robot.ActiveProgramAction || targetId != robot.CurrentOrder.TargetEntityId)
                {
                    robot.ActiveProgramAction = choice;
                    robot.CurrentOrder.TargetEntityId = targetId;
                    robot.ClearPath();
                    robot.NextManeuverSeconds = 0f;
                }
            }

            if (robot.ActiveProgramAction == ProgramAction.Capture)
            {
                if (strategicTick) UpdateCapture(robot, state);
            }
            else if (robot.ActiveProgramAction == ProgramAction.Attack || robot.ActiveProgramAction == ProgramAction.Defend)
            {
                if (state.Robots.TryGetValue(robot.CurrentOrder.TargetEntityId.Value, out RobotState target) && target.IsAlive)
                    UpdateCombatManeuver(robot, target, state);
            }
            else if (robot.ActiveProgramAction == ProgramAction.Explore)
            {
                if (strategicTick) RouteToExplore(robot, state);
            }
            else if (robot.ActiveProgramAction == ProgramAction.Retreat)
            {
                if (strategicTick || !robot.HasPath) UpdateRetreat(robot, state);
            }
            else robot.ClearPath();
        }

        private static BuildingState FindRetreatBuilding(RobotState robot, MatchState state)
        {
            BuildingState nearestBase = null, nearestOther = null;
            float baseDistance = float.MaxValue, otherDistance = float.MaxValue;
            foreach (BuildingState building in state.Buildings.Values)
            {
                if (building.Owner != robot.Owner) continue;
                float distance = SimVector2.SqrDistance(robot.Position, building.SpawnPoint);
                if (building.Type == BuildingType.Base && distance < baseDistance) { nearestBase = building; baseDistance = distance; }
                else if (building.Type != BuildingType.Base && distance < otherDistance) { nearestOther = building; otherDistance = distance; }
            }
            return nearestBase ?? nearestOther;
        }

        private void UpdateRetreat(RobotState robot, MatchState state)
        {
            if (!state.Buildings.TryGetValue(robot.CurrentOrder.TargetEntityId.Value, out BuildingState refuge) || refuge.Owner != robot.Owner)
            {
                robot.ClearPath();
                return;
            }
            SimVector2 destination = refuge.SpawnPoint;
            robot.CurrentOrder.TargetPosition = destination;
            if (SimVector2.SqrDistance(robot.Position, destination) <= MoveArrivalDistance * MoveArrivalDistance)
            {
                robot.ClearPath();
                return;
            }
            if (!robot.HasPath) RouteTo(robot, destination);
        }

        private static bool MeetsHealthCondition(RobotState robot, int minimumPercent) =>
            minimumPercent == 0 || robot.MaxHealth > 0 && robot.CurrentHealth * 100L >= (long)minimumPercent * robot.MaxHealth;

        private static RobotState FindThreatToOwnedBuilding(RobotState robot, MatchState state)
        {
            RobotState nearest = null;
            float nearestDistance = float.MaxValue;
            foreach (BuildingState building in state.Buildings.Values)
            {
                if (building.Owner != robot.Owner ||
                    (robot.DefendTargetId.IsValid && robot.DefendTargetId != building.Id)) continue;
                foreach (RobotState enemy in state.Robots.Values)
                {
                    if (!enemy.IsAlive || enemy.Owner == robot.Owner ||
                        !IsEnemyVisible(robot, enemy, state) ||
                        SimVector2.SqrDistance(enemy.Position, building.Position) > robot.DefendRadius * robot.DefendRadius) continue;
                    float distance = SimVector2.SqrDistance(robot.Position, enemy.Position);
                    if (distance >= nearestDistance) continue;
                    nearest = enemy;
                    nearestDistance = distance;
                }
            }
            return nearest;
        }

        private static RobotState FindProgramAttackTarget(RobotState robot, MatchState state)
        {
            RobotState target;
            if (robot.AttackTargetId.IsValid)
            {
                if (!state.Robots.TryGetValue(robot.AttackTargetId.Value, out target)) return null;
            }
            else target = FindNearestEnemy(robot, state);
            return target != null && target.IsAlive && target.Owner != robot.Owner && IsEnemyVisible(robot, target, state) &&
                   SimVector2.SqrDistance(robot.Position, target.Position) <= robot.AttackRadius * robot.AttackRadius
                ? target : null;
        }

        private static BuildingState FindProgramCaptureTarget(RobotState robot, MatchState state)
        {
            if (robot.CaptureTargetId.IsValid)
                return state.Buildings.TryGetValue(robot.CaptureTargetId.Value, out BuildingState selected) && IsProgramCaptureCandidate(robot, selected, state) ? selected : null;
            BuildingState nearest = null;
            float distance = float.MaxValue;
            foreach (BuildingState building in state.Buildings.Values)
            {
                if (!IsProgramCaptureCandidate(robot, building, state)) continue;
                float candidateDistance = SimVector2.SqrDistance(robot.Position, building.Position);
                if (candidateDistance >= distance) continue;
                nearest = building;
                distance = candidateDistance;
            }
            return nearest;
        }

        private static bool IsProgramCaptureCandidate(RobotState robot, BuildingState building, MatchState state) =>
            building.Owner != robot.Owner &&
            (!robot.CaptureTargetId.IsValid || robot.CaptureTargetId == building.Id) &&
            (state.FogOfWar == null || state.FogOfWar.IsExplored(robot.Owner, building.Position)) &&
            SimVector2.SqrDistance(robot.Position, building.Position) <= robot.CaptureRadius * robot.CaptureRadius;

        private void RouteToExplore(RobotState robot, MatchState state)
        {
            if (robot.HasPath || state.FogOfWar == null) return;
            NavigationGrid grid = _navigation.GetGrid(robot.Mobility);
            _exploreCandidates.Clear();
            for (int y = 0; y < grid.Height; y++)
            {
                for (int x = 0; x < grid.Width; x++)
                    if (state.FogOfWar.GetCell(robot.Owner, x, y) == 0 && grid.IsWalkable(x, y))
                        _exploreCandidates.Add(new GridCoord(x, y));
            }
            SimVector2 from = robot.Position;
            _exploreCandidates.Sort((left, right) =>
            {
                int distance = SimVector2.SqrDistance(grid.GridToWorld(left), from).CompareTo(SimVector2.SqrDistance(grid.GridToWorld(right), from));
                if (distance != 0) return distance;
                return left.Y != right.Y ? left.Y.CompareTo(right.Y) : left.X.CompareTo(right.X);
            });
            int attempts = Math.Min(32, _exploreCandidates.Count);
            for (int i = 0; i < attempts; i++)
            {
                SimVector2 target = grid.GridToWorld(_exploreCandidates[i]);
                if (!Pathfinder(robot).TryFindPath(robot.Position, target, out List<SimVector2> path)) continue;
                robot.SetPath(path);
                robot.CurrentOrder.TargetPosition = target;
                return;
            }
        }

        private void UpdateCombatManeuver(RobotState robot, RobotState target, MatchState state)
        {
            if (state.MatchTimeSeconds < robot.NextManeuverSeconds) return;
            if (!CombatRangePlanner.TryGetManeuverDestination(robot, target, _catalog, out SimVector2 destination)) return;

            float interval = DefaultManeuverIntervalSeconds;
            if (robot.Blueprint != null && robot.Blueprint.HasElectronics && _catalog.TryGetValue(robot.Blueprint.ElectronicsId, out ModuleDefinition electronics))
            {
                interval = Math.Max(MinManeuverIntervalSeconds, electronics.CombatManeuverIntervalSeconds);
            }
            robot.NextManeuverSeconds = state.MatchTimeSeconds + interval;

            bool destinationChanged = SimVector2.SqrDistance(robot.CurrentOrder.TargetPosition, destination) > 1f;
            robot.CurrentOrder.TargetPosition = destination;
            if (destinationChanged || !robot.HasPath) RouteTo(robot, destination);
        }

        private AStarPathfinder Pathfinder(RobotState robot) => _navigation.GetPathfinder(robot.Mobility);

        private void RouteTo(RobotState robot, SimVector2 target)
        {
            if (Pathfinder(robot).TryFindPath(robot.Position, target, out List<SimVector2> path)) robot.SetPath(path);
        }

        private void RouteToCapture(RobotState robot, BuildingState building, MatchState state)
        {
            // Выбираем реальную проходимую клетку внутри зоны, а не точку на дуге от текущей позиции.
            // Кандидаты упорядочены по расстоянию, поэтому A* запускается только до первого успеха.
            NavigationGrid grid = _navigation.GetGrid(robot.Mobility);
            float clearance = BuildingFootprint.GetClearance(building.Type);
            float minSq = clearance * clearance;
            float maxSq = building.CaptureRadius * building.CaptureRadius;
            GridCoord min = grid.WorldToGrid(new SimVector2(building.Position.X - building.CaptureRadius, building.Position.Y - building.CaptureRadius));
            GridCoord max = grid.WorldToGrid(new SimVector2(building.Position.X + building.CaptureRadius, building.Position.Y + building.CaptureRadius));

            _captureCandidates.Clear();
            for (int y = Math.Max(0, min.Y); y <= Math.Min(grid.Height - 1, max.Y); y++)
            {
                for (int x = Math.Max(0, min.X); x <= Math.Min(grid.Width - 1, max.X); x++)
                {
                    if (!grid.IsWalkable(x, y)) continue;
                    float radiusSq = SimVector2.SqrDistance(grid.GridToWorld(new GridCoord(x, y)), building.Position);
                    if (radiusSq < minSq || radiusSq > maxSq) continue;
                    _captureCandidates.Add(new GridCoord(x, y));
                }
            }

            SimVector2 from = robot.Position;
            _captureCandidates.Sort((left, right) =>
            {
                int byDistance = SimVector2.SqrDistance(grid.GridToWorld(left), from).CompareTo(SimVector2.SqrDistance(grid.GridToWorld(right), from));
                if (byDistance != 0) return byDistance;
                return left.Y != right.Y ? left.Y.CompareTo(right.Y) : left.X.CompareTo(right.X);
            });

            foreach (GridCoord cell in _captureCandidates)
            {
                SimVector2 point = grid.GridToWorld(cell);
                if (!_movement.IsPositionFree(robot, point, state)) continue;
                if (!Pathfinder(robot).TryFindPath(robot.Position, point, out List<SimVector2> path)) continue;
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
                if (building.Owner == robot.Owner ||
                    (state.FogOfWar != null && !state.FogOfWar.IsExplored(robot.Owner, building.Position))) continue;
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
                if (!candidate.IsAlive || candidate.Owner == robot.Owner || !IsEnemyVisible(robot, candidate, state)) continue;
                float distance = SimVector2.SqrDistance(robot.Position, candidate.Position);
                if (distance < nearestDistance)
                {
                    nearest = candidate;
                    nearestDistance = distance;
                }
            }
            return nearest;
        }

        private static bool IsEnemyVisible(RobotState observer, RobotState enemy, MatchState state) =>
            state.FogOfWar == null || state.FogOfWar.IsVisible(observer.Owner, enemy.Position);
    }
}
