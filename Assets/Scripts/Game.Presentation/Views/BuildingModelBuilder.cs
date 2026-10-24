using System.Collections.Generic;
using UnityEngine;
using Game.Core.Types;
using Game.Presentation.Rendering;
using Swatch = Game.Presentation.Rendering.LowPolyMeshBuilder.Swatch;

namespace Game.Presentation.Views
{
    /// <summary>
    /// Низкополигональные модели базы и завода. Фасад смотрит в +z; цвет стороны берётся из материала
    /// (<see cref="TeamPalette"/>), поэтому меш один на тип здания, а захват меняет только материал.
    /// Корпуса вписаны в непроходимые круги симуляции (база ~2,35, завод ~1,65), плита основания — декор.
    /// </summary>
    public static class BuildingModelBuilder
    {
        private static readonly Dictionary<BuildingType, Mesh> MeshCache = new Dictionary<BuildingType, Mesh>();
        private static readonly Matrix4x4 I = Matrix4x4.identity;
        private static readonly Quaternion AlongX = Quaternion.Euler(0f, 0f, -90f);

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

        private static void BuildBase(LowPolyMeshBuilder b)
        {
            b.Box(I, new Vector3(0f, 0.07f, 0f), new Vector3(4.0f, 0.14f, 4.0f), Swatch.Concrete);
            foreach (float x in new[] { -1f, 1f })
            {
                foreach (float z in new[] { -1f, 1f })
                    b.Box(I, new Vector3(x * 1.85f, 0.145f, z * 1.85f), new Vector3(0.3f, 0.01f, 0.08f), Swatch.Hazard);
            }

            // Главный корпус с поясом цвета команды.
            const float hallZ = -0.3f, hallTop = 1.39f;
            b.Frustum(I, new Vector3(0f, 0.14f, hallZ), new Vector2(3.2f, 2.6f), new Vector2(2.9f, 2.3f), 1.25f, Vector2.zero, Swatch.MetalMid, Swatch.MetalDark);
            b.Box(I, new Vector3(0f, 1.05f, hallZ), new Vector3(3.04f, 0.14f, 2.44f), Swatch.TeamAccent);

            // Фасад: ворота цеха в жёлто-чёрной раме и светящееся остекление.
            b.Box(I, new Vector3(0.45f, 0.48f, 0.95f), new Vector3(1.1f, 0.68f, 0.08f), Swatch.MetalDark);
            foreach (float x in new[] { -0.14f, 1.04f })
                b.Box(I, new Vector3(x, 0.48f, 0.97f), new Vector3(0.08f, 0.7f, 0.06f), Swatch.Hazard);
            b.Box(I, new Vector3(-0.9f, 0.66f, 0.94f), new Vector3(0.8f, 0.12f, 0.04f), Swatch.Glow);

            // Командная башня на крыше: остекление, полоса команды, радар и антенна.
            var tower = new Vector3(-0.7f, hallTop, -0.5f);
            b.Frustum(I, tower, new Vector2(1.1f, 1.1f), new Vector2(0.85f, 0.85f), 0.9f, Vector2.zero, Swatch.MetalLight, Swatch.MetalMid);
            b.Box(I, tower + new Vector3(0f, 0.56f, 0f), new Vector3(0.97f, 0.1f, 0.97f), Swatch.Glow);
            b.Box(I, tower + new Vector3(0f, 0.22f, 0f), new Vector3(1.06f, 0.08f, 1.06f), Swatch.TeamAccent);
            b.Cylinder(I, tower + new Vector3(0f, 0.9f, 0f), 0.05f, 0.2f, 6, Swatch.MetalDark);
            b.Cylinder(At(tower + new Vector3(0f, 1.1f, 0f), Quaternion.Euler(-35f, 0f, 0f)), Vector3.zero, 0.06f, 0.32f, 0.1f, 10, Swatch.MetalLight, Swatch.MetalLight);
            b.Cylinder(I, new Vector3(-0.35f, hallTop, -0.85f), 0.02f, 0.62f, 4, Swatch.MetalDark);
            b.Box(I, new Vector3(-0.35f, hallTop + 0.64f, -0.85f), new Vector3(0.07f, 0.07f, 0.07f), Swatch.Glow);

            // Вентиляция на крыше.
            b.Box(I, new Vector3(0.75f, hallTop + 0.08f, -0.9f), new Vector3(0.6f, 0.16f, 0.4f), Swatch.MetalDark);
            b.Box(I, new Vector3(0.75f, hallTop + 0.06f, -0.2f), new Vector3(0.5f, 0.12f, 0.3f), Swatch.MetalMid);

            // Топливные баки сбоку.
            foreach (float z in new[] { -1.15f, -0.4f })
            {
                b.Cylinder(I, new Vector3(1.72f, 0.14f, z), 0.3f, 0.3f, 0.9f, 10, Swatch.MetalLight, Swatch.MetalMid);
                b.Cylinder(I, new Vector3(1.72f, 0.7f, z), 0.31f, 0.08f, 10, Swatch.TeamAccent);
            }

            // Площадка у фасада с разметкой.
            var pad = new Vector3(-1.0f, 0.14f, 1.45f);
            b.Cylinder(I, pad, 0.5f, 0.02f, 12, Swatch.MetalDark);
            b.Box(I, pad + new Vector3(-0.15f, 0.025f, 0f), new Vector3(0.06f, 0.01f, 0.4f), Swatch.Hazard);
            b.Box(I, pad + new Vector3(0.15f, 0.025f, 0f), new Vector3(0.06f, 0.01f, 0.4f), Swatch.Hazard);
            b.Box(I, pad + new Vector3(0f, 0.025f, 0f), new Vector3(0.3f, 0.01f, 0.06f), Swatch.Hazard);
        }

