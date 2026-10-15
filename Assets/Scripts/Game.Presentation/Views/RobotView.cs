using System.Collections.Generic;
using UnityEngine;
using Game.Core.Models;
using Game.Core.Types;
using Game.Presentation.Robots;

namespace Game.Presentation.Views
{
    public class RobotView : MonoBehaviour
    {
        [Header("State")]
        [SerializeField] private int _entityId;
        [SerializeField] private PlayerId _owner;

        [Header("Components")]
        [SerializeField] private LineRenderer _selectionRing;
        [SerializeField] private LineRenderer _healthBarBg;
        [SerializeField] private LineRenderer _healthBarFill;
        private Transform _healthBarRoot;

        private RobotState _state;
        private bool _isSelected;
        private List<SimVector2> _currentPath = new List<SimVector2>();
        private int _currentPathIndex = 0;
        private System.Func<SimVector2, float> _terrainSpeedProvider;
        private SimVector2 _lastStatePosition;

        public int EntityId => _entityId;
        public PlayerId Owner => _owner;
        public bool IsSelected => _isSelected;
        public RobotState State => _state;
        public bool IsMovingAlongPath => _currentPath != null && _currentPathIndex < _currentPath.Count;

        public void Initialize(RobotState state)
        {
            _state = state;
            _entityId = state.Id.Value;
            _owner = state.Owner;
            _lastStatePosition = state.Position;

            transform.position = new Vector3(state.Position.X, 0f, state.Position.Y);
            transform.rotation = Quaternion.Euler(0f, state.RotationDegrees, 0f);

            // Добавляем коллайдер на корень робота для надежного выбора лучами
            var col = GetComponent<CapsuleCollider>();
            if (col == null) col = gameObject.AddComponent<CapsuleCollider>();
            col.center = new Vector3(0f, 0.75f, 0f);
            col.radius = 0.8f;
            col.height = 1.5f;

            // Собираем визуальную модель робота
            var visualModel = ModularRobotBuilder.BuildRobotModel(state.Blueprint, state.Owner);
            visualModel.transform.SetParent(transform, false);

            CreateSelectionRing();
            CreateHealthBar();
            SetSelected(false);
            UpdateHealthVisual();
        }

        private void CreateSelectionRing()
        {
            var ringObj = new GameObject("SelectionRing");
            ringObj.transform.SetParent(transform, false);
            ringObj.transform.localPosition = new Vector3(0f, 0.05f, 0f);

            _selectionRing = ringObj.AddComponent<LineRenderer>();
            _selectionRing.useWorldSpace = false;
            _selectionRing.loop = true;
            _selectionRing.startWidth = 0.08f;
            _selectionRing.endWidth = 0.08f;

            var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
            _selectionRing.material = new Material(shader);
            Color selColor = new Color(0.2f, 1f, 0.3f); // Ярко-зелёный маркер выбора
            _selectionRing.startColor = selColor;
            _selectionRing.endColor = selColor;

            const int segments = 32;
            _selectionRing.positionCount = segments;
            float step = 360f / segments;
            for (int i = 0; i < segments; i++)
            {
                float rad = Mathf.Deg2Rad * (i * step);
                _selectionRing.SetPosition(i, new Vector3(Mathf.Cos(rad) * 0.9f, 0f, Mathf.Sin(rad) * 0.9f));
            }
        }

        private void CreateHealthBar()
        {
            var hbObj = new GameObject("HealthBar");
            hbObj.transform.SetParent(transform, false);
            hbObj.transform.localPosition = new Vector3(0f, 1.8f, 0f);
            _healthBarRoot = hbObj.transform;

            var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");

            // Фон полоски (тёмный)
            var bgGo = new GameObject("Bg");
            bgGo.transform.SetParent(hbObj.transform, false);
            _healthBarBg = bgGo.AddComponent<LineRenderer>();
            _healthBarBg.useWorldSpace = false;
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
            _healthBarFill.startWidth = 0.08f;
            _healthBarFill.endWidth = 0.08f;
            _healthBarFill.material = new Material(shader);
            _healthBarFill.startColor = new Color(0.2f, 0.9f, 0.2f);
            _healthBarFill.endColor = new Color(0.2f, 0.9f, 0.2f);
            _healthBarFill.positionCount = 2;
            _healthBarFill.SetPosition(0, new Vector3(-0.5f, 0f, 0f));
            _healthBarFill.SetPosition(1, new Vector3(0.5f, 0f, 0f));
        }

        public void SetSelected(bool selected)
        {
            _isSelected = selected;
            if (_selectionRing != null)
            {
                _selectionRing.enabled = selected;
            }
        }

        public void SetMovementPath(List<SimVector2> waypoints)
        {
            _currentPath = waypoints ?? new List<SimVector2>();
            _currentPathIndex = 0;
        }

        public void StopMovement()
        {
            _currentPath.Clear();
            _currentPathIndex = 0;
        }

        public void SetTerrainSpeedProvider(System.Func<SimVector2, float> terrainSpeedProvider)
        {
            _terrainSpeedProvider = terrainSpeedProvider;
        }

        public void MoveDirect(Vector2 inputDirection)
        {
            if (_state == null || inputDirection.sqrMagnitude < 0.001f)
            {
                return;
            }

            StopMovement();
            _state.CurrentOrder = RobotOrder.Stop();

            Vector3 direction = new Vector3(inputDirection.x, 0f, inputDirection.y).normalized;
            transform.position += direction * (_state.MoveSpeed * GetTerrainSpeedMultiplier(_state.Position) * Time.deltaTime);
            transform.rotation = Quaternion.LookRotation(direction);
            _state.Position = new SimVector2(transform.position.x, transform.position.z);
            _state.RotationDegrees = transform.eulerAngles.y;
        }

        private void Update()
        {
            MoveAlongPath();
            if (_state != null && Time.deltaTime > 0f)
            {
                _state.Velocity = (_state.Position - _lastStatePosition) / Time.deltaTime;
                _lastStatePosition = _state.Position;
            }
        }

        private void LateUpdate()
        {
            if (_healthBarRoot == null || UnityEngine.Camera.main == null) return;
            var cameraTransform = UnityEngine.Camera.main.transform;
            _healthBarRoot.rotation = Quaternion.LookRotation(cameraTransform.forward, cameraTransform.up);
        }

        private void MoveAlongPath()
        {
            if (_currentPath == null || _currentPathIndex >= _currentPath.Count) return;

            var target2D = _currentPath[_currentPathIndex];
            Vector3 targetWorld = new Vector3(target2D.X, 0f, target2D.Y);

            float speed = _state != null ? _state.MoveSpeed * GetTerrainSpeedMultiplier(_state.Position) : 3.5f;
            transform.position = Vector3.MoveTowards(transform.position, targetWorld, speed * Time.deltaTime);

            Vector3 dir = (targetWorld - transform.position);
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.001f)
            {
                Quaternion targetRot = Quaternion.LookRotation(dir);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, 360f * Time.deltaTime);
            }

            if (Vector3.Distance(transform.position, targetWorld) < 0.1f)
            {
                _currentPathIndex++;
                if (_state != null)
                {
                    _state.Position = new SimVector2(transform.position.x, transform.position.z);
                    _state.RotationDegrees = transform.eulerAngles.y;
                }
            }
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
        }

        private float GetTerrainSpeedMultiplier(SimVector2 position)
        {
            return Mathf.Max(0.1f, _terrainSpeedProvider?.Invoke(position) ?? 1f);
        }
    }
}
