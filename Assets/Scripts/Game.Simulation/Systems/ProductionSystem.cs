using System;
using System.Collections.Generic;
using Game.Content.Definitions;
using Game.Content.Validation;
using Game.Core.Models;
using Game.Core.Types;

namespace Game.Simulation.Systems
{
    public class ProductionSystem
    {
        private const float SpawnExitClearance = 1.5f;
        private readonly IReadOnlyDictionary<string, ModuleDefinition> _catalog;

        public event Action<RobotState> OnRobotSpawned;

        public ProductionSystem(IReadOnlyDictionary<string, ModuleDefinition> catalog)
        {
            _catalog = catalog;
        }

        public void Update(MatchState state, float deltaSeconds)
        {
            if (state == null || state.IsPaused || state.Result != MatchResult.InProgress)
                return;

            // 1. Почасовой доход: база даёт General, каждый захваченный завод — ресурс своего типа
            UpdateEconomy(state, deltaSeconds);

            // 2. Обновление очередей производства на базах
            UpdateProductionQueues(state, deltaSeconds);
        }

        /// <summary>Начисляет доход за каждый игровой час, начавшийся к концу этого тика.</summary>
        public static void UpdateEconomy(MatchState state, float deltaSeconds)
        {
            int elapsedHours = GameClock.GetElapsedHours(state.MatchTimeSeconds + deltaSeconds);
            while (state.IncomeHoursPaid < elapsedHours)
            {
                state.IncomeHoursPaid++;
                foreach (var building in state.Buildings.Values)
                {
                    PlayerState owner = state.GetPlayer(building.Owner);
                    owner?.Resources.Add(EconomyRules.GetProducedResource(building), EconomyRules.GetHourlyIncome(building));
                }
            }
        }

        /// <summary>Доход стороны за игровой час по типам ресурсов — для интерфейса и ИИ.</summary>
        public static int[] GetHourlyIncome(MatchState state, PlayerId owner)
        {
            var income = new int[ResourceStock.TypeCount];
            foreach (var building in state.Buildings.Values)
            {
                if (building.Owner == owner) income[(int)EconomyRules.GetProducedResource(building)] += EconomyRules.GetHourlyIncome(building);
            }
            return income;
        }

        private void UpdateProductionQueues(MatchState state, float deltaSeconds)
        {
            foreach (var building in state.Buildings.Values)
            {
                if (building.Type != BuildingType.Base || building.ProductionQueue.Count == 0)
                    continue;

                var currentOrder = building.ProductionQueue[0];
                var player = state.GetPlayer(building.Owner);

                if (currentOrder.Status == OrderProductionStatus.InProgress)
                {
                    currentOrder.RemainingBuildTimeSeconds -= deltaSeconds;
                    if (currentOrder.RemainingBuildTimeSeconds <= 0f)
                    {
                        currentOrder.RemainingBuildTimeSeconds = 0f;
                        // Проверяем, свободен ли выход базы (SpawnPoint)
                        if (IsSpawnPointBlocked(building.SpawnPoint, state))
                        {
                            currentOrder.Status = OrderProductionStatus.ExitBlocked;
                        }
                        else
                        {
                            CompleteOrderAndSpawn(building, currentOrder, state, player);
                        }
                    }
                }
                else if (currentOrder.Status == OrderProductionStatus.ExitBlocked)
                {
                    // Ожидание освобождения выхода
                    if (!IsSpawnPointBlocked(building.SpawnPoint, state))
                    {
                        CompleteOrderAndSpawn(building, currentOrder, state, player);
                    }
                }
            }
        }

        private bool IsSpawnPointBlocked(SimVector2 spawnPoint, MatchState state)
        {
            const float blockRadius = 1.2f;
            foreach (var robot in state.Robots.Values)
            {
                if (robot.IsAlive && SimVector2.Distance(robot.Position, spawnPoint) < blockRadius)
                {
                    return true;
                }
            }
            return false;
        }

        private void CompleteOrderAndSpawn(BuildingState building, ProductionOrder order, MatchState state, PlayerState player)
        {
            building.ProductionQueue.RemoveAt(0);

            if (player != null)
            {
                player.QueuedRobotsCount = Math.Max(0, player.QueuedRobotsCount - 1);
                player.ActiveRobotsCount++;
            }

            var val = RobotAssemblyValidator.Validate(order.Blueprint, _catalog);
            int robotId = ++state.NextRobotId;

            var newRobot = new RobotState
            {
                Id = new EntityId(robotId),
                Owner = building.Owner,
                Blueprint = order.Blueprint,
                Position = GetReleasePosition(building, robotId),
                RotationDegrees = building.Owner == PlayerId.Player1 ? 0f : 180f,
                MaxHealth = val.TotalHealth,
                CurrentHealth = val.TotalHealth,
                MoveSpeed = val.Speed,
                Mobility = val.Mobility,
                TotalCost = val.TotalCost
            };
            newRobot.CurrentOrder = RobotOrder.Defend(building.Position);
            foreach (string ignored in order.Blueprint.WeaponIds) newRobot.WeaponCooldowns.Add(0f);

            state.Robots[newRobot.Id.Value] = newRobot;
            OnRobotSpawned?.Invoke(newRobot);
        }

        private static SimVector2 GetReleasePosition(BuildingState building, int robotId)
        {
            SimVector2 direction = building.SpawnPoint - building.Position;
            if (direction.SqrMagnitude < 0.001f)
            {
                direction = building.Owner == PlayerId.Player2
                    ? new SimVector2(-1f, 0f)
                    : new SimVector2(1f, 0f);
            }

            direction /= direction.Magnitude;
            SimVector2 tangent = new SimVector2(-direction.Y, direction.X);
            float laneOffset = (robotId % 5 - 2) * 1.8f;
            return building.SpawnPoint + direction * SpawnExitClearance + tangent * laneOffset;
        }
    }
}
