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
        private bool _isOrderMode;
        private bool _isCaptureTargeting;

        public event Action<BuildingView> OnBuildingSelected;
        public event Action<RobotView> OnRobotSelected;
        public event Action<SimVector2> OnGroundClicked;
        public event Action<RobotView, RobotView> OnAttackOrderRequested;
        public event Action<RobotView, BuildingView> OnCaptureOrderRequested;

        public RobotView SelectedRobot => _selectedRobot;
        public BuildingView SelectedBuilding => _selectedBuilding;
        public bool IsOrderMode => _isOrderMode;

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
                    RobotView hitRobot = null;
                    foreach (RaycastHit candidate in hits)
                    {
                        var candidateRobot = candidate.collider.GetComponentInParent<RobotView>();
                        if (candidateRobot != null)
                        {
                            hitRobot = candidateRobot;
                            break;
                        }
                    }

                    // Робот имеет приоритет над зданием: его коллайдер может пересекаться с зоной захвата.
                    if (hitRobot != null)
                    {
                        if (hitRobot == _selectedRobot && !_isOrderMode && !_isCaptureTargeting)
                        {
                            SelectRobot(null);
                            return;
                        }
                        SelectBuilding(null);
                        SelectRobot(hitRobot);
                        return;
                    }

                    foreach (RaycastHit candidate in hits)
                    {
                        var candidateBuilding = candidate.collider.GetComponentInParent<BuildingView>();
                        if (candidateBuilding != null)
                        {
                            if (_isCaptureTargeting && _selectedRobot != null && _selectedRobot.Owner == PlayerId.Player1)
                            {
                                _isCaptureTargeting = false;
                                OnCaptureOrderRequested?.Invoke(_selectedRobot, candidateBuilding);
                                return;
                            }

                            SelectRobot(null);
                            SelectBuilding(candidateBuilding);
                            return;
                        }
                    }

                    // Клик по земле
                    var worldPos = new SimVector2(hits[0].point.x, hits[0].point.z);

                    // Если был выбран свой робот — отдаем приказ на движение
                    if (_selectedRobot == null || _selectedRobot.Owner != PlayerId.Player1)
                    {
                        SelectBuilding(null);
                        SelectRobot(null);
                        OnGroundClicked?.Invoke(worldPos);
                    }
                }
            }
            // Клик правой кнопкой мыши (приказ движения)
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

        public void SetOrderMode(bool isOrderMode)
        {
            _isOrderMode = isOrderMode;
        }

        public void BeginCaptureTargeting()
        {
            _isOrderMode = true;
            _isCaptureTargeting = true;
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
