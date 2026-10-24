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
        private const float HipHeight = 0.8f;
        private const float HipOffset = 0.42f;
        private const float WalkerDeck = 0.97f;

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
                deckHeight = TracksDeck;
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
                deckHeight = WalkerDeck;
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
                radar.localPosition = new Vector3(RadarX, radarHeight, RadarZ);
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

        // Гусеничное шасси по эскизу «Тяжёлая гусеничная платформа»: петля траков, шесть катков, белые плиты.
        private const float TrackX = 0.44f;        // центр гусеницы по x
        private const float TrackWidth = 0.24f;
        private const float TrackEndZ = 0.6f;      // центры ленивца и ведущего колеса
        private const float TrackAxleY = 0.22f;
        private const float TrackRadius = 0.19f;   // радиус петли по центру траков
        private const float TracksDeck = 0.56f;
        // Полки и короба уже гусеницы и сдвинуты к корпусу: внешняя половина ленты видна сверху.
        private const float ShelfX = 0.38f;
        private const float ShelfWidth = 0.15f;

        private static void BuildTracks(LowPolyMeshBuilder b)
        {
            foreach (float side in new[] { -1f, 1f })
            {
                BuildTrackLoop(b, side * TrackX);
                BuildRoadWheels(b, side);

                // Надгусеничные полки спереди и сзади, бортовой короб с полосой цвета стороны.
                b.ChamferBox(I, new Vector3(side * ShelfX, 0.48f, 0.56f), new Vector3(ShelfWidth, 0.1f, 0.34f), 0.035f, Swatch.ArmorGrey, Swatch.ArmorWhite);
                b.ChamferBox(I, new Vector3(side * ShelfX, 0.47f, -0.6f), new Vector3(ShelfWidth, 0.09f, 0.26f), 0.03f, Swatch.ArmorGrey, Swatch.ArmorWhite);
                b.ChamferBox(I, new Vector3(side * ShelfX, 0.5f, -0.05f), new Vector3(ShelfWidth, 0.13f, 0.6f), 0.035f, Swatch.ArmorGrey, Swatch.ArmorWhite);
                b.Box(I, new Vector3(side * ShelfX, 0.5f, 0.12f), new Vector3(ShelfWidth + 0.006f, 0.136f, 0.05f), Swatch.TeamAccent);
                b.ChamferBox(I, new Vector3(side * ShelfX, 0.5f, -0.42f), new Vector3(ShelfWidth - 0.02f, 0.1f, 0.14f), 0.02f, Swatch.MetalDark);
            }

            // Корпус между гусеницами и верхняя палуба.
            b.Box(I, new Vector3(0f, 0.26f, 0f), new Vector3(0.64f, 0.3f, 1.2f), Swatch.Grille);
            b.ChamferBox(I, new Vector3(0f, 0.5f, -0.08f), new Vector3(0.62f, 0.12f, 1.0f), 0.04f, Swatch.ArmorGrey, Swatch.ArmorWhite);

            // Скошенный лоб с шевроном стороны и тёмной нижней плитой.
            Matrix4x4 glacis = At(new Vector3(0f, 0.42f, 0.56f), Quaternion.Euler(32f, 0f, 0f));
            b.ChamferBox(glacis, Vector3.zero, new Vector3(0.6f, 0.1f, 0.34f), 0.035f, Swatch.ArmorGrey, Swatch.ArmorWhite);
            b.ChamferBox(glacis, new Vector3(0f, 0.055f, 0.02f), new Vector3(0.2f, 0.012f, 0.16f), 0.004f, Swatch.TeamAccent);
            b.Chevron(glacis.MultiplyPoint3x4(new Vector3(0f, 0.062f, 0.02f)), glacis.MultiplyVector(Vector3.up), glacis.MultiplyVector(Vector3.back), 0.11f, Swatch.ArmorWhite);
            b.Box(I, new Vector3(0f, 0.28f, 0.66f), new Vector3(0.48f, 0.16f, 0.06f), Swatch.Grille);
            foreach (float x in new[] { -0.2f, 0.2f })
            {
                b.Cylinder(At(new Vector3(x, 0.27f, 0.69f), AlongZ), Vector3.zero, 0.025f, 0.05f, 6, Swatch.Steel);
                b.Box(I, new Vector3(x * 1.3f, 0.36f, 0.705f), new Vector3(0.07f, 0.035f, 0.015f), Swatch.Glow);
            }

            // Погон башни, решётка двигателя и ящики на корме.
            b.Cylinder(I, new Vector3(0f, TracksDeck, 0.02f), 0.27f, 0.015f, 14, Swatch.Grille);
            b.Cylinder(I, new Vector3(0f, TracksDeck, 0.02f), 0.21f, 0.02f, 14, Swatch.Steel);
            b.Box(I, new Vector3(0f, 0.565f, -0.4f), new Vector3(0.3f, 0.012f, 0.24f), Swatch.Grille);
            for (int slat = 0; slat < 5; slat++)
                b.Box(I, new Vector3(0f, 0.575f, -0.49f + slat * 0.045f), new Vector3(0.28f, 0.012f, 0.014f), Swatch.MetalDark);
            b.ChamferBox(I, new Vector3(-0.2f, 0.6f, -0.24f), new Vector3(0.14f, 0.08f, 0.12f), 0.015f, Swatch.MetalDark);
            b.ChamferBox(I, new Vector3(0.21f, 0.6f, -0.26f), new Vector3(0.16f, 0.08f, 0.18f), 0.02f, Swatch.ArmorGrey, Swatch.ArmorWhite);
            b.Box(I, new Vector3(0.21f, 0.6f, -0.2f), new Vector3(0.165f, 0.085f, 0.03f), Swatch.TeamAccent);
        }

        /// <summary>
        /// Петля гусеницы в плоскости (z, y): нижняя и верхняя ветви, полукруги вокруг ленивца и ведущего колеса.
        /// Траки — отдельные бруски по касательной, через один темнее, чтобы лента читалась звеньями.
        /// </summary>
        private static void BuildTrackLoop(LowPolyMeshBuilder b, float x)
        {
            float straight = TrackEndZ * 2f, arc = Mathf.PI * TrackRadius;
            float perimeter = 2f * straight + 2f * arc;
            const float pitch = 0.08f;
            int links = Mathf.RoundToInt(perimeter / pitch);
            for (int i = 0; i < links; i++)
            {
                float s = i * perimeter / links;
                Vector3 position, tangent, normal;
                if (s < straight)
                {
                    position = new Vector3(x, TrackAxleY - TrackRadius, -TrackEndZ + s);
                    tangent = Vector3.forward; normal = Vector3.down;
                }
                else if (s < straight + arc)
                {
                    float a = -Mathf.PI * 0.5f + (s - straight) / TrackRadius;
                    normal = new Vector3(0f, Mathf.Sin(a), Mathf.Cos(a));
                    position = new Vector3(x, TrackAxleY, TrackEndZ) + normal * TrackRadius;
                    tangent = new Vector3(0f, Mathf.Cos(a), -Mathf.Sin(a));
                }
                else if (s < 2f * straight + arc)
                {
                    position = new Vector3(x, TrackAxleY + TrackRadius, TrackEndZ - (s - straight - arc));
                    tangent = Vector3.back; normal = Vector3.up;
                }
                else
                {
                    float a = Mathf.PI * 0.5f + (s - 2f * straight - arc) / TrackRadius;
                    normal = new Vector3(0f, Mathf.Sin(a), Mathf.Cos(a));
                    position = new Vector3(x, TrackAxleY, -TrackEndZ) + normal * TrackRadius;
                    tangent = new Vector3(0f, Mathf.Cos(a), -Mathf.Sin(a));
                }
                b.Box(At(position, Quaternion.LookRotation(tangent, normal)), Vector3.zero, new Vector3(TrackWidth, 0.045f, pitch * 0.82f),
                    i % 2 == 0 ? Swatch.Steel : Swatch.MetalMid);
            }
            // Ленивец и ведущее колесо внутри петли.
            foreach (float z in new[] { -TrackEndZ, TrackEndZ })
                b.Cylinder(At(new Vector3(x - TrackWidth * 0.45f, TrackAxleY, z), AlongX), Vector3.zero, 0.15f, TrackWidth * 0.9f, 10, Swatch.Steel);
        }

        /// <summary>Шесть опорных катков снаружи: шина, обод и колпак цвета стороны.</summary>
        private static void BuildRoadWheels(LowPolyMeshBuilder b, float side)
        {
            float inner = TrackX - TrackWidth * 0.42f, outer = TrackX + TrackWidth * 0.5f;
            for (int i = 0; i < 6; i++)
            {
                float z = -0.5f + i * 0.2f;
                // Цилиндр растёт по +x от основания, поэтому для левого борта основание — у внешнего края.
                float tireStart = side > 0f ? inner : -outer;
                b.Cylinder(At(new Vector3(tireStart, 0.19f, z), AlongX), Vector3.zero, 0.145f, outer - inner, 12, Swatch.MetalDark);
                b.Cylinder(At(new Vector3(side > 0f ? outer : -outer - 0.015f, 0.19f, z), AlongX), Vector3.zero, 0.105f, 0.015f, 12, Swatch.Steel);
                b.Cylinder(At(new Vector3(side > 0f ? outer + 0.015f : -outer - 0.03f, 0.19f, z), AlongX), Vector3.zero, 0.045f, 0.015f, 8, Swatch.MetalDark);
            }
        }

        /// <summary>
        /// Таз шагохода по эскизу «тяжёлое шагающее шасси»: блок с белой палубой и полосой стороны,
        /// по бокам ребристые приводы бёдер до шарниров ног.
        /// </summary>
        private static void BuildBipodHip(LowPolyMeshBuilder b)
        {
            b.ChamferBox(I, new Vector3(0f, 0.82f, 0f), new Vector3(0.5f, 0.24f, 0.42f), 0.04f, Swatch.ArmorGrey, Swatch.ArmorWhite);
            b.ChamferBox(I, new Vector3(0f, WalkerDeck - 0.03f, -0.02f), new Vector3(0.56f, 0.06f, 0.44f), 0.02f, Swatch.ArmorGrey, Swatch.ArmorWhite);
            b.ChamferBox(I, new Vector3(0f, 0.78f, 0.22f), new Vector3(0.3f, 0.2f, 0.04f), 0.015f, Swatch.ArmorGrey, Swatch.ArmorWhite);
            b.Box(I, new Vector3(0f, WalkerDeck - 0.002f, 0.12f), new Vector3(0.36f, 0.006f, 0.04f), Swatch.TeamAccent);
            b.Box(I, new Vector3(0f, 0.76f, 0.242f), new Vector3(0.1f, 0.02f, 0.006f), Swatch.Glow);

            // Ребристые приводы бёдер: от борта таза до шарнира ноги.
            foreach (float side in new[] { -1f, 1f })
            {
                float start = side > 0f ? 0.22f : -HipOffset;
                b.Cylinder(At(new Vector3(start, HipHeight, 0f), AlongX), Vector3.zero, 0.12f, HipOffset - 0.22f, 10, Swatch.MetalDark);
                for (int rib = 0; rib < 3; rib++)
                    b.Cylinder(At(new Vector3(start + 0.03f + rib * 0.055f, HipHeight, 0f), AlongX), Vector3.zero, 0.128f, 0.016f, 10, Swatch.Steel);
                b.ChamferBox(I, new Vector3(side * 0.22f, WalkerDeck + 0.02f, -0.12f), new Vector3(0.12f, 0.06f, 0.14f), 0.015f, Swatch.MetalDark);
                b.Box(I, new Vector3(side * 0.22f, WalkerDeck + 0.02f, -0.049f), new Vector3(0.05f, 0.015f, 0.006f), Swatch.Glow);
            }

            // Тёмный блок на корме с решёткой.
            b.ChamferBox(I, new Vector3(0f, 0.84f, -0.24f), new Vector3(0.4f, 0.16f, 0.06f), 0.015f, Swatch.MetalDark);
            for (int slat = 0; slat < 4; slat++)
                b.Box(I, new Vector3(0f, 0.79f + slat * 0.035f, -0.272f), new Vector3(0.3f, 0.012f, 0.006f), Swatch.Grille);
        }

        /// <summary>
        /// Нога шагохода относительно шарнира бедра; опора стопы на y = −HipHeight (уровень земли).
        /// Бедро с диагональной полосой стороны, дисковое колено, голень с накладкой, привод голеностопа,
        /// широкая стопа с двумя «пальцами» и пяткой. Нога симметрична: один меш на обе стороны.
        /// </summary>
        private static void BuildLeg(LowPolyMeshBuilder b)
        {
            Matrix4x4 thigh = At(new Vector3(0f, -0.18f, 0.03f), Quaternion.Euler(-10f, 0f, 0f));
            b.ChamferBox(thigh, Vector3.zero, new Vector3(0.2f, 0.36f, 0.22f), 0.04f, Swatch.ArmorGrey, Swatch.ArmorWhite);
            b.ChamferBox(thigh, new Vector3(0f, 0.01f, 0.115f), new Vector3(0.17f, 0.26f, 0.03f), 0.012f, Swatch.ArmorWhite);
            b.Box(thigh * At(new Vector3(0f, 0.01f, 0.132f), Quaternion.Euler(0f, 0f, 38f)), Vector3.zero, new Vector3(0.035f, 0.22f, 0.006f), Swatch.TeamAccent);

            // Колено: тёмный диск-привод с ободом.
            b.Cylinder(At(new Vector3(-0.13f, -0.37f, 0.06f), AlongX), Vector3.zero, 0.1f, 0.26f, 12, Swatch.MetalDark);
            b.Cylinder(At(new Vector3(-0.14f, -0.37f, 0.06f), AlongX), Vector3.zero, 0.055f, 0.28f, 10, Swatch.Steel);

            Matrix4x4 shin = At(new Vector3(0f, -0.54f, 0.01f), Quaternion.Euler(8f, 0f, 0f));
            b.ChamferBox(shin, Vector3.zero, new Vector3(0.18f, 0.3f, 0.2f), 0.035f, Swatch.ArmorGrey, Swatch.ArmorWhite);
            b.ChamferBox(shin, new Vector3(0f, 0.02f, 0.105f), new Vector3(0.15f, 0.2f, 0.025f), 0.01f, Swatch.ArmorWhite);
            b.Box(shin, new Vector3(0f, -0.06f, 0.12f), new Vector3(0.04f, 0.015f, 0.006f), Swatch.Glow);

            // Голеностоп и стопа.
            b.Cylinder(At(new Vector3(-0.1f, -0.68f, -0.01f), AlongX), Vector3.zero, 0.06f, 0.2f, 10, Swatch.MetalDark);
            float sole = -HipHeight;
            b.ChamferBox(I, new Vector3(0f, sole + 0.04f, 0.02f), new Vector3(0.3f, 0.08f, 0.34f), 0.02f, Swatch.MetalDark);
            b.ChamferBox(I, new Vector3(0f, sole + 0.1f, -0.01f), new Vector3(0.22f, 0.06f, 0.22f), 0.02f, Swatch.ArmorGrey, Swatch.ArmorWhite);
            foreach (float x in new[] { -0.075f, 0.075f })
                b.ChamferBox(I, new Vector3(x, sole + 0.035f, 0.24f), new Vector3(0.12f, 0.07f, 0.14f), 0.02f, Swatch.MetalDark, Swatch.ArmorGrey);
            b.ChamferBox(I, new Vector3(0f, sole + 0.035f, -0.19f), new Vector3(0.2f, 0.07f, 0.1f), 0.02f, Swatch.MetalDark);
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
        // Блок электроники и радар над ним.
        private const float RadarX = -0.1f;
        private const float RadarZ = -0.08f;

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
                radarHeight = y + 0.23f;
                y += Electronics(b, y);
            }
            if (blueprint != null && blueprint.HasNuclear) y += Nuclear(b, y);
            return y;
        }

        /// <summary>
        /// Пушечная башня по эскизу тяжёлого танка: угловатые плиты, бортовые панели с шевроном,
        /// маска, длинный ствол с кольцами и дульным срезом, блок датчиков с огнями, башенка и антенна.
        /// </summary>
        private static float Cannon(LowPolyMeshBuilder b, float y)
        {
            const float height = 0.36f;
            if (b == null) return height;

            b.ChamferBox(I, new Vector3(0f, y + 0.13f, -0.06f), new Vector3(0.58f, 0.26f, 0.62f), 0.05f, Swatch.ArmorGrey, Swatch.ArmorWhite);
            b.ChamferBox(I, new Vector3(0f, y + 0.12f, 0.24f), new Vector3(0.46f, 0.22f, 0.2f), 0.045f, Swatch.ArmorGrey, Swatch.ArmorWhite);
            b.ChamferBox(I, new Vector3(0.04f, y + 0.29f, -0.12f), new Vector3(0.4f, 0.08f, 0.38f), 0.03f, Swatch.ArmorGrey, Swatch.ArmorWhite);

            // Бортовые панели цвета стороны с белым шевроном.
            foreach (float side in new[] { -1f, 1f })
            {
                var panel = new Vector3(side * 0.298f, y + 0.13f, -0.06f);
                b.ChamferBox(I, panel, new Vector3(0.022f, 0.17f, 0.34f), 0.008f, Swatch.TeamAccent);
                b.Chevron(panel + new Vector3(side * 0.011f, 0f, 0f), new Vector3(side, 0f, 0f), Vector3.up, 0.12f, Swatch.ArmorWhite);
            }

            // Маска и ствол: тёмная труба, светлая обойма, полоса стороны, дульный тормоз.
            b.ChamferBox(I, new Vector3(0f, y + 0.12f, 0.36f), new Vector3(0.2f, 0.16f, 0.1f), 0.025f, Swatch.MetalDark, Swatch.ArmorGrey);
            b.Cylinder(At(new Vector3(0f, y + 0.12f, 0.4f), AlongZ), Vector3.zero, 0.048f, 0.5f, 8, Swatch.MetalDark);
            b.Cylinder(At(new Vector3(0f, y + 0.12f, 0.46f), AlongZ), Vector3.zero, 0.064f, 0.1f, 8, Swatch.ArmorWhite);
            b.Cylinder(At(new Vector3(0f, y + 0.12f, 0.72f), AlongZ), Vector3.zero, 0.055f, 0.03f, 8, Swatch.TeamAccent);
            b.Cylinder(At(new Vector3(0f, y + 0.12f, 0.86f), AlongZ), Vector3.zero, 0.074f, 0.12f, 8, Swatch.MetalDark);
            b.Cylinder(At(new Vector3(0f, y + 0.12f, 0.98f), AlongZ), Vector3.zero, 0.042f, 0.004f, 8, Swatch.Grille);

            // Блок датчиков с тремя огнями, командирская башенка и антенна.
            b.ChamferBox(I, new Vector3(-0.19f, y + 0.3f, 0.06f), new Vector3(0.13f, 0.08f, 0.1f), 0.015f, Swatch.MetalDark);
            for (int light = -1; light <= 1; light++)
                b.Box(I, new Vector3(-0.19f + light * 0.03f, y + 0.3f, 0.112f), new Vector3(0.018f, 0.018f, 0.006f), Swatch.Glow);
            b.Cylinder(I, new Vector3(0.14f, y + 0.33f, -0.16f), 0.075f, 0.065f, 0.05f, 10, Swatch.ArmorGrey, Swatch.MetalDark);
            b.Cylinder(I, new Vector3(0.14f, y + 0.365f, -0.16f), 0.077f, 0.015f, 10, Swatch.TeamAccent);
            b.Cylinder(I, new Vector3(-0.22f, y + 0.26f, -0.3f), 0.012f, 0.42f, 5, Swatch.MetalDark);
            b.Cylinder(I, new Vector3(-0.22f, y + 0.6f, -0.3f), 0.016f, 0.05f, 5, Swatch.TeamAccent);
            return height;
        }

        /// <summary>
        /// Ракетный блок по эскизу «четырёхствольная ракетная турель»: бронированный контейнер, спереди четыре трубы 2×2
        /// в восьмигранных обоймах с красными головками и жёлтыми кольцами, сзади блок с рукоятью, на борту панель с шевроном.
        /// </summary>
        private static float Missiles(LowPolyMeshBuilder b, float y)
        {
            const float height = 0.34f;
            if (b == null) return height;

            b.ChamferBox(I, new Vector3(0f, y + 0.17f, -0.06f), new Vector3(0.56f, 0.3f, 0.48f), 0.045f, Swatch.ArmorGrey, Swatch.ArmorWhite);
            b.ChamferBox(I, new Vector3(0f, y + 0.17f, -0.33f), new Vector3(0.48f, 0.24f, 0.08f), 0.025f, Swatch.MetalDark);
            b.Box(I, new Vector3(0f, y + 0.17f, -0.38f), new Vector3(0.22f, 0.025f, 0.025f), Swatch.Steel);
            foreach (float x in new[] { -0.11f, 0.11f })
                b.Box(I, new Vector3(x, y + 0.17f, -0.36f), new Vector3(0.025f, 0.025f, 0.05f), Swatch.Steel);

            // Бортовые панели с шевроном и решётка на крыше.
            foreach (float side in new[] { -1f, 1f })
            {
                var panel = new Vector3(side * 0.288f, y + 0.19f, -0.04f);
                b.ChamferBox(I, panel, new Vector3(0.02f, 0.13f, 0.2f), 0.006f, Swatch.TeamAccent);
                b.Chevron(panel + new Vector3(side * 0.01f, 0f, 0f), new Vector3(side, 0f, 0f), Vector3.up, 0.09f, Swatch.ArmorWhite);
            }
            b.Box(I, new Vector3(0f, y + 0.325f, -0.1f), new Vector3(0.22f, 0.01f, 0.14f), Swatch.Grille);
            for (int slat = 0; slat < 4; slat++)
                b.Box(I, new Vector3(0f, y + 0.332f, -0.155f + slat * 0.036f), new Vector3(0.2f, 0.01f, 0.012f), Swatch.MetalDark);

            // Четыре трубы: обойма, корпус ракеты, жёлтое кольцо, красная головка.
            foreach (float row in new[] { 0.1f, 0.245f })
            {
                foreach (float x in new[] { -0.135f, 0.135f })
                {
                    var tube = new Vector3(x, y + row, 0.24f);
                    b.ChamferBox(I, tube, new Vector3(0.22f, 0.135f, 0.16f), 0.035f, Swatch.MetalDark, Swatch.ArmorGrey);
                    b.Cylinder(At(tube + new Vector3(0f, 0f, 0.07f), AlongZ), Vector3.zero, 0.052f, 0.05f, 8, Swatch.Grille);
                    b.Cylinder(At(tube + new Vector3(0f, 0f, 0.1f), AlongZ), Vector3.zero, 0.054f, 0.015f, 8, Swatch.Hazard);
                    b.Cylinder(At(tube + new Vector3(0f, 0f, 0.115f), AlongZ), Vector3.zero, 0.05f, 0.012f, 0.07f, 8, Swatch.Warhead, Swatch.Warhead);
                }
            }
            return height;
        }

        /// <summary>
        /// Фазер по эскизу «турель-рельсотрон»: корпус со скошенной бортовой панелью, передний блок,
        /// длинная направляющая с тёмным пазом, бандажи со светящимися огнями и дульный блок.
        /// </summary>
        private static float Phaser(LowPolyMeshBuilder b, float y)
        {
            const float height = 0.3f;
            if (b == null) return height;

            b.ChamferBox(I, new Vector3(0f, y + 0.14f, -0.1f), new Vector3(0.5f, 0.26f, 0.46f), 0.045f, Swatch.ArmorGrey, Swatch.ArmorWhite);
            b.ChamferBox(I, new Vector3(0.03f, y + 0.27f, -0.14f), new Vector3(0.3f, 0.06f, 0.26f), 0.02f, Swatch.MetalDark, Swatch.ArmorGrey);
            foreach (float side in new[] { -1f, 1f })
            {
                var panel = new Vector3(side * 0.258f, y + 0.14f, -0.1f);
                b.ChamferBox(I, panel, new Vector3(0.02f, 0.18f, 0.3f), 0.006f, Swatch.TeamAccent);
                b.Chevron(panel + new Vector3(side * 0.01f, 0f, 0f), new Vector3(side, 0f, 0f), Vector3.up, 0.1f, Swatch.ArmorWhite);
            }

            // Передний блок и направляющая: светлая рейка с тёмным пазом посередине.
            b.ChamferBox(I, new Vector3(0f, y + 0.13f, 0.18f), new Vector3(0.26f, 0.2f, 0.16f), 0.035f, Swatch.ArmorGrey, Swatch.ArmorWhite);
            b.ChamferBox(I, new Vector3(0f, y + 0.13f, 0.54f), new Vector3(0.15f, 0.11f, 0.58f), 0.025f, Swatch.ArmorGrey, Swatch.ArmorWhite);
            b.Box(I, new Vector3(0f, y + 0.13f, 0.54f), new Vector3(0.156f, 0.03f, 0.56f), Swatch.Grille);
            foreach (float z in new[] { 0.38f, 0.62f })
            {
                b.ChamferBox(I, new Vector3(0f, y + 0.13f, z), new Vector3(0.19f, 0.15f, 0.05f), 0.015f, Swatch.MetalDark);
                b.Box(I, new Vector3(0f, y + 0.055f, z), new Vector3(0.05f, 0.006f, 0.02f), Swatch.Glow);
            }
            b.ChamferBox(I, new Vector3(0f, y + 0.13f, 0.87f), new Vector3(0.18f, 0.14f, 0.1f), 0.03f, Swatch.ArmorGrey, Swatch.ArmorWhite);
            b.Cylinder(At(new Vector3(0f, y + 0.13f, 0.92f), AlongZ), Vector3.zero, 0.045f, 0.004f, 8, Swatch.Glow);

            // Антенна с огнём и янтарная метка на переднем блоке.
            b.Cylinder(I, new Vector3(-0.19f, y + 0.27f, -0.26f), 0.014f, 0.3f, 5, Swatch.Steel);
            b.Cylinder(I, new Vector3(-0.19f, y + 0.5f, -0.26f), 0.018f, 0.04f, 5, Swatch.Glow);
            b.Box(I, new Vector3(0.09f, y + 0.08f, 0.262f), new Vector3(0.03f, 0.02f, 0.006f), Swatch.Lamp);
            return height;
        }

        /// <summary>Электроника: блок датчиков со светящейся панелью и угловыми накладками, антенна; радар ставится сверху.</summary>
        private static float Electronics(LowPolyMeshBuilder b, float y)
        {
            const float height = 0.3f;
            if (b == null) return height;
            var box = new Vector3(RadarX, y + 0.12f, RadarZ);
            b.ChamferBox(I, box, new Vector3(0.3f, 0.22f, 0.28f), 0.04f, Swatch.ArmorGrey, Swatch.ArmorWhite);
            b.ChamferBox(I, box + new Vector3(0f, 0f, 0.14f), new Vector3(0.18f, 0.12f, 0.012f), 0.01f, Swatch.Glow);
            foreach (float x in new[] { -0.15f, 0.15f })
                b.ChamferBox(I, box + new Vector3(x, 0f, 0f), new Vector3(0.03f, 0.2f, 0.24f), 0.01f, Swatch.MetalDark);
            b.Box(I, box + new Vector3(0f, -0.05f, 0.141f), new Vector3(0.16f, 0.02f, 0.006f), Swatch.TeamAccent);
            b.Cylinder(I, new Vector3(0.18f, y, -0.15f), 0.014f, 0.45f, 5, Swatch.Steel);
            b.Cylinder(I, new Vector3(0.18f, y + 0.42f, -0.15f), 0.02f, 0.05f, 5, Swatch.Glow);
            return height;
        }

        /// <summary>Радар отдельным мешем: его вращает <see cref="RobotModelRig"/>.</summary>
        private static void BuildRadar(LowPolyMeshBuilder b)
        {
            b.Cylinder(I, Vector3.zero, 0.05f, 0.04f, 8, Swatch.MetalDark);
            b.Cylinder(I, Vector3.zero, 0.022f, 0.12f, 6, Swatch.Steel);
            b.Cylinder(At(new Vector3(0f, 0.13f, 0f), Quaternion.Euler(-60f, 0f, 0f)), Vector3.zero, 0.04f, 0.17f, 0.05f, 10, Swatch.ArmorGrey, Swatch.ArmorWhite);
            b.Cylinder(At(new Vector3(0f, 0.15f, 0.03f), Quaternion.Euler(-60f, 0f, 0f)), Vector3.zero, 0.012f, 0.1f, 4, Swatch.Steel);
            b.Box(I, new Vector3(0f, 0.2f, 0.09f), new Vector3(0.025f, 0.025f, 0.025f), Swatch.Glow);
        }

        /// <summary>Ядерный заряд: жёлто-чёрный контейнер на опорной плите, с ручками и светящимся окном.</summary>
        private static float Nuclear(LowPolyMeshBuilder b, float y)
        {
            const float height = 0.42f;
            if (b == null) return height;
            b.ChamferBox(I, new Vector3(0f, y + 0.03f, 0f), new Vector3(0.36f, 0.06f, 0.36f), 0.02f, Swatch.MetalDark, Swatch.ArmorGrey);
            b.Cylinder(I, new Vector3(0f, y + 0.06f, 0f), 0.16f, 0.3f, 10, Swatch.Hazard);
            foreach (float band in new[] { 0.1f, 0.24f })
                b.Cylinder(I, new Vector3(0f, y + band, 0f), 0.168f, 0.04f, 10, Swatch.MetalDark);
            b.Cylinder(I, new Vector3(0f, y + 0.36f, 0f), 0.16f, 0.1f, 0.04f, 10, Swatch.ArmorGrey, Swatch.MetalDark);
            b.Cylinder(I, new Vector3(0f, y + 0.4f, 0f), 0.07f, 0.02f, 8, Swatch.Glow);
            foreach (float side in new[] { -1f, 1f })
                b.Box(I, new Vector3(side * 0.18f, y + 0.2f, 0f), new Vector3(0.025f, 0.12f, 0.06f), Swatch.Steel);
            return height;
        }
    }
}
