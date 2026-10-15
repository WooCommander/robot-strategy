using System.Collections.Generic;
using UnityEngine;
using Game.Core.Types;

namespace Game.Presentation.Views
{
    public sealed class CombatFeedbackView : MonoBehaviour
    {
        private readonly Queue<TextMesh> _free = new Queue<TextMesh>();
        private readonly List<ActiveText> _active = new List<ActiveText>();

        public void ShowDamage(SimVector2 position, int damage)
        {
            TextMesh text = _free.Count > 0 ? _free.Dequeue() : CreateText();
            text.text = $"-{damage}";
            text.color = new Color(1f, 0.25f, 0.2f);
            text.transform.position = new Vector3(position.X, 2.4f, position.Y);
            text.gameObject.SetActive(true);
            _active.Add(new ActiveText { Text = text, StartedAt = Time.time });
        }

        private void Update()
        {
            var cameraTransform = UnityEngine.Camera.main?.transform;
            for (int index = _active.Count - 1; index >= 0; index--)
            {
                ActiveText active = _active[index];
                float age = Time.time - active.StartedAt;
                if (cameraTransform != null) active.Text.transform.rotation = Quaternion.LookRotation(cameraTransform.forward, cameraTransform.up);
                active.Text.transform.position += Vector3.up * Time.deltaTime * 0.7f;
                active.Text.color = new Color(1f, 0.25f, 0.2f, Mathf.Clamp01(1f - age / 0.7f));
                if (age < 0.7f) continue;
                active.Text.gameObject.SetActive(false);
                _free.Enqueue(active.Text);
                _active.RemoveAt(index);
            }
        }

        private TextMesh CreateText()
        {
            var go = new GameObject("PooledDamageText");
            go.transform.SetParent(transform, false);
            var text = go.AddComponent<TextMesh>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.fontSize = 32;
            text.characterSize = 0.13f;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            go.SetActive(false);
            return text;
        }

        private struct ActiveText { public TextMesh Text; public float StartedAt; }
    }
}
