using System.Collections.Generic;
using UnityEngine;
using Game.Core.Types;
using Game.Presentation.Rendering;
using Swatch = Game.Presentation.Rendering.LowPolyMeshBuilder.Swatch;

namespace Game.Presentation.Views
{
    /// <summary>
    /// Низкополигональные модели базы и завода по эскизам пользователя (img-models, картинки 5 и 6).
    /// Фасад смотрит в +z; цвет стороны берётся из материала (<see cref="TeamPalette"/>), поэтому меш один
    /// на тип здания, а захват меняет только материал. Корпуса вписаны в непроходимые круги симуляции
    /// (база ~2,35, завод ~1,65), плита основания — декор.
    /// </summary>
    public static class BuildingModelBuilder
    {
        private static readonly Dictionary<BuildingType, Mesh> MeshCache = new Dictionary<BuildingType, Mesh>();
        private static readonly Matrix4x4 I = Matrix4x4.identity;
        private static readonly Quaternion AlongX = Quaternion.Euler(0f, 0f, -90f);
        private static readonly Quaternion AlongZ = Quaternion.Euler(90f, 0f, 0f);

        public static Mesh GetMesh(BuildingType type)
        {
            if (MeshCache.TryGetValue(type, out Mesh cached) && cached != null) return cached;
            var builder = new LowPolyMeshBuilder();
            if (type == BuildingType.Base) BuildBase(builder);
            else BuildFactory(builder);
            Mesh mesh = builder.ToMesh("Building_" + type);
            MeshCache[type] = mesh;
            return mesh;
        }

        private static Matrix4x4 At(Vector3 position, Quaternion rotation) => Matrix4x4.TRS(position, rotation, Vector3.one);

        // ───────────────────────── База ─────────────────────────

        private static void BuildBase(LowPolyMeshBuilder b)
        {
            b.ChamferBox(I, new Vector3(0f, 0.06f, 0f), new Vector3(4.3f, 0.12f, 4.0f), 0.04f, Swatch.MetalDark, Swatch.MetalMid);

            // Центральный ангар с вертолётной площадкой на крыше.
            var hangar = new Vector3(0f, 0.72f, -0.1f);
            const float roof = 1.32f;
            b.ChamferBox(I, hangar, new Vector3(2.0f, 1.2f, 1.9f), 0.08f, Swatch.ArmorGrey, Swatch.ArmorWhite);
            b.ChamferBox(I, new Vector3(0f, roof + 0.02f, -0.1f), new Vector3(1.6f, 0.04f, 1.5f), 0.015f, Swatch.MetalDark);
            b.Cylinder(At(new Vector3(0f, roof + 0.04f, -0.1f), Quaternion.Euler(0f, 22.5f, 0f)), Vector3.zero, 0.58f, 0.01f, 8, Swatch.ArmorWhite);
            b.Cylinder(At(new Vector3(0f, roof + 0.05f, -0.1f), Quaternion.Euler(0f, 22.5f, 0f)), Vector3.zero, 0.44f, 0.005f, 8, Swatch.MetalDark);
            foreach (float angle in new[] { 45f, -45f })
                b.Box(At(new Vector3(0f, roof + 0.06f, -0.1f), Quaternion.Euler(0f, angle, 0f)), Vector3.zero, new Vector3(0.1f, 0.01f, 0.5f), Swatch.ArmorWhite);
            foreach (float x in new[] { -0.72f, 0.72f })
            {
                foreach (float z in new[] { -0.78f, 0.58f })
                {
                    b.Box(I, new Vector3(x, roof + 0.045f, z), new Vector3(0.18f, 0.01f, 0.06f), Swatch.TeamAccent);
                    b.Box(I, new Vector3(x * 1.08f, roof + 0.05f, z + (z > 0f ? 0.1f : -0.1f)), new Vector3(0.05f, 0.04f, 0.05f), Swatch.Lamp);
                }
            }

            // Портал ворот: тёмный проём, рама со стойками в жёлто-чёрную полосу, свет над воротами, пандус.
            const float face = 0.85f;
            b.Box(I, new Vector3(0f, 0.5f, face + 0.01f), new Vector3(0.9f, 0.72f, 0.04f), Swatch.Grille);
            b.ChamferBox(I, new Vector3(0f, 0.95f, face + 0.05f), new Vector3(1.24f, 0.16f, 0.16f), 0.03f, Swatch.ArmorGrey, Swatch.ArmorWhite);
            foreach (float side in new[] { -1f, 1f })
            {
                b.ChamferBox(I, new Vector3(side * 0.55f, 0.5f, face + 0.05f), new Vector3(0.16f, 0.78f, 0.16f), 0.03f, Swatch.ArmorGrey, Swatch.ArmorWhite);
                for (int stripe = 0; stripe < 3; stripe++)
                    b.Box(I, new Vector3(side * 0.55f, 0.24f + stripe * 0.2f, face + 0.135f), new Vector3(0.12f, 0.06f, 0.006f), Swatch.Hazard);
                b.Box(I, new Vector3(side * 0.38f, 0.5f, face + 0.04f), new Vector3(0.03f, 0.3f, 0.01f), Swatch.Glow);
            }
            b.Box(I, new Vector3(0f, 1.08f, face + 0.03f), new Vector3(0.44f, 0.05f, 0.02f), Swatch.Glow);
            b.Box(I, new Vector3(0f, 0.13f, face + 0.42f), new Vector3(0.9f, 0.03f, 0.7f), Swatch.Steel);
            foreach (float side in new[] { -1f, 1f })
                b.Box(I, new Vector3(side * 0.43f, 0.15f, face + 0.42f), new Vector3(0.04f, 0.01f, 0.7f), Swatch.Hazard);

            // Эмблемы стороны на фасаде по бокам от ворот.
            foreach (float side in new[] { -1f, 1f })
            {
                var emblem = new Vector3(side * 0.8f, 0.72f, face + 0.005f);
                b.ChamferBox(I, emblem, new Vector3(0.22f, 0.3f, 0.02f), 0.008f, Swatch.TeamAccent);
                b.Chevron(emblem + new Vector3(0f, 0f, 0.01f), Vector3.forward, Vector3.down, 0.14f, Swatch.ArmorWhite);
            }

            BuildBaseLeftWing(b);
            BuildBaseTowers(b, roof);
        }

