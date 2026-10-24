using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Presentation.Camera
{
    /// <summary>
    /// Перспективная RTS-камера с узким углом обзора: выглядит как «диорама», но почти без искажений.
    /// Камера смотрит на точку на земле; зум задаёт половину видимой высоты кадра в этой точке
    /// (в тех же единицах, что прежний orthographicSize).
    /// </summary>
    public class RtsCameraController : MonoBehaviour
    {
        [Header("View")]
        [SerializeField] private float _pitch = 50f;
        [SerializeField] private float _yaw;
        [SerializeField] private float _fieldOfView = 30f;

        [Header("Movement & Damping")]
        [SerializeField] private float _dragPanSpeed = 1.0f;
        [SerializeField] private float _smoothTime = 0.1f;
        [SerializeField] private float _dragThresholdPixels = 10f;

        [Header("Zoom")]
        [SerializeField] private float _minViewSize = 5f;
        [SerializeField] private float _maxViewSize = 18f;
        [SerializeField] private float _defaultViewSize = 9f;
        [SerializeField] private float _zoomSensitivity = 1.5f;

        [Header("Map Boundaries")]
        [SerializeField] private Vector2 _panLimitMin = new Vector2(-5f, -5f);
        [SerializeField] private Vector2 _panLimitMax = new Vector2(35f, 25f);

        private static readonly Plane GroundPlane = new Plane(Vector3.up, Vector3.zero);

        private UnityEngine.Camera _cam;
        private Vector3 _focus;
        private Vector3 _targetFocus;
        private Vector3 _focusVelocity;
        private float _zoom;
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
                _cam.orthographic = false;
                _cam.fieldOfView = _fieldOfView;
                _cam.nearClipPlane = 1f;
                _cam.farClipPlane = 300f;
            }

            _zoom = _targetZoom = _defaultViewSize;
            // Точка на земле, куда смотрела камера из сцены.
            Ray ray = new Ray(transform.position, transform.forward);
            _focus = _targetFocus = GroundPlane.Raycast(ray, out float hit) ? ray.GetPoint(hit) : new Vector3(transform.position.x, 0f, transform.position.z);
            ApplyTransform();
        }

        /// <summary>Навести камеру на точку карты: на старте мгновенно, по клику на радаре — плавным перелётом.</summary>
        public void FocusOn(float x, float z, bool instant = true)
        {
            _targetFocus = ClampToBounds(new Vector3(x, 0f, z));
            if (!instant) return;
            _focus = _targetFocus;
            ApplyTransform();
        }

        public void SetBounds(float width, float height)
        {
            _panLimitMin = new Vector2(0f, 0f);
            _panLimitMax = new Vector2(width, height);
            FocusOn(width * 0.5f, height * 0.5f);
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
                    _targetZoom = Mathf.Clamp(_targetZoom - Mathf.Sign(scroll) * _zoomSensitivity, _minViewSize, _maxViewSize);
                }

                // Drag правой или средней кнопкой мыши
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
                        DragTo(mousePos);
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

                    if (touch.press.wasPressedThisFrame && IsPointerOverUi())
                    {
                        _isDragging = false;
                    }
                    else if (touch.press.wasPressedThisFrame)
                    {
                        _isDragging = true;
                        _lastPointerPos = touchPos;
                        _dragStartPos = touchPos;
                        _hasDraggedBeyondThreshold = false;
                    }
                    else if (touch.press.isPressed && _isDragging)
                    {
                        DragTo(touchPos);
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

                    _targetZoom = Mathf.Clamp(_targetZoom - delta * 0.02f, _minViewSize, _maxViewSize);
                    _hasDraggedBeyondThreshold = true;
                }
            }

            _targetFocus = ClampToBounds(_targetFocus);
        }

        /// <summary>Сдвигает камеру так, чтобы точка земли под пальцем/курсором следовала за ним.</summary>
        private void DragTo(Vector2 pointerPos)
        {
            Vector2 previous = _lastPointerPos;
            _lastPointerPos = pointerPos;
            if (Vector2.Distance(pointerPos, _dragStartPos) > _dragThresholdPixels) _hasDraggedBeyondThreshold = true;
            if (!_hasDraggedBeyondThreshold || _cam == null) return;
            if (TryGroundPoint(previous, out Vector3 from) && TryGroundPoint(pointerPos, out Vector3 to))
            {
                _targetFocus += (from - to) * _dragPanSpeed;
            }
        }

        private bool TryGroundPoint(Vector2 screenPos, out Vector3 point)
        {
            Ray ray = _cam.ScreenPointToRay(screenPos);
            point = default;
            if (!GroundPlane.Raycast(ray, out float distance)) return false;
            point = ray.GetPoint(distance);
            return true;
        }

        private Vector3 ClampToBounds(Vector3 focus) =>
            new Vector3(Mathf.Clamp(focus.x, _panLimitMin.x, _panLimitMax.x), 0f, Mathf.Clamp(focus.z, _panLimitMin.y, _panLimitMax.y));

        private static bool IsPointerOverUi() =>
            UnityEngine.EventSystems.EventSystem.current != null && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();

        private void SmoothUpdate()
        {
            // Камера живёт в реальном времени: замедление игры не должно делать управление вязким.
            _focus = Vector3.SmoothDamp(_focus, _targetFocus, ref _focusVelocity, _smoothTime, Mathf.Infinity, Time.unscaledDeltaTime);
            _zoom = Mathf.SmoothDamp(_zoom, _targetZoom, ref _zoomVelocity, _smoothTime, Mathf.Infinity, Time.unscaledDeltaTime);
            ApplyTransform();
        }

        private void ApplyTransform()
        {
            Quaternion rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            float fov = _cam != null ? _cam.fieldOfView : _fieldOfView;
            float distance = _zoom / Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
            transform.SetPositionAndRotation(_focus - rotation * Vector3.forward * distance, rotation);
        }
    }
}
