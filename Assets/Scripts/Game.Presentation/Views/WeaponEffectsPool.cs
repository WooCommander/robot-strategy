using System.Collections.Generic;
using UnityEngine;
using Game.Core.Types;
using Game.Presentation.Rendering;

namespace Game.Presentation.Views
{
    /// <summary>
    /// Боевые эффекты без аллокаций в бою: лучи выстрелов из пула и по одной системе частиц на тип
    /// (искры, вспышка, огонь, дым, обломки, ударная волна) в мировых координатах. Взрыв — это Emit
    /// нужного числа частиц в точку, а не отдельный объект.
    /// </summary>
    public sealed class WeaponEffectsPool : MonoBehaviour
    {
        private const float TraceDuration = 0.12f;
        private readonly Queue<LineRenderer> _freeTraces = new Queue<LineRenderer>();
        private readonly List<ActiveTrace> _active = new List<ActiveTrace>();
        private readonly Dictionary<WeaponType, Material> _traceMaterials = new Dictionary<WeaponType, Material>();
        private readonly List<Object> _ownedAssets = new List<Object>();

        private ParticleSystem _sparks, _flash, _fire, _smoke, _debris, _shockwave;
        private bool _initialized;

        public void Initialize(int prewarmCount = 16)
        {
            if (_initialized) return;
            _initialized = true;
            foreach (WeaponType type in new[] { WeaponType.DirectCannon, WeaponType.AreaMissile, WeaponType.Phaser })
                _traceMaterials[type] = Own(FxLibrary.Glow("Fx_Trace_" + type, Texture2D.whiteTexture, GetWeaponColor(type) * 2.2f));
            for (int index = 0; index < prewarmCount; index++) _freeTraces.Enqueue(CreateTrace());

            Material sparkMaterial = Own(FxLibrary.Glow("Fx_Spark", FxLibrary.SoftCircle, new Color(2.4f, 2.1f, 1.6f)));
            Material fireMaterial = Own(FxLibrary.Glow("Fx_Fire", FxLibrary.SoftCircle, new Color(2.6f, 1.5f, 0.7f)));
            Material shockMaterial = Own(FxLibrary.Glow("Fx_Shockwave", FxLibrary.RingSprite, new Color(1.8f, 1.5f, 1.1f)));
            Material debrisMaterial = Own(MaterialFactory.CreateLit("Fx_Debris", new Color(0.16f, 0.17f, 0.19f), 0.6f, 0.3f));

            _sparks = CreateSystem("Sparks", sparkMaterial, ParticleSystemRenderMode.Stretch, lifetime: (0.2f, 0.45f), speed: (3f, 7f), size: (0.04f, 0.08f), gravity: 1.2f);
            var sparkRenderer = _sparks.GetComponent<ParticleSystemRenderer>();
            sparkRenderer.velocityScale = 0.04f;
            sparkRenderer.lengthScale = 1.5f;
            SetFade(_sparks, Color.white, new Color(1f, 0.45f, 0.15f));

            _flash = CreateSystem("Flash", fireMaterial, ParticleSystemRenderMode.Billboard, lifetime: (0.06f, 0.1f), speed: (0f, 0.2f), size: (0.35f, 0.5f), gravity: 0f);
            SetFade(_flash, Color.white, Color.white);
            _fire = CreateSystem("Fire", fireMaterial, ParticleSystemRenderMode.Billboard, lifetime: (0.3f, 0.55f), speed: (0.6f, 2f), size: (0.35f, 0.7f), gravity: -0.2f);
            SetSizeOverLifetime(_fire, 0.6f, 1.6f);
            SetFade(_fire, new Color(1f, 0.85f, 0.5f), new Color(0.6f, 0.15f, 0.05f));

            _smoke = CreateSystem("Smoke", FxLibrary.Smoke, ParticleSystemRenderMode.Billboard, lifetime: (1.2f, 2.2f), speed: (0.3f, 0.9f), size: (0.4f, 0.8f), gravity: -0.08f);
            SetSizeOverLifetime(_smoke, 0.7f, 2.4f);
            SetFade(_smoke, new Color(0.32f, 0.31f, 0.3f), new Color(0.5f, 0.48f, 0.45f));
            var smokeMain = _smoke.main;
            smokeMain.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

            _debris = CreateSystem("Debris", debrisMaterial, ParticleSystemRenderMode.Mesh, lifetime: (0.9f, 1.4f), speed: (2.5f, 5f), size: (0.06f, 0.16f), gravity: 2.5f);
            var debrisRenderer = _debris.GetComponent<ParticleSystemRenderer>();
            debrisRenderer.mesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            debrisRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            var debrisMain = _debris.main;
            debrisMain.startRotation3D = true;
            debrisMain.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            debrisMain.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            debrisMain.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var spin = _debris.rotationOverLifetime;
            spin.enabled = true;
            spin.separateAxes = true;
            spin.x = new ParticleSystem.MinMaxCurve(-8f, 8f);
            spin.y = new ParticleSystem.MinMaxCurve(-8f, 8f);
            spin.z = new ParticleSystem.MinMaxCurve(-8f, 8f);
            var debrisShape = _debris.shape;
            debrisShape.shapeType = ParticleSystemShapeType.Hemisphere;

            // Ударная волна лежит на земле и расширяется: горизонтальный billboard с текстурой кольца.
            _shockwave = CreateSystem("Shockwave", shockMaterial, ParticleSystemRenderMode.HorizontalBillboard, lifetime: (0.45f, 0.45f), speed: (0f, 0f), size: (1f, 1f), gravity: 0f);
            SetSizeOverLifetime(_shockwave, 0.15f, 1f);
            SetFade(_shockwave, Color.white, Color.white);
            var shockShape = _shockwave.shape;
            shockShape.enabled = false;
        }