        /// <summary>Левое крыло: низкий блок с вентиляторами на крыше и лежачим баком спереди.</summary>
        private static void BuildBaseLeftWing(LowPolyMeshBuilder b)
        {
            b.ChamferBox(I, new Vector3(-1.5f, 0.45f, 0.05f), new Vector3(0.9f, 0.66f, 2.4f), 0.05f, Swatch.ArmorGrey, Swatch.ArmorWhite);
            foreach (float z in new[] { -0.55f, -0.05f })
            {
                b.Cylinder(I, new Vector3(-1.5f, 0.78f, z), 0.2f, 0.04f, 12, Swatch.Steel);
                b.Cylinder(I, new Vector3(-1.5f, 0.8f, z), 0.16f, 0.02f, 12, Swatch.Grille);
                b.Box(At(new Vector3(-1.5f, 0.825f, z), Quaternion.Euler(0f, 30f, 0f)), Vector3.zero, new Vector3(0.3f, 0.01f, 0.05f), Swatch.MetalDark);
            }
            b.Box(I, new Vector3(-1.5f, 0.6f, 1.255f), new Vector3(0.3f, 0.08f, 0.01f), Swatch.Glow);

            b.Cylinder(At(new Vector3(-1.85f, 0.32f, 1.55f), AlongX), Vector3.zero, 0.19f, 0.7f, 10, Swatch.ArmorWhite);
            b.Cylinder(At(new Vector3(-1.62f, 0.32f, 1.55f), AlongX), Vector3.zero, 0.195f, 0.06f, 10, Swatch.TeamAccent);
            foreach (float x in new[] { -1.75f, -1.25f })
                b.ChamferBox(I, new Vector3(x, 0.17f, 1.55f), new Vector3(0.08f, 0.1f, 0.3f), 0.02f, Swatch.MetalDark);

            // Антенна с огнём на ангаре и перила вдоль края плиты.
            b.Cylinder(I, new Vector3(-0.85f, 1.32f, -0.85f), 0.03f, 0.55f, 6, Swatch.MetalDark);
            b.Box(I, new Vector3(-0.85f, 1.9f, -0.85f), new Vector3(0.05f, 0.06f, 0.05f), Swatch.Lamp);
            for (int post = 0; post < 5; post++)
                b.Box(I, new Vector3(-2.05f, 0.24f, -1.4f + post * 0.5f), new Vector3(0.025f, 0.24f, 0.025f), Swatch.Hazard);
            b.Box(I, new Vector3(-2.05f, 0.34f, -0.4f), new Vector3(0.02f, 0.02f, 2.0f), Swatch.Hazard);
        }

