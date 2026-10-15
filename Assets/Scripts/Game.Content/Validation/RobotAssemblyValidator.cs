using System.Collections.Generic;
using Game.Content.Definitions;
using Game.Core.Models;
using Game.Core.Types;

namespace Game.Content.Validation
{
    public class AssemblyValidationResult
    {
        public bool IsValid => Errors.Count == 0;
        public List<string> Errors { get; } = new List<string>();

        /// <summary>Цена по типам ресурсов (индекс — <see cref="ResourceType"/>).</summary>
        public int[] CostByType { get; } = new int[ResourceStock.TypeCount];
        public int TotalCost { get; set; }
        public int TotalHealth { get; set; }
        public float Speed { get; set; }
        public MobilityClass Mobility { get; set; }

        public static AssemblyValidationResult Fail(string error)
        {
            var result = new AssemblyValidationResult();
            result.Errors.Add(error);
            return result;
        }
    }

    public static class RobotAssemblyValidator
    {
        public static AssemblyValidationResult Validate(RobotBlueprint blueprint, IReadOnlyDictionary<string, ModuleDefinition> catalog)
        {
            if (blueprint == null) return AssemblyValidationResult.Fail("Чертёж не может быть пустым.");
            var result = new AssemblyValidationResult();

            ModuleDefinition chassis = null;
            if (string.IsNullOrEmpty(blueprint.ChassisId)) result.Errors.Add("Не выбрано шасси.");
            else chassis = Require(blueprint.ChassisId, ModuleSlotType.Chassis, "шасси", catalog, result);

            int weaponCount = blueprint.WeaponIds?.Count ?? 0;
            if (weaponCount > RobotBlueprint.MaxWeapons) result.Errors.Add($"Не больше {RobotBlueprint.MaxWeapons} орудий на робота.");
            var seenWeapons = new HashSet<string>();
            var weapons = new List<ModuleDefinition>();
            if (blueprint.WeaponIds != null)
            {
                foreach (string weaponId in blueprint.WeaponIds)
                {
                    if (!seenWeapons.Add(weaponId))
                    {
                        result.Errors.Add("Каждый вид оружия можно установить только один раз.");
                        continue;
                    }
                    ModuleDefinition weapon = Require(weaponId, ModuleSlotType.Weapon, "оружие", catalog, result);
                    if (weapon != null) weapons.Add(weapon);
                }
            }

            ModuleDefinition electronics = blueprint.HasElectronics ? Require(blueprint.ElectronicsId, ModuleSlotType.Electronics, "электроника", catalog, result) : null;
            ModuleDefinition nuclear = blueprint.HasNuclear ? Require(blueprint.NuclearId, ModuleSlotType.Nuclear, "ядерный заряд", catalog, result) : null;

            if (weaponCount == 0 && !blueprint.HasNuclear) result.Errors.Add("Нужно оружие или ядерный заряд.");

            AddModule(chassis, result);
            foreach (ModuleDefinition weapon in weapons) AddModule(weapon, result);
            AddModule(electronics, result);
            AddModule(nuclear, result);
            result.Speed = chassis?.Speed ?? 0f;
            result.Mobility = chassis?.Mobility ?? MobilityClass.Walker;
            return result;
        }

        private static ModuleDefinition Require(string id, ModuleSlotType slot, string label, IReadOnlyDictionary<string, ModuleDefinition> catalog, AssemblyValidationResult result)
        {
            if (catalog.TryGetValue(id, out ModuleDefinition module) && module.SlotType == slot) return module;
            result.Errors.Add($"Деталь «{label}» '{id}' не найдена в каталоге.");
            return null;
        }

        private static void AddModule(ModuleDefinition module, AssemblyValidationResult result)
        {
            if (module == null) return;
            result.CostByType[(int)module.Resource] += module.Cost;
            result.TotalCost += module.Cost;
            result.TotalHealth += module.HealthBonus;
        }
    }
}
