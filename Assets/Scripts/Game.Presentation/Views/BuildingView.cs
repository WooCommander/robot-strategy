using UnityEngine;
using Game.Core.Models;
using Game.Core.Types;

namespace Game.Presentation.Views
{
    public class BuildingView : MonoBehaviour
    {
        [Header("State")]
        [SerializeField] private int _entityId;
        [SerializeField] private BuildingType _type;
        [SerializeField] private PlayerId _owner = PlayerId.Neutral;
        [SerializeField] private float _captureRadius = 3f;

        [Header("Visuals")]
        [SerializeField] private MeshRenderer _buildingRenderer;
        [SerializeField] private LineRenderer _captureZoneCircle;
        [SerializeField] private TextMesh _nameLabel;

        private MaterialPropertyBlock _propBlock;

        public int EntityId => _entityId;
        public BuildingType Type => _type;
        public PlayerId Owner => _owner;

        public void Initialize(BuildingSpawnDefinition def)
        {
            _entityId = def.EntityIdValue;
            _type = def.Type;
            _owner = def.InitialOwner;
            _captureRadius = def.CaptureRadius;

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
                var bShader = Shader.Find("Standard") ?? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Unlit/Color");
                _buildingRenderer.sharedMaterial = new Material(bShader);
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
                _nameLabel.fontSize = 24;
                _nameLabel.characterSize = 0.1f;
                _nameLabel.alignment = TextAlignment.Center;
                _nameLabel.anchor = TextAnchor.MiddleCenter;
                _nameLabel.text = _type == BuildingType.Base ? $"БАЗА #{_entityId}" : $"ЗАВОД #{_entityId}";
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

        public static Color GetPlayerColor(PlayerId player) => player switch
        {
            PlayerId.Player1 => new Color(0.2f, 0.6f, 1.0f),    // Неоновый синий
            PlayerId.Player2 => new Color(1.0f, 0.25f, 0.25f),  // Неоновый красный
            _ => new Color(0.65f, 0.65f, 0.65f)                  // Нейтральный серый
        };
    }
}
