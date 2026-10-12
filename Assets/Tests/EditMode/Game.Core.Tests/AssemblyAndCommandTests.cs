using System.Collections.Generic;
using NUnit.Framework;
using Game.Content.Definitions;
using Game.Content.Validation;
using Game.Core.Commands;
using Game.Core.Models;
using Game.Core.Types;
using Game.Simulation.Systems;

namespace Game.Core.Tests
{
    [TestFixture]
    public class AssemblyAndCommandTests
    {
        private Dictionary<string, ModuleDefinition> _catalog;

        [SetUp]
        public void Setup()
        {
            _catalog = DefaultContentCatalog.CreateDefaultModules();
        }

        [Test]
        public void ValidBlueprint_ShouldPassValidation_AndCalculateCorrectStats()
        {
            var bp = new RobotBlueprint(
                "bp_1",
                "Танк",
                DefaultContentCatalog.ChassisTracks,
                DefaultContentCatalog.BodyHeavy,
                DefaultContentCatalog.WeaponCannon,
                DefaultContentCatalog.EquipArmor
            );

            var res = RobotAssemblyValidator.Validate(bp, _catalog);

            Assert.IsTrue(res.IsValid, "Сборка должна быть валидной");
            Assert.AreEqual(0, res.Errors.Count);

            // Cost: 120 (chassis) + 220 (heavy) + 150 (cannon) + 80 (armor) = 570
            Assert.AreEqual(570, res.TotalCost);

            // Mass: 60 + 80 + 40 + 35 = 215 кг (лимит гусениц 230 кг)
            Assert.AreEqual(215, res.TotalMass);
            Assert.IsTrue(res.TotalMass <= res.MaxLoadCapacity);

            // Health: 260 (heavy) + 80 (armor) = 340
            Assert.AreEqual(340, res.TotalHealth);
            Assert.AreEqual(3.0f, res.Speed);
        }

        [Test]
        public void OverloadedBlueprint_ShouldFail_DueToMassLimit()
        {
            // Двуногое шасси имеет лимит 160 кг
            // Biped (40) + Heavy (80) + Missiles (55) + Armor (35) = 210 кг > 160 кг!
            var bp = new RobotBlueprint(
                "bp_overload",
                "Перегруженный робот",
                DefaultContentCatalog.ChassisBiped,
                DefaultContentCatalog.BodyHeavy,
                DefaultContentCatalog.WeaponMissiles,
                DefaultContentCatalog.EquipArmor
            );

            var res = RobotAssemblyValidator.Validate(bp, _catalog);

            Assert.IsFalse(res.IsValid, "Сборка с перегрузом должна быть отклонена");
            Assert.IsTrue(res.Errors.Exists(e => e.Contains("Превышена грузоподъёмность")));
        }

        [Test]
        public void MissingWeapon_ShouldFailValidation()
        {
            var bp = new RobotBlueprint(
                "bp_no_gun",
                "Безоружный",
                DefaultContentCatalog.ChassisTracks,
                DefaultContentCatalog.BodyLight,
                null
            );

            var res = RobotAssemblyValidator.Validate(bp, _catalog);

            Assert.IsFalse(res.IsValid);
            Assert.IsTrue(res.Errors.Exists(e => e.Contains("Не выбрано обязательное оружие")));
        }

        [Test]
        public void A01_EnqueueProduction_WithEnoughResources_ShouldDeductMetalAndEnqueue()
        {
            var dispatcher = new CommandDispatcher(_catalog);
            var state = CreateTestMatch();

            var bp = new RobotBlueprint(
                "bp_scout",
                "Разведчик",
                DefaultContentCatalog.ChassisBiped,
                DefaultContentCatalog.BodyLight,
                DefaultContentCatalog.WeaponCannon
            ); // Стоимость: 100 + 100 + 150 = 350 металла

            var cmd = new EnqueueProductionCommand
            {
                PlayerId = PlayerId.Player1,
                BaseBuildingId = new EntityId(1),
                Blueprint = bp
            };

            int startMetal = state.Player1.Metal; // 600
            var res = dispatcher.Dispatch(cmd, state);

            Assert.IsTrue(res.IsSuccess);
            Assert.AreEqual(startMetal - 350, state.Player1.Metal);
            Assert.AreEqual(1, state.Buildings[1].ProductionQueue.Count);
            Assert.AreEqual(1, state.Player1.QueuedRobotsCount);
        }

        [Test]
        public void A02_EnqueueProduction_WithoutEnoughResources_ShouldFailAndNotChangeState()
        {
            var dispatcher = new CommandDispatcher(_catalog);
            var state = CreateTestMatch();
            state.Player1.Metal = 100; // Меньше стоимости сборки

            var bp = new RobotBlueprint(
                "bp_scout",
                "Разведчик",
                DefaultContentCatalog.ChassisBiped,
                DefaultContentCatalog.BodyLight,
                DefaultContentCatalog.WeaponCannon
            ); // 350 металла

            var cmd = new EnqueueProductionCommand
            {
                PlayerId = PlayerId.Player1,
                BaseBuildingId = new EntityId(1),
                Blueprint = bp
            };

            var res = dispatcher.Dispatch(cmd, state);

            Assert.IsFalse(res.IsSuccess);
            Assert.AreEqual(100, state.Player1.Metal);
            Assert.AreEqual(0, state.Buildings[1].ProductionQueue.Count);
        }

        [Test]
        public void A13_OrderCommand_ToEnemyRobot_ShouldBeRejected()
        {
            var dispatcher = new CommandDispatcher(_catalog);
            var state = CreateTestMatch();

            // Робот принадлежит Player2
            var enemyRobot = new RobotState
            {
                Id = new EntityId(100),
                Owner = PlayerId.Player2,
                CurrentHealth = 100,
                Position = new SimVector2(10, 10)
            };
            state.Robots[100] = enemyRobot;

            // Player1 пытается отдать ему приказ
            var cmd = new OrderMoveCommand
            {
                PlayerId = PlayerId.Player1,
                RobotIds = new List<EntityId> { new EntityId(100) },
                TargetPosition = new SimVector2(0, 0)
            };

            var res = dispatcher.Dispatch(cmd, state);

            Assert.IsFalse(res.IsSuccess);
            Assert.IsTrue(res.ErrorReason.Contains("принадлежит другому игроку"));
        }

        private MatchState CreateTestMatch()
        {
            var state = new MatchState();
            state.Player1.Metal = 600;
            state.Player2.Metal = 600;

            var base1 = new BuildingState
            {
                Id = new EntityId(1),
                Type = BuildingType.Base,
                Owner = PlayerId.Player1,
                Position = new SimVector2(0, 0),
                SpawnPoint = new SimVector2(2, 0)
            };
            state.Buildings[1] = base1;

            return state;
        }
    }
}
