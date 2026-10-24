using System.Collections.Generic;
using UnityEngine;
using Game.Core.Models;
using Game.Core.Types;

namespace Game.Presentation.Rendering
{
    /// <summary>
    /// Детерминированно расставляет декор и сливает его в статичные меши по кускам карты.
    /// Внутри поля только низкие объекты (валуны, щебень, кусты), чтобы роботы не проезжали сквозь деревья,
    /// а туман войны их накрывал. Ельник растёт в декоративной рамке за краем карты.
    /// </summary>
    public static class DecorationScatter
    {
        private const int ChunkCells = 32;
        private const int Seed = 9157;

        public static void Build(TerrainField field, Material material, Transform parent)
        {
            MapDefinition map = field.Map;
            var builder = new LowPolyMeshBuilder();
            int minCell = -TerrainField.Margin;
            for (int chunkZ = minCell; chunkZ < map.Height + TerrainField.Margin; chunkZ += ChunkCells)
            {
                for (int chunkX = minCell; chunkX < map.Width + TerrainField.Margin; chunkX += ChunkCells)
                {
                    builder.Clear();
                    for (int cz = chunkZ; cz < Mathf.Min(chunkZ + ChunkCells, map.Height + TerrainField.Margin); cz++)
                    {
                        for (int cx = chunkX; cx < Mathf.Min(chunkX + ChunkCells, map.Width + TerrainField.Margin); cx++)
                            DecorateCell(field, builder, cx, cz);
                    }
                    if (builder.VertexCount == 0) continue;

                    var go = new GameObject($"Decor_{chunkX}_{chunkZ}");
                    go.transform.SetParent(parent, false);
                    go.AddComponent<MeshFilter>().sharedMesh = builder.ToMesh(go.name);
                    go.AddComponent<MeshRenderer>().sharedMaterial = material;
                }
            }
        }

        private static void DecorateCell(TerrainField field, LowPolyMeshBuilder builder, int cx, int cz)
        {
            float roll = ProceduralNoise.Hash(cx, cz, Seed);
            switch (field.GetSurface(cx, cz))
            {
                case TerrainField.Surface.Outside:
                {
                    // Ельник пятнами: густые рощи чередуются с полянами и скальными выходами.
                    float grove = Mathf.InverseLerp(0.3f, 0.65f, ProceduralNoise.Fbm(cx * 0.07f, cz * 0.07f, Seed + 1, 2));
                    if (roll < 0.08f + 0.5f * grove) AddPine(field, builder, cx, cz, 0);
                    if (ProceduralNoise.Hash(cx, cz, Seed + 2) < 0.3f * grove) AddPine(field, builder, cx, cz, 1);
                    if (ProceduralNoise.Hash(cx, cz, Seed + 3) < 0.06f) AddRock(field, builder, cx, cz, 0, 0.4f, 0.9f);
                    break;
                }
                case TerrainField.Surface.Mountain:
                    if (roll < 0.55f) AddRock(field, builder, cx, cz, 0, 0.3f, 0.5f);
                    break;
                case TerrainField.Surface.Rough:
                    if (roll < 0.7f) AddRock(field, builder, cx, cz, 0, 0.1f, 0.22f);
                    if (ProceduralNoise.Hash(cx, cz, Seed + 4) < 0.45f) AddRock(field, builder, cx, cz, 1, 0.08f, 0.16f);
                    break;
                case TerrainField.Surface.Flat:
                    if (!IsOpenGround(field.Map, cx, cz)) break;
                    if (roll < 0.05f) AddBush(field, builder, cx, cz);
                    else if (roll < 0.075f) AddRock(field, builder, cx, cz, 0, 0.1f, 0.2f);
                    break;
            }
        }

