using System.Collections.Generic;
using UnityEngine;
using Game.Core.Models;
using Game.Core.Types;

namespace Game.Presentation.Rendering
{
    /// <summary>
    /// Визуальный рельеф поверх логической сетки. Проходимые клетки остаются ровными (роботы стоят на них),
    /// а перепады высот целиком лежат внутри скал, воды и трещин. Вокруг карты добавляется декоративная
    /// рамка холмов, чтобы у края поля не было пустоты. Симуляция об этом рельефе не знает.
    /// </summary>
    public sealed class TerrainField
    {
        /// <summary>Ширина декоративной рамки вокруг карты в клетках.</summary>
        public const int Margin = 24;
        /// <summary>Вершин рельефа на сторону клетки.</summary>
        public const int SamplesPerTile = 2;
        public const float WaterLevel = -0.2f;

        private const int Seed = 1709;

        public enum Surface : byte { Flat, Rough, Water, Crack, Mountain, Outside }

        private readonly MapDefinition _map;
        private readonly float[] _heights;
        private readonly int[] _mountainDepth;

        public int VertexCountX { get; }
        public int VertexCountZ { get; }
        public float Step { get; }
        public float OriginX { get; }
        public float OriginZ { get; }
        public float TileSize => _map.TileSize;
        public int CellCountX => _map.Width + Margin * 2;
        public int CellCountZ => _map.Height + Margin * 2;
        public MapDefinition Map => _map;

        public TerrainField(MapDefinition map)
        {
            _map = map;
            Step = map.TileSize / SamplesPerTile;
            OriginX = OriginZ = -Margin * map.TileSize;
            VertexCountX = CellCountX * SamplesPerTile + 1;
            VertexCountZ = CellCountZ * SamplesPerTile + 1;
            _mountainDepth = BuildMountainDepth(map);
            _heights = new float[VertexCountX * VertexCountZ];
            for (int iz = 0; iz < VertexCountZ; iz++)
            {
                for (int ix = 0; ix < VertexCountX; ix++) _heights[iz * VertexCountX + ix] = ComputeHeight(ix, iz);
            }
        }

        public float GetVertexHeight(int ix, int iz) =>
            _heights[Mathf.Clamp(iz, 0, VertexCountZ - 1) * VertexCountX + Mathf.Clamp(ix, 0, VertexCountX - 1)];

        public Vector3 GetVertex(int ix, int iz) => new Vector3(OriginX + ix * Step, GetVertexHeight(ix, iz), OriginZ + iz * Step);

