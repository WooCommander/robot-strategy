using System.Collections.Generic;
using Game.Core.Models;

namespace Game.Content.Definitions
{
    public sealed class RobotPresetDefinition
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public float BuildTimeSeconds { get; set; }
        public string ChassisId { get; set; }
        public string[] WeaponIds { get; set; } = new string[0];
        public string ElectronicsId { get; set; }
        public string NuclearId { get; set; }

        // ID чертежа детерминирован: ИИ заказывает роботов внутри симуляции, и replay должен совпадать.
        public RobotBlueprint CreateBlueprint() => new RobotBlueprint(Id, Name, ChassisId, WeaponIds, ElectronicsId, NuclearId);

        public int[] GetCostByType(IReadOnlyDictionary<string, ModuleDefinition> modules)
        {
            var cost = new int[ResourceStock.TypeCount];
            foreach (string moduleId in CreateBlueprint().GetAllModuleIds())
            {
                if (modules.TryGetValue(moduleId, out var module)) cost[(int)module.Resource] += module.Cost;
            }
            return cost;
        }

        public bool Matches(RobotBlueprint blueprint)
        {
            if (blueprint == null || blueprint.ChassisId != ChassisId || blueprint.ElectronicsId != ElectronicsId || blueprint.NuclearId != NuclearId) return false;
            if (blueprint.WeaponIds == null || blueprint.WeaponIds.Count != WeaponIds.Length) return false;
            foreach (string weaponId in WeaponIds)
            {
                if (!blueprint.WeaponIds.Contains(weaponId)) return false;
            }
            return true;
        }
    }

    public static class RobotPresetCatalog
    {
        public static readonly RobotPresetDefinition Scout = new RobotPresetDefinition
        {
            Id = "scout", Name = "Разведчик", BuildTimeSeconds = 6f,
            ChassisId = DefaultContentCatalog.ChassisBipod, WeaponIds = new[] { DefaultContentCatalog.WeaponCannon }
        };
        public static readonly RobotPresetDefinition Tank = new RobotPresetDefinition
        {
            Id = "tank", Name = "Танк", BuildTimeSeconds = 10f,
            ChassisId = DefaultContentCatalog.ChassisTracks, WeaponIds = new[] { DefaultContentCatalog.WeaponPhasers, DefaultContentCatalog.WeaponCannon }
        };
        public static readonly RobotPresetDefinition Missile = new RobotPresetDefinition
        {
            Id = "missile", Name = "Ракетчик", BuildTimeSeconds = 9f,
            ChassisId = DefaultContentCatalog.ChassisTracks, WeaponIds = new[] { DefaultContentCatalog.WeaponMissiles }, ElectronicsId = DefaultContentCatalog.Electronics
        };

        public static readonly RobotPresetDefinition Heavy = new RobotPresetDefinition
        {
            Id = "heavy", Name = "Тяжёлый", BuildTimeSeconds = 12f,
            ChassisId = DefaultContentCatalog.ChassisTracks,
            WeaponIds = new[] { DefaultContentCatalog.WeaponPhasers, DefaultContentCatalog.WeaponMissiles, DefaultContentCatalog.WeaponCannon },
            ElectronicsId = DefaultContentCatalog.Electronics
        };

        public static readonly RobotPresetDefinition[] All = { Scout, Tank, Missile, Heavy };

        public static bool TryFind(RobotBlueprint blueprint, out RobotPresetDefinition preset)
        {
            foreach (var candidate in All)
            {
                if (candidate.Matches(blueprint)) { preset = candidate; return true; }
            }
            preset = null;
            return false;
        }
    }
}
