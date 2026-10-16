using System;
using Game.Core.Models;
using Game.Core.Types;

namespace Game.Simulation.Systems
{
    public sealed class CaptureSystem
    {
        public static bool IsInCaptureRange(RobotState robot, BuildingState building) =>
            robot != null && robot.IsAlive && building != null &&
            SimVector2.SqrDistance(robot.Position, building.Position) <= building.CaptureRadius * building.CaptureRadius;

        public event Action<BuildingState, PlayerId> OnBuildingCaptured;

        public void Update(MatchState state, float deltaSeconds)
        {
            if (state == null || state.IsPaused || state.Result != MatchResult.InProgress)
            {
                return;
            }

            foreach (var building in state.Buildings.Values)
            {
                bool player1Present = false;
                bool player2Present = false;
                foreach (var robot in state.Robots.Values)
                {
                    if (!robot.IsAlive ||
                        (robot.CurrentOrder.Type != OrderType.Capture &&
                         !(robot.CurrentOrder.Type == OrderType.Program && robot.ActiveProgramAction == ProgramAction.Capture)) ||
                        robot.CurrentOrder.TargetEntityId != building.Id ||
                        !IsInCaptureRange(robot, building))
                    {
                        continue;
                    }

                    player1Present |= robot.Owner == PlayerId.Player1;
                    player2Present |= robot.Owner == PlayerId.Player2;
                }

                if (player1Present && player2Present)
                {
                    building.IsContested = true;
                    continue;
                }

                building.IsContested = false;
                PlayerId capturer = player1Present ? PlayerId.Player1 : player2Present ? PlayerId.Player2 : PlayerId.Neutral;
                if (capturer == PlayerId.Neutral || capturer == building.Owner)
                {
                    ResetCapture(building);
                    continue;
                }

                if (building.CapturingPlayer != capturer)
                {
                    building.CapturingPlayer = capturer;
                    building.CaptureProgressSeconds = 0f;
                }

                building.CaptureProgressSeconds += deltaSeconds;
                if (building.CaptureProgressSeconds >= building.CaptureTimeTotal)
                {
                    CompleteCapture(building, capturer, state);
                }
            }
        }

        private static void ResetCapture(BuildingState building)
        {
            building.CapturingPlayer = PlayerId.Neutral;
            building.CaptureProgressSeconds = 0f;
        }

        private void CompleteCapture(BuildingState building, PlayerId capturer, MatchState state)
        {
            PlayerId previousOwner = building.Owner;
            var previousPlayer = state.GetPlayer(previousOwner);
            if (previousPlayer != null)
            {
                foreach (var order in building.ProductionQueue)
                {
                    previousPlayer.Resources.Add(order.Payment);
                    previousPlayer.QueuedRobotsCount = Math.Max(0, previousPlayer.QueuedRobotsCount - 1);
                }
            }

            building.ProductionQueue.Clear();
            building.Owner = capturer;
            ResetCapture(building);
            if (building.Type == BuildingType.Base)
            {
                state.Result = capturer == PlayerId.Player1 ? MatchResult.Player1Won : MatchResult.Player2Won;
            }
            OnBuildingCaptured?.Invoke(building, previousOwner);
        }
    }
}
