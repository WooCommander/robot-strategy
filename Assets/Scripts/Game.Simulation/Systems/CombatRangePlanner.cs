using System;
using System.Collections.Generic;
using Game.Content.Definitions;
using Game.Core.Models;
using Game.Core.Types;

namespace Game.Simulation.Systems
{
    /// <summary>Выбирает точку манёвра внутри эффективной дальности, не допуская сближения в упор.</summary>
    public static class CombatRangePlanner
    {
        private const float EffectiveRangeFactor = 0.8f;
        private const float RangeTolerance = 0.15f;

        public static bool TryGetManeuverDestination(RobotState attacker, RobotState target, IReadOnlyDictionary<string, ModuleDefinition> catalog, out SimVector2 destination)
        {
            destination = SimVector2.Zero;
            if (attacker?.Blueprint == null || target == null || catalog == null) return false;

            // Держим дальность самого короткого орудия, чтобы в бою участвовали все установленные.
            float shortestRange = float.MaxValue;
            foreach (string weaponId in attacker.Blueprint.WeaponIds)
            {
                if (catalog.TryGetValue(weaponId, out ModuleDefinition weapon) && weapon.Range > 0f) shortestRange = Math.Min(shortestRange, weapon.Range);
            }
            if (shortestRange == float.MaxValue) return false;

            float effectiveRange = shortestRange * EffectiveRangeFactor;
            SimVector2 radial = attacker.Position - target.Position;
            float distance = radial.Magnitude;
            if (distance < 0.01f)
            {
                radial = attacker.Id.Value % 2 == 0 ? new SimVector2(1f, 0f) : new SimVector2(-1f, 0f);
                distance = 1f;
            }
            radial /= distance;

            if (distance < effectiveRange * (1f - RangeTolerance) || distance > effectiveRange * (1f + RangeTolerance))
            {
                destination = target.Position + radial * effectiveRange;
                return true;
            }

            // В рабочей зоне робот идёт по небольшой дуге, сохраняя дальность и возможность вести огонь.
            // Большой шаг по окружности менял точку прицеливания быстрее, чем летели снаряды.
            float direction = attacker.Id.Value % 2 == 0 ? 1f : -1f;
            var tangent = new SimVector2(-radial.Y * direction, radial.X * direction);
            SimVector2 maneuverDirection = (radial * 0.98f + tangent * 0.2f);
            destination = target.Position + maneuverDirection * effectiveRange;
            return true;
        }
    }
}
