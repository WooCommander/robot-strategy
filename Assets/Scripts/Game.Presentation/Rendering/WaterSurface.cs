using UnityEngine;

namespace Game.Presentation.Rendering
{
    /// <summary>Сдвигает UV воды: карта нормалей URP/Lit использует преобразование _BaseMap, и рябь течёт.</summary>
    public sealed class WaterSurface : MonoBehaviour
    {
        [SerializeField] private Vector2 _flow = new Vector2(0.015f, 0.035f);

        private Material _material;

        public void Initialize(Material material) => _material = material;

        private void Update()
        {
            if (_material == null) return;
            Vector2 offset = _flow * Time.time;
            offset.x -= Mathf.Floor(offset.x);
            offset.y -= Mathf.Floor(offset.y);
            _material.SetTextureOffset("_BaseMap", offset);
        }
    }
}
