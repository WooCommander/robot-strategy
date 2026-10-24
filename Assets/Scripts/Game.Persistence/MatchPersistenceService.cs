using System;
using System.Collections.Generic;
using System.IO;
using Game.Core.Models;
using Game.Core.Types;
using UnityEngine;
using EntityId = Game.Core.Types.EntityId;

namespace Game.Persistence
{
    public sealed class MatchPersistenceService
    {
        public const int ManualSlotCount = 3;
        private readonly string _storageDirectory;

        public MatchPersistenceService(string storageDirectory = null)
        {
            _storageDirectory = storageDirectory ?? Path.Combine(Application.persistentDataPath, "saves");
        }

        public string Serialize(MatchState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            return JsonUtility.ToJson(MatchSnapshot.FromState(state));
        }

        public MatchState Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("JSON снимок матча пуст.", nameof(json));
            MatchSnapshot snapshot = JsonUtility.FromJson<MatchSnapshot>(json);
            if (snapshot == null || snapshot.Version != MatchSnapshot.CurrentVersion) throw new InvalidDataException("Неподдерживаемый или повреждённый снимок матча.");
            return snapshot.ToState();
        }

        public void SaveManual(int slot, MatchState state) => Save(GetManualPath(slot), state);
        public MatchState LoadManual(int slot) => Load(GetManualPath(slot));
        public bool HasManualSave(int slot) => File.Exists(GetManualPath(slot));
        public void SaveAuto(MatchState state) => Save(Path.Combine(_storageDirectory, "autosave.json"), state);
        public MatchState LoadAuto() => Load(Path.Combine(_storageDirectory, "autosave.json"));
        public bool HasAutoSave() => File.Exists(Path.Combine(_storageDirectory, "autosave.json"));

        private void Save(string path, MatchState state)
        {
            Directory.CreateDirectory(_storageDirectory);
            File.WriteAllText(path, Serialize(state));
        }

        private MatchState Load(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("Снимок матча не найден.", path);
            return Deserialize(File.ReadAllText(path));
        }

        private string GetManualPath(int slot)
        {
            if (slot < 1 || slot > ManualSlotCount) throw new ArgumentOutOfRangeException(nameof(slot), "Доступны ручные слоты 1–3.");
            return Path.Combine(_storageDirectory, $"slot-{slot}.json");
        }

        [Serializable]
        private sealed class MatchSnapshot
        {
            public const int CurrentVersion = 2;
            public int Version;
            public int Tick; public float MatchTimeSeconds; public MatchResult Result; public bool IsPaused; public int NextRobotId; public int NextProductionOrderId; public int IncomeHoursPaid;
            public PlayerSnapshot Player1; public PlayerSnapshot Player2;
            public FogSnapshot Fog;
            // Хранится как «выключен», чтобы старые сохранения без поля загружались с туманом.
            public bool FogDisabled;
            public List<RobotSnapshot> Robots = new List<RobotSnapshot>();
            public List<BuildingSnapshot> Buildings = new List<BuildingSnapshot>();
            public List<ProjectileSnapshot> Projectiles = new List<ProjectileSnapshot>();

            public static MatchSnapshot FromState(MatchState state)
            {
                var snapshot = new MatchSnapshot { Version = CurrentVersion, Tick = state.Tick, MatchTimeSeconds = state.MatchTimeSeconds, Result = state.Result, IsPaused = state.IsPaused, NextRobotId = state.NextRobotId, NextProductionOrderId = state.NextProductionOrderId, IncomeHoursPaid = state.IncomeHoursPaid, Player1 = PlayerSnapshot.FromState(state.Player1), Player2 = PlayerSnapshot.FromState(state.Player2), Fog = FogSnapshot.FromState(state.FogOfWar), FogDisabled = state.FogOfWarDisabled };
                foreach (RobotState robot in state.Robots.Values) snapshot.Robots.Add(RobotSnapshot.FromState(robot));
                foreach (BuildingState building in state.Buildings.Values) snapshot.Buildings.Add(BuildingSnapshot.FromState(building));
                foreach (ProjectileState projectile in state.Projectiles) snapshot.Projectiles.Add(ProjectileSnapshot.FromState(projectile));
                return snapshot;
            }

