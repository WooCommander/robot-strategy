using System;

namespace Game.Core.Types
{
    public readonly struct EntityId : IEquatable<EntityId>
    {
        public int Value { get; }

        public EntityId(int value)
        {
            Value = value;
        }

        public bool IsValid => Value > 0;
        public static readonly EntityId None = new EntityId(0);

        public bool Equals(EntityId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is EntityId other && Equals(other);
        public override int GetHashCode() => Value;
        public override string ToString() => $"Entity#{Value}";

        public static bool operator ==(EntityId left, EntityId right) => left.Equals(right);
        public static bool operator !=(EntityId left, EntityId right) => !left.Equals(right);
    }

    public enum PlayerId : byte
    {
        Neutral = 0,
        Player1 = 1,
        Player2 = 2
    }

    /// <summary>Категории деталей по образцу Nether Earth: шасси, до трёх разных орудий, электроника и ядерный заряд.</summary>
    public enum ModuleSlotType : byte
    {
        Chassis = 0,
        Weapon = 1,
        Electronics = 2,
        Nuclear = 3
    }

    /// <summary>
    /// Ресурсы стороны. General приносят базы и годится для любой детали;
    /// остальные производят заводы своего типа, и тратятся они только на детали этого типа.
    /// </summary>
    public enum ResourceType : byte
    {
        General = 0,
        Electronics = 1,
        Nuclear = 2,
        Phasers = 3,
        Missiles = 4,
        Cannon = 5,
        Chassis = 6
    }

    public enum OrderType : byte
    {
        None = 0,
        Move = 1,
        Attack = 2,
        Capture = 3,
        Defend = 4,
        Stop = 5,
        Hunt = 6,
        Scout = 7,
        Detonate = 8
    }

    public enum BuildingType : byte
    {
        Base = 0,
        Factory = 1
    }

    public enum WeaponType : byte
    {
        DirectCannon = 0,
        AreaMissile = 1,
        Phaser = 2
    }

    public readonly struct SimVector2 : IEquatable<SimVector2>
    {
        public float X { get; }
        public float Y { get; }

        public SimVector2(float x, float y)
        {
            X = x;
            Y = y;
        }

        public static readonly SimVector2 Zero = new SimVector2(0f, 0f);

        public float SqrMagnitude => X * X + Y * Y;
        public float Magnitude => (float)Math.Sqrt(SqrMagnitude);

        public static SimVector2 operator +(SimVector2 a, SimVector2 b) => new SimVector2(a.X + b.X, a.Y + b.Y);
        public static SimVector2 operator -(SimVector2 a, SimVector2 b) => new SimVector2(a.X - b.X, a.Y - b.Y);
        public static SimVector2 operator *(SimVector2 a, float d) => new SimVector2(a.X * d, a.Y * d);
        public static SimVector2 operator /(SimVector2 a, float d) => new SimVector2(a.X / d, a.Y / d);

        public static float Distance(SimVector2 a, SimVector2 b) => (a - b).Magnitude;
        public static float SqrDistance(SimVector2 a, SimVector2 b) => (a - b).SqrMagnitude;

        public bool Equals(SimVector2 other) => Math.Abs(X - other.X) < 0.0001f && Math.Abs(Y - other.Y) < 0.0001f;
        public override bool Equals(object obj) => obj is SimVector2 other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(X, Y);
        public override string ToString() => $"({X:F2}, {Y:F2})";
    }
}
