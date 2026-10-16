using System;
using System.Collections.Generic;
using Game.Content.Definitions;
using Game.Core.Commands;
using Game.Core.Models;
using Game.Core.Types;
using Game.Simulation.Systems;

namespace Game.AI
{
    public sealed class AiCommander : IMatchAgent
    {
        private const float BaseThreatRadius = 10f;
        private readonly ICommandDispatcher _dispatcher;
        private readonly IReadOnlyDictionary<string, ModuleDefinition> _moduleCatalog;

        public AiCommander(ICommandDispatcher dispatcher, IReadOnlyDictionary<string, ModuleDefinition> moduleCatalog)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _moduleCatalog = moduleCatalog ?? throw new ArgumentNullException(nameof(moduleCatalog));
        }

        public void Think(MatchState state)
        {
            if (state == null || state.IsPaused || state.Result != MatchResult.InProgress) return;
            BuildingState baseBuilding = FindBase(state, PlayerId.Player2);
            if (baseBuilding == null) return;

            QueueReinforcement(state, baseBuilding);
            List<RobotState> robots = GetActiveRobots(state, PlayerId.Player2);
            if (robots.Count == 0) return;

            if (FindNearestEnemy(state, baseBuilding.Position, BaseThreatRadius) != null)
            {
                IssueDefend(robots, baseBuilding.Position, state);
                return;
            }

            BuildingState objective = ChooseObjective(state, baseBuilding.Position, robots.Count, PlayerId.Player2);
            if (objective == null)
            {
                if (state.FogOfWar != null) IssueScout(robots, state);
                return;
            }
            int defenders = robots.Count >= 4 ? 1 : 0;
            int attackersCount = robots.Count - defenders;
            if (attackersCount > 0) IssueCapture(robots.GetRange(0, attackersCount), objective.Id, state);
            if (defenders > 0) IssueDefend(robots.GetRange(attackersCount, defenders), baseBuilding.Position, state);
        }

        private void QueueReinforcement(MatchState state, BuildingState baseBuilding)
        {
            if (!state.Player2.CanBuildMoreRobots || baseBuilding.ProductionQueue.Count >= BuildingState.MaxQueueSize) return;
            RobotPresetDefinition preset = ChooseProductionPreset(state, baseBuilding.Position);
            if (preset == null) return;
            _dispatcher.Dispatch(new EnqueueProductionCommand { PlayerId = PlayerId.Player2, BaseBuildingId = baseBuilding.Id, Blueprint = preset.CreateBlueprint() }, state);
        }

        /// <summary>
        /// Выбирает доступный пресет, который лучше всего тратит накопленные заводами ресурсы:
        /// каждая единица General ценится вдвое, поэтому склад специализированных деталей не простаивает.
        /// </summary>
        private RobotPresetDefinition ChooseProductionPreset(MatchState state, SimVector2 basePosition)
        {
            bool underThreat = FindNearestEnemy(state, basePosition, BaseThreatRadius) != null;
            RobotPresetDefinition best = null;
            int bestScore = int.MinValue;
            foreach (RobotPresetDefinition preset in RobotPresetCatalog.All)
            {
                int[] cost = preset.GetCostByType(_moduleCatalog);
                if (!state.Player2.Resources.TryPlanPayment(cost, out int[] payment)) continue;
                int totalCost = 0;
                foreach (int amount in cost) totalCost += amount;
                // Под угрозой базы важнее быстро выставить робота, чем экономить General.
                int score = underThreat ? -(int)(preset.BuildTimeSeconds * 10f) : totalCost - 2 * payment[(int)ResourceType.General];
                if (score > bestScore) { best = preset; bestScore = score; }
            }
            return best;
        }

