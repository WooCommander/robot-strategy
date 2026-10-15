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
            public const int CurrentVersion = 1;
            public int Version;
            public int Tick; public float MatchTimeSeconds; public MatchResult Result; public bool IsPaused;
            public PlayerSnapshot Player1; public PlayerSnapshot Player2;
            public List<RobotSnapshot> Robots = new List<RobotSnapshot>();
            public List<BuildingSnapshot> Buildings = new List<BuildingSnapshot>();

            public static MatchSnapshot FromState(MatchState state)
            {
                var snapshot = new MatchSnapshot { Version = CurrentVersion, Tick = state.Tick, MatchTimeSeconds = state.MatchTimeSeconds, Result = state.Result, IsPaused = state.IsPaused, Player1 = PlayerSnapshot.FromState(state.Player1), Player2 = PlayerSnapshot.FromState(state.Player2) };
                foreach (RobotState robot in state.Robots.Values) snapshot.Robots.Add(RobotSnapshot.FromState(robot));
                foreach (BuildingState building in state.Buildings.Values) snapshot.Buildings.Add(BuildingSnapshot.FromState(building));
                return snapshot;
            }

            public MatchState ToState()
            {
                var state = new MatchState { Tick = Tick, MatchTimeSeconds = MatchTimeSeconds, Result = Result, IsPaused = IsPaused, Player1 = Player1?.ToState() ?? new PlayerState { Id = PlayerId.Player1 }, Player2 = Player2?.ToState() ?? new PlayerState { Id = PlayerId.Player2 } };
                foreach (RobotSnapshot robot in Robots ?? new List<RobotSnapshot>()) { RobotState restored = robot.ToState(); state.Robots[restored.Id.Value] = restored; }
                foreach (BuildingSnapshot building in Buildings ?? new List<BuildingSnapshot>()) { BuildingState restored = building.ToState(); state.Buildings[restored.Id.Value] = restored; }
                return state;
            }
        }

        [Serializable]
        private sealed class PlayerSnapshot
        {
            public PlayerId Id; public int Metal; public float MetalFraction; public int MetalIncomePerSecond; public int ActiveRobotsCount; public int QueuedRobotsCount;
            public static PlayerSnapshot FromState(PlayerState state) => new PlayerSnapshot { Id = state.Id, Metal = state.Metal, MetalFraction = state.MetalFraction, MetalIncomePerSecond = state.MetalIncomePerSecond, ActiveRobotsCount = state.ActiveRobotsCount, QueuedRobotsCount = state.QueuedRobotsCount };
            public PlayerState ToState() => new PlayerState { Id = Id, Metal = Metal, MetalFraction = MetalFraction, MetalIncomePerSecond = MetalIncomePerSecond, ActiveRobotsCount = ActiveRobotsCount, QueuedRobotsCount = QueuedRobotsCount };
        }

        [Serializable]
        private sealed class RobotSnapshot
        {
            public int Id; public PlayerId Owner; public BlueprintSnapshot Blueprint; public VectorSnapshot Position; public float RotationDegrees; public int MaxHealth; public int CurrentHealth; public float MoveSpeed; public int Mass; public int TotalCost; public float WeaponCooldownRemaining; public int ScoutPatrolStep; public OrderSnapshot Order;
            public static RobotSnapshot FromState(RobotState state) => new RobotSnapshot { Id = state.Id.Value, Owner = state.Owner, Blueprint = BlueprintSnapshot.FromState(state.Blueprint), Position = VectorSnapshot.From(state.Position), RotationDegrees = state.RotationDegrees, MaxHealth = state.MaxHealth, CurrentHealth = state.CurrentHealth, MoveSpeed = state.MoveSpeed, Mass = state.Mass, TotalCost = state.TotalCost, WeaponCooldownRemaining = state.WeaponCooldownRemaining, ScoutPatrolStep = state.ScoutPatrolStep, Order = OrderSnapshot.FromState(state.CurrentOrder) };
            public RobotState ToState() => new RobotState { Id = new EntityId(Id), Owner = Owner, Blueprint = Blueprint?.ToState(), Position = Position.ToState(), RotationDegrees = RotationDegrees, MaxHealth = MaxHealth, CurrentHealth = CurrentHealth, MoveSpeed = MoveSpeed, Mass = Mass, TotalCost = TotalCost, WeaponCooldownRemaining = WeaponCooldownRemaining, ScoutPatrolStep = ScoutPatrolStep, CurrentOrder = Order?.ToState() ?? new RobotOrder() };
        }

        [Serializable]
        private sealed class BuildingSnapshot
        {
            public int Id; public BuildingType Type; public PlayerId Owner; public VectorSnapshot Position; public VectorSnapshot SpawnPoint; public float CaptureRadius; public float CaptureProgressSeconds; public PlayerId CapturingPlayer; public bool IsContested; public List<ProductionSnapshot> ProductionQueue = new List<ProductionSnapshot>();
            public static BuildingSnapshot FromState(BuildingState state)
            {
                var snapshot = new BuildingSnapshot { Id = state.Id.Value, Type = state.Type, Owner = state.Owner, Position = VectorSnapshot.From(state.Position), SpawnPoint = VectorSnapshot.From(state.SpawnPoint), CaptureRadius = state.CaptureRadius, CaptureProgressSeconds = state.CaptureProgressSeconds, CapturingPlayer = state.CapturingPlayer, IsContested = state.IsContested };
                foreach (ProductionOrder order in state.ProductionQueue) snapshot.ProductionQueue.Add(ProductionSnapshot.FromState(order));
                return snapshot;
            }
            public BuildingState ToState()
            {
                var state = new BuildingState { Id = new EntityId(Id), Type = Type, Owner = Owner, Position = Position.ToState(), SpawnPoint = SpawnPoint.ToState(), CaptureRadius = CaptureRadius, CaptureProgressSeconds = CaptureProgressSeconds, CapturingPlayer = CapturingPlayer, IsContested = IsContested };
                foreach (ProductionSnapshot order in ProductionQueue ?? new List<ProductionSnapshot>()) state.ProductionQueue.Add(order.ToState());
                return state;
            }
        }

        [Serializable]
        private sealed class ProductionSnapshot
        {
            public string OrderId; public BlueprintSnapshot Blueprint; public int Cost; public float TotalBuildTimeSeconds; public float RemainingBuildTimeSeconds; public OrderProductionStatus Status;
            public static ProductionSnapshot FromState(ProductionOrder state) => new ProductionSnapshot { OrderId = state.OrderId, Blueprint = BlueprintSnapshot.FromState(state.Blueprint), Cost = state.Cost, TotalBuildTimeSeconds = state.TotalBuildTimeSeconds, RemainingBuildTimeSeconds = state.RemainingBuildTimeSeconds, Status = state.Status };
            public ProductionOrder ToState() => new ProductionOrder { OrderId = OrderId, Blueprint = Blueprint?.ToState(), Cost = Cost, TotalBuildTimeSeconds = TotalBuildTimeSeconds, RemainingBuildTimeSeconds = RemainingBuildTimeSeconds, Status = Status };
        }

        [Serializable]
        private sealed class BlueprintSnapshot
        {
            public string Id; public string Name; public string ChassisId; public string BodyId; public string WeaponId; public string EquipmentId;
            public static BlueprintSnapshot FromState(RobotBlueprint state) => state == null ? null : new BlueprintSnapshot { Id = state.Id, Name = state.Name, ChassisId = state.ChassisId, BodyId = state.BodyId, WeaponId = state.WeaponId, EquipmentId = state.EquipmentId };
            public RobotBlueprint ToState() => new RobotBlueprint(Id, Name, ChassisId, BodyId, WeaponId, EquipmentId);
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
        }
    }
}
