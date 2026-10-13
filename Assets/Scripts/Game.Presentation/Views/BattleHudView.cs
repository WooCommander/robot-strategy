using System;
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
        public event Action<string, string, string, string, string> OnOrderPresetRequested;
        public event Action OnSelectPlayerBaseRequested;

        private Font _uiFont;
        private bool _isOrderMode;

        public void Initialize()
        {
            EnsureFont();
            EnsureEventSystem();
            CreateCanvas();
            CreateTopBar();
            CreateBottomPanel();
        }

        public void SetOrderMode(bool isOrderMode)
        {
            _isOrderMode = isOrderMode;
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
            _queueStatusText = CreateLabel(_bottomPanel.transform, "QueueStatus", "Очередь: 0/3 (Свободно — выберите робота для заказа)",
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
            _scoutBtn = CreateButton(_bottomPanel.transform, "Btn_Scout", "<b>РАЗВЕДЧИК</b>\n<color=#FFD700>350 М</color> • 8 сек\n<size=12>Ноги + Пушка</size>",
                new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(160f, btnY), new Vector2(btnW, btnH),
                new Color(0.12f, 0.32f, 0.54f, 0.98f), 15);
            _scoutBtnText = _scoutBtn.GetComponentInChildren<Text>();
            _scoutBtn.onClick.AddListener(() =>
            {
                OnOrderPresetRequested?.Invoke("Разведчик", DefaultContentCatalog.ChassisBiped, DefaultContentCatalog.BodyLight, DefaultContentCatalog.WeaponCannon, null);
            });

            // 2. Тяжёлый танк (570 М, 12с)
            _tankBtn = CreateButton(_bottomPanel.transform, "Btn_Tank", "<b>ТЯЖЁЛЫЙ ТАНК</b>\n<color=#FFD700>570 М</color> • 12 сек\n<size=12>Гусеницы + Броня</size>",
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, btnY), new Vector2(btnW, btnH),
                new Color(0.12f, 0.32f, 0.54f, 0.98f), 15);
            _tankBtnText = _tankBtn.GetComponentInChildren<Text>();
            _tankBtn.onClick.AddListener(() =>
            {
                OnOrderPresetRequested?.Invoke("Танк", DefaultContentCatalog.ChassisTracks, DefaultContentCatalog.BodyHeavy, DefaultContentCatalog.WeaponCannon, DefaultContentCatalog.EquipArmor);
            });

            // 3. Ракетчик (440 М, 10с)
            _missileBtn = CreateButton(_bottomPanel.transform, "Btn_Missile", "<b>РАКЕТЧИК</b>\n<color=#FFD700>440 М</color> • 10 сек\n<size=12>Гусеницы + Ракеты</size>",
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-160f, btnY), new Vector2(btnW, btnH),
                new Color(0.12f, 0.32f, 0.54f, 0.98f), 15);
            _missileBtnText = _missileBtn.GetComponentInChildren<Text>();
            _missileBtn.onClick.AddListener(() =>
            {
                OnOrderPresetRequested?.Invoke("Ракетчик", DefaultContentCatalog.ChassisTracks, DefaultContentCatalog.BodyLight, DefaultContentCatalog.WeaponMissiles, null);
            });
        }

        public void UpdateState(PlayerState player, BuildingState activeBuilding, RobotState selectedRobot)
        {
            if (player != null)
            {
                _metalText.text = $"💰 Металл: <color=#FFD700><b>{player.Metal}</b></color> (+5/с База)";
                _armyText.text = $"🤖 Армия: <b>{player.TotalArmyCount} / {PlayerState.MaxArmyLimit}</b> (В бою: {player.ActiveRobotsCount})";

                UpdateButtonsAffordability(player.Metal);
            }

            if (selectedRobot != null)
            {
                if (!_bottomPanel.activeSelf) _bottomPanel.SetActive(true);
                _panelTitleText.text = $"⚔ <b>РОБОТ #{selectedRobot.Id.Value}: {selectedRobot.Blueprint.Name}</b> ⚔";
                string controlHint = _isOrderMode
                    ? "<color=#66CCFF>РЕЖИМ ПРИКАЗОВ: клик по земле — движение, по врагу — атака</color>"
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
                            _queueStatusText.text = $"Сборка: <b>{first.Blueprint.Name}</b> (осталось {rem:F1}с) | Очередь: {q.Count}/3";
                            SetProgress(progress);
                        }
                    }
                    else
                    {
                        _queueStatusText.text = "Очередь: <b>0/3 (Свободно — нажмите кнопку ниже для сборки)</b>";
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
            SetButtonState(_scoutBtn, _scoutBtnText, "РАЗВЕДЧИК", 350, 8, "Ноги + Пушка", metal >= 350);
            SetButtonState(_tankBtn, _tankBtnText, "ТЯЖЁЛЫЙ ТАНК", 570, 12, "Гусеницы + Броня", metal >= 570);
            SetButtonState(_missileBtn, _missileBtnText, "РАКЕТЧИК", 440, 10, "Гусеницы + Ракеты", metal >= 440);
        }

        private void SetButtonState(Button btn, Text txt, string name, int cost, int time, string modules, bool canAfford)
        {
            if (btn == null || txt == null) return;
            btn.interactable = canAfford;
            string costColor = canAfford ? "#FFD700" : "#FF6666";
            txt.text = $"<b>{name}</b>\n<color={costColor}>{cost} М</color> • {time} сек\n<size=12>{modules}</size>";
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
