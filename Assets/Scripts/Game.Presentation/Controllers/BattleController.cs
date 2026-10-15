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
using Game.Simulation.Systems;

namespace Game.Presentation.Controllers
{
    /// <summary>
    /// Связывает сцену с <see cref="MatchSimulation"/>: переводит ввод в команды, крутит фиксированные тики
    /// и синхронизирует визуал. Игровая логика (движение, манёвр, приказы) живёт только в симуляции.
    /// </summary>
    public class BattleController : MonoBehaviour
    {
        [Header("Components")]
        [SerializeField] private MapRenderer _mapRenderer;
        [SerializeField] private RtsCameraController _cameraController;
        [SerializeField] private SelectionController _selectionController;

        private MapDefinition _mapDefinition;
        private MatchSimulation _simulation;
        private MatchPersistenceService _persistence;
        private Dictionary<string, ModuleDefinition> _moduleCatalog;

        private readonly Dictionary<int, RobotView> _spawnedRobots = new Dictionary<int, RobotView>();
        private BuildingView _currentlySelectedBuilding;
        private RobotView _currentlySelectedRobot;
        private BattleHudView _hudView;
        private WeaponEffectsPool _weaponEffects;
        private CombatFeedbackView _combatFeedback;
        private bool _isOrderMode;
        private bool _matchResultReported;
        private RobotState _drivenRobot;
        private SimVector2 _lastDriveDirection;

        private MatchState MatchState => _simulation?.State;

        private void Start()
        {
            InitializeMatch();
        }

        private void InitializeMatch()
        {
            // 1. Загрузка каталога модулей, карты и симуляции матча
            _moduleCatalog = DefaultContentCatalog.CreateDefaultModules();
            _mapDefinition = DefaultMapFactory.CreatePrototypeMap();
            _simulation = new MatchSimulation(_moduleCatalog, _mapDefinition);
            _simulation.AddAgent(new AiCommander(_simulation.Dispatcher, _moduleCatalog));
            _simulation.Production.OnRobotSpawned += HandleRobotSpawned;
            _simulation.Combat.OnRobotDestroyed += HandleRobotDestroyed;
            _simulation.Combat.OnWeaponFired += HandleWeaponFired;
            _simulation.Combat.OnProjectileImpact += HandleProjectileImpact;
            _simulation.Combat.OnDamageDealt += HandleDamageDealt;
            _simulation.Combat.OnNuclearDetonated += (carrier, center, radius) => _weaponEffects.PlayNuclearBlast(center, radius);
            _simulation.Capture.OnBuildingCaptured += HandleBuildingCaptured;
            _simulation.StepCompleted += HandleSimulationStep;
            _persistence = new MatchPersistenceService();
            _weaponEffects = gameObject.AddComponent<WeaponEffectsPool>();
            _weaponEffects.Initialize();

            // 2. Построение визуала карты
            if (_mapRenderer == null)
            {
                var mrGo = new GameObject("MapRenderer");
                _mapRenderer = mrGo.AddComponent<MapRenderer>();
            }
            _mapRenderer.BuildMap(_mapDefinition);

            // 3. Настройка камеры
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
                foreach (BuildingSpawnDefinition building in _mapDefinition.Buildings)
                {
                    if (building.Type == BuildingType.Base && building.InitialOwner == PlayerId.Player1) _cameraController.FocusOn(building.Position.X + 8f, building.Position.Y);
                }
            }

            // 4. Настройка контроллера ввода
            if (_selectionController == null)
            {
                _selectionController = gameObject.AddComponent<SelectionController>();
            }

            _selectionController.OnBuildingSelected += HandleBuildingSelected;
            _selectionController.OnRobotSelected += HandleRobotSelected;
            _selectionController.OnAttackOrderRequested += HandleAttackOrder;
            _selectionController.OnCaptureOrderRequested += HandleCaptureOrder;

            // 5. Инициализация нативного Canvas HUD
            var hudGo = new GameObject("BattleHud");
            _hudView = hudGo.AddComponent<BattleHudView>();
            _hudView.Initialize(_moduleCatalog, _mapDefinition);
            _hudView.SetOrderMode(false);
            _hudView.OnOrderBlueprintRequested += HandleOrderBlueprintFromHud;
            _hudView.OnRobotOrderRequested += HandleRobotOrderFromHud;
            _hudView.OnRestartRequested += RestartMatch;
            _hudView.OnSelectPlayerBaseRequested += HandleSelectPlayerBaseFromHud;
            _hudView.OnMinimapPointRequested += point => _cameraController?.FocusOn(point.X, point.Y, instant: false);
            _combatFeedback = new GameObject("CombatFeedback").AddComponent<CombatFeedbackView>();

            // На старте ничего не выбрано — экран чистый
            _currentlySelectedBuilding = null;
            _currentlySelectedRobot = null;

            Debug.Log($"[Battle] Initialized map '{_mapDefinition.Name}' with {_mapDefinition.Buildings.Count} buildings.");
        }

