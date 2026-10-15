using System.Collections.Generic;
using NUnit.Framework;
using Game.Content.Definitions;
using Game.Content.Validation;
using Game.Core.Models;
using Game.Core.Types;
using Game.Simulation.Systems;

namespace Game.Core.Tests
{
    /// <summary>
    /// Роли оружия проверяются полными боями в симуляции (движение, манёвр, снаряды) на ровной карте:
    /// пушка — дешёвая масса ближнего боя, ракеты — дальний огонь по скоплениям, фазер — точный дуэлянт.
    /// Если правка цифр ломает тест, роли оружия перестали различаться.
    /// </summary>
    [TestFixture]
    public class WeaponBalanceTests
    {
        private const string Cannon = DefaultContentCatalog.WeaponCannon;
        private const string Missiles = DefaultContentCatalog.WeaponMissiles;
        private const string Phasers = DefaultContentCatalog.WeaponPhasers;

        private Dictionary<string, ModuleDefinition> _catalog;

        [SetUp]
        public void SetUp()
        {
            _catalog = DefaultContentCatalog.CreateDefaultModules();
        }

        /// <summary>Победитель должен брать не меньше 6 из 8 вариантов: разные ID (направление манёвра) и обе стороны карты.</summary>
        private const int Variants = 8;
        private const int RequiredWins = 6;

        [TestCase(Phasers, Cannon)]
        [TestCase(Phasers, Missiles)]
        [TestCase(Cannon, Missiles)]
        public void Duel_OneOnOne(string expectedWinner, string loser)
        {
            AssertWins(expectedWinner, 1, loser, 1, 2f);
        }

        [TestCase(Cannon)]
        [TestCase(Phasers)]
        public void Missiles_WinAgainstTightGroups(string enemyWeapon)
        {
            AssertWins(Missiles, 3, enemyWeapon, 3, 2f);
        }

        [Test]
        public void Phasers_WinAgainstMissiles_WhenEnemySpreadsOut()
        {
            AssertWins(Phasers, 3, Missiles, 3, 6f);
        }

        [Test]
        public void CheapCannons_WinAgainstPhasers_AtEqualCost()
        {
            // 4 × (гусеницы 5 + пушка 2) = 28 против 3 × (гусеницы 5 + фазер 4) = 27.
            AssertWins(Cannon, 4, Phasers, 3, 2f);
        }

        private void AssertWins(string winnerWeapon, int winnerCount, string loserWeapon, int loserCount, float spacing)
        {
            int wins = 0;
            for (int variant = 0; variant < Variants; variant++)
            {
                bool winnerOnLeft = variant < Variants / 2;
                int winnerShift = variant & 1;
                int loserShift = (variant >> 1) & 1;
                PlayerId winnerSide = winnerOnLeft ? PlayerId.Player1 : PlayerId.Player2;
                PlayerId loserSide = winnerOnLeft ? PlayerId.Player2 : PlayerId.Player1;
                var winners = Squad(winnerSide, winnerWeapon, winnerCount, spacing, winnerShift);
                var losers = Squad(loserSide, loserWeapon, loserCount, spacing, loserShift);
                PlayerId result = winnerOnLeft ? Fight(winners, losers) : Fight(losers, winners);
                if (result == winnerSide) wins++;
            }
            Assert.GreaterOrEqual(wins, RequiredWins,
                $"{winnerCount}×{_catalog[winnerWeapon].ShortName} против {loserCount}×{_catalog[loserWeapon].ShortName}: побед {wins} из {Variants}.");
        }

        private List<RobotState> Squad(PlayerId owner, string weaponId, int count, float spacing, int idShift)
        {
            var squad = new List<RobotState>();
            for (int index = 0; index < count; index++)
            {
                var blueprint = new RobotBlueprint("balance", "Тест", DefaultContentCatalog.ChassisTracks, new[] { weaponId });
                var stats = RobotAssemblyValidator.Validate(blueprint, _catalog);
                var robot = new RobotState
                {
                    Id = new EntityId((owner == PlayerId.Player1 ? 100 : 200) + index * 2 + idShift),
                    Owner = owner,
                    Blueprint = blueprint,
                    Position = new SimVector2(owner == PlayerId.Player1 ? 20f : 50f, 20f + (index - (count - 1) * 0.5f) * spacing),
                    MaxHealth = stats.TotalHealth,
                    CurrentHealth = stats.TotalHealth,
                    MoveSpeed = stats.Speed,
                    Mobility = stats.Mobility,
                    CurrentOrder = RobotOrder.Hunt()
                };
                robot.WeaponCooldowns.Add(0f);
                squad.Add(robot);
            }
            return squad;
        }

        private PlayerId Fight(List<RobotState> left, List<RobotState> right)
        {
            var state = new MatchState();
            foreach (RobotState robot in left) state.Robots[robot.Id.Value] = robot;
            foreach (RobotState robot in right) state.Robots[robot.Id.Value] = robot;
            state.Player1.ActiveRobotsCount = left.Count;
            state.Player2.ActiveRobotsCount = right.Count;
            var simulation = new MatchSimulation(_catalog, CreateFlatMap(), state);
            for (int tick = 0; tick < MatchSimulation.TicksPerSecond * 120; tick++)
            {
                simulation.Step(MatchSimulation.FixedDeltaSeconds);
                if (state.Player1.ActiveRobotsCount == 0) return PlayerId.Player2;
                if (state.Player2.ActiveRobotsCount == 0) return PlayerId.Player1;
            }
            return PlayerId.Neutral;
        }

        private static MapDefinition CreateFlatMap()
        {
            const int width = 70, height = 40;
            var map = new MapDefinition { Id = "balance_flat", Width = width, Height = height, Tiles = new TileType[width * height] };
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    bool edge = x == 0 || y == 0 || x == width - 1 || y == height - 1;
                    map.SetTile(x, y, edge ? TileType.Mountain : TileType.Ground);
                }
            }
            return map;
        }
    }
}
