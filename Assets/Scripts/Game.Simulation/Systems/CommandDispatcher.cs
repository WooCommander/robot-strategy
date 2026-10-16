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
                OrderHuntCommand hunt => HandleHunt(hunt, state),
                OrderScoutCommand scout => HandleScout(scout, state),
                OrderDriveCommand drive => HandleDrive(drive, state),
                OrderDetonateCommand detonate => HandleDetonate(detonate, state),
                ConfigureProgramCommand program => HandleProgram(program, state),
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

            if (!player.Resources.TryPlanPayment(validation.CostByType, out int[] payment))
            {
                return CommandResult.Fail($"Недостаточно ресурсов: не хватает {payment[(int)ResourceType.General] - player.Resources[ResourceType.General]} общих.");
            }

            // Списание ресурсов и постановка в очередь
            player.Resources.Spend(payment);
            player.QueuedRobotsCount++;

            float buildTime = GetBuildTimeSeconds(cmd.Blueprint, validation.TotalCost);
            var order = new ProductionOrder
            {
                OrderId = $"order-{state.NextProductionOrderId++}",
                Blueprint = cmd.Blueprint,
                Payment = payment,
                TotalBuildTimeSeconds = buildTime,
                RemainingBuildTimeSeconds = buildTime,
                Status = OrderProductionStatus.InProgress
            };

            building.ProductionQueue.Add(order);
            return CommandResult.Success();
        }

        private static float GetBuildTimeSeconds(RobotBlueprint blueprint, int totalCost)
        {
            if (RobotPresetCatalog.TryFind(blueprint, out var preset)) return preset.BuildTimeSeconds;

            return Math.Max(5f, totalCost * 0.6f);
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
            player.Resources.Add(order.Payment);
            player.QueuedRobotsCount = Math.Max(0, player.QueuedRobotsCount - 1);

            return CommandResult.Success();
        }

        private CommandResult HandleMove(OrderMoveCommand cmd, MatchState state)
        {
            var robots = ValidateAndGetOwnedRobots(cmd.RobotIds, cmd.PlayerId, state, out var error);
            if (error != null) return CommandResult.Fail(error);

            foreach (var robot in robots)
            {
                AssignOrder(robot, RobotOrder.MoveTo(cmd.TargetPosition));
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

            if (targetRobot.Owner == cmd.PlayerId)
            {
                return CommandResult.Fail("Нельзя атаковать своего робота.");
            }
            if (state.FogOfWar != null && !state.FogOfWar.IsVisible(cmd.PlayerId, targetRobot.Position))
                return CommandResult.Fail("Вражеский робот скрыт туманом войны.");

            foreach (var robot in robots)
            {
                AssignOrder(robot, RobotOrder.Attack(cmd.TargetEntityId));
            }
            return CommandResult.Success();
        }

        private CommandResult HandleCapture(OrderCaptureCommand cmd, MatchState state)
        {
            var robots = ValidateAndGetOwnedRobots(cmd.RobotIds, cmd.PlayerId, state, out var error);
            if (error != null) return CommandResult.Fail(error);

            BuildingState targetBuilding = null;
            if (cmd.TargetBuildingId.IsValid && !state.Buildings.TryGetValue(cmd.TargetBuildingId.Value, out targetBuilding))
            {
                return CommandResult.Fail("Целевое здание для захвата не найдено.");
            }

            if (cmd.TargetBuildingId.IsValid && targetBuilding.Owner == cmd.PlayerId)
            {
                return CommandResult.Fail("Нельзя захватывать собственное здание.");
            }
            if (cmd.TargetBuildingId.IsValid && state.FogOfWar != null &&
                !state.FogOfWar.IsExplored(cmd.PlayerId, targetBuilding.Position))
                return CommandResult.Fail("Здание ещё не разведано.");

            foreach (var robot in robots)
            {
                AssignOrder(robot, RobotOrder.Capture(cmd.TargetBuildingId));
            }
            return CommandResult.Success();
        }

        private CommandResult HandleDefend(OrderDefendCommand cmd, MatchState state)
        {
            var robots = ValidateAndGetOwnedRobots(cmd.RobotIds, cmd.PlayerId, state, out var error);
            if (error != null) return CommandResult.Fail(error);

            foreach (var robot in robots)
            {
                AssignOrder(robot, RobotOrder.Defend(cmd.TargetPosition));
            }
            return CommandResult.Success();
        }

        private CommandResult HandleStop(OrderStopCommand cmd, MatchState state)
        {
            var robots = ValidateAndGetOwnedRobots(cmd.RobotIds, cmd.PlayerId, state, out var error);
            if (error != null) return CommandResult.Fail(error);

            foreach (var robot in robots)
            {
                AssignOrder(robot, RobotOrder.Stop());
            }
            return CommandResult.Success();
        }

        private CommandResult HandleHunt(OrderHuntCommand cmd, MatchState state)
        {
            var robots = ValidateAndGetOwnedRobots(cmd.RobotIds, cmd.PlayerId, state, out var error);
            if (error != null) return CommandResult.Fail(error);
            foreach (var robot in robots) AssignOrder(robot, RobotOrder.Hunt());
            return CommandResult.Success();
        }

        private CommandResult HandleScout(OrderScoutCommand cmd, MatchState state)
        {
            var robots = ValidateAndGetOwnedRobots(cmd.RobotIds, cmd.PlayerId, state, out var error);
            if (error != null) return CommandResult.Fail(error);
            foreach (var robot in robots)
            {
                robot.ScoutPatrolStep = 0;
                AssignOrder(robot, RobotOrder.Scout(cmd.TargetPosition));
            }
            return CommandResult.Success();
        }

        private CommandResult HandleDrive(OrderDriveCommand cmd, MatchState state)
        {
            var robots = ValidateAndGetOwnedRobots(cmd.RobotIds, cmd.PlayerId, state, out var error);
            if (error != null) return CommandResult.Fail(error);

            SimVector2 direction = cmd.Direction;
            float length = direction.Magnitude;
            direction = length > 0.001f ? direction / length : SimVector2.Zero;
            foreach (var robot in robots)
            {
                AssignOrder(robot, RobotOrder.Stop());
                robot.DriveDirection = direction;
            }
            return CommandResult.Success();
        }

        private CommandResult HandleDetonate(OrderDetonateCommand cmd, MatchState state)
        {
            var robots = ValidateAndGetOwnedRobots(cmd.RobotIds, cmd.PlayerId, state, out var error);
            if (error != null) return CommandResult.Fail(error);
            foreach (var robot in robots)
            {
                if (robot.Blueprint == null || !robot.Blueprint.HasNuclear) return CommandResult.Fail($"У робота #{robot.Id.Value} нет ядерного заряда.");
            }
            // Сам взрыв выполняет CombatSystem на ближайшем тике.
            foreach (var robot in robots) AssignOrder(robot, RobotOrder.Detonate());
            return CommandResult.Success();
        }

        private CommandResult HandleProgram(ConfigureProgramCommand cmd, MatchState state)
        {
            var robots = ValidateAndGetOwnedRobots(cmd.RobotIds, cmd.PlayerId, state, out var error);
            if (error != null) return CommandResult.Fail(error);
            if (cmd.DefendPriority < 1 || cmd.DefendPriority > 3 ||
                cmd.AttackPriority < 1 || cmd.AttackPriority > 3 ||
                cmd.CapturePriority < 1 || cmd.CapturePriority > 3 ||
                cmd.DefendPriority == cmd.AttackPriority || cmd.DefendPriority == cmd.CapturePriority || cmd.AttackPriority == cmd.CapturePriority)
                return CommandResult.Fail("Приоритеты программы должны быть разными числами от 1 до 3.");
            if (!IsValidProgramRadius(cmd.DefendRadius) || !IsValidProgramRadius(cmd.AttackRadius) || !IsValidProgramRadius(cmd.CaptureRadius))
                return CommandResult.Fail("Радиус условия должен быть в пределах 1–256 м.");
            if (!IsValidHealthThreshold(cmd.DefendMinHealthPercent) || !IsValidHealthThreshold(cmd.AttackMinHealthPercent) ||
                !IsValidHealthThreshold(cmd.CaptureMinHealthPercent) || !IsValidHealthThreshold(cmd.RetreatHealthPercent))
                return CommandResult.Fail("Минимальное здоровье должно быть в пределах 0–100%." );
            if (cmd.CaptureResourceType < 0 || cmd.CaptureResourceType > (int)ResourceType.Chassis ||
                cmd.CaptureResourceBelow < 0 || cmd.CaptureResourceBelow > 100 ||
                (cmd.CaptureResourceType == 0) != (cmd.CaptureResourceBelow == 0))
                return CommandResult.Fail("Условие захвата: тип заводского ресурса и порог запаса 1–100 либо оба выключены.");
            if (cmd.DefendTargetId.IsValid &&
                (!state.Buildings.TryGetValue(cmd.DefendTargetId.Value, out BuildingState defendBuilding) || defendBuilding.Owner != cmd.PlayerId))
                return CommandResult.Fail("Цель защиты должна быть собственным зданием.");
            if (cmd.AttackTargetId.IsValid &&
                (!state.Robots.TryGetValue(cmd.AttackTargetId.Value, out RobotState attackRobot) || !attackRobot.IsAlive || attackRobot.Owner == cmd.PlayerId))
                return CommandResult.Fail("Цель атаки должна быть живым вражеским роботом.");
            if (cmd.AttackTargetId.IsValid && state.FogOfWar != null &&
                !state.FogOfWar.IsVisible(cmd.PlayerId, state.Robots[cmd.AttackTargetId.Value].Position))
                return CommandResult.Fail("Цель атаки скрыта туманом войны.");
            if (cmd.CaptureTargetId.IsValid &&
                (!state.Buildings.TryGetValue(cmd.CaptureTargetId.Value, out BuildingState captureBuilding) || captureBuilding.Owner == cmd.PlayerId))
                return CommandResult.Fail("Цель захвата должна быть чужим или нейтральным зданием.");
            if (cmd.CaptureTargetId.IsValid && state.FogOfWar != null &&
                !state.FogOfWar.IsExplored(cmd.PlayerId, state.Buildings[cmd.CaptureTargetId.Value].Position))
                return CommandResult.Fail("Цель захвата ещё не разведана.");

            foreach (var robot in robots)
            {
                robot.DefendPriority = cmd.DefendPriority;
                robot.AttackPriority = cmd.AttackPriority;
                robot.CapturePriority = cmd.CapturePriority;
                robot.DefendRadius = cmd.DefendRadius;
                robot.AttackRadius = cmd.AttackRadius;
                robot.CaptureRadius = cmd.CaptureRadius;
                robot.DefendMinHealthPercent = cmd.DefendMinHealthPercent;
                robot.AttackMinHealthPercent = cmd.AttackMinHealthPercent;
                robot.CaptureMinHealthPercent = cmd.CaptureMinHealthPercent;
                robot.RetreatHealthPercent = cmd.RetreatHealthPercent;
                robot.CaptureResourceType = cmd.CaptureResourceType;
                robot.CaptureResourceBelow = cmd.CaptureResourceBelow;
                robot.DefendTargetId = cmd.DefendTargetId;
                robot.AttackTargetId = cmd.AttackTargetId;
                robot.CaptureTargetId = cmd.CaptureTargetId;
                robot.ActiveProgramAction = ProgramAction.None;
                AssignOrder(robot, RobotOrder.Program());
            }
            return CommandResult.Success();
        }

        private static bool IsValidProgramRadius(float radius) => radius >= 1f && radius <= 256f && !float.IsNaN(radius);
        private static bool IsValidHealthThreshold(int value) => value >= 0 && value <= 100;

        /// <summary>Новый приказ отменяет старый маршрут и ручной привод; путь строит планировщик симуляции.</summary>
        private static void AssignOrder(RobotState robot, RobotOrder order)
        {
            robot.CurrentOrder = order;
            robot.ClearPath();
            robot.DriveDirection = SimVector2.Zero;
            robot.NextManeuverSeconds = 0f;
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
