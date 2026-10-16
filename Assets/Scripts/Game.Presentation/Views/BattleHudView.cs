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
using Game.Simulation.Systems;
using EntityId = Game.Core.Types.EntityId;

namespace Game.Presentation.Views
{
    public sealed class ProgramEditorValues
    {
        public EntityId RobotId;
        public int DefendPriority, AttackPriority, CapturePriority;
        public float DefendRadius, AttackRadius, CaptureRadius;
        public int DefendMinHealthPercent, AttackMinHealthPercent, CaptureMinHealthPercent;
        public int RetreatHealthPercent;
        public int CaptureResourceType, CaptureResourceBelow;
        public EntityId DefendTargetId, AttackTargetId, CaptureTargetId;

        public static ProgramEditorValues FromRobot(RobotState robot) => new ProgramEditorValues
        {
            RobotId = robot.Id,
            DefendPriority = robot.DefendPriority, AttackPriority = robot.AttackPriority, CapturePriority = robot.CapturePriority,
            DefendRadius = robot.DefendRadius, AttackRadius = robot.AttackRadius, CaptureRadius = robot.CaptureRadius,
            DefendMinHealthPercent = robot.DefendMinHealthPercent, AttackMinHealthPercent = robot.AttackMinHealthPercent, CaptureMinHealthPercent = robot.CaptureMinHealthPercent,
            RetreatHealthPercent = robot.RetreatHealthPercent,
            CaptureResourceType = robot.CaptureResourceType, CaptureResourceBelow = robot.CaptureResourceBelow,
            DefendTargetId = robot.DefendTargetId, AttackTargetId = robot.AttackTargetId, CaptureTargetId = robot.CaptureTargetId
        };
    }

    /// <summary>
    /// Боевой интерфейс по образцу Nether Earth: часы, статус сторон (базы, заводы по типам, роботы),
    /// ресурсы игрока по типам и экран сборки робота из деталей.
    /// </summary>
    public class BattleHudView : MonoBehaviour
    {
        private const float StatusRefreshSeconds = 0.25f;
        private const string ProgramPresetKey = "RobotStrategy.ProgramPreset.";

        [Serializable]
        private sealed class ProgramPreset
        {
            public int DefendPriority, AttackPriority, CapturePriority;
            public float DefendRadius, AttackRadius, CaptureRadius;
            public int DefendMinHealthPercent, AttackMinHealthPercent, CaptureMinHealthPercent;
            public int RetreatHealthPercent;
            public int CaptureResourceType, CaptureResourceBelow;

            public static ProgramPreset From(ProgramEditorValues values) => new ProgramPreset
            {
                DefendPriority = values.DefendPriority, AttackPriority = values.AttackPriority, CapturePriority = values.CapturePriority,
                DefendRadius = values.DefendRadius, AttackRadius = values.AttackRadius, CaptureRadius = values.CaptureRadius,
                DefendMinHealthPercent = values.DefendMinHealthPercent, AttackMinHealthPercent = values.AttackMinHealthPercent, CaptureMinHealthPercent = values.CaptureMinHealthPercent,
                RetreatHealthPercent = values.RetreatHealthPercent,
                CaptureResourceType = values.CaptureResourceType, CaptureResourceBelow = values.CaptureResourceBelow
            };

            public void ApplyTo(ProgramEditorValues values)
            {
                values.DefendPriority = DefendPriority; values.AttackPriority = AttackPriority; values.CapturePriority = CapturePriority;
                values.DefendRadius = DefendRadius; values.AttackRadius = AttackRadius; values.CaptureRadius = CaptureRadius;
                values.DefendMinHealthPercent = DefendMinHealthPercent; values.AttackMinHealthPercent = AttackMinHealthPercent; values.CaptureMinHealthPercent = CaptureMinHealthPercent;
                values.RetreatHealthPercent = RetreatHealthPercent;
                values.CaptureResourceType = CaptureResourceType; values.CaptureResourceBelow = CaptureResourceBelow;
                // ID принадлежат конкретному матчу, поэтому в шаблон не входят.
                values.DefendTargetId = EntityId.None; values.AttackTargetId = EntityId.None; values.CaptureTargetId = EntityId.None;
            }
        }

        // Палитра в духе оригинала: тёмный фон, голубые названия, зелёные запасы, пурпурные цены.
        private static readonly Color PanelColor = new Color(0.02f, 0.03f, 0.06f, 0.94f);
        private static readonly Color HeaderColor = new Color(1f, 0.9f, 0.2f);
        private static readonly Color NameColor = new Color(0.3f, 0.9f, 1f);
        private static readonly Color StockColor = new Color(0.3f, 1f, 0.35f);
        private static readonly Color PriceColor = new Color(1f, 0.35f, 0.95f);
        private static readonly Color ClockColor = new Color(0.75f, 0.08f, 0.08f, 0.96f);
        private static readonly Color HumanColor = new Color(0.35f, 0.75f, 1f);
        private static readonly Color ComputerColor = new Color(1f, 0.4f, 0.4f);

        private static readonly ResourceType[] FactoryTypes =
        {
            ResourceType.Electronics, ResourceType.Nuclear, ResourceType.Phasers, ResourceType.Missiles, ResourceType.Cannon, ResourceType.Chassis
        };
        private static readonly ResourceType[] AllResourceTypes =
        {
            ResourceType.General, ResourceType.Electronics, ResourceType.Nuclear, ResourceType.Phasers, ResourceType.Missiles, ResourceType.Cannon, ResourceType.Chassis
        };

        private Canvas _canvas;
        private Font _uiFont;
        private IReadOnlyDictionary<string, ModuleDefinition> _moduleCatalog;
        private MapDefinition _mapDefinition;
        private bool _isOrderMode;
        private float _nextStatusRefresh;

        // Часы, статус и ресурсы
        private Text _clockText;
        private Text _armyText;
        private Text _statusHumanColumn;
        private Text _statusComputerColumn;
        private Text _resourceValues;

        // Нижняя панель выбранного объекта
        private GameObject _bottomPanel;
        private Text _panelTitleText;
        private Text _queueStatusText;
        private RectTransform _progressBarFillRect;
        private Button _constructorBtn;
        private readonly Dictionary<OrderType, Button> _orderButtons = new Dictionary<OrderType, Button>();
        private Button _programButton;
        private RobotState _shownRobot;
        private GameObject _programOverlay;
        private ProgramEditorValues _programDraft;
        private MatchState _programState;
        private readonly Text[] _programRuleLabels = new Text[3];
        private readonly Text[] _programRadiusLabels = new Text[3];
        private readonly Button[] _programTargetButtons = new Button[3];
        private readonly Button[] _programHealthButtons = new Button[3];
        private readonly Button[] _programResourceButtons = new Button[3];
        private readonly Button[] _programResourceLimitButtons = new Button[3];
        private Button _programPresetSlotButton;
        private Button _programRetreatButton;
        private int _programPresetSlot;
        private Text _programError;

        // Экран сборки
        private GameObject _constructorOverlay;
        private RobotConstructorDraft _constructorDraft;
        private Text _constructorResources;
        private Text _constructorSummary;
        private Text _constructorName;
        private Transform _constructorPreview;
        private Button _constructorProduce;
        private readonly Dictionary<string, Image> _partRowBackgrounds = new Dictionary<string, Image>();
        private readonly Dictionary<string, Text> _partRowMarks = new Dictionary<string, Text>();
        private PlayerState _player;
        private BuildingState _playerBase;

        // Миникарта и итог матча
        private RectTransform _minimapContent;
        private RawImage _minimapFog;
        private readonly Dictionary<int, Image> _minimapBuildingMarkers = new Dictionary<int, Image>();
        private readonly Dictionary<int, Image> _minimapRobotMarkers = new Dictionary<int, Image>();
        private GameObject _resultOverlay;
        private Text _resultTitle;

