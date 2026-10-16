using System.Collections.Generic;
using NUnit.Framework;
using Game.Content.Definitions;
using Game.Core.Models;
using Game.Core.Types;
using Game.Simulation.Systems;

namespace Game.Core.Tests
{
    [TestFixture]
    public class CombatSystemTests
    {
        private Dictionary<string, ModuleDefinition> _catalog;

        [SetUp]
        public void SetUp()
        {
            _catalog = DefaultContentCatalog.CreateDefaultModules();
        }

        // Ожидания считаются из каталога: ребаланс цифр не должен ломать проверки механики.
        private ModuleDefinition CannonDef => _catalog[DefaultContentCatalog.WeaponCannon];
        private ModuleDefinition MissileDef => _catalog[DefaultContentCatalog.WeaponMissiles];
        private ModuleDefinition PhaserDef => _catalog[DefaultContentCatalog.WeaponPhasers];
        private float CannonTravel(float distance) => distance / CannonDef.ProjectileSpeed;
        private float MissileTravel(float distance) => distance / MissileDef.ProjectileSpeed;

        [Test]
        public void CannonAttack_DamagesOnlyTarget_WhenTargetIsInRange()
        {
            var state = CreateState(CreateRobot(1, PlayerId.Player1, 0f, DefaultContentCatalog.WeaponCannon),
                CreateRobot(2, PlayerId.Player2, 5f, DefaultContentCatalog.WeaponCannon));
            state.Robots[1].CurrentOrder = RobotOrder.Attack(new EntityId(2));

            var combat = new CombatSystem(_catalog);
            combat.Update(state, 0.1f);

            Assert.AreEqual(100, state.Robots[2].CurrentHealth, "Кинетический снаряд не должен попадать мгновенно.");
            Assert.AreEqual(1, state.Projectiles.Count);
            combat.Update(state, CannonTravel(5f));

            Assert.AreEqual(100 - CannonDef.Damage, state.Robots[2].CurrentHealth);
            Assert.AreEqual(CannonDef.CooldownSeconds - CannonTravel(5f), state.Robots[1].WeaponCooldowns[0], 0.001f);
        }

        [Test]
        public void MissileAttack_DamagesEnemiesInsideSplash_AndNotAllies()
        {
            var state = CreateState(
                CreateRobot(1, PlayerId.Player1, 0f, DefaultContentCatalog.WeaponMissiles),
                CreateRobot(2, PlayerId.Player2, 8f, DefaultContentCatalog.WeaponCannon),
                CreateRobot(3, PlayerId.Player2, 9.5f, DefaultContentCatalog.WeaponCannon),
                CreateRobot(4, PlayerId.Player1, 8.5f, DefaultContentCatalog.WeaponCannon));
            state.Robots[1].CurrentOrder = RobotOrder.Attack(new EntityId(2));

            new CombatSystem(_catalog).Update(state, MissileTravel(8f) + 0.05f);

            Assert.AreEqual(100 - MissileDef.Damage, state.Robots[2].CurrentHealth);
            Assert.AreEqual(100 - MissileDef.Damage, state.Robots[3].CurrentHealth);
            Assert.AreEqual(100, state.Robots[4].CurrentHealth);
        }

        [Test]
        public void Missiles_DoNotFireInsideMinimumRange()
        {
            var attacker = CreateRobot(1, PlayerId.Player1, 0f, DefaultContentCatalog.WeaponMissiles);
            var target = CreateRobot(2, PlayerId.Player2, MissileDef.MinRange - 1f, DefaultContentCatalog.WeaponCannon);
            var state = CreateState(attacker, target);
            attacker.CurrentOrder = RobotOrder.Attack(target.Id);

            new CombatSystem(_catalog).Update(state, 0.05f);

            Assert.AreEqual(0, state.Projectiles.Count, "Вплотную ракеты молчат.");
            Assert.AreEqual(0f, attacker.WeaponCooldowns[0]);
        }

