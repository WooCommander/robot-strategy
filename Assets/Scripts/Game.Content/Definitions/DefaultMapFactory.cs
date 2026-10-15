using Game.Core.Models;
using Game.Core.Types;

namespace Game.Content.Definitions
{
    /// <summary>
    /// Карта прототипа 128×80. Планировка детерминирована: одна и та же карта нужна для сохранений и replay.
    /// Препятствия разделяют шасси: шагающим нужна ровная земля, гусеницы проходят неровности,
    /// антиграв летит над водой, скалами и трещинами — его держат только здания.
    /// </summary>
    public static class DefaultMapFactory
    {
        public const string PrototypeMapId = "map_prototype_valley";
        public const int Width = 128;
        public const int Height = 80;

        // Магистрали: южная, центральная, северная. Разведка ходит по этим же рядам.
        public const int SouthRoadY = 22;
        public const int CenterRoadY = 40;
        public const int NorthRoadY = 58;

        private const int RiverX = 63;          // река занимает две колонки: 63 и 64
        private const int WestRidgeX = 46;      // хребты по две колонки
        private const int EastRidgeX = 80;

        public static MapDefinition CreatePrototypeMap()
        {
            var map = new MapDefinition
            {
                Id = PrototypeMapId,
                Name = "Зелёная долина",
                Width = Width,
                Height = Height,
                TileSize = 1.0f,
                Tiles = new TileType[Width * Height]
            };

            Fill(map, 0, 0, Width - 1, Height - 1, TileType.Ground);
            BuildBorder(map);
            BuildRiver(map);
            BuildRidges(map);
            BuildRoads(map);
            BuildRoughFields(map);
            BuildCracks(map);
            BuildLakes(map);
            AddBuildings(map);
            return map;
        }

        private static void BuildBorder(MapDefinition map)
        {
            Fill(map, 0, 0, Width - 1, 0, TileType.Mountain);
            Fill(map, 0, Height - 1, Width - 1, Height - 1, TileType.Mountain);
            Fill(map, 0, 0, 0, Height - 1, TileType.Mountain);
            Fill(map, Width - 1, 0, Width - 1, Height - 1, TileType.Mountain);
        }

        /// <summary>Река через всю карту; два брода с заводами на севере и юге, мосты на магистралях.</summary>
        private static void BuildRiver(MapDefinition map)
        {
            Fill(map, RiverX, 1, RiverX + 1, Height - 2, TileType.Water);
            Fill(map, RiverX - 2, 5, RiverX + 3, 11, TileType.Ground);   // южный брод: завод электроники
            Fill(map, RiverX - 2, 68, RiverX + 3, 74, TileType.Ground);  // северный брод: ядерный завод
        }

        /// <summary>
        /// Хребты с проходами у магистралей. Северные проходы завалены неровностями:
        /// гусеницы и антиграв проходят, шагающим остаются центр и юг.
        /// </summary>
        private static void BuildRidges(MapDefinition map)
        {
            foreach (int ridgeX in new[] { WestRidgeX, EastRidgeX })
            {
                Fill(map, ridgeX, 1, ridgeX + 1, Height - 2, TileType.Mountain);
                Fill(map, ridgeX, SouthRoadY - 3, ridgeX + 1, SouthRoadY + 3, TileType.Ground);
                Fill(map, ridgeX, CenterRoadY - 3, ridgeX + 1, CenterRoadY + 3, TileType.Ground);
                Fill(map, ridgeX, NorthRoadY - 3, ridgeX + 1, NorthRoadY + 3, TileType.Rough);
            }
        }

        private static void BuildRoads(MapDefinition map)
        {
            foreach (int roadY in new[] { SouthRoadY, CenterRoadY, NorthRoadY })
            {
                for (int x = 9; x <= Width - 10; x++)
                {
                    TileType current = map.GetTile(x, roadY);
                    if (current == TileType.Water) Fill(map, x, roadY - 1, x, roadY + 1, TileType.Bridge);
                    else if (current == TileType.Ground) map.SetTile(x, roadY, TileType.Road);
                }
            }
            // Подъезды от баз и к фланговым заводам.
            foreach (int roadX in new[] { 10, 30, Width - 31, Width - 11 })
            {
                for (int y = SouthRoadY; y <= NorthRoadY; y++)
                {
                    if (map.GetTile(roadX, y) == TileType.Ground) map.SetTile(roadX, y, TileType.Road);
                }
            }
        }

