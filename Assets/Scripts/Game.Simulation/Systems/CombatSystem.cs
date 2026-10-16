using System;
using System.Collections.Generic;
using Game.Content.Definitions;
using Game.Core.Models;
using Game.Core.Types;

namespace Game.Simulation.Systems
{
    public sealed class CombatSystem
    {
        /// <summary>Попадание пушки проверяется по габариту корпуса, а не по точке в центре.</summary>
        private const float CannonHitRadius = 1.2f;

        private readonly IReadOnlyDictionary<string, ModuleDefinition> _catalog;
        private readonly List<RobotState> _blastTargets = new List<RobotState>();

        public event Action<RobotState, RobotState> OnRobotDestroyed;
        public event Action<RobotState, RobotState, WeaponType> OnWeaponFired;
        public event Action<RobotState, SimVector2, WeaponType> OnProjectileImpact;
        public event Action<RobotState, int> OnDamageDealt;
        /// <summary>Носитель, точка и радиус ядерного взрыва.</summary>
        public event Action<RobotState, SimVector2, float> OnNuclearDetonated;

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

            foreach (var robot in state.Robots.Values)
            {
                if (!robot.IsAlive || robot.Blueprint == null) continue;
                if (robot.CurrentOrder?.Type == OrderType.Detonate)
                {
                    Detonate(robot, state);
                    continue;
                }

                List<string> weaponIds = robot.Blueprint.WeaponIds;
                if (weaponIds == null) continue;
                while (robot.WeaponCooldowns.Count < weaponIds.Count) robot.WeaponCooldowns.Add(0f);
                for (int index = 0; index < weaponIds.Count && robot.IsAlive; index++)
                {
                    robot.WeaponCooldowns[index] = Math.Max(0f, robot.WeaponCooldowns[index] - deltaSeconds);
                    if (_catalog.TryGetValue(weaponIds[index], out ModuleDefinition weapon) && weapon.SlotType == ModuleSlotType.Weapon)
                    {
                        TryFire(robot, weapon, index, state);
                    }
                }
            }
            UpdateProjectiles(state, deltaSeconds);
        }

        private void TryFire(RobotState attacker, ModuleDefinition weapon, int weaponIndex, MatchState state)
        {
            OrderType order = attacker.CurrentOrder?.Type ?? OrderType.None;
            if ((order != OrderType.Attack && order != OrderType.Hunt && order != OrderType.Defend && order != OrderType.Capture && order != OrderType.Program) ||
                (order == OrderType.Program && (attacker.ActiveProgramAction == ProgramAction.None || attacker.ActiveProgramAction == ProgramAction.Explore || attacker.ActiveProgramAction == ProgramAction.Retreat)) ||
                attacker.WeaponCooldowns[weaponIndex] > 0f)
            {
                return;
            }

            RobotState target = null;
            bool programmedTarget = order == OrderType.Program &&
                (attacker.ActiveProgramAction == ProgramAction.Attack || attacker.ActiveProgramAction == ProgramAction.Defend);
            if (order == OrderType.Attack || programmedTarget)
            {
                if (!state.Robots.TryGetValue(attacker.CurrentOrder.TargetEntityId.Value, out target) ||
                    !target.IsAlive || target.Owner == attacker.Owner)
                {
                    if (order == OrderType.Attack) attacker.CurrentOrder = RobotOrder.Stop();
                    return;
                }
                if (state.FogOfWar != null && !state.FogOfWar.IsVisible(attacker.Owner, target.Position)) return;
            }
            else
            {
                // Захват сохраняет ID здания в приказе; боевую цель каждое орудие выбирает в своей дальности.
                float nearest = weapon.Range * weapon.Range;
                float minRangeSq = weapon.MinRange * weapon.MinRange;
                foreach (var candidate in state.Robots.Values)
                {
                    if (!candidate.IsAlive || candidate.Owner == attacker.Owner ||
                        (state.FogOfWar != null && !state.FogOfWar.IsVisible(attacker.Owner, candidate.Position))) continue;
                    float distance = SimVector2.SqrDistance(attacker.Position, candidate.Position);
                    if (distance > nearest || distance < minRangeSq) continue;
                    nearest = distance;
                    target = candidate;
                }
            }
            if (target == null) return;
            float targetDistanceSq = SimVector2.SqrDistance(attacker.Position, target.Position);
            // Ракета не успевает взвестись вплотную: в «мёртвой зоне» орудие молчит.
            if (targetDistanceSq > weapon.Range * weapon.Range || targetDistanceSq < weapon.MinRange * weapon.MinRange) return;

            attacker.WeaponCooldowns[weaponIndex] = weapon.CooldownSeconds;
            if (weapon.ProjectileSpeed <= 0f)
            {
                // Фазер — мгновенный луч: урон применяется до визуальных событий.
                OnWeaponFired?.Invoke(attacker, target, weapon.WeaponType);
                SimVector2 impact = target.Position;
                ApplyDamage(attacker, target, weapon.Damage, state);
                OnProjectileImpact?.Invoke(attacker, impact, weapon.WeaponType);
                return;
            }

            bool canGuide = attacker.Blueprint.HasElectronics &&
                            _catalog.TryGetValue(attacker.Blueprint.ElectronicsId, out ModuleDefinition electronics) &&
                            electronics.CanGuideMissiles;
            float travelSeconds = GetInterceptTime(attacker.Position, target.Position, target.Velocity, weapon.ProjectileSpeed);
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
                IsGuided = weapon.WeaponType == WeaponType.AreaMissile && canGuide
            });
            OnWeaponFired?.Invoke(attacker, target, weapon.WeaponType);
        }

        private void Detonate(RobotState carrier, MatchState state)
        {
            if (!carrier.Blueprint.HasNuclear || !_catalog.TryGetValue(carrier.Blueprint.NuclearId, out ModuleDefinition nuclear))
            {
                carrier.CurrentOrder = RobotOrder.Stop();
                return;
            }

            SimVector2 center = carrier.Position;
            float radiusSq = nuclear.BlastRadius * nuclear.BlastRadius;
            _blastTargets.Clear();
            foreach (var candidate in state.Robots.Values)
            {
                if (candidate != carrier && candidate.IsAlive && SimVector2.SqrDistance(candidate.Position, center) <= radiusSq) _blastTargets.Add(candidate);
            }
            // Взрыв не различает своих и чужих; носитель погибает последним, чтобы стать автором всех уничтожений.
            _blastTargets.Add(carrier);
            OnNuclearDetonated?.Invoke(carrier, center, nuclear.BlastRadius);
            foreach (RobotState target in _blastTargets) ApplyDamage(carrier, target, nuclear.BlastDamage, state);
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
            // Манёвр всё ещё позволяет уйти от снаряда.
            if (state.Robots.TryGetValue(projectile.TargetRobotId.Value, out RobotState target) && target.IsAlive &&
                target.Owner != projectile.Owner && SimVector2.SqrDistance(target.Position, projectile.ImpactPosition) <= CannonHitRadius * CannonHitRadius)
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
            if (!target.IsAlive) return;
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
