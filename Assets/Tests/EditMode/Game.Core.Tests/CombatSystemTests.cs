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
            combat.Update(state, 0.2f);

            Assert.AreEqual(65, state.Robots[2].CurrentHealth);
            Assert.AreEqual(1.3f, state.Robots[1].WeaponCooldowns[0], 0.001f);
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

            new CombatSystem(_catalog).Update(state, 1.0f);

            Assert.AreEqual(50, state.Robots[2].CurrentHealth);
            Assert.AreEqual(50, state.Robots[3].CurrentHealth);
            Assert.AreEqual(100, state.Robots[4].CurrentHealth);
        }

        [Test]
        public void CannonProjectile_MissesWhenTargetManeuversAwayBeforeImpact()
        {
            var state = CreateState(CreateRobot(1, PlayerId.Player1, 0f, DefaultContentCatalog.WeaponCannon),
                CreateRobot(2, PlayerId.Player2, 5f, DefaultContentCatalog.WeaponCannon));
            state.Robots[1].CurrentOrder = RobotOrder.Attack(new EntityId(2));
            var combat = new CombatSystem(_catalog);

            combat.Update(state, 0.1f);
            state.Robots[2].Position = new SimVector2(5f, 3f);
            combat.Update(state, 0.2f);

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

            combat.Update(state, 0.1f);
            state.Robots[2].Position = new SimVector2(5f, 1.1f);
            combat.Update(state, 0.2f);

            Assert.AreEqual(65, state.Robots[2].CurrentHealth);
        }

        [Test]
        public void Cannon_LeadsTargetForProjectileTravelTime()
        {
            var attacker = CreateRobot(1, PlayerId.Player1, 0f, DefaultContentCatalog.WeaponCannon);
            var target = CreateRobot(2, PlayerId.Player2, 5f, DefaultContentCatalog.WeaponCannon);
            target.Velocity = new SimVector2(0f, 3f);
            var state = CreateState(attacker, target);
            attacker.CurrentOrder = RobotOrder.Attack(target.Id);
            var combat = new CombatSystem(_catalog);

            combat.Update(state, 0.1f);
            target.Position = new SimVector2(5f, 5f / 18f * 3f);
            combat.Update(state, 0.3f);

            Assert.AreEqual(65, target.CurrentHealth);
        }

        [Test]
        public void UnguidedMissile_ExplodesAtOriginalAimPoint_WhenTargetMovesAway()
        {
            var state = CreateState(CreateRobot(1, PlayerId.Player1, 0f, DefaultContentCatalog.WeaponMissiles),
                CreateRobot(2, PlayerId.Player2, 8f, DefaultContentCatalog.WeaponCannon));
            state.Robots[1].CurrentOrder = RobotOrder.Attack(new EntityId(2));
            var combat = new CombatSystem(_catalog);

            combat.Update(state, 0.1f);
            state.Robots[2].Position = new SimVector2(8f, 4f);
            combat.Update(state, 0.8f);

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

            combat.Update(state, 0.1f);
            target.Position = new SimVector2(8f, 4f);
            combat.Update(state, 0.8f);

            Assert.AreEqual(50, target.CurrentHealth);
        }

        [Test]
        public void Electronics_CannonStillLeadsMovingTarget()
        {
            var attacker = CreateRobot(1, PlayerId.Player1, 0f, DefaultContentCatalog.WeaponCannon, DefaultContentCatalog.Electronics);
            var target = CreateRobot(2, PlayerId.Player2, 5f, DefaultContentCatalog.WeaponCannon);
            target.Velocity = new SimVector2(0f, 3f);
            var state = CreateState(attacker, target);
            attacker.CurrentOrder = RobotOrder.Attack(target.Id);
            var combat = new CombatSystem(_catalog);

            combat.Update(state, 0.1f);
            target.Position = new SimVector2(5f, 0.9f);
            combat.Update(state, 0.2f);

            Assert.AreEqual(65, target.CurrentHealth);
        }

        [Test]
        public void LethalAttack_SetsHealthToZero_AndDecrementsArmy()
        {
            var attacker = CreateRobot(1, PlayerId.Player1, 0f, DefaultContentCatalog.WeaponCannon);
            var target = CreateRobot(2, PlayerId.Player2, 2f, DefaultContentCatalog.WeaponCannon);
            target.CurrentHealth = 35;
            var state = CreateState(attacker, target);
            state.Player2.ActiveRobotsCount = 1;
            attacker.CurrentOrder = RobotOrder.Attack(target.Id);

            new CombatSystem(_catalog).Update(state, 0.2f);

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

            attacker.Position = new SimVector2(5.8f, 0f);
            Assert.IsTrue(CombatRangePlanner.TryGetManeuverDestination(attacker, target, _catalog, out var maneuver));
            Assert.AreEqual(4.8f, SimVector2.Distance(maneuver, target.Position), 0.05f, "Манёвр должен сохранять эффективную дальность пушки.");
            Assert.AreNotEqual(0f, maneuver.Y, "В рабочей зоне робот должен двигаться по дуге, а не останавливаться.");
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
            for (int tick = 0; tick < 600 && target.IsAlive; tick++) combat.Update(state, 0.02f);
            Assert.AreEqual(0, target.CurrentHealth);
            Assert.AreEqual(3, hits);
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
            Assert.AreEqual(SimVector2.Distance(attacker.Position, shot.ImpactPosition) / 18f, flight, 0.001f);
            target.Position += target.Velocity * flight;
            combat.Update(state, flight);
            Assert.AreEqual(65, target.CurrentHealth);
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
            Assert.Throws<System.InvalidOperationException>(() => combat.Update(state, 0.2f));
            Assert.AreEqual(65, target.CurrentHealth);
        }

        [Test]
        public void Phaser_HitsInstantlyWithoutProjectile()
        {
            var attacker = CreateRobot(1, PlayerId.Player1, 0f, DefaultContentCatalog.WeaponPhasers);
            var target = CreateRobot(2, PlayerId.Player2, 6f, DefaultContentCatalog.WeaponCannon);
            var state = CreateState(attacker, target);
            attacker.CurrentOrder = RobotOrder.Attack(target.Id);

            new CombatSystem(_catalog).Update(state, 0.05f);

            Assert.AreEqual(70, target.CurrentHealth);
            Assert.AreEqual(0, state.Projectiles.Count);
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
            Assert.AreEqual(170, target.CurrentHealth, "Фазер попадает сразу.");
            Assert.AreEqual(1, state.Projectiles.Count, "Пушка выстрелила в том же тике.");
            Assert.AreEqual(2.0f, attacker.WeaponCooldowns[0], 0.001f);
            Assert.AreEqual(1.5f, attacker.WeaponCooldowns[1], 0.001f);

            combat.Update(state, 0.3f);
            Assert.AreEqual(135, target.CurrentHealth, "Снаряд пушки долетел.");
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