        /// <summary>Неровности у центра: гусеницы срезают по ним путь к мосту, шагающие идут по дорогам.</summary>
        private static void BuildRoughFields(MapDefinition map)
        {
            Fill(map, 50, 27, 59, 35, TileType.Rough);
            Fill(map, 68, 27, 77, 35, TileType.Rough);
            Fill(map, 50, 45, 59, 53, TileType.Rough);
            Fill(map, 68, 45, 77, 53, TileType.Rough);
        }

        /// <summary>Трещины-овраги на флангах: наземные роботы обходят их, антиграв летит напрямую.</summary>
        private static void BuildCracks(MapDefinition map)
        {
            foreach (bool west in new[] { true, false })
            {
                int fromX = west ? 14 : Width - 45;
                int toX = west ? 44 : Width - 15;
                Fill(map, fromX, 14, toX, 14, TileType.Crack);
                Fill(map, fromX, 66, toX, 66, TileType.Crack);
                int nearX = west ? 16 : Width - 23;
                Fill(map, nearX, 30, nearX + 6, 31, TileType.Crack);
                Fill(map, nearX, 49, nearX + 6, 50, TileType.Crack);
            }
            // Ядерный брод прикрыт оврагом с севера: наземным — только с юга.
            Fill(map, RiverX - 6, 76, RiverX + 7, 76, TileType.Crack);
        }

        private static void BuildLakes(MapDefinition map)
        {
            Fill(map, 16, 70, 24, 76, TileType.Water);
            Fill(map, Width - 25, 3, Width - 17, 9, TileType.Water);
        }

        private static void AddBuildings(MapDefinition map)
        {
            map.Buildings.Add(new BuildingSpawnDefinition
            {
                EntityIdValue = 1, Type = BuildingType.Base, InitialOwner = PlayerId.Player1,
                Position = new SimVector2(6.5f, CenterRoadY + 0.5f), SpawnPoint = new SimVector2(9.5f, CenterRoadY + 0.5f), CaptureRadius = 3.5f
            });
            map.Buildings.Add(new BuildingSpawnDefinition
            {
                EntityIdValue = 2, Type = BuildingType.Base, InitialOwner = PlayerId.Player2,
                Position = new SimVector2(Width - 6.5f, CenterRoadY + 0.5f), SpawnPoint = new SimVector2(Width - 9.5f, CenterRoadY + 0.5f), CaptureRadius = 3.5f
            });

            AddFactory(map, 101, new SimVector2(RiverX + 1f, 8f), ResourceType.Electronics);
            AddFactory(map, 102, new SimVector2(RiverX + 1f, 71f), ResourceType.Nuclear);
            AddFactory(map, 103, new SimVector2(26.5f, NorthRoadY + 4.5f), ResourceType.Phasers);
            AddFactory(map, 104, new SimVector2(Width - 26.5f, NorthRoadY + 4.5f), ResourceType.Missiles);
            AddFactory(map, 105, new SimVector2(26.5f, SouthRoadY - 4.5f), ResourceType.Cannon);
            AddFactory(map, 106, new SimVector2(Width - 26.5f, SouthRoadY - 4.5f), ResourceType.Chassis);
        }

        private static void AddFactory(MapDefinition map, int id, SimVector2 position, ResourceType resource)
        {
            map.Buildings.Add(new BuildingSpawnDefinition
            {
                EntityIdValue = id, Type = BuildingType.Factory, InitialOwner = PlayerId.Neutral,
                Position = position, SpawnPoint = position, CaptureRadius = 2.5f, ProducedResource = resource
            });
        }

        private static void Fill(MapDefinition map, int fromX, int fromY, int toX, int toY, TileType type)
        {
            for (int y = fromY; y <= toY; y++)
            {
                for (int x = fromX; x <= toX; x++) map.SetTile(x, y, type);
            }
        }
    }
}