        public event Action<RobotBlueprint> OnOrderBlueprintRequested;
        public event Action<OrderType> OnRobotOrderRequested;
        public event Func<ProgramEditorValues, string> OnRobotProgramRequested;
        public event Action OnRestartRequested;
        public event Action OnSelectPlayerBaseRequested;
        public event Action<SimVector2> OnMinimapPointRequested;

        public void Initialize(IReadOnlyDictionary<string, ModuleDefinition> moduleCatalog, MapDefinition mapDefinition)
        {
            _moduleCatalog = moduleCatalog;
            _mapDefinition = mapDefinition;
            _constructorDraft = new RobotConstructorDraft(moduleCatalog);
            EnsureFont();
            EnsureEventSystem();
            CreateCanvas();
            CreateTopLeftBar();
            CreateClockAndStatus();
            CreateBottomPanel();
            CreateMinimap();
        }

        public void SetOrderMode(bool isOrderMode)
        {
            _isOrderMode = isOrderMode;
        }

        public void ShowMatchResult(MatchResult result)
        {
            EnsureResultOverlay();
            _resultTitle.text = result == MatchResult.Player1Won ? "ПОБЕДА" : "ПОРАЖЕНИЕ";
            _resultTitle.color = result == MatchResult.Player1Won ? new Color(0.25f, 0.9f, 0.45f) : new Color(1f, 0.3f, 0.3f);
            _resultOverlay.SetActive(true);
        }

        public void UpdateState(MatchState state, BuildingState activeBuilding, RobotState selectedRobot)
        {
            if (state == null) return;
            _player = state.Player1;
            _playerBase = FindBase(state, PlayerId.Player1);
            _programState = state;

            if (Time.unscaledTime >= _nextStatusRefresh)
            {
                _nextStatusRefresh = Time.unscaledTime + StatusRefreshSeconds;
                RefreshClockAndStatus(state);
                if (_constructorOverlay != null && _constructorOverlay.activeSelf) RefreshConstructor();
                if (_programOverlay != null && _programOverlay.activeSelf) RefreshProgramEditor();
            }
            if (_constructorOverlay != null && _constructorOverlay.activeSelf && _playerBase == null) _constructorOverlay.SetActive(false);

            if (selectedRobot != null) ShowRobotPanel(selectedRobot);
            else if (activeBuilding != null) ShowBuildingPanel(activeBuilding);
            else if (_bottomPanel.activeSelf) _bottomPanel.SetActive(false);
        }

        public void UpdateMinimap(MatchState state)
        {
            if (_minimapContent == null || state == null || _mapDefinition == null) return;

            foreach (var building in state.Buildings.Values)
            {
                var marker = GetOrCreateMarker(_minimapBuildingMarkers, building.Id.Value, "Building", 10f);
                bool visible = building.Owner == PlayerId.Player1 || state.FogOfWar == null || state.FogOfWar.IsVisible(PlayerId.Player1, building.Position);
                bool explored = visible || state.FogOfWar.IsExplored(PlayerId.Player1, building.Position);
                marker.gameObject.SetActive(explored);
                marker.color = visible ? GetOwnerColor(building.Owner, 0.95f) : new Color(0.75f, 0.75f, 0.75f, 0.5f);
                PlaceMarker(marker.rectTransform, building.Position);
            }

            var activeRobotIds = new HashSet<int>();
            foreach (var robot in state.Robots.Values)
            {
                if (!robot.IsAlive) continue;
                activeRobotIds.Add(robot.Id.Value);
                var marker = GetOrCreateMarker(_minimapRobotMarkers, robot.Id.Value, "Robot", 5f);
                marker.gameObject.SetActive(robot.Owner == PlayerId.Player1 || state.FogOfWar == null || state.FogOfWar.IsVisible(PlayerId.Player1, robot.Position));
                marker.color = GetOwnerColor(robot.Owner, 1f);
                marker.rectTransform.sizeDelta = Vector2.one * 5f;
                PlaceMarker(marker.rectTransform, robot.Position);
            }

            if (state.FogOfWar != null)
            {
                foreach (LastSeenEnemyState contact in state.FogOfWar.Player1LastSeenEnemies)
                {
                    if (state.Robots.TryGetValue(contact.RobotId.Value, out RobotState enemy) &&
                        enemy.IsAlive && state.FogOfWar.IsVisible(PlayerId.Player1, enemy.Position)) continue;
                    activeRobotIds.Add(contact.RobotId.Value);
                    Image marker = GetOrCreateMarker(_minimapRobotMarkers, contact.RobotId.Value, "Robot", 7f);
                    marker.gameObject.SetActive(true);
                    marker.color = new Color(1f, 0.78f, 0.25f, 0.7f);
                    marker.rectTransform.sizeDelta = Vector2.one * 7f;
                    PlaceMarker(marker.rectTransform, contact.Position);
                }
            }

            RemoveMissingRobotMarkers(activeRobotIds);
        }

        // ───────────────────────── Часы, статус, ресурсы ─────────────────────────

        private void CreateTopLeftBar()
        {
            var bar = CreatePanel(_canvas.transform, "TopLeft", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(20f, -20f), new Vector2(330f, 70f), PanelColor);
            _armyText = CreateText(bar.transform, "Army", "", new Vector2(14f, -8f), new Vector2(190f, 54f), 16, FontStyle.Bold, Color.white, TextAnchor.MiddleLeft);
            var selectBase = CreateButton(bar.transform, "SelectBaseBtn", "БАЗА", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-62f, 0f), new Vector2(106f, 54f), new Color(0.12f, 0.35f, 0.58f, 0.98f), 15);
            selectBase.onClick.AddListener(() => OnSelectPlayerBaseRequested?.Invoke());
        }

        private void CreateClockAndStatus()
        {
            const float width = 320f;
            var clock = CreatePanel(_canvas.transform, "Clock", Vector2.one, Vector2.one, Vector2.one, new Vector2(-20f, -20f), new Vector2(width, 64f), ClockColor);
            _clockText = CreateText(clock.transform, "ClockText", "", new Vector2(12f, -4f), new Vector2(width - 24f, 56f), 22, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);

            // Статус сторон: слева вы, справа компьютер, посередине названия строк.
            var status = CreatePanel(_canvas.transform, "Status", Vector2.one, Vector2.one, Vector2.one, new Vector2(-20f, -94f), new Vector2(width, 292f), PanelColor);
            CreateText(status.transform, "Header", "СТАТУС", new Vector2(0f, -8f), new Vector2(width, 30f), 20, FontStyle.Bold, HeaderColor, TextAnchor.MiddleCenter);
            CreateText(status.transform, "Human", "ВЫ", new Vector2(12f, -38f), new Vector2(60f, 24f), 14, FontStyle.Bold, HumanColor, TextAnchor.MiddleLeft);
            CreateText(status.transform, "Computer", "ИИ", new Vector2(width - 72f, -38f), new Vector2(60f, 24f), 14, FontStyle.Bold, ComputerColor, TextAnchor.MiddleRight);
            string rowNames = "БАЗЫ";
            foreach (ResourceType type in FactoryTypes) rowNames += "\n" + DefaultContentCatalog.GetResourceName(type).ToUpperInvariant();
            rowNames += "\nРОБОТЫ";
            CreateText(status.transform, "Rows", rowNames, new Vector2(60f, -64f), new Vector2(width - 120f, 220f), 16, FontStyle.Bold, NameColor, TextAnchor.UpperCenter).lineSpacing = 1.05f;
            _statusHumanColumn = CreateText(status.transform, "HumanValues", "", new Vector2(12f, -64f), new Vector2(48f, 220f), 16, FontStyle.Bold, HumanColor, TextAnchor.UpperLeft);
            _statusHumanColumn.lineSpacing = 1.05f;
            _statusComputerColumn = CreateText(status.transform, "ComputerValues", "", new Vector2(width - 60f, -64f), new Vector2(48f, 220f), 16, FontStyle.Bold, ComputerColor, TextAnchor.UpperRight);
            _statusComputerColumn.lineSpacing = 1.05f;

            var resources = CreatePanel(_canvas.transform, "Resources", Vector2.one, Vector2.one, Vector2.one, new Vector2(-20f, -396f), new Vector2(width, 236f), PanelColor);
            CreateText(resources.transform, "Header", "РЕСУРСЫ", new Vector2(0f, -8f), new Vector2(width, 30f), 20, FontStyle.Bold, HeaderColor, TextAnchor.MiddleCenter);
            string resourceNames = "";
            foreach (ResourceType type in AllResourceTypes) resourceNames += (resourceNames.Length > 0 ? "\n" : "") + DefaultContentCatalog.GetResourceName(type).ToUpperInvariant();
            CreateText(resources.transform, "Names", resourceNames, new Vector2(16f, -42f), new Vector2(170f, 190f), 16, FontStyle.Bold, StockColor, TextAnchor.UpperLeft).lineSpacing = 1.05f;
            _resourceValues = CreateText(resources.transform, "Values", "", new Vector2(170f, -42f), new Vector2(width - 186f, 190f), 16, FontStyle.Bold, Color.white, TextAnchor.UpperRight);
            _resourceValues.lineSpacing = 1.05f;
        }

