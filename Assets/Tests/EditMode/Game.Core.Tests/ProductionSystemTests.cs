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

            // 1. Ставим блокирующего робота прямо на спавн-поинт
            var blockingRobot = new RobotState
            {
                Id = new EntityId(99),
                Owner = PlayerId.Player1,
                CurrentHealth = 100,
                Position = spawnPoint
            };
            state.Robots[99] = blockingRobot;

            // 2. Ставим заказ на производство
            var bp = new RobotBlueprint(
                "bp_scout",
                "Разведчик",
                DefaultContentCatalog.ChassisBiped,
                DefaultContentCatalog.BodyLight,
                DefaultContentCatalog.WeaponCannon,
                DefaultContentCatalog.ControlBasic
            );

            var cmd = new EnqueueProductionCommand
            {
                PlayerId = PlayerId.Player1,
                BaseBuildingId = new EntityId(1),
                Blueprint = bp
            };
            dispatcher.Dispatch(cmd, state);

            Assert.AreEqual(1, baseBuilding.ProductionQueue.Count);
            var order = baseBuilding.ProductionQueue[0];

            // 3. Прокручиваем время вперед больше, чем время сборки
            production.Update(state, order.TotalBuildTimeSeconds + 1.0f);

            // 4. Заказ должен перейти в статус ExitBlocked, так как выход заблокирован
            Assert.AreEqual(OrderProductionStatus.ExitBlocked, order.Status, "Заказ должен ждать освобождения выхода");
            Assert.AreEqual(1, baseBuilding.ProductionQueue.Count, "Заказ не должен исчезать из очереди");

            // 5. Освобождаем выход (передвигаем блокирующего робота в сторону)
            blockingRobot.Position = new SimVector2(20.0f, 20.0f);

            // 6. Прокручиваем 1 такт
            production.Update(state, 0.05f);

            // 7. Робот должен успешно появиться на поле, а очередь очиститься
            Assert.AreEqual(0, baseBuilding.ProductionQueue.Count, "Заказ должен завершиться");
            Assert.AreEqual(2, state.Robots.Count, "Должен появиться новый произведенный робот");
            RobotState producedRobot = state.Robots.Values.First(robot => robot.Id.Value != blockingRobot.Id.Value);
            Assert.AreEqual(OrderType.Defend, producedRobot.CurrentOrder.Type);
            Assert.AreEqual(baseBuilding.Position, producedRobot.CurrentOrder.TargetPosition);
        }
    }
}
