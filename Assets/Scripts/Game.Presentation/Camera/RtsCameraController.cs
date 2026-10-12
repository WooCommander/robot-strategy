using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Presentation.Camera
{
    public class RtsCameraController : MonoBehaviour
    {
        [Header("Movement & Damping")]
        [SerializeField] private float _dragPanSpeed = 1.0f;
        [SerializeField] private float _smoothTime = 0.1f;
        [SerializeField] private float _dragThresholdPixels = 10f;

        [Header("Zoom")]
        [SerializeField] private float _minOrthographicSize = 5f;
        [SerializeField] private float _maxOrthographicSize = 18f;
        [SerializeField] private float _defaultOrthographicSize = 10f;
        [SerializeField] private float _zoomSensitivity = 1.5f;

        [Header("Map Boundaries")]
        [SerializeField] private Vector2 _panLimitMin = new Vector2(-5f, -5f);
        [SerializeField] private Vector2 _panLimitMax = new Vector2(35f, 25f);

        private UnityEngine.Camera _cam;
        private Vector3 _targetPosition;
        private Vector3 _currentVelocity;
        private float _targetZoom;
        private float _zoomVelocity;

        private Vector2 _lastPointerPos;
        private bool _isDragging;
        private bool _hasDraggedBeyondThreshold;
        private Vector2 _dragStartPos;

        public bool IsDraggingView => _hasDraggedBeyondThreshold;

        private void Awake()
        {
            _cam = GetComponent<UnityEngine.Camera>();
            if (_cam == null) _cam = UnityEngine.Camera.main;

            if (_cam != null)
            {
                _cam.orthographic = true;
                _cam.orthographicSize = _defaultOrthographicSize;
                _targetZoom = _defaultOrthographicSize;
            }

            _targetPosition = transform.position;
            // Изометрический наклон (например, pitch 55 градусов)
            transform.rotation = Quaternion.Euler(55f, 0f, 0f);
        }

        public void SetBounds(float width, float height)
        {
            _panLimitMin = new Vector2(0f, 0f);
            _panLimitMax = new Vector2(width, height);
            _targetPosition = new Vector3(width * 0.5f, 15f, height * 0.5f - 8f);
            transform.position = _targetPosition;
        }

        private void Update()
        {
            HandleMouseAndTouchInput();
            SmoothUpdate();
        }

        private void HandleMouseAndTouchInput()
        {
            // 1. Zoom колесиком мыши
            if (Mouse.current != null)
            {
                float scroll = Mouse.current.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f)
                {
                    _targetZoom = Mathf.Clamp(_targetZoom - Mathf.Sign(scroll) * _zoomSensitivity, _minOrthographicSize, _maxOrthographicSize);
                }

                // Drag правой или средней кнопкой мыши (или левой при зажатии)
                bool isMouseDown = Mouse.current.rightButton.isPressed || Mouse.current.middleButton.isPressed;
                Vector2 mousePos = Mouse.current.position.ReadValue();

                if (isMouseDown)
                {
                    if (!_isDragging)
                    {
                        _isDragging = true;
                        _lastPointerPos = mousePos;
                        _dragStartPos = mousePos;
                        _hasDraggedBeyondThreshold = false;
                    }
                    else
                    {
                        Vector2 delta = mousePos - _lastPointerPos;
                        _lastPointerPos = mousePos;

                        if (Vector2.Distance(mousePos, _dragStartPos) > _dragThresholdPixels)
                        {
                            _hasDraggedBeyondThreshold = true;
                        }

                        if (_hasDraggedBeyondThreshold && _cam != null)
                        {
                            // Перемещение в плоскости XZ
                            float factor = (_cam.orthographicSize * 2f) / Screen.height;
                            Vector3 move = new Vector3(-delta.x * factor * _dragPanSpeed, 0f, -delta.y * factor * _dragPanSpeed * 1.5f);
                            _targetPosition += move;
                        }
                    }
                }
                else
                {
                    _isDragging = false;
                    _hasDraggedBeyondThreshold = false;
                }
            }

            // 2. Pinch Zoom и тач-управление на Android
            if (Touchscreen.current != null)
            {
                var touches = Touchscreen.current.touches;
                int touchCount = 0;
                for (int i = 0; i < touches.Count; i++)
                {
                    if (touches[i].isInProgress) touchCount++;
                }

                if (touchCount == 1)
                {
                    var touch = touches[0];
                    Vector2 touchPos = touch.position.ReadValue();

                    if (touch.press.wasPressedThisFrame)
                    {
                        _isDragging = true;
                        _lastPointerPos = touchPos;
                        _dragStartPos = touchPos;
                        _hasDraggedBeyondThreshold = false;
                    }
                    else if (touch.press.isPressed && _isDragging)
                    {
                        Vector2 delta = touchPos - _lastPointerPos;
                        _lastPointerPos = touchPos;

                        if (Vector2.Distance(touchPos, _dragStartPos) > _dragThresholdPixels)
                        {
                            _hasDraggedBeyondThreshold = true;
                        }

                        if (_hasDraggedBeyondThreshold && _cam != null)
                        {
                            float factor = (_cam.orthographicSize * 2f) / Screen.height;
                            Vector3 move = new Vector3(-delta.x * factor * _dragPanSpeed, 0f, -delta.y * factor * _dragPanSpeed * 1.5f);
                            _targetPosition += move;
                        }
                    }
                    else if (touch.press.wasReleasedThisFrame)
                    {
                        _isDragging = false;
                        _hasDraggedBeyondThreshold = false;
                    }
                }
                else if (touchCount >= 2)
                {
                    // Двупальцевый Pinch Zoom
                    Vector2 pos0 = touches[0].position.ReadValue();
                    Vector2 pos1 = touches[1].position.ReadValue();
                    Vector2 prevPos0 = pos0 - touches[0].delta.ReadValue();
                    Vector2 prevPos1 = pos1 - touches[1].delta.ReadValue();

                    float prevDist = Vector2.Distance(prevPos0, prevPos1);
                    float curDist = Vector2.Distance(pos0, pos1);
                    float delta = curDist - prevDist;

                    _targetZoom = Mathf.Clamp(_targetZoom - delta * 0.02f, _minOrthographicSize, _maxOrthographicSize);
                    _hasDraggedBeyondThreshold = true;
                }
            }

            // Ограничение перемещения камеры границами карты
            _targetPosition.x = Mathf.Clamp(_targetPosition.x, _panLimitMin.x, _panLimitMax.x);
            _targetPosition.z = Mathf.Clamp(_targetPosition.z, _panLimitMin.y, _panLimitMax.y);
        }

        private void SmoothUpdate()
        {
            transform.position = Vector3.SmoothDamp(transform.position, _targetPosition, ref _currentVelocity, _smoothTime);
            if (_cam != null)
            {
                _cam.orthographicSize = Mathf.SmoothDamp(_cam.orthographicSize, _targetZoom, ref _zoomVelocity, _smoothTime);
            }
        }
    }
}
