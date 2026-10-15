using System;
using System.Collections.Generic;
using Game.Content.Definitions;
using Game.Core.Models;
using Game.Core.Types;

namespace Game.Content.Validation
{
    /// <summary>
    /// Черновик экрана сборки: шасси выбирается одно, орудия, электроника и заряд включаются и выключаются нажатием.
    /// </summary>
    public sealed class RobotConstructorDraft
    {
        private readonly IReadOnlyDictionary<string, ModuleDefinition> _catalog;
        private readonly List<string> _weaponIds = new List<string>();

        public string ChassisId { get; private set; }
        public IReadOnlyList<string> WeaponIds => _weaponIds;
        public string ElectronicsId { get; private set; }
        public string NuclearId { get; private set; }

        public RobotConstructorDraft(IReadOnlyDictionary<string, ModuleDefinition> catalog)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        public bool IsSelected(string moduleId) =>
            moduleId == ChassisId || moduleId == ElectronicsId || moduleId == NuclearId || _weaponIds.Contains(moduleId);

        /// <summary>Нажатие на деталь: шасси заменяет прежнее, остальные детали переключаются.</summary>
        public bool Toggle(string moduleId)
        {
            if (string.IsNullOrEmpty(moduleId) || !_catalog.TryGetValue(moduleId, out ModuleDefinition module)) return false;
            switch (module.SlotType)
            {
                case ModuleSlotType.Chassis:
                    ChassisId = ChassisId == moduleId ? null : moduleId;
                    return true;
                case ModuleSlotType.Weapon:
                    if (_weaponIds.Remove(moduleId)) return true;
                    if (_weaponIds.Count >= RobotBlueprint.MaxWeapons) return false;
                    // Орудия хранятся в порядке каталога, чтобы одинаковые сборки давали одинаковый чертёж.
                    _weaponIds.Add(moduleId);
                    _weaponIds.Sort((left, right) => Array.IndexOf(DefaultContentCatalog.ConstructorOrder, left).CompareTo(Array.IndexOf(DefaultContentCatalog.ConstructorOrder, right)));
                    return true;
                case ModuleSlotType.Electronics:
                    ElectronicsId = ElectronicsId == moduleId ? null : moduleId;
                    return true;
                case ModuleSlotType.Nuclear:
                    NuclearId = NuclearId == moduleId ? null : moduleId;
                    return true;
                default:
                    return false;
            }
        }

        public void Clear()
        {
            ChassisId = null;
            ElectronicsId = null;
            NuclearId = null;
            _weaponIds.Clear();
        }

        public RobotBlueprint CreateBlueprint(string id, string name) =>
            new RobotBlueprint(id, name, ChassisId, _weaponIds, ElectronicsId, NuclearId);

        public AssemblyValidationResult Validate(string id = "constructor_preview") =>
            RobotAssemblyValidator.Validate(CreateBlueprint(id, "Конструктор"), _catalog);

        /// <summary>Детали в порядке экрана сборки.</summary>
        public IEnumerable<ModuleDefinition> GetModulesInDisplayOrder()
        {
            foreach (string id in DefaultContentCatalog.ConstructorOrder)
            {
                if (_catalog.TryGetValue(id, out ModuleDefinition module)) yield return module;
            }
        }

        /// <summary>Короткое имя по составу, как у роботов в оригинале: «ГУСЕНИЦЫ + ПУШКА + ФАЗЕР».</summary>
        public string DescribeSelection()
        {
            var parts = new List<string>();
            foreach (ModuleDefinition module in GetModulesInDisplayOrder())
            {
                if (IsSelected(module.Id) && module.SlotType == ModuleSlotType.Chassis) parts.Insert(0, module.ShortName);
                else if (IsSelected(module.Id)) parts.Add(module.ShortName);
            }
            return parts.Count == 0 ? "—" : string.Join(" + ", parts);
        }
    }
}
