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

        public event Action<BuildingView> OnBuildingSelected;
        public event Action<SimVector2> OnGroundClicked;

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
            if (Mouse.current == null) return;

            // Клик левой кнопкой мыши или отпускание пальца
            if (Mouse.current.leftButton.wasReleasedThisFrame)
            {
                if (_cameraController != null && _cameraController.IsDraggingView)
                {
                    // Игнорируем клик, если игрок панорамировал камеру
                    return;
                }

                Vector2 pointerPos = Mouse.current.position.ReadValue();
                Ray ray = _cam.ScreenPointToRay(pointerPos);

                if (Physics.Raycast(ray, out RaycastHit hit, 200f))
                {
                    // Проверяем клик по зданию
                    var building = hit.collider.GetComponentInParent<BuildingView>();
                    if (building != null)
                    {
                        SelectBuilding(building);
                        return;
                    }

                    // Клик по земле
                    var worldPos = new SimVector2(hit.point.x, hit.point.z);
                    SelectBuilding(null);
                    OnGroundClicked?.Invoke(worldPos);
                }
            }
        }

        public void SelectBuilding(BuildingView building)
        {
            _selectedBuilding = building;
            OnBuildingSelected?.Invoke(building);
        }
    }
}
