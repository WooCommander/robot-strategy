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
        public event Action<RobotState, SimVector2, WeaponType> OnProjectileImpact;
        public event Action<RobotState, int> OnDamageDealt;

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
            UpdateProjectiles(state, deltaSeconds);
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
            ModuleDefinition controller = null;
            if (!string.IsNullOrEmpty(attacker.Blueprint.ControlModuleId))
            {
                _catalog.TryGetValue(attacker.Blueprint.ControlModuleId, out controller);
            }
            float speed = Math.Max(0.1f, weapon.ProjectileSpeed);
            float travelSeconds = SimVector2.Distance(attacker.Position, target.Position) / speed;
            // Даже базовый контроллер знает время полёта. Дополнительное упреждение
            // от модуля применяется только если оно больше физически необходимого.
            // При резкой смене траектории цель всё ещё может уйти от неуправляемого снаряда.
            float leadSeconds = Math.Max(travelSeconds, controller?.LeadPredictionSeconds ?? 0f);
            state.Projectiles.Add(new ProjectileState
            {
                SourceRobotId = attacker.Id,
                TargetRobotId = target.Id,
                Owner = attacker.Owner,
                WeaponType = weapon.WeaponType,
                Damage = weapon.Damage,
                SplashRadius = weapon.SplashRadius,
                ImpactPosition = target.Position + target.Velocity * leadSeconds,
                RemainingTravelSeconds = travelSeconds,
                IsGuided = weapon.WeaponType == WeaponType.AreaMissile && (controller?.CanGuideMissiles ?? false)
            });
        }

        private void UpdateProjectiles(MatchState state, float deltaSeconds)
        {
            for (int index = state.Projectiles.Count - 1; index >= 0; index--)
            {
                ProjectileState projectile = state.Projectiles[index];
                if (projectile.IsGuided && state.Robots.TryGetValue(projectile.TargetRobotId.Value, out var guidedTarget) && guidedTarget.IsAlive)
                {
                    projectile.ImpactPosition = guidedTarget.Position;
                }
                projectile.RemainingTravelSeconds -= deltaSeconds;
                if (projectile.RemainingTravelSeconds > 0f) continue;

                state.Projectiles.RemoveAt(index);
                state.Robots.TryGetValue(projectile.SourceRobotId.Value, out RobotState attacker);
                OnProjectileImpact?.Invoke(attacker, projectile.ImpactPosition, projectile.WeaponType);
                if (projectile.WeaponType == WeaponType.AreaMissile)
                {
                    ApplySplashDamage(attacker, projectile, state);
                }
                else
                {
                    ApplyDirectHit(attacker, projectile, state);
                }
            }
        }

        private void ApplyDirectHit(RobotState attacker, ProjectileState projectile, MatchState state)
        {
            // Попадание проверяется по габариту корпуса, а не по математической точке в центре.
            // Манёвр всё ещё позволяет уйти от снаряда.
            const float hitRadius = 1.25f;
            if (state.Robots.TryGetValue(projectile.TargetRobotId.Value, out RobotState target) && target.IsAlive &&
                target.Owner != projectile.Owner && SimVector2.SqrDistance(target.Position, projectile.ImpactPosition) <= hitRadius * hitRadius)
            {
                ApplyDamage(attacker, target, projectile.Damage, state);
            }
        }

        private void ApplySplashDamage(RobotState attacker, ProjectileState projectile, MatchState state)
        {
            float splashRadiusSq = projectile.SplashRadius * projectile.SplashRadius;
            var targets = new List<RobotState>();
            foreach (var candidate in state.Robots.Values)
            {
                if (candidate.IsAlive && candidate.Owner != projectile.Owner &&
                    SimVector2.SqrDistance(candidate.Position, projectile.ImpactPosition) <= splashRadiusSq)
                {
                    targets.Add(candidate);
                }
            }

            foreach (var target in targets)
            {
                ApplyDamage(attacker, target, projectile.Damage, state);
            }
        }

        private void ApplyDamage(RobotState attacker, RobotState target, int damage, MatchState state)
        {
            target.CurrentHealth = Math.Max(0, target.CurrentHealth - Math.Max(0, damage));
            OnDamageDealt?.Invoke(target, damage);
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
