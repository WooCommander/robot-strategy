using System;
using System.Collections.Generic;

namespace Game.Core.Models
{
    /// <summary>Чертёж робота: одно шасси, до трёх разных орудий, необязательные электроника и ядерный заряд.</summary>
    [Serializable]
    public class RobotBlueprint
    {
        public const int MaxWeapons = 3;

        public string Id { get; set; }
        public string Name { get; set; }
        public string ChassisId { get; set; }
        public List<string> WeaponIds { get; set; } = new List<string>();
        public string ElectronicsId { get; set; }
        public string NuclearId { get; set; }

        public bool HasElectronics => !string.IsNullOrEmpty(ElectronicsId);
        public bool HasNuclear => !string.IsNullOrEmpty(NuclearId);

        public RobotBlueprint() { }

        public RobotBlueprint(string id, string name, string chassisId, IEnumerable<string> weaponIds, string electronicsId = null, string nuclearId = null)
        {
            Id = id;
            Name = name;
            ChassisId = chassisId;
            if (weaponIds != null)
            {
                foreach (string weaponId in weaponIds)
                {
                    if (!string.IsNullOrEmpty(weaponId)) WeaponIds.Add(weaponId);
                }
            }
            ElectronicsId = electronicsId;
            NuclearId = nuclearId;
        }

        public IEnumerable<string> GetAllModuleIds()
        {
            if (!string.IsNullOrEmpty(ChassisId)) yield return ChassisId;
            if (WeaponIds != null)
            {
                foreach (string weaponId in WeaponIds)
                {
                    if (!string.IsNullOrEmpty(weaponId)) yield return weaponId;
                }
            }
            if (HasElectronics) yield return ElectronicsId;
            if (HasNuclear) yield return NuclearId;
        }

        public bool HasModule(string moduleId)
        {
            foreach (string id in GetAllModuleIds())
            {
                if (id == moduleId) return true;
            }
            return false;
        }
    }
}