        [Test]
        public void CannonProjectile_MissesWhenTargetManeuversAwayBeforeImpact()
        {
            var state = CreateState(CreateRobot(1, PlayerId.Player1, 0f, DefaultContentCatalog.WeaponCannon),
                CreateRobot(2, PlayerId.Player2, 5f, DefaultContentCatalog.WeaponCannon));
            state.Robots[1].CurrentOrder = RobotOrder.Attack(new EntityId(2));
            var combat = new CombatSystem(_catalog);

            combat.Update(state, 0.05f);
            state.Robots[2].Position = new SimVector2(5f, 3f);
            combat.Update(state, CannonTravel(5f));

            Assert.AreEqual(100, state.Robots[2].CurrentHealth);
            Assert.AreEqual(0, state.Projectiles.Count);
        }

        [Test]
        public void CannonProjectile_HitsTargetThatMovedInsideItsHull()
        {
            var state = CreateState(CreateRobot(1, PlayerId.Player1, 0f, DefaultContentCatalog.WeaponCannon),
                CreateRobot(2, PlayerId.Player2, 5f, DefaultContentCatalog.WeaponCannon));
            state.Robots[1].CurrentOrder = RobotOrder.Attack(new EntityId(2));
            var combat = new CombatSystem(_catalog);

            combat.Update(state, 0.05f);
            state.Robots[2].Position = new SimVector2(5f, 0.9f);
            combat.Update(state, CannonTravel(5f));

            Assert.AreEqual(100 - CannonDef.Damage, state.Robots[2].CurrentHealth);
        }

        [TestCase(null)]
        [TestCase(DefaultContentCatalog.Electronics)]
        public void Cannon_LeadsMovingTargetForProjectileTravelTime(string electronicsId)
        {
            var attacker = CreateRobot(1, PlayerId.Player1, 0f, DefaultContentCatalog.WeaponCannon, electronicsId);
            var target = CreateRobot(2, PlayerId.Player2, 5f, DefaultContentCatalog.WeaponCannon);
            target.Velocity = new SimVector2(0f, 3f);
            var state = CreateState(attacker, target);
            attacker.CurrentOrder = RobotOrder.Attack(target.Id);
            var combat = new CombatSystem(_catalog);

            combat.Update(state, 0.01f);
            ProjectileState shot = state.Projectiles[0];
            Assert.Greater(shot.ImpactPosition.Y, 0.5f, "Снаряд летит в упреждённую точку, а не в текущую позицию.");
            float remaining = shot.RemainingTravelSeconds;
            target.Position += target.Velocity * (remaining + 0.01f);
            combat.Update(state, remaining);

            Assert.AreEqual(100 - CannonDef.Damage, target.CurrentHealth);
        }

        [Test]
        public void UnguidedMissile_ExplodesAtOriginalAimPoint_WhenTargetMovesAway()
        {
            var state = CreateState(CreateRobot(1, PlayerId.Player1, 0f, DefaultContentCatalog.WeaponMissiles),
                CreateRobot(2, PlayerId.Player2, 8f, DefaultContentCatalog.WeaponCannon));
            state.Robots[1].CurrentOrder = RobotOrder.Attack(new EntityId(2));
            var combat = new CombatSystem(_catalog);

            combat.Update(state, 0.05f);
            state.Robots[2].Position = new SimVector2(8f, MissileDef.SplashRadius + 1.5f);
            combat.Update(state, MissileTravel(8f));

            Assert.AreEqual(100, state.Robots[2].CurrentHealth);
            Assert.AreEqual(0, state.Projectiles.Count);
        }

        [Test]
        public void Electronics_GuidesMissileToMovingEnemy()
        {
            var attacker = CreateRobot(1, PlayerId.Player1, 0f, DefaultContentCatalog.WeaponMissiles, DefaultContentCatalog.Electronics);
            var target = CreateRobot(2, PlayerId.Player2, 8f, DefaultContentCatalog.WeaponCannon);
            var state = CreateState(attacker, target);
            attacker.CurrentOrder = RobotOrder.Attack(target.Id);
            var combat = new CombatSystem(_catalog);

            combat.Update(state, 0.05f);
            target.Position = new SimVector2(8f, MissileDef.SplashRadius + 1.5f);
            combat.Update(state, MissileTravel(8f));

            Assert.AreEqual(100 - MissileDef.Damage, target.CurrentHealth);
        }

