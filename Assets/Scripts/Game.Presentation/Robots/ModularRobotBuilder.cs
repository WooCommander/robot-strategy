using UnityEngine;
using Game.Content.Definitions;
using Game.Core.Models;
using Game.Core.Types;
using Game.Presentation.Rendering;
using Game.Presentation.Views;

namespace Game.Presentation.Robots
{
    /// <summary>
    /// Собирает модель робота стопкой, как в Nether Earth: шасси внизу, над ним орудия,
    /// сверху электроника и ядерный заряд. Силуэт сразу показывает состав робота.
    /// </summary>
    public static class ModularRobotBuilder
    {
        public static GameObject BuildRobotModel(RobotBlueprint blueprint, PlayerId owner)
        {
            var root = new GameObject($"Robot_{blueprint?.Name ?? "Modular"}");
            var teamMat = MaterialFactory.CreateLit("Mat_TeamColor", BuildingView.GetPlayerColor(owner), 0.3f, 0.55f);
            var darkMat = MaterialFactory.CreateLit("Mat_DarkMetal", new Color(0.2f, 0.22f, 0.25f), 0.75f, 0.45f);
            var lightMat = MaterialFactory.CreateLit("Mat_LightMetal", new Color(0.55f, 0.58f, 0.62f), 0.65f, 0.5f);

            float height = BuildChassis(root.transform, blueprint?.ChassisId, darkMat, lightMat, teamMat);
            if (blueprint?.WeaponIds != null)
            {
                foreach (string weaponId in blueprint.WeaponIds)
                {
                    height = BuildWeapon(root.transform, weaponId, height, darkMat, teamMat);
                }
            }
            if (blueprint != null && blueprint.HasElectronics)
            {
                AddPart(root.transform, "Electronics_Box", PrimitiveType.Cube, new Vector3(0f, height + 0.12f, -0.1f), new Vector3(0.45f, 0.24f, 0.4f), lightMat);
                AddPart(root.transform, "Electronics_Antenna", PrimitiveType.Cylinder, new Vector3(0.15f, height + 0.45f, -0.15f), new Vector3(0.04f, 0.22f, 0.04f), darkMat);
                height += 0.3f;
            }
            if (blueprint != null && blueprint.HasNuclear)
            {
                var nuclearMat = MaterialFactory.CreateEmissive("Mat_Nuclear", new Color(0.95f, 0.8f, 0.1f), 1.5f);
                AddPart(root.transform, "Nuclear_Core", PrimitiveType.Sphere, new Vector3(0f, height + 0.22f, 0f), Vector3.one * 0.45f, nuclearMat);
                height += 0.45f;
            }

            var collider = root.AddComponent<CapsuleCollider>();
            collider.radius = 0.7f;
            collider.height = height + 0.2f;
            collider.center = new Vector3(0f, collider.height * 0.5f, 0f);
            return root;
        }

        /// <summary>Строит шасси и возвращает высоту его верхней площадки.</summary>
        private static float BuildChassis(Transform root, string chassisId, Material darkMat, Material lightMat, Material teamMat)
        {
            if (chassisId == DefaultContentCatalog.ChassisTracks)
            {
                AddPart(root, "Track_Left", PrimitiveType.Cube, new Vector3(-0.55f, 0.22f, 0f), new Vector3(0.3f, 0.45f, 1.5f), darkMat);
                AddPart(root, "Track_Right", PrimitiveType.Cube, new Vector3(0.55f, 0.22f, 0f), new Vector3(0.3f, 0.45f, 1.5f), darkMat);
                AddPart(root, "Chassis_Deck", PrimitiveType.Cube, new Vector3(0f, 0.4f, 0f), new Vector3(1.0f, 0.2f, 1.2f), teamMat);
                return 0.5f;
            }
            if (chassisId == DefaultContentCatalog.ChassisAntiGrav)
            {
                // Антиграв парит над землёй: светлый диск-излучатель и корпус над ним.
                AddPart(root, "AntiGrav_Emitter", PrimitiveType.Cylinder, new Vector3(0f, 0.25f, 0f), new Vector3(1.2f, 0.06f, 1.2f), lightMat);
                AddPart(root, "AntiGrav_Deck", PrimitiveType.Cube, new Vector3(0f, 0.45f, 0f), new Vector3(0.9f, 0.25f, 0.9f), teamMat);
                return 0.58f;
            }

            AddPart(root, "Leg_Left", PrimitiveType.Cylinder, new Vector3(-0.35f, 0.3f, 0f), new Vector3(0.2f, 0.3f, 0.2f), darkMat);
            AddPart(root, "Leg_Right", PrimitiveType.Cylinder, new Vector3(0.35f, 0.3f, 0f), new Vector3(0.2f, 0.3f, 0.2f), darkMat);
            AddPart(root, "Chassis_Hip", PrimitiveType.Cube, new Vector3(0f, 0.68f, 0f), new Vector3(0.8f, 0.25f, 0.65f), teamMat);
            return 0.8f;
        }

        /// <summary>Добавляет ярус орудия и возвращает новую высоту стопки.</summary>
        private static float BuildWeapon(Transform root, string weaponId, float baseHeight, Material darkMat, Material teamMat)
        {
            var mount = new GameObject("Weapon_" + weaponId).transform;
            mount.SetParent(root, false);
            mount.localPosition = new Vector3(0f, baseHeight, 0f);

            if (weaponId == DefaultContentCatalog.WeaponMissiles)
            {
                AddPart(mount, "Missile_Pod", PrimitiveType.Cube, new Vector3(0f, 0.17f, 0f), new Vector3(0.7f, 0.32f, 0.7f), teamMat);
                for (int index = -1; index <= 1; index++)
                {
                    AddPart(mount, "Missile_Tube" + index, PrimitiveType.Cylinder, new Vector3(index * 0.2f, 0.2f, 0.4f), new Vector3(0.12f, 0.12f, 0.12f), darkMat)
                        .transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                }
                return baseHeight + 0.34f;
            }
            if (weaponId == DefaultContentCatalog.WeaponPhasers)
            {
                AddPart(mount, "Phaser_Housing", PrimitiveType.Cube, new Vector3(0f, 0.14f, 0f), new Vector3(0.6f, 0.26f, 0.6f), darkMat);
                var lens = AddPart(mount, "Phaser_Emitter", PrimitiveType.Cylinder, new Vector3(0f, 0.14f, 0.45f), new Vector3(0.22f, 0.15f, 0.22f), MaterialFactory.CreateEmissive("Mat_Phaser", new Color(0.3f, 0.95f, 1f), 3f));
                lens.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                return baseHeight + 0.28f;
            }

            AddPart(mount, "Gun_Mantlet", PrimitiveType.Cube, new Vector3(0f, 0.13f, 0f), new Vector3(0.5f, 0.26f, 0.5f), teamMat);
            var barrel = AddPart(mount, "Gun_Barrel", PrimitiveType.Cylinder, new Vector3(0f, 0.13f, 0.55f), new Vector3(0.12f, 0.35f, 0.12f), darkMat);
            barrel.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            return baseHeight + 0.28f;
        }

        private static GameObject AddPart(Transform parent, string name, PrimitiveType primitive, Vector3 localPosition, Vector3 localScale, Material material)
        {
            var part = GameObject.CreatePrimitive(primitive);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = localScale;
            part.GetComponent<MeshRenderer>().sharedMaterial = material;
            return part;
        }
    }
}