        /// <summary>Правая сторона: диспетчерская башня с остеклением и баннером, передняя башенка, трубы, ящики.</summary>
        private static void BuildBaseTowers(LowPolyMeshBuilder b, float roof)
        {
            var tower = new Vector3(1.38f, 0.12f, -0.65f);
            b.ChamferBox(I, tower + new Vector3(0f, 0.95f, 0f), new Vector3(0.75f, 1.9f, 0.75f), 0.06f, Swatch.ArmorGrey, Swatch.ArmorWhite);
            b.ChamferBox(I, tower + new Vector3(0f, 2.06f, 0f), new Vector3(0.88f, 0.3f, 0.88f), 0.05f, Swatch.ArmorGrey, Swatch.ArmorWhite);
            b.Box(I, tower + new Vector3(0f, 2.07f, 0f), new Vector3(0.9f, 0.11f, 0.9f), Swatch.Glow);
            b.ChamferBox(I, tower + new Vector3(0f, 2.24f, 0f), new Vector3(0.68f, 0.06f, 0.68f), 0.02f, Swatch.MetalDark);
            b.Cylinder(I, tower + new Vector3(0.1f, 2.27f, 0.05f), 0.035f, 0.55f, 6, Swatch.MetalDark);
            b.Box(I, tower + new Vector3(0.1f, 2.84f, 0.05f), new Vector3(0.06f, 0.07f, 0.06f), Swatch.Lamp);
            var banner = tower + new Vector3(0f, 1.15f, 0.385f);
            b.ChamferBox(I, banner, new Vector3(0.4f, 0.66f, 0.02f), 0.01f, Swatch.TeamAccent);
            b.Chevron(banner + new Vector3(0f, 0.05f, 0.012f), Vector3.forward, Vector3.down, 0.2f, Swatch.ArmorWhite);

            var pillar = new Vector3(1.62f, 0.12f, 0.95f);
            b.ChamferBox(I, pillar + new Vector3(0f, 0.62f, 0f), new Vector3(0.5f, 1.24f, 0.5f), 0.05f, Swatch.ArmorGrey, Swatch.ArmorWhite);
            var pillarBanner = pillar + new Vector3(0f, 0.7f, 0.26f);
            b.ChamferBox(I, pillarBanner, new Vector3(0.3f, 0.5f, 0.02f), 0.01f, Swatch.TeamAccent);
            b.Chevron(pillarBanner + new Vector3(0f, 0.04f, 0.012f), Vector3.forward, Vector3.down, 0.16f, Swatch.ArmorWhite);
            b.Cylinder(I, pillar + new Vector3(0f, 1.24f, 0f), 0.035f, 0.5f, 6, Swatch.MetalDark);
            b.Box(I, pillar + new Vector3(0f, 1.76f, 0f), new Vector3(0.06f, 0.07f, 0.06f), Swatch.Lamp);

            // Трубы по краю крыши ангара в башню.
            foreach (float z in new[] { -0.32f, -0.48f })
            {
                b.Cylinder(At(new Vector3(0.72f, roof + 0.08f, z), AlongX), Vector3.zero, 0.06f, 0.3f, 8, Swatch.Steel);
                b.Cylinder(At(new Vector3(0.78f, roof + 0.08f, z), AlongX), Vector3.zero, 0.075f, 0.04f, 8, Swatch.MetalDark);
            }

            // Ящики у фасада.
            b.ChamferBox(I, new Vector3(1.05f, 0.24f, 1.55f), new Vector3(0.36f, 0.24f, 0.3f), 0.03f, Swatch.ArmorGrey, Swatch.ArmorWhite);
            b.Box(I, new Vector3(1.05f, 0.24f, 1.705f), new Vector3(0.36f, 0.04f, 0.006f), Swatch.TeamAccent);
            b.ChamferBox(I, new Vector3(1.1f, 0.44f, 1.5f), new Vector3(0.24f, 0.16f, 0.22f), 0.025f, Swatch.MetalDark);
        }