            public MatchState ToState()
            {
                var state = new MatchState { Tick = Tick, MatchTimeSeconds = MatchTimeSeconds, IncomeHoursPaid = IncomeHoursPaid, Result = Result, IsPaused = IsPaused, Player1 = Player1?.ToState() ?? new PlayerState { Id = PlayerId.Player1 }, Player2 = Player2?.ToState() ?? new PlayerState { Id = PlayerId.Player2 }, FogOfWar = FogDisabled ? null : Fog?.ToState(), FogOfWarDisabled = FogDisabled };
                foreach (RobotSnapshot robot in Robots ?? new List<RobotSnapshot>()) { RobotState restored = robot.ToState(); state.Robots[restored.Id.Value] = restored; }
                foreach (BuildingSnapshot building in Buildings ?? new List<BuildingSnapshot>()) { BuildingState restored = building.ToState(); state.Buildings[restored.Id.Value] = restored; }
                foreach (ProjectileSnapshot projectile in Projectiles ?? new List<ProjectileSnapshot>()) state.Projectiles.Add(projectile.ToState());
                // Старые снимки не хранили счётчики: продолжаем после максимального ID, чтобы не было коллизий.
                foreach (int robotId in state.Robots.Keys) state.NextRobotId = Math.Max(state.NextRobotId, robotId);
                if (NextRobotId > 0) state.NextRobotId = Math.Max(state.NextRobotId, NextRobotId);
                if (NextProductionOrderId > 0) state.NextProductionOrderId = NextProductionOrderId;
                return state;
            }
        }

        [Serializable]
        private sealed class FogSnapshot
        {
            public int Width; public int Height; public float TileSize;
            public byte[] Player1Cells; public byte[] Player2Cells;
            public List<LastSeenEnemySnapshot> Player1LastSeenEnemies = new List<LastSeenEnemySnapshot>();
            public List<LastSeenEnemySnapshot> Player2LastSeenEnemies = new List<LastSeenEnemySnapshot>();

            public static FogSnapshot FromState(FogOfWarState state)
            {
                if (state == null) return null;
                var snapshot = new FogSnapshot
                {
                    Width = state.Width, Height = state.Height, TileSize = state.TileSize,
                    Player1Cells = (byte[])state.Player1Cells.Clone(), Player2Cells = (byte[])state.Player2Cells.Clone()
                };
                foreach (LastSeenEnemyState enemy in state.Player1LastSeenEnemies) snapshot.Player1LastSeenEnemies.Add(LastSeenEnemySnapshot.FromState(enemy));
                foreach (LastSeenEnemyState enemy in state.Player2LastSeenEnemies) snapshot.Player2LastSeenEnemies.Add(LastSeenEnemySnapshot.FromState(enemy));
                return snapshot;
            }

            public FogOfWarState ToState()
            {
                var state = new FogOfWarState
                {
                    Width = Width, Height = Height, TileSize = TileSize,
                    Player1Cells = Player1Cells != null ? (byte[])Player1Cells.Clone() : null,
                    Player2Cells = Player2Cells != null ? (byte[])Player2Cells.Clone() : null
                };
                foreach (LastSeenEnemySnapshot enemy in Player1LastSeenEnemies ?? new List<LastSeenEnemySnapshot>()) state.Player1LastSeenEnemies.Add(enemy.ToState());
                foreach (LastSeenEnemySnapshot enemy in Player2LastSeenEnemies ?? new List<LastSeenEnemySnapshot>()) state.Player2LastSeenEnemies.Add(enemy.ToState());
                return state;
            }
        }

        [Serializable]
        private sealed class LastSeenEnemySnapshot
        {
            public int RobotId; public VectorSnapshot Position; public int SeenTick;
            public static LastSeenEnemySnapshot FromState(LastSeenEnemyState state) => new LastSeenEnemySnapshot
            {
                RobotId = state.RobotId.Value, Position = VectorSnapshot.From(state.Position), SeenTick = state.SeenTick
            };
            public LastSeenEnemyState ToState() => new LastSeenEnemyState
            {
                RobotId = new EntityId(RobotId), Position = Position.ToState(), SeenTick = SeenTick
            };
        }

