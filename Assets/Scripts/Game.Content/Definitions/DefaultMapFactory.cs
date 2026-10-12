using System.Collections.Generic;
using Game.Core.Models;
using Game.Core.Types;

namespace Game.Content.Definitions
{
    public static class DefaultMapFactory
    {
        public const string PrototypeMapId = "map_prototype_valley";

        public static MapDefinition CreatePrototypeMap()
        {
            const int width = 32;
            const int height = 20;

            var map = new MapDefinition
            {
                Id = PrototypeMapId,
                Name = "Зелёная долина (Прототип)",
                Width = width,
                Height = height,
                TileSize = 1.0f,
                Tiles = new TileType[width * height]
            };

            // Заполнение базовой землей
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    map.SetTile(x, y, TileType.Ground);
                }
            }

            // Границы карты (скалы по краям)
            for (int x = 0; x < width; x++)
            {
                map.SetTile(x, 0, TileType.Mountain);
                map.SetTile(x, height - 1, TileType.Mountain);
            }
            for (int y = 0; y < height; y++)
            {
                map.SetTile(0, y, TileType.Mountain);
                map.SetTile(width - 1, y, TileType.Mountain);
            }

            // Центральный хребет гор с узким проходом (Chokepoint)
            int midX = width / 2; // 16
            for (int y = 1; y < height - 1; y++)
            {
                // Оставляем проход по центру (y = 9..11), на севере (y = 15..17) и юге (y = 2..4)
                if (y >= 5 && y <= 8)
                {
                    map.SetTile(midX - 1, y, TileType.Mountain);
                    map.SetTile(midX, y, TileType.Mountain);
                }
                else if (y >= 12 && y <= 15)
                {
                    map.SetTile(midX - 1, y, TileType.Mountain);
                    map.SetTile(midX, y, TileType.Mountain);
                }
            }

            // Центральное озеро/вода с мостом в узком месте (y = 9..11)
            for (int y = 9; y <= 11; y++)
            {
                map.SetTile(midX - 1, y, TileType.Bridge);
                map.SetTile(midX, y, TileType.Bridge);
            }

            // Дороги: Северная магистраль (y = 16) и Южная магистраль (y = 3)
            for (int x = 3; x < width - 3; x++)
            {
                map.SetTile(x, 16, TileType.Road);
                map.SetTile(x, 3, TileType.Road);
            }
            // Подводящие дороги от баз
            for (int y = 3; y <= 16; y++)
            {
                map.SetTile(4, y, TileType.Road);
                map.SetTile(width - 5, y, TileType.Road);
            }

            // Здания:
            // 1. База Игрока 1 (Слева)
            map.Buildings.Add(new BuildingSpawnDefinition
            {
                EntityIdValue = 1,
                Type = BuildingType.Base,
                InitialOwner = PlayerId.Player1,
                Position = new SimVector2(4.5f, 10.0f),
                SpawnPoint = new SimVector2(6.5f, 10.0f),
                CaptureRadius = 3.5f
            });

            // 2. База Игрока 2 (Справа)
            map.Buildings.Add(new BuildingSpawnDefinition
            {
                EntityIdValue = 2,
                Type = BuildingType.Base,
                InitialOwner = PlayerId.Player2,
                Position = new SimVector2(width - 4.5f, 10.0f),
                SpawnPoint = new SimVector2(width - 6.5f, 10.0f),
                CaptureRadius = 3.5f
            });

            // 3. Северный нейтральный завод
            map.Buildings.Add(new BuildingSpawnDefinition
            {
                EntityIdValue = 101,
                Type = BuildingType.Factory,
                InitialOwner = PlayerId.Neutral,
                Position = new SimVector2(midX + 0.5f, 16.5f),
                SpawnPoint = new SimVector2(midX + 0.5f, 15.0f),
                CaptureRadius = 2.5f
            });

            // 4. Центральный нейтральный завод (у моста)
            map.Buildings.Add(new BuildingSpawnDefinition
            {
                EntityIdValue = 102,
                Type = BuildingType.Factory,
                InitialOwner = PlayerId.Neutral,
                Position = new SimVector2(midX + 0.5f, 10.0f),
                SpawnPoint = new SimVector2(midX + 0.5f, 8.5f),
                CaptureRadius = 2.5f
            });

            // 5. Южный нейтральный завод
            map.Buildings.Add(new BuildingSpawnDefinition
            {
                EntityIdValue = 103,
                Type = BuildingType.Factory,
                InitialOwner = PlayerId.Neutral,
                Position = new SimVector2(midX + 0.5f, 3.5f),
                SpawnPoint = new SimVector2(midX + 0.5f, 5.0f),
                CaptureRadius = 2.5f
            });

            return map;
        }
    }
}
