using Game.Core.Models;
using Game.Core.Types;

namespace Game.Simulation.Navigation
{
    /// <summary>Габариты корпусов зданий: общие для сетки навигации, разъезда роботов и точек захвата.</summary>
    public static class BuildingFootprint
    {
        public const float BaseClearance = 2.35f;
        public const float FactoryClearance = 1.65f;

        public static float GetClearance(BuildingType type) => type == BuildingType.Base ? BaseClearance : FactoryClearance;

        /// <summary>Сетка карты для класса шасси, в которой корпуса баз и заводов уже непроходимы.</summary>
        public static NavigationGrid CreateMatchGrid(MapDefinition map, MobilityClass mobility = MobilityClass.Tracked)
        {
            var grid = new NavigationGrid(map, mobility);
            foreach (BuildingSpawnDefinition building in map.Buildings)
            {
                grid.BlockCircle(building.Position, GetClearance(building.Type));
            }
            return grid;
        }
    }

    /// <summary>Навигация матча: отдельная сетка и поиск пути для шагающих, гусениц и антиграва.</summary>
    public sealed class MatchNavigation
    {
        private readonly NavigationGrid[] _grids = new NavigationGrid[3];
        private readonly AStarPathfinder[] _pathfinders = new AStarPathfinder[3];

        public MatchNavigation(MapDefinition map)
        {
            foreach (MobilityClass mobility in new[] { MobilityClass.Walker, MobilityClass.Tracked, MobilityClass.Hover })
            {
                NavigationGrid grid = BuildingFootprint.CreateMatchGrid(map, mobility);
                _grids[(int)mobility] = grid;
                _pathfinders[(int)mobility] = new AStarPathfinder(grid);
            }
        }

        public NavigationGrid GetGrid(MobilityClass mobility) => _grids[(int)mobility];
        public AStarPathfinder GetPathfinder(MobilityClass mobility) => _pathfinders[(int)mobility];
    }
}
