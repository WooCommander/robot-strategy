using UnityEngine;
using Game.Core.Models;
using Game.Core.Types;
using Game.Content.Definitions;
using Game.Presentation.Rendering;

namespace Game.Presentation.Views
{
    public class BuildingView : MonoBehaviour
    {
        [Header("State")]
        [SerializeField] private int _entityId;
        [SerializeField] private BuildingType _type;
        [SerializeField] private PlayerId _owner = PlayerId.Neutral;
        [SerializeField] private float _captureRadius = 3f;
        [SerializeField] private ResourceType _producedResource;

        [Header("Visuals")]
        [SerializeField] private MeshRenderer _buildingRenderer;
        [SerializeField] private LineRenderer _captureZoneCircle;
        [SerializeField] private TextMesh _nameLabel;
        [SerializeField] private TextMesh _captureLabel;
        [SerializeField] private LineRenderer _captureProgressArc;

        private MaterialPropertyBlock _propBlock;

        public int EntityId => _entityId;
        public BuildingType Type => _type;
        public PlayerId Owner => _owner;
        public ResourceType ProducedResource => _producedResource;
        /// <summary>Подпись как в оригинале: завод называется по тому, что производит.</summary>
        public string DisplayName => _type == BuildingType.Base ? $"БАЗА #{_entityId}" : $"ЗАВОД: {DefaultContentCatalog.GetResourceName(_producedResource).ToUpperInvariant()}";

        public void Initialize(BuildingSpawnDefinition def)
        {
            _entityId = def.EntityIdValue;
            _type = def.Type;
            _owner = def.InitialOwner;
            _captureRadius = def.CaptureRadius;
            _producedResource = def.ProducedResource;

            transform.position = new Vector3(def.Position.X, 0f, def.Position.Y);
            gameObject.name = $"{def.Type}_{def.EntityIdValue}_{def.InitialOwner}";

            CreateVisuals();
            UpdateOwnerVisuals(_owner);
        }