        private void RefreshClockAndStatus(MatchState state)
        {
            int hour = GameClock.GetHourOfDay(state.MatchTimeSeconds);
            _clockText.text = $"ДЕНЬ: {GameClock.GetDay(state.MatchTimeSeconds)}   ВРЕМЯ: {hour:00}:00";
            _armyText.text = $"Армия: {state.Player1.TotalArmyCount}/{PlayerState.MaxArmyLimit}\n<size=13>в бою {state.Player1.ActiveRobotsCount}, в очереди {state.Player1.QueuedRobotsCount}</size>";
            _statusHumanColumn.text = BuildStatusColumn(state, PlayerId.Player1);
            _statusComputerColumn.text = BuildStatusColumn(state, PlayerId.Player2);

            int[] income = ProductionSystem.GetHourlyIncome(state, PlayerId.Player1);
            string values = "";
            foreach (ResourceType type in AllResourceTypes)
            {
                int perHour = income[(int)type];
                string incomeText = perHour > 0 ? $"<color=#66FF88>+{perHour}/ч</color>" : "<color=#556070>—</color>";
                values += (values.Length > 0 ? "\n" : "") + $"{state.Player1.Resources[type],3}  {incomeText}";
            }
            _resourceValues.text = values;
        }

        private static string BuildStatusColumn(MatchState state, PlayerId owner)
        {
            int bases = 0;
            var factories = new int[ResourceStock.TypeCount];
            foreach (BuildingState building in state.Buildings.Values)
            {
                if (building.Owner != owner) continue;
                if (owner != PlayerId.Player1 && state.FogOfWar != null && !state.FogOfWar.IsVisible(PlayerId.Player1, building.Position)) continue;
                if (building.Type == BuildingType.Base) bases++;
                else factories[(int)building.ProducedResource]++;
            }
            string column = bases.ToString();
            foreach (ResourceType type in FactoryTypes) column += "\n" + factories[(int)type];
            int visibleRobots = 0;
            foreach (RobotState robot in state.Robots.Values)
                if (robot.IsAlive && robot.Owner == owner && (owner == PlayerId.Player1 || state.FogOfWar == null || state.FogOfWar.IsVisible(PlayerId.Player1, robot.Position))) visibleRobots++;
            return column + "\n" + visibleRobots.ToString("00");
        }

        // ───────────────────────── Нижняя панель ─────────────────────────

        private void CreateBottomPanel()
        {
            _bottomPanel = CreatePanel(_canvas.transform, "BottomDeck", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 18f), new Vector2(980f, 300f), PanelColor);
            _bottomPanel.SetActive(false);

            _panelTitleText = CreateLabel(_bottomPanel.transform, "Title", "", new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(20f, -12f), new Vector2(-20f, -36f), 18, FontStyle.Bold, NameColor, TextAnchor.MiddleLeft);
            _queueStatusText = CreateLabel(_bottomPanel.transform, "QueueStatus", "", new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(20f, -38f), new Vector2(-330f, -96f), 15, FontStyle.Normal, Color.white, TextAnchor.UpperLeft);

