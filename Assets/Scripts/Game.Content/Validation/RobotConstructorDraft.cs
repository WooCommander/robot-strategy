using System;
using System.Collections.Generic;
using Game.Content.Definitions;
using Game.Core.Models;
using Game.Core.Types;

namespace Game.Content.Validation
{
    public sealed class RobotConstructorDraft
    {
        private readonly IReadOnlyDictionary<string, ModuleDefinition> _catalog;

        public string ChassisId { get; private set; }
        public string BodyId { get; private set; }
        public string WeaponId { get; private set; }
        public string ControlModuleId { get; private set; }
        public string EquipmentId { get; private set; }

        public RobotConstructorDraft(IReadOnlyDictionary<string, ModuleDefinition> catalog)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        public bool Select(ModuleSlotType slot, string moduleId)
        {
            if (slot == ModuleSlotType.Equipment && string.IsNullOrEmpty(moduleId))
            {
                EquipmentId = null;
                return true;
            }

            if (string.IsNullOrEmpty(moduleId) || !_catalog.TryGetValue(moduleId, out var module) || module.SlotType != slot)
            {
                return false;
            }

            switch (slot)
            {
                case ModuleSlotType.Chassis: ChassisId = moduleId; break;
                case ModuleSlotType.Body: BodyId = moduleId; break;
                case ModuleSlotType.Weapon: WeaponId = moduleId; break;
                case ModuleSlotType.Control: ControlModuleId = moduleId; break;
                case ModuleSlotType.Equipment: EquipmentId = moduleId; break;
                default: return false;
            }
            return true;
        }

        public RobotBlueprint CreateBlueprint(string id, string name) => new RobotBlueprint(
            id, name, ChassisId, BodyId, WeaponId, ControlModuleId, EquipmentId);

        public AssemblyValidationResult Validate(string id = "constructor_preview") =>
            RobotAssemblyValidator.Validate(CreateBlueprint(id, "Конструктор"), _catalog);

        public IEnumerable<ModuleDefinition> GetModules(ModuleSlotType slot)
        {
            foreach (var module in _catalog.Values)
            {
                if (module.SlotType == slot) yield return module;
            }
        }
    }
}
