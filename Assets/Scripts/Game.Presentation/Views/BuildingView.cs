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
        [SerializeField] private MeshRenderer _captureZoneCircle;
        [SerializeField] private TextMesh _nameLabel;
        [SerializeField] private TextMesh _captureLabel;
        [SerializeField] private MeshRenderer _captureProgressArc;


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

            // Фасад смотрит туда, откуда выезжают роботы; у заводов точки выхода нет — к камере (на юг).
            var facing = new Vector2(def.SpawnPoint.X - def.Position.X, def.SpawnPoint.Y - def.Position.Y);
            float yaw = facing.sqrMagnitude > 0.01f ? Mathf.Atan2(facing.x, facing.y) * Mathf.Rad2Deg : 180f;

            CreateVisuals(yaw);
            UpdateOwnerVisuals(_owner);
        }

        private void CreateVisuals(float modelYaw)
        {
            // Корпус — процедурная модель, если в префабе не назначен свой рендер.
            if (_buildingRenderer == null)
            {
                var body = new GameObject("Model");
                body.transform.SetParent(transform, false);
                body.transform.localRotation = Quaternion.Euler(0f, modelYaw, 0f);
                body.AddComponent<MeshFilter>().sharedMesh = BuildingModelBuilder.GetMesh(_type);
                _buildingRenderer = body.AddComponent<MeshRenderer>();
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

            // Зона захвата — тонкое светящееся кольцо цвета владельца; при выборе поверх загорается широкое жёлтое.
            if (_captureZoneCircle == null)
                _captureZoneCircle = FxLibrary.CreateGroundRing("CaptureZone", transform, _captureRadius - 0.07f, _captureRadius + 0.07f, Color.white);
            _selectionRing = FxLibrary.CreateGroundRing("SelectionRing", transform, _captureRadius - 0.22f, _captureRadius + 0.22f, SelectedRingColor, 0.07f);
            _selectionRing.enabled = false;

            // Название здания
            if (_nameLabel == null)
            {
                var labelObj = new GameObject("Label");
                labelObj.transform.SetParent(transform, false);
                labelObj.transform.localPosition = new Vector3(0f, _type == BuildingType.Base ? 3.3f : 2.95f, 0f);
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
            captureLabelObj.transform.localPosition = new Vector3(0f, _type == BuildingType.Base ? 4.0f : 3.45f, 0f);
            _captureLabel = captureLabelObj.AddComponent<TextMesh>();
            _captureLabel.font = GetWorldFont();
            _captureLabel.fontSize = 28;
            _captureLabel.characterSize = 0.11f;
            _captureLabel.alignment = TextAlignment.Center;
            _captureLabel.anchor = TextAnchor.MiddleCenter;
            _captureLabel.gameObject.SetActive(false);

            // Дуга прогресса захвата снаружи зоны; меш пересобирается при изменении процента.
            _captureProgressArc = FxLibrary.CreateGroundRing("CaptureProgressArc", transform, _captureRadius + 0.12f, _captureRadius + 0.4f, Color.white, 0.08f);
            _captureArcMesh = _captureProgressArc.GetComponent<MeshFilter>().sharedMesh;
            _captureProgressArc.gameObject.SetActive(false);

            // Золотой 3D-маркер выбора над зданием
            if (_selectionMarker == null)
            {
                _selectionMarker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                _selectionMarker.name = "SelectionMarker";
                _selectionMarker.transform.SetParent(transform, false);
                _selectionMarker.transform.localPosition = new Vector3(0f, _type == BuildingType.Base ? 4.2f : 3.6f, 0f);
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

            // Пояса, баки и огни окрашены материалом стороны: захват меняет только материал.
            if (_buildingRenderer != null) _buildingRenderer.sharedMaterial = TeamPalette.Get(newOwner, teamColor);

            if (_captureZoneCircle != null) _captureZoneCircle.sharedMaterial.color = ZoneColor(teamColor);
        }

        /// <summary>HDR-цвет кольца зоны: чуть ярче 1, чтобы bloom дал мягкое свечение.</summary>
        private static Color ZoneColor(Color teamColor) => new Color(teamColor.r * 1.5f, teamColor.g * 1.5f, teamColor.b * 1.5f, 0.85f);

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
            _captureProgressArc.sharedMaterial.color = new Color(color.r * 2f, color.g * 2f, color.b * 2f, 1f);
            // Пересобираем дугу только при видимом изменении — не каждый кадр.
            if (Mathf.Abs(progress - _shownArcProgress) < 0.005f) return;
            _shownArcProgress = progress;
            FxLibrary.Ring(_captureRadius + 0.12f, _captureRadius + 0.4f, Mathf.Max(0.01f, progress), 64, _captureArcMesh);
        }

        private static readonly Color SelectedRingColor = new Color(2.2f, 1.8f, 0.35f, 0.9f);

        [SerializeField] private GameObject _selectionMarker;
        private MeshRenderer _selectionRing;
        private Mesh _captureArcMesh;
        private float _shownArcProgress = -1f;
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
                float baseY = _type == BuildingType.Base ? 4.2f : 3.6f;
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

            if (_selectionRing != null) _selectionRing.enabled = selected;

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
