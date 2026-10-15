using System;
using Game.Core.Models;
using Game.Core.Types;
using Game.Simulation.Navigation;

namespace Game.Simulation.Systems
{
    /// <summary>Перемещает роботов по пути A* или ручному приводу с локальным объездом соседей и зданий.</summary>
    public sealed class MovementSystem
    {
        public const float RobotSeparation = 1.7f;
        private const float WaypointReachedDistance = 0.1f;
        private const float MinSpeedMultiplier = 0.1f;

        private readonly MatchNavigation _navigation;

        public MovementSystem(MatchNavigation navigation)
        {
            _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        }

        public void Update(MatchState state, float deltaSeconds)
        {
            if (state == null || state.IsPaused || state.Result != MatchResult.InProgress || deltaSeconds <= 0f) return;

            foreach (RobotState robot in state.Robots.Values)
            {
                if (!robot.IsAlive)
                {
                    robot.Velocity = SimVector2.Zero;
                    continue;
                }

                SimVector2 before = robot.Position;
                if (robot.DriveDirection.SqrMagnitude > 0.001f) Drive(robot, state, deltaSeconds);
                else if (robot.HasPath) FollowPath(robot, state, deltaSeconds);
                robot.Velocity = (robot.Position - before) / deltaSeconds;
            }
        }

        public bool IsPositionFree(RobotState movingRobot, SimVector2 position, MatchState state)
        {
            NavigationGrid grid = _navigation.GetGrid(movingRobot.Mobility);
            GridCoord cell = grid.WorldToGrid(position);
            if (!grid.IsWalkable(cell.X, cell.Y)) return false;
            foreach (RobotState other in state.Robots.Values)
            {
                if (other == movingRobot || !other.IsAlive) continue;
                if (SimVector2.SqrDistance(position, other.Position) < RobotSeparation * RobotSeparation) return false;
            }
            foreach (BuildingState building in state.Buildings.Values)
            {
                float clearance = BuildingFootprint.GetClearance(building.Type);
                if (SimVector2.SqrDistance(position, building.Position) < clearance * clearance) return false;
            }
            return true;
        }

        public float GetTerrainSpeedMultiplier(RobotState robot, SimVector2 position)
        {
            NavigationGrid grid = _navigation.GetGrid(robot.Mobility);
            GridCoord cell = grid.WorldToGrid(position);
            float multiplier = grid.GetSpeedMultiplier(cell.X, cell.Y);
            return Math.Max(MinSpeedMultiplier, multiplier > 0f ? multiplier : 1f);
        }

        private void Drive(RobotState robot, MatchState state, float deltaSeconds)
        {
            SimVector2 step = robot.DriveDirection * (robot.MoveSpeed * GetTerrainSpeedMultiplier(robot, robot.Position) * deltaSeconds);
            robot.Position = ResolveStep(robot, robot.Position + step, state);
            robot.RotationDegrees = HeadingDegrees(robot.DriveDirection);
        }

        private void FollowPath(RobotState robot, MatchState state, float deltaSeconds)
        {
            SimVector2 waypoint = robot.Path[robot.PathIndex];
            SimVector2 toWaypoint = waypoint - robot.Position;
            float distance = toWaypoint.Magnitude;
            float maxStep = robot.MoveSpeed * GetTerrainSpeedMultiplier(robot, robot.Position) * deltaSeconds;
            SimVector2 desired = distance <= maxStep ? waypoint : robot.Position + toWaypoint / distance * maxStep;
            robot.Position = ResolveStep(robot, desired, state);

            SimVector2 heading = waypoint - robot.Position;
            if (heading.SqrMagnitude > 0.001f) robot.RotationDegrees = HeadingDegrees(heading);
            if (SimVector2.Distance(robot.Position, waypoint) < WaypointReachedDistance) robot.PathIndex++;
        }

        private SimVector2 ResolveStep(RobotState robot, SimVector2 desired, MatchState state)
        {
            if (IsPositionFree(robot, desired, state)) return desired;
            // Объезд ограничен длиной шага: коллизии не телепортируют робота и не искажают упреждение.
            SimVector2 origin = robot.Position;
            SimVector2 step = desired - origin;
            float side = robot.Id.Value % 2 == 0 ? 1f : -1f;
            for (int attempt = 1; attempt <= 4; attempt++)
            {
                float angle = (attempt <= 2 ? 45f : 90f) * (attempt % 2 == 1 ? side : -side);
                SimVector2 candidate = origin + Rotate(step, angle);
                if (IsPositionFree(robot, candidate, state)) return candidate;
            }
            return origin;
        }

        /// <summary>Поворот вокруг вертикали в системе Unity: X — вправо, Y симуляции — ось Z мира.</summary>
        private static SimVector2 Rotate(SimVector2 vector, float degrees)
        {
            double radians = degrees * Math.PI / 180.0;
            float cos = (float)Math.Cos(radians);
            float sin = (float)Math.Sin(radians);
            return new SimVector2(vector.X * cos + vector.Y * sin, -vector.X * sin + vector.Y * cos);
        }

        private static float HeadingDegrees(SimVector2 direction)
        {
            float degrees = (float)(Math.Atan2(direction.X, direction.Y) * 180.0 / Math.PI);
            return degrees < 0f ? degrees + 360f : degrees;
        }
    }
}