        [Test]
        public void LethalAttack_SetsHealthToZero_AndDecrementsArmy()
        {
            var attacker = CreateRobot(1, PlayerId.Player1, 0f, DefaultContentCatalog.WeaponCannon);
            var target = CreateRobot(2, PlayerId.Player2, 2f, DefaultContentCatalog.WeaponCannon);
            target.CurrentHealth = CannonDef.Damage;
            var state = CreateState(attacker, target);
            state.Player2.ActiveRobotsCount = 1;
            attacker.CurrentOrder = RobotOrder.Attack(target.Id);

            new CombatSystem(_catalog).Update(state, CannonTravel(2f) + 0.05f);

            Assert.AreEqual(0, target.CurrentHealth);
            Assert.AreEqual(0, state.Player2.ActiveRobotsCount);
            Assert.AreEqual(OrderType.Stop, target.CurrentOrder.Type);
        }

        [Test]
        public void CombatRangePlanner_RetreatsFromPointBlankAndManeuversInsideEffectiveRange()
        {
            var attacker = CreateRobot(1, PlayerId.Player1, 0f, DefaultContentCatalog.WeaponCannon);
            var target = CreateRobot(2, PlayerId.Player2, 1f, DefaultContentCatalog.WeaponCannon);

            Assert.IsTrue(CombatRangePlanner.TryGetManeuverDestination(attacker, target, _catalog, out var retreat));
            Assert.Less(retreat.X, attacker.Position.X, "При стрельбе в упор робот должен отступать.");

            float effectiveRange = CannonDef.Range * 0.8f;
            attacker.Position = new SimVector2(1f + effectiveRange, 0f);
            Assert.IsTrue(CombatRangePlanner.TryGetManeuverDestination(attacker, target, _catalog, out var maneuver));
            Assert.AreEqual(effectiveRange, SimVector2.Distance(maneuver, target.Position), 0.05f, "Манёвр должен сохранять эффективную дальность пушки.");
            Assert.AreNotEqual(0f, maneuver.Y, "В рабочей зоне робот должен двигаться по дуге, а не останавливаться.");
        }

        [Test]
        public void CombatRangePlanner_MissileCarrierHoldsGround_AndLeavesOnlyTheDeadZone()
        {
            var attacker = CreateRobot(1, PlayerId.Player1, 0f, DefaultContentCatalog.WeaponMissiles);
            var target = CreateRobot(2, PlayerId.Player2, MissileDef.MinRange + 2f, DefaultContentCatalog.WeaponCannon);

            Assert.IsTrue(CombatRangePlanner.TryGetManeuverDestination(attacker, target, _catalog, out var hold));
            Assert.AreEqual(MissileDef.MinRange + 2f, SimVector2.Distance(hold, target.Position), 0.05f,
                "Ракетчик не убегает от сближения: иначе равный по скорости противник с пушкой никогда не выстрелит.");

            target.Position = new SimVector2(MissileDef.MinRange - 1f, 0f);
            Assert.IsTrue(CombatRangePlanner.TryGetManeuverDestination(attacker, target, _catalog, out var escape));
            Assert.Greater(SimVector2.Distance(escape, target.Position), MissileDef.MinRange, "Из «мёртвой зоны» ракетчик отходит.");
        }

        [Test]
        public void CaptureRobot_FiresWithoutLosingBuildingOrder_AndKillsScout()
        {
            var attacker = CreateRobot(1, PlayerId.Player1, 0f, DefaultContentCatalog.WeaponMissiles);
            var target = CreateRobot(2, PlayerId.Player2, 7f, DefaultContentCatalog.WeaponCannon);
            target.CurrentHealth = target.MaxHealth = 120;
            var state = CreateState(attacker, target);
            state.Player2.ActiveRobotsCount = 1;
            attacker.CurrentOrder = RobotOrder.Capture(new EntityId(101));
            int hits = 0;
            var combat = new CombatSystem(_catalog);
            combat.OnDamageDealt += (robot, damage) => { if (robot == target) hits++; };
            for (int tick = 0; tick < 1000 && target.IsAlive; tick++) combat.Update(state, 0.02f);
            Assert.AreEqual(0, target.CurrentHealth);
            Assert.AreEqual((int)System.Math.Ceiling(120f / MissileDef.Damage), hits);
            Assert.AreEqual(0, state.Player2.ActiveRobotsCount);
            Assert.AreEqual(OrderType.Capture, attacker.CurrentOrder.Type);
            Assert.AreEqual(new EntityId(101), attacker.CurrentOrder.TargetEntityId);
        }

