using System;
using System.Collections.Generic;
using Game.Core.Models;

namespace Game.Content.Definitions
{
    public sealed class RobotPresetDefinition
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string CardTitle { get; set; }
        public string ModulesLabel { get; set; }
        public float BuildTimeSeconds { get; set; }
        public string ChassisId { get; set; }
        public string BodyId { get; set; }
        public string WeaponId { get; set; }
        public string EquipmentId { get; set; }

        public RobotBlueprint CreateBlueprint() => new RobotBlueprint(
            $"{Id}_{Guid.NewGuid():N}", Name, ChassisId, BodyId, WeaponId, EquipmentId);

        public int GetCost(IReadOnlyDictionary<string, ModuleDefinition> modules)
        {
            int cost = 0;
            foreach (string moduleId in CreateBlueprint().GetAllModuleIds())
            {
                if (modules.TryGetValue(moduleId, out var module)) cost += module.Cost;
            }
            return cost;
        }

        public bool Matches(RobotBlueprint blueprint) => blueprint != null &&
            blueprint.ChassisId == ChassisId && blueprint.BodyId == BodyId &&
            blueprint.WeaponId == WeaponId && blueprint.EquipmentId == EquipmentId;
    }

    public static class RobotPresetCatalog
    {
        public static readonly RobotPresetDefinition Scout = new RobotPresetDefinition
        {
            Id = "scout", Name = "Разведчик", CardTitle = "РАЗВЕДЧИК", ModulesLabel = "Ноги + Пушка", BuildTimeSeconds = 8f,
            ChassisId = DefaultContentCatalog.ChassisBiped, BodyId = DefaultContentCatalog.BodyLight, WeaponId = DefaultContentCatalog.WeaponCannon
        };
        public static readonly RobotPresetDefinition Tank = new RobotPresetDefinition
        {
            Id = "tank", Name = "Танк", CardTitle = "ТЯЖЁЛЫЙ ТАНК", ModulesLabel = "Гусеницы + Броня", BuildTimeSeconds = 12f,
            ChassisId = DefaultContentCatalog.ChassisTracks, BodyId = DefaultContentCatalog.BodyHeavy, WeaponId = DefaultContentCatalog.WeaponCannon, EquipmentId = DefaultContentCatalog.EquipArmor
        };
        public static readonly RobotPresetDefinition Missile = new RobotPresetDefinition
        {
            Id = "missile", Name = "Ракетчик", CardTitle = "РАКЕТЧИК", ModulesLabel = "Гусеницы + Ракеты", BuildTimeSeconds = 10f,
            ChassisId = DefaultContentCatalog.ChassisTracks, BodyId = DefaultContentCatalog.BodyLight, WeaponId = DefaultContentCatalog.WeaponMissiles
        };

        private static readonly RobotPresetDefinition[] Presets = { Scout, Tank, Missile };

        public static bool TryFind(RobotBlueprint blueprint, out RobotPresetDefinition preset)
        {
            foreach (var candidate in Presets)
            {
                if (candidate.Matches(blueprint)) { preset = candidate; return true; }
            }
            preset = null;
            return false;
        }
    }
}
