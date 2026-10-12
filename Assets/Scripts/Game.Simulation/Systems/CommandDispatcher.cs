using System;
using System.Collections.Generic;
using Game.Content.Definitions;
using Game.Content.Validation;
using Game.Core.Commands;
using Game.Core.Models;
using Game.Core.Types;

namespace Game.Simulation.Systems
{
    public class CommandDispatcher : ICommandDispatcher
    {
        private readonly IReadOnlyDictionary<string, ModuleDefinition> _moduleCatalog;

        public CommandDispatcher(IReadOnlyDictionary<string, ModuleDefinition> moduleCatalog)
        {
            _moduleCatalog = moduleCatalog;
        }

        public CommandResult Dispatch(ICommand command, MatchState state)
        {
            if (command == null) return CommandResult.Fail("Команда не может быть null.");
            if (state == null) return CommandResult.Fail("Состояние матча не может быть null.");
            if (state.Result != MatchResult.InProgress) return CommandResult.Fail("Матч уже завершён.");

            var player = state.GetPlayer(command.PlayerId);
            if (player == null) return CommandResult.Fail($"Игрок {command.PlayerId} не найден в матче.");

            return command switch
            {
                EnqueueProductionCommand enqueue => HandleEnqueue(enqueue, state, player),
                CancelProductionCommand cancel => HandleCancel(cancel, state, player),
                OrderMoveCommand move => HandleMove(move, state),
                OrderAttackCommand attack => HandleAttack(attack, state),
                OrderCaptureCommand capture => HandleCapture(capture, state),
                OrderDefendCommand defend => HandleDefend(defend, state),
                OrderStopCommand stop => HandleStop(stop, state),
                _ => CommandResult.Fail($"Неизвестный тип команды: {command.GetType().Name}")
            };
        }

        private CommandResult HandleEnqueue(EnqueueProductionCommand cmd, MatchState state, PlayerState player)
        {
            if (!state.Buildings.TryGetValue(cmd.BaseBuildingId.Value, out var building))
            {
                return CommandResult.Fail($"База #{cmd.BaseBuildingId.Value} не найдена.");
            }

            if (building.Owner != cmd.PlayerId)
            {
                return CommandResult.Fail("Нельзя размещать заказ на чужой базе.");
            }

            if (building.Type != BuildingType.Base)
            {
                return CommandResult.Fail("Производство возможно только на базе.");
            }

            if (building.ProductionQueue.Count >= BuildingState.MaxQueueSize)
            {
                return CommandResult.Fail($"Очередь производства заполнена (максимум {BuildingState.MaxQueueSize} заказов).");
            }

            if (!player.CanBuildMoreRobots)
            {
                return CommandResult.Fail($"Достигнут максимальный лимит армии ({PlayerState.MaxArmyLimit} роботов).");
            }

            var validation = RobotAssemblyValidator.Validate(cmd.Blueprint, _moduleCatalog);
            if (!validation.IsValid)
            {
                return CommandResult.Fail($"Сборка невалидна: {string.Join("; ", validation.Errors)}");
            }

            if (player.Metal < validation.TotalCost)
            {
                return CommandResult.Fail($"Недостаточно металла: требуется {validation.TotalCost}, доступно {player.Metal}.");
            }

            // Списание ресурсов и постановка в очередь
            player.Metal -= validation.TotalCost;
            player.QueuedRobotsCount++;

            float buildTime = Math.Max(5.0f, validation.TotalCost * 0.05f); // 5% от стоимости как базовое время сборки
            var order = new ProductionOrder
            {
                OrderId = Guid.NewGuid().ToString("N"),
                Blueprint = cmd.Blueprint,
                Cost = validation.TotalCost,
                TotalBuildTimeSeconds = buildTime,
                RemainingBuildTimeSeconds = buildTime,
                Status = OrderProductionStatus.InProgress
            };

            building.ProductionQueue.Add(order);
            return CommandResult.Success();
        }

