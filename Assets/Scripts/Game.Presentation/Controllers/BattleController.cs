using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
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
        private ProductionSystem _productionSystem;
        private CombatSystem _combatSystem;
        private Dictionary<string, ModuleDefinition> _moduleCatalog;

        private readonly Dictionary<int, RobotView> _spawnedRobots = new Dictionary<int, RobotView>();
        private BuildingView _currentlySelectedBuilding;
        private RobotView _currentlySelectedRobot;
        private BattleHudView _hudView;
        private bool _isOrderMode;

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
            _productionSystem = new ProductionSystem(_moduleCatalog);
            _productionSystem.OnRobotSpawned += HandleRobotSpawned;
            _combatSystem = new CombatSystem(_moduleCatalog);
            _combatSystem.OnRobotDestroyed += HandleRobotDestroyed;
            _combatSystem.OnWeaponFired += HandleWeaponFired;

            // 3. Построение визуала карты
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

            // 5. Настройка контроллера ввода
            if (_selectionController == null)
            {
                _selectionController = gameObject.AddComponent<SelectionController>();
            }

            _selectionController.OnBuildingSelected += HandleBuildingSelected;
            _selectionController.OnRobotSelected += HandleRobotSelected;
            _selectionController.OnMoveOrderRequested += HandleMoveOrder;
            _selectionController.OnAttackOrderRequested += HandleAttackOrder;

            // 6. Инициализация нативного Canvas HUD
            var hudGo = new GameObject("BattleHud");
            _hudView = hudGo.AddComponent<BattleHudView>();
            _hudView.Initialize();
            _hudView.SetOrderMode(false);
            _hudView.OnOrderPresetRequested += HandleOrderPresetFromHud;
            _hudView.OnSelectPlayerBaseRequested += HandleSelectPlayerBaseFromHud;

            // Прямая подписка на клики коллайдеров

            // На старте ничего не выбрано — экран чистый
            _currentlySelectedBuilding = null;
            _currentlySelectedRobot = null;

            Debug.Log($"[Battle] Initialized map '{_mapDefinition.Name}' with {_mapDefinition.Buildings.Count} buildings.");
        }

        private void Update()
        {
            if (_matchState == null) return;

            // Тиковый расчет симуляции производства и экономики
            _productionSystem.Update(_matchState, Time.deltaTime);
            _combatSystem.Update(_matchState, Time.deltaTime);
            UpdateControlMode();
            UpdateDirectControl();

            // Обновление нативного Canvas интерфейса
            if (_hudView != null)
            {
                BuildingState activeBuildingState = null;
                if (_currentlySelectedBuilding != null && _matchState.Buildings.TryGetValue(_currentlySelectedBuilding.EntityId, out var bState))
                {
                    activeBuildingState = bState;
                }

                RobotState activeRobotState = _currentlySelectedRobot != null ? _currentlySelectedRobot.State : null;
                _hudView.UpdateState(_matchState.Player1, activeBuildingState, activeRobotState);
            }
        }

        private void HandleOrderPresetFromHud(string name, string chassis, string body, string weapon, string equip)
        {
            BuildingView p1Base = GetPlayer1BaseView();
            if (p1Base == null) return;
            OrderPresetRobot(new EntityId(p1Base.EntityId), name, chassis, body, weapon, equip);
        }

        private void HandleSelectPlayerBaseFromHud()
        {
            BuildingView p1Base = GetPlayer1BaseView();
            if (p1Base != null)
            {
                HandleBuildingSelected(p1Base);
                Debug.Log($"[Battle] Главная база #{p1Base.EntityId} выбрана через кнопку интерфейса!");
            }
        }

        private void HandleRobotSpawned(RobotState robotState)
        {
            var go = new GameObject($"Robot_Unit_{robotState.Id.Value}");
            var view = go.AddComponent<RobotView>();
            view.Initialize(robotState);

            _spawnedRobots[robotState.Id.Value] = view;
            Debug.Log($"[Battle] Робот #{robotState.Id.Value} ({robotState.Blueprint.Name}) вышел на поле боя в точке {robotState.Position}!");
        }

        private void HandleRobotDestroyed(RobotState attacker, RobotState target)
        {
            if (_spawnedRobots.Remove(target.Id.Value, out var view) && view != null)
            {
                Destroy(view.gameObject);
            }

            if (_currentlySelectedRobot != null && _currentlySelectedRobot.EntityId == target.Id.Value)
            {
                HandleRobotSelected(null);
            }
        }

        private void HandleWeaponFired(RobotState attacker, RobotState target, WeaponType weaponType)
        {
            Vector3 start = new Vector3(attacker.Position.X, 1.1f, attacker.Position.Y);
            Vector3 end = new Vector3(target.Position.X, 1.1f, target.Position.Y);
            Color color = weaponType == WeaponType.AreaMissile ? new Color(1f, 0.3f, 0.1f) : new Color(1f, 0.85f, 0.2f);

            var shot = new GameObject("WeaponTrace");
            var line = shot.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.SetPosition(0, start);
            line.SetPosition(1, end);
            line.startWidth = weaponType == WeaponType.AreaMissile ? 0.16f : 0.08f;
            line.endWidth = line.startWidth;
            line.startColor = color;
            line.endColor = color;
            line.material = new Material(Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color"));
            Destroy(shot, 0.12f);

            var impact = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            impact.name = "WeaponImpact";
            impact.transform.position = end;
            impact.transform.localScale = Vector3.one * (weaponType == WeaponType.AreaMissile ? 0.8f : 0.35f);
            var collider = impact.GetComponent<Collider>();
            if (collider != null) collider.enabled = false;
            var renderer = impact.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color"));
            renderer.sharedMaterial.color = color;
            Destroy(impact, 0.12f);
        }

        private void HandleBuildingSelected(BuildingView view)
        {
            if (_currentlySelectedBuilding != null && _currentlySelectedBuilding != view)
            {
                _currentlySelectedBuilding.SetSelected(false);
            }
            if (_currentlySelectedRobot != null)
            {
                _currentlySelectedRobot.SetSelected(false);
                _currentlySelectedRobot = null;
            }

            _currentlySelectedBuilding = view;
            if (_currentlySelectedBuilding != null)
            {
                _currentlySelectedBuilding.SetSelected(true);
            }

            if (_selectionController != null)
            {
                _selectionController.SyncSelectedBuilding(view);
                _selectionController.SyncSelectedRobot(null);
            }
        }

        private void HandleRobotSelected(RobotView view)
        {
            if (_currentlySelectedBuilding != null)
            {
                _currentlySelectedBuilding.SetSelected(false);
                _currentlySelectedBuilding = null;
            }
            if (_currentlySelectedRobot != null && _currentlySelectedRobot != view)
            {
                _currentlySelectedRobot.SetSelected(false);
            }

            _currentlySelectedRobot = view;
            if (_currentlySelectedRobot != null)
            {
                _currentlySelectedRobot.SetSelected(true);
            }

            if (_selectionController != null)
            {
                _selectionController.SyncSelectedRobot(view);
                _selectionController.SyncSelectedBuilding(null);
            }
        }

        private void HandleMoveOrder(RobotView robot, SimVector2 targetWorld)
        {
            if (robot == null || robot.State == null) return;

            // Проверяем возможность пути через A*
            if (_pathfinder.TryFindPath(robot.State.Position, targetWorld, out var waypoints))
            {
                // Отдаем команду в диспетчер
                var cmd = new OrderMoveCommand
                {
                    PlayerId = robot.Owner,
                    RobotIds = new List<EntityId> { robot.State.Id },
                    TargetPosition = targetWorld
                };

                var res = _commandDispatcher.Dispatch(cmd, _matchState);
                if (res.IsSuccess)
                {
                    robot.SetMovementPath(waypoints);
                    Debug.Log($"[Battle] Робот #{robot.EntityId} двигается к {targetWorld} ({waypoints.Count} точек пути).");
                }
            }
            else
            {
                Debug.LogWarning($"[Battle] Нет пути к точке {targetWorld}!");
            }
        }

        private void HandleAttackOrder(RobotView attacker, RobotView target)
        {
            if (attacker?.State == null || target?.State == null || attacker.Owner != PlayerId.Player1 || attacker.Owner == target.Owner)
            {
                return;
            }

            var command = new OrderAttackCommand
            {
                PlayerId = attacker.Owner,
                RobotIds = new List<EntityId> { attacker.State.Id },
                TargetEntityId = target.State.Id
            };

            var result = _commandDispatcher.Dispatch(command, _matchState);
            if (!result.IsSuccess)
            {
                Debug.LogWarning($"[Battle] РќРµ СѓРґР°Р»РѕСЃСЊ РѕС‚РґР°С‚СЊ РїСЂРёРєР°Р· Р°С‚Р°РєРё: {result.ErrorReason}");
            }
        }

        private void UpdateDirectControl()
        {
            if (_isOrderMode || _currentlySelectedRobot == null || _currentlySelectedRobot.Owner != PlayerId.Player1 || Keyboard.current == null)
            {
                return;
            }

            Vector2 direction = Vector2.zero;
            if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) direction.y += 1f;
            if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) direction.y -= 1f;
            if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) direction.x -= 1f;
            if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) direction.x += 1f;

            _currentlySelectedRobot.MoveDirect(direction);
        }

        private void UpdateControlMode()
        {
            if (Keyboard.current == null || !Keyboard.current.tabKey.wasPressedThisFrame)
            {
                return;
            }

            _isOrderMode = !_isOrderMode;
            _selectionController.SetOrderMode(_isOrderMode);
            _hudView.SetOrderMode(_isOrderMode);
            Debug.Log($"[Battle] Р РµР¶РёРј СѓРїСЂР°РІР»РµРЅРёСЏ: {(_isOrderMode ? "РїСЂРёРєР°Р·С‹" : "СЂСѓС‡РЅРѕР№")}.");
        }



        public BuildingView GetPlayer1BaseView()
        {
            if (_mapRenderer == null) return null;
            foreach (var b in _mapRenderer.SpawnedBuildings)
            {
                if (b != null && b.Owner == PlayerId.Player1 && b.Type == BuildingType.Base)
                {
                    return b;
                }
            }
            return null;
        }

        private void OrderPresetRobot(EntityId baseId, string name, string chassis, string body, string weapon, string equip)
        {
            var bp = new RobotBlueprint(
                $"bp_{Guid.NewGuid():N}",
                name,
                chassis,
                body,
                weapon,
                equip
            );

            var cmd = new EnqueueProductionCommand
            {
                PlayerId = PlayerId.Player1,
                BaseBuildingId = baseId,
                Blueprint = bp
            };

            var res = _commandDispatcher.Dispatch(cmd, _matchState);
            if (res.IsSuccess)
            {
                Debug.Log($"[Battle] Заказ на робота '{name}' поставлен в очередь производства!");
            }
            else
            {
                Debug.LogWarning($"[Battle] Ошибка заказа: {res.ErrorReason}");
            }
        }
    }
}