        private void Update()
        {
            if (_simulation == null) return;

            // Ввод превращается в команды до тиков этого кадра.
            UpdateControlMode();
            UpdateDirectControl();

            _simulation.AdvanceFrame(Time.deltaTime);

            float alpha = _simulation.InterpolationAlpha;
            foreach (var view in _spawnedRobots.Values)
            {
                view?.ApplyInterpolation(alpha);
            }

            UpdateCombatAndCaptureVisuals();
            if (!_matchResultReported && MatchState.Result != MatchResult.InProgress)
            {
                _matchResultReported = true;
                Debug.Log(MatchState.Result == MatchResult.Player1Won ? "[Battle] Победа: вражеская база захвачена." : "[Battle] Поражение: ваша база захвачена.");
                _hudView.ShowMatchResult(MatchState.Result);
            }

            // Обновление нативного Canvas интерфейса
            if (_hudView != null)
            {
                BuildingState activeBuildingState = null;
                if (_currentlySelectedBuilding != null && MatchState.Buildings.TryGetValue(_currentlySelectedBuilding.EntityId, out var bState))
                {
                    activeBuildingState = bState;
                }

                RobotState activeRobotState = _currentlySelectedRobot != null ? _currentlySelectedRobot.State : null;
                _hudView.UpdateState(MatchState, activeBuildingState, activeRobotState);
                _hudView.UpdateMinimap(MatchState);
            }
        }

        private void HandleSimulationStep()
        {
            foreach (var view in _spawnedRobots.Values)
            {
                view?.CaptureSimulationStep();
            }
        }

        private CommandResult IssueForRobot(ICommand command, string failurePrefix)
        {
            CommandResult result = _simulation.Issue(command);
            if (!result.IsSuccess) Debug.LogWarning($"[Battle] {failurePrefix}: {result.ErrorReason}");
            return result;
        }

        private void HandleOrderBlueprintFromHud(RobotBlueprint blueprint)
        {
            BuildingView p1Base = GetPlayer1BaseView();
            if (p1Base == null) return;
            IssueForRobot(new EnqueueProductionCommand
            {
                PlayerId = PlayerId.Player1,
                BaseBuildingId = new EntityId(p1Base.EntityId),
                Blueprint = blueprint
            }, "Ошибка заказа");
        }

        private void HandleRobotOrderFromHud(OrderType order)
        {
            if (_currentlySelectedRobot == null || _currentlySelectedRobot.Owner != PlayerId.Player1) return;
            IssueForRobot(CreateRobotOrderCommand(order, _currentlySelectedRobot.State), "Приказ отклонён");
        }

        private ICommand CreateRobotOrderCommand(OrderType order, RobotState robot)
        {
            var ids = new List<EntityId> { robot.Id };
            return order switch
            {
                OrderType.Capture => new OrderCaptureCommand { PlayerId = PlayerId.Player1, RobotIds = ids, TargetBuildingId = EntityId.None },
                OrderType.Hunt => new OrderHuntCommand { PlayerId = PlayerId.Player1, RobotIds = ids },
                OrderType.Scout => new OrderScoutCommand { PlayerId = PlayerId.Player1, RobotIds = ids, TargetPosition = OrderPlanner.GetScoutDestination(robot, _mapDefinition) },
                OrderType.Defend => new OrderDefendCommand { PlayerId = PlayerId.Player1, RobotIds = ids, TargetPosition = robot.Position },
                OrderType.Detonate => new OrderDetonateCommand { PlayerId = PlayerId.Player1, RobotIds = ids },
                _ => new OrderStopCommand { PlayerId = PlayerId.Player1, RobotIds = ids }
            };
        }

