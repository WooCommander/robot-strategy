using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using Game.Content.Definitions;
using Game.Content.Validation;
using Game.Core.Models;
using Game.Core.Types;
using EntityId = Game.Core.Types.EntityId;

namespace Game.Presentation.Views
{
    public class BattleHudView : MonoBehaviour
    {
        private Canvas _canvas;
        private CanvasScaler _scaler;
        private GraphicRaycaster _raycaster;

        // Элементы Top Bar
        private Text _metalText;
        private Text _armyText;

        // Элементы Bottom Panel
        private GameObject _bottomPanel;
        private Text _panelTitleText;
        private Text _queueStatusText;
        private RectTransform _progressBarFillRect;
        private GameObject _progressBarObj;

        private Button _constructorBtn;
        private GameObject _constructorOverlay;
        private Text _constructorSummary;
        private RobotConstructorDraft _constructorDraft;
        private int _constructorStep;
        private readonly string[] _selectedModules = new string[4];
        private readonly string[] _stepNames = { "ШАССИ", "КОРПУС", "ОРУЖИЕ", "УПРАВЛЕНИЕ" };
        private readonly ModuleSlotType[] _stepSlots = { ModuleSlotType.Chassis, ModuleSlotType.Body, ModuleSlotType.Weapon, ModuleSlotType.Control };
        private Transform _constructorOptions;
        private Transform _constructorPreview;
        private Text _constructorStepTitle;
        private Text _constructorParts;
        private Button _constructorNext;
        private Button _constructorBack;
        private Button _constructorProduce;
        private PlayerState _constructorPlayer;
        private BuildingState _constructorBase;

        // Делегат заказа робота
        public event Action<RobotBlueprint> OnOrderBlueprintRequested;
        public event Action<OrderType> OnRobotOrderRequested;
        public event Action OnRestartRequested;
        private readonly Dictionary<OrderType, Button> _orderButtons = new Dictionary<OrderType, Button>();
        public event Action OnSelectPlayerBaseRequested;

        private Font _uiFont;
        private bool _isOrderMode;
        private IReadOnlyDictionary<string, ModuleDefinition> _moduleCatalog;
        private MapDefinition _mapDefinition;
        private RectTransform _minimapContent;
        private readonly Dictionary<int, Image> _minimapBuildingMarkers = new Dictionary<int, Image>();
        private readonly Dictionary<int, Image> _minimapRobotMarkers = new Dictionary<int, Image>();
        private GameObject _resultOverlay;
        private Text _resultTitle;

        public void Initialize(IReadOnlyDictionary<string, ModuleDefinition> moduleCatalog, MapDefinition mapDefinition)
        {
            _moduleCatalog = moduleCatalog;
            _mapDefinition = mapDefinition;
            _constructorDraft = new RobotConstructorDraft(moduleCatalog);
            EnsureFont();
            EnsureEventSystem();
            CreateCanvas();
            CreateTopBar();
            CreateBottomPanel();
            CreateMinimap();
        }

        public void SetOrderMode(bool isOrderMode)
        {
            _isOrderMode = isOrderMode;
        }

        public void UpdateMinimap(MatchState state)
        {
            if (_minimapContent == null || state == null || _mapDefinition == null) return;

            foreach (var building in state.Buildings.Values)
            {
                var marker = GetOrCreateMarker(_minimapBuildingMarkers, building.Id.Value, "Building", 10f);
                marker.color = GetOwnerColor(building.Owner, 0.95f);
                PlaceMarker(marker.rectTransform, building.Position);
            }

            var activeRobotIds = new HashSet<int>();
            foreach (var robot in state.Robots.Values)
            {
                if (!robot.IsAlive) continue;
                activeRobotIds.Add(robot.Id.Value);
                var marker = GetOrCreateMarker(_minimapRobotMarkers, robot.Id.Value, "Robot", 5f);
                marker.color = GetOwnerColor(robot.Owner, 1f);
                PlaceMarker(marker.rectTransform, robot.Position);
            }

            RemoveMissingRobotMarkers(activeRobotIds);
        }

