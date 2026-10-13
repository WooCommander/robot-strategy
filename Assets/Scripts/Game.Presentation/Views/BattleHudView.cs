using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using Game.Content.Definitions;
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

        private Button _scoutBtn;
        private Text _scoutBtnText;
        private Button _tankBtn;
        private Text _tankBtnText;
        private Button _missileBtn;
        private Text _missileBtnText;

        // Делегат заказа робота
        public event Action<RobotPresetDefinition> OnOrderPresetRequested;
        public event Action OnSelectPlayerBaseRequested;

        private Font _uiFont;
        private bool _isOrderMode;
        private IReadOnlyDictionary<string, ModuleDefinition> _moduleCatalog;
        private MapDefinition _mapDefinition;
        private RectTransform _minimapContent;
        private readonly Dictionary<int, Image> _minimapBuildingMarkers = new Dictionary<int, Image>();
        private readonly Dictionary<int, Image> _minimapRobotMarkers = new Dictionary<int, Image>();

        public void Initialize(IReadOnlyDictionary<string, ModuleDefinition> moduleCatalog, MapDefinition mapDefinition)
        {
            _moduleCatalog = moduleCatalog;
            _mapDefinition = mapDefinition;
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
            float btnW = 280f;
            float btnH = 110f;
            float btnY = 65f;

            // 1. Разведчик (350 М, 8с)
            _scoutBtn = CreatePresetButton("Btn_Scout", RobotPresetCatalog.Scout,
                new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(160f, btnY), new Vector2(btnW, btnH),
                new Color(0.12f, 0.32f, 0.54f, 0.98f));
            _scoutBtnText = _scoutBtn.GetComponentInChildren<Text>();

            // 2. Тяжёлый танк (570 М, 12с)
            _tankBtn = CreatePresetButton("Btn_Tank", RobotPresetCatalog.Tank,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, btnY), new Vector2(btnW, btnH),
                new Color(0.12f, 0.32f, 0.54f, 0.98f));
            _tankBtnText = _tankBtn.GetComponentInChildren<Text>();

            // 3. Ракетчик (440 М, 10с)
            _missileBtn = CreatePresetButton("Btn_Missile", RobotPresetCatalog.Missile,
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-160f, btnY), new Vector2(btnW, btnH),
                new Color(0.12f, 0.32f, 0.54f, 0.98f));
            _missileBtnText = _missileBtn.GetComponentInChildren<Text>();
        }

        public void UpdateState(PlayerState player, BuildingState activeBuilding, RobotState selectedRobot)
        {
            if (player != null)
            {
                _metalText.text = $"💰 Металл: <color=#FFD700><b>{player.Metal}</b></color> (<color=#00FF88>+{player.MetalIncomePerSecond}/с</color>)";
                _armyText.text = $"🤖 Армия: <b>{player.TotalArmyCount} / {PlayerState.MaxArmyLimit}</b> (В бою: {player.ActiveRobotsCount})";

                UpdateButtonsAffordability(player.Metal);
            }

            if (selectedRobot != null)
            {
                if (!_bottomPanel.activeSelf) _bottomPanel.SetActive(true);
                _panelTitleText.text = $"⚔ <b>РОБОТ #{selectedRobot.Id.Value}: {selectedRobot.Blueprint.Name}</b> ⚔";
                string controlHint = _isOrderMode
                    ? "<color=#66CCFF>ПРИКАЗЫ: клик по врагу — атака | C — автозахват | H — охота | R — разведка | S — стоп</color>"
                    : "<color=#FFFF00>РУЧНОЙ РЕЖИМ: WASD / стрелки — движение</color>";
                _queueStatusText.text = $"HP: <color=#00FF88><b>{selectedRobot.CurrentHealth}/{selectedRobot.MaxHealth}</b></color> | Скорость: {selectedRobot.MoveSpeed:F1} м/с | {controlHint}";
                SetProgress(0f);
                SetProductionButtonsVisible(false);
            }
            else if (activeBuilding != null)
            {
                if (!_bottomPanel.activeSelf) _bottomPanel.SetActive(true);
                SetProductionButtonsVisible(true);

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
                    _queueStatusText.text = $"Доход объекта: +{activeBuilding.MetalIncomePerSecond} М/сек. Подведите боевых роботов в круг захвата!";
                    SetProgress(0f);
                    SetProductionButtonsVisible(false);
                }
            }
            else
            {
                if (_bottomPanel.activeSelf) _bottomPanel.SetActive(false);
            }
        }

        private void SetProductionButtonsVisible(bool visible)
        {
            if (_scoutBtn != null) _scoutBtn.gameObject.SetActive(visible);
            if (_tankBtn != null) _tankBtn.gameObject.SetActive(visible);
            if (_missileBtn != null) _missileBtn.gameObject.SetActive(visible);
        }

        private void SetProgress(float value)
        {
            if (_progressBarFillRect == null) return;

            _progressBarFillRect.anchorMax = new Vector2(Mathf.Clamp01(value), 1f);
        }

        private void UpdateButtonsAffordability(int metal)
        {
            SetPresetButtonState(_scoutBtn, _scoutBtnText, RobotPresetCatalog.Scout, metal);
            SetPresetButtonState(_tankBtn, _tankBtnText, RobotPresetCatalog.Tank, metal);
            SetPresetButtonState(_missileBtn, _missileBtnText, RobotPresetCatalog.Missile, metal);
        }

        private Button CreatePresetButton(string name, RobotPresetDefinition preset, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos, Vector2 sizeDelta, Color color)
        {
            var button = CreateButton(_bottomPanel.transform, name, FormatPresetText(preset, true), anchorMin, anchorMax, anchoredPos, sizeDelta, color, 15);
            button.onClick.AddListener(() => OnOrderPresetRequested?.Invoke(preset));
            return button;
        }

        private void SetPresetButtonState(Button btn, Text txt, RobotPresetDefinition preset, int metal)
        {
            if (btn == null || txt == null) return;
            int cost = preset.GetCost(_moduleCatalog);
            bool canAfford = metal >= cost;
            btn.interactable = canAfford;
            string costColor = canAfford ? "#FFD700" : "#FF6666";
            txt.text = $"<b>{preset.CardTitle}</b>\n<color={costColor}>{cost} М</color> • {preset.BuildTimeSeconds:0} сек\n<size=12>{preset.ModulesLabel}</size>";
        }

        private string FormatPresetText(RobotPresetDefinition preset, bool canAfford)
        {
            int cost = preset.GetCost(_moduleCatalog);
            string costColor = canAfford ? "#FFD700" : "#FF6666";
            return $"<b>{preset.CardTitle}</b>\n<color={costColor}>{cost} М</color> • {preset.BuildTimeSeconds:0} сек\n<size=12>{preset.ModulesLabel}</size>";
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
