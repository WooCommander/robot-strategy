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
        private int _nextEntityId = 1000;

        public event Action<RobotState> OnRobotSpawned;

        public ProductionSystem(IReadOnlyDictionary<string, ModuleDefinition> catalog)
        {
            _catalog = catalog;
        }

        public void Update(MatchState state, float deltaSeconds)
        {
            if (state == null || state.IsPaused || state.Result != MatchResult.InProgress)
                return;

            // 1. Начисление дохода от металла (раздел 6: база 5/с, завод 10/с)
            UpdateEconomy(state, deltaSeconds);

            // 2. Обновление очередей производства на базах
            UpdateProductionQueues(state, deltaSeconds);
        }

        private void UpdateEconomy(MatchState state, float deltaSeconds)
        {
            // Начисление происходит пропорционально прошедшему времени
            float metalToAddP1 = 0f;
            float metalToAddP2 = 0f;

            foreach (var b in state.Buildings.Values)
            {
                if (b.Owner == PlayerId.Player1)
                    metalToAddP1 += b.MetalIncomePerSecond * deltaSeconds;
                else if (b.Owner == PlayerId.Player2)
                    metalToAddP2 += b.MetalIncomePerSecond * deltaSeconds;
            }

            state.Player1.Metal += (int)metalToAddP1;
            state.Player2.Metal += (int)metalToAddP2;
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

            var newRobot = new RobotState
            {
                Id = new EntityId(++_nextEntityId),
                Owner = building.Owner,
                Blueprint = order.Blueprint,
                Position = GetReleasePosition(building),
                RotationDegrees = building.Owner == PlayerId.Player1 ? 0f : 180f,
                MaxHealth = val.TotalHealth,
                CurrentHealth = val.TotalHealth,
                MoveSpeed = val.Speed,
                Mass = val.TotalMass,
                TotalCost = val.TotalCost
            };

            state.Robots[newRobot.Id.Value] = newRobot;
            OnRobotSpawned?.Invoke(newRobot);
        }

        private static SimVector2 GetReleasePosition(BuildingState building)
        {
            SimVector2 direction = building.SpawnPoint - building.Position;
            if (direction.SqrMagnitude < 0.001f)
            {
                direction = building.Owner == PlayerId.Player2
                    ? new SimVector2(-1f, 0f)
                    : new SimVector2(1f, 0f);
            }

            return building.SpawnPoint + direction / direction.Magnitude * SpawnExitClearance;
        }
    }
}
