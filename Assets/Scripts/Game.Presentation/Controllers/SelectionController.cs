using System;
using UnityEngine;
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

        public event Action<BuildingView> OnBuildingSelected;
        public event Action<RobotView> OnRobotSelected;
        public event Action<SimVector2> OnGroundClicked;
        public event Action<RobotView, SimVector2> OnMoveOrderRequested;

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
            HandlePointerClick();
        }

        private void HandlePointerClick()
        {
            Vector2 pointerPos = Vector2.zero;
            bool isLeftReleased = false;
            bool isRightReleased = false;

            if (Mouse.current != null)
            {
                if (Mouse.current.leftButton.wasReleasedThisFrame)
                {
                    isLeftReleased = true;
                    pointerPos = Mouse.current.position.ReadValue();
                }
                else if (Mouse.current.rightButton.wasReleasedThisFrame)
                {
                    isRightReleased = true;
                    pointerPos = Mouse.current.position.ReadValue();
                }
            }

            // Fallback на стандартный Input
            if (!isLeftReleased && !isRightReleased)
            {
                if (Input.GetMouseButtonUp(0))
                {
                    isLeftReleased = true;
                    pointerPos = Input.mousePosition;
                }
                else if (Input.GetMouseButtonUp(1))
                {
                    isRightReleased = true;
                    pointerPos = Input.mousePosition;
                }
            }

            if (_cam == null) _cam = UnityEngine.Camera.main;
            if (_cam == null) return;

            // Клик левой кнопкой мыши
            if (isLeftReleased)
            {
                if (_cameraController != null && _cameraController.IsDraggingView)
                {
                    return;
                }

                // Игнорируем клики по интерфейсу (нижняя панель управления и верхний статус-бар)
                bool isOverBottomHud = pointerPos.y <= 175f;
                bool isOverTopHud = pointerPos.y >= Screen.height - 95f && pointerPos.x <= 320f;
                if (isOverBottomHud || isOverTopHud)
                {
                    return;
                }

                Ray ray = _cam.ScreenPointToRay(pointerPos);
                Debug.Log($"[Selection] Left click at screen {pointerPos}");

                if (Physics.Raycast(ray, out RaycastHit hit, 200f))
                {
                    Debug.Log($"[Selection] Hit object: {hit.collider.gameObject.name}");

                    // 1. Проверяем клик по роботу
                    var robot = hit.collider.GetComponentInParent<RobotView>();
                    if (robot != null)
                    {
                        SelectRobot(robot);
                        SelectBuilding(null);
                        return;
                    }

                    // 2. Проверяем клик по зданию
                    var building = hit.collider.GetComponentInParent<BuildingView>();
                    if (building != null)
                    {
                        SelectBuilding(building);
                        SelectRobot(null);
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
            // Клик правой кнопкой мыши (прямой приказ движения для ПК)
            else if (isRightReleased && _selectedRobot != null && _selectedRobot.Owner == PlayerId.Player1)
            {
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
            _selectedBuilding = building;
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
    }
}
