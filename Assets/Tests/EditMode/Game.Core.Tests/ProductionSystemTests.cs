using System.Linq;
using NUnit.Framework;
using Game.Content.Definitions;
using Game.Core.Commands;
using Game.Core.Models;
using Game.Core.Types;
using Game.Simulation.Systems;

namespace Game.Core.Tests
{
    [TestFixture]
    public class ProductionSystemTests
    {
        [Test]
        public void A03_ExitBlocked_ShouldWaitUntilExitIsFree()
        {
            var catalog = DefaultContentCatalog.CreateDefaultModules();
            var dispatcher = new CommandDispatcher(catalog);
            var production = new ProductionSystem(catalog);

            var state = new MatchState();
            state.Player1.Resources[ResourceType.General] = 20;
            var spawnPoint = new SimVector2(6.5f, 10.0f);
            var baseBuilding = new BuildingState
            {
                Id = new EntityId(1),
                Type = BuildingType.Base,
                Owner = PlayerId.Player1,
                Position = new SimVector2(4.5f, 10.0f),
                SpawnPoint = spawnPoint
            };
            state.Buildings[1] = baseBuilding;

            // 1. Ставим блокирующего робота прямо на точку выхода
            var blockingRobot = new RobotState { Id = new EntityId(99), Owner = PlayerId.Player1, CurrentHealth = 100, Position = spawnPoint };
            state.Robots[99] = blockingRobot;

            // 2. Ставим заказ на производство
            dispatcher.Dispatch(new EnqueueProductionCommand
            {
                PlayerId = PlayerId.Player1,
                BaseBuildingId = new EntityId(1),
                Blueprint = new RobotBlueprint("bp_tank", "Танк", DefaultContentCatalog.ChassisTracks,
                    new[] { DefaultContentCatalog.WeaponCannon, DefaultContentCatalog.WeaponPhasers })
            }, state);

            Assert.AreEqual(1, baseBuilding.ProductionQueue.Count);
            var order = baseBuilding.ProductionQueue[0];

            // 3. Прокручиваем время вперёд больше, чем время сборки
            production.Update(state, order.TotalBuildTimeSeconds + 1.0f);

            // 4. Заказ ждёт освобождения выхода
            Assert.AreEqual(OrderProductionStatus.ExitBlocked, order.Status, "Заказ должен ждать освобождения выхода");
            Assert.AreEqual(1, baseBuilding.ProductionQueue.Count, "Заказ не должен исчезать из очереди");

            // 5. Освобождаем выход
            blockingRobot.Position = new SimVector2(20.0f, 20.0f);
            production.Update(state, 0.05f);

            // 6. Робот появился с перезарядкой на каждое орудие и приказом обороны
            Assert.AreEqual(0, baseBuilding.ProductionQueue.Count, "Заказ должен завершиться");
            Assert.AreEqual(2, state.Robots.Count, "Должен появиться новый произведённый робот");
            RobotState producedRobot = state.Robots.Values.First(robot => robot.Id.Value != blockingRobot.Id.Value);
            Assert.AreEqual(OrderType.Defend, producedRobot.CurrentOrder.Type);
            Assert.AreEqual(baseBuilding.Position, producedRobot.CurrentOrder.TargetPosition);
            Assert.AreEqual(2, producedRobot.WeaponCooldowns.Count);
            Assert.AreEqual(160, producedRobot.MaxHealth);
        }

        [Test]
        public void HourlyIncome_BaseGivesGeneral_FactoryGivesItsResource_NeutralGivesNothing()
        {
            var production = new ProductionSystem(DefaultContentCatalog.CreateDefaultModules());
            var state = new MatchState();
            state.Buildings[1] = new BuildingState { Id = new EntityId(1), Type = BuildingType.Base, Owner = PlayerId.Player1 };
            state.Buildings[101] = new BuildingState { Id = new EntityId(101), Type = BuildingType.Factory, Owner = PlayerId.Player1, ProducedResource = ResourceType.Cannon };
            state.Buildings[102] = new BuildingState { Id = new EntityId(102), Type = BuildingType.Factory, Owner = PlayerId.Player2, ProducedResource = ResourceType.Missiles };
            state.Buildings[103] = new BuildingState { Id = new EntityId(103), Type = BuildingType.Factory, Owner = PlayerId.Neutral, ProducedResource = ResourceType.Nuclear };

            // Почти час: дохода ещё нет.
            Advance(production, state, GameClock.SecondsPerHour - 0.1f);
            Assert.AreEqual(0, state.Player1.Resources.Total);

            // Два полных часа.
            Advance(production, state, GameClock.SecondsPerHour + 0.1f);
            Assert.AreEqual(2 * EconomyRules.BaseGeneralPerHour, state.Player1.Resources[ResourceType.General]);
            Assert.AreEqual(2 * EconomyRules.FactoryResourcePerHour, state.Player1.Resources[ResourceType.Cannon]);
            Assert.AreEqual(2 * EconomyRules.FactoryResourcePerHour, state.Player2.Resources[ResourceType.Missiles]);
            Assert.AreEqual(0, state.Player1.Resources[ResourceType.Nuclear] + state.Player2.Resources[ResourceType.Nuclear]);
            Assert.AreEqual(2, state.IncomeHoursPaid);
        }

        [Test]
        public void GameClock_DayLastsTwoMinutes()
        {
            Assert.AreEqual(1, GameClock.GetDay(0f));
            Assert.AreEqual(5, GameClock.GetHourOfDay(25f));
            Assert.AreEqual(2, GameClock.GetDay(120f));
            Assert.AreEqual(0, GameClock.GetHourOfDay(120f));
        }

        private static void Advance(ProductionSystem production, MatchState state, float seconds)
        {
            const float step = 0.05f;
            for (float elapsed = 0f; elapsed < seconds - 0.0001f; elapsed += step)
            {
                production.Update(state, step);
                state.MatchTimeSeconds += step;
            }
        }
    }
}