        /// <summary>Трава вдали от дорог, зданий и точек выхода роботов.</summary>
        private static bool IsOpenGround(MapDefinition map, int cx, int cz)
        {
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    TileType tile = map.GetTile(cx + dx, cz + dz);
                    if (tile == TileType.Road || tile == TileType.Bridge) return false;
                }
            }
            var center = new Vector2(cx + 0.5f, cz + 0.5f) * map.TileSize;
            foreach (BuildingSpawnDefinition building in map.Buildings)
            {
                if (Vector2.Distance(center, new Vector2(building.Position.X, building.Position.Y)) < building.CaptureRadius + 2f) return false;
                if (Vector2.Distance(center, new Vector2(building.SpawnPoint.X, building.SpawnPoint.Y)) < 3f) return false;
            }
            return true;
        }

        private static Vector3 RandomPoint(TerrainField field, int cx, int cz, int slot, float sink)
        {
            float x = (cx + 0.15f + 0.7f * ProceduralNoise.Hash(cx, cz, Seed + 10 + slot * 7)) * field.TileSize;
            float z = (cz + 0.15f + 0.7f * ProceduralNoise.Hash(cx, cz, Seed + 11 + slot * 7)) * field.TileSize;
            return new Vector3(x, field.SampleHeight(x, z) - sink, z);
        }

        private static float Yaw(int cx, int cz, int slot) => ProceduralNoise.Hash(cx, cz, Seed + 12 + slot * 7) * 360f;

        private static void AddPine(TerrainField field, LowPolyMeshBuilder builder, int cx, int cz, int slot)
        {
            float size = 0.9f + 0.8f * ProceduralNoise.Hash(cx, cz, Seed + 13 + slot);
            Vector3 position = RandomPoint(field, cx, cz, slot, 0.05f);
            int tiers = size > 1.35f ? 4 : 3;
            var foliage = ProceduralNoise.Hash(cx, cz, Seed + 14 + slot) < 0.5f ? LowPolyMeshBuilder.Swatch.PineDark : LowPolyMeshBuilder.Swatch.PineLight;
            builder.Pine(Matrix4x4.TRS(position, Quaternion.Euler(0f, Yaw(cx, cz, slot), 0f), Vector3.one * size), tiers, foliage);
        }

        private static void AddRock(TerrainField field, LowPolyMeshBuilder builder, int cx, int cz, int slot, float minSize, float maxSize)
        {
            float size = Mathf.Lerp(minSize, maxSize, ProceduralNoise.Hash(cx, cz, Seed + 15 + slot));
            // Камень наполовину утоплен в грунт и чуть приплюснут — так он «сидит» на склоне.
            Vector3 position = RandomPoint(field, cx, cz, slot, size * 0.35f);
            float tilt = (ProceduralNoise.Hash(cx, cz, Seed + 16 + slot) - 0.5f) * 30f;
            var scale = new Vector3(size * 1.15f, size * 0.75f, size);
            var swatch = ProceduralNoise.Hash(cx, cz, Seed + 17 + slot) < 0.6f ? LowPolyMeshBuilder.Swatch.RockLight : LowPolyMeshBuilder.Swatch.RockDark;
            builder.Rock(Matrix4x4.TRS(position, Quaternion.Euler(tilt, Yaw(cx, cz, slot), tilt * 0.5f), scale), cx * 31 + cz * 17 + slot, swatch);
        }

        private static void AddBush(TerrainField field, LowPolyMeshBuilder builder, int cx, int cz)
        {
            float size = 0.18f + 0.14f * ProceduralNoise.Hash(cx, cz, Seed + 18);
            Vector3 position = RandomPoint(field, cx, cz, 0, size * 0.3f);
            var scale = new Vector3(size * 1.3f, size * 0.9f, size * 1.2f);
            builder.Rock(Matrix4x4.TRS(position, Quaternion.Euler(0f, Yaw(cx, cz, 0), 0f), scale), cx * 13 + cz * 7, LowPolyMeshBuilder.Swatch.Bush, LowPolyMeshBuilder.Swatch.PineDark);
        }
    }
}
