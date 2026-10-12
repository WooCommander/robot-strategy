using System.Collections.Generic;
using UnityEngine;
using Game.Content.Definitions;
using Game.Core.Models;
using Game.Core.Types;
using Game.Presentation.Views;

namespace Game.Presentation.Robots
{
    public static class ModularRobotBuilder
    {
        public static GameObject BuildRobotModel(RobotBlueprint blueprint, PlayerId owner)
        {
            var root = new GameObject($"Robot_{blueprint?.Name ?? "Modular"}");
            Color teamColor = BuildingView.GetPlayerColor(owner);
            Color darkMetal = new Color(0.2f, 0.22f, 0.25f);
            Color armorMetal = new Color(0.35f, 0.38f, 0.42f);

            var teamMat = CreateSimpleMaterial(teamColor, "Mat_TeamColor");
            var darkMat = CreateSimpleMaterial(darkMetal, "Mat_DarkMetal");
            var armorMat = CreateSimpleMaterial(armorMetal, "Mat_ArmorMetal");

            // 1. Шасси (Chassis)
            var chassisGo = new GameObject("Chassis");
            chassisGo.transform.SetParent(root.transform, false);

            float bodyHeight = 0.5f;

            if (blueprint?.ChassisId == DefaultContentCatalog.ChassisTracks)
            {
                // Гусеничное шасси: центральная база + 2 гусеничных трака
                bodyHeight = 0.55f;

                var mainChassis = GameObject.CreatePrimitive(PrimitiveType.Cube);
                mainChassis.name = "Chassis_Center";
                mainChassis.transform.SetParent(chassisGo.transform, false);
                mainChassis.transform.localScale = new Vector3(0.9f, 0.3f, 1.3f);
                mainChassis.transform.localPosition = new Vector3(0f, 0.2f, 0f);
                mainChassis.GetComponent<MeshRenderer>().sharedMaterial = darkMat;

                // Левый трак
                var leftTrack = GameObject.CreatePrimitive(PrimitiveType.Cube);
                leftTrack.name = "Track_Left";
                leftTrack.transform.SetParent(chassisGo.transform, false);
                leftTrack.transform.localScale = new Vector3(0.3f, 0.45f, 1.5f);
                leftTrack.transform.localPosition = new Vector3(-0.55f, 0.22f, 0f);
                leftTrack.GetComponent<MeshRenderer>().sharedMaterial = darkMat;

                // Правый трак
                var rightTrack = GameObject.CreatePrimitive(PrimitiveType.Cube);
                rightTrack.name = "Track_Right";
                rightTrack.transform.SetParent(chassisGo.transform, false);
                rightTrack.transform.localScale = new Vector3(0.3f, 0.45f, 1.5f);
                rightTrack.transform.localPosition = new Vector3(0.55f, 0.22f, 0f);
                rightTrack.GetComponent<MeshRenderer>().sharedMaterial = darkMat;
            }
            else
            {
                // Двуногое шасси (Biped): опора + 2 шагающие ноги
                bodyHeight = 0.75f;

                var hip = GameObject.CreatePrimitive(PrimitiveType.Cube);
                hip.name = "Chassis_Hip";
                hip.transform.SetParent(chassisGo.transform, false);
                hip.transform.localScale = new Vector3(0.8f, 0.25f, 0.6f);
                hip.transform.localPosition = new Vector3(0f, 0.55f, 0f);
                hip.GetComponent<MeshRenderer>().sharedMaterial = darkMat;

                // Нога левая
                var legLeft = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                legLeft.name = "Leg_Left";
                legLeft.transform.SetParent(chassisGo.transform, false);
                legLeft.transform.localScale = new Vector3(0.2f, 0.3f, 0.2f);
                legLeft.transform.localPosition = new Vector3(-0.4f, 0.3f, 0f);
                legLeft.GetComponent<MeshRenderer>().sharedMaterial = darkMat;

                // Нога правая
                var legRight = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                legRight.name = "Leg_Right";
                legRight.transform.SetParent(chassisGo.transform, false);
                legRight.transform.localScale = new Vector3(0.2f, 0.3f, 0.2f);
                legRight.transform.localPosition = new Vector3(0.4f, 0.3f, 0f);
                legRight.GetComponent<MeshRenderer>().sharedMaterial = darkMat;
            }

            // 2. Корпус (Body)
            var bodyGo = new GameObject("Body");
            bodyGo.transform.SetParent(root.transform, false);
            bodyGo.transform.localPosition = new Vector3(0f, bodyHeight, 0f);

            float weaponMountHeight = 0.45f;

            if (blueprint?.BodyId == DefaultContentCatalog.BodyHeavy)
            {
                // Тяжёлый корпус: массивный блок с бронескосами
                weaponMountHeight = 0.6f;

                var mainBlock = GameObject.CreatePrimitive(PrimitiveType.Cube);
                mainBlock.name = "Body_HeavyMain";
                mainBlock.transform.SetParent(bodyGo.transform, false);
                mainBlock.transform.localScale = new Vector3(1.1f, 0.6f, 1.2f);
                mainBlock.transform.localPosition = new Vector3(0f, 0.3f, 0f);
                mainBlock.GetComponent<MeshRenderer>().sharedMaterial = teamMat;
            }
            else
            {
                // Лёгкий корпус: компактный клиновидный блок
                weaponMountHeight = 0.4f;

                var mainBlock = GameObject.CreatePrimitive(PrimitiveType.Cube);
                mainBlock.name = "Body_LightMain";
                mainBlock.transform.SetParent(bodyGo.transform, false);
                mainBlock.transform.localScale = new Vector3(0.8f, 0.4f, 0.9f);
                mainBlock.transform.localPosition = new Vector3(0f, 0.2f, 0f);
                mainBlock.GetComponent<MeshRenderer>().sharedMaterial = teamMat;
            }

            // 3. Оборудование (Броня)
            if (blueprint?.EquipmentId == DefaultContentCatalog.EquipArmor)
            {
                // Бортовые экраны слева и справа
                var armorLeft = GameObject.CreatePrimitive(PrimitiveType.Cube);
                armorLeft.name = "Armor_Shield_L";
                armorLeft.transform.SetParent(bodyGo.transform, false);
                armorLeft.transform.localScale = new Vector3(0.12f, 0.45f, 1.1f);
                armorLeft.transform.localPosition = new Vector3(-0.62f, 0.25f, 0f);
                armorLeft.GetComponent<MeshRenderer>().sharedMaterial = armorMat;

                var armorRight = GameObject.CreatePrimitive(PrimitiveType.Cube);
                armorRight.name = "Armor_Shield_R";
                armorRight.transform.SetParent(bodyGo.transform, false);
                armorRight.transform.localScale = new Vector3(0.12f, 0.45f, 1.1f);
                armorRight.transform.localPosition = new Vector3(0.62f, 0.25f, 0f);
                armorRight.GetComponent<MeshRenderer>().sharedMaterial = armorMat;
            }

            // 4. Оружие (Weapon)
            var weaponGo = new GameObject("Weapon_Turret");
            weaponGo.transform.SetParent(bodyGo.transform, false);
            weaponGo.transform.localPosition = new Vector3(0f, weaponMountHeight, 0.05f);

            // Башенка
            var turretBase = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            turretBase.name = "Turret_Base";
            turretBase.transform.SetParent(weaponGo.transform, false);
            turretBase.transform.localScale = new Vector3(0.5f, 0.15f, 0.5f);
            turretBase.transform.localPosition = new Vector3(0f, 0.075f, 0f);
            turretBase.GetComponent<MeshRenderer>().sharedMaterial = darkMat;

            if (blueprint?.WeaponId == DefaultContentCatalog.WeaponMissiles)
            {
                // Ракетная установка: пусковой контейнер
                var launcher = GameObject.CreatePrimitive(PrimitiveType.Cube);
                launcher.name = "Missile_Pod";
                launcher.transform.SetParent(weaponGo.transform, false);
                launcher.transform.localScale = new Vector3(0.6f, 0.35f, 0.7f);
                launcher.transform.localPosition = new Vector3(0f, 0.3f, 0f);
                launcher.transform.localRotation = Quaternion.Euler(-15f, 0f, 0f); // Наклон вверх
                launcher.GetComponent<MeshRenderer>().sharedMaterial = teamMat;
            }
            else
            {
                // Пушка: танковый ствол
                var mantlet = GameObject.CreatePrimitive(PrimitiveType.Cube);
                mantlet.name = "Gun_Mantlet";
                mantlet.transform.SetParent(weaponGo.transform, false);
                mantlet.transform.localScale = new Vector3(0.35f, 0.25f, 0.4f);
                mantlet.transform.localPosition = new Vector3(0f, 0.2f, 0.1f);
                mantlet.GetComponent<MeshRenderer>().sharedMaterial = darkMat;

                var barrel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                barrel.name = "Gun_Barrel";
                barrel.transform.SetParent(mantlet.transform, false);
                barrel.transform.localScale = new Vector3(0.12f, 0.6f, 0.12f);
                barrel.transform.localPosition = new Vector3(0f, 0f, 0.65f);
                barrel.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                barrel.GetComponent<MeshRenderer>().sharedMaterial = darkMat;
            }

            // Добавляем CapsuleCollider на корень для выбора юнита
            var col = root.AddComponent<CapsuleCollider>();
            col.radius = 0.7f;
            col.height = bodyHeight + weaponMountHeight + 0.6f;
            col.center = new Vector3(0f, col.height * 0.5f, 0f);

            return root;
        }

        private static Material CreateSimpleMaterial(Color color, string name)
        {
            var shader = Shader.Find("Standard") ?? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default");
            var mat = new Material(shader) { name = name };
            mat.color = color;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            return mat;
        }
    }
}