        [Serializable]
        private sealed class PlayerSnapshot
        {
            public PlayerId Id; public int[] Resources; public int ActiveRobotsCount; public int QueuedRobotsCount;
            public static PlayerSnapshot FromState(PlayerState state) => new PlayerSnapshot { Id = state.Id, Resources = state.Resources.ToArray(), ActiveRobotsCount = state.ActiveRobotsCount, QueuedRobotsCount = state.QueuedRobotsCount };
            public PlayerState ToState() => new PlayerState { Id = Id, Resources = ResourceStock.FromArray(Resources), ActiveRobotsCount = ActiveRobotsCount, QueuedRobotsCount = QueuedRobotsCount };
        }

        [Serializable]
        private sealed class RobotSnapshot
        {
            public int Id; public PlayerId Owner; public BlueprintSnapshot Blueprint; public VectorSnapshot Position; public VectorSnapshot Velocity; public float RotationDegrees; public int MaxHealth; public int CurrentHealth; public float MoveSpeed; public MobilityClass Mobility; public int TotalCost; public List<float> WeaponCooldowns = new List<float>(); public int ScoutPatrolStep; public OrderSnapshot Order; public List<VectorSnapshot> Path = new List<VectorSnapshot>(); public int PathIndex; public VectorSnapshot DriveDirection; public float NextManeuverSeconds;
            public int DefendPriority; public int AttackPriority; public int CapturePriority; public ProgramAction ActiveProgramAction;
            public float DefendRadius; public float AttackRadius; public float CaptureRadius;
            public int DefendMinHealthPercent; public int AttackMinHealthPercent; public int CaptureMinHealthPercent;
            public int RetreatHealthPercent;
            public int CaptureResourceType; public int CaptureResourceBelow;
            public int DefendTargetId; public int AttackTargetId; public int CaptureTargetId;
            public static RobotSnapshot FromState(RobotState state) => new RobotSnapshot { Id = state.Id.Value, Owner = state.Owner, Blueprint = BlueprintSnapshot.FromState(state.Blueprint), Position = VectorSnapshot.From(state.Position), Velocity = VectorSnapshot.From(state.Velocity), RotationDegrees = state.RotationDegrees, MaxHealth = state.MaxHealth, CurrentHealth = state.CurrentHealth, MoveSpeed = state.MoveSpeed, Mobility = state.Mobility, TotalCost = state.TotalCost, WeaponCooldowns = new List<float>(state.WeaponCooldowns ?? new List<float>()), ScoutPatrolStep = state.ScoutPatrolStep, Order = OrderSnapshot.FromState(state.CurrentOrder), Path = VectorSnapshot.FromList(state.Path), PathIndex = state.PathIndex, DriveDirection = VectorSnapshot.From(state.DriveDirection), NextManeuverSeconds = state.NextManeuverSeconds, DefendPriority = state.DefendPriority, AttackPriority = state.AttackPriority, CapturePriority = state.CapturePriority, ActiveProgramAction = state.ActiveProgramAction,
                DefendRadius = state.DefendRadius, AttackRadius = state.AttackRadius, CaptureRadius = state.CaptureRadius,
                DefendMinHealthPercent = state.DefendMinHealthPercent, AttackMinHealthPercent = state.AttackMinHealthPercent, CaptureMinHealthPercent = state.CaptureMinHealthPercent,
                RetreatHealthPercent = state.RetreatHealthPercent,
                CaptureResourceType = state.CaptureResourceType, CaptureResourceBelow = state.CaptureResourceBelow,
                DefendTargetId = state.DefendTargetId.Value, AttackTargetId = state.AttackTargetId.Value, CaptureTargetId = state.CaptureTargetId.Value };
            public RobotState ToState() => new RobotState { Id = new EntityId(Id), Owner = Owner, Blueprint = Blueprint?.ToState(), Position = Position.ToState(), Velocity = Velocity?.ToState() ?? SimVector2.Zero, RotationDegrees = RotationDegrees, MaxHealth = MaxHealth, CurrentHealth = CurrentHealth, MoveSpeed = MoveSpeed, Mobility = Mobility, TotalCost = TotalCost, WeaponCooldowns = new List<float>(WeaponCooldowns ?? new List<float>()), ScoutPatrolStep = ScoutPatrolStep, CurrentOrder = Order?.ToState() ?? new RobotOrder(), Path = VectorSnapshot.ToList(Path), PathIndex = PathIndex, DriveDirection = DriveDirection?.ToState() ?? SimVector2.Zero, NextManeuverSeconds = NextManeuverSeconds, DefendPriority = DefendPriority == 0 ? 1 : DefendPriority, AttackPriority = AttackPriority == 0 ? 2 : AttackPriority, CapturePriority = CapturePriority == 0 ? 3 : CapturePriority, ActiveProgramAction = ActiveProgramAction,
                DefendRadius = DefendRadius > 0f ? DefendRadius : 8f, AttackRadius = AttackRadius > 0f ? AttackRadius : 12f, CaptureRadius = CaptureRadius > 0f ? CaptureRadius : 200f,
                DefendMinHealthPercent = DefendMinHealthPercent, AttackMinHealthPercent = AttackMinHealthPercent, CaptureMinHealthPercent = CaptureMinHealthPercent,
                RetreatHealthPercent = RetreatHealthPercent,
                CaptureResourceType = CaptureResourceType, CaptureResourceBelow = CaptureResourceBelow,
                DefendTargetId = new EntityId(DefendTargetId), AttackTargetId = new EntityId(AttackTargetId), CaptureTargetId = new EntityId(CaptureTargetId) };
        }

