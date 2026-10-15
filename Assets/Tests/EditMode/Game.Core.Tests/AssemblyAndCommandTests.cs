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
        public void ValidBlueprint_ShouldPassValidation_AndCalculateCostByResourceType()
        {
            var bp = new RobotBlueprint("bp_1", "Танк", DefaultContentCatalog.ChassisTracks,
                new[] { DefaultContentCatalog.WeaponCannon, DefaultContentCatalog.WeaponPhasers }, DefaultContentCatalog.Electronics);

            var res = RobotAssemblyValidator.Validate(bp, _catalog);

            Assert.IsTrue(res.IsValid, string.Join("; ", res.Errors));
            Assert.AreEqual(5, res.CostByType[(int)ResourceType.Chassis]);
            Assert.AreEqual(2, res.CostByType[(int)ResourceType.Cannon]);
            Assert.AreEqual(4, res.CostByType[(int)ResourceType.Phasers]);
            Assert.AreEqual(3, res.CostByType[(int)ResourceType.Electronics]);
            Assert.AreEqual(14, res.TotalCost);
            Assert.AreEqual(160, res.TotalHealth, "Гусеницы 140 + по 10 за каждое орудие.");
            Assert.AreEqual(3.6f, res.Speed);
        }

        [Test]
        public void SameWeaponTwice_ShouldFailValidation()
        {
            var bp = new RobotBlueprint("bp_dup", "Две пушки", DefaultContentCatalog.ChassisBipod,
                new[] { DefaultContentCatalog.WeaponCannon, DefaultContentCatalog.WeaponCannon });

            var res = RobotAssemblyValidator.Validate(bp, _catalog);

            Assert.IsFalse(res.IsValid);
            Assert.IsTrue(res.Errors.Exists(e => e.Contains("только один раз")));
        }

        [Test]
        public void MissingChassis_ShouldFailValidation()
        {
            var res = RobotAssemblyValidator.Validate(new RobotBlueprint("bp", "Без шасси", null, new[] { DefaultContentCatalog.WeaponCannon }), _catalog);

            Assert.IsFalse(res.IsValid);
            Assert.IsTrue(res.Errors.Exists(e => e.Contains("Не выбрано шасси")));
        }

        [Test]
        public void RobotWithoutWeaponOrNuclear_ShouldFail_ButNuclearOnlyIsValid()
        {
            var unarmed = new RobotBlueprint("bp_unarmed", "Безоружный", DefaultContentCatalog.ChassisBipod, new string[0], DefaultContentCatalog.Electronics);
            var bomber = new RobotBlueprint("bp_bomber", "Камикадзе", DefaultContentCatalog.ChassisAntiGrav, new string[0], nuclearId: DefaultContentCatalog.Nuclear);

            Assert.IsFalse(RobotAssemblyValidator.Validate(unarmed, _catalog).IsValid);
            var bomberResult = RobotAssemblyValidator.Validate(bomber, _catalog);
            Assert.IsTrue(bomberResult.IsValid);
            Assert.AreEqual(30, bomberResult.TotalCost);
        }

        [Test]
        public void A01_EnqueueProduction_PaysWithOwnResourceTypeFirst_ThenGeneral()
        {
            var dispatcher = new CommandDispatcher(_catalog);
            var state = CreateTestMatch();
            state.Player1.Resources[ResourceType.General] = 10;
            state.Player1.Resources[ResourceType.Cannon] = 1;
            state.Player1.Resources[ResourceType.Chassis] = 5;

            var res = dispatcher.Dispatch(new EnqueueProductionCommand
            {
                PlayerId = PlayerId.Player1,
                BaseBuildingId = new EntityId(1),
                Blueprint = RobotPresetCatalog.Scout.CreateBlueprint() // двуногое 3 шасси + пушка 2
            }, state);

            Assert.IsTrue(res.IsSuccess, res.ErrorReason);
            Assert.AreEqual(2, state.Player1.Resources[ResourceType.Chassis], "Шасси оплачено своим ресурсом.");
            Assert.AreEqual(0, state.Player1.Resources[ResourceType.Cannon], "Единственная «пушечная» единица потрачена.");
            Assert.AreEqual(9, state.Player1.Resources[ResourceType.General], "Недостающую единицу пушки покрыл General.");
            Assert.AreEqual(1, state.Buildings[1].ProductionQueue.Count);
            Assert.AreEqual(1, state.Player1.QueuedRobotsCount);
        }

        [Test]
        public void A02_EnqueueProduction_WithoutEnoughResources_ShouldFailAndNotChangeState()
        {
            var dispatcher = new CommandDispatcher(_catalog);
            var state = CreateTestMatch();
            state.Player1.Resources[ResourceType.General] = 4;

            var res = dispatcher.Dispatch(new EnqueueProductionCommand
            {
                PlayerId = PlayerId.Player1,
                BaseBuildingId = new EntityId(1),
                Blueprint = RobotPresetCatalog.Scout.CreateBlueprint()
            }, state);

            Assert.IsFalse(res.IsSuccess);
            Assert.AreEqual(4, state.Player1.Resources[ResourceType.General]);
            Assert.AreEqual(0, state.Buildings[1].ProductionQueue.Count);
        }

        [Test]
        public void CancelProduction_RefundsExactlyWhatWasPaid()
        {
            var dispatcher = new CommandDispatcher(_catalog);
            var state = CreateTestMatch();
            state.Player1.Resources[ResourceType.General] = 10;
            state.Player1.Resources[ResourceType.Chassis] = 2;
            int[] before = state.Player1.Resources.ToArray();
            dispatcher.Dispatch(new EnqueueProductionCommand { PlayerId = PlayerId.Player1, BaseBuildingId = new EntityId(1), Blueprint = RobotPresetCatalog.Scout.CreateBlueprint() }, state);
            string orderId = state.Buildings[1].ProductionQueue[0].OrderId;

            var res = dispatcher.Dispatch(new CancelProductionCommand { PlayerId = PlayerId.Player1, BaseBuildingId = new EntityId(1), OrderId = orderId }, state);

            Assert.IsTrue(res.IsSuccess, res.ErrorReason);
            CollectionAssert.AreEqual(before, state.Player1.Resources.ToArray());
        }

        [Test]
        public void ConstructorDraft_TogglesPartsLikeOriginalScreen()
        {
            var draft = new RobotConstructorDraft(_catalog);

            Assert.IsTrue(draft.Toggle(DefaultContentCatalog.ChassisBipod));
            Assert.IsTrue(draft.Toggle(DefaultContentCatalog.ChassisTracks));
            Assert.AreEqual(DefaultContentCatalog.ChassisTracks, draft.ChassisId, "Шасси одно: новое заменяет прежнее.");

            draft.Toggle(DefaultContentCatalog.WeaponCannon);
            draft.Toggle(DefaultContentCatalog.WeaponPhasers);
            draft.Toggle(DefaultContentCatalog.WeaponMissiles);
            draft.Toggle(DefaultContentCatalog.WeaponCannon);
            CollectionAssert.AreEqual(new[] { DefaultContentCatalog.WeaponPhasers, DefaultContentCatalog.WeaponMissiles }, draft.WeaponIds);

            draft.Toggle(DefaultContentCatalog.Electronics);
            var validation = draft.Validate();

            Assert.IsTrue(validation.IsValid);
            Assert.AreEqual(16, validation.TotalCost);
            Assert.AreEqual("ГУСЕНИЦЫ + ЭЛЕКТРОНИКА + ФАЗЕР + РАКЕТЫ", draft.DescribeSelection());
        }

        [Test]
        public void ResourceStock_PlansPaymentWithoutChangingStock()
        {
            var stock = new ResourceStock();
            stock[ResourceType.General] = 3;
            stock[ResourceType.Nuclear] = 18;
            var cost = new int[ResourceStock.TypeCount];
            cost[(int)ResourceType.Nuclear] = 20;

            Assert.IsTrue(stock.TryPlanPayment(cost, out int[] payment));
            Assert.AreEqual(18, payment[(int)ResourceType.Nuclear]);
            Assert.AreEqual(2, payment[(int)ResourceType.General]);
            Assert.AreEqual(18, stock[ResourceType.Nuclear], "Планирование ничего не списывает.");

            stock[ResourceType.General] = 1;
            Assert.IsFalse(stock.TryPlanPayment(cost, out _));
        }

        [Test]
        public void A13_OrderCommand_ToEnemyRobot_ShouldBeRejected()
        {
            var dispatcher = new CommandDispatcher(_catalog);
            var state = CreateTestMatch();
            state.Robots[100] = new RobotState { Id = new EntityId(100), Owner = PlayerId.Player2, CurrentHealth = 100, Position = new SimVector2(10, 10) };

            var res = dispatcher.Dispatch(new OrderMoveCommand
            {
                PlayerId = PlayerId.Player1,
                RobotIds = new List<EntityId> { new EntityId(100) },
                TargetPosition = new SimVector2(0, 0)
            }, state);

            Assert.IsFalse(res.IsSuccess);
            Assert.IsTrue(res.ErrorReason.Contains("принадлежит другому игроку"));
        }

        [Test]
        public void Detonate_RequiresNuclearCharge()
        {
            var dispatcher = new CommandDispatcher(_catalog);
            var state = CreateTestMatch();
            state.Robots[10] = new RobotState
            {
                Id = new EntityId(10), Owner = PlayerId.Player1, CurrentHealth = 100,
                Blueprint = RobotPresetCatalog.Scout.CreateBlueprint()
            };

            var res = dispatcher.Dispatch(new OrderDetonateCommand { PlayerId = PlayerId.Player1, RobotIds = new List<EntityId> { new EntityId(10) } }, state);

            Assert.IsFalse(res.IsSuccess);
            Assert.IsTrue(res.ErrorReason.Contains("нет ядерного заряда"));
        }

        private MatchState CreateTestMatch()
        {
            var state = new MatchState();
            state.Buildings[1] = new BuildingState
            {
                Id = new EntityId(1),
                Type = BuildingType.Base,
                Owner = PlayerId.Player1,
                Position = new SimVector2(0, 0),
                SpawnPoint = new SimVector2(2, 0)
            };
            return state;
        }
    }
}
