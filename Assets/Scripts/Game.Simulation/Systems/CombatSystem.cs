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
            if ((attacker.CurrentOrder?.Type != OrderType.Attack && attacker.CurrentOrder?.Type != OrderType.Hunt &&
                 attacker.CurrentOrder?.Type != OrderType.Defend && attacker.CurrentOrder?.Type != OrderType.Capture) ||
                attacker.WeaponCooldownRemaining > 0f)
            {
                return;
            }

            if (attacker.Blueprint == null || string.IsNullOrEmpty(attacker.Blueprint.WeaponId) ||
                !_catalog.TryGetValue(attacker.Blueprint.WeaponId, out var weapon) || weapon.SlotType != ModuleSlotType.Weapon)
                return;

            RobotState target = null;
            if (attacker.CurrentOrder.Type == OrderType.Attack)
            {
                if (!state.Robots.TryGetValue(attacker.CurrentOrder.TargetEntityId.Value, out target) ||
                    !target.IsAlive || target.Owner == attacker.Owner)
                {
                    attacker.CurrentOrder = RobotOrder.Stop();
                    return;
                }
            }
            else
            {
                // Захват сохраняет ID здания в приказе; боевую цель выбираем отдельно.
                float nearest = weapon.Range * weapon.Range;
                foreach (var candidate in state.Robots.Values)
                {
                    if (!candidate.IsAlive || candidate.Owner == attacker.Owner) continue;
                    float distance = SimVector2.SqrDistance(attacker.Position, candidate.Position);
                    if (distance > nearest) continue;
                    nearest = distance;
                    target = candidate;
                }
            }
            if (target == null || SimVector2.SqrDistance(attacker.Position, target.Position) > weapon.Range * weapon.Range) return;

            attacker.WeaponCooldownRemaining = weapon.CooldownSeconds;
            ModuleDefinition controller = null;
            if (!string.IsNullOrEmpty(attacker.Blueprint.ControlModuleId))
            {
                _catalog.TryGetValue(attacker.Blueprint.ControlModuleId, out controller);
            }
            float speed = Math.Max(0.1f, weapon.ProjectileSpeed);
            float travelSeconds = GetInterceptTime(attacker.Position, target.Position, target.Velocity, speed);
            state.Projectiles.Add(new ProjectileState
            {
                SourceRobotId = attacker.Id,
                TargetRobotId = target.Id,
                Owner = attacker.Owner,
                WeaponType = weapon.WeaponType,
                Damage = weapon.Damage,
                SplashRadius = weapon.SplashRadius,
                ImpactPosition = target.Position + target.Velocity * travelSeconds,
                RemainingTravelSeconds = travelSeconds,
                IsGuided = weapon.WeaponType == WeaponType.AreaMissile && (controller?.CanGuideMissiles ?? false)
            });
            OnWeaponFired?.Invoke(attacker, target, weapon.WeaponType);
        }

        private static float GetInterceptTime(SimVector2 origin, SimVector2 target, SimVector2 velocity, float speed)
        {
            SimVector2 offset = target - origin;
            float a = velocity.SqrMagnitude - speed * speed;
            float b = 2f * (offset.X * velocity.X + offset.Y * velocity.Y);
            float c = offset.SqrMagnitude;
            float fallback = (float)Math.Sqrt(c) / speed;
            if (Math.Abs(a) < 0.0001f) return b < -0.0001f ? -c / b : fallback;
            float discriminant = b * b - 4f * a * c;
            if (discriminant < 0f) return fallback;
            float root = (float)Math.Sqrt(discriminant);
            float t1 = (-b - root) / (2f * a);
            float t2 = (-b + root) / (2f * a);
            if (t1 > 0f && t2 > 0f) return Math.Min(t1, t2);
            return t1 > 0f ? t1 : t2 > 0f ? t2 : fallback;
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
                if (projectile.WeaponType == WeaponType.AreaMissile)
                {
                    ApplySplashDamage(attacker, projectile, state);
                }
                else
                {
                    ApplyDirectHit(attacker, projectile, state);
                }
                OnProjectileImpact?.Invoke(attacker, projectile.ImpactPosition, projectile.WeaponType);
            }
        }

        private void ApplyDirectHit(RobotState attacker, ProjectileState projectile, MatchState state)
        {
            // Попадание проверяется по габариту корпуса, а не по математической точке в центре.
            // Манёвр всё ещё позволяет уйти от снаряда.
            const float hitRadius = 1.6f;
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
            int appliedDamage = Math.Min(target.CurrentHealth, Math.Max(0, damage));
            target.CurrentHealth -= appliedDamage;
            if (target.IsAlive)
            {
                OnDamageDealt?.Invoke(target, appliedDamage);
                return;
            }

            target.CurrentOrder = RobotOrder.Stop();
            var owner = state.GetPlayer(target.Owner);
            if (owner != null)
            {
                owner.ActiveRobotsCount = Math.Max(0, owner.ActiveRobotsCount - 1);
            }

            OnRobotDestroyed?.Invoke(attacker, target);
            OnDamageDealt?.Invoke(target, appliedDamage);
        }
    }
}
