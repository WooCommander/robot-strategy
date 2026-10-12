using System;
using System.Collections.Generic;
using Game.Core.Types;

namespace Game.Content.Definitions
{
    [Serializable]
    public class ModuleDefinition
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public ModuleSlotType SlotType { get; set; }

        public int Cost { get; set; }
        public int Mass { get; set; }
        public int HealthBonus { get; set; }
        public float Speed { get; set; }
        public int MaxLoadCapacity { get; set; } // Только для Chassis

        // Для оружия
        public WeaponType WeaponType { get; set; }
        public int Damage { get; set; }
        public float Range { get; set; }
        public float CooldownSeconds { get; set; }
        public float SplashRadius { get; set; }
    }

    public static class DefaultContentCatalog
    {
        public const string ChassisTracks = "chassis_tracks";
        public const string ChassisBiped = "chassis_biped";

        public const string BodyLight = "body_light";
        public const string BodyHeavy = "body_heavy";

        public const string WeaponCannon = "weapon_cannon";
        public const string WeaponMissiles = "weapon_missiles";

        public const string EquipArmor = "equip_armor";

        public static Dictionary<string, ModuleDefinition> CreateDefaultModules()
        {
            return new Dictionary<string, ModuleDefinition>
            {
                // Шасси
                [ChassisTracks] = new ModuleDefinition
                {
                    Id = ChassisTracks,
                    Name = "Гусеничное шасси",
                    SlotType = ModuleSlotType.Chassis,
                    Cost = 120,
                    Mass = 60,
                    Speed = 3.0f,
                    MaxLoadCapacity = 230
                },
                [ChassisBiped] = new ModuleDefinition
                {
                    Id = ChassisBiped,
                    Name = "Двуногое шасси",
                    SlotType = ModuleSlotType.Chassis,
                    Cost = 100,
                    Mass = 40,
                    Speed = 4.5f,
                    MaxLoadCapacity = 160
                },

                // Корпуса
                [BodyLight] = new ModuleDefinition
                {
                    Id = BodyLight,
                    Name = "Лёгкий корпус",
                    SlotType = ModuleSlotType.Body,
                    Cost = 100,
                    Mass = 30,
                    HealthBonus = 120
                },
                [BodyHeavy] = new ModuleDefinition
                {
                    Id = BodyHeavy,
                    Name = "Тяжёлый корпус",
                    SlotType = ModuleSlotType.Body,
                    Cost = 220,
                    Mass = 80,
                    HealthBonus = 260
                },

                // Оружие
                [WeaponCannon] = new ModuleDefinition
                {
                    Id = WeaponCannon,
                    Name = "Пушка",
                    SlotType = ModuleSlotType.Weapon,
                    WeaponType = WeaponType.DirectCannon,
                    Cost = 150,
                    Mass = 40,
                    Damage = 35,
                    Range = 6.0f,
                    CooldownSeconds = 1.5f,
                    SplashRadius = 0f
                },
                [WeaponMissiles] = new ModuleDefinition
                {
                    Id = WeaponMissiles,
                    Name = "Ракетная установка",
                    SlotType = ModuleSlotType.Weapon,
                    WeaponType = WeaponType.AreaMissile,
                    Cost = 220,
                    Mass = 55,
                    Damage = 50,
                    Range = 9.0f,
                    CooldownSeconds = 3.5f,
                    SplashRadius = 2.0f
                },

                // Оборудование
                [EquipArmor] = new ModuleDefinition
                {
                    Id = EquipArmor,
                    Name = "Усиленная броня",
                    SlotType = ModuleSlotType.Equipment,
                    Cost = 80,
                    Mass = 35,
                    HealthBonus = 80
                }
            };
        }
    }
}
