using UnityEngine;
using Game.Core.Types;

namespace Game.Presentation.Rendering
{
    /// <summary>
    /// Текстуры поля боя, генерируемые при загрузке карты: цветовая карта рельефа с мягкими переходами
    /// между типами поверхности и бесшовные карты деталей/нормалей для мелкого рисунка вблизи.
    /// </summary>
    public static class TerrainTextures
    {
        private const int Seed = 4211;

        private enum Paint : byte { Grass, Road, Rock, Bed, Crack, Rough, Forest }

        private static readonly Color Sand = new Color(0.62f, 0.56f, 0.40f);

        public static Texture2D BuildAlbedo(TerrainField field, int pixelsPerTile = 4)
        {
            int cellsX = field.CellCountX, cellsZ = field.CellCountZ;
            Paint[] paint = BuildPaint(field);
            float[] shore = BuildShore(paint, cellsX, cellsZ);

            int width = cellsX * pixelsPerTile, height = cellsZ * pixelsPerTile;
            var pixels = new Color32[width * height];
            float tile = field.TileSize;
            const float slopeProbe = 0.35f;

            for (int py = 0; py < height; py++)
            {
                for (int px = 0; px < width; px++)
                {
                    float wx = field.OriginX + (px + 0.5f) / pixelsPerTile * tile;
                    float wz = field.OriginZ + (py + 0.5f) / pixelsPerTile * tile;

                    // Сдвиг шумом делает границы клеток извилистыми вместо ступенчатых.
                    float warpX = wx + (ProceduralNoise.Value(wx * 0.7f, wz * 0.7f, Seed) - 0.5f) * 0.7f;
                    float warpZ = wz + (ProceduralNoise.Value(wx * 0.7f + 31f, wz * 0.7f + 17f, Seed) - 0.5f) * 0.7f;
                    float u = (warpX - field.OriginX) / tile - 0.5f, v = (warpZ - field.OriginZ) / tile - 0.5f;
                    int i0 = Mathf.Clamp(Mathf.FloorToInt(u), 0, cellsX - 2), j0 = Mathf.Clamp(Mathf.FloorToInt(v), 0, cellsZ - 2);
                    float tu = Mathf.Clamp01(u - i0), tv = Mathf.Clamp01(v - j0);

                    float n1 = ProceduralNoise.Fbm(wx * 0.08f, wz * 0.08f, Seed + 10, 2);
                    float n2 = ProceduralNoise.Value(wx * 2.3f, wz * 2.3f, Seed + 11);

                    Paint p00 = paint[j0 * cellsX + i0], p10 = paint[j0 * cellsX + i0 + 1];
                    Paint p01 = paint[(j0 + 1) * cellsX + i0], p11 = paint[(j0 + 1) * cellsX + i0 + 1];
                    Color color;
                    float bedWeight;
                    if (p00 == p10 && p00 == p01 && p00 == p11)
                    {
                        color = PaintColor(p00, wx, wz, n1, n2);
                        bedWeight = p00 == Paint.Bed ? 1f : 0f;
                    }
                    else
                    {
                        float w00 = (1f - tu) * (1f - tv), w10 = tu * (1f - tv), w01 = (1f - tu) * tv, w11 = tu * tv;
                        color = PaintColor(p00, wx, wz, n1, n2) * w00 + PaintColor(p10, wx, wz, n1, n2) * w10
                              + PaintColor(p01, wx, wz, n1, n2) * w01 + PaintColor(p11, wx, wz, n1, n2) * w11;
                        bedWeight = (p00 == Paint.Bed ? w00 : 0f) + (p10 == Paint.Bed ? w10 : 0f)
                                  + (p01 == Paint.Bed ? w01 : 0f) + (p11 == Paint.Bed ? w11 : 0f);
                    }

                    float shoreAmount = Mathf.Lerp(Mathf.Lerp(shore[j0 * cellsX + i0], shore[j0 * cellsX + i0 + 1], tu),
                                                   Mathf.Lerp(shore[(j0 + 1) * cellsX + i0], shore[(j0 + 1) * cellsX + i0 + 1], tu), tv);
                    color = Color.Lerp(color, Sand * (0.9f + 0.2f * n2), shoreAmount * (1f - bedWeight) * 0.6f);

                    // Крутые склоны — голый камень, где бы они ни были: скалы, холмы рамки, стенки трещин.
                    float dhx = field.SampleHeight(wx + slopeProbe, wz) - field.SampleHeight(wx - slopeProbe, wz);
                    float dhz = field.SampleHeight(wx, wz + slopeProbe) - field.SampleHeight(wx, wz - slopeProbe);
                    float slope = Mathf.Sqrt(dhx * dhx + dhz * dhz) / (2f * slopeProbe);
                    color = Color.Lerp(color, PaintColor(Paint.Rock, wx, wz, n1, n2), Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.9f, 1.8f, slope)));

                    // Глубина под водой темнее: русло читается даже сквозь полупрозрачную воду.
                    float h = field.SampleHeight(wx, wz);
                    if (h < TerrainField.WaterLevel) color *= Mathf.Lerp(1f, 0.55f, Mathf.Clamp01((TerrainField.WaterLevel - h) / 0.6f));

                    color.a = 1f;
                    pixels[py * width + px] = color;
                }
            }

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, true)
            {
                name = "TerrainAlbedo", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4
            };
            texture.SetPixels32(pixels);
            texture.Apply(true, true);
            return texture;
        }

        /// <summary>Бесшовная серая карта деталей около 0.5: режим ×2 URP оставляет средний тон без изменений.</summary>
        public static Texture2D BuildDetailAlbedo(int size = 128, int period = 8)
        {
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float n = ProceduralNoise.Fbm((float)x / size * period, (float)y / size * period, Seed + 20, 3, period);
                    byte value = (byte)Mathf.Clamp(Mathf.RoundToInt((0.5f + (n - 0.5f) * 0.45f) * 255f), 0, 255);
                    pixels[y * size + x] = new Color32(value, value, value, 255);
                }
            }
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "TerrainDetail", wrapMode = TextureWrapMode.Repeat, anisoLevel = 4 };
            texture.SetPixels32(pixels);
            texture.Apply(true, true);
            return texture;
        }

        /// <summary>
        /// Бесшовная карта нормалей из шума. Упаковка (x, y, z, 1) читается и как RGB, и как AG-формат,
        /// поэтому подходит и мобильным, и настольным платформам.
        /// </summary>
        public static Texture2D BuildNormalMap(string name, int size, int period, float strength, int seed)
        {
            var heights = new float[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                    heights[y * size + x] = ProceduralNoise.Fbm((float)x / size * period, (float)y / size * period, seed, 3, period);
            }

            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = heights[y * size + (x + 1) % size] - heights[y * size + (x - 1 + size) % size];
                    float dy = heights[((y + 1) % size) * size + x] - heights[((y - 1 + size) % size) * size + x];
                    Vector3 normal = new Vector3(-dx * strength, -dy * strength, 1f).normalized;
                    pixels[y * size + x] = new Color32(ToByte(normal.x), ToByte(normal.y), ToByte(normal.z), 255);
                }
            }
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true, true) { name = name, wrapMode = TextureWrapMode.Repeat, anisoLevel = 4 };
            texture.SetPixels32(pixels);
            texture.Apply(true, true);
            return texture;
        }

        private static byte ToByte(float component) => (byte)Mathf.Clamp(Mathf.RoundToInt((component * 0.5f + 0.5f) * 255f), 0, 255);

        private static Paint[] BuildPaint(TerrainField field)
        {
            int cellsX = field.CellCountX, cellsZ = field.CellCountZ;
            var paint = new Paint[cellsX * cellsZ];
            for (int j = 0; j < cellsZ; j++)
            {
                for (int i = 0; i < cellsX; i++)
                {
                    int cellX = i - TerrainField.Margin, cellZ = j - TerrainField.Margin;
                    Paint value = field.GetSurface(cellX, cellZ) switch
                    {
                        TerrainField.Surface.Outside => Paint.Forest,
                        TerrainField.Surface.Mountain => Paint.Rock,
                        TerrainField.Surface.Water => Paint.Bed,
                        TerrainField.Surface.Crack => Paint.Crack,
                        TerrainField.Surface.Rough => Paint.Rough,
                        _ => field.Map.GetTile(cellX, cellZ) == TileType.Road ? Paint.Road : Paint.Grass
                    };
                    paint[j * cellsX + i] = value;
                }
            }
            return paint;
        }

        /// <summary>Доля воды в окрестности 3×3 клеток: из неё рисуется узкая песчаная полоса берега.</summary>
        private static float[] BuildShore(Paint[] paint, int cellsX, int cellsZ)
        {
            var shore = new float[paint.Length];
            for (int j = 0; j < cellsZ; j++)
            {
                for (int i = 0; i < cellsX; i++)
                {
                    int water = 0, total = 0;
                    for (int dj = -1; dj <= 1; dj++)
                    {
                        for (int di = -1; di <= 1; di++)
                        {
                            int x = i + di, z = j + dj;
                            if (x < 0 || z < 0 || x >= cellsX || z >= cellsZ) continue;
                            total++;
                            if (paint[z * cellsX + x] == Paint.Bed) water++;
                        }
                    }
                    shore[j * cellsX + i] = Mathf.Clamp01(water * 2f / total);
                }
            }
            return shore;
        }

        private static Color PaintColor(Paint paint, float wx, float wz, float n1, float n2)
        {
            switch (paint)
            {
                case Paint.Road:
                    return Color.Lerp(new Color(0.47f, 0.40f, 0.29f), new Color(0.57f, 0.50f, 0.37f), n2);
                case Paint.Rock:
                    return Color.Lerp(new Color(0.36f, 0.35f, 0.33f), new Color(0.6f, 0.57f, 0.52f), n2 * 0.7f + n1 * 0.3f);
                case Paint.Bed:
                    return new Color(0.30f, 0.27f, 0.19f) * (0.85f + 0.3f * n2);
                case Paint.Crack:
                    return new Color(0.15f, 0.12f, 0.09f) * (0.85f + 0.3f * n2);
                case Paint.Rough:
                    return Color.Lerp(new Color(0.42f, 0.36f, 0.25f), new Color(0.56f, 0.49f, 0.35f), n2);
                case Paint.Forest:
                    return Color.Lerp(new Color(0.16f, 0.27f, 0.11f), new Color(0.28f, 0.40f, 0.17f), n1) * (0.9f + 0.2f * n2);
                default:
                {
                    Color grass = Color.Lerp(new Color(0.24f, 0.33f, 0.15f), new Color(0.41f, 0.47f, 0.25f), n1);
                    // Небольшие проплешины земли: реже и бледнее, чтобы поле не казалось вытоптанным.
                    float dirt = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.7f, 0.8f, ProceduralNoise.Value(wx * 0.3f, wz * 0.3f, Seed + 12)));
                    return Color.Lerp(grass, new Color(0.43f, 0.37f, 0.25f), dirt * 0.55f) * (0.9f + 0.2f * n2);
                }
            }
        }
    }
}
