using UnityEngine;

namespace Game.Presentation.Rendering
{
    /// <summary>
    /// Общие ресурсы эффектов: мягкие текстуры частиц и колец, пунктир, светящиеся материалы и меш кольца.
    /// Материалы — Sprites/Default с HDR-цветом: вершинный цвет 8-битный, а множитель материала
    /// выводит яркость выше 1, и bloom подсвечивает кольца, лучи и огонь.
    /// </summary>
    public static class FxLibrary
    {
        private static Texture2D _softCircle, _ringProfile, _ringSprite, _dash;
        private static Material _smoke;

        /// <summary>Круглое мягкое пятно: дым, пыль, огонь.</summary>
        public static Texture2D SoftCircle => _softCircle != null ? _softCircle : _softCircle = Radial("FxSoftCircle", r => Smooth(1f - r) * Smooth(1f - r));

        /// <summary>Кольцо для плоской ударной волны.</summary>
        public static Texture2D RingSprite => _ringSprite != null ? _ringSprite : _ringSprite = Radial("FxRing", r => Mathf.Exp(-Mathf.Pow((r - 0.8f) / 0.1f, 2f)));

        /// <summary>Поперечный профиль кольца на земле: яркий центр, мягкие края (v — от внутреннего края к внешнему).</summary>
        public static Texture2D RingProfile
        {
            get
            {
                if (_ringProfile != null) return _ringProfile;
                const int height = 32;
                var pixels = new Color32[4 * height];
                for (int y = 0; y < height; y++)
                {
                    float v = (y + 0.5f) / height;
                    byte alpha = ToByte(Mathf.Exp(-Mathf.Pow((v - 0.5f) / 0.28f, 2f)));
                    for (int x = 0; x < 4; x++) pixels[y * 4 + x] = new Color32(255, 255, 255, alpha);
                }
                return _ringProfile = Create("FxRingProfile", 4, height, pixels, TextureWrapMode.Clamp);
            }
        }

        /// <summary>Штрих пунктира вдоль линии (u), тайлится.</summary>
        public static Texture2D Dash
        {
            get
            {
                if (_dash != null) return _dash;
                const int width = 32, height = 8;
                var pixels = new Color32[width * height];
                for (int y = 0; y < height; y++)
                {
                    float across = Mathf.Abs((y + 0.5f) / height - 0.5f) * 2f;
                    for (int x = 0; x < width; x++)
                    {
                        float u = (x + 0.5f) / width;
                        float dash = Mathf.Clamp01(Mathf.Min(u, 0.55f - u) * 20f);
                        pixels[y * width + x] = new Color32(255, 255, 255, ToByte(dash * Smooth(1f - across)));
                    }
                }
                return _dash = Create("FxDash", width, height, pixels, TextureWrapMode.Repeat);
            }
        }

        /// <summary>Общий материал дыма и пыли (цвет задают частицы).</summary>
        public static Material Smoke => _smoke != null ? _smoke : _smoke = Glow("Fx_Smoke", SoftCircle, Color.white, 3000);

        /// <summary>Прозрачный неосвещаемый материал; color может быть HDR (&gt; 1) для свечения.</summary>
        public static Material Glow(string name, Texture texture, Color color, int renderQueue = 3050)
        {
            Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Transparent");
            return new Material(shader) { name = name, mainTexture = texture, color = color, renderQueue = renderQueue };
        }

        /// <summary>
        /// Плоское кольцо (или дуга при fraction &lt; 1) в плоскости XZ, начало — на +z, по часовой стрелке сверху.
        /// UV: u — вдоль окружности, v — от внутреннего радиуса к внешнему.
        /// </summary>
        public static Mesh Ring(float inner, float outer, float fraction = 1f, int segments = 64, Mesh reuse = null)
        {
            int steps = Mathf.Max(2, Mathf.CeilToInt(segments * Mathf.Clamp01(fraction)));
            var vertices = new Vector3[(steps + 1) * 2];
            var uvs = new Vector2[vertices.Length];
            var triangles = new int[steps * 6];
            for (int i = 0; i <= steps; i++)
            {
                float t = (float)i / steps;
                float angle = t * Mathf.Clamp01(fraction) * Mathf.PI * 2f;
                var direction = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                vertices[i * 2] = direction * inner;
                vertices[i * 2 + 1] = direction * outer;
                uvs[i * 2] = new Vector2(t, 0f);
                uvs[i * 2 + 1] = new Vector2(t, 1f);
                if (i == steps) break;
                int a = i * 2, tri = i * 6;
                triangles[tri] = a; triangles[tri + 1] = a + 1; triangles[tri + 2] = a + 3;
                triangles[tri + 3] = a; triangles[tri + 4] = a + 3; triangles[tri + 5] = a + 2;
            }
            Mesh mesh = reuse != null ? reuse : new Mesh { name = "FxRing" };
            mesh.Clear();
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Объект-кольцо на земле с собственным экземпляром материала (цвет меняется на лету).</summary>
        public static MeshRenderer CreateGroundRing(string name, Transform parent, float inner, float outer, Color color, float height = 0.06f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, height, 0f);
            go.AddComponent<MeshFilter>().sharedMesh = Ring(inner, outer);
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = Glow(name + "_Mat", RingProfile, color);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return renderer;
        }

        /// <summary>
        /// Пыль за движущимся роботом: частицы выпускаются по пройденному расстоянию, поэтому стоящий робот не пылит.
        /// </summary>
        public static ParticleSystem CreateDustTrail(Transform parent, float particlesPerMeter)
        {
            var go = new GameObject("DustTrail");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 0.08f, -0.4f);
            var system = go.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = system.main;
            main.playOnAwake = false;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new Color(0.62f, 0.55f, 0.42f, 0.45f);
            main.gravityModifier = -0.03f;
            main.maxParticles = 80;

            var emission = system.emission;
            emission.rateOverTime = 0f;
            emission.rateOverDistance = particlesPerMeter;
            var shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.2f;

            var size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.6f, 1f, 1.8f));
            var color = system.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
            color.color = gradient;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = Smoke;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            system.Play();
            return system;
        }

        private static Texture2D Radial(string name, System.Func<float, float> alphaByRadius)
        {
            const int size = 64;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float r = new Vector2((x + 0.5f) / size * 2f - 1f, (y + 0.5f) / size * 2f - 1f).magnitude;
                    pixels[y * size + x] = new Color32(255, 255, 255, ToByte(r >= 1f ? 0f : alphaByRadius(r)));
                }
            }
            return Create(name, size, size, pixels, TextureWrapMode.Clamp);
        }

        private static Texture2D Create(string name, int width, int height, Color32[] pixels, TextureWrapMode wrap)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, true) { name = name, wrapMode = wrap, filterMode = FilterMode.Bilinear };
            texture.SetPixels32(pixels);
            texture.Apply(true, true);
            return texture;
        }

        private static float Smooth(float t) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
        private static byte ToByte(float value) => (byte)Mathf.RoundToInt(Mathf.Clamp01(value) * 255f);
    }
}