        /// <summary>Выстрел: луч от ствола к цели и дульная вспышка.</summary>
        public void PlayLaunch(Vector3 muzzle, Vector3 aim, WeaponType weaponType)
        {
            Initialize();
            LineRenderer trace = _freeTraces.Count > 0 ? _freeTraces.Dequeue() : CreateTrace();
            if (trace == null) trace = CreateTrace();
            trace.sharedMaterial = _traceMaterials[weaponType];
            trace.startWidth = weaponType == WeaponType.AreaMissile ? 0.12f : weaponType == WeaponType.Phaser ? 0.1f : 0.05f;
            trace.endWidth = weaponType == WeaponType.Phaser ? 0.06f : trace.startWidth * 0.5f;
            trace.SetPosition(0, muzzle);
            trace.SetPosition(1, aim);
            trace.gameObject.SetActive(true);
            _active.Add(new ActiveTrace { Trace = trace, ReleaseAt = Time.time + TraceDuration });

            Emit(_flash, muzzle, 1, GetWeaponColor(weaponType), weaponType == WeaponType.AreaMissile ? 0.6f : 0.4f);
            if (weaponType == WeaponType.AreaMissile) Emit(_smoke, muzzle, 2, Color.white, 0.35f);
        }

        public void PlayImpact(Vector3 position, WeaponType weaponType)
        {
            Initialize();
            switch (weaponType)
            {
                case WeaponType.AreaMissile:
                    Emit(_fire, position, 10, Color.white);
                    Emit(_smoke, position, 5, Color.white);
                    Emit(_sparks, position, 14, new Color(1f, 0.75f, 0.4f));
                    Emit(_shockwave, Ground(position), 1, Color.white, 5f);
                    break;
                case WeaponType.Phaser:
                    Emit(_flash, position, 1, GetWeaponColor(weaponType), 0.7f);
                    Emit(_sparks, position, 10, GetWeaponColor(weaponType));
                    break;
                default:
                    Emit(_sparks, position, 8, new Color(1f, 0.85f, 0.5f));
                    Emit(_smoke, position, 2, Color.white, 0.35f);
                    break;
            }
        }

