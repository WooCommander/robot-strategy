using System;
using System.Collections.Generic;
using Game.Content.Definitions;
using Game.Core.Models;
using Game.Core.Types;

namespace Game.Simulation.Systems
{
    public sealed class CombatSystem
    {
        private readonly IReadOnlyDictionary<string, ModuleDefinition> _catalog;

        public event Action<RobotState, RobotState> OnRobotDestroyed;
        public event Action<RobotState, RobotState, WeaponType> OnWeaponFired;

        public CombatSystem(IReadOnlyDictionary<string, ModuleDefinition> catalog)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        public void Update(MatchState state, float deltaSeconds)
        {
            if (state == null || state.IsPaused || state.Result != MatchResult.InProgress)
            {
                return;
            }

            foreach (var attacker in state.Robots.Values)
            {
                if (!attacker.IsAlive)
                {
                    continue;
                }

                attacker.WeaponCooldownRemaining = Math.Max(0f, attacker.WeaponCooldownRemaining - deltaSeconds);
                TryAttack(attacker, state);
            }
        }

        private void TryAttack(RobotState attacker, MatchState state)
        {
            if ((attacker.CurrentOrder?.Type != OrderType.Attack && attacker.CurrentOrder?.Type != OrderType.Hunt && attacker.CurrentOrder?.Type != OrderType.Defend) || attacker.WeaponCooldownRemaining > 0f)
            {
                return;
            }

            if (!state.Robots.TryGetValue(attacker.CurrentOrder.TargetEntityId.Value, out var target) ||
                !target.IsAlive || target.Owner == attacker.Owner)
            {
                if (attacker.CurrentOrder.Type == OrderType.Attack)
                {
                    attacker.CurrentOrder = RobotOrder.Stop();
                }
                return;
            }

            if (attacker.Blueprint == null ||
                !_catalog.TryGetValue(attacker.Blueprint.WeaponId, out var weapon) ||
                weapon.SlotType != ModuleSlotType.Weapon ||
                SimVector2.SqrDistance(attacker.Position, target.Position) > weapon.Range * weapon.Range)
            {
                return;
            }

            attacker.WeaponCooldownRemaining = weapon.CooldownSeconds;
            OnWeaponFired?.Invoke(attacker, target, weapon.WeaponType);
            if (weapon.WeaponType == WeaponType.AreaMissile)
            {
                ApplySplashDamage(attacker, target.Position, weapon, state);
            }
            else
            {
                ApplyDamage(attacker, target, weapon.Damage, state);
            }
        }

        private void ApplySplashDamage(RobotState attacker, SimVector2 impactPosition, ModuleDefinition weapon, MatchState state)
        {
            float splashRadiusSq = weapon.SplashRadius * weapon.SplashRadius;
            var targets = new List<RobotState>();
            foreach (var candidate in state.Robots.Values)
            {
                if (candidate.IsAlive && candidate.Owner != attacker.Owner &&
                    SimVector2.SqrDistance(candidate.Position, impactPosition) <= splashRadiusSq)
                {
                    targets.Add(candidate);
                }
            }

            foreach (var target in targets)
            {
                ApplyDamage(attacker, target, weapon.Damage, state);
            }
        }

        private void ApplyDamage(RobotState attacker, RobotState target, int damage, MatchState state)
        {
            target.CurrentHealth = Math.Max(0, target.CurrentHealth - Math.Max(0, damage));
            if (target.IsAlive)
            {
                return;
            }

            target.CurrentOrder = RobotOrder.Stop();
            var owner = state.GetPlayer(target.Owner);
            if (owner != null)
            {
                owner.ActiveRobotsCount = Math.Max(0, owner.ActiveRobotsCount - 1);
            }

            OnRobotDestroyed?.Invoke(attacker, target);
        }
    }
}