        private void RestartMatch()
        {
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (MatchState == null) return;
            MatchState.IsPaused = pauseStatus;
            if (pauseStatus)
            {
                _persistence.SaveAuto(MatchState);
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
            view.SetSurfaceHeightProvider(GetSurfaceHeight);
            _spawnedRobots[robotState.Id.Value] = view;
            Debug.Log($"[Battle] Робот #{robotState.Id.Value} ({robotState.Blueprint.Name}) вышел на поле боя в точке {robotState.Position}!");
        }

        private float GetSurfaceHeight(SimVector2 position)
        {
            GridCoord cell = GridCoord.FromSimVector2(position, _mapDefinition.TileSize);
            return MapRenderer.GetTileHeight(_mapDefinition.GetTile(cell.X, cell.Y));
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

        private void UpdateCombatAndCaptureVisuals()
        {
            foreach (var view in _spawnedRobots.Values)
            {
                view?.UpdateHealthVisual();
            }
            foreach (var view in _mapRenderer.SpawnedBuildings)
            {
                if (MatchState.Buildings.TryGetValue(view.EntityId, out var building))
                {
                    view.UpdateCaptureVisual(building);
                }
            }
        }

        private void HandleWeaponFired(RobotState attacker, RobotState target, WeaponType weaponType)
        {
            _weaponEffects.PlayLaunch(attacker.Position, target.Position, weaponType);
        }

        private void HandleProjectileImpact(RobotState attacker, SimVector2 impactPosition, WeaponType weaponType)
        {
            _weaponEffects.PlayImpact(impactPosition, weaponType);
        }

        private void HandleDamageDealt(RobotState target, int damage)
        {
            _combatFeedback?.ShowDamage(target.Position, damage);
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

        private void HandleAttackOrder(RobotView attacker, RobotView target)
        {
            if (attacker?.State == null || target?.State == null || attacker.Owner != PlayerId.Player1 || attacker.Owner == target.Owner)
            {
                return;
            }

            IssueForRobot(new OrderAttackCommand
            {
                PlayerId = attacker.Owner,
                RobotIds = new List<EntityId> { attacker.State.Id },
                TargetEntityId = target.State.Id
            }, "Не удалось отдать приказ атаки");
        }

        private void HandleCaptureOrder(RobotView robot, BuildingView target)
        {
            if (robot?.State == null || target == null || robot.Owner != PlayerId.Player1)
            {
                return;
            }

            CommandResult result = IssueForRobot(new OrderCaptureCommand
            {
                PlayerId = robot.Owner,
                RobotIds = new List<EntityId> { robot.State.Id },
                TargetBuildingId = new EntityId(target.EntityId)
            }, "Приказ захвата отклонён");
            if (result.IsSuccess)
            {
                Debug.Log($"[Battle] Робот #{robot.EntityId} получил приказ захватить здание #{target.EntityId}.");
            }
        }

        /// <summary>Ручное управление отправляет команду только при смене направления, поэтому попадает в replay.</summary>
        private void UpdateDirectControl()
        {
            RobotState robot = !_isOrderMode && _currentlySelectedRobot != null && _currentlySelectedRobot.Owner == PlayerId.Player1
                ? _currentlySelectedRobot.State
                : null;

            SimVector2 direction = SimVector2.Zero;
            if (robot != null && Keyboard.current != null)
            {
                float x = 0f, y = 0f;
                if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) y += 1f;
                if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) y -= 1f;
                if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) x -= 1f;
                if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) x += 1f;
                direction = new SimVector2(x, y);
            }

            // Отпускаем робота, которым управляли раньше, если выбор или режим сменился.
            if (_drivenRobot != null && _drivenRobot != robot)
            {
                StopDriving();
            }
            if (robot == null || direction.Equals(_lastDriveDirection)) return;

            // Пока клавиши не нажимали, робот выполняет свой приказ.
            if (_drivenRobot == null && direction.SqrMagnitude < 0.001f) return;
            if (!robot.IsAlive) return;

            _simulation.Issue(new OrderDriveCommand
            {
                PlayerId = PlayerId.Player1,
                RobotIds = new List<EntityId> { robot.Id },
                Direction = direction
            });
            _drivenRobot = robot;
            _lastDriveDirection = direction;
        }

        private void StopDriving()
        {
            if (_drivenRobot != null && _drivenRobot.IsAlive && _drivenRobot.DriveDirection.SqrMagnitude > 0.001f)
            {
                _simulation.Issue(new OrderDriveCommand
                {
                    PlayerId = PlayerId.Player1,
                    RobotIds = new List<EntityId> { _drivenRobot.Id },
                    Direction = SimVector2.Zero
                });
            }
            _drivenRobot = null;
            _lastDriveDirection = SimVector2.Zero;
        }

        private void UpdateControlMode()
        {
            if (Keyboard.current == null)
            {
                return;
            }

            if (_currentlySelectedRobot != null && _currentlySelectedRobot.Owner == PlayerId.Player1)
            {
                OrderType? hotkeyOrder =
                    Keyboard.current.cKey.wasPressedThisFrame ? OrderType.Capture :
                    Keyboard.current.hKey.wasPressedThisFrame ? OrderType.Hunt :
                    Keyboard.current.rKey.wasPressedThisFrame ? OrderType.Scout :
                    Keyboard.current.fKey.wasPressedThisFrame ? OrderType.Defend :
                    Keyboard.current.nKey.wasPressedThisFrame && _currentlySelectedRobot.State.Blueprint.HasNuclear ? OrderType.Detonate :
                    _isOrderMode && Keyboard.current.sKey.wasPressedThisFrame ? OrderType.Stop :
                    (OrderType?)null;

                if (hotkeyOrder.HasValue)
                {
                    if (hotkeyOrder.Value != OrderType.Stop) SetOrderMode(true);
                    // Новый приказ сам сбрасывает ручной привод, поэтому повторная команда остановки не нужна.
                    _drivenRobot = null;
                    _lastDriveDirection = SimVector2.Zero;
                    IssueForRobot(CreateRobotOrderCommand(hotkeyOrder.Value, _currentlySelectedRobot.State), "Приказ отклонён");
                }
            }

            if (!Keyboard.current.tabKey.wasPressedThisFrame)
            {
                return;
            }

            SetOrderMode(!_isOrderMode);
            Debug.Log($"[Battle] Режим управления: {(_isOrderMode ? "приказы" : "ручной")}.");
        }

        private void SetOrderMode(bool isOrderMode)
        {
            _isOrderMode = isOrderMode;
            _selectionController.SetOrderMode(isOrderMode);
            _hudView.SetOrderMode(isOrderMode);
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
    }
}
