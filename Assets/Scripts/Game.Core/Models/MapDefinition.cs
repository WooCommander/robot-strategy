using System;
using System.Collections.Generic;
using Game.Core.Models;
using Game.Core.Types;

namespace Game.Core.Types
{
    public enum TileType : byte
    {
        Ground = 0,    // Обычная почва (скорость 1.0)
        Road = 1,      // Дорога (скорость 1.3x)
        Mountain = 2,  // Скала (непроходимо)
        Water = 3,     // Вода (непроходимо)
        Bridge = 4     // Мост (скорость 1.1x)
    }

    public readonly struct GridCoord : IEquatable<GridCoord>
    {
        public int X { get; }
        public int Y { get; }

        public GridCoord(int x, int y)
        {
            X = x;
            Y = y;
        }

        public bool Equals(GridCoord other) => X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is GridCoord other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(X, Y);
        public override string ToString() => $"[{X}, {Y}]";

        public static bool operator ==(GridCoord a, GridCoord b) => a.Equals(b);
        public static bool operator !=(GridCoord a, GridCoord b) => !a.Equals(b);

        public SimVector2 ToSimVector2(float tileSize = 1.0f) => new SimVector2(X * tileSize + tileSize * 0.5f, Y * tileSize + tileSize * 0.5f);
        public static GridCoord FromSimVector2(SimVector2 pos, float tileSize = 1.0f) => new GridCoord((int)Math.Floor(pos.X / tileSize), (int)Math.Floor(pos.Y / tileSize));
    }
}

namespace Game.Core.Models
{
    [Serializable]
    public class BuildingSpawnDefinition
    {
        public int EntityIdValue { get; set; }
        public BuildingType Type { get; set; }
        public PlayerId InitialOwner { get; set; }
        public SimVector2 Position { get; set; }
        public SimVector2 SpawnPoint { get; set; }
        public float CaptureRadius { get; set; } = 3.0f;
        /// <summary>Что производит завод; для базы всегда General.</summary>
        public ResourceType ProducedResource { get; set; } = ResourceType.General;
    }

    [Serializable]
    public class MapDefinition
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public float TileSize { get; set; } = 1.0f;

        public TileType[] Tiles { get; set; }
        public List<BuildingSpawnDefinition> Buildings { get; set; } = new List<BuildingSpawnDefinition>();

        public TileType GetTile(int x, int y)
        {
            if (x < 0 || x >= Width || y < 0 || y >= Height || Tiles == null)
                return TileType.Mountain;
            return Tiles[y * Width + x];
        }

        public void SetTile(int x, int y, TileType type)
        {
            if (x >= 0 && x < Width && y >= 0 && y < Height && Tiles != null)
                Tiles[y * Width + x] = type;
        }
    }
}