        [Serializable]
        private sealed class BuildingSnapshot
        {
            public int Id; public BuildingType Type; public PlayerId Owner; public VectorSnapshot Position; public VectorSnapshot SpawnPoint; public float CaptureRadius; public ResourceType ProducedResource; public float CaptureProgressSeconds; public PlayerId CapturingPlayer; public bool IsContested; public List<ProductionSnapshot> ProductionQueue = new List<ProductionSnapshot>();
            public static BuildingSnapshot FromState(BuildingState state)
            {
                var snapshot = new BuildingSnapshot { Id = state.Id.Value, Type = state.Type, Owner = state.Owner, Position = VectorSnapshot.From(state.Position), SpawnPoint = VectorSnapshot.From(state.SpawnPoint), CaptureRadius = state.CaptureRadius, ProducedResource = state.ProducedResource, CaptureProgressSeconds = state.CaptureProgressSeconds, CapturingPlayer = state.CapturingPlayer, IsContested = state.IsContested };
                foreach (ProductionOrder order in state.ProductionQueue) snapshot.ProductionQueue.Add(ProductionSnapshot.FromState(order));
                return snapshot;
            }
            public BuildingState ToState()
            {
                var state = new BuildingState { Id = new EntityId(Id), Type = Type, Owner = Owner, Position = Position.ToState(), SpawnPoint = SpawnPoint.ToState(), CaptureRadius = CaptureRadius, ProducedResource = ProducedResource, CaptureProgressSeconds = CaptureProgressSeconds, CapturingPlayer = CapturingPlayer, IsContested = IsContested };
                foreach (ProductionSnapshot order in ProductionQueue ?? new List<ProductionSnapshot>()) state.ProductionQueue.Add(order.ToState());
                return state;
            }
        }

