using System.Collections.Generic;
using UnityEngine;
using Game.Core.Types;

namespace Game.Presentation.Views
{
    /// <summary>Переиспользует краткоживущие VFX выстрелов, избегая GameObject/Material аллокаций в бою.</summary>
    public sealed class WeaponEffectsPool : MonoBehaviour
    {
        private const float EffectDuration = 0.12f;
        private readonly Queue<LineRenderer> _freeTraces = new Queue<LineRenderer>();
        private readonly Queue<Transform> _freeImpacts = new Queue<Transform>();
        private readonly List<ActiveEffect> _active = new List<ActiveEffect>();
        private readonly MaterialPropertyBlock _impactProperties = new MaterialPropertyBlock();
        private Material _traceMaterial;
        private Material _impactMaterial;

        public void Initialize(int prewarmCount = 16)
        {
            if (_traceMaterial != null) return;
            _traceMaterial = new Material(Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color"));
            _impactMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color"));
            for (int index = 0; index < prewarmCount; index++)
            {
                _freeTraces.Enqueue(CreateTrace());
                _freeImpacts.Enqueue(CreateImpact());
            }
        }

        public void PlayLaunch(SimVector2 start, SimVector2 aimPosition, WeaponType weaponType)
        {
            Initialize();
            Color color = GetWeaponColor(weaponType);
            LineRenderer trace = _freeTraces.Count > 0 ? _freeTraces.Dequeue() : CreateTrace();
            trace.startColor = color;
            trace.endColor = color;
            trace.startWidth = weaponType == WeaponType.AreaMissile ? 0.16f : weaponType == WeaponType.Phaser ? 0.12f : 0.08f;
            trace.endWidth = trace.startWidth;
            trace.SetPosition(0, new Vector3(start.X, 1.1f, start.Y));
            trace.SetPosition(1, new Vector3(aimPosition.X, 1.1f, aimPosition.Y));
            trace.gameObject.SetActive(true);
            _active.Add(new ActiveEffect { Trace = trace, ReleaseAt = Time.time + EffectDuration });
        }

        public void PlayImpact(SimVector2 impactPosition, WeaponType weaponType)
        {
            PlayFlash(impactPosition, GetWeaponColor(weaponType), weaponType == WeaponType.AreaMissile ? 0.8f : 0.35f, EffectDuration);
        }

        public void PlayNuclearBlast(SimVector2 center, float radius)
        {
            PlayFlash(center, new Color(1f, 0.95f, 0.6f), radius * 2f, EffectDuration * 4f);
        }

        private void PlayFlash(SimVector2 position, Color color, float scale, float duration)
        {
            Initialize();
            Transform impact = _freeImpacts.Count > 0 ? _freeImpacts.Dequeue() : null;
            // Объект из пула мог быть уничтожен вместе со сценой: создаём новый до настройки.
            if (impact == null || impact.GetComponent<Renderer>() == null) impact = CreateImpact();
            impact.position = new Vector3(position.X, 1.1f, position.Y);
            impact.localScale = Vector3.one * scale;
            _impactProperties.SetColor("_Color", color);
            _impactProperties.SetColor("_BaseColor", color);
            impact.GetComponent<Renderer>().SetPropertyBlock(_impactProperties);
            impact.gameObject.SetActive(true);
            _active.Add(new ActiveEffect { Impact = impact, ReleaseAt = Time.time + duration });
        }

        private static Color GetWeaponColor(WeaponType weaponType) => weaponType switch
        {
            WeaponType.AreaMissile => new Color(1f, 0.3f, 0.1f),
            WeaponType.Phaser => new Color(0.3f, 0.95f, 1f),
            _ => new Color(1f, 0.85f, 0.2f)
        };

        private void Update()
        {
            for (int index = _active.Count - 1; index >= 0; index--)
            {
                ActiveEffect effect = _active[index];
                if (Time.time < effect.ReleaseAt) continue;
                if (effect.Trace != null)
                {
                    effect.Trace.gameObject.SetActive(false);
                    _freeTraces.Enqueue(effect.Trace);
                }
                if (effect.Impact != null)
                {
                    effect.Impact.gameObject.SetActive(false);
                    _freeImpacts.Enqueue(effect.Impact);
                }
                _active.RemoveAt(index);
            }
        }

        private LineRenderer CreateTrace()
        {
            var go = new GameObject("PooledWeaponTrace");
            go.transform.SetParent(transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.sharedMaterial = _traceMaterial;
            go.SetActive(false);
            return line;
        }

        private Transform CreateImpact()
        {
            GameObject impact = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            impact.name = "PooledWeaponImpact";
            impact.transform.SetParent(transform, false);
            impact.GetComponent<Collider>().enabled = false;
            impact.GetComponent<Renderer>().sharedMaterial = _impactMaterial;
            impact.SetActive(false);
            return impact.transform;
        }

        private void OnDestroy()
        {
            if (_traceMaterial != null) Destroy(_traceMaterial);
            if (_impactMaterial != null) Destroy(_impactMaterial);
        }

        private struct ActiveEffect
        {
            public LineRenderer Trace;
            public Transform Impact;
            public float ReleaseAt;
        }
    }
}