        /// <summary>Гибель робота: огненный шар, дым, искры, обломки и волна по земле.</summary>
        public void PlayExplosion(Vector3 position)
        {
            Initialize();
            Emit(_flash, position, 1, Color.white, 2.2f);
            Emit(_fire, position, 24, Color.white);
            Emit(_smoke, position, 14, Color.white);
            Emit(_sparks, position, 22, new Color(1f, 0.7f, 0.35f));
            Emit(_debris, position, 10, Color.white);
            Emit(_shockwave, Ground(position), 1, Color.white, 4f);
        }

        public void PlayNuclearBlast(Vector3 center, float radius)
        {
            Initialize();
            Emit(_flash, center, 1, Color.white, radius * 1.6f);
            for (int ring = 0; ring < 3; ring++) Emit(_fire, center + Vector3.up * ring * 0.6f, 30, Color.white, 1.2f + ring * 0.5f);
            Emit(_smoke, center, 40, Color.white, 1.6f);
            Emit(_sparks, center, 40, new Color(1f, 0.9f, 0.6f));
            Emit(_debris, center, 20, Color.white);
            Emit(_shockwave, Ground(center), 1, Color.white, radius * 2.4f);
        }

        private static Vector3 Ground(Vector3 position) => new Vector3(position.x, Mathf.Max(0f, position.y - 0.6f) + 0.08f, position.z);

        private static void Emit(ParticleSystem system, Vector3 position, int count, Color tint, float size = -1f)
        {
            if (system == null) return;
            var parameters = new ParticleSystem.EmitParams { position = position, applyShapeToPosition = true, startColor = tint };
            if (size > 0f) parameters.startSize = size;
            system.Emit(parameters, count);
        }

        private ParticleSystem CreateSystem(string name, Material material, ParticleSystemRenderMode mode,
            (float min, float max) lifetime, (float min, float max) speed, (float min, float max) size, float gravity)
        {
            var go = new GameObject("Fx_" + name);
            go.transform.SetParent(transform, false);
            var system = go.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = system.main;
            main.playOnAwake = false;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime.min, lifetime.max);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed.min, speed.max);
            main.startSize = new ParticleSystem.MinMaxCurve(size.min, size.max);
            main.gravityModifier = gravity;
            main.maxParticles = 600;

            // Частицы выпускаются только через Emit.
            var emission = system.emission;
            emission.enabled = false;
            var shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.15f;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = mode;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            system.Play();
            return system;
        }

        private static void SetSizeOverLifetime(ParticleSystem system, float start, float end)
        {
            var module = system.sizeOverLifetime;
            module.enabled = true;
            module.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, start, 1f, end));
        }

        /// <summary>Цвет от start к end с плавным появлением и исчезновением (умножается на цвет частицы).</summary>
        private static void SetFade(ParticleSystem system, Color start, Color end)
        {
            var module = system.colorOverLifetime;
            module.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(start, 0f), new GradientColorKey(end, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.08f), new GradientAlphaKey(0.6f, 0.5f), new GradientAlphaKey(0f, 1f) });
            module.color = gradient;
        }

        private static Color GetWeaponColor(WeaponType weaponType) => weaponType switch
        {
            WeaponType.AreaMissile => new Color(1f, 0.45f, 0.15f),
            WeaponType.Phaser => new Color(0.3f, 0.95f, 1f),
            _ => new Color(1f, 0.85f, 0.3f)
        };

        private void Update()
        {
            for (int index = _active.Count - 1; index >= 0; index--)
            {
                ActiveTrace effect = _active[index];
                if (Time.time < effect.ReleaseAt) continue;
                if (effect.Trace != null)
                {
                    effect.Trace.gameObject.SetActive(false);
                    _freeTraces.Enqueue(effect.Trace);
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
            line.startColor = line.endColor = Color.white;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.SetActive(false);
            return line;
        }

        private T Own<T>(T asset) where T : Object
        {
            _ownedAssets.Add(asset);
            return asset;
        }

        private void OnDestroy()
        {
            foreach (Object asset in _ownedAssets)
            {
                if (asset != null) Destroy(asset);
            }
        }

        private struct ActiveTrace
        {
            public LineRenderer Trace;
            public float ReleaseAt;
        }
    }
}
