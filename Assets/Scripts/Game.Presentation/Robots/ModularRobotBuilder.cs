using System.Collections.Generic;
using UnityEngine;
using Game.Content.Definitions;
using Game.Core.Models;
using Game.Core.Types;
using Game.Presentation.Rendering;
using Game.Presentation.Views;
using Swatch = Game.Presentation.Rendering.LowPolyMeshBuilder.Swatch;

namespace Game.Presentation.Robots
{
    /// <summary>
    /// Собирает модель робота стопкой, как в Nether Earth: шасси внизу, над ним ярусы орудий,
    /// сверху электроника и ядерный заряд. Силуэт сразу показывает состав робота.
    /// Детали — низкополигональные меши одной палитры; цвет стороны задаёт материал, поэтому меши
    /// одинаковых деталей общие для всех роботов.
    /// </summary>
    public static class ModularRobotBuilder
    {
        private static readonly Dictionary<string, Mesh> MeshCache = new Dictionary<string, Mesh>();
        private static readonly Matrix4x4 I = Matrix4x4.identity;
        private static readonly Quaternion AlongZ = Quaternion.Euler(90f, 0f, 0f);
        private static readonly Quaternion AlongX = Quaternion.Euler(0f, 0f, -90f);

        // Точки крепления ног шагающего шасси (бедро).
        private const float HipHeight = 0.78f;
        private const float HipOffset = 0.38f;

        public static GameObject BuildRobotModel(RobotBlueprint blueprint, PlayerId owner)
        {
            Material material = TeamPalette.Get(owner, BuildingView.GetPlayerColor(owner));
            var root = new GameObject($"Robot_{blueprint?.Name ?? "Modular"}");
            var rig = root.AddComponent<RobotModelRig>();

            var body = new GameObject("Body").transform;
            body.SetParent(root.transform, false);
            rig.Body = body;

            string chassisId = blueprint?.ChassisId;
            float deckHeight;
            if (chassisId == DefaultContentCatalog.ChassisTracks)
            {
                deckHeight = 0.5f;
                AddPart(body, "Chassis", GetMesh("chassis_tracks", BuildTracks), material);
            }
            else if (chassisId == DefaultContentCatalog.ChassisAntiGrav)
            {
                deckHeight = 0.51f;
                rig.Hovering = true;
                AddPart(body, "Chassis", GetMesh("chassis_antigrav", BuildAntiGrav), material);
            }
            else
            {
                deckHeight = 0.92f;
                AddPart(body, "Chassis", GetMesh("chassis_bipod", BuildBipodHip), material);
                Mesh leg = GetMesh("bipod_leg", BuildLeg);
                rig.LeftLeg = AddPart(root.transform, "Leg_Left", leg, material).transform;
                rig.LeftLeg.localPosition = new Vector3(-HipOffset, HipHeight, 0f);
                rig.RightLeg = AddPart(root.transform, "Leg_Right", leg, material).transform;
                rig.RightLeg.localPosition = new Vector3(HipOffset, HipHeight, 0f);
            }

            var turret = new GameObject("Turret").transform;
            turret.SetParent(body, false);
            turret.localPosition = new Vector3(0f, deckHeight, 0f);
            rig.Turret = turret;

            string turretKey = "turret|" + string.Join(",", blueprint?.WeaponIds ?? new List<string>())
                + (blueprint != null && blueprint.HasElectronics ? "|e" : "") + (blueprint != null && blueprint.HasNuclear ? "|n" : "");
            AddPart(turret, "TurretStack", GetMesh(turretKey, builder => BuildTurretStack(builder, blueprint, out _)), material);
            // Высоту и точку радара считаем отдельно: при попадании в кэш построение не вызывается.
            float stackHeight = BuildTurretStack(null, blueprint, out float radarHeight);
            if (radarHeight >= 0f)
            {
                Transform radar = AddPart(turret, "Radar", GetMesh("radar", BuildRadar), material).transform;
                radar.localPosition = new Vector3(-0.12f, radarHeight, -0.1f);
                rig.Radar = radar;
            }

            rig.Height = deckHeight + stackHeight;
            return root;
        }

        private static Mesh GetMesh(string key, System.Action<LowPolyMeshBuilder> build)
        {
            if (MeshCache.TryGetValue(key, out Mesh cached) && cached != null) return cached;
            var builder = new LowPolyMeshBuilder();
            build(builder);
            Mesh mesh = builder.ToMesh("Robot_" + key);
            MeshCache[key] = mesh;
            return mesh;
        }