        [Test]
        public void Electronics_CloseShot_DoesNotOvershootMovingTarget()
        {
            var attacker = CreateRobot(1, PlayerId.Player1, 0f, DefaultContentCatalog.WeaponCannon, DefaultContentCatalog.Electronics);
            var target = CreateRobot(2, PlayerId.Player2, 2f, DefaultContentCatalog.WeaponCannon);
            target.Velocity = new SimVector2(0f, 6f);
            var state = CreateState(attacker, target);
            attacker.CurrentOrder = RobotOrder.Attack(target.Id);
            var combat = new CombatSystem(_catalog);
            combat.Update(state, 0.01f);
            var shot = state.Projectiles[0];
            float flight = shot.RemainingTravelSeconds + 0.01f;
            Assert.AreEqual(SimVector2.Distance(attacker.Position, shot.ImpactPosition) / CannonDef.ProjectileSpeed, flight, 0.001f);
            target.Position += target.Velocity * flight;
            combat.Update(state, flight);
            Assert.AreEqual(100 - CannonDef.Damage, target.CurrentHealth);
        }

        [Test]
        public void ImpactEffectException_CannotCancelDamage()
        {
            var attacker = CreateRobot(1, PlayerId.Player1, 0f, DefaultContentCatalog.WeaponCannon);
            var target = CreateRobot(2, PlayerId.Player2, 2f, DefaultContentCatalog.WeaponCannon);
            var state = CreateState(attacker, target);
            attacker.CurrentOrder = RobotOrder.Attack(target.Id);
            var combat = new CombatSystem(_catalog);
            combat.OnProjectileImpact += (robot, point, weapon) => throw new System.InvalidOperationException("VFX failure");
            Assert.Throws<System.InvalidOperationException>(() => combat.Update(state, CannonTravel(2f) + 0.05f));
            Assert.AreEqual(100 - CannonDef.Damage, target.CurrentHealth);
        }

        [Test]
        public void Phaser_HitsInstantlyWithoutProjectile()
        {
            var attacker = CreateRobot(1, PlayerId.Player1, 0f, DefaultContentCatalog.WeaponPhasers);
            var target = CreateRobot(2, PlayerId.Player2, 6f, DefaultContentCatalog.WeaponCannon);
            var state = CreateState(attacker, target);
            attacker.CurrentOrder = RobotOrder.Attack(target.Id);

            new CombatSystem(_catalog).Update(state, 0.05f);

            Assert.AreEqual(100 - PhaserDef.Damage, target.CurrentHealth);
            Assert.AreEqual(0, state.Projectiles.Count);
        }

        [Test]
        public void ProgrammedAttack_FiresAtSpecifiedRobotNotNearestEnemy()
        {
            var attacker = CreateRobot(1, PlayerId.Player1, 0f, DefaultContentCatalog.WeaponPhasers);
            var closer = CreateRobot(2, PlayerId.Player2, 3f, DefaultContentCatalog.WeaponCannon);
            var selected = CreateRobot(3, PlayerId.Player2, 6f, DefaultContentCatalog.WeaponCannon);
            var state = CreateState(attacker, closer, selected);
            attacker.CurrentOrder = RobotOrder.Program();
            attacker.ActiveProgramAction = ProgramAction.Attack;
            attacker.CurrentOrder.TargetEntityId = selected.Id;

            new CombatSystem(_catalog).Update(state, 0.05f);

            Assert.AreEqual(100, closer.CurrentHealth);
            Assert.AreEqual(100 - PhaserDef.Damage, selected.CurrentHealth);
        }

        [Test]
        public void ProgramWithNoAvailableAction_DoesNotFire()
        {
            var attacker = CreateRobot(1, PlayerId.Player1, 0f, DefaultContentCatalog.WeaponPhasers);
            var target = CreateRobot(2, PlayerId.Player2, 3f, DefaultContentCatalog.WeaponCannon);
            var state = CreateState(attacker, target);
            attacker.CurrentOrder = RobotOrder.Program();
            attacker.ActiveProgramAction = ProgramAction.None;

            new CombatSystem(_catalog).Update(state, 0.05f);

            Assert.AreEqual(100, target.CurrentHealth);
        }

