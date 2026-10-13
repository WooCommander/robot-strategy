using System;
using Game.Core.Types;

namespace Game.Core.Models
{
    [Serializable]
    public class RobotOrder
    {
        public OrderType Type { get; set; } = OrderType.None;
        public SimVector2 TargetPosition { get; set; } = SimVector2.Zero;
        public EntityId TargetEntityId { get; set; } = EntityId.None;

        public static RobotOrder Stop() => new RobotOrder { Type = OrderType.Stop };
        public static RobotOrder MoveTo(SimVector2 pos) => new RobotOrder { Type = OrderType.Move, TargetPosition = pos };
        public static RobotOrder Attack(EntityId target) => new RobotOrder { Type = OrderType.Attack, TargetEntityId = target };
        public static RobotOrder Capture(EntityId target) => new RobotOrder { Type = OrderType.Capture, TargetEntityId = target };
        public static RobotOrder Defend(SimVector2 pos) => new RobotOrder { Type = OrderType.Defend, TargetPosition = pos };
        public static RobotOrder Hunt() => new RobotOrder { Type = OrderType.Hunt };
        public static RobotOrder Scout(SimVector2 position) => new RobotOrder { Type = OrderType.Scout, TargetPosition = position };
    }

    [Serializable]
    public class RobotState
    {
        public EntityId Id { get; set; }
        public PlayerId Owner { get; set; }
        public RobotBlueprint Blueprint { get; set; }

        public SimVector2 Position { get; set; }
        public float RotationDegrees { get; set; }

        public int MaxHealth { get; set; }
        public int CurrentHealth { get; set; }

        public float MoveSpeed { get; set; }
        public int Mass { get; set; }
        public int TotalCost { get; set; }

        public float WeaponCooldownRemaining { get; set; }
        public int ScoutPatrolStep { get; set; }
        public RobotOrder CurrentOrder { get; set; } = new RobotOrder();

        public bool IsAlive => CurrentHealth > 0;
    }

    public enum OrderProductionStatus : byte
    {
        InProgress = 0,
        ExitBlocked = 1,
        Completed = 2
    }

    [Serializable]
    public class ProductionOrder
    {
        public string OrderId { get; set; }
        public RobotBlueprint Blueprint { get; set; }
        public int Cost { get; set; }
        public float TotalBuildTimeSeconds { get; set; }
        public float RemainingBuildTimeSeconds { get; set; }
        public OrderProductionStatus Status { get; set; } = OrderProductionStatus.InProgress;

        public bool IsFinished => RemainingBuildTimeSeconds <= 0f;
    }

    [Serializable]
    public class BuildingState
    {
        public EntityId Id { get; set; }
        public BuildingType Type { get; set; }
        public PlayerId Owner { get; set; }
        public SimVector2 Position { get; set; }
        public SimVector2 SpawnPoint { get; set; }
        public float CaptureRadius { get; set; } = 3.0f;

        // Экономика
        public int MetalIncomePerSecond => Type == BuildingType.Base ? 5 : 10;

        // Захват
        public float CaptureTimeTotal => Type == BuildingType.Base ? 45.0f : 15.0f;
        public float CaptureProgressSeconds { get; set; } = 0f;
        public PlayerId CapturingPlayer { get; set; } = PlayerId.Neutral;
        public bool IsContested { get; set; } = false;

        // Очередь производства (только для баз)
        public const int MaxQueueSize = 5;
        public System.Collections.Generic.List<ProductionOrder> ProductionQueue { get; set; } 
            = new System.Collections.Generic.List<ProductionOrder>();
    }

    [Serializable]
    public class PlayerState
    {
        public PlayerId Id { get; set; }
        public int Metal { get; set; } = 600;
        public float MetalFraction { get; set; }
        public int MetalIncomePerSecond { get; set; }
        public const int MaxArmyLimit = 40;

        public int ActiveRobotsCount { get; set; }
        public int QueuedRobotsCount { get; set; }
        public int TotalArmyCount => ActiveRobotsCount + QueuedRobotsCount;
        public bool CanBuildMoreRobots => TotalArmyCount < MaxArmyLimit;
    }

    public enum MatchResult : byte
    {
        InProgress = 0,
        Player1Won = 1,
        Player2Won = 2,
        Draw = 3
    }

    [Serializable]
    public class MatchState
    {
        public int Tick { get; set; } = 0;
        public float MatchTimeSeconds { get; set; } = 0f;
        public MatchResult Result { get; set; } = MatchResult.InProgress;
        public bool IsPaused { get; set; } = false;

        public PlayerState Player1 { get; set; } = new PlayerState { Id = PlayerId.Player1 };
        public PlayerState Player2 { get; set; } = new PlayerState { Id = PlayerId.Player2 };

        public System.Collections.Generic.Dictionary<int, RobotState> Robots { get; set; } 
            = new System.Collections.Generic.Dictionary<int, RobotState>();

        public System.Collections.Generic.Dictionary<int, BuildingState> Buildings { get; set; } 
            = new System.Collections.Generic.Dictionary<int, BuildingState>();

        public PlayerState GetPlayer(PlayerId id) => id switch
        {
            PlayerId.Player1 => Player1,
            PlayerId.Player2 => Player2,
            _ => null
        };
    }
}
