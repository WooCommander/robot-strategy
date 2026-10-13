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

            new CombatSystem(_catalog).Update(state, 0.1f);

            Assert.AreEqual(65, state.Robots[2].CurrentHealth);
            Assert.AreEqual(1.5f, state.Robots[1].WeaponCooldownRemaining, 0.001f);
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

            new CombatSystem(_catalog).Update(state, 0.1f);

            Assert.AreEqual(50, state.Robots[2].CurrentHealth);
            Assert.AreEqual(50, state.Robots[3].CurrentHealth);
            Assert.AreEqual(100, state.Robots[4].CurrentHealth);
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

            new CombatSystem(_catalog).Update(state, 0.1f);

            Assert.AreEqual(0, target.CurrentHealth);
            Assert.AreEqual(0, state.Player2.ActiveRobotsCount);
            Assert.AreEqual(OrderType.Stop, target.CurrentOrder.Type);
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

        private static RobotState CreateRobot(int id, PlayerId owner, float x, string weaponId)
        {
            return new RobotState
            {
                Id = new EntityId(id),
                Owner = owner,
                Position = new SimVector2(x, 0f),
                CurrentHealth = 100,
                MaxHealth = 100,
                Blueprint = new RobotBlueprint("bp_" + id, "Test", DefaultContentCatalog.ChassisBiped,
                    DefaultContentCatalog.BodyLight, weaponId)
            };
        }
    }
}