        // ───────────────────────── Завод ─────────────────────────

        private static void BuildFactory(LowPolyMeshBuilder b)
        {
            b.ChamferBox(I, new Vector3(0f, 0.05f, 0f), new Vector3(3.4f, 0.1f, 3.2f), 0.035f, Swatch.MetalDark, Swatch.MetalMid);

            // Цех с эмблемой стороны на крыше.
            const float roof = 1.0f;
            b.ChamferBox(I, new Vector3(0f, 0.55f, -0.2f), new Vector3(1.6f, 0.9f, 1.6f), 0.07f, Swatch.ArmorGrey, Swatch.ArmorWhite);
            b.ChamferBox(I, new Vector3(0f, roof + 0.015f, -0.2f), new Vector3(1.2f, 0.03f, 1.1f), 0.01f, Swatch.MetalDark);
            b.ChamferBox(I, new Vector3(0f, roof + 0.03f, -0.2f), new Vector3(0.9f, 0.01f, 0.8f), 0.005f, Swatch.ArmorWhite);
            // Завод развёрнут фасадом к камере, поэтому вершина «▽» смотрит к фасаду (+z модели).
            b.Chevron(new Vector3(0f, roof + 0.03f, -0.2f), Vector3.up, Vector3.forward, 0.55f, Swatch.TeamAccent);
            foreach (float side in new[] { -1f, 1f })
                b.Box(I, new Vector3(side * 0.805f, 0.62f, -0.2f), new Vector3(0.012f, 0.12f, 1.3f), Swatch.TeamAccent);

            // Ворота со светом внутри и конвейер с фермами наружу.
            const float face = 0.6f;
            b.Box(I, new Vector3(0f, 0.42f, face + 0.01f), new Vector3(0.8f, 0.62f, 0.04f), Swatch.Grille);
            b.Box(I, new Vector3(0f, 0.6f, face + 0.02f), new Vector3(0.6f, 0.04f, 0.01f), Swatch.Glow);
            b.ChamferBox(I, new Vector3(0f, 0.84f, face + 0.07f), new Vector3(1.16f, 0.14f, 0.16f), 0.03f, Swatch.ArmorGrey, Swatch.ArmorWhite);
            foreach (float side in new[] { -1f, 1f })
            {
                b.ChamferBox(I, new Vector3(side * 0.5f, 0.45f, face + 0.07f), new Vector3(0.16f, 0.78f, 0.16f), 0.03f, Swatch.ArmorGrey, Swatch.ArmorWhite);
                b.Box(I, new Vector3(side * 0.5f, 0.45f, face + 0.153f), new Vector3(0.05f, 0.6f, 0.006f), Swatch.TeamAccent);
                b.Box(I, new Vector3(side * 0.22f, 0.78f, face + 0.155f), new Vector3(0.1f, 0.03f, 0.006f), Swatch.Glow);
            }
            b.Box(I, new Vector3(0f, 0.13f, face + 0.55f), new Vector3(0.6f, 0.05f, 0.9f), Swatch.Steel);
            for (int roller = 0; roller < 8; roller++)
                b.Box(I, new Vector3(0f, 0.16f, face + 0.17f + roller * 0.11f), new Vector3(0.56f, 0.012f, 0.04f), Swatch.MetalDark);
            foreach (float side in new[] { -1f, 1f })
            {
                var truss = new Vector3(side * 0.42f, 0.1f, face + 0.75f);
                b.ChamferBox(I, truss + new Vector3(0f, 0.32f, 0f), new Vector3(0.1f, 0.64f, 0.1f), 0.02f, Swatch.MetalDark);
                foreach (float angle in new[] { 35f, -35f })
                    b.Box(At(truss + new Vector3(side * 0.06f, 0.3f, 0f), Quaternion.Euler(angle, 0f, 0f)), Vector3.zero, new Vector3(0.015f, 0.62f, 0.02f), Swatch.Hazard);
            }
            b.ChamferBox(I, new Vector3(0f, 0.72f, face + 0.75f), new Vector3(0.94f, 0.08f, 0.12f), 0.02f, Swatch.MetalDark);

            BuildFactoryTowers(b);
        }