        private static GameObject AddPart(Transform parent, string name, Mesh mesh, Material material)
        {
            var part = new GameObject(name);
            part.transform.SetParent(parent, false);
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            part.AddComponent<MeshRenderer>().sharedMaterial = material;
            return part;
        }

        private static Matrix4x4 At(Vector3 position, Quaternion rotation) => Matrix4x4.TRS(position, rotation, Vector3.one);

        // ───────────────────────── Шасси ─────────────────────────

        private static void BuildTracks(LowPolyMeshBuilder b)
        {
            foreach (float side in new[] { -1f, 1f })
            {
                float x = side * 0.47f;
                b.Box(I, new Vector3(x, 0.19f, 0f), new Vector3(0.24f, 0.3f, 1.2f), Swatch.Rubber);
                // Скруглённые концы гусеницы и опорные катки снаружи.
                foreach (float z in new[] { -0.6f, 0.6f })
                    b.Cylinder(At(new Vector3(x - 0.12f, 0.19f, z), AlongX), Vector3.zero, 0.15f, 0.24f, 8, Swatch.Rubber);
                float outer = side > 0f ? 0.59f : -0.63f;
                foreach (float z in new[] { -0.4f, -0.13f, 0.13f, 0.4f })
                    b.Cylinder(At(new Vector3(outer, 0.17f, z), AlongX), Vector3.zero, 0.1f, 0.04f, 8, Swatch.MetalMid);
                b.Box(I, new Vector3(x, 0.38f, 0f), new Vector3(0.3f, 0.05f, 1.32f), Swatch.TeamAccent);
            }
            // Корпус со скошенным лбом.
            b.Frustum(I, new Vector3(0f, 0.2f, 0f), new Vector2(0.7f, 1.15f), new Vector2(0.6f, 0.85f), 0.3f, new Vector2(0f, -0.08f), Swatch.MetalMid, Swatch.MetalLight);
            b.Box(I, new Vector3(0f, 0.505f, 0.22f), new Vector3(0.46f, 0.02f, 0.1f), Swatch.TeamAccent);
            b.Box(I, new Vector3(0f, 0.36f, -0.56f), new Vector3(0.46f, 0.14f, 0.06f), Swatch.MetalDark);
            foreach (float x in new[] { -0.2f, 0.2f })
                b.Box(I, new Vector3(x, 0.36f, 0.575f), new Vector3(0.08f, 0.05f, 0.02f), Swatch.Glow);
        }

        private static void BuildBipodHip(LowPolyMeshBuilder b)
        {
            b.Frustum(I, new Vector3(0f, 0.7f, 0f), new Vector2(0.62f, 0.46f), new Vector2(0.5f, 0.38f), 0.22f, Vector2.zero, Swatch.MetalMid, Swatch.MetalLight);
            foreach (float side in new[] { -1f, 1f })
                b.Cylinder(At(new Vector3(side > 0f ? 0.28f : -0.38f, HipHeight, 0f), AlongX), Vector3.zero, 0.09f, 0.1f, 8, Swatch.MetalDark);
            b.Box(I, new Vector3(0f, 0.8f, 0.215f), new Vector3(0.4f, 0.12f, 0.04f), Swatch.TeamAccent);
            b.Box(I, new Vector3(0f, 0.86f, 0.236f), new Vector3(0.16f, 0.03f, 0.01f), Swatch.Glow);
        }

        /// <summary>Нога шагающего относительно бедра: опора стопы на y = −HipHeight (уровень земли).</summary>
        private static void BuildLeg(LowPolyMeshBuilder b)
        {
            b.Box(At(new Vector3(0f, -0.18f, 0.04f), Quaternion.Euler(-12f, 0f, 0f)), Vector3.zero, new Vector3(0.14f, 0.38f, 0.16f), Swatch.TeamAccent);
            b.Cylinder(At(new Vector3(-0.07f, -0.37f, 0.08f), AlongX), Vector3.zero, 0.07f, 0.14f, 8, Swatch.MetalDark);
            b.Box(At(new Vector3(0f, -0.56f, 0.03f), Quaternion.Euler(10f, 0f, 0f)), Vector3.zero, new Vector3(0.11f, 0.38f, 0.12f), Swatch.MetalMid);
            b.Frustum(I, new Vector3(0f, -HipHeight, 0.04f), new Vector2(0.2f, 0.34f), new Vector2(0.14f, 0.22f), 0.08f, Swatch.MetalDark);
        }