        private void CreateVisuals()
        {
            _propBlock = new MaterialPropertyBlock();

            // Создаем корпус здания процедурно, если нет назначенного рендера
            if (_buildingRenderer == null)
            {
                var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
                body.transform.SetParent(transform, false);

                if (_type == BuildingType.Base)
                {
                    body.transform.localScale = new Vector3(3f, 2f, 3f);
                    body.transform.localPosition = new Vector3(0f, 1f, 0f);
                }
                else
                {
                    body.transform.localScale = new Vector3(2f, 1.5f, 2f);
                    body.transform.localPosition = new Vector3(0f, 0.75f, 0f);
                }

                _buildingRenderer = body.GetComponent<MeshRenderer>();
                _buildingRenderer.sharedMaterial = MaterialFactory.CreateLit("Mat_Building", Color.white, 0.4f, 0.35f);

                // Удаляем коллайдер с дочернего объекта, чтобы он был строго на корне
                var childCol = body.GetComponent<Collider>();
                if (childCol != null) DestroyImmediate(childCol);
            }

            // Добавляем BoxCollider прямо на корень здания
            var rootCol = GetComponent<BoxCollider>();
            if (rootCol == null) rootCol = gameObject.AddComponent<BoxCollider>();

            if (_type == BuildingType.Base)
            {
                rootCol.center = new Vector3(0f, 1f, 0f);
                rootCol.size = new Vector3(3.5f, 2.5f, 3.5f);
            }
            else
            {
                rootCol.center = new Vector3(0f, 0.75f, 0f);
                rootCol.size = new Vector3(2.5f, 2.0f, 2.5f);
            }

            // Добавляем круг зоны захвата с помощью LineRenderer
            if (_captureZoneCircle == null)
            {
                var zoneObj = new GameObject("CaptureZone");
                zoneObj.transform.SetParent(transform, false);
                zoneObj.transform.localPosition = new Vector3(0f, 0.05f, 0f);

                _captureZoneCircle = zoneObj.AddComponent<LineRenderer>();
                _captureZoneCircle.useWorldSpace = false;
                _captureZoneCircle.loop = true;
                _captureZoneCircle.startWidth = 0.15f;
                _captureZoneCircle.endWidth = 0.15f;

                var lineShader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
                _captureZoneCircle.material = new Material(lineShader);

                const int segments = 40;
                _captureZoneCircle.positionCount = segments;
                float angleStep = 360f / segments;
                for (int i = 0; i < segments; i++)
                {
                    float rad = Mathf.Deg2Rad * (i * angleStep);
                    float x = Mathf.Cos(rad) * _captureRadius;
                    float z = Mathf.Sin(rad) * _captureRadius;
                    _captureZoneCircle.SetPosition(i, new Vector3(x, 0f, z));
                }
            }

            // Название здания
            if (_nameLabel == null)
            {
                var labelObj = new GameObject("Label");
                labelObj.transform.SetParent(transform, false);
                labelObj.transform.localPosition = new Vector3(0f, _type == BuildingType.Base ? 3.0f : 2.2f, 0f);
                labelObj.transform.localRotation = Quaternion.Euler(55f, 0f, 0f);

                _nameLabel = labelObj.AddComponent<TextMesh>();
                _nameLabel.font = GetWorldFont();
                _nameLabel.fontSize = 24;
                _nameLabel.characterSize = 0.1f;
                _nameLabel.alignment = TextAlignment.Center;
                _nameLabel.anchor = TextAnchor.MiddleCenter;
                _nameLabel.text = DisplayName;
            }

            var captureLabelObj = new GameObject("CaptureProgress");
            captureLabelObj.transform.SetParent(transform, false);
            captureLabelObj.transform.localPosition = new Vector3(0f, _type == BuildingType.Base ? 3.75f : 2.7f, 0f);
            _captureLabel = captureLabelObj.AddComponent<TextMesh>();
            _captureLabel.font = GetWorldFont();
            _captureLabel.fontSize = 28;
            _captureLabel.characterSize = 0.11f;
            _captureLabel.alignment = TextAlignment.Center;
            _captureLabel.anchor = TextAnchor.MiddleCenter;
            _captureLabel.gameObject.SetActive(false);

            var arcObj = new GameObject("CaptureProgressArc");
            arcObj.transform.SetParent(transform, false);
            arcObj.transform.localPosition = new Vector3(0f, 0.08f, 0f);
            _captureProgressArc = arcObj.AddComponent<LineRenderer>();
            _captureProgressArc.useWorldSpace = false;
            _captureProgressArc.alignment = LineAlignment.TransformZ;
            _captureProgressArc.startWidth = 0.28f;
            _captureProgressArc.endWidth = 0.28f;
            _captureProgressArc.material = new Material(Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color"));
            _captureProgressArc.gameObject.SetActive(false);

            // Золотой 3D-маркер выбора над зданием
            if (_selectionMarker == null)
            {
                _selectionMarker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                _selectionMarker.name = "SelectionMarker";
                _selectionMarker.transform.SetParent(transform, false);
                _selectionMarker.transform.localPosition = new Vector3(0f, _type == BuildingType.Base ? 3.5f : 2.5f, 0f);
                _selectionMarker.transform.localScale = new Vector3(0.85f, 0.85f, 0.85f);
                _selectionMarker.transform.localRotation = Quaternion.Euler(45f, 45f, 45f);

                var mCol = _selectionMarker.GetComponent<Collider>();
                if (mCol != null) DestroyImmediate(mCol);

                var mRen = _selectionMarker.GetComponent<MeshRenderer>();
                mRen.sharedMaterial = MaterialFactory.CreateEmissive("Mat_SelectionMarker", new Color(1.0f, 0.9f, 0.15f, 1.0f), 2f);
                _selectionMarker.SetActive(false);
            }
        }

        public void UpdateOwnerVisuals(PlayerId newOwner)
        {
            _owner = newOwner;
            Color teamColor = GetPlayerColor(newOwner);

            if (_buildingRenderer != null)
            {
                _buildingRenderer.GetPropertyBlock(_propBlock);
                _propBlock.SetColor("_BaseColor", teamColor);
                _propBlock.SetColor("_Color", teamColor);
                _buildingRenderer.SetPropertyBlock(_propBlock);
            }

            if (_captureZoneCircle != null)
            {
                _captureZoneCircle.startColor = teamColor;
                _captureZoneCircle.endColor = teamColor;
                if (_captureZoneCircle.material != null)
                {
                    _captureZoneCircle.material.color = teamColor;
                }
            }
        }

        public void UpdateCaptureVisual(BuildingState state)
        {
            if (_captureLabel == null || state == null) return;
            bool isCapturing = state.CaptureProgressSeconds > 0f || state.IsContested;
            _captureLabel.gameObject.SetActive(isCapturing);
            _captureProgressArc.gameObject.SetActive(isCapturing);
            if (!isCapturing) return;

            float percent = Mathf.Clamp01(state.CaptureProgressSeconds / state.CaptureTimeTotal) * 100f;
            UpdateCaptureArc(percent / 100f, state.IsContested ? Color.yellow : GetPlayerColor(state.CapturingPlayer));
            if (state.IsContested)
            {
                _captureLabel.text = "ЗАХВАТ ОСПАРИВАЕТСЯ";
                _captureLabel.color = Color.yellow;
            }
            else
            {
                _captureLabel.text = $"ЗАХВАТ: {percent:0}%";
                _captureLabel.color = GetPlayerColor(state.CapturingPlayer);
            }
        }

        private void UpdateCaptureArc(float progress, Color color)
        {
            const int segments = 40;
            int points = Mathf.Max(2, Mathf.CeilToInt(segments * progress));
            _captureProgressArc.positionCount = points;
            _captureProgressArc.startColor = color;
            _captureProgressArc.endColor = color;
            for (int index = 0; index < points; index++)
            {
                float angle = Mathf.Deg2Rad * (360f * index / (segments - 1));
                _captureProgressArc.SetPosition(index, new Vector3(Mathf.Cos(angle) * _captureRadius, 0f, Mathf.Sin(angle) * _captureRadius));
            }
        }

        [SerializeField] private GameObject _selectionMarker;
        private bool _isSelected;

        public bool IsSelected => _isSelected;

        private void Update()
        {
            if (_captureLabel != null && _captureLabel.gameObject.activeSelf && UnityEngine.Camera.main != null)
            {
                var cameraTransform = UnityEngine.Camera.main.transform;
                _captureLabel.transform.rotation = Quaternion.LookRotation(cameraTransform.forward, cameraTransform.up);
                float scale = Mathf.Clamp(CameraZoom.ViewHalfHeight(UnityEngine.Camera.main, _captureLabel.transform.position) / 9f, 0.75f, 1.8f);
                _captureLabel.transform.localScale = Vector3.one * scale;
            }
            if (_isSelected && _selectionMarker != null)
            {
                _selectionMarker.transform.Rotate(Vector3.up, 90f * Time.deltaTime, Space.World);
                float hover = Mathf.Sin(Time.time * 4f) * 0.15f;
                float baseY = _type == BuildingType.Base ? 3.5f : 2.5f;
                _selectionMarker.transform.localPosition = new Vector3(0f, baseY + hover, 0f);
            }
        }

        public void SetSelected(bool selected)
        {
            _isSelected = selected;
            if (_selectionMarker != null)
            {
                _selectionMarker.SetActive(selected);
            }

            Color activeColor = selected ? new Color(1.0f, 0.85f, 0.15f) : GetPlayerColor(_owner);
            if (_captureZoneCircle != null)
            {
                _captureZoneCircle.startWidth = selected ? 0.45f : 0.15f;
                _captureZoneCircle.endWidth = selected ? 0.45f : 0.15f;
                _captureZoneCircle.startColor = activeColor;
                _captureZoneCircle.endColor = activeColor;
                if (_captureZoneCircle.material != null)
                {
                    _captureZoneCircle.material.color = activeColor;
                }
            }

            if (_nameLabel != null)
            {
                string baseName = DisplayName;
                _nameLabel.text = selected ? $"★ {baseName} (ВЫБРАН) ★" : baseName;
                _nameLabel.color = selected ? Color.yellow : Color.white;
            }
        }

        public static Color GetPlayerColor(PlayerId player) => player switch
        {
            PlayerId.Player1 => new Color(0.2f, 0.6f, 1.0f),    // Неоновый синий
            PlayerId.Player2 => new Color(1.0f, 0.25f, 0.25f),  // Неоновый красный
            _ => new Color(0.65f, 0.65f, 0.65f)                  // Нейтральный серый
        };

        private static Font GetWorldFont() =>
            Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
    }
}
