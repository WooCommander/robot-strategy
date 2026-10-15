using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Game.Content.Definitions;
using Game.AI;
using Game.Core.Commands;
using Game.Core.Models;
using Game.Core.Types;
using EntityId = Game.Core.Types.EntityId;
using Game.Presentation.Camera;
using Game.Presentation.Views;
using Game.Persistence;
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
        private CaptureSystem _captureSystem;
        private AiCommander _aiCommander;
        private MatchPersistenceService _persistence;
        private Dictionary<string, ModuleDefinition> _moduleCatalog;

        private readonly Dictionary<int, RobotView> _spawnedRobots = new Dictionary<int, RobotView>();
        private BuildingView _currentlySelectedBuilding;
        private RobotView _currentlySelectedRobot;
        private BattleHudView _hudView;
        private bool _isOrderMode;
        private float _nextAutonomousOrderUpdate;
        private bool _matchResultReported;

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
            _captureSystem = new CaptureSystem();
            _captureSystem.OnBuildingCaptured += HandleBuildingCaptured;
            _aiCommander = new AiCommander(_commandDispatcher, _moduleCatalog);
            _persistence = new MatchPersistenceService();

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
            _selectionController.OnAttackOrderRequested += HandleAttackOrder;
            _selectionController.OnCaptureOrderRequested += HandleCaptureOrder;

            // 6. Инициализация нативного Canvas HUD
            var hudGo = new GameObject("BattleHud");
            _hudView = hudGo.AddComponent<BattleHudView>();
            _hudView.Initialize(_moduleCatalog, _mapDefinition);
            _hudView.SetOrderMode(false);
            _hudView.OnOrderPresetRequested += HandleOrderPresetFromHud;
            _hudView.OnRobotOrderRequested += HandleRobotOrderFromHud;
            _hudView.OnRestartRequested += RestartMatch;
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

            if (!_matchState.IsPaused)
            {
                _matchState.Tick++;
                _matchState.MatchTimeSeconds += Time.deltaTime;
            }
            // Тиковый расчет симуляции производства и экономики
            _productionSystem.Update(_matchState, Time.deltaTime);
            _aiCommander.Think(_matchState);
            UpdateAutonomousOrders();
            _combatSystem.Update(_matchState, Time.deltaTime);
            _captureSystem.Update(_matchState, Time.deltaTime);
            if (!_matchResultReported && _matchState.Result != MatchResult.InProgress)
            {
                _matchResultReported = true;
                Debug.Log(_matchState.Result == MatchResult.Player1Won ? "[Battle] Победа: вражеская база захвачена." : "[Battle] Поражение: ваша база захвачена.");
                _hudView.ShowMatchResult(_matchState.Result);
            }
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
                _hudView.UpdateMinimap(_matchState);
            }
        }

        private void HandleOrderPresetFromHud(RobotPresetDefinition preset)
        {
            BuildingView p1Base = GetPlayer1BaseView();
            if (p1Base == null) return;
            OrderPresetRobot(new EntityId(p1Base.EntityId), preset);
        }

        private void HandleRobotOrderFromHud(OrderType order)
        {
            if (_currentlySelectedRobot == null || _currentlySelectedRobot.Owner != PlayerId.Player1) return;
            var robot = _currentlySelectedRobot.State;
            if (order == OrderType.Stop) _currentlySelectedRobot.StopMovement();
            robot.CurrentOrder = order == OrderType.Capture ? RobotOrder.Capture(EntityId.None) :
                order == OrderType.Hunt ? RobotOrder.Hunt() :
                order == OrderType.Scout ? RobotOrder.Scout(GetScoutDestination(robot)) :
                order == OrderType.Defend ? RobotOrder.Defend(robot.Position) : RobotOrder.Stop();
        }

        private void RestartMatch()
        {
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (_matchState == null) return;
            _matchState.IsPaused = pauseStatus;
            if (pauseStatus)
            {
                _persistence.SaveAuto(_matchState);
                Debug.Log("[Battle] Матч поставлен на паузу и сохранён в автослот.");
            }
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
            view.SetTerrainSpeedProvider(GetTerrainSpeedMultiplier);

            _spawnedRobots[robotState.Id.Value] = view;
            if (robotState.Owner == PlayerId.Player2)
            {
                if (_matchState.Player2.ActiveRobotsCount > 1)
                {
                    _commandDispatcher.Dispatch(new OrderDefendCommand
                    {
                        PlayerId = PlayerId.Player2,
                        RobotIds = new List<EntityId> { robotState.Id },
                        TargetPosition = robotState.Position
                    }, _matchState);
                }
                else
                {
                    _commandDispatcher.Dispatch(new OrderCaptureCommand
                    {
                        PlayerId = PlayerId.Player2,
                        RobotIds = new List<EntityId> { robotState.Id },
                        TargetBuildingId = EntityId.None
                    }, _matchState);
                }
            }
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

            if (target.Owner == PlayerId.Player2)
            {
            }
        }

        private void HandleBuildingCaptured(BuildingState building, PlayerId previousOwner)
        {
            foreach (var view in _mapRenderer.SpawnedBuildings)
            {
                if (view.EntityId == building.Id.Value)
                {
                    view.UpdateOwnerVisuals(building.Owner);
                    break;
                }
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

        private void HandleCaptureOrder(RobotView robot, BuildingView target)
        {
            if (robot?.State == null || target == null || robot.Owner != PlayerId.Player1)
            {
                return;
            }

            var command = new OrderCaptureCommand
            {
                PlayerId = robot.Owner,
                RobotIds = new List<EntityId> { robot.State.Id },
                TargetBuildingId = new EntityId(target.EntityId)
            };
            var result = _commandDispatcher.Dispatch(command, _matchState);
            if (result.IsSuccess && _pathfinder.TryFindPath(robot.State.Position, _matchState.Buildings[target.EntityId].Position, out var waypoints))
            {
                robot.SetMovementPath(waypoints);
                Debug.Log($"[Battle] Робот #{robot.EntityId} получил приказ захватить здание #{target.EntityId}.");
            }
            else if (!result.IsSuccess)
            {
                Debug.LogWarning($"[Battle] Приказ захвата отклонён: {result.ErrorReason}");
            }
        }

        private void UpdateAutonomousOrders()
        {
            if (Time.time < _nextAutonomousOrderUpdate) return;
            _nextAutonomousOrderUpdate = Time.time + 0.4f;

            foreach (var pair in _spawnedRobots)
            {
                RobotView view = pair.Value;
                RobotState robot = view?.State;
                if (robot == null || !robot.IsAlive) continue;

                if (robot.CurrentOrder.Type == OrderType.Capture)
                {
                    BuildingState target = null;
                    if (robot.CurrentOrder.TargetEntityId.IsValid &&
                        _matchState.Buildings.TryGetValue(robot.CurrentOrder.TargetEntityId.Value, out var commandedTarget) &&
                        commandedTarget.Owner != robot.Owner)
                    {
                        target = commandedTarget;
                    }
                    else
                    {
                        target = FindNearestBuilding(robot, building => building.Owner != robot.Owner);
                    }
                    if (target == null) continue;
                    bool targetChanged = robot.CurrentOrder.TargetEntityId != target.Id;
                    robot.CurrentOrder.TargetEntityId = target.Id;
                    if (targetChanged || !view.IsMovingAlongPath) RouteRobotTo(view, target.Position);
                }
                else if (robot.CurrentOrder.Type == OrderType.Hunt)
                {
                    var target = FindNearestEnemy(robot);
                    if (target == null) continue;
                    bool targetChanged = robot.CurrentOrder.TargetEntityId != target.Id;
                    robot.CurrentOrder.TargetEntityId = target.Id;
                    if (targetChanged || !view.IsMovingAlongPath) RouteRobotTo(view, target.Position);
                }
                else if (robot.CurrentOrder.Type == OrderType.Defend)
                {
                    view.StopMovement();
                    var target = FindNearestEnemy(robot);
                    robot.CurrentOrder.TargetEntityId = target != null && SimVector2.SqrDistance(robot.Position, target.Position) <= 64f
                        ? target.Id : EntityId.None;
                }
                else if (robot.CurrentOrder.Type == OrderType.Scout)
                {
                    bool targetIsAtMapEdge = robot.CurrentOrder.TargetPosition.X <= 2f ||
                                             robot.CurrentOrder.TargetPosition.X >= _mapDefinition.Width - 3f;
                    bool reachedTarget = SimVector2.SqrDistance(robot.Position, robot.CurrentOrder.TargetPosition) < 0.8f * 0.8f;
                    if (targetIsAtMapEdge || reachedTarget)
                    {
                        if (reachedTarget) robot.ScoutPatrolStep++;
                        robot.CurrentOrder.TargetPosition = GetScoutDestination(robot, robot.ScoutPatrolStep);
                        view.StopMovement();
                    }

                    if (!view.IsMovingAlongPath) RouteRobotTo(view, robot.CurrentOrder.TargetPosition);
                }
            }
        }

        private void RouteRobotTo(RobotView view, SimVector2 target)
        {
            if (_pathfinder.TryFindPath(view.State.Position, target, out var waypoints))
            {
                view.SetMovementPath(waypoints);
            }
        }

        private float GetTerrainSpeedMultiplier(SimVector2 position)
        {
            GridCoord grid = _navigationGrid.WorldToGrid(position);
            float multiplier = _navigationGrid.GetSpeedMultiplier(grid.X, grid.Y);
            return multiplier > 0f ? multiplier : 1f;
        }

        private BuildingState FindNearestBuilding(RobotState robot, System.Func<BuildingState, bool> predicate)
        {
            BuildingState nearest = null;
            float nearestDistance = float.MaxValue;
            foreach (var building in _matchState.Buildings.Values)
            {
                if (!predicate(building)) continue;
                float distance = SimVector2.SqrDistance(robot.Position, building.Position);
                if (distance < nearestDistance)
                {
                    nearest = building;
                    nearestDistance = distance;
                }
            }
            return nearest;
        }

        private RobotState FindNearestEnemy(RobotState robot)
        {
            RobotState nearest = null;
            float nearestDistance = float.MaxValue;
            foreach (var candidate in _matchState.Robots.Values)
            {
                if (!candidate.IsAlive || candidate.Owner == robot.Owner) continue;
                float distance = SimVector2.SqrDistance(robot.Position, candidate.Position);
                if (distance < nearestDistance)
                {
                    nearest = candidate;
                    nearestDistance = distance;
                }
            }
            return nearest;
        }

        private SimVector2 GetScoutDestination(RobotState robot, int patrolStep = 0)
        {
            // Разведка ведёт в сектора у противника, но не к скалам карты и не под базу.
            float x = robot.Owner == PlayerId.Player1 ? _mapDefinition.Width - 9f : 8f;
            float y = 6f + ((robot.Id.Value + patrolStep) % 3) * 9f;
            return new SimVector2(x, y);
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
            if (Keyboard.current == null)
            {
                return;
            }

            if (_currentlySelectedRobot != null && _currentlySelectedRobot.Owner == PlayerId.Player1)
            {
                if (Keyboard.current.cKey.wasPressedThisFrame)
                {
                    _isOrderMode = true;
                    _selectionController.SetOrderMode(true);
                    _hudView.SetOrderMode(true);
                    _commandDispatcher.Dispatch(new OrderCaptureCommand
                    {
                        PlayerId = PlayerId.Player1,
                        RobotIds = new List<EntityId> { _currentlySelectedRobot.State.Id },
                        TargetBuildingId = EntityId.None
                    }, _matchState);
                }
                else if (Keyboard.current.hKey.wasPressedThisFrame)
                {
                    _isOrderMode = true;
                    _selectionController.SetOrderMode(true);
                    _hudView.SetOrderMode(true);
                    _commandDispatcher.Dispatch(new OrderHuntCommand
                    {
                        PlayerId = PlayerId.Player1,
                        RobotIds = new List<EntityId> { _currentlySelectedRobot.State.Id }
                    }, _matchState);
                }
                else if (Keyboard.current.rKey.wasPressedThisFrame)
                {
                    _isOrderMode = true;
                    _selectionController.SetOrderMode(true);
                    _hudView.SetOrderMode(true);
                    _commandDispatcher.Dispatch(new OrderScoutCommand
                    {
                        PlayerId = PlayerId.Player1,
                        RobotIds = new List<EntityId> { _currentlySelectedRobot.State.Id },
                        TargetPosition = GetScoutDestination(_currentlySelectedRobot.State)
                    }, _matchState);
                }
                else if (Keyboard.current.fKey.wasPressedThisFrame)
                {
                    _isOrderMode = true;
                    _selectionController.SetOrderMode(true);
                    _hudView.SetOrderMode(true);
                    _currentlySelectedRobot.StopMovement();
                    _commandDispatcher.Dispatch(new OrderDefendCommand
                    {
                        PlayerId = PlayerId.Player1,
                        RobotIds = new List<EntityId> { _currentlySelectedRobot.State.Id },
                        TargetPosition = _currentlySelectedRobot.State.Position
                    }, _matchState);
                }
                else if (_isOrderMode && Keyboard.current.sKey.wasPressedThisFrame)
                {
                    var result = _commandDispatcher.Dispatch(new OrderStopCommand
                    {
                        PlayerId = PlayerId.Player1,
                        RobotIds = new List<EntityId> { _currentlySelectedRobot.State.Id }
                    }, _matchState);
                    if (result.IsSuccess)
                    {
                        _currentlySelectedRobot.StopMovement();
                    }
                }
            }

            if (!Keyboard.current.tabKey.wasPressedThisFrame)
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

        private void OrderPresetRobot(EntityId baseId, RobotPresetDefinition preset)
        {
            var bp = preset.CreateBlueprint();

            var cmd = new EnqueueProductionCommand
            {
                PlayerId = PlayerId.Player1,
                BaseBuildingId = baseId,
                Blueprint = bp
            };

            var res = _commandDispatcher.Dispatch(cmd, _matchState);
            if (res.IsSuccess)
            {
                Debug.Log($"[Battle] Заказ на робота '{preset.Name}' поставлен в очередь производства!");
            }
            else
            {
                Debug.LogWarning($"[Battle] Ошибка заказа: {res.ErrorReason}");
            }
        }
    }
}
