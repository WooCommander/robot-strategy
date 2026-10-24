using UnityEngine;
using Game.Core.Models;
using Game.Core.Types;
using Game.Presentation.Rendering;
using Game.Presentation.Robots;

namespace Game.Presentation.Views
{
    public class RobotView : MonoBehaviour
    {
        [Header("State")]
        [SerializeField] private int _entityId;
        [SerializeField] private PlayerId _owner;

        [Header("Components")]
        [SerializeField] private MeshRenderer _selectionRing;
        [SerializeField] private LineRenderer _healthBarBg;
        [SerializeField] private LineRenderer _healthBarFill;
        private Transform _healthBarRoot;
        private RobotModelRig _rig;
        private TextMesh _healthLabel;

        private RobotState _state;
        private bool _isSelected;
        private Vector3 _previousSimPosition;
        private Vector3 _currentSimPosition;
        private System.Func<SimVector2, float> _surfaceHeight;
        private float _visualHeight;

        public int EntityId => _entityId;
        public PlayerId Owner => _owner;
        public bool IsSelected => _isSelected;
        public RobotState State => _state;

        public void Initialize(RobotState state)
        {
            _state = state;
            _entityId = state.Id.Value;
            _owner = state.Owner;

            _previousSimPosition = _currentSimPosition = ToWorld(state.Position);
            transform.position = _currentSimPosition;
            transform.rotation = Quaternion.Euler(0f, state.RotationDegrees, 0f);

            // Собираем визуальную модель робота
            var visualModel = ModularRobotBuilder.BuildRobotModel(state.Blueprint, state.Owner);
            visualModel.transform.SetParent(transform, false);
            _rig = visualModel.GetComponent<RobotModelRig>();
            float modelHeight = Mathf.Max(1.2f, _rig != null ? _rig.Height : 1.5f);

            // Коллайдер на корне робота для надёжного выбора лучами — по высоте модели.
            var col = GetComponent<CapsuleCollider>();
            if (col == null) col = gameObject.AddComponent<CapsuleCollider>();
            col.center = new Vector3(0f, modelHeight * 0.5f, 0f);
            col.radius = 0.8f;
            col.height = modelHeight;

            CreateSelectionRing();
            CreateHealthBar(modelHeight + 0.35f);
            // Гусеницы поднимают пыль сильнее, шагающие — слабее, антиграв не касается земли.
            if (state.Mobility != MobilityClass.Hover)
                FxLibrary.CreateDustTrail(transform, state.Mobility == MobilityClass.Tracked ? 5f : 2.5f);
            SetSelected(false);
            UpdateHealthVisual();
        }

        /// <summary>Светящееся кольцо на земле: голубое у своих, красное у чужих (HDR-цвет — для bloom).</summary>
        private void CreateSelectionRing()
        {
            Color ringColor = _owner == PlayerId.Player1 ? new Color(0.4f, 1.6f, 2.2f) : new Color(2.2f, 0.5f, 0.45f);
            _selectionRing = FxLibrary.CreateGroundRing("SelectionRing", transform, 0.72f, 0.95f, ringColor);
        }

        /// <summary>Повернуть башню к цели выстрела.</summary>
        public void AimAt(SimVector2 target)
        {
            if (_rig != null) _rig.AimAt(new Vector3(target.X, transform.position.y, target.Y));
        }