        /// <summary>Две башни с антеннами (правая с баннером стороны), резервуар, генераторный блок, трубы.</summary>
        private static void BuildFactoryTowers(LowPolyMeshBuilder b)
        {
            var left = new Vector3(-1.08f, 0.1f, -0.85f);
            b.ChamferBox(I, left + new Vector3(0f, 0.72f, 0f), new Vector3(0.5f, 1.44f, 0.5f), 0.05f, Swatch.ArmorGrey, Swatch.ArmorWhite);
            b.Box(I, left + new Vector3(0f, 1.0f, 0.255f), new Vector3(0.06f, 0.6f, 0.006f), Swatch.TeamAccent);
            b.Box(I, left + new Vector3(0.12f, 1.25f, 0.256f), new Vector3(0.1f, 0.04f, 0.006f), Swatch.Glow);
            foreach (float x in new[] { -0.08f, 0.08f })
                b.Cylinder(I, left + new Vector3(x, 1.44f, 0f), 0.025f, x < 0f ? 0.5f : 0.36f, 6, Swatch.MetalDark);

            var right = new Vector3(1.08f, 0.1f, -0.75f);
            b.ChamferBox(I, right + new Vector3(0f, 0.85f, 0f), new Vector3(0.55f, 1.7f, 0.5f), 0.05f, Swatch.ArmorGrey, Swatch.ArmorWhite);
            var banner = right + new Vector3(0f, 1.0f, 0.26f);
            b.ChamferBox(I, banner, new Vector3(0.32f, 0.6f, 0.02f), 0.01f, Swatch.TeamAccent);
            b.Chevron(banner + new Vector3(0f, 0.04f, 0.012f), Vector3.forward, Vector3.down, 0.2f, Swatch.ArmorWhite);
            b.Cylinder(I, right + new Vector3(0.1f, 1.7f, 0f), 0.03f, 0.55f, 6, Swatch.MetalDark);
            b.Box(I, right + new Vector3(0.1f, 2.27f, 0f), new Vector3(0.05f, 0.06f, 0.05f), Swatch.Lamp);
            b.Cylinder(I, right + new Vector3(-0.12f, 1.7f, -0.1f), 0.02f, 0.35f, 6, Swatch.MetalDark);

            // Резервуар с поясами и куполом, трубы к цеху.
            var tank = new Vector3(1.22f, 0.1f, 0.55f);
            b.Cylinder(I, tank, 0.3f, 0.3f, 0.8f, 12, Swatch.ArmorWhite, Swatch.ArmorGrey);
            foreach (float band in new[] { 0.2f, 0.58f })
                b.Cylinder(I, tank + new Vector3(0f, band, 0f), 0.305f, 0.06f, 12, Swatch.TeamAccent);
            b.Cylinder(I, tank + new Vector3(0f, 0.8f, 0f), 0.3f, 0.15f, 0.08f, 12, Swatch.ArmorGrey, Swatch.MetalDark);
            b.Cylinder(At(new Vector3(0.8f, 0.55f, 0.45f), AlongX), Vector3.zero, 0.05f, 0.14f, 8, Swatch.Steel);
            b.Cylinder(At(new Vector3(0.8f, 0.35f, 0.6f), AlongX), Vector3.zero, 0.04f, 0.14f, 8, Swatch.Steel);
            b.Cylinder(At(new Vector3(1.22f, 0.5f, 0.85f), AlongZ), Vector3.zero, 0.04f, 0.2f, 8, Swatch.Steel);

            // Генераторный блок с решётками слева спереди.
            var generator = new Vector3(-1.15f, 0.36f, 0.55f);
            b.ChamferBox(I, generator, new Vector3(0.6f, 0.52f, 0.6f), 0.05f, Swatch.ArmorGrey, Swatch.ArmorWhite);
            for (int slat = 0; slat < 4; slat++)
                b.Box(I, generator + new Vector3(0f, -0.1f + slat * 0.07f, 0.302f), new Vector3(0.36f, 0.025f, 0.006f), Swatch.Grille);
            b.Box(I, generator + new Vector3(0f, 0.262f, 0f), new Vector3(0.4f, 0.006f, 0.3f), Swatch.Grille);
            b.Box(I, generator + new Vector3(-0.302f, 0.12f, 0f), new Vector3(0.006f, 0.05f, 0.4f), Swatch.TeamAccent);
        }
    }
}