        public void ShowMatchResult(MatchResult result)
        {
            EnsureResultOverlay();
            _resultTitle.text = result == MatchResult.Player1Won ? "ПОБЕДА" : "ПОРАЖЕНИЕ";
            _resultTitle.color = result == MatchResult.Player1Won ? new Color(0.25f, 0.9f, 0.45f) : new Color(1f, 0.3f, 0.3f);
            _resultOverlay.SetActive(true);
        }

        private void EnsureFont()
        {
            if (_uiFont != null) return;
            _uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") 
                      ?? Resources.GetBuiltinResource<Font>("Arial.ttf")
                      ?? Font.CreateDynamicFontFromOSFont("Arial", 16);
        }

        private void EnsureEventSystem()
        {
            if (EventSystem.current == null)
            {
                var esGo = new GameObject("EventSystem");
                esGo.AddComponent<EventSystem>();
                esGo.AddComponent<InputSystemUIInputModule>();
            }
        }

        private void CreateCanvas()
        {
            var canvasGo = new GameObject("BattleCanvas", typeof(RectTransform));

            var canvasRect = (RectTransform)canvasGo.transform;
            canvasRect.anchorMin = Vector2.zero;
            canvasRect.anchorMax = Vector2.one;
            canvasRect.offsetMin = Vector2.zero;
            canvasRect.offsetMax = Vector2.zero;

            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 100;

            _scaler = canvasGo.AddComponent<CanvasScaler>();
            _scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _scaler.referenceResolution = new Vector2(1920, 1080);
            _scaler.matchWidthOrHeight = 0.5f;

            _raycaster = canvasGo.AddComponent<GraphicRaycaster>();
        }

        private void CreateTopBar()
        {
            // Плашка ресурсов (Pivot 0, 1 - левый верхний угол)
            var topBar = CreatePanel(_canvas.transform, "TopBar", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(20f, -20f), new Vector2(430f, 110f), new Color(0.06f, 0.10f, 0.18f, 0.94f));

            // Заголовок
            CreateLabel(topBar.transform, "Title", "★ РЕСУРСЫ ИГРОКА (СИНИЕ) ★", new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(15f, -10f), new Vector2(-140f, -32f), 17, FontStyle.Bold, new Color(0.35f, 0.85f, 1f), TextAnchor.MiddleLeft);

            // Металл
            _metalText = CreateLabel(topBar.transform, "Metal", "💰 Металл: 600 (+5/с)", new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(15f, -38f), new Vector2(-140f, -62f), 16, FontStyle.Bold, new Color(1f, 0.85f, 0.15f), TextAnchor.MiddleLeft);

            // Армия
            _armyText = CreateLabel(topBar.transform, "Army", "🤖 Армия: 0 / 20", new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(15f, -68f), new Vector2(-140f, -92f), 15, FontStyle.Normal, Color.white, TextAnchor.MiddleLeft);

            // Кнопка быстрого выбора базы
            var selectBaseBtn = CreateButton(topBar.transform, "SelectBaseBtn", "🏛 БАЗА\n<size=11>ВЫБРАТЬ</size>", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-70f, 0f), new Vector2(115f, 80f), new Color(0.12f, 0.35f, 0.58f, 0.98f), 13);
            selectBaseBtn.onClick.AddListener(() => OnSelectPlayerBaseRequested?.Invoke());
        }

        private void CreateMinimap()
        {
            var panel = CreatePanel(_canvas.transform, "Minimap", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-20f, -20f), new Vector2(230f, 170f), new Color(0.03f, 0.07f, 0.12f, 0.92f));
            CreateLabel(panel.transform, "Title", "МИНИКАРТА", new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(8f, -6f), new Vector2(-8f, -27f), 12, FontStyle.Bold, new Color(0.5f, 0.85f, 1f), TextAnchor.MiddleCenter);

