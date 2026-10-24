using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Presentation.Rendering
{
    /// <summary>
    /// Собирает низкополигональную геометрию с плоским затенением. Цвет задаётся UV-координатой
    /// в текстуре-палитре, поэтому деревья, камни и мосты рисуются одним материалом и сливаются в один меш.
    /// </summary>
    public sealed class LowPolyMeshBuilder
    {
        /// <summary>
        /// Ячейки палитры. Team* и Glow зависят от стороны: у каждой стороны своя текстура палитры,
        /// поэтому смена владельца здания — это просто смена материала.
        /// </summary>
        public enum Swatch
        {
            Bark, PineDark, PineMid, PineLight, RockDark, RockLight, Bush, Concrete,
            MetalLight, MetalMid, MetalDark, Rubber, TeamAccent, TeamDark, Glow, Hazard
        }

        private const int SwatchCount = 16;
        private static readonly Color32[] SwatchColors =
        {
            new Color32(92, 64, 44, 255),     // Bark
            new Color32(38, 72, 40, 255),     // PineDark
            new Color32(52, 92, 48, 255),     // PineMid
            new Color32(76, 116, 58, 255),    // PineLight
            new Color32(98, 94, 88, 255),     // RockDark
            new Color32(138, 132, 120, 255),  // RockLight
            new Color32(70, 104, 46, 255),    // Bush
            new Color32(150, 146, 138, 255),  // Concrete
            new Color32(172, 176, 180, 255),  // MetalLight
            new Color32(112, 117, 124, 255),  // MetalMid
            new Color32(54, 58, 64, 255),     // MetalDark
            new Color32(30, 30, 32, 255),     // Rubber
            new Color32(128, 128, 128, 255),  // TeamAccent (заменяется цветом стороны)
            new Color32(72, 72, 72, 255),     // TeamDark (заменяется цветом стороны)
            new Color32(220, 230, 235, 255),  // Glow (заменяется цветом стороны)
            new Color32(226, 176, 34, 255),   // Hazard
        };

        private readonly List<Vector3> _vertices = new List<Vector3>();
        private readonly List<Vector3> _normals = new List<Vector3>();
        private readonly List<Vector2> _uvs = new List<Vector2>();
        private readonly List<int> _triangles = new List<int>();

        public int VertexCount => _vertices.Count;

        public static Texture2D CreatePaletteTexture() => CreatePaletteTexture(null);

        /// <summary>Палитра стороны: ячейки Team* и Glow окрашены в цвет команды.</summary>
        public static Texture2D CreatePaletteTexture(Color? teamColor)
        {
            var colors = (Color32[])SwatchColors.Clone();
            if (teamColor.HasValue)
            {
                Color team = teamColor.Value;
                colors[(int)Swatch.TeamAccent] = team;
                colors[(int)Swatch.TeamDark] = team * 0.5f;
                colors[(int)Swatch.Glow] = Color.Lerp(team, Color.white, 0.45f);
            }
            return CreateTexture("LowPolyPalette", colors);
        }

        /// <summary>Карта эмиссии к палитре: светится только ячейка Glow.</summary>
        public static Texture2D CreateEmissionTexture(Color teamColor)
        {
            var colors = new Color32[SwatchCount];
            for (int i = 0; i < colors.Length; i++) colors[i] = new Color32(0, 0, 0, 255);
            colors[(int)Swatch.Glow] = Color.Lerp(teamColor, Color.white, 0.3f);
            return CreateTexture("LowPolyEmission", colors);
        }

        private static Texture2D CreateTexture(string name, Color32[] colors)
        {
            var texture = new Texture2D(SwatchCount, 1, TextureFormat.RGBA32, false)
            {
                name = name, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp
            };
            texture.SetPixels32(colors);
            texture.Apply(false, true);
            return texture;
        }

        public void Triangle(Vector3 a, Vector3 b, Vector3 c, Swatch swatch)
        {
            Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
            var uv = new Vector2(((int)swatch + 0.5f) / SwatchCount, 0.5f);
            int start = _vertices.Count;
            _vertices.Add(a); _vertices.Add(b); _vertices.Add(c);
            _normals.Add(normal); _normals.Add(normal); _normals.Add(normal);
            _uvs.Add(uv); _uvs.Add(uv); _uvs.Add(uv);
            _triangles.Add(start); _triangles.Add(start + 1); _triangles.Add(start + 2);
        }

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Swatch swatch)
        {
            Triangle(a, b, c, swatch);
            Triangle(a, c, d, swatch);
        }

        /// <summary>Ель: короткий ствол и ярусы конусов, сужающиеся кверху.</summary>
        public void Pine(Matrix4x4 transform, int tiers, Swatch foliage)
        {
            Prism(transform, Vector3.zero, 0.09f, 0.35f, 4, Swatch.Bark);
            float y = 0.25f, radius = 0.55f;
            for (int tier = 0; tier < tiers; tier++)
            {
                Cone(transform, new Vector3(0f, y, 0f), radius, 0.75f, 6, tier % 2 == 0 ? foliage : Swatch.PineMid);
                y += 0.42f;
                radius *= 0.74f;
            }
        }

        /// <summary>Валун: икосаэдр, вершины которого сдвинуты шумом.</summary>
        public void Rock(Matrix4x4 transform, int seed, Swatch swatch, Swatch sideSwatch = Swatch.RockDark)
        {
            Vector3[] points = Icosahedron.Vertices;
            var displaced = new Vector3[points.Length];
            for (int i = 0; i < points.Length; i++)
                displaced[i] = points[i] * (0.75f + 0.45f * ProceduralNoise.Hash(i, seed, 77));
            int[] faces = Icosahedron.Faces;
            for (int i = 0; i < faces.Length; i += 3)
            {
                Vector3 a = transform.MultiplyPoint3x4(displaced[faces[i]]);
                Vector3 b = transform.MultiplyPoint3x4(displaced[faces[i + 1]]);
                Vector3 c = transform.MultiplyPoint3x4(displaced[faces[i + 2]]);
                // Светлые верхние грани и тёмные нижние дают камню объём даже в тени.
                Vector3 normal = Vector3.Cross(b - a, c - a);
                Triangle(a, b, c, normal.y > 0.3f * normal.magnitude ? swatch : sideSwatch);
            }
        }

        /// <summary>Ящик по центру основания (x, z) и низу y.</summary>
        public void Box(Matrix4x4 transform, Vector3 center, Vector3 size, Swatch swatch)
        {
            Vector3 h = size * 0.5f;
            Vector3 P(float x, float y, float z) => transform.MultiplyPoint3x4(center + new Vector3(x * h.x, y * h.y, z * h.z));
            Quad(P(-1, 1, -1), P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1), swatch);       // верх
            Quad(P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1), swatch);   // низ
            Quad(P(-1, -1, -1), P(-1, 1, -1), P(1, 1, -1), P(1, -1, -1), swatch);   // -z
            Quad(P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1), P(-1, -1, 1), swatch);       // +z
            Quad(P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1), P(-1, -1, -1), swatch);   // -x
            Quad(P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), P(1, -1, 1), swatch);       // +x
        }

        /// <summary>
        /// Усечённая призма: прямоугольное основание (bottom — полные размеры по x и z) сужается к верху (top),
        /// верх можно сдвинуть (topOffset) — так получаются скошенная лобовая броня и наклонные стены.
        /// </summary>
        public void Frustum(Matrix4x4 transform, Vector3 baseCenter, Vector2 bottom, Vector2 top, float height, Vector2 topOffset,
            Swatch sides, Swatch cap)
        {
            Vector3 B(float x, float z) => transform.MultiplyPoint3x4(baseCenter + new Vector3(x * bottom.x * 0.5f, 0f, z * bottom.y * 0.5f));
            Vector3 T(float x, float z) => transform.MultiplyPoint3x4(baseCenter + new Vector3(topOffset.x + x * top.x * 0.5f, height, topOffset.y + z * top.y * 0.5f));
            Quad(T(-1, -1), T(-1, 1), T(1, 1), T(1, -1), cap);              // верх
            Quad(B(-1, -1), B(1, -1), B(1, 1), B(-1, 1), sides);            // низ
            Quad(B(-1, -1), T(-1, -1), T(1, -1), B(1, -1), sides);          // -z
            Quad(B(1, 1), T(1, 1), T(-1, 1), B(-1, 1), sides);              // +z
            Quad(B(-1, 1), T(-1, 1), T(-1, -1), B(-1, -1), sides);          // -x
            Quad(B(1, -1), T(1, -1), T(1, 1), B(1, 1), sides);              // +x
        }

        public void Frustum(Matrix4x4 transform, Vector3 baseCenter, Vector2 bottom, Vector2 top, float height, Swatch swatch) =>
            Frustum(transform, baseCenter, bottom, top, height, Vector2.zero, swatch, swatch);

        /// <summary>Цилиндр или усечённый конус вдоль локальной оси Y с крышками.</summary>
        public void Cylinder(Matrix4x4 transform, Vector3 baseCenter, float radiusBottom, float radiusTop, float height, int sides,
            Swatch side, Swatch cap)
        {
            Vector3 bottomCenter = transform.MultiplyPoint3x4(baseCenter);
            Vector3 topCenter = transform.MultiplyPoint3x4(baseCenter + Vector3.up * height);
            for (int i = 0; i < sides; i++)
            {
                Vector3 a = baseCenter + Ring(i, sides, radiusBottom), b = baseCenter + Ring(i + 1, sides, radiusBottom);
                Vector3 at = baseCenter + Vector3.up * height + Ring(i, sides, radiusTop), bt = baseCenter + Vector3.up * height + Ring(i + 1, sides, radiusTop);
                Vector3 wa = transform.MultiplyPoint3x4(a), wb = transform.MultiplyPoint3x4(b);
                Vector3 wat = transform.MultiplyPoint3x4(at), wbt = transform.MultiplyPoint3x4(bt);
                Quad(wa, wat, wbt, wb, side);
                if (radiusTop > 0f) Triangle(topCenter, wbt, wat, cap);
                if (radiusBottom > 0f) Triangle(bottomCenter, wa, wb, cap);
            }
        }

        public void Cylinder(Matrix4x4 transform, Vector3 baseCenter, float radius, float height, int sides, Swatch swatch) =>
            Cylinder(transform, baseCenter, radius, radius, height, sides, swatch, swatch);

        private void Cone(Matrix4x4 transform, Vector3 baseCenter, float radius, float height, int sides, Swatch swatch)
        {
            Vector3 apex = transform.MultiplyPoint3x4(baseCenter + Vector3.up * height);
            for (int side = 0; side < sides; side++)
            {
                Vector3 a = transform.MultiplyPoint3x4(baseCenter + Ring(side, sides, radius));
                Vector3 b = transform.MultiplyPoint3x4(baseCenter + Ring(side + 1, sides, radius));
                // Донышко конуса сверху не видно, его не строим: деревьев тысячи.
                Triangle(a, apex, b, swatch);
            }
        }

        private void Prism(Matrix4x4 transform, Vector3 baseCenter, float radius, float height, int sides, Swatch swatch)
        {
            for (int side = 0; side < sides; side++)
            {
                Vector3 a = baseCenter + Ring(side, sides, radius), b = baseCenter + Ring(side + 1, sides, radius);
                Quad(transform.MultiplyPoint3x4(a), transform.MultiplyPoint3x4(a + Vector3.up * height),
                     transform.MultiplyPoint3x4(b + Vector3.up * height), transform.MultiplyPoint3x4(b), swatch);
            }
        }

        private static Vector3 Ring(int index, int sides, float radius)
        {
            float angle = index * Mathf.PI * 2f / sides;
            return new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name, indexFormat = _vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(_vertices);
            mesh.SetNormals(_normals);
            mesh.SetUVs(0, _uvs);
            mesh.SetTriangles(_triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        public void Clear()
        {
            _vertices.Clear(); _normals.Clear(); _uvs.Clear(); _triangles.Clear();
        }

        private static class Icosahedron
        {
            private const float T = 1.618034f;
            public static readonly Vector3[] Vertices =
            {
                new Vector3(-1, T, 0).normalized, new Vector3(1, T, 0).normalized, new Vector3(-1, -T, 0).normalized, new Vector3(1, -T, 0).normalized,
                new Vector3(0, -1, T).normalized, new Vector3(0, 1, T).normalized, new Vector3(0, -1, -T).normalized, new Vector3(0, 1, -T).normalized,
                new Vector3(T, 0, -1).normalized, new Vector3(T, 0, 1).normalized, new Vector3(-T, 0, -1).normalized, new Vector3(-T, 0, 1).normalized,
            };
            public static readonly int[] Faces =
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11,
                1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9,
                4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
            };
        }
    }
}
