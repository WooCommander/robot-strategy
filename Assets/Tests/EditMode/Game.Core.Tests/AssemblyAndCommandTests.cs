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
                DefaultContentCatalog.ControlTactical,
                DefaultContentCatalog.EquipArmor
            );

            var res = RobotAssemblyValidator.Validate(bp, _catalog);

            Assert.IsTrue(res.IsValid, "Сборка должна быть валидной");
            Assert.AreEqual(0, res.Errors.Count);

            // Cost: 120 (chassis) + 220 (heavy) + 150 (cannon) + 80 (control) + 80 (armor) = 650
            Assert.AreEqual(650, res.TotalCost);

            // Mass: 60 + 80 + 40 + 12 + 35 = 227 кг (лимит гусениц 230 кг)
            Assert.AreEqual(227, res.TotalMass);
            Assert.IsTrue(res.TotalMass <= res.MaxLoadCapacity);

            // Health: 260 (heavy) + 80 (armor) = 340
            Assert.AreEqual(340, res.TotalHealth);
            Assert.AreEqual(3.0f, res.Speed);
        }

        [Test]
        public void OverloadedBlueprint_ShouldFail_DueToMassLimit()
        {
            // Двуногое шасси имеет лимит 160 кг
            // Biped (40) + Heavy (80) + Missiles (55) + Basic control (8) + Armor (35) = 218 кг > 160 кг!
            var bp = new RobotBlueprint(
                "bp_overload",
                "Перегруженный робот",
                DefaultContentCatalog.ChassisBiped,
                DefaultContentCatalog.BodyHeavy,
                DefaultContentCatalog.WeaponMissiles,
                DefaultContentCatalog.ControlBasic,
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
                null,
                DefaultContentCatalog.ControlBasic
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
                DefaultContentCatalog.WeaponCannon,
                DefaultContentCatalog.ControlBasic
            ); // Стоимость: 100 + 100 + 150 + 40 = 390 металла

            var cmd = new EnqueueProductionCommand
            {
                PlayerId = PlayerId.Player1,
                BaseBuildingId = new EntityId(1),
                Blueprint = bp
            };

            int startMetal = state.Player1.Metal; // 600
            var res = dispatcher.Dispatch(cmd, state);

            Assert.IsTrue(res.IsSuccess);
            Assert.AreEqual(startMetal - 390, state.Player1.Metal);
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
                DefaultContentCatalog.WeaponCannon,
                DefaultContentCatalog.ControlBasic
            ); // 390 металла

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
        public void MissingControlModule_ShouldFailValidation()
        {
            var bp = new RobotBlueprint("bp_no_control", "Без контроллера",
                DefaultContentCatalog.ChassisBiped, DefaultContentCatalog.BodyLight,
                DefaultContentCatalog.WeaponCannon, null);

            var res = RobotAssemblyValidator.Validate(bp, _catalog);

            Assert.IsFalse(res.IsValid);
            Assert.IsTrue(res.Errors.Exists(e => e.Contains("обязательный модуль управления")));
        }

        [Test]
        public void GuidanceControl_OnLightBody_ShouldFailCompatibility()
        {
            var bp = new RobotBlueprint("bp_incompatible_control", "Несовместимый",
                DefaultContentCatalog.ChassisBiped, DefaultContentCatalog.BodyLight,
                DefaultContentCatalog.WeaponCannon, DefaultContentCatalog.ControlGuidance);

            var res = RobotAssemblyValidator.Validate(bp, _catalog);

            Assert.IsFalse(res.IsValid);
            Assert.IsTrue(res.Errors.Exists(e => e.Contains("не поддерживает уровень управления")));
        }

        [Test]
        public void SecondaryWeapon_RequiresHeavyBodyAndAddsToAssemblyCost()
        {
            var light = new RobotBlueprint("light_two_weapons", "Лёгкий", DefaultContentCatalog.ChassisTracks,
                DefaultContentCatalog.BodyLight, DefaultContentCatalog.WeaponCannon, DefaultContentCatalog.ControlBasic,
                secondaryWeaponId: DefaultContentCatalog.WeaponMissiles);
            var heavy = new RobotBlueprint("heavy_two_weapons", "Тяжёлый", DefaultContentCatalog.ChassisTracks,
                DefaultContentCatalog.BodyHeavy, DefaultContentCatalog.WeaponCannon, DefaultContentCatalog.ControlTactical,
                secondaryWeaponId: DefaultContentCatalog.WeaponMissiles);

            Assert.IsFalse(RobotAssemblyValidator.Validate(light, _catalog).IsValid);
            var validation = RobotAssemblyValidator.Validate(heavy, _catalog);
            Assert.IsTrue(validation.IsValid);
            Assert.AreEqual(790, validation.TotalCost);
        }

        [Test]
        public void ConstructorDraft_ShouldBuildAndValidateSelectedModules()
        {
            var draft = new RobotConstructorDraft(_catalog);
            Assert.IsTrue(draft.Select(ModuleSlotType.Chassis, DefaultContentCatalog.ChassisBiped));
            Assert.IsTrue(draft.Select(ModuleSlotType.Body, DefaultContentCatalog.BodyLight));
            Assert.IsTrue(draft.Select(ModuleSlotType.Weapon, DefaultContentCatalog.WeaponCannon));
            Assert.IsTrue(draft.Select(ModuleSlotType.Control, DefaultContentCatalog.ControlBasic));

            var validation = draft.Validate();

            Assert.IsTrue(validation.IsValid);
            Assert.AreEqual(390, validation.TotalCost);
            Assert.IsFalse(draft.Select(ModuleSlotType.Control, DefaultContentCatalog.WeaponCannon));
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