            var progressBar = CreatePanel(_bottomPanel.transform, "ProgressBarBg", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -106f), new Vector2(-40f, 8f), new Color(0.10f, 0.15f, 0.22f, 1f));
            var fillGo = new GameObject("Fill");
            fillGo.transform.SetParent(progressBar.transform, false);
            _progressBarFillRect = fillGo.AddComponent<RectTransform>();
            _progressBarFillRect.anchorMin = Vector2.zero;
            _progressBarFillRect.anchorMax = new Vector2(0f, 1f);
            _progressBarFillRect.pivot = new Vector2(0f, 0.5f);
            _progressBarFillRect.offsetMin = Vector2.zero;
            _progressBarFillRect.offsetMax = Vector2.zero;
            fillGo.AddComponent<Image>().color = new Color(0.15f, 0.80f, 0.95f, 1f);
            SetProgress(0f);

            _constructorBtn = CreateButton(_bottomPanel.transform, "Btn_Constructor", "СБОРКА РОБОТА", new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-165f, -62f), new Vector2(290f, 64f), new Color(0.22f, 0.45f, 0.62f), 18);
            _constructorBtn.onClick.AddListener(ShowConstructor);

            CreateOrderButton(OrderType.Capture, "ЗАХВАТ", -375f);
            CreateOrderButton(OrderType.Hunt, "ОХОТА", -225f);
            CreateOrderButton(OrderType.Scout, "РАЗВЕДКА", -75f);
            CreateOrderButton(OrderType.Defend, "ОБОРОНА", 75f);
            CreateOrderButton(OrderType.Stop, "СТОП", 225f);
            CreateOrderButton(OrderType.Detonate, "ПОДРЫВ", 375f).GetComponent<Image>().color = new Color(0.55f, 0.35f, 0.05f);
            _programButton = CreateButton(_bottomPanel.transform, "EditProgram", "ПРОГРАММА РОБОТА", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 145f), new Vector2(320f, 50f), new Color(0.12f, 0.32f, 0.28f), 16);
            _programButton.onClick.AddListener(ShowProgramEditor);
            _programButton.gameObject.SetActive(false);
        }

        private void ShowRobotPanel(RobotState robot)
        {
            _shownRobot = robot;
            if (!_bottomPanel.activeSelf) _bottomPanel.SetActive(true);
            _panelTitleText.text = $"РОБОТ #{robot.Id.Value}: {DescribeBlueprint(robot.Blueprint)}";
            string controlHint = _isOrderMode
                ? "<color=#66CCFF>ПРИКАЗЫ: C — захват | H — охота | R — разведка | F — оборона | S — стоп" + (robot.Blueprint.HasNuclear ? " | N — подрыв" : "") + "</color>"
                : "<color=#FFFF00>РУЧНОЙ РЕЖИМ: WASD / стрелки — движение</color>";
            string programStatus = robot.CurrentOrder.Type == OrderType.Program
                ? $" | Программа: {(robot.ActiveProgramAction == ProgramAction.Defend ? "защита" : robot.ActiveProgramAction == ProgramAction.Attack ? "атака" : robot.ActiveProgramAction == ProgramAction.Capture ? "захват" : robot.ActiveProgramAction == ProgramAction.Explore ? "разведка" : robot.ActiveProgramAction == ProgramAction.Retreat ? "отступление" : "ожидание")}"
                : "";
            _queueStatusText.text = $"HP: <color=#00FF88><b>{robot.CurrentHealth}/{robot.MaxHealth}</b></color> | Скорость: {robot.MoveSpeed:F1} м/с{programStatus}\n{controlHint}";
            SetProgress(0f);
            _constructorBtn.gameObject.SetActive(false);
            bool own = robot.Owner == PlayerId.Player1;
            _programButton.gameObject.SetActive(own);
            _programButton.GetComponent<Image>().color = robot.CurrentOrder.Type == OrderType.Program ? new Color(0.15f, 0.65f, 0.45f) : new Color(0.12f, 0.32f, 0.28f);
            foreach (var pair in _orderButtons)
            {
                bool visible = own && (pair.Key != OrderType.Detonate || robot.Blueprint.HasNuclear);
                pair.Value.gameObject.SetActive(visible);
                if (pair.Key != OrderType.Detonate)
                {
                    pair.Value.GetComponent<Image>().color = pair.Key == robot.CurrentOrder.Type ? new Color(0.15f, 0.65f, 0.95f) : new Color(0.12f, 0.20f, 0.32f);
                }
            }
        }

        private void ShowBuildingPanel(BuildingState building)
        {
            _shownRobot = null;
            if (!_bottomPanel.activeSelf) _bottomPanel.SetActive(true);
            foreach (var button in _orderButtons.Values) button.gameObject.SetActive(false);
            _programButton.gameObject.SetActive(false);
            bool ownBase = building.Type == BuildingType.Base && building.Owner == PlayerId.Player1;
            _constructorBtn.gameObject.SetActive(ownBase);

            string owner = building.Owner == PlayerId.Player1 ? "ВАШ" : building.Owner == PlayerId.Player2 ? "ВРАЖЕСКИЙ" : "НЕЙТРАЛЬНЫЙ";
            string produces = $"Производит: <color=#66FF88>{DefaultContentCatalog.GetResourceName(EconomyRules.GetProducedResource(building))} +{EconomyRules.GetHourlyIncome(building)}/ч</color>";
            if (building.Type == BuildingType.Base)
            {
                _panelTitleText.text = building.Owner == PlayerId.Player1 ? "ВАША БАЗА — СБОРКА РОБОТОВ" : $"{owner} БАЗА #{building.Id.Value}";
            }
            else
            {
                _panelTitleText.text = $"{owner} ЗАВОД: {DefaultContentCatalog.GetResourceName(building.ProducedResource).ToUpperInvariant()}";
            }

            if (building.CaptureProgressSeconds > 0f || building.IsContested)
            {
                float capturePercent = Mathf.Clamp01(building.CaptureProgressSeconds / building.CaptureTimeTotal) * 100f;
                _queueStatusText.text = produces + "\n" + (building.IsContested
                    ? "<color=#FFD700>ЗАХВАТ ОСПАРИВАЕТСЯ</color>"
                    : $"<color=#66CCFF>Захват: {capturePercent:0}%</color> игроком {building.CapturingPlayer}");
                SetProgress(capturePercent / 100f);
                return;
            }

            if (!ownBase)
            {
                _queueStatusText.text = produces + (building.Owner == PlayerId.Player1 ? "" : "\nПодведите робота с приказом «Захват» в круг здания.");
                SetProgress(0f);
                return;
            }

            var queue = building.ProductionQueue;
            if (queue.Count == 0)
            {
                _queueStatusText.text = $"{produces}\nОчередь: 0/{BuildingState.MaxQueueSize}. Нажмите «Сборка робота».";
                SetProgress(0f);
            }
            else if (queue[0].Status == OrderProductionStatus.ExitBlocked)
            {
                _queueStatusText.text = "<color=yellow>ВЫХОД ЗАБЛОКИРОВАН — уведите робота с точки выхода.</color>";
                SetProgress(1f);
            }
            else
            {
                ProductionOrder first = queue[0];
                float progress = Mathf.Clamp01(1f - first.RemainingBuildTimeSeconds / Mathf.Max(0.01f, first.TotalBuildTimeSeconds));
                _queueStatusText.text = $"Сборка: <b>{DescribeBlueprint(first.Blueprint)}</b> (осталось {first.RemainingBuildTimeSeconds:F1} с)\nОчередь: {queue.Count}/{BuildingState.MaxQueueSize}";
                SetProgress(progress);
            }
        }

        private Button CreateOrderButton(OrderType order, string label, float x)
        {
            var button = CreateButton(_bottomPanel.transform, "Order_" + order, label, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(x, 50f), new Vector2(140f, 48f), new Color(0.12f, 0.20f, 0.32f), 14);
            button.onClick.AddListener(() => OnRobotOrderRequested?.Invoke(order));
            _orderButtons[order] = button;
            button.gameObject.SetActive(false);
            return button;
        }

        private void ShowProgramEditor()
        {
            if (_shownRobot == null || _shownRobot.Owner != PlayerId.Player1) return;
            if (_programOverlay == null) CreateProgramEditor();
            if (_constructorOverlay != null) _constructorOverlay.SetActive(false);
            _programDraft = ProgramEditorValues.FromRobot(_shownRobot);
            _programError.text = "";
            _programOverlay.SetActive(true);
            RefreshProgramEditor();
        }

        private void CreateProgramEditor()
        {
            _programOverlay = CreatePanel(_canvas.transform, "RobotProgramEditor", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, new Color(0f, 0f, 0f, 0.93f));
            var card = CreatePanel(_programOverlay.transform, "Card", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1040f, 730f), new Color(0.04f, 0.08f, 0.15f));
            Transform root = card.transform;
            CreateText(root, "Title", "ПРОГРАММА РОБОТА", new Vector2(35f, -25f), new Vector2(570f, 55f), 32, FontStyle.Bold, NameColor, TextAnchor.MiddleLeft);
            _programRetreatButton = CreateButton(root, "RetreatThreshold", "", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(790f, -50f), new Vector2(320f, 48f), new Color(0.42f, 0.23f, 0.12f), 15);
            _programRetreatButton.onClick.AddListener(() => { _programDraft.RetreatHealthPercent = (_programDraft.RetreatHealthPercent + 20) % 100; RefreshProgramEditor(); });
            CreateText(root, "Help", "Первая доступная операция выполняется. При низком HP отступление выше приоритетов. Радиус и минимум HP задают условия.", new Vector2(35f, -85f), new Vector2(620f, 65f), 17, FontStyle.Normal, Color.white, TextAnchor.UpperLeft);
            _programPresetSlotButton = CreateButton(root, "PresetSlot", "ШАБЛОН 1", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(710f, -110f), new Vector2(110f, 48f), new Color(0.16f, 0.30f, 0.4f), 14);
            _programPresetSlotButton.onClick.AddListener(() => { _programPresetSlot = (_programPresetSlot + 1) % 3; RefreshProgramEditor(); });
            var savePreset = CreateButton(root, "SavePreset", "СОХР.", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(830f, -110f), new Vector2(90f, 48f), new Color(0.08f, 0.42f, 0.23f), 14);
            savePreset.onClick.AddListener(SaveProgramPreset);
            var loadPreset = CreateButton(root, "LoadPreset", "ЗАГР.", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(935f, -110f), new Vector2(90f, 48f), new Color(0.16f, 0.38f, 0.5f), 14);
            loadPreset.onClick.AddListener(LoadProgramPreset);

            for (int index = 0; index < 3; index++)
            {
                int slot = index;
                var row = CreatePanel(root, "Rule_" + (index + 1), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(35f, -160f - index * 145f), new Vector2(970f, 130f), new Color(0.09f, 0.15f, 0.25f, 0.98f));
                _programRuleLabels[index] = CreateText(row.transform, "Name", "", new Vector2(15f, -12f), new Vector2(280f, 74f), 19, FontStyle.Bold, NameColor, TextAnchor.UpperLeft);
                _programRuleLabels[index].raycastTarget = false;
                _programHealthButtons[index] = CreateButton(row.transform, "MinHealth", "", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(160f, -103f), new Vector2(250f, 36f), new Color(0.2f, 0.3f, 0.4f), 14);
                _programHealthButtons[index].onClick.AddListener(() => CycleProgramHealth(slot));
                var up = CreateButton(row.transform, "MoveUp", "↑", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(340f, -62f), new Vector2(54f, 60f), new Color(0.16f, 0.38f, 0.5f), 24);
                up.onClick.AddListener(() => MoveProgramRule(slot, -1));
                var down = CreateButton(row.transform, "MoveDown", "↓", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(404f, -62f), new Vector2(54f, 60f), new Color(0.16f, 0.38f, 0.5f), 24);
                down.onClick.AddListener(() => MoveProgramRule(slot, 1));
                CreateText(row.transform, "RadiusTitle", "РАДИУС", new Vector2(465f, -12f), new Vector2(165f, 26f), 15, FontStyle.Bold, HeaderColor, TextAnchor.MiddleCenter);
                var minus = CreateButton(row.transform, "RadiusMinus", "−", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(485f, -79f), new Vector2(42f, 48f), new Color(0.23f, 0.28f, 0.34f), 22);
                minus.onClick.AddListener(() => ChangeProgramRadius(slot, -1));
                _programRadiusLabels[index] = CreateText(row.transform, "RadiusValue", "", new Vector2(513f, -54f), new Vector2(88f, 44f), 18, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
                _programRadiusLabels[index].raycastTarget = false;
                var plus = CreateButton(row.transform, "RadiusPlus", "+", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(615f, -79f), new Vector2(42f, 48f), new Color(0.23f, 0.28f, 0.34f), 22);
                plus.onClick.AddListener(() => ChangeProgramRadius(slot, 1));
                _programTargetButtons[index] = CreateButton(row.transform, "Target", "", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(790f, -66f), new Vector2(300f, 92f), new Color(0.2f, 0.3f, 0.4f), 16);
                _programTargetButtons[index].onClick.AddListener(() => CycleProgramTarget(slot));
                _programResourceButtons[index] = CreateButton(row.transform, "ResourceType", "", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(715f, -101f), new Vector2(145f, 42f), new Color(0.18f, 0.32f, 0.27f), 13);
                _programResourceButtons[index].onClick.AddListener(() => CycleCaptureResource());
                _programResourceLimitButtons[index] = CreateButton(row.transform, "ResourceLimit", "", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(865f, -101f), new Vector2(145f, 42f), new Color(0.18f, 0.32f, 0.27f), 13);
                _programResourceLimitButtons[index].onClick.AddListener(() => CycleCaptureResourceLimit());
            }

            _programError = CreateText(root, "Error", "", new Vector2(35f, -590f), new Vector2(970f, 40f), 17, FontStyle.Bold, new Color(1f, 0.45f, 0.35f), TextAnchor.MiddleCenter);
            var apply = CreateButton(root, "Apply", "СОХРАНИТЬ И ЗАПУСТИТЬ", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-160f, 45f), new Vector2(350f, 64f), new Color(0.08f, 0.42f, 0.23f), 19);
            apply.onClick.AddListener(() =>
            {
                string error = OnRobotProgramRequested?.Invoke(_programDraft) ?? "Не удалось передать программу роботу.";
                if (string.IsNullOrEmpty(error)) _programOverlay.SetActive(false);
                else _programError.text = error;
            });
            var cancel = CreateButton(root, "Cancel", "ОТМЕНА", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(215f, 45f), new Vector2(170f, 64f), new Color(0.35f, 0.14f, 0.17f), 18);
            cancel.onClick.AddListener(() => _programOverlay.SetActive(false));
            _programOverlay.SetActive(false);
        }

        private void RefreshProgramEditor()
        {
            if (_programDraft == null || _programState == null) return;
            _programPresetSlotButton.GetComponentInChildren<Text>().text = $"ШАБЛОН {_programPresetSlot + 1}";
            _programRetreatButton.GetComponentInChildren<Text>().text = _programDraft.RetreatHealthPercent == 0
                ? "ОТСТУПЛЕНИЕ: ВЫКЛ. ›" : $"ОТСТУПЛЕНИЕ: HP ≤ {_programDraft.RetreatHealthPercent}% ›";
            for (int slot = 0; slot < 3; slot++)
            {
                ProgramAction action = ProgramActionAt(slot);
                string name = action == ProgramAction.Defend ? "ЗАЩИТА" : action == ProgramAction.Attack ? "АТАКА" : "ЗАХВАТ";
                string condition = action == ProgramAction.Defend ? "Враг возле своего здания" : action == ProgramAction.Attack ? "Враг рядом с роботом" : "Чужое здание рядом";
                _programRuleLabels[slot].text = $"{slot + 1}. {name}\n<size=15>{condition}</size>";
                _programRadiusLabels[slot].text = $"{GetProgramRadius(action):0} м";
                _programHealthButtons[slot].GetComponentInChildren<Text>().text = $"HP ≥ {GetProgramMinHealth(action)}% ›";
                _programTargetButtons[slot].GetComponentInChildren<Text>().text = "ЦЕЛЬ\n" + DescribeProgramTarget(action, GetProgramTarget(action));
                bool isCapture = action == ProgramAction.Capture;
                RectTransform targetRect = (RectTransform)_programTargetButtons[slot].transform;
                targetRect.anchoredPosition = new Vector2(790f, isCapture ? -42f : -66f);
                targetRect.sizeDelta = new Vector2(300f, isCapture ? 44f : 92f);
                _programResourceButtons[slot].gameObject.SetActive(isCapture);
                _programResourceLimitButtons[slot].gameObject.SetActive(isCapture && _programDraft.CaptureResourceType != 0);
                _programResourceButtons[slot].GetComponentInChildren<Text>().text = _programDraft.CaptureResourceType == 0
                    ? "РЕСУРС: ЛЮБОЙ ›" : DefaultContentCatalog.GetResourceName((ResourceType)_programDraft.CaptureResourceType).ToUpperInvariant() + " ›";
                _programResourceLimitButtons[slot].GetComponentInChildren<Text>().text = $"ЗАПАС < {_programDraft.CaptureResourceBelow} ›";
            }
        }

        private void CycleCaptureResource()
        {
            _programDraft.CaptureResourceType = (_programDraft.CaptureResourceType + 1) % 7;
            _programDraft.CaptureResourceBelow = _programDraft.CaptureResourceType == 0 ? 0 : 5;
            RefreshProgramEditor();
        }

        private void CycleCaptureResourceLimit()
        {
            int current = _programDraft.CaptureResourceBelow;
            _programDraft.CaptureResourceBelow = current < 5 ? 5 : current < 10 ? 10 : current < 20 ? 20 : 3;
            RefreshProgramEditor();
        }

        private void SaveProgramPreset()
        {
            if (_programDraft == null) return;
            PlayerPrefs.SetString(ProgramPresetKey + _programPresetSlot, JsonUtility.ToJson(ProgramPreset.From(_programDraft)));
            PlayerPrefs.Save();
            _programError.text = $"Шаблон {_programPresetSlot + 1} сохранён без целей конкретного матча.";
        }

        private void LoadProgramPreset()
        {
            string key = ProgramPresetKey + _programPresetSlot;
            if (!PlayerPrefs.HasKey(key)) { _programError.text = "Этот шаблон ещё не сохранён."; return; }
            try
            {
                ProgramPreset preset = JsonUtility.FromJson<ProgramPreset>(PlayerPrefs.GetString(key));
                if (preset == null || preset.DefendPriority < 1 || preset.AttackPriority < 1 || preset.CapturePriority < 1 ||
                    preset.DefendPriority == preset.AttackPriority || preset.DefendPriority == preset.CapturePriority || preset.AttackPriority == preset.CapturePriority ||
                    preset.DefendRadius < 1f || preset.AttackRadius < 1f || preset.CaptureRadius < 1f ||
                    preset.RetreatHealthPercent < 0 || preset.RetreatHealthPercent > 100 ||
                    preset.CaptureResourceType < 0 || preset.CaptureResourceType > 6 ||
                    preset.CaptureResourceBelow < 0 || preset.CaptureResourceBelow > 100 ||
                    (preset.CaptureResourceType == 0) != (preset.CaptureResourceBelow == 0))
                { _programError.text = "Шаблон повреждён или устарел."; return; }
                preset.ApplyTo(_programDraft);
                _programError.text = $"Шаблон {_programPresetSlot + 1} загружен; примените его к роботу.";
                RefreshProgramEditor();
            }
            catch (Exception) { _programError.text = "Не удалось загрузить шаблон."; }
        }

        private ProgramAction ProgramActionAt(int slot)
        {
            int priority = slot + 1;
            if (_programDraft.DefendPriority == priority) return ProgramAction.Defend;
            return _programDraft.AttackPriority == priority ? ProgramAction.Attack : ProgramAction.Capture;
        }

        private void MoveProgramRule(int slot, int direction)
        {
            int other = slot + direction;
            if (other < 0 || other > 2) return;
            ProgramAction first = ProgramActionAt(slot), second = ProgramActionAt(other);
            SetProgramPriority(first, other + 1);
            SetProgramPriority(second, slot + 1);
            RefreshProgramEditor();
        }

        private void SetProgramPriority(ProgramAction action, int value)
        {
            if (action == ProgramAction.Defend) _programDraft.DefendPriority = value;
            else if (action == ProgramAction.Attack) _programDraft.AttackPriority = value;
            else _programDraft.CapturePriority = value;
        }

        private float GetProgramRadius(ProgramAction action) => action == ProgramAction.Defend ? _programDraft.DefendRadius : action == ProgramAction.Attack ? _programDraft.AttackRadius : _programDraft.CaptureRadius;

        private int GetProgramMinHealth(ProgramAction action) => action == ProgramAction.Defend ? _programDraft.DefendMinHealthPercent : action == ProgramAction.Attack ? _programDraft.AttackMinHealthPercent : _programDraft.CaptureMinHealthPercent;

        private void CycleProgramHealth(int slot)
        {
            ProgramAction action = ProgramActionAt(slot);
            int next = (GetProgramMinHealth(action) + 25) % 125;
            if (action == ProgramAction.Defend) _programDraft.DefendMinHealthPercent = next;
            else if (action == ProgramAction.Attack) _programDraft.AttackMinHealthPercent = next;
            else _programDraft.CaptureMinHealthPercent = next;
            RefreshProgramEditor();
        }

        private void ChangeProgramRadius(int slot, int direction)
        {
            ProgramAction action = ProgramActionAt(slot);
            float step = action == ProgramAction.Capture ? 10f : 1f;
            float value = Mathf.Clamp(GetProgramRadius(action) + direction * step, 1f, 256f);
            if (action == ProgramAction.Defend) _programDraft.DefendRadius = value;
            else if (action == ProgramAction.Attack) _programDraft.AttackRadius = value;
            else _programDraft.CaptureRadius = value;
            RefreshProgramEditor();
        }

        private EntityId GetProgramTarget(ProgramAction action) => action == ProgramAction.Defend ? _programDraft.DefendTargetId : action == ProgramAction.Attack ? _programDraft.AttackTargetId : _programDraft.CaptureTargetId;

        private void CycleProgramTarget(int slot)
        {
            ProgramAction action = ProgramActionAt(slot);
            var options = new List<int> { 0 };
            if (action == ProgramAction.Attack)
            {
                foreach (RobotState robot in _programState.Robots.Values)
                    if (robot.IsAlive && robot.Owner != PlayerId.Player1 &&
                        (_programState.FogOfWar == null || _programState.FogOfWar.IsVisible(PlayerId.Player1, robot.Position))) options.Add(robot.Id.Value);
            }
            else
            {
                foreach (BuildingState building in _programState.Buildings.Values)
                    if (action == ProgramAction.Defend ? building.Owner == PlayerId.Player1 :
                        building.Owner != PlayerId.Player1 && (_programState.FogOfWar == null || _programState.FogOfWar.IsExplored(PlayerId.Player1, building.Position))) options.Add(building.Id.Value);
            }
            options.Sort();
            int currentIndex = options.IndexOf(GetProgramTarget(action).Value);
            EntityId next = new EntityId(options[(currentIndex + 1) % options.Count]);
            if (action == ProgramAction.Defend) _programDraft.DefendTargetId = next;
            else if (action == ProgramAction.Attack) _programDraft.AttackTargetId = next;
            else _programDraft.CaptureTargetId = next;
            RefreshProgramEditor();
        }

        private string DescribeProgramTarget(ProgramAction action, EntityId id)
        {
            if (!id.IsValid) return action == ProgramAction.Capture ? "ближайшее здание ›" : "любая ›";
            if (action == ProgramAction.Attack) return _programState.Robots.ContainsKey(id.Value) ? $"робот #{id.Value} ›" : $"нет робота #{id.Value} ›";
            return _programState.Buildings.TryGetValue(id.Value, out BuildingState building)
                ? $"{(building.Type == BuildingType.Base ? "база" : "завод")} #{id.Value} ›"
                : $"нет здания #{id.Value} ›";
        }

        private void SetProgress(float value)
        {
            if (_progressBarFillRect != null) _progressBarFillRect.anchorMax = new Vector2(Mathf.Clamp01(value), 1f);
        }

        // ───────────────────────── Экран сборки ─────────────────────────

        private void ShowConstructor()
        {
            if (_playerBase == null) return;
            if (_programOverlay != null) _programOverlay.SetActive(false);
            if (_constructorOverlay == null) CreateConstructorOverlay();
            _constructorDraft.Clear();
            _constructorOverlay.SetActive(true);
            RefreshConstructor();
        }

        private void CreateConstructorOverlay()
        {
            _constructorOverlay = CreatePanel(_canvas.transform, "RobotConstruction", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, new Color(0f, 0f, 0f, 0.97f));
            var card = CreatePanel(_constructorOverlay.transform, "Card", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1400f, 900f), Color.black);
            Transform root = card.transform;

            // Левая половина: заголовок, доступные ресурсы, миниатюра и кнопки.
            CreateText(root, "Title", "СБОРКА\nРОБОТА", new Vector2(40f, -30f), new Vector2(600f, 140f), 52, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
            CreateText(root, "ResourcesHeader", "— ДОСТУПНЫЕ РЕСУРСЫ —", new Vector2(40f, -185f), new Vector2(600f, 40f), 26, FontStyle.Bold, HeaderColor, TextAnchor.MiddleCenter);
            _constructorResources = CreateText(root, "Resources", "", new Vector2(60f, -235f), new Vector2(560f, 330f), 24, FontStyle.Bold, StockColor, TextAnchor.UpperLeft);
            _constructorResources.lineSpacing = 1.1f;

            _constructorPreview = CreatePanel(root, "Preview", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -585f), new Vector2(230f, 270f), new Color(0.05f, 0.07f, 0.1f)).transform;
            _constructorName = CreateText(root, "RobotName", "", new Vector2(290f, -585f), new Vector2(350f, 120f), 20, FontStyle.Bold, NameColor, TextAnchor.UpperLeft);
            _constructorSummary = CreateText(root, "Summary", "", new Vector2(290f, -705f), new Vector2(350f, 80f), 17, FontStyle.Normal, Color.white, TextAnchor.UpperLeft);

            var exit = CreateButton(root, "Exit", "ВЫХОД", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(380f, -830f), new Vector2(160f, 64f), new Color(0.2f, 0.24f, 0.3f), 22);
            exit.onClick.AddListener(() => _constructorOverlay.SetActive(false));
            _constructorProduce = CreateButton(root, "Start", "СОБРАТЬ\nРОБОТА", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(560f, -830f), new Vector2(180f, 64f), new Color(0.1f, 0.5f, 0.3f), 20);
            _constructorProduce.onClick.AddListener(ProduceFromConstructor);

            // Правая половина: детали с ценой, как в оригинальном экране.
            int row = 0;
            foreach (ModuleDefinition module in _constructorDraft.GetModulesInDisplayOrder())
            {
                string moduleId = module.Id;
                float y = -40f - row * 104f;
                var rowButton = CreateButton(root, "Part_" + moduleId, "", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(1040f, y - 48f), new Vector2(660f, 96f), new Color(0.04f, 0.05f, 0.08f), 14);
                rowButton.onClick.AddListener(() => { _constructorDraft.Toggle(moduleId); RefreshConstructor(); });
                _partRowBackgrounds[moduleId] = rowButton.GetComponent<Image>();
                var icon = CreatePanel(rowButton.transform, "Icon", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(16f, 0f), new Vector2(76f, 76f), GetModuleColor(moduleId));
                icon.GetComponent<Image>().raycastTarget = false;
                var glyph = CreateText(icon.transform, "Glyph", GetModuleGlyph(module), new Vector2(0f, 0f), new Vector2(76f, 76f), 26, FontStyle.Bold, Color.black, TextAnchor.MiddleCenter);
                glyph.raycastTarget = false;
                CreateText(rowButton.transform, "Name", module.ShortName, new Vector2(116f, -4f), new Vector2(400f, 36f), 28, FontStyle.Bold, NameColor, TextAnchor.MiddleLeft).raycastTarget = false;
                CreateText(rowButton.transform, "Role", module.Role, new Vector2(116f, -38f), new Vector2(420f, 22f), 15, FontStyle.Normal, new Color(0.7f, 0.75f, 0.8f), TextAnchor.MiddleLeft).raycastTarget = false;
                CreateText(rowButton.transform, "Price", $"{module.Cost}  <size=16><color=#8090A0>{DefaultContentCatalog.GetResourceName(module.Resource).ToLowerInvariant()}</color></size>", new Vector2(116f, -60f), new Vector2(400f, 32f), 24, FontStyle.Bold, PriceColor, TextAnchor.MiddleLeft).raycastTarget = false;
                _partRowMarks[moduleId] = CreateText(rowButton.transform, "Mark", "", new Vector2(520f, -8f), new Vector2(130f, 80f), 34, FontStyle.Bold, StockColor, TextAnchor.MiddleRight);
                _partRowMarks[moduleId].raycastTarget = false;
                row++;
            }
            _constructorOverlay.SetActive(false);
        }

        private void RefreshConstructor()
        {
            if (_constructorOverlay == null || _player == null) return;
            AssemblyValidationResult validation = _constructorDraft.Validate();
            bool hasAnyPart = _constructorDraft.ChassisId != null || _constructorDraft.WeaponIds.Count > 0 || _constructorDraft.ElectronicsId != null || _constructorDraft.NuclearId != null;
            bool affordable = _player.Resources.TryPlanPayment(validation.CostByType, out int[] payment);

            // Слева — запасы; при выбранных деталях рядом видно, сколько спишется.
            string text = "";
            foreach (ResourceType type in AllResourceTypes)
            {
                int stock = _player.Resources[type];
                int spend = payment[(int)type];
                string spendText = !hasAnyPart || spend == 0 ? "" : type == ResourceType.General && !affordable ? $"  <color=#FF5555>−{spend}</color>" : $"  <color=#FF77EE>−{spend}</color>";
                text += $"{DefaultContentCatalog.GetResourceName(type).ToUpperInvariant(),-14}{stock,4}{spendText}\n";
            }
            text += $"\n<color=#FFFFFF>ИТОГО {_player.Resources.Total,10}</color>";
            _constructorResources.text = text;

            foreach (var pair in _partRowBackgrounds)
            {
                bool selected = _constructorDraft.IsSelected(pair.Key);
                pair.Value.color = selected ? new Color(0.08f, 0.3f, 0.38f) : new Color(0.04f, 0.05f, 0.08f);
                _partRowMarks[pair.Key].text = selected ? "✓" : "";
            }

            RefreshConstructorPreview();
            _constructorName.text = hasAnyPart ? _constructorDraft.DescribeSelection() : "Выберите детали справа";

            bool queueOpen = _playerBase != null && _playerBase.ProductionQueue.Count < BuildingState.MaxQueueSize;
            bool canProduce = validation.IsValid && affordable && _player.CanBuildMoreRobots && queueOpen;
            _constructorProduce.interactable = canProduce;
            if (!hasAnyPart) _constructorSummary.text = "Шасси обязательно, плюс оружие или ядерный заряд.";
            else if (!validation.IsValid) _constructorSummary.text = "<color=#FF8888>" + string.Join("\n", validation.Errors) + "</color>";
            else if (!affordable) _constructorSummary.text = $"<color=#FF8888>Не хватает {payment[(int)ResourceType.General] - _player.Resources[ResourceType.General]} общих ресурсов.</color>";
            else if (!_player.CanBuildMoreRobots) _constructorSummary.text = "<color=#FF8888>Достигнут лимит армии.</color>";
            else if (!queueOpen) _constructorSummary.text = "<color=#FF8888>Очередь базы заполнена.</color>";
            else _constructorSummary.text = $"Цена {validation.TotalCost} • HP {validation.TotalHealth} • скорость {validation.Speed:F1}\n{DescribeMobility(validation.Mobility)}";
        }

        private void ProduceFromConstructor()
        {
            RefreshConstructor();
            if (!_constructorProduce.interactable) return;
            OnOrderBlueprintRequested?.Invoke(_constructorDraft.CreateBlueprint("custom", _constructorDraft.DescribeSelection()));
            _constructorOverlay.SetActive(false);
        }

        /// <summary>Миниатюра робота сбоку: детали стоят стопкой, как модель на поле боя.</summary>
        private void RefreshConstructorPreview()
        {
            foreach (Transform child in _constructorPreview) Destroy(child.gameObject);
            float y = 24f;
            string chassis = _constructorDraft.ChassisId;
            if (chassis == DefaultContentCatalog.ChassisBipod)
            {
                PreviewPart("LegL", -30f, y, 18f, 46f, Color.gray);
                PreviewPart("LegR", 30f, y, 18f, 46f, Color.gray);
                y += 46f;
            }
            else if (chassis == DefaultContentCatalog.ChassisTracks)
            {
                PreviewPart("Tracks", 0f, y, 150f, 34f, new Color(0.35f, 0.35f, 0.38f));
                y += 34f;
            }
            else if (chassis == DefaultContentCatalog.ChassisAntiGrav)
            {
                PreviewPart("Emitter", 0f, y + 8f, 150f, 8f, new Color(0.7f, 0.85f, 1f));
                y += 28f;
            }
            if (chassis != null)
            {
                PreviewPart("Deck", 0f, y, 120f, 18f, HumanColor);
                y += 18f;
            }
            foreach (string weaponId in _constructorDraft.WeaponIds)
            {
                PreviewPart("Weapon_" + weaponId, 0f, y + 2f, 104f, 30f, GetModuleColor(weaponId));
                PreviewPart("Barrel_" + weaponId, 66f, y + 12f, 34f, 9f, GetModuleColor(weaponId) * 0.7f);
                y += 34f;
            }
            if (_constructorDraft.ElectronicsId != null)
            {
                PreviewPart("Electronics", 0f, y + 2f, 70f, 24f, GetModuleColor(DefaultContentCatalog.Electronics));
                y += 28f;
            }
            if (_constructorDraft.NuclearId != null)
            {
                PreviewPart("Nuclear", 0f, y + 2f, 40f, 36f, GetModuleColor(DefaultContentCatalog.Nuclear));
            }
        }

        private void PreviewPart(string name, float x, float bottom, float width, float height, Color color)
        {
            var part = CreatePanel(_constructorPreview, name, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(x, bottom), new Vector2(width, height), color);
            part.GetComponent<Image>().raycastTarget = false;
        }

        private string DescribeBlueprint(RobotBlueprint blueprint)
        {
            if (blueprint == null) return "—";
            var parts = new List<string>();
            foreach (string moduleId in blueprint.GetAllModuleIds())
            {
                if (_moduleCatalog.TryGetValue(moduleId, out ModuleDefinition module)) parts.Add(module.ShortName);
            }
            return string.Join(" + ", parts);
        }

        private static string DescribeMobility(MobilityClass mobility) => mobility switch
        {
            MobilityClass.Hover => "Летит над водой, скалами и трещинами",
            MobilityClass.Tracked => "Проходит неровности, обходит трещины и воду",
            _ => "Только ровная земля: обходит неровности, трещины и воду"
        };

        private static Color GetModuleColor(string moduleId) => moduleId switch
        {
            DefaultContentCatalog.Electronics => new Color(0.92f, 0.92f, 0.92f),
            DefaultContentCatalog.Nuclear => new Color(0.95f, 0.8f, 0.1f),
            DefaultContentCatalog.WeaponPhasers => new Color(0.3f, 0.95f, 1f),
            DefaultContentCatalog.WeaponMissiles => new Color(1f, 0.45f, 0.15f),
            DefaultContentCatalog.WeaponCannon => new Color(0.85f, 0.75f, 0.3f),
            _ => new Color(0.6f, 0.62f, 0.66f)
        };

        private static string GetModuleGlyph(ModuleDefinition module) => module.Id switch
        {
            DefaultContentCatalog.Electronics => "Э",
            DefaultContentCatalog.Nuclear => "☢",
            DefaultContentCatalog.WeaponPhasers => "Ф",
            DefaultContentCatalog.WeaponMissiles => "Р",
            DefaultContentCatalog.WeaponCannon => "П",
            DefaultContentCatalog.ChassisAntiGrav => "А",
            DefaultContentCatalog.ChassisTracks => "Г",
            _ => "Н"
        };

        private static BuildingState FindBase(MatchState state, PlayerId owner)
        {
            foreach (BuildingState building in state.Buildings.Values)
            {
                if (building.Type == BuildingType.Base && building.Owner == owner) return building;
            }
            return null;
        }

        // ───────────────────────── Миникарта и итог ─────────────────────────

        private void CreateMinimap()
        {
            var panel = CreatePanel(_canvas.transform, "Minimap", Vector2.zero, Vector2.zero, Vector2.zero,
                new Vector2(20f, 20f), new Vector2(300f, 200f), PanelColor);
            CreateLabel(panel.transform, "Title", "РАДАР", new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(8f, -6f), new Vector2(-8f, -27f), 13, FontStyle.Bold, NameColor, TextAnchor.MiddleCenter);
            var mapArea = CreatePanel(panel.transform, "Map", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -12f), new Vector2(-12f, -38f), new Color(0.05f, 0.12f, 0.3f, 1f));
            _minimapContent = mapArea.GetComponent<RectTransform>();
            var fogGo = new GameObject("FogOfWar", typeof(RectTransform));
            fogGo.transform.SetParent(mapArea.transform, false);
            var fogRect = (RectTransform)fogGo.transform;
            fogRect.anchorMin = Vector2.zero;
            fogRect.anchorMax = Vector2.one;
            fogRect.offsetMin = Vector2.zero;
            fogRect.offsetMax = Vector2.zero;
            _minimapFog = fogGo.AddComponent<RawImage>();
            _minimapFog.raycastTarget = false;
            // Нажатие по радару переносит камеру в эту точку карты; перетаскивание ведёт её следом.
            mapArea.AddComponent<MinimapInput>().OnPointRequested += normalized =>
                OnMinimapPointRequested?.Invoke(new SimVector2(normalized.x * _mapDefinition.Width, normalized.y * _mapDefinition.Height));
        }

        public void SetFogTexture(Texture2D texture)
        {
            if (_minimapFog != null) _minimapFog.texture = texture;
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
            image.raycastTarget = false;
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

        // ───────────────────────── Построение UGUI ─────────────────────────

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

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            canvasGo.AddComponent<GraphicRaycaster>();
        }

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

        /// <summary>Текст с фиксированным прямоугольником от левого верхнего угла родителя.</summary>
        private Text CreateText(Transform parent, string name, string text, Vector2 topLeft, Vector2 size, int fontSize, FontStyle style, Color color, TextAnchor alignment)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = topLeft;
            rt.sizeDelta = size;
            return ConfigureText(go.AddComponent<Text>(), text, fontSize, style, color, alignment);
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
            return ConfigureText(go.AddComponent<Text>(), text, fontSize, style, color, alignment);
        }

        private Text ConfigureText(Text txt, string text, int fontSize, FontStyle style, Color color, TextAnchor alignment)
        {
            txt.font = _uiFont;
            txt.text = text;
            txt.fontSize = fontSize;
            txt.fontStyle = style;
            txt.color = color;
            txt.alignment = alignment;
            txt.supportRichText = true;
            txt.horizontalOverflow = HorizontalWrapMode.Wrap;
            txt.verticalOverflow = VerticalWrapMode.Overflow;
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

            // Цвет задаёт Image, кнопка лишь подсвечивает его: так выделение строк и приказов меняется одной строкой.
            var img = go.AddComponent<Image>();
            img.color = normalColor;

            var btn = go.AddComponent<Button>();
            var colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(0.45f, 0.45f, 0.5f, 0.8f);
            colors.colorMultiplier = 1.5f;
            btn.colors = colors;

            var txtGo = new GameObject("Text");
            txtGo.transform.SetParent(go.transform, false);
            var txtRt = txtGo.AddComponent<RectTransform>();
            txtRt.anchorMin = Vector2.zero;
            txtRt.anchorMax = Vector2.one;
            txtRt.offsetMin = Vector2.zero;
            txtRt.offsetMax = Vector2.zero;
            var txt = ConfigureText(txtGo.AddComponent<Text>(), labelText, fontSize, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
            txt.raycastTarget = false;
            return btn;
        }
    }
}
