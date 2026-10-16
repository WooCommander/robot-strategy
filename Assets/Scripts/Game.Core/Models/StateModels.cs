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
        public static RobotOrder Detonate() => new RobotOrder { Type = OrderType.Detonate };
        public static RobotOrder Program() => new RobotOrder { Type = OrderType.Program };
    }

    public enum ProgramAction : byte { None = 0, Defend = 1, Attack = 2, Capture = 3, Explore = 4 }

    [Serializable]
    public class RobotState
    {
        public EntityId Id { get; set; }
        public PlayerId Owner { get; set; }
        public RobotBlueprint Blueprint { get; set; }

        public SimVector2 Position { get; set; }
        public SimVector2 Velocity { get; set; }
        public float RotationDegrees { get; set; }

        public int MaxHealth { get; set; }
        public int CurrentHealth { get; set; }

        public float MoveSpeed { get; set; }
        public MobilityClass Mobility { get; set; }
        public int TotalCost { get; set; }

        /// <summary>Перезарядка каждого орудия; индекс совпадает с <see cref="RobotBlueprint.WeaponIds"/>.</summary>
        public System.Collections.Generic.List<float> WeaponCooldowns { get; set; } = new System.Collections.Generic.List<float>();
        public int ScoutPatrolStep { get; set; }
        public RobotOrder CurrentOrder { get; set; } = new RobotOrder();
        public int DefendPriority { get; set; } = 1;
        public int AttackPriority { get; set; } = 2;
        public int CapturePriority { get; set; } = 3;
        public float DefendRadius { get; set; } = 8f;
        public float AttackRadius { get; set; } = 12f;
        public float CaptureRadius { get; set; } = 200f;
        public int DefendMinHealthPercent { get; set; }
        public int AttackMinHealthPercent { get; set; }
        public int CaptureMinHealthPercent { get; set; }
        public EntityId DefendTargetId { get; set; } = EntityId.None;
        public EntityId AttackTargetId { get; set; } = EntityId.None;
        public EntityId CaptureTargetId { get; set; } = EntityId.None;
        public ProgramAction ActiveProgramAction { get; set; }

        // Движение принадлежит симуляции: путь A*, ручной привод и расписание боевого манёвра.
        public System.Collections.Generic.List<SimVector2> Path { get; set; } = new System.Collections.Generic.List<SimVector2>();
        public int PathIndex { get; set; }
        public SimVector2 DriveDirection { get; set; }
        public float NextManeuverSeconds { get; set; }

        public bool IsAlive => CurrentHealth > 0;
        public bool HasPath => Path != null && PathIndex < Path.Count;

        public void SetPath(System.Collections.Generic.List<SimVector2> waypoints)
        {
            Path = waypoints ?? new System.Collections.Generic.List<SimVector2>();
            PathIndex = 0;
        }

        public void ClearPath()
        {
            Path?.Clear();
            PathIndex = 0;
        }
    }

    [Serializable]
    public class ProjectileState
    {
        public EntityId SourceRobotId { get; set; }
        public EntityId TargetRobotId { get; set; }
        public PlayerId Owner { get; set; }
        public WeaponType WeaponType { get; set; }
        public int Damage { get; set; }
        public float SplashRadius { get; set; }
        public SimVector2 ImpactPosition { get; set; }
        public float RemainingTravelSeconds { get; set; }
        public bool IsGuided { get; set; }
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
        /// <summary>Сколько ресурсов каждого типа списано; при отмене возвращается ровно это.</summary>
        public int[] Payment { get; set; } = new int[ResourceStock.TypeCount];
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

        // Экономика: база даёт General, завод — ресурс своего типа
        public ResourceType ProducedResource { get; set; } = ResourceType.General;

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
        public ResourceStock Resources { get; set; } = new ResourceStock();
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

    /// <summary>0 — неизвестно, 1 — изучено, 2 — видно сейчас. Массивы принадлежат состоянию матча.</summary>
    [Serializable]
    public sealed class FogOfWarState
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public float TileSize { get; set; } = 1f;
        public byte[] Player1Cells { get; set; }
        public byte[] Player2Cells { get; set; }

        public FogOfWarState() { }

        public FogOfWarState(int width, int height, float tileSize)
        {
            Width = width;
            Height = height;
            TileSize = tileSize;
            Player1Cells = new byte[width * height];
            Player2Cells = new byte[width * height];
        }

        public byte GetCell(PlayerId player, int x, int y)
        {
            if (x < 0 || x >= Width || y < 0 || y >= Height) return 0;
            byte[] cells = player == PlayerId.Player1 ? Player1Cells : player == PlayerId.Player2 ? Player2Cells : null;
            return cells != null && cells.Length == Width * Height ? cells[y * Width + x] : (byte)0;
        }

        public bool IsVisible(PlayerId player, SimVector2 position) => GetCell(player,
            (int)Math.Floor(position.X / TileSize), (int)Math.Floor(position.Y / TileSize)) == 2;

        public bool IsExplored(PlayerId player, SimVector2 position) => GetCell(player,
            (int)Math.Floor(position.X / TileSize), (int)Math.Floor(position.Y / TileSize)) > 0;
    }

    [Serializable]
    public class MatchState
    {
        public int Tick { get; set; } = 0;
        public float MatchTimeSeconds { get; set; } = 0f;
        public MatchResult Result { get; set; } = MatchResult.InProgress;
        public bool IsPaused { get; set; } = false;
        public int NextRobotId { get; set; } = 1000;
        public int NextProductionOrderId { get; set; } = 1;
        /// <summary>Сколько игровых часов дохода уже начислено.</summary>
        public int IncomeHoursPaid { get; set; }
        public FogOfWarState FogOfWar { get; set; }

        public PlayerState Player1 { get; set; } = new PlayerState { Id = PlayerId.Player1 };
        public PlayerState Player2 { get; set; } = new PlayerState { Id = PlayerId.Player2 };

        public System.Collections.Generic.Dictionary<int, RobotState> Robots { get; set; } 
            = new System.Collections.Generic.Dictionary<int, RobotState>();

        public System.Collections.Generic.Dictionary<int, BuildingState> Buildings { get; set; } 
            = new System.Collections.Generic.Dictionary<int, BuildingState>();

        public System.Collections.Generic.List<ProjectileState> Projectiles { get; set; }
            = new System.Collections.Generic.List<ProjectileState>();

        public PlayerState GetPlayer(PlayerId id) => id switch
        {
            PlayerId.Player1 => Player1,
            PlayerId.Player2 => Player2,
            _ => null
        };
    }
}