            var mapArea = CreatePanel(panel.transform, "Map", new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -12f), new Vector2(-12f, -38f), new Color(0.18f, 0.36f, 0.16f, 1f));
            _minimapContent = mapArea.GetComponent<RectTransform>();
        }

        private void EnsureResultOverlay()
        {
            if (_resultOverlay != null) return;
            _resultOverlay = CreatePanel(_canvas.transform, "MatchResult", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, new Color(0f, 0f, 0f, 0.72f));
            var card = CreatePanel(_resultOverlay.transform, "Card", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(420f, 220f), new Color(0.05f, 0.1f, 0.18f, 1f));
            _resultTitle = CreateLabel(card.transform, "Title", "ПОБЕДА", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(20f, -35f), new Vector2(-20f, -95f), 38, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
            var restart = CreateButton(card.transform, "Restart", "НОВЫЙ МАТЧ", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 42f), new Vector2(220f, 58f), new Color(0.12f, 0.42f, 0.7f), 18);
            restart.onClick.AddListener(() => OnRestartRequested?.Invoke());
            _resultOverlay.SetActive(false);
        }

        private Image GetOrCreateMarker(Dictionary<int, Image> markers, int id, string type, float size)
        {
            if (markers.TryGetValue(id, out var existing)) return existing;

            var markerGo = new GameObject($"{type}_{id}");
            markerGo.transform.SetParent(_minimapContent, false);
            var rt = markerGo.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = Vector2.one * size;
            var image = markerGo.AddComponent<Image>();
            markers[id] = image;
            return image;
        }

        private void PlaceMarker(RectTransform marker, SimVector2 position)
        {
            marker.anchorMin = new Vector2(position.X / _mapDefinition.Width, position.Y / _mapDefinition.Height);
            marker.anchorMax = marker.anchorMin;
            marker.anchoredPosition = Vector2.zero;
        }

        private void RemoveMissingRobotMarkers(HashSet<int> activeRobotIds)
        {
            var obsolete = new List<int>();
            foreach (var pair in _minimapRobotMarkers)
            {
                if (!activeRobotIds.Contains(pair.Key))
                {
                    Destroy(pair.Value.gameObject);
                    obsolete.Add(pair.Key);
                }
            }
            foreach (int id in obsolete) _minimapRobotMarkers.Remove(id);
        }

        private static Color GetOwnerColor(PlayerId owner, float alpha)
        {
            return owner == PlayerId.Player1 ? new Color(0.2f, 0.65f, 1f, alpha) :
                owner == PlayerId.Player2 ? new Color(1f, 0.25f, 0.25f, alpha) : new Color(0.8f, 0.8f, 0.8f, alpha);
        }

        private void CreateBottomPanel()
        {
            // Нижняя панель производства (Bottom Deck)
            _bottomPanel = CreatePanel(_canvas.transform, "BottomDeck", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 18f), new Vector2(920f, 215f), new Color(0.06f, 0.10f, 0.18f, 0.95f));
            _bottomPanel.SetActive(false); // По умолчанию скрыта, пока ничего не выбрано

            // Заголовок базы
            _panelTitleText = CreateLabel(_bottomPanel.transform, "Title", "★ ГЛАВНАЯ БАЗА ИГРОКА 1 (ПРОИЗВОДСТВО) ★",
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(20f, -12f), new Vector2(-20f, -36f), 18, FontStyle.Bold, new Color(0.35f, 0.85f, 1f), TextAnchor.MiddleLeft);

            // Статус очереди
            _queueStatusText = CreateLabel(_bottomPanel.transform, "QueueStatus", "Очередь: 0/5 | База: +5 М/сек, завод: +10 М/сек",
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(20f, -38f), new Vector2(-20f, -62f), 15, FontStyle.Normal, Color.white, TextAnchor.MiddleLeft);

            // Прогресс-бар сборки (фон)
            _progressBarObj = CreatePanel(_bottomPanel.transform, "ProgressBarBg", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -70f), new Vector2(-40f, 8f), new Color(0.10f, 0.15f, 0.22f, 1f));

            // Прогресс-бар сборки (заполнение)
            var fillGo = new GameObject("Fill");
            fillGo.transform.SetParent(_progressBarObj.transform, false);
            var fillRt = fillGo.AddComponent<RectTransform>();
            fillRt.anchorMin = new Vector2(0f, 0f);
            fillRt.anchorMax = new Vector2(0f, 1f);
            fillRt.pivot = new Vector2(0f, 0.5f);
            fillRt.offsetMin = Vector2.zero;
            fillRt.offsetMax = Vector2.zero;
            _progressBarFillRect = fillRt;
            var progressBarFill = fillGo.AddComponent<Image>();
            progressBarFill.color = new Color(0.15f, 0.80f, 0.95f, 1f);
            SetProgress(0f);

            // Кнопки сборки роботов
            _constructorBtn = CreateButton(_bottomPanel.transform, "Btn_Constructor", "КОНСТРУКТОР", new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-165f, -75f), new Vector2(280f, 64f), new Color(0.22f, 0.45f, 0.62f), 12);
            _constructorBtn.onClick.AddListener(ShowConstructor);

            CreateOrderButton(OrderType.Capture, "ЗАХВАТ", -300f);
            CreateOrderButton(OrderType.Hunt, "ОХОТА", -150f);
            CreateOrderButton(OrderType.Scout, "РАЗВЕДКА", 0f);
            CreateOrderButton(OrderType.Defend, "ОБОРОНА", 150f);
            CreateOrderButton(OrderType.Stop, "СТОП", 300f);
        }

        public void UpdateState(PlayerState player, BuildingState activeBuilding, RobotState selectedRobot)
        {
            _constructorPlayer = player;
            _constructorBase = activeBuilding != null && activeBuilding.Owner == PlayerId.Player1 && activeBuilding.Type == BuildingType.Base ? activeBuilding : null;
            if (_constructorOverlay != null && _constructorOverlay.activeSelf)
            {
                if (_constructorBase == null) _constructorOverlay.SetActive(false);
                else RefreshConstructorSummary();
            }
            if (player != null)
            {
                _metalText.text = $"💰 Металл: <color=#FFD700><b>{player.Metal}</b></color> (<color=#00FF88>+{player.MetalIncomePerSecond}/с</color>)";
                _armyText.text = $"🤖 Армия: <b>{player.TotalArmyCount} / {PlayerState.MaxArmyLimit}</b> (В бою: {player.ActiveRobotsCount})";

                _constructorPlayer = player;
            }

            if (selectedRobot != null)
            {
                if (!_bottomPanel.activeSelf) _bottomPanel.SetActive(true);
                _panelTitleText.text = $"⚔ <b>РОБОТ #{selectedRobot.Id.Value}: {selectedRobot.Blueprint.Name}</b> ⚔";
                string controlHint = _isOrderMode
                    ? "<color=#66CCFF>ПРИКАЗЫ: C — захват | H — охота | R — разведка | F — оборона | S — стоп</color>"
                    : "<color=#FFFF00>РУЧНОЙ РЕЖИМ: WASD / стрелки — движение</color>";
                _queueStatusText.text = $"HP: <color=#00FF88><b>{selectedRobot.CurrentHealth}/{selectedRobot.MaxHealth}</b></color> | Скорость: {selectedRobot.MoveSpeed:F1} м/с | {controlHint}";
                SetProgress(0f);
                SetProductionButtonsVisible(false);
                SetOrderButtonsVisible(true, selectedRobot.CurrentOrder.Type);
            }
            else if (activeBuilding != null)
            {
                if (!_bottomPanel.activeSelf) _bottomPanel.SetActive(true);
                SetProductionButtonsVisible(true);
                SetOrderButtonsVisible(false, OrderType.None);

                if (activeBuilding.Type == BuildingType.Base && activeBuilding.Owner == PlayerId.Player1)
                {
                    _panelTitleText.text = "★ <b>ГЛАВНАЯ БАЗА ИГРОКА 1 (СБОРКА РОБОТОВ)</b> ★";
                    var q = activeBuilding.ProductionQueue;

                    if (q.Count > 0)
                    {
                        var first = q[0];
                        if (first.Status == OrderProductionStatus.ExitBlocked)
                        {
                            _queueStatusText.text = "<color=yellow>⚠️ ВЫХОД ЗАБЛОКИРОВАН (уведите робота с точки сбора)!</color>";
                            SetProgress(1f);
                        }
                        else
                        {
                            float rem = first.RemainingBuildTimeSeconds;
                            float total = Mathf.Max(0.01f, first.TotalBuildTimeSeconds);
                            float progress = Mathf.Clamp01(1f - (rem / total));
                            _queueStatusText.text = $"Сборка: <b>{first.Blueprint.Name}</b> (осталось {rem:F1}с) | Очередь: {q.Count}/{BuildingState.MaxQueueSize}";
                            SetProgress(progress);
                        }
                    }
                    else
                    {
                        _queueStatusText.text = $"Очередь: <b>0/{BuildingState.MaxQueueSize}</b> | База: <color=#00FF88>+5 М/сек</color>, завод: <color=#00FF88>+10 М/сек</color>.";
                        SetProgress(0f);
                    }
                }
                else
                {
                    _panelTitleText.text = activeBuilding.Type == BuildingType.Base
                        ? $"★ ВРАЖЕСКАЯ БАЗА #{activeBuilding.Id.Value} ★"
                        : $"★ НЕЙТРАЛЬНЫЙ ЗАВОД #{activeBuilding.Id.Value} ★";
                    if (activeBuilding.CaptureProgressSeconds > 0f || activeBuilding.IsContested)
                    {
                        float capturePercent = Mathf.Clamp01(activeBuilding.CaptureProgressSeconds / activeBuilding.CaptureTimeTotal) * 100f;
                        _queueStatusText.text = activeBuilding.IsContested
                            ? "<color=#FFD700>ЗАХВАТ ОСПАРИВАЕТСЯ</color>"
                            : $"<color=#66CCFF>Захват: {capturePercent:0}%</color> игроком {activeBuilding.CapturingPlayer}";
                    }
                    else
                    {
                        _queueStatusText.text = $"Доход объекта: +{activeBuilding.MetalIncomePerSecond} М/сек. Подведите боевых роботов в круг захвата!";
                    }
                    SetProgress(0f);
                    SetProductionButtonsVisible(false);
                    SetOrderButtonsVisible(false, OrderType.None);
                }
            }
            else
            {
                if (_bottomPanel.activeSelf) _bottomPanel.SetActive(false);
            }
        }

        private void SetProductionButtonsVisible(bool visible)
        {
            if (_constructorBtn != null) _constructorBtn.gameObject.SetActive(visible);
        }

        private void CreateOrderButton(OrderType order, string label, float x)
        {
            var button = CreateButton(_bottomPanel.transform, "Order_" + order, label, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(x, 65f), new Vector2(135f, 48f), new Color(0.12f, 0.20f, 0.32f), 13);
            button.onClick.AddListener(() => OnRobotOrderRequested?.Invoke(order));
            _orderButtons[order] = button;
            button.gameObject.SetActive(false);
        }

        private void SetOrderButtonsVisible(bool visible, OrderType activeOrder)
        {
            foreach (var pair in _orderButtons)
            {
                pair.Value.gameObject.SetActive(visible);
                var image = pair.Value.GetComponent<Image>();
                image.color = pair.Key == activeOrder ? new Color(0.15f, 0.65f, 0.95f) : new Color(0.12f, 0.20f, 0.32f);
            }
        }

        private void SetProgress(float value)
        {
            if (_progressBarFillRect == null) return;

            _progressBarFillRect.anchorMax = new Vector2(Mathf.Clamp01(value), 1f);
        }

        private void ShowConstructor()
        {
            if (_constructorBase == null) return;
            if (_constructorOverlay == null) CreateConstructorOverlay();
            _constructorStep = 0;
            _constructorDraft = new RobotConstructorDraft(_moduleCatalog);
            Array.Clear(_selectedModules, 0, _selectedModules.Length);
            _constructorOverlay.SetActive(true);
            RefreshConstructorStep();
        }

        private void CreateConstructorOverlay()
        {
            _constructorOverlay = CreatePanel(_canvas.transform, "RobotConstructor", Vector2.zero, Vector2.one,
                new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, new Color(0f, 0f, 0f, 0.85f));
            var card = CreatePanel(_constructorOverlay.transform, "Card", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(900f, 640f), new Color(0.06f, 0.10f, 0.18f));
            CreateLabel(card.transform, "Title", "КОНСТРУКТОР РОБОТА", new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(24f, -18f), new Vector2(-85f, -55f), 24, FontStyle.Bold, Color.cyan, TextAnchor.MiddleLeft);
            var close = CreateButton(card.transform, "Close", "×", Vector2.one, Vector2.one,
                new Vector2(-34f, -34f), new Vector2(44f, 42f), new Color(0.38f, 0.16f, 0.18f), 22);
            close.onClick.AddListener(() => _constructorOverlay.SetActive(false));
            _constructorStepTitle = CreateLabel(card.transform, "Step", "", new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(24f, -75f), new Vector2(-24f, -110f), 18, FontStyle.Bold, Color.white, TextAnchor.MiddleLeft);
            _constructorOptions = CreatePanel(card.transform, "Options", new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 1f), new Vector2(24f, -125f), new Vector2(410f, 320f), Color.clear).transform;
            _constructorPreview = CreatePanel(card.transform, "Preview", new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(1f, 1f), new Vector2(-24f, -125f), new Vector2(410f, 230f), new Color(0.09f, 0.17f, 0.25f)).transform;
            _constructorParts = CreateLabel(card.transform, "Parts", "", new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-434f, -370f), new Vector2(-24f, -460f), 15, FontStyle.Normal, Color.white, TextAnchor.UpperLeft);
            _constructorSummary = CreateLabel(card.transform, "Summary", "", Vector2.zero, new Vector2(1f, 0f),
                new Vector2(24f, 160f), new Vector2(-24f, 80f), 16, FontStyle.Normal, Color.white, TextAnchor.MiddleLeft);
            _constructorBack = CreateButton(card.transform, "Back", "НАЗАД", Vector2.zero, Vector2.zero,
                new Vector2(100f, 36f), new Vector2(150f, 48f), new Color(0.2f, 0.3f, 0.4f), 16);
            _constructorBack.onClick.AddListener(() => { _constructorStep--; RefreshConstructorStep(); });
            _constructorNext = CreateButton(card.transform, "Next", "ДАЛЕЕ", new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-174f, 36f), new Vector2(300f, 48f), new Color(0.1f, 0.45f, 0.65f), 16);
            _constructorNext.onClick.AddListener(() => { _constructorStep++; RefreshConstructorStep(); });
            _constructorProduce = CreateButton(card.transform, "Produce", "ПОСТАВИТЬ В ОЧЕРЕДЬ", new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-174f, 36f), new Vector2(300f, 48f), new Color(0.12f, 0.48f, 0.30f), 16);
            _constructorProduce.onClick.AddListener(() =>
            {
                RefreshConstructorSummary();
                if (!_constructorProduce.interactable) return;
                OnOrderBlueprintRequested?.Invoke(_constructorDraft.CreateBlueprint($"custom_{Guid.NewGuid():N}", "Пользовательский робот"));
                _constructorOverlay.SetActive(false);
            });
            _constructorOverlay.SetActive(false);
        }

        private void RefreshConstructorStep()
        {
            foreach (Transform child in _constructorOptions) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
            _constructorStepTitle.text = $"ШАГ {_constructorStep + 1} / 4 — {_stepNames[_constructorStep]}";
            int index = 0;
            foreach (var module in _constructorDraft.GetModules(_stepSlots[_constructorStep]))
            {
                string id = module.Id;
                bool selected = _selectedModules[_constructorStep] == id;
                var button = CreateButton(_constructorOptions, id, (selected ? "✓ " : "") + FormatModuleButton(module),
                    new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(205f, -48f - index * 105f),
                    new Vector2(410f, 96f), selected ? new Color(0.12f, 0.55f, 0.65f) : new Color(0.18f, 0.29f, 0.4f), 16);
                button.onClick.AddListener(() =>
                {
                    _selectedModules[_constructorStep] = id;
                    _constructorDraft.Select(_stepSlots[_constructorStep], id);
                    RefreshConstructorStep();
                });
                index++;
            }
            _constructorBack.interactable = _constructorStep > 0;
            _constructorNext.gameObject.SetActive(_constructorStep < 3);
            _constructorNext.interactable = _selectedModules[_constructorStep] != null;
            _constructorProduce.gameObject.SetActive(_constructorStep == 3);
            RefreshConstructorPreview();
            RefreshConstructorSummary();
        }

        private void RefreshConstructorPreview()
        {
            foreach (Transform child in _constructorPreview) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
            // Схематический вид сверху: части появляются только после выбора соответствующего модуля.
            if (_selectedModules[0] != null)
            {
                bool tracks = _selectedModules[0] == DefaultContentCatalog.ChassisTracks;
                PreviewPart("LeftChassis", -72f, -25f, 32f, tracks ? 145f : 90f, Color.gray);
                PreviewPart("RightChassis", 72f, -25f, 32f, tracks ? 145f : 90f, Color.gray);
                PreviewPart("Frame", 0f, -25f, 115f, 35f, new Color(0.3f, 0.34f, 0.4f));
            }
            if (_selectedModules[1] != null)
            {
                bool heavy = _selectedModules[1] == DefaultContentCatalog.BodyHeavy;
                PreviewPart("Body", 0f, -10f, heavy ? 125f : 90f, heavy ? 110f : 80f, new Color(0.12f, 0.5f, 0.8f));
            }
            if (_selectedModules[2] != null)
            {
                bool missiles = _selectedModules[2] == DefaultContentCatalog.WeaponMissiles;
                if (missiles)
                    for (int i = -1; i <= 1; i++) PreviewPart("Missile" + i, i * 23f, 48f, 16f, 68f, new Color(0.9f, 0.6f, 0.25f));
                else PreviewPart("Cannon", 0f, 55f, 16f, 85f, new Color(0.65f, 0.75f, 0.85f));
            }
            if (_selectedModules[3] != null)
            {
                int lights = _selectedModules[3] == DefaultContentCatalog.ControlBasic ? 1 : _selectedModules[3] == DefaultContentCatalog.ControlTactical ? 2 : 3;
                for (int i = 0; i < lights; i++) PreviewPart("Controller" + i, -18f + i * 18f, -35f, 12f, 12f, Color.cyan);
            }
            string parts = "";
            for (int i = 0; i < 4; i++)
                parts += $"{_stepNames[i]}: {(_selectedModules[i] == null ? "не выбрано" : _moduleCatalog[_selectedModules[i]].Name)}\n";
            _constructorParts.text = parts;
        }

        private void PreviewPart(string name, float x, float y, float width, float height, Color color)
        {
            var part = CreatePanel(_constructorPreview, name, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(x, y), new Vector2(width, height), color);
            part.GetComponent<Image>().raycastTarget = false;
        }

        private void RefreshConstructorSummary()
        {
            if (_constructorSummary == null) return;
            bool complete = Array.TrueForAll(_selectedModules, id => id != null);
            _constructorProduce.interactable = false;
            if (!complete)
            {
                _constructorSummary.text = "Выберите по одному модулю на каждом шаге. Справа — схема сборки (вид сверху).";
                return;
            }
            var validation = _constructorDraft.Validate();
            if (!validation.IsValid)
            {
                _constructorSummary.text = "<color=#FF8888>" + string.Join("\n", validation.Errors) + "</color>";
                return;
            }
            bool available = _constructorPlayer != null && _constructorBase != null &&
                _constructorPlayer.Metal >= validation.TotalCost && _constructorPlayer.CanBuildMoreRobots &&
                _constructorBase.ProductionQueue.Count < BuildingState.MaxQueueSize;
            _constructorProduce.interactable = available;
            _constructorSummary.text = $"Цена: {validation.TotalCost} М   Масса: {validation.TotalMass}/{validation.MaxLoadCapacity} кг   HP: {validation.TotalHealth}" +
                (available ? "\nСборка готова к производству." : "\nНедостаточно металла, заполнена очередь или достигнут лимит армии.");
        }

        private static string FormatModuleButton(ModuleDefinition module)
        {
            string common = $"<color=#FFD700>{module.Cost} М</color> • {module.Mass} кг";
            return module.SlotType switch
            {
                ModuleSlotType.Chassis => $"<b>{module.Name}</b>\n{common}\nСкорость {module.Speed:F1}, лимит {module.MaxLoadCapacity}",
                ModuleSlotType.Body => $"<b>{module.Name}</b>\n{common}\nHP +{module.HealthBonus}, слотов {module.WeaponSlots}",
                ModuleSlotType.Weapon => $"<b>{module.Name}</b>\n{common}\nУрон {module.Damage}, дальн. {module.Range:F0}, КД {module.CooldownSeconds:F1}\nРазведчик: {Mathf.CeilToInt(120f / module.Damage)} попад.",
                ModuleSlotType.Control => $"<b>{module.Name}</b>\n{common}\nУпрежд. {module.LeadPredictionSeconds:F1}с, манёвр {module.CombatManeuverIntervalSeconds:F2}с",
                _ => $"<b>{module.Name}</b>\n{common}\nHP +{module.HealthBonus}"
            };
        }

        // Вспомогательные методы построения UGUI элементов
        private GameObject CreatePanel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPos, Vector2 sizeDelta, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = sizeDelta;

            var img = go.AddComponent<Image>();
            img.color = color;
            return go;
        }

        private Text CreateLabel(Transform parent, string name, string text, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, int fontSize, FontStyle style, Color color, TextAnchor alignment)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = new Vector2(offsetMin.x, offsetMax.y);
            rt.offsetMax = new Vector2(offsetMax.x, offsetMin.y);

            var txt = go.AddComponent<Text>();
            txt.font = _uiFont;
            txt.text = text;
            txt.fontSize = fontSize;
            txt.fontStyle = style;
            txt.color = color;
            txt.alignment = alignment;
            txt.supportRichText = true;
            return txt;
        }

        private Button CreateButton(Transform parent, string name, string labelText, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos, Vector2 sizeDelta, Color normalColor, int fontSize)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = sizeDelta;

            var img = go.AddComponent<Image>();
            img.color = normalColor;

            var btn = go.AddComponent<Button>();
            var colors = btn.colors;
            colors.normalColor = normalColor;
            colors.highlightedColor = normalColor * 1.25f;
            colors.pressedColor = normalColor * 0.8f;
            colors.disabledColor = new Color(0.18f, 0.20f, 0.24f, 0.8f);
            btn.colors = colors;

            // Дочерний текст
            var txtGo = new GameObject("Text");
            txtGo.transform.SetParent(go.transform, false);
            var txtRt = txtGo.AddComponent<RectTransform>();
            txtRt.anchorMin = Vector2.zero;
            txtRt.anchorMax = Vector2.one;
            txtRt.offsetMin = Vector2.zero;
            txtRt.offsetMax = Vector2.zero;

            var txt = txtGo.AddComponent<Text>();
            txt.font = _uiFont;
            txt.text = labelText;
            txt.fontSize = fontSize;
            txt.fontStyle = FontStyle.Bold;
            txt.color = Color.white;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.supportRichText = true;

            return btn;
        }
    }
}
