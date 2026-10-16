using System;
using System.Collections.Generic;
using Game.Core.Models;
using Game.Core.Types;

namespace Game.Core.Commands
{
    [Serializable]
    public sealed class ReplayCommandLog
    {
        public List<ReplayCommandRecord> Records = new List<ReplayCommandRecord>();

        public void Record(int tick, ICommand command)
        {
            if (tick < 0) throw new ArgumentOutOfRangeException(nameof(tick));
            if (command == null) throw new ArgumentNullException(nameof(command));
            Records.Add(ReplayCommandRecord.FromCommand(tick, command));
        }

        public IEnumerable<ReplayCommandRecord> GetForTick(int tick)
        {
            foreach (ReplayCommandRecord record in Records)
            {
                if (record.Tick == tick) yield return record;
            }
        }
    }

    [Serializable]
    public sealed class ReplayCommandRecord
    {
        public int Tick;
        public CommandType Type;
        public PlayerId PlayerId;
        public int SequenceNumber;
        public List<int> RobotIds = new List<int>();
        public int TargetEntityId;
        public float TargetX;
        public float TargetY;
        public string OrderId;
        public ReplayBlueprint Blueprint;
        public int DefendPriority;
        public int AttackPriority;
        public int CapturePriority;
        public float DefendRadius;
        public float AttackRadius;
        public float CaptureRadius;
        public int DefendMinHealthPercent;
        public int AttackMinHealthPercent;
        public int CaptureMinHealthPercent;
        public int DefendTargetId;
        public int AttackTargetId;
        public int CaptureTargetId;

        public static ReplayCommandRecord FromCommand(int tick, ICommand command)
        {
            var record = new ReplayCommandRecord { Tick = tick, Type = command.Type, PlayerId = command.PlayerId, SequenceNumber = command.SequenceNumber };
            switch (command)
            {
                case OrderMoveCommand move: record.SetRobots(move.RobotIds); record.SetTarget(move.TargetPosition); break;
                case OrderAttackCommand attack: record.SetRobots(attack.RobotIds); record.TargetEntityId = attack.TargetEntityId.Value; break;
                case OrderCaptureCommand capture: record.SetRobots(capture.RobotIds); record.TargetEntityId = capture.TargetBuildingId.Value; break;
                case OrderDefendCommand defend: record.SetRobots(defend.RobotIds); record.SetTarget(defend.TargetPosition); break;
                case OrderStopCommand stop: record.SetRobots(stop.RobotIds); break;
                case OrderHuntCommand hunt: record.SetRobots(hunt.RobotIds); break;
                case OrderScoutCommand scout: record.SetRobots(scout.RobotIds); record.SetTarget(scout.TargetPosition); break;
                case OrderDriveCommand drive: record.SetRobots(drive.RobotIds); record.SetTarget(drive.Direction); break;
                case OrderDetonateCommand detonate: record.SetRobots(detonate.RobotIds); break;
                case ConfigureProgramCommand program:
                    record.SetRobots(program.RobotIds);
                    record.DefendPriority = program.DefendPriority; record.AttackPriority = program.AttackPriority; record.CapturePriority = program.CapturePriority;
                    record.DefendRadius = program.DefendRadius; record.AttackRadius = program.AttackRadius; record.CaptureRadius = program.CaptureRadius;
                    record.DefendMinHealthPercent = program.DefendMinHealthPercent; record.AttackMinHealthPercent = program.AttackMinHealthPercent; record.CaptureMinHealthPercent = program.CaptureMinHealthPercent;
                    record.DefendTargetId = program.DefendTargetId.Value; record.AttackTargetId = program.AttackTargetId.Value; record.CaptureTargetId = program.CaptureTargetId.Value;
                    break;
                case EnqueueProductionCommand enqueue: record.TargetEntityId = enqueue.BaseBuildingId.Value; record.Blueprint = ReplayBlueprint.From(enqueue.Blueprint); break;
                case CancelProductionCommand cancel: record.TargetEntityId = cancel.BaseBuildingId.Value; record.OrderId = cancel.OrderId; break;
                default: throw new NotSupportedException($"Команда {command.GetType().Name} не поддерживается в replay.");
            }
            return record;
        }