        /// <summary>Высота рельефа в мировой точке (билинейно между вершинами).</summary>
        public float SampleHeight(float x, float z)
        {
            float fx = (x - OriginX) / Step, fz = (z - OriginZ) / Step;
            int ix = Mathf.FloorToInt(fx), iz = Mathf.FloorToInt(fz);
            float tx = fx - ix, tz = fz - iz;
            float a = GetVertexHeight(ix, iz), b = GetVertexHeight(ix + 1, iz);
            float c = GetVertexHeight(ix, iz + 1), d = GetVertexHeight(ix + 1, iz + 1);
            return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), tz);
        }

        /// <summary>Высота, на которой стоит или парит робот: на мосту — настил, над водой — её поверхность.</summary>
        public float SampleSurface(float x, float z)
        {
            int cellX = Mathf.FloorToInt(x / TileSize), cellZ = Mathf.FloorToInt(z / TileSize);
            if (_map.GetTile(cellX, cellZ) == TileType.Bridge) return 0f;
            float height = SampleHeight(x, z);
            return GetSurface(cellX, cellZ) == Surface.Water ? Mathf.Max(height, WaterLevel) : height;
        }

        public Surface GetSurface(int cellX, int cellY)
        {
            if (cellX < 0 || cellY < 0 || cellX >= _map.Width || cellY >= _map.Height) return Surface.Outside;
            return _map.GetTile(cellX, cellY) switch
            {
                TileType.Mountain => Surface.Mountain,
                TileType.Water => Surface.Water,
                TileType.Bridge => Surface.Water,
                TileType.Crack => Surface.Crack,
                TileType.Rough => Surface.Rough,
                _ => Surface.Flat
            };
        }

        private float ComputeHeight(int ix, int iz)
        {
            float x = OriginX + ix * Step, z = OriginZ + iz * Step;
            // Вершина на границе клеток принадлежит всем соседним; на любой проходимой соседней клетке она ровная.
            int cellX = Mathf.FloorToInt(x / TileSize), cellZ = Mathf.FloorToInt(z / TileSize);
            bool onEdgeX = ix % SamplesPerTile == 0, onEdgeZ = iz % SamplesPerTile == 0;

            bool anyFlat = false, anyRough = false, anyOutside = false, anyMountain = false;
            bool allWater = true, allCrack = true;
            int depth = int.MaxValue;
            for (int dz = onEdgeZ ? -1 : 0; dz <= 0; dz++)
            {
                for (int dx = onEdgeX ? -1 : 0; dx <= 0; dx++)
                {
                    int cx = cellX + dx, cz = cellZ + dz;
                    Surface surface = GetSurface(cx, cz);
                    anyFlat |= surface == Surface.Flat;
                    anyRough |= surface == Surface.Rough;
                    anyOutside |= surface == Surface.Outside;
                    allWater &= surface == Surface.Water;
                    allCrack &= surface == Surface.Crack;
                    if (surface == Surface.Mountain)
                    {
                        anyMountain = true;
                        depth = Mathf.Min(depth, _mountainDepth[cz * _map.Width + cx]);
                    }
                }
            }

            if (anyFlat) return 0f;
            if (anyRough) return 0.04f + 0.22f * ProceduralNoise.Fbm(x * 1.7f, z * 1.7f, Seed + 1, 2);
            if (allWater) return -0.75f - 0.15f * ProceduralNoise.Value(x * 0.8f, z * 0.8f, Seed + 2);
            if (allCrack) return -0.7f - 0.1f * ProceduralNoise.Value(x * 2f, z * 2f, Seed + 3);
            if (!anyMountain && !anyOutside) return 0f; // берег воды или трещины

            float height = float.MinValue;
            if (anyMountain)
            {
                float rocky = ProceduralNoise.Fbm(x * 0.45f, z * 0.45f, Seed + 4, 3);
                height = 1.0f + 0.45f * Mathf.Min(depth - 1, 4) + 1.3f * rocky;
            }
            if (anyOutside) height = Mathf.Max(height, OutsideHeight(x, z));
            return height;
        }

        /// <summary>Холмы рамки: поднимаются от края карты и отсекают взгляд к горизонту.</summary>
        private float OutsideHeight(float x, float z)
        {
            float width = _map.Width * TileSize, height = _map.Height * TileSize;
            float dx = Mathf.Max(0f, Mathf.Max(-x, x - width));
            float dz = Mathf.Max(0f, Mathf.Max(-z, z - height));
            float distance = Mathf.Sqrt(dx * dx + dz * dz);
            float hills = ProceduralNoise.Fbm(x * 0.09f, z * 0.09f, Seed + 5, 3);
            float detail = ProceduralNoise.Fbm(x * 0.5f, z * 0.5f, Seed + 6, 2);
            return 1.0f + 0.1f * distance + 2.6f * hills + 0.5f * detail;
        }

        /// <summary>Глубина клетки внутри скалы: 1 у подножия, растёт к середине массива.</summary>
        private static int[] BuildMountainDepth(MapDefinition map)
        {
            var depth = new int[map.Width * map.Height];
            var queue = new Queue<int>();
            for (int y = 0; y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    int index = y * map.Width + x;
                    if (map.GetTile(x, y) == TileType.Mountain) { depth[index] = int.MaxValue; continue; }
                    depth[index] = 0;
                    queue.Enqueue(index);
                }
            }
            while (queue.Count > 0)
            {
                int index = queue.Dequeue();
                int x = index % map.Width, y = index / map.Width;
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= map.Width || ny >= map.Height) continue;
                        int next = ny * map.Width + nx;
                        if (depth[next] <= depth[index] + 1) continue;
                        depth[next] = depth[index] + 1;
                        queue.Enqueue(next);
                    }
                }
            }
            // Скалы без единой проходимой клетки (теоретически) считаем глубокими.
            for (int i = 0; i < depth.Length; i++) if (depth[i] == int.MaxValue) depth[i] = 5;
            return depth;
        }
    }
}
