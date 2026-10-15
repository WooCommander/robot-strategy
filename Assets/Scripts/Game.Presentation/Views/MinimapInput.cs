using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Game.Presentation.Views
{
    /// <summary>Нажатие и перетаскивание по радару сообщают точку в долях карты (0..1 по обеим осям).</summary>
    public sealed class MinimapInput : MonoBehaviour, IPointerDownHandler, IDragHandler
    {
        public event Action<Vector2> OnPointRequested;

        public void OnPointerDown(PointerEventData eventData) => Report(eventData);
        public void OnDrag(PointerEventData eventData) => Report(eventData);

        private void Report(PointerEventData eventData)
        {
            var rect = (RectTransform)transform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, eventData.position, eventData.pressEventCamera, out Vector2 local)) return;
            Vector2 size = rect.rect.size;
            if (size.x <= 0f || size.y <= 0f) return;
            Vector2 normalized = (local - rect.rect.min) / size;
            OnPointRequested?.Invoke(new Vector2(Mathf.Clamp01(normalized.x), Mathf.Clamp01(normalized.y)));
        }
    }
}