        private void CreateHealthBar(float height)
        {
            var hbObj = new GameObject("HealthBar");
            hbObj.transform.SetParent(transform, false);
            hbObj.transform.localPosition = new Vector3(0f, height, 0f);
            _healthBarRoot = hbObj.transform;

            var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");

            // Фон полоски (тёмный)
            var bgGo = new GameObject("Bg");
            bgGo.transform.SetParent(hbObj.transform, false);
            _healthBarBg = bgGo.AddComponent<LineRenderer>();
            _healthBarBg.useWorldSpace = false;
            _healthBarBg.alignment = LineAlignment.TransformZ;
            _healthBarBg.startWidth = 0.12f;
            _healthBarBg.endWidth = 0.12f;
            _healthBarBg.material = new Material(shader);
            _healthBarBg.startColor = new Color(0.1f, 0.1f, 0.1f, 0.8f);
            _healthBarBg.endColor = new Color(0.1f, 0.1f, 0.1f, 0.8f);
            _healthBarBg.positionCount = 2;
            _healthBarBg.SetPosition(0, new Vector3(-0.5f, 0f, 0f));
            _healthBarBg.SetPosition(1, new Vector3(0.5f, 0f, 0f));

            // Заполнение полоски (зелёный/командный)
            var fillGo = new GameObject("Fill");
            fillGo.transform.SetParent(hbObj.transform, false);
            fillGo.transform.localPosition = new Vector3(0f, 0.01f, 0f);
            _healthBarFill = fillGo.AddComponent<LineRenderer>();
            _healthBarFill.useWorldSpace = false;
            _healthBarFill.alignment = LineAlignment.TransformZ;
            _healthBarFill.startWidth = 0.08f;
            _healthBarFill.endWidth = 0.08f;
            _healthBarFill.material = new Material(shader);
            _healthBarFill.startColor = new Color(0.2f, 0.9f, 0.2f);
            _healthBarFill.endColor = new Color(0.2f, 0.9f, 0.2f);
            _healthBarFill.positionCount = 2;
            _healthBarFill.SetPosition(0, new Vector3(-0.5f, 0f, 0f));
            _healthBarFill.SetPosition(1, new Vector3(0.5f, 0f, 0f));

            var label = new GameObject("HealthText");
            label.transform.SetParent(hbObj.transform, false);
            label.transform.localPosition = new Vector3(0f, 0.16f, 0f);
            _healthLabel = label.AddComponent<TextMesh>();
            _healthLabel.font = GetWorldFont();
            _healthLabel.fontSize = 24;
            _healthLabel.characterSize = 0.07f;
            _healthLabel.anchor = TextAnchor.MiddleCenter;
            _healthLabel.alignment = TextAlignment.Center;
            _healthLabel.color = Color.white;
        }

        public void SetSelected(bool selected)
        {
            _isSelected = selected;
            if (_selectionRing != null)
            {
                _selectionRing.enabled = selected;
            }
        }

        /// <summary>Фиксирует позицию после тика симуляции; кадр рисуется между двумя последними тиками.</summary>
        public void CaptureSimulationStep()
        {
            if (_state == null) return;
            _previousSimPosition = _currentSimPosition;
            _currentSimPosition = ToWorld(_state.Position);
        }

        /// <summary>Высота поверхности под роботом: антиграв поднимается над скалами, остальные идут по земле.</summary>
        public void SetSurfaceHeightProvider(System.Func<SimVector2, float> surfaceHeight)
        {
            _surfaceHeight = surfaceHeight;
        }

        public void ApplyInterpolation(float alpha)
        {
            if (_state == null) return;
            Vector3 position = Vector3.Lerp(_previousSimPosition, _currentSimPosition, Mathf.Clamp01(alpha));
            float targetHeight = _surfaceHeight != null ? Mathf.Max(0f, _surfaceHeight(new SimVector2(position.x, position.z))) : 0f;
            _visualHeight = Mathf.MoveTowards(_visualHeight, targetHeight, 4f * Time.deltaTime);
            position.y = _visualHeight;
            transform.position = position;
            Quaternion targetRotation = Quaternion.Euler(0f, _state.RotationDegrees, 0f);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, 360f * Time.deltaTime);
        }

        private void LateUpdate()
        {
            if (_healthBarRoot == null || UnityEngine.Camera.main == null) return;
            var cameraTransform = UnityEngine.Camera.main.transform;
            _healthBarRoot.rotation = Quaternion.LookRotation(cameraTransform.forward, cameraTransform.up);
            float scale = Mathf.Clamp(CameraZoom.ViewHalfHeight(UnityEngine.Camera.main, _healthBarRoot.position) / 9f, 0.75f, 1.8f);
            _healthBarRoot.localScale = Vector3.one * scale;
        }

        public void UpdateHealthVisual()
        {
            if (_state == null || _healthBarFill == null) return;

            float ratio = Mathf.Clamp01((float)_state.CurrentHealth / Mathf.Max(1, _state.MaxHealth));
            float endX = -0.5f + ratio * 1.0f;
            _healthBarFill.SetPosition(1, new Vector3(endX, 0f, 0f));

            Color col = ratio > 0.5f ? Color.green : (ratio > 0.25f ? Color.yellow : Color.red);
            _healthBarFill.startColor = col;
            _healthBarFill.endColor = col;
            if (_healthLabel != null)
            {
                _healthLabel.text = $"{_state.CurrentHealth}/{_state.MaxHealth}";
                _healthLabel.color = col;
            }
        }

        private static Vector3 ToWorld(SimVector2 position) => new Vector3(position.X, 0f, position.Y);

        private static Font GetWorldFont() =>
            Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
    }
}