        private CommandResult HandleCancel(CancelProductionCommand cmd, MatchState state, PlayerState player)
        {
            if (!state.Buildings.TryGetValue(cmd.BaseBuildingId.Value, out var building))
            {
                return CommandResult.Fail($"База #{cmd.BaseBuildingId.Value} не найдена.");
            }

            if (building.Owner != cmd.PlayerId)
            {
                return CommandResult.Fail("Нельзя отменять заказ на чужой базе.");
            }

            int index = building.ProductionQueue.FindIndex(o => o.OrderId == cmd.OrderId);
            if (index < 0)
            {
                return CommandResult.Fail($"Заказ '{cmd.OrderId}' не найден в очереди.");
            }

            var order = building.ProductionQueue[index];
            building.ProductionQueue.RemoveAt(index);

            // Возврат ресурсов и освобождение слота лимита
            player.Metal += order.Cost;
            player.QueuedRobotsCount = Math.Max(0, player.QueuedRobotsCount - 1);

            return CommandResult.Success();
        }

        private CommandResult HandleMove(OrderMoveCommand cmd, MatchState state)
        {
            var robots = ValidateAndGetOwnedRobots(cmd.RobotIds, cmd.PlayerId, state, out var error);
            if (error != null) return CommandResult.Fail(error);

            foreach (var robot in robots)
            {
                robot.CurrentOrder = RobotOrder.MoveTo(cmd.TargetPosition);
            }
            return CommandResult.Success();
        }

        private CommandResult HandleAttack(OrderAttackCommand cmd, MatchState state)
        {
            var robots = ValidateAndGetOwnedRobots(cmd.RobotIds, cmd.PlayerId, state, out var error);
            if (error != null) return CommandResult.Fail(error);

            if (!state.Robots.TryGetValue(cmd.TargetEntityId.Value, out var targetRobot) || !targetRobot.IsAlive)
            {
                return CommandResult.Fail("Цель атаки не существует или уже уничтожена.");
            }

            foreach (var robot in robots)
            {
                robot.CurrentOrder = RobotOrder.Attack(cmd.TargetEntityId);
            }
            return CommandResult.Success();
        }

        private CommandResult HandleCapture(OrderCaptureCommand cmd, MatchState state)
        {
            var robots = ValidateAndGetOwnedRobots(cmd.RobotIds, cmd.PlayerId, state, out var error);
            if (error != null) return CommandResult.Fail(error);

            if (!state.Buildings.ContainsKey(cmd.TargetBuildingId.Value))
            {
                return CommandResult.Fail("Целевое здание для захвата не найдено.");
            }

            foreach (var robot in robots)
            {
                robot.CurrentOrder = RobotOrder.Capture(cmd.TargetBuildingId);
            }
            return CommandResult.Success();
        }

        private CommandResult HandleDefend(OrderDefendCommand cmd, MatchState state)
        {
            var robots = ValidateAndGetOwnedRobots(cmd.RobotIds, cmd.PlayerId, state, out var error);
            if (error != null) return CommandResult.Fail(error);

            foreach (var robot in robots)
            {
                robot.CurrentOrder = RobotOrder.Defend(cmd.TargetPosition);
            }
            return CommandResult.Success();
        }

        private CommandResult HandleStop(OrderStopCommand cmd, MatchState state)
        {
            var robots = ValidateAndGetOwnedRobots(cmd.RobotIds, cmd.PlayerId, state, out var error);
            if (error != null) return CommandResult.Fail(error);

            foreach (var robot in robots)
            {
                robot.CurrentOrder = RobotOrder.Stop();
            }
            return CommandResult.Success();
        }

        private List<RobotState> ValidateAndGetOwnedRobots(List<EntityId> ids, PlayerId expectedOwner, MatchState state, out string error)
        {
            error = null;
            if (ids == null || ids.Count == 0)
            {
                error = "Список роботов пуст.";
                return null;
            }

            var result = new List<RobotState>(ids.Count);
            foreach (var id in ids)
            {
                if (!state.Robots.TryGetValue(id.Value, out var robot) || !robot.IsAlive)
                {
                    error = $"Робот #{id.Value} не найден или уничтожен.";
                    return null;
                }
                if (robot.Owner != expectedOwner)
                {
                    error = $"Команда отклонена: робот #{id.Value} принадлежит другому игроку ({robot.Owner}).";
                    return null;
                }
                result.Add(robot);
            }
            return result;
        }
    }
}
