using System;
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
        private ProductionSystem _productionSystem;
        private Dictionary<string, ModuleDefinition> _moduleCatalog;

        private readonly Dictionary<int, RobotView> _spawnedRobots = new Dictionary<int, RobotView>();
        private BuildingView _currentlySelectedBuilding;
        private RobotView _currentlySelectedRobot;

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

            // Прямая подписка на клики коллайдеров
            BuildingView.OnAnyBuildingClicked += HandleBuildingSelected;
            RobotView.OnAnyRobotClicked += HandleRobotSelected;

            // Автоматически выбираем синюю базу игрока на старте
            foreach (var b in _mapRenderer.SpawnedBuildings)
            {
                if (b.Owner == PlayerId.Player1 && b.Type == BuildingType.Base)
                {
                    HandleBuildingSelected(b);
                    break;
                }
            }

            Debug.Log($"[Battle] Initialized map '{_mapDefinition.Name}' with {_mapDefinition.Buildings.Count} buildings.");
        }

        private void OnDestroy()
        {
            BuildingView.OnAnyBuildingClicked -= HandleBuildingSelected;
            RobotView.OnAnyRobotClicked -= HandleRobotSelected;
        }

        private void Update()
        {
            if (_matchState == null) return;

            // Тиковый расчет симуляции производства и экономики
            _productionSystem.Update(_matchState, Time.deltaTime);
        }

        private void HandleRobotSpawned(RobotState robotState)
        {
            var go = new GameObject($"Robot_Unit_{robotState.Id.Value}");
            var view = go.AddComponent<RobotView>();
            view.Initialize(robotState);

            _spawnedRobots[robotState.Id.Value] = view;
            Debug.Log($"[Battle] Робот #{robotState.Id.Value} ({robotState.Blueprint.Name}) вышел на поле боя в точке {robotState.Position}!");
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

        private GUIStyle _panelBoxStyle;
        private GUIStyle _headerStyle;
        private GUIStyle _infoStyle;
        private GUIStyle _btnStyle;
        private GUIStyle _btnDisabledStyle;
        private GUIStyle _progressBarBg;
        private GUIStyle _progressBarFill;
        private bool _stylesInitialized;

        private void EnsureStyles()
        {
            if (_stylesInitialized && _panelBoxStyle != null) return;

            var bgTex = MakeTex(2, 2, new Color(0.05f, 0.08f, 0.15f, 0.95f));
            var btnNormal = MakeTex(2, 2, new Color(0.12f, 0.30f, 0.50f, 0.98f));
            var btnHover = MakeTex(2, 2, new Color(0.20f, 0.48f, 0.75f, 1.0f));
            var btnDisabled = MakeTex(2, 2, new Color(0.14f, 0.17f, 0.22f, 0.88f));
            var progBg = MakeTex(2, 2, new Color(0.08f, 0.12f, 0.18f, 1f));
            var progFill = MakeTex(2, 2, new Color(0.10f, 0.80f, 0.95f, 1f));

            _panelBoxStyle = new GUIStyle(GUI.skin.box)
            {
                normal = { background = bgTex, textColor = Color.white },
                fontSize = 15,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.UpperCenter
            };

            _headerStyle = new GUIStyle(GUI.skin.label)
            {
                normal = { textColor = new Color(0.35f, 0.85f, 1.0f) },
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.UpperLeft,
                richText = true
            };

            _infoStyle = new GUIStyle(GUI.skin.label)
            {
                normal = { textColor = Color.white },
                fontSize = 14,
                richText = true
            };

            _btnStyle = new GUIStyle(GUI.skin.button)
            {
                normal = { background = btnNormal, textColor = Color.white },
                hover = { background = btnHover, textColor = Color.white },
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                richText = true
            };

            _btnDisabledStyle = new GUIStyle(GUI.skin.button)
            {
                normal = { background = btnDisabled, textColor = new Color(0.6f, 0.6f, 0.6f) },
                fontSize = 12,
                alignment = TextAnchor.MiddleCenter,
                richText = true
            };

            _progressBarBg = new GUIStyle
            {
                normal = { background = progBg }
            };

            _progressBarFill = new GUIStyle
            {
                normal = { background = progFill }
            };

            _stylesInitialized = true;
        }

        private static Texture2D MakeTex(int width, int height, Color col)
        {
            Color[] pix = new Color[width * height];
            for (int i = 0; i < pix.Length; ++i) pix[i] = col;
            Texture2D result = new Texture2D(width, height);
            result.SetPixels(pix);
            result.Apply();
            return result;
        }

        private void OnGUI()
        {
            if (_matchState == null) return;
            EnsureStyles();

            var p1 = _matchState.Player1;

            // 1. Верхняя плашка ресурсов игрока (Top Bar)
            float topW = 340f;
            float topH = 92f;
            GUI.Box(new Rect(14, 14, topW, topH), "", _panelBoxStyle);
            GUI.Label(new Rect(26, 20, topW - 20, 24), "★ <b>РЕСУРСЫ КОМАНДЫ (СИНИЕ)</b> ★", _headerStyle);
            GUI.Label(new Rect(26, 44, topW - 20, 22), $"💰 Металл: <color=#FFD700><b>{p1.Metal}</b></color> <color=#88FF88>(+5/с База)</color>", _infoStyle);
            GUI.Label(new Rect(26, 66, topW - 20, 22), $"🤖 Армия: <b>{p1.TotalArmyCount} / {PlayerState.MaxArmyLimit}</b> (Активных: {p1.ActiveRobotsCount})", _infoStyle);

            // 2. Определение активного объекта для нижней панели
            BuildingView activeBuilding = _currentlySelectedBuilding;
            if (activeBuilding == null && _currentlySelectedRobot == null)
            {
                // По умолчанию всегда держим выбранной базу игрока №1, чтобы кнопки никогда не пропадали
                activeBuilding = GetPlayer1BaseView();
            }

            if (_currentlySelectedRobot != null)
            {
                DrawRobotPanel(_currentlySelectedRobot);
            }
            else if (activeBuilding != null)
            {
                DrawBuildingPanel(activeBuilding);
            }
        }

        private BuildingView GetPlayer1BaseView()
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

        private void DrawBuildingPanel(BuildingView view)
        {
            int bId = view.EntityId;
            if (!_matchState.Buildings.TryGetValue(bId, out var building)) return;

            float panelW = 590f;
            float panelH = 165f;
            float px = Mathf.Max(10f, (Screen.width - panelW) * 0.5f);
            float py = Screen.height - panelH - 12f;

            // Фоновая панель
            GUI.Box(new Rect(px, py, panelW, panelH), "", _panelBoxStyle);

            string title = building.Type == BuildingType.Base
                ? (building.Owner == PlayerId.Player1 ? "★ ГЛАВНАЯ БАЗА ИГРОКА 1 ★" : $"★ ВРАЖЕСКАЯ БАЗА #{building.Id.Value} ★")
                : $"★ НЕЙТРАЛЬНЫЙ ЗАВОД #{building.Id.Value} ★";

            GUI.Label(new Rect(px + 18, py + 12, 360, 24), title, _headerStyle);
            GUI.Label(new Rect(px + panelW - 210, py + 12, 195, 24), $"Доход: <color=#88FF88>+{building.MetalIncomePerSecond} М/сек</color>", _infoStyle);

            if (building.Type == BuildingType.Base && building.Owner == PlayerId.Player1)
            {
                var queue = building.ProductionQueue;
                string qText;
                float progress = 0f;

                if (queue.Count > 0)
                {
                    var currentOrder = queue[0];
                    if (currentOrder.Status == OrderProductionStatus.ExitBlocked)
                    {
                        qText = "<color=yellow>⚠️ ВЫХОД ЗАБЛОКИРОВАН (уведите робота с точки сбора)!</color>";
                    }
                    else
                    {
                        float remaining = currentOrder.RemainingBuildTimeSeconds;
                        // Расчет прогресса (предполагаем среднее время 10с для полосы)
                        progress = Mathf.Clamp01(1f - (remaining / 10f));
                        qText = $"Сборка: <b>{currentOrder.Blueprint.Name}</b> (осталось {remaining:F1}с)";
                    }
                }
                else
                {
                    qText = "Очередь: <b>0/3 (Свободно — выберите робота для заказа)</b>";
                }

                GUI.Label(new Rect(px + 18, py + 38, panelW - 36, 22), qText, _infoStyle);

                // Полоса прогресса сборки
                if (queue.Count > 0 && queue[0].Status == OrderProductionStatus.InProgress)
                {
                    float barW = panelW - 36;
                    float barH = 6f;
                    GUI.Box(new Rect(px + 18, py + 62, barW, barH), "", _progressBarBg);
                    GUI.Box(new Rect(px + 18, py + 62, barW * progress, barH), "", _progressBarFill);
                }

                // Кнопки производства
                float btnY = py + 72f;
                float btnW = 176f;
                float btnH = 80f;
                int playerMetal = _matchState.Player1.Metal;

                // Кнопка 1: Разведчик (350 М)
                DrawPresetButton(px + 18, btnY, btnW, btnH, building.Id, "Разведчик", 350, 8,
                    DefaultContentCatalog.ChassisBiped, DefaultContentCatalog.BodyLight, DefaultContentCatalog.WeaponCannon, null, playerMetal);

                // Кнопка 2: Танк (570 М)
                DrawPresetButton(px + 206, btnY, btnW, btnH, building.Id, "Тяжёлый Танк", 570, 12,
                    DefaultContentCatalog.ChassisTracks, DefaultContentCatalog.BodyHeavy, DefaultContentCatalog.WeaponCannon, DefaultContentCatalog.EquipArmor, playerMetal);

                // Кнопка 3: Ракетчик (440 М)
                DrawPresetButton(px + 394, btnY, btnW, btnH, building.Id, "Ракетчик", 440, 10,
                    DefaultContentCatalog.ChassisTracks, DefaultContentCatalog.BodyLight, DefaultContentCatalog.WeaponMissiles, null, playerMetal);
            }
            else
            {
                GUI.Label(new Rect(px + 18, py + 50, panelW - 36, 60),
                    "Завод производит металл для своего владельца.\n" +
                    "Подведите боевых роботов в круг захвата объекта, чтобы перехватить контроль!", _infoStyle);
            }
        }

        private void DrawPresetButton(float x, float y, float w, float h, EntityId baseId, string name, int cost, int timeSec,
            string chassis, string body, string weapon, string equip, int playerMetal)
        {
            bool canAfford = playerMetal >= cost;
            string costColor = canAfford ? "#FFD700" : "#FF6666";
            string label = $"<b>{name.ToUpper()}</b>\n<color={costColor}>{cost} М</color> • {timeSec}с\n<size=11>{weapon}</size>";

            var style = canAfford ? _btnStyle : _btnDisabledStyle;
            if (GUI.Button(new Rect(x, y, w, h), label, style))
            {
                if (canAfford)
                {
                    OrderPresetRobot(baseId, name, chassis, body, weapon, equip);
                }
                else
                {
                    Debug.LogWarning($"[Battle] Недостаточно металла для сборки '{name}' (требуется {cost}, в наличии {playerMetal})!");
                }
            }
        }

        private void DrawRobotPanel(RobotView robot)
        {
            var st = robot.State;
            if (st == null) return;

            float panelW = 520f;
            float panelH = 145f;
            float px = Mathf.Max(10f, (Screen.width - panelW) * 0.5f);
            float py = Screen.height - panelH - 12f;

            GUI.Box(new Rect(px, py, panelW, panelH), "", _panelBoxStyle);
            GUI.Label(new Rect(px + 18, py + 12, 320, 24), $"⚔ <b>РОБОТ #{robot.EntityId}: {st.Blueprint.Name}</b> ⚔", _headerStyle);
            GUI.Label(new Rect(px + panelW - 170, py + 12, 155, 24), $"Владелец: <b>{st.Owner}</b>", _infoStyle);

            GUI.Label(new Rect(px + 18, py + 38, 230, 20), $"Прочность: <color=#00FF88><b>{st.CurrentHealth} / {st.MaxHealth} HP</b></color>", _infoStyle);
            GUI.Label(new Rect(px + 260, py + 38, 230, 20), $"Скорость: <b>{st.MoveSpeed:F1} м/с</b> | Вес: {st.Mass} кг", _infoStyle);
            GUI.Label(new Rect(px + 18, py + 62, 230, 20), $"Шасси: {st.Blueprint.ChassisId}", _infoStyle);
            GUI.Label(new Rect(px + 260, py + 62, 230, 20), $"Оружие: {st.Blueprint.WeaponId}", _infoStyle);

            if (st.Owner == PlayerId.Player1)
            {
                GUI.Label(new Rect(px + 18, py + 95, panelW - 36, 36),
                    "👉 <color=#FFFF00><b>ПРИКАЗ: Кликните по земле в любое место карты, чтобы направить робота!</b></color>", _infoStyle);
            }
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