        private static void BuildAntiGrav(LowPolyMeshBuilder b)
        {
            b.Cylinder(I, new Vector3(0f, 0.12f, 0f), 0.36f, 0.4f, 0.06f, 10, Swatch.Glow, Swatch.Glow);
            b.Cylinder(I, new Vector3(0f, 0.18f, 0f), 0.45f, 0.62f, 0.1f, 10, Swatch.MetalMid, Swatch.MetalMid);
            b.Cylinder(I, new Vector3(0f, 0.28f, 0f), 0.62f, 0.5f, 0.09f, 10, Swatch.MetalLight, Swatch.MetalLight);
            b.Frustum(I, new Vector3(0f, 0.37f, 0f), new Vector2(0.6f, 0.6f), new Vector2(0.48f, 0.48f), 0.14f, Vector2.zero, Swatch.TeamAccent, Swatch.MetalLight);
            foreach (float angle in new[] { 45f, 135f, 225f, 315f })
            {
                float rad = angle * Mathf.Deg2Rad;
                var position = new Vector3(Mathf.Sin(rad) * 0.6f, 0.29f, Mathf.Cos(rad) * 0.6f);
                b.Box(At(position, Quaternion.Euler(0f, angle, 0f)), Vector3.zero, new Vector3(0.06f, 0.12f, 0.28f), Swatch.MetalDark);
            }
        }

        // ───────────────────────── Башня ─────────────────────────

        /// <summary>
        /// Строит ярусы над шасси и возвращает их общую высоту. builder = null — только расчёт высот.
        /// radarHeight — высота площадки радара (−1, если электроники нет).
        /// </summary>
        private static float BuildTurretStack(LowPolyMeshBuilder b, RobotBlueprint blueprint, out float radarHeight)
        {
            radarHeight = -1f;
            b?.Cylinder(I, Vector3.zero, 0.3f, 0.05f, 10, Swatch.MetalDark);
            float y = 0.05f;
            foreach (string weaponId in blueprint?.WeaponIds ?? new List<string>())
            {
                if (weaponId == DefaultContentCatalog.WeaponMissiles) y += Missiles(b, y);
                else if (weaponId == DefaultContentCatalog.WeaponPhasers) y += Phaser(b, y);
                else y += Cannon(b, y);
            }
            if (blueprint != null && blueprint.HasElectronics)
            {
                radarHeight = y + 0.16f;
                y += Electronics(b, y);
            }
            if (blueprint != null && blueprint.HasNuclear) y += Nuclear(b, y);
            return y;
        }

        private static float Cannon(LowPolyMeshBuilder b, float y)
        {
            if (b == null) return 0.28f;
            b.Frustum(I, new Vector3(0f, y, -0.03f), new Vector2(0.56f, 0.64f), new Vector2(0.44f, 0.44f), 0.24f, new Vector2(0f, -0.04f), Swatch.MetalLight, Swatch.MetalMid);
            foreach (float side in new[] { -1f, 1f })
                b.Box(I, new Vector3(side * 0.255f, y + 0.11f, -0.05f), new Vector3(0.04f, 0.12f, 0.4f), Swatch.TeamAccent);
            b.Box(I, new Vector3(0f, y + 0.12f, 0.27f), new Vector3(0.2f, 0.14f, 0.1f), Swatch.MetalDark);
            b.Cylinder(At(new Vector3(0f, y + 0.12f, 0.3f), AlongZ), Vector3.zero, 0.045f, 0.58f, 6, Swatch.MetalDark);
            b.Cylinder(At(new Vector3(0f, y + 0.12f, 0.84f), AlongZ), Vector3.zero, 0.07f, 0.1f, 6, Swatch.MetalDark);
            b.Cylinder(I, new Vector3(-0.1f, y + 0.24f, -0.12f), 0.08f, 0.04f, 8, Swatch.MetalMid);
            return 0.28f;
        }

