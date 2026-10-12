using System;
using System.Collections.Generic;
using Game.Content.Definitions;
using Game.Core.Models;
using Game.Core.Types;

namespace Game.Content.Validation
{
    public class AssemblyValidationResult
    {
        public bool IsValid { get; }
        public List<string> Errors { get; } = new List<string>();

        public int TotalCost { get; set; }
        public int TotalMass { get; set; }
        public int TotalHealth { get; set; }
        public float Speed { get; set; }
        public int MaxLoadCapacity { get; set; }

        public AssemblyValidationResult(bool isValid, List<string> errors = null)
        {
            IsValid = isValid;
            if (errors != null)
            {
                Errors.AddRange(errors);
            }
        }

        public static AssemblyValidationResult Fail(string error)
        {
            var res = new AssemblyValidationResult(false);
            res.Errors.Add(error);
            return res;
        }
    }

    public static class RobotAssemblyValidator
    {
        public static AssemblyValidationResult Validate(RobotBlueprint blueprint, IReadOnlyDictionary<string, ModuleDefinition> catalog)
        {
            var errors = new List<string>();

            if (blueprint == null)
            {
                return AssemblyValidationResult.Fail("Чертёж не может быть пустым.");
            }

            // 1. Проверка обязательных модулей
            if (string.IsNullOrEmpty(blueprint.ChassisId))
            {
                errors.Add("Не выбрано обязательное шасси.");
            }
            if (string.IsNullOrEmpty(blueprint.BodyId))
            {
                errors.Add("Не выбран обязательный корпус.");
            }
            if (string.IsNullOrEmpty(blueprint.WeaponId))
            {
                errors.Add("Не выбрано обязательное оружие.");
            }

            if (errors.Count > 0)
            {
                return new AssemblyValidationResult(false, errors);
            }

            // 2. Проверка наличия в каталоге
            if (!catalog.TryGetValue(blueprint.ChassisId, out var chassis) || chassis.SlotType != ModuleSlotType.Chassis)
            {
                errors.Add($"Модуль шасси '{blueprint.ChassisId}' не найден или некорректен.");
            }
            if (!catalog.TryGetValue(blueprint.BodyId, out var body) || body.SlotType != ModuleSlotType.Body)
            {
                errors.Add($"Модуль корпуса '{blueprint.BodyId}' не найден или некорректен.");
            }
            if (!catalog.TryGetValue(blueprint.WeaponId, out var weapon) || weapon.SlotType != ModuleSlotType.Weapon)
            {
                errors.Add($"Модуль оружия '{blueprint.WeaponId}' не найден или некорректен.");
            }

            ModuleDefinition equip = null;
            if (!string.IsNullOrEmpty(blueprint.EquipmentId))
            {
                if (!catalog.TryGetValue(blueprint.EquipmentId, out equip) || equip.SlotType != ModuleSlotType.Equipment)
                {
                    errors.Add($"Модуль оборудования '{blueprint.EquipmentId}' не найден или некорректен.");
                }
            }

            if (errors.Count > 0)
            {
                return new AssemblyValidationResult(false, errors);
            }

            // 3. Расчет характеристик
            int totalCost = chassis.Cost + body.Cost + weapon.Cost + (equip?.Cost ?? 0);
            int totalMass = chassis.Mass + body.Mass + weapon.Mass + (equip?.Mass ?? 0);
            int totalHealth = body.HealthBonus + (equip?.HealthBonus ?? 0);
            float speed = chassis.Speed;
            int maxLoad = chassis.MaxLoadCapacity;

            // 4. Проверка грузоподъемности
            if (totalMass > maxLoad)
            {
                errors.Add($"Превышена грузоподъёмность шасси: масса сборки {totalMass} кг превышает допустимый лимит {maxLoad} кг на {totalMass - maxLoad} кг.");
            }

            if (errors.Count > 0)
            {
                var failResult = new AssemblyValidationResult(false, errors)
                {
                    TotalCost = totalCost,
                    TotalMass = totalMass,
                    TotalHealth = totalHealth,
                    Speed = speed,
                    MaxLoadCapacity = maxLoad
                };
                return failResult;
            }

            return new AssemblyValidationResult(true)
            {
                TotalCost = totalCost,
                TotalMass = totalMass,
                TotalHealth = totalHealth,
                Speed = speed,
                MaxLoadCapacity = maxLoad
            };
        }
    }
}
