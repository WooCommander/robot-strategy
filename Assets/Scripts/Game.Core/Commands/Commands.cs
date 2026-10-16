using System;
using System.Collections.Generic;
using Game.Core.Models;
using Game.Core.Types;

namespace Game.Core.Commands
{
    public enum CommandType : byte
    {
        Move = 1,
        Attack = 2,
        Capture = 3,
        Defend = 4,
        Stop = 5,
        Hunt = 6,
        Scout = 7,
        Drive = 8,
        Detonate = 9,
        EnqueueProduction = 10,
        CancelProduction = 11,
        ConfigureProgram = 12
    }

    public interface ICommand
    {
        PlayerId PlayerId { get; }
        int SequenceNumber { get; }
        CommandType Type { get; }
    }

    public readonly struct CommandResult
    {
        public bool IsSuccess { get; }
        public string ErrorReason { get; }

        private CommandResult(bool isSuccess, string errorReason)
        {
            IsSuccess = isSuccess;
            ErrorReason = errorReason;
        }

        public static CommandResult Success() => new CommandResult(true, null);
        public static CommandResult Fail(string reason) => new CommandResult(false, reason);
    }

    public interface ICommandDispatcher
    {
        CommandResult Dispatch(ICommand command, MatchState state);
    }

    [Serializable]
    public class OrderMoveCommand : ICommand
    {
        public PlayerId PlayerId { get; set; }
        public int SequenceNumber { get; set; }
        public CommandType Type => CommandType.Move;

        public List<EntityId> RobotIds { get; set; } = new List<EntityId>();
        public SimVector2 TargetPosition { get; set; }
    }

    [Serializable]
    public class OrderAttackCommand : ICommand
    {
        public PlayerId PlayerId { get; set; }
        public int SequenceNumber { get; set; }
        public CommandType Type => CommandType.Attack;

        public List<EntityId> RobotIds { get; set; } = new List<EntityId>();
        public EntityId TargetEntityId { get; set; }
    }

    [Serializable]
    public class OrderCaptureCommand : ICommand
    {
        public PlayerId PlayerId { get; set; }
        public int SequenceNumber { get; set; }
        public CommandType Type => CommandType.Capture;

        public List<EntityId> RobotIds { get; set; } = new List<EntityId>();
        public EntityId TargetBuildingId { get; set; }
    }

    [Serializable]
    public class OrderDefendCommand : ICommand
    {
        public PlayerId PlayerId { get; set; }
        public int SequenceNumber { get; set; }
        public CommandType Type => CommandType.Defend;

        public List<EntityId> RobotIds { get; set; } = new List<EntityId>();
        public SimVector2 TargetPosition { get; set; }
    }

    [Serializable]
    public class OrderStopCommand : ICommand
    {
        public PlayerId PlayerId { get; set; }
        public int SequenceNumber { get; set; }
        public CommandType Type => CommandType.Stop;

        public List<EntityId> RobotIds { get; set; } = new List<EntityId>();
    }

    [Serializable]
    public class OrderHuntCommand : ICommand
    {
        public PlayerId PlayerId { get; set; }
        public int SequenceNumber { get; set; }
        public CommandType Type => CommandType.Hunt;
        public List<EntityId> RobotIds { get; set; } = new List<EntityId>();
    }

    [Serializable]
    public class OrderScoutCommand : ICommand
    {
        public PlayerId PlayerId { get; set; }
        public int SequenceNumber { get; set; }
        public CommandType Type => CommandType.Scout;
        public List<EntityId> RobotIds { get; set; } = new List<EntityId>();
        public SimVector2 TargetPosition { get; set; }
    }

    /// <summary>Подрыв ядерного заряда робота: уничтожает всех роботов в радиусе взрыва, включая самого носителя.</summary>
    [Serializable]
    public class OrderDetonateCommand : ICommand
    {
        public PlayerId PlayerId { get; set; }
        public int SequenceNumber { get; set; }
        public CommandType Type => CommandType.Detonate;
        public List<EntityId> RobotIds { get; set; } = new List<EntityId>();
    }

    /// <summary>Ручное управление: робот едет в направлении, пока не придёт новое направление или приказ.</summary>
    [Serializable]
    public class OrderDriveCommand : ICommand
    {
        public PlayerId PlayerId { get; set; }
        public int SequenceNumber { get; set; }
        public CommandType Type => CommandType.Drive;
        public List<EntityId> RobotIds { get; set; } = new List<EntityId>();
        public SimVector2 Direction { get; set; }
    }

    [Serializable]
    public class ConfigureProgramCommand : ICommand
    {
        public PlayerId PlayerId { get; set; }
        public int SequenceNumber { get; set; }
        public CommandType Type => CommandType.ConfigureProgram;
        public List<EntityId> RobotIds { get; set; } = new List<EntityId>();
        public int DefendPriority { get; set; } = 1;
        public int AttackPriority { get; set; } = 2;
        public int CapturePriority { get; set; } = 3;
        public float DefendRadius { get; set; } = 8f;
        public float AttackRadius { get; set; } = 12f;
        public float CaptureRadius { get; set; } = 200f;
        public int DefendMinHealthPercent { get; set; }
        public int AttackMinHealthPercent { get; set; }
        public int CaptureMinHealthPercent { get; set; }
        public int RetreatHealthPercent { get; set; }
        public int CaptureResourceType { get; set; }
        public int CaptureResourceBelow { get; set; }
        public EntityId DefendTargetId { get; set; } = EntityId.None;
        public EntityId AttackTargetId { get; set; } = EntityId.None;
        public EntityId CaptureTargetId { get; set; } = EntityId.None;
    }

    [Serializable]
    public class EnqueueProductionCommand : ICommand
    {
        public PlayerId PlayerId { get; set; }
        public int SequenceNumber { get; set; }
        public CommandType Type => CommandType.EnqueueProduction;

        public EntityId BaseBuildingId { get; set; }
        public RobotBlueprint Blueprint { get; set; }
    }

    [Serializable]
    public class CancelProductionCommand : ICommand
    {
        public PlayerId PlayerId { get; set; }
        public int SequenceNumber { get; set; }
        public CommandType Type => CommandType.CancelProduction;

        public EntityId BaseBuildingId { get; set; }
        public string OrderId { get; set; }
    }
}
