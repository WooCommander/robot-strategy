using System;
using Game.Core.Models;
using Game.Core.Types;

namespace Game.Simulation.Systems
{
    public sealed class CaptureSystem
    {
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
                float captureRadiusSq = building.CaptureRadius * building.CaptureRadius;
                foreach (var robot in state.Robots.Values)
                {
                    if (!robot.IsAlive ||
                        robot.CurrentOrder.Type != OrderType.Capture ||
                        robot.CurrentOrder.TargetEntityId != building.Id ||
                        SimVector2.SqrDistance(robot.Position, building.Position) > captureRadiusSq)
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
                    previousPlayer.Metal += order.Cost;
                    previousPlayer.QueuedRobotsCount = Math.Max(0, previousPlayer.QueuedRobotsCount - 1);
                }
            }

            building.ProductionQueue.Clear();
            building.Owner = capturer;
            ResetCapture(building);
            OnBuildingCaptured?.Invoke(building, previousOwner);
        }
    }
}