        private static float Missiles(LowPolyMeshBuilder b, float y)
        {
            if (b == null) return 0.32f;
            b.Box(I, new Vector3(0f, y + 0.16f, 0f), new Vector3(0.6f, 0.3f, 0.5f), Swatch.MetalMid);
            foreach (float side in new[] { -1f, 1f })
                b.Box(I, new Vector3(side * 0.31f, y + 0.16f, 0f), new Vector3(0.03f, 0.22f, 0.4f), Swatch.TeamAccent);
            b.Box(I, new Vector3(0f, y + 0.16f, 0.255f), new Vector3(0.5f, 0.24f, 0.02f), Swatch.MetalDark);
            foreach (float row in new[] { 0.1f, 0.22f })
            {
                foreach (float x in new[] { -0.16f, 0f, 0.16f })
                    b.Cylinder(At(new Vector3(x, y + row, 0.26f), AlongZ), Vector3.zero, 0.045f, 0.015f, 0.08f, 6, Swatch.Hazard, Swatch.Hazard);
            }
            b.Box(I, new Vector3(0f, y + 0.315f, -0.12f), new Vector3(0.36f, 0.01f, 0.08f), Swatch.Hazard);
            return 0.32f;
        }

        private static float Phaser(LowPolyMeshBuilder b, float y)
        {
            if (b == null) return 0.26f;
            b.Frustum(I, new Vector3(0f, y, -0.05f), new Vector2(0.5f, 0.5f), new Vector2(0.38f, 0.4f), 0.24f, Vector2.zero, Swatch.MetalLight, Swatch.MetalMid);
            b.Cylinder(At(new Vector3(0f, y + 0.12f, 0.15f), AlongZ), Vector3.zero, 0.09f, 0.07f, 0.35f, 8, Swatch.MetalDark, Swatch.MetalDark);
            foreach (float z in new[] { 0.24f, 0.36f })
                b.Cylinder(At(new Vector3(0f, y + 0.12f, z), AlongZ), Vector3.zero, 0.12f, 0.04f, 8, Swatch.TeamAccent);
            b.Cylinder(At(new Vector3(0f, y + 0.12f, 0.5f), AlongZ), Vector3.zero, 0.075f, 0.03f, 8, Swatch.Glow);
            return 0.26f;
        }

        private static float Electronics(LowPolyMeshBuilder b, float y)
        {
            if (b == null) return 0.28f;
            b.Box(I, new Vector3(-0.12f, y + 0.08f, -0.1f), new Vector3(0.26f, 0.16f, 0.26f), Swatch.MetalMid);
            b.Box(I, new Vector3(-0.12f, y + 0.1f, 0.035f), new Vector3(0.18f, 0.03f, 0.01f), Swatch.Glow);
            b.Cylinder(I, new Vector3(0.18f, y, -0.15f), 0.015f, 0.45f, 4, Swatch.MetalDark);
            b.Box(I, new Vector3(0.18f, y + 0.47f, -0.15f), new Vector3(0.05f, 0.05f, 0.05f), Swatch.Glow);
            return 0.28f;
        }

        /// <summary>Радар отдельным мешем: его вращает <see cref="RobotModelRig"/>.</summary>
        private static void BuildRadar(LowPolyMeshBuilder b)
        {
            b.Cylinder(I, Vector3.zero, 0.03f, 0.1f, 6, Swatch.MetalDark);
            b.Cylinder(At(new Vector3(0f, 0.12f, 0f), Quaternion.Euler(-60f, 0f, 0f)), Vector3.zero, 0.04f, 0.17f, 0.06f, 8, Swatch.MetalLight, Swatch.MetalLight);
            b.Box(I, new Vector3(0f, 0.16f, 0.06f), new Vector3(0.03f, 0.03f, 0.03f), Swatch.Glow);
        }

        private static float Nuclear(LowPolyMeshBuilder b, float y)
        {
            if (b == null) return 0.38f;
            b.Cylinder(I, new Vector3(0f, y, 0f), 0.17f, 0.17f, 0.32f, 8, Swatch.Hazard, Swatch.MetalDark);
            foreach (float band in new[] { 0.07f, 0.21f })
                b.Cylinder(I, new Vector3(0f, y + band, 0f), 0.18f, 0.04f, 8, Swatch.MetalDark);
            b.Cylinder(I, new Vector3(0f, y + 0.32f, 0f), 0.12f, 0.06f, 8, Swatch.Glow);
            return 0.38f;
        }
    }
}
