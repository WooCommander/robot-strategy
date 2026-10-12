using System.Collections.Generic;
using UnityEngine;
using Game.Content.Definitions;
using Game.Core.Commands;
using Game.Core.Models;
using Game.Core.Types;
using EntityId = Game.Core.Types.EntityId;
using Game.Presentation.Camera;
using Game.Presentation.Views;
using Game.Simulation.Navigation;
using Game.Simulation.Systems;

namespace Game.Presentation.Controllers
{
    public class BattleController : MonoBehaviour
    {
        [Header("Components")]
        [SerializeField] private MapRenderer _mapRenderer;
        [SerializeField] private RtsCameraController _cameraController;
        [SerializeField] private SelectionController _selectionController;

        private MapDefinition _mapDefinition;
        private NavigationGrid _navigationGrid;
        private AStarPathfinder _pathfinder;
        private MatchState _matchState;
        private CommandDispatcher _commandDispatcher;
        private Dictionary<string, ModuleDefinition> _moduleCatalog;

        private BuildingView _currentlySelectedBuilding;

        private void Start()
        {
            InitializeMatch();
        }

        private void InitializeMatch()
        {
            // 1. Загрузка каталога модулей и карты
            _moduleCatalog = DefaultContentCatalog.CreateDefaultModules();
            _mapDefinition = DefaultMapFactory.CreatePrototypeMap();
            _navigationGrid = new NavigationGrid(_mapDefinition);
            _pathfinder = new AStarPathfinder(_navigationGrid);

            // 2. Инициализация состояния матча
            _matchState = new MatchState();
            _matchState.Player1.Metal = 600;
            _matchState.Player2.Metal = 600;

            foreach (var bDef in _mapDefinition.Buildings)
            {
                var building = new BuildingState
                {
                    Id = new EntityId(bDef.EntityIdValue),
                    Type = bDef.Type,
                    Owner = bDef.InitialOwner,
                    Position = bDef.Position,
                    SpawnPoint = bDef.SpawnPoint,
                    CaptureRadius = bDef.CaptureRadius
                };
                _matchState.Buildings[bDef.EntityIdValue] = building;
            }

            _commandDispatcher = new CommandDispatcher(_moduleCatalog);

            // 3. Построение визуала
            if (_mapRenderer == null)
            {
                var mrGo = new GameObject("MapRenderer");
                _mapRenderer = mrGo.AddComponent<MapRenderer>();
            }
            _mapRenderer.BuildMap(_mapDefinition);

            // 4. Настройка камеры
            if (_cameraController == null)
            {
                _cameraController = FindAnyObjectByType<RtsCameraController>();
                if (_cameraController == null && UnityEngine.Camera.main != null)
                {
                    _cameraController = UnityEngine.Camera.main.gameObject.AddComponent<RtsCameraController>();
                }
            }

            if (_cameraController != null)
            {
                _cameraController.SetBounds(_mapDefinition.Width, _mapDefinition.Height);
            }

            // 5. Настройка выбора
            if (_selectionController == null)
            {
                _selectionController = gameObject.AddComponent<SelectionController>();
            }

            _selectionController.OnBuildingSelected += HandleBuildingSelected;
            _selectionController.OnGroundClicked += HandleGroundClicked;

            Debug.Log($"[Battle] Initialized map '{_mapDefinition.Name}' with {_mapDefinition.Buildings.Count} buildings.");
        }

        private void HandleBuildingSelected(BuildingView view)
        {
            _currentlySelectedBuilding = view;
        }

        private void HandleGroundClicked(SimVector2 pos)
        {
            Debug.Log($"[Battle] Ground clicked at {pos}");
        }

        private void OnGUI()
        {
            if (_matchState == null) return;

            // Стильный HUD с ресурсами и информацией
            var p1 = _matchState.Player1;

            GUI.Box(new Rect(10, 10, 260, 90), "СТАТУС ИГРОКА (Player 1)");
            GUI.Label(new Rect(20, 35, 240, 25), $"Металл: <b>{p1.Metal}</b>");
            GUI.Label(new Rect(20, 60, 240, 25), $"Армия: <b>{p1.ActiveRobotsCount + p1.QueuedRobotsCount}/{PlayerState.MaxArmyLimit}</b>");

            // Инфо о выбранном здании
            if (_currentlySelectedBuilding != null)
            {
                int bId = _currentlySelectedBuilding.EntityId;
                if (_matchState.Buildings.TryGetValue(bId, out var building))
                {
                    GUI.Box(new Rect(10, 110, 300, 140), $"ВЫБРАН: {building.Type} #{building.Id.Value}");
                    GUI.Label(new Rect(20, 135, 280, 20), $"Владелец: {building.Owner}");
                    GUI.Label(new Rect(20, 155, 280, 20), $"Доход: +{building.MetalIncomePerSecond} / сек");

                    if (building.Type == BuildingType.Base && building.Owner == PlayerId.Player1)
                    {
                        GUI.Label(new Rect(20, 175, 280, 20), $"Очередь: {building.ProductionQueue.Count}/{BuildingState.MaxQueueSize}");

                        if (GUI.Button(new Rect(20, 205, 260, 30), "Заказать робота (Разведчик, 350 М)"))
                        {
                            var bp = new RobotBlueprint(
                                "bp_scout",
                                "Разведчик",
                                DefaultContentCatalog.ChassisBiped,
                                DefaultContentCatalog.BodyLight,
                                DefaultContentCatalog.WeaponCannon
                            );

                            var cmd = new EnqueueProductionCommand
                            {
                                PlayerId = PlayerId.Player1,
                                BaseBuildingId = building.Id,
                                Blueprint = bp
                            };

                            var res = _commandDispatcher.Dispatch(cmd, _matchState);
                            if (res.IsSuccess)
                            {
                                Debug.Log($"[Battle] Робот успешно заказан в очередь базы #{building.Id.Value}!");
                            }
                            else
                            {
                                Debug.LogWarning($"[Battle] Ошибка заказа: {res.ErrorReason}");
                            }
                        }
                    }
                }
            }
        }
    }
}