        [Serializable]
        private sealed class ProjectileSnapshot
        {
            public int SourceRobotId; public int TargetRobotId; public PlayerId Owner; public WeaponType WeaponType; public int Damage; public float SplashRadius; public VectorSnapshot ImpactPosition; public float RemainingTravelSeconds; public bool IsGuided;
            public static ProjectileSnapshot FromState(ProjectileState state) => new ProjectileSnapshot { SourceRobotId = state.SourceRobotId.Value, TargetRobotId = state.TargetRobotId.Value, Owner = state.Owner, WeaponType = state.WeaponType, Damage = state.Damage, SplashRadius = state.SplashRadius, ImpactPosition = VectorSnapshot.From(state.ImpactPosition), RemainingTravelSeconds = state.RemainingTravelSeconds, IsGuided = state.IsGuided };
            public ProjectileState ToState() => new ProjectileState { SourceRobotId = new EntityId(SourceRobotId), TargetRobotId = new EntityId(TargetRobotId), Owner = Owner, WeaponType = WeaponType, Damage = Damage, SplashRadius = SplashRadius, ImpactPosition = ImpactPosition.ToState(), RemainingTravelSeconds = RemainingTravelSeconds, IsGuided = IsGuided };
        }

        [Serializable]
        private sealed class ProductionSnapshot
        {
            public string OrderId; public BlueprintSnapshot Blueprint; public int[] Payment; public float TotalBuildTimeSeconds; public float RemainingBuildTimeSeconds; public OrderProductionStatus Status;
            public static ProductionSnapshot FromState(ProductionOrder state) => new ProductionSnapshot { OrderId = state.OrderId, Blueprint = BlueprintSnapshot.FromState(state.Blueprint), Payment = (int[])(state.Payment ?? new int[ResourceStock.TypeCount]).Clone(), TotalBuildTimeSeconds = state.TotalBuildTimeSeconds, RemainingBuildTimeSeconds = state.RemainingBuildTimeSeconds, Status = state.Status };
            public ProductionOrder ToState() => new ProductionOrder { OrderId = OrderId, Blueprint = Blueprint?.ToState(), Payment = Payment ?? new int[ResourceStock.TypeCount], TotalBuildTimeSeconds = TotalBuildTimeSeconds, RemainingBuildTimeSeconds = RemainingBuildTimeSeconds, Status = Status };
        }

        [Serializable]
        private sealed class BlueprintSnapshot
        {
            public string Id; public string Name; public string ChassisId; public List<string> WeaponIds = new List<string>(); public string ElectronicsId; public string NuclearId;
            public static BlueprintSnapshot FromState(RobotBlueprint state) => state == null ? null : new BlueprintSnapshot { Id = state.Id, Name = state.Name, ChassisId = state.ChassisId, WeaponIds = new List<string>(state.WeaponIds ?? new List<string>()), ElectronicsId = state.ElectronicsId, NuclearId = state.NuclearId };
            public RobotBlueprint ToState() => new RobotBlueprint(Id, Name, ChassisId, WeaponIds, ElectronicsId, NuclearId);
        }

        [Serializable]
        private sealed class OrderSnapshot
        {
            public OrderType Type; public VectorSnapshot TargetPosition; public int TargetEntityId;
            public static OrderSnapshot FromState(RobotOrder state) => state == null ? null : new OrderSnapshot { Type = state.Type, TargetPosition = VectorSnapshot.From(state.TargetPosition), TargetEntityId = state.TargetEntityId.Value };
            public RobotOrder ToState() => new RobotOrder { Type = Type, TargetPosition = TargetPosition.ToState(), TargetEntityId = new EntityId(TargetEntityId) };
        }

        [Serializable]
        private sealed class VectorSnapshot
        {
            public float X; public float Y;
            public static VectorSnapshot From(SimVector2 value) => new VectorSnapshot { X = value.X, Y = value.Y };
            public SimVector2 ToState() => new SimVector2(X, Y);
            public static List<VectorSnapshot> FromList(List<SimVector2> values)
            {
                var result = new List<VectorSnapshot>();
                if (values != null) foreach (SimVector2 value in values) result.Add(From(value));
                return result;
            }
            public static List<SimVector2> ToList(List<VectorSnapshot> values)
            {
                var result = new List<SimVector2>();
                if (values != null) foreach (VectorSnapshot value in values) result.Add(value.ToState());
                return result;
            }
        }
    }
}