        private static void BuildFactory(LowPolyMeshBuilder b)
        {
            b.Box(I, new Vector3(0f, 0.06f, 0f), new Vector3(3.3f, 0.12f, 3.1f), Swatch.Concrete);

            // Цех с поясом команды и воротами.
            const float hallZ = -0.1f, hallTop = 0.97f;
            b.Frustum(I, new Vector3(0f, 0.12f, hallZ), new Vector2(2.4f, 1.9f), new Vector2(2.25f, 1.75f), 0.85f, Vector2.zero, Swatch.MetalMid, Swatch.MetalDark);
            b.Box(I, new Vector3(0f, 0.75f, hallZ), new Vector3(2.31f, 0.12f, 1.81f), Swatch.TeamAccent);
            b.Box(I, new Vector3(0f, 0.4f, 0.84f), new Vector3(0.9f, 0.56f, 0.06f), Swatch.MetalDark);
            foreach (float x in new[] { -0.5f, 0.5f })
                b.Box(I, new Vector3(x, 0.4f, 0.86f), new Vector3(0.07f, 0.58f, 0.05f), Swatch.Hazard);

            // Пилообразная крыша: крутые грани смотрят на фасад и светятся фонарями.
            for (int i = -1; i <= 1; i++)
            {
                b.Frustum(I, new Vector3(i * 0.75f, hallTop, hallZ), new Vector2(0.75f, 1.75f), new Vector2(0.75f, 0.3f), 0.35f, new Vector2(0f, 0.72f),
                    Swatch.MetalLight, Swatch.MetalLight);
                b.Box(I, new Vector3(i * 0.75f, hallTop + 0.17f, 0.78f), new Vector3(0.6f, 0.24f, 0.02f), Swatch.Glow);
            }

            // Дымовые трубы за цехом.
            foreach ((float x, float height) in new[] { (-0.8f, 1.9f), (-0.35f, 1.55f) })
            {
                var stack = new Vector3(x, 0.12f, -0.85f);
                b.Cylinder(I, stack, 0.17f, 0.14f, height, 8, Swatch.MetalDark, Swatch.Rubber);
                b.Cylinder(I, stack + new Vector3(0f, height * 0.7f, 0f), 0.165f, 0.1f, 8, Swatch.Hazard);
                b.Cylinder(I, stack + new Vector3(0f, height * 0.85f, 0f), 0.155f, 0.08f, 8, Swatch.TeamAccent);
            }

            // Резервуар с трубопроводом в цех.
            var tank = new Vector3(1.35f, 0.12f, -0.75f);
            b.Cylinder(I, tank, 0.36f, 0.36f, 0.75f, 10, Swatch.MetalLight, Swatch.MetalMid);
            b.Cylinder(I, tank + new Vector3(0f, 0.5f, 0f), 0.37f, 0.07f, 10, Swatch.TeamAccent);
            b.Cylinder(At(new Vector3(1.0f, 0.62f, -0.75f), AlongX), Vector3.zero, 0.05f, 0.25f, 6, Swatch.MetalDark);
            b.Cylinder(At(new Vector3(1.0f, 0.35f, -0.55f), AlongX), Vector3.zero, 0.04f, 0.25f, 6, Swatch.MetalDark);
        }
    }
}