        private static BuildingState ChooseObjective(MatchState state, SimVector2 from, int armySize, PlayerId owner)
        {
            BuildingState best = null;
            float bestScore = float.MaxValue;
            foreach (BuildingState building in state.Buildings.Values)
            {
                if (building.Owner == owner || (building.Type == BuildingType.Base && armySize < 3) ||
                    (state.FogOfWar != null && !state.FogOfWar.IsExplored(owner, building.Position))) continue;
                // Объект уже оспаривается противником: ищем свободную цель, а не кружим у зоны.
                if (building.CapturingPlayer != PlayerId.Neutral && building.CapturingPlayer != owner && building.CaptureProgressSeconds > 0f) continue;
                float score = SimVector2.SqrDistance(from, building.Position);
                if (building.Type == BuildingType.Base) score += 100f;
                if (building.Owner == PlayerId.Player1) score -= 25f;
                if (score < bestScore) { best = building; bestScore = score; }
            }
            return best;
        }

        private void IssueCapture(List<RobotState> robots, EntityId target, MatchState state)
        {
            if (robots.Count == 0 || AllHaveOrder(robots, OrderType.Capture, target)) return;
            _dispatcher.Dispatch(new OrderCaptureCommand { PlayerId = PlayerId.Player2, RobotIds = ToIds(robots), TargetBuildingId = target }, state);
        }

        private void IssueDefend(List<RobotState> robots, SimVector2 position, MatchState state)
        {
            if (robots.Count == 0 || AllDefending(robots, position)) return;
            _dispatcher.Dispatch(new OrderDefendCommand { PlayerId = PlayerId.Player2, RobotIds = ToIds(robots), TargetPosition = position }, state);
        }

        private void IssueScout(List<RobotState> robots, MatchState state)
        {
            float x = state.FogOfWar.Width * state.FogOfWar.TileSize * 0.5f;
            foreach (RobotState robot in robots)
            {
                if (robot.CurrentOrder.Type == OrderType.Scout) continue;
                float y = state.FogOfWar.Height * state.FogOfWar.TileSize * (0.3f + 0.2f * (robot.Id.Value % 3));
                _dispatcher.Dispatch(new OrderScoutCommand
                {
                    PlayerId = PlayerId.Player2,
                    RobotIds = new List<EntityId> { robot.Id },
                    TargetPosition = new SimVector2(x, y)
                }, state);
            }
        }

        private static List<RobotState> GetActiveRobots(MatchState state, PlayerId owner)
        {
            var result = new List<RobotState>();
            foreach (RobotState robot in state.Robots.Values) if (robot.IsAlive && robot.Owner == owner) result.Add(robot);
            result.Sort((left, right) => left.Id.Value.CompareTo(right.Id.Value));
            return result;
        }

        private static BuildingState FindBase(MatchState state, PlayerId owner)
        {
            foreach (BuildingState building in state.Buildings.Values) if (building.Type == BuildingType.Base && building.Owner == owner) return building;
            return null;
        }

        private static RobotState FindNearestEnemy(MatchState state, SimVector2 position, float radius)
        {
            float nearestDistance = radius * radius;
            RobotState nearest = null;
            foreach (RobotState robot in state.Robots.Values)
            {
                if (!robot.IsAlive || robot.Owner != PlayerId.Player1 ||
                    (state.FogOfWar != null && !state.FogOfWar.IsVisible(PlayerId.Player2, robot.Position))) continue;
                float distance = SimVector2.SqrDistance(position, robot.Position);
                if (distance <= nearestDistance) { nearest = robot; nearestDistance = distance; }
            }
            return nearest;
        }

        private static bool AllHaveOrder(List<RobotState> robots, OrderType type, EntityId target)
        {
            foreach (RobotState robot in robots) if (robot.CurrentOrder.Type != type || robot.CurrentOrder.TargetEntityId != target) return false;
            return true;
        }

        private static bool AllDefending(List<RobotState> robots, SimVector2 position)
        {
            foreach (RobotState robot in robots) if (robot.CurrentOrder.Type != OrderType.Defend || !robot.CurrentOrder.TargetPosition.Equals(position)) return false;
            return true;
        }

        private static List<EntityId> ToIds(List<RobotState> robots)
        {
            var ids = new List<EntityId>(robots.Count);
            foreach (RobotState robot in robots) ids.Add(robot.Id);
            return ids;
        }
    }
}
