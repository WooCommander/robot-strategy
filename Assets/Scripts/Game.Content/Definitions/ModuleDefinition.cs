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
        public string ShortName { get; set; }
        public ModuleSlotType SlotType { get; set; }

        /// <summary>Цена в единицах ресурса <see cref="Resource"/>; недостачу покрывает General.</summary>
        public ResourceType Resource { get; set; }
        public int Cost { get; set; }
        public int HealthBonus { get; set; }

        // Шасси
        public float Speed { get; set; }

        // Электроника: качество решений между приказами
        public float CombatManeuverIntervalSeconds { get; set; } = 0.5f;
        public bool CanGuideMissiles { get; set; }

        // Оружие
        public WeaponType WeaponType { get; set; }
        public int Damage { get; set; }
        public float Range { get; set; }
        public float CooldownSeconds { get; set; }
        public float SplashRadius { get; set; }
        /// <summary>Скорость снаряда; 0 — мгновенный луч (фазер).</summary>
        public float ProjectileSpeed { get; set; }

        // Ядерный заряд
        public float BlastRadius { get; set; }
        public int BlastDamage { get; set; }
    }

    /// <summary>Набор деталей по мотивам Nether Earth. Числа — исходные значения прототипа для плейтеста.</summary>
    public static class DefaultContentCatalog
    {
        public const string ChassisBipod = "chassis_bipod";
        public const string ChassisTracks = "chassis_tracks";
        public const string ChassisAntiGrav = "chassis_antigrav";

        public const string WeaponCannon = "weapon_cannon";
        public const string WeaponMissiles = "weapon_missiles";
        public const string WeaponPhasers = "weapon_phasers";

        public const string Electronics = "electronics";
        public const string Nuclear = "nuclear";

        /// <summary>Интервал боевого манёвра робота без электроники.</summary>
        public const float BaseManeuverIntervalSeconds = 0.55f;

        /// <summary>Порядок деталей в конструкторе — как в оригинальном экране сборки.</summary>
        public static readonly string[] ConstructorOrder =
        {
            Electronics, Nuclear, WeaponPhasers, WeaponMissiles, WeaponCannon, ChassisAntiGrav, ChassisTracks, ChassisBipod
        };

        public static Dictionary<string, ModuleDefinition> CreateDefaultModules()
        {
            return new Dictionary<string, ModuleDefinition>
            {
                // Шасси: тратят ресурс «Шасси»
                [ChassisBipod] = new ModuleDefinition
                {
                    Id = ChassisBipod, Name = "Двуногое шасси", ShortName = "ДВУНОГОЕ", SlotType = ModuleSlotType.Chassis,
                    Resource = ResourceType.Chassis, Cost = 3, HealthBonus = 80, Speed = 3.0f
                },
                [ChassisTracks] = new ModuleDefinition
                {
                    Id = ChassisTracks, Name = "Гусеничное шасси", ShortName = "ГУСЕНИЦЫ", SlotType = ModuleSlotType.Chassis,
                    Resource = ResourceType.Chassis, Cost = 5, HealthBonus = 140, Speed = 3.6f
                },
                [ChassisAntiGrav] = new ModuleDefinition
                {
                    Id = ChassisAntiGrav, Name = "Антигравитационное шасси", ShortName = "АНТИГРАВ", SlotType = ModuleSlotType.Chassis,
                    Resource = ResourceType.Chassis, Cost = 10, HealthBonus = 100, Speed = 5.0f
                },

                // Оружие: каждое не больше одного на робота
                [WeaponCannon] = new ModuleDefinition
                {
                    Id = WeaponCannon, Name = "Пушка", ShortName = "ПУШКА", SlotType = ModuleSlotType.Weapon,
                    Resource = ResourceType.Cannon, Cost = 2, HealthBonus = 10,
                    WeaponType = WeaponType.DirectCannon, Damage = 35, Range = 6.0f, CooldownSeconds = 1.5f, ProjectileSpeed = 18f
                },
                [WeaponMissiles] = new ModuleDefinition
                {
                    Id = WeaponMissiles, Name = "Ракеты", ShortName = "РАКЕТЫ", SlotType = ModuleSlotType.Weapon,
                    Resource = ResourceType.Missiles, Cost = 4, HealthBonus = 10,
                    WeaponType = WeaponType.AreaMissile, Damage = 50, Range = 9.0f, CooldownSeconds = 3.5f, SplashRadius = 2.0f, ProjectileSpeed = 10f
                },
                [WeaponPhasers] = new ModuleDefinition
                {
                    Id = WeaponPhasers, Name = "Фазер", ShortName = "ФАЗЕР", SlotType = ModuleSlotType.Weapon,
                    Resource = ResourceType.Phasers, Cost = 4, HealthBonus = 10,
                    WeaponType = WeaponType.Phaser, Damage = 30, Range = 7.0f, CooldownSeconds = 2.0f, ProjectileSpeed = 0f
                },

                // Электроника: частый пересчёт манёвра и наведение ракет в полёте
                [Electronics] = new ModuleDefinition
                {
                    Id = Electronics, Name = "Электроника", ShortName = "ЭЛЕКТРОНИКА", SlotType = ModuleSlotType.Electronics,
                    Resource = ResourceType.Electronics, Cost = 3,
                    CombatManeuverIntervalSeconds = 0.15f, CanGuideMissiles = true
                },

                // Ядерный заряд: подрыв по приказу уничтожает всё вокруг, включая носителя
                [Nuclear] = new ModuleDefinition
                {
                    Id = Nuclear, Name = "Ядерный заряд", ShortName = "ЯДЕРНЫЙ", SlotType = ModuleSlotType.Nuclear,
                    Resource = ResourceType.Nuclear, Cost = 20, BlastRadius = 4.5f, BlastDamage = 1000
                }
            };
        }

        public static string GetResourceName(ResourceType type) => type switch
        {
            ResourceType.General => "Общие",
            ResourceType.Electronics => "Электроника",
            ResourceType.Nuclear => "Ядерные",
            ResourceType.Phasers => "Фазеры",
            ResourceType.Missiles => "Ракеты",
            ResourceType.Cannon => "Пушки",
            ResourceType.Chassis => "Шасси",
            _ => type.ToString()
        };
    }
}
