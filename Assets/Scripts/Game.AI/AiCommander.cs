using System;
using System.Collections.Generic;
using Game.Content.Definitions;
using Game.Core.Commands;
using Game.Core.Models;
using Game.Core.Types;

namespace Game.AI
{
    public sealed class AiCommander
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
            if (state == null || state.Result != MatchResult.InProgress) return;
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

            BuildingState objective = ChooseObjective(state, baseBuilding.Position, robots.Count);
            if (objective == null) return;
            int defenders = robots.Count >= 4 ? 1 : 0;
            int attackersCount = robots.Count - defenders;
            if (attackersCount > 0) IssueCapture(robots.GetRange(0, attackersCount), objective.Id, state);
            if (defenders > 0) IssueDefend(robots.GetRange(attackersCount, defenders), baseBuilding.Position, state);
        }

        private void QueueReinforcement(MatchState state, BuildingState baseBuilding)
        {
            if (!state.Player2.CanBuildMoreRobots || baseBuilding.ProductionQueue.Count >= BuildingState.MaxQueueSize) return;
            RobotPresetDefinition preset = ChooseProductionPreset(state, baseBuilding.Position);
            if (state.Player2.Metal < preset.GetCost(_moduleCatalog)) return;
            _dispatcher.Dispatch(new EnqueueProductionCommand { PlayerId = PlayerId.Player2, BaseBuildingId = baseBuilding.Id, Blueprint = preset.CreateBlueprint() }, state);
        }

        private RobotPresetDefinition ChooseProductionPreset(MatchState state, SimVector2 basePosition)
        {
            bool underThreat = FindNearestEnemy(state, basePosition, BaseThreatRadius) != null;
            if (underThreat && state.Player2.Metal >= RobotPresetCatalog.Tank.GetCost(_moduleCatalog)) return RobotPresetCatalog.Tank;
            if (state.Player1.ActiveRobotsCount >= 3 && state.Player2.Metal >= RobotPresetCatalog.Missile.GetCost(_moduleCatalog)) return RobotPresetCatalog.Missile;
            if (state.Player2.ActiveRobotsCount < state.Player1.ActiveRobotsCount && state.Player2.Metal >= RobotPresetCatalog.Tank.GetCost(_moduleCatalog)) return RobotPresetCatalog.Tank;
            return RobotPresetCatalog.Scout;
        }

        private static BuildingState ChooseObjective(MatchState state, SimVector2 from, int armySize)
        {
            BuildingState best = null;
            float bestScore = float.MaxValue;
            foreach (BuildingState building in state.Buildings.Values)
            {
                if (building.Owner == PlayerId.Player2 || (building.Type == BuildingType.Base && armySize < 3)) continue;
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
                if (!robot.IsAlive || robot.Owner != PlayerId.Player1) continue;
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
