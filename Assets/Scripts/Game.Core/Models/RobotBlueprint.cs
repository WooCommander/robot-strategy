using System;
using System.Collections.Generic;

namespace Game.Core.Models
{
    [Serializable]
    public class RobotBlueprint
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string ChassisId { get; set; }
        public string BodyId { get; set; }
        public string WeaponId { get; set; }
        public string ControlModuleId { get; set; }
        public string EquipmentId { get; set; }

        public RobotBlueprint() { }

        public RobotBlueprint(string id, string name, string chassisId, string bodyId, string weaponId, string controlModuleId, string equipmentId = null)
        {
            Id = id;
            Name = name;
            ChassisId = chassisId;
            BodyId = bodyId;
            WeaponId = weaponId;
            ControlModuleId = controlModuleId;
            EquipmentId = equipmentId;
        }

        public IEnumerable<string> GetAllModuleIds()
        {
            if (!string.IsNullOrEmpty(ChassisId)) yield return ChassisId;
            if (!string.IsNullOrEmpty(BodyId)) yield return BodyId;
            if (!string.IsNullOrEmpty(WeaponId)) yield return WeaponId;
            if (!string.IsNullOrEmpty(ControlModuleId)) yield return ControlModuleId;
            if (!string.IsNullOrEmpty(EquipmentId)) yield return EquipmentId;
        }
    }
}
