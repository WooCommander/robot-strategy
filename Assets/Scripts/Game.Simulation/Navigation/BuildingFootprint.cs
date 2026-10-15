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

        /// <summary>Сетка карты, в которой корпуса баз и заводов уже непроходимы.</summary>
        public static NavigationGrid CreateMatchGrid(MapDefinition map)
        {
            var grid = new NavigationGrid(map);
            foreach (BuildingSpawnDefinition building in map.Buildings)
            {
                grid.BlockCircle(building.Position, GetClearance(building.Type));
            }
            return grid;
        }
    }
}