        [Test]
        public void SeveralWeapons_FireIndependentlyWithOwnCooldowns()
        {
            var attacker = CreateRobot(1, PlayerId.Player1, 0f, new[] { DefaultContentCatalog.WeaponPhasers, DefaultContentCatalog.WeaponCannon });
            var target = CreateRobot(2, PlayerId.Player2, 5f, DefaultContentCatalog.WeaponCannon);
            target.CurrentHealth = target.MaxHealth = 200;
            var state = CreateState(attacker, target);
            attacker.CurrentOrder = RobotOrder.Attack(target.Id);
            var combat = new CombatSystem(_catalog);

            combat.Update(state, 0.05f);
            Assert.AreEqual(200 - PhaserDef.Damage, target.CurrentHealth, "Фазер попадает сразу.");
            Assert.AreEqual(1, state.Projectiles.Count, "Пушка выстрелила в том же тике.");
            Assert.AreEqual(PhaserDef.CooldownSeconds, attacker.WeaponCooldowns[0], 0.001f);
            Assert.AreEqual(CannonDef.CooldownSeconds, attacker.WeaponCooldowns[1], 0.001f);

            combat.Update(state, CannonTravel(5f));
            Assert.AreEqual(200 - PhaserDef.Damage - CannonDef.Damage, target.CurrentHealth, "Снаряд пушки долетел.");
        }

        [Test]
        public void NuclearDetonation_DestroysEveryRobotInRadius_IncludingCarrier()
        {
            var carrier = CreateRobot(1, PlayerId.Player1, 0f, new string[0], nuclearId: DefaultContentCatalog.Nuclear);
            var enemyNear = CreateRobot(2, PlayerId.Player2, 3f, DefaultContentCatalog.WeaponCannon);
            var allyNear = CreateRobot(3, PlayerId.Player1, -2f, DefaultContentCatalog.WeaponCannon);
            var enemyFar = CreateRobot(4, PlayerId.Player2, 10f, DefaultContentCatalog.WeaponCannon);
            var state = CreateState(carrier, enemyNear, allyNear, enemyFar);
            state.Player1.ActiveRobotsCount = 2;
            state.Player2.ActiveRobotsCount = 2;
            carrier.CurrentOrder = RobotOrder.Detonate();
            int blasts = 0;
            var combat = new CombatSystem(_catalog);
            combat.OnNuclearDetonated += (robot, center, radius) => blasts++;

            combat.Update(state, 0.05f);

            Assert.AreEqual(1, blasts);
            Assert.IsFalse(carrier.IsAlive);
            Assert.IsFalse(enemyNear.IsAlive);
            Assert.IsFalse(allyNear.IsAlive, "Взрыв не щадит своих.");
            Assert.IsTrue(enemyFar.IsAlive);
            Assert.AreEqual(0, state.Player1.ActiveRobotsCount);
            Assert.AreEqual(1, state.Player2.ActiveRobotsCount);
        }

        private static MatchState CreateState(params RobotState[] robots)
        {
            var state = new MatchState();
            foreach (var robot in robots)
            {
                state.Robots[robot.Id.Value] = robot;
            }
            return state;
        }

        private static RobotState CreateRobot(int id, PlayerId owner, float x, string weaponId, string electronicsId = null) =>
            CreateRobot(id, owner, x, new[] { weaponId }, electronicsId);

        private static RobotState CreateRobot(int id, PlayerId owner, float x, string[] weaponIds, string electronicsId = null, string nuclearId = null)
        {
            return new RobotState
            {
                Id = new EntityId(id),
                Owner = owner,
                Position = new SimVector2(x, 0f),
                CurrentHealth = 100,
                MaxHealth = 100,
                Blueprint = new RobotBlueprint("bp_" + id, "Test", DefaultContentCatalog.ChassisBipod, weaponIds, electronicsId, nuclearId)
            };
        }
    }
}
