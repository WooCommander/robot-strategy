using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using Game.Core.Types;
using Game.Presentation.Camera;
using Game.Presentation.Views;

namespace Game.Presentation.Controllers
{
    public class SelectionController : MonoBehaviour
    {
        [SerializeField] private RtsCameraController _cameraController;

        private UnityEngine.Camera _cam;
        private BuildingView _selectedBuilding;
        private RobotView _selectedRobot;

        private Vector2 _pointerDownPos;
        private bool _isPointerDown;
        private bool _pressedOverUI;

        public event Action<BuildingView> OnBuildingSelected;
        public event Action<RobotView> OnRobotSelected;
        public event Action<SimVector2> OnGroundClicked;
        public event Action<RobotView, SimVector2> OnMoveOrderRequested;
        public event Action<RobotView, RobotView> OnAttackOrderRequested;

        public RobotView SelectedRobot => _selectedRobot;
        public BuildingView SelectedBuilding => _selectedBuilding;

        private void Awake()
        {
            _cam = UnityEngine.Camera.main;
            if (_cameraController == null)
            {
                _cameraController = FindAnyObjectByType<RtsCameraController>();
            }
        }

        private void Update()
        {
            // Отслеживаем начало нажатия
            bool isDownThisFrame = Input.GetMouseButtonDown(0) || 
                                   (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) ||
                                   (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame);

            if (isDownThisFrame)
            {
                _isPointerDown = true;
                _pointerDownPos = GetCurrentScreenPosition();
                _pressedOverUI = IsPointerOverUi();
            }

            HandlePointerClick();
        }

        private Vector2 GetCurrentScreenPosition()
        {
            if (Input.touchCount > 0)
            {
                return Input.GetTouch(0).position;
            }
            return Input.mousePosition;
        }

        private bool IsPointerOverUi()
        {
            if (EventSystem.current == null) return false;
            if (EventSystem.current.IsPointerOverGameObject()) return true;

            if (Input.touchCount > 0)
            {
                for (int i = 0; i < Input.touchCount; i++)
                {
                    if (EventSystem.current.IsPointerOverGameObject(Input.GetTouch(i).fingerId))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private void HandlePointerClick()
        {
            bool isLeftReleased = Input.GetMouseButtonUp(0) || 
                                  (Mouse.current != null && Mouse.current.leftButton.wasReleasedThisFrame) ||
                                  (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasReleasedThisFrame);
            bool isRightReleased = Input.GetMouseButtonUp(1) || 
                                   (Mouse.current != null && Mouse.current.rightButton.wasReleasedThisFrame);

            if (!isLeftReleased && !isRightReleased) return;

            Vector2 pointerPos = GetCurrentScreenPosition();

            if (isLeftReleased)
            {
                if (!_isPointerDown) return;
                _isPointerDown = false;

                bool wasOverUI = _pressedOverUI || IsPointerOverUi();
                _pressedOverUI = false;

                // Игнорируем клики по интерфейсу (Canvas / UI)
                if (wasOverUI)
                {
                    return;
                }

                // Игнорируем, если курсор сдвинулся (панорамирование камеры)
                float dragDistance = Vector2.Distance(_pointerDownPos, pointerPos);
                if (dragDistance > 12f)
                {
                    return;
                }

                if (_cameraController != null && _cameraController.IsDraggingView)
                {
                    return;
                }

                if (_cam == null) _cam = UnityEngine.Camera.main;
                if (_cam == null) return;

                Ray ray = _cam.ScreenPointToRay(pointerPos);
                Debug.Log($"[Selection] Left click at screen {pointerPos}");

                RaycastHit[] hits = Physics.RaycastAll(ray, 200f);
                if (hits.Length > 0)
                {
                    System.Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
                    RaycastHit hit = hits[0];
                    Debug.Log($"[Selection] Hit object: {hit.collider.gameObject.name}");

                    foreach (RaycastHit candidate in hits)
                    {
                        var candidateRobot = candidate.collider.GetComponentInParent<RobotView>();
                        if (candidateRobot != null)
                        {
                            if (_selectedRobot != null &&
                                _selectedRobot.Owner == PlayerId.Player1 &&
                                candidateRobot.Owner != _selectedRobot.Owner)
                            {
                                OnAttackOrderRequested?.Invoke(_selectedRobot, candidateRobot);
                                return;
                            }

                            SelectBuilding(null);
                            SelectRobot(candidateRobot);
                            return;
                        }

                        var candidateBuilding = candidate.collider.GetComponentInParent<BuildingView>();
                        if (candidateBuilding != null)
                        {
                            SelectRobot(null);
                            SelectBuilding(candidateBuilding);
                            return;
                        }
                    }

                    // 1. Проверяем клик по роботу
                    var robot = hit.collider.GetComponentInParent<RobotView>();
                    if (robot != null)
                    {
                        if (_selectedRobot != null &&
                            _selectedRobot.Owner == PlayerId.Player1 &&
                            robot.Owner != _selectedRobot.Owner)
                        {
                            OnAttackOrderRequested?.Invoke(_selectedRobot, robot);
                            return;
                        }

                        SelectBuilding(null);
                        SelectRobot(robot);
                        return;
                    }

                    // 2. Проверяем клик по зданию
                    var building = hit.collider.GetComponentInParent<BuildingView>();
                    if (building != null)
                    {
                        SelectRobot(null);
                        SelectBuilding(building);
                        return;
                    }

                    // 3. Клик по земле
                    var worldPos = new SimVector2(hit.point.x, hit.point.z);

                    // Если был выбран свой робот — отдаем приказ на движение
                    if (_selectedRobot != null && _selectedRobot.Owner == PlayerId.Player1)
                    {
                        OnMoveOrderRequested?.Invoke(_selectedRobot, worldPos);
                    }
                    else
                    {
                        SelectBuilding(null);
                        SelectRobot(null);
                        OnGroundClicked?.Invoke(worldPos);
                    }
                }
            }
            // Клик правой кнопкой мыши (приказ движения)
            else if (isRightReleased && _selectedRobot != null && _selectedRobot.Owner == PlayerId.Player1)
            {
                if (IsPointerOverUi()) return;

                if (_cam == null) _cam = UnityEngine.Camera.main;
                if (_cam == null) return;

                Ray ray = _cam.ScreenPointToRay(pointerPos);
                if (Physics.Raycast(ray, out RaycastHit hit, 200f))
                {
                    var worldPos = new SimVector2(hit.point.x, hit.point.z);
                    OnMoveOrderRequested?.Invoke(_selectedRobot, worldPos);
                }
            }
        }

        public void SelectBuilding(BuildingView building)
        {
            if (_selectedBuilding != null && _selectedBuilding != building)
            {
                _selectedBuilding.SetSelected(false);
            }

            _selectedBuilding = building;
            if (_selectedBuilding != null)
            {
                _selectedBuilding.SetSelected(true);
            }

            OnBuildingSelected?.Invoke(building);
        }

        public void SelectRobot(RobotView robot)
        {
            if (_selectedRobot != null && _selectedRobot != robot)
            {
                _selectedRobot.SetSelected(false);
            }

            _selectedRobot = robot;
            if (_selectedRobot != null)
            {
                _selectedRobot.SetSelected(true);
            }
            OnRobotSelected?.Invoke(robot);
        }

        public void SyncSelectedBuilding(BuildingView building)
        {
            _selectedBuilding = building;
        }

        public void SyncSelectedRobot(RobotView robot)
        {
            _selectedRobot = robot;
        }
    }
}