        public ICommand ToCommand()
        {
            List<EntityId> robotIds = ToEntityIds();
            return Type switch
            {
                CommandType.Move => new OrderMoveCommand { PlayerId = PlayerId, SequenceNumber = SequenceNumber, RobotIds = robotIds, TargetPosition = TargetPosition },
                CommandType.Attack => new OrderAttackCommand { PlayerId = PlayerId, SequenceNumber = SequenceNumber, RobotIds = robotIds, TargetEntityId = new EntityId(TargetEntityId) },
                CommandType.Capture => new OrderCaptureCommand { PlayerId = PlayerId, SequenceNumber = SequenceNumber, RobotIds = robotIds, TargetBuildingId = new EntityId(TargetEntityId) },
                CommandType.Defend => new OrderDefendCommand { PlayerId = PlayerId, SequenceNumber = SequenceNumber, RobotIds = robotIds, TargetPosition = TargetPosition },
                CommandType.Stop => new OrderStopCommand { PlayerId = PlayerId, SequenceNumber = SequenceNumber, RobotIds = robotIds },
                CommandType.Hunt => new OrderHuntCommand { PlayerId = PlayerId, SequenceNumber = SequenceNumber, RobotIds = robotIds },
                CommandType.Scout => new OrderScoutCommand { PlayerId = PlayerId, SequenceNumber = SequenceNumber, RobotIds = robotIds, TargetPosition = TargetPosition },
                CommandType.Detonate => new OrderDetonateCommand { PlayerId = PlayerId, SequenceNumber = SequenceNumber, RobotIds = robotIds },
                CommandType.Drive => new OrderDriveCommand { PlayerId = PlayerId, SequenceNumber = SequenceNumber, RobotIds = robotIds, Direction = TargetPosition },
                CommandType.ConfigureProgram => new ConfigureProgramCommand { PlayerId = PlayerId, SequenceNumber = SequenceNumber, RobotIds = robotIds,
                    DefendPriority = DefendPriority, AttackPriority = AttackPriority, CapturePriority = CapturePriority,
                    DefendRadius = DefendRadius > 0f ? DefendRadius : 8f, AttackRadius = AttackRadius > 0f ? AttackRadius : 12f, CaptureRadius = CaptureRadius > 0f ? CaptureRadius : 200f,
                    DefendMinHealthPercent = DefendMinHealthPercent, AttackMinHealthPercent = AttackMinHealthPercent, CaptureMinHealthPercent = CaptureMinHealthPercent,
                    DefendTargetId = new EntityId(DefendTargetId), AttackTargetId = new EntityId(AttackTargetId), CaptureTargetId = new EntityId(CaptureTargetId) },
                CommandType.EnqueueProduction => new EnqueueProductionCommand { PlayerId = PlayerId, SequenceNumber = SequenceNumber, BaseBuildingId = new EntityId(TargetEntityId), Blueprint = Blueprint?.ToBlueprint() },
                CommandType.CancelProduction => new CancelProductionCommand { PlayerId = PlayerId, SequenceNumber = SequenceNumber, BaseBuildingId = new EntityId(TargetEntityId), OrderId = OrderId },
                _ => throw new NotSupportedException($"Тип команды {Type} не поддерживается в replay.")
            };
        }

        private SimVector2 TargetPosition => new SimVector2(TargetX, TargetY);
        private void SetTarget(SimVector2 target) { TargetX = target.X; TargetY = target.Y; }
        private void SetRobots(List<EntityId> ids)
        {
            RobotIds.Clear();
            if (ids == null) return;
            foreach (EntityId id in ids) RobotIds.Add(id.Value);
        }
        private List<EntityId> ToEntityIds()
        {
            var ids = new List<EntityId>();
            foreach (int id in RobotIds ?? new List<int>()) ids.Add(new EntityId(id));
            return ids;
        }
    }

    [Serializable]
    public sealed class ReplayBlueprint
    {
        public string Id; public string Name; public string ChassisId; public List<string> WeaponIds = new List<string>(); public string ElectronicsId; public string NuclearId;
        public static ReplayBlueprint From(RobotBlueprint blueprint) => blueprint == null ? null : new ReplayBlueprint { Id = blueprint.Id, Name = blueprint.Name, ChassisId = blueprint.ChassisId, WeaponIds = new List<string>(blueprint.WeaponIds ?? new List<string>()), ElectronicsId = blueprint.ElectronicsId, NuclearId = blueprint.NuclearId };
        public RobotBlueprint ToBlueprint() => new RobotBlueprint(Id, Name, ChassisId, WeaponIds, ElectronicsId, NuclearId);
    }
}
