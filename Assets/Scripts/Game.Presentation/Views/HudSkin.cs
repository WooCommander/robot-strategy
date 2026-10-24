using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Presentation.Views
{
    public enum HudIcon { Flag, Crosshair, Eye, Shield, Stop, Radiation, Gear, Wrench, Hangar }

    /// <summary>
    /// Оформление HUD: палитра, шрифты, спрайты панелей и кнопок со срезанными углами и векторные иконки.
    /// Всё генерируется в коде, поэтому HUD не зависит от импортированных картинок; меняется в одном месте.
    /// </summary>
    public static class HudSkin
    {
        public static readonly Color PanelFill = new Color(0.03f, 0.065f, 0.11f, 0.93f);
        public static readonly Color PanelFrame = new Color(0.28f, 0.72f, 1f, 0.55f);
        public static readonly Color CardFill = new Color(0.035f, 0.075f, 0.13f, 0.98f);
        public static readonly Color InsetFill = new Color(0.06f, 0.12f, 0.2f, 0.98f);
        public static readonly Color Accent = new Color(0.38f, 0.86f, 1f);
        public static readonly Color Title = new Color(0.62f, 0.92f, 1f);
        public static readonly Color Muted = new Color(0.62f, 0.72f, 0.82f);
        public static readonly Color ButtonIdle = new Color(0.08f, 0.18f, 0.3f);
        public static readonly Color ButtonActive = new Color(0.13f, 0.52f, 0.86f);
        public static readonly Color ButtonPrimary = new Color(0.2f, 0.62f, 0.95f);
        public static readonly Color ButtonDanger = new Color(0.6f, 0.33f, 0.06f);
        public static readonly Color ButtonFrame = new Color(0.55f, 0.85f, 1f, 0.35f);

        private const int SpriteSize = 64;
        private const int PanelCut = 12;
        private const int ButtonCut = 7;

        private static Sprite _panel, _panelFrame, _button, _buttonFrame;
        private static Font _regular, _bold;
        private static readonly Dictionary<HudIcon, Sprite> Icons = new Dictionary<HudIcon, Sprite>();

        public static Sprite Panel => _panel != null ? _panel : _panel = CreateChamfer("HudPanel", PanelCut, 0f, false);
        public static Sprite PanelFrameSprite => _panelFrame != null ? _panelFrame : _panelFrame = CreateChamfer("HudPanelFrame", PanelCut, 2f, false);
        public static Sprite Button => _button != null ? _button : _button = CreateChamfer("HudButton", ButtonCut, 0f, true);
        public static Sprite ButtonFrameSprite => _buttonFrame != null ? _buttonFrame : _buttonFrame = CreateChamfer("HudButtonFrame", ButtonCut, 1.5f, false);

        /// <summary>Шрифт Play (OFL) из Resources/Fonts; без него — встроенный.</summary>
        public static Font GetFont(bool bold)
        {
            if (bold && _bold != null) return _bold;
            if (!bold && _regular != null) return _regular;
            Font font = Resources.Load<Font>(bold ? "Fonts/Play-Bold" : "Fonts/Play-Regular")
                        ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (bold) _bold = font; else _regular = font;
            return font;
        }

        /// <summary>Есть ли у шрифта настоящее начертание bold: тогда синтетический жирный не нужен.</summary>
        public static bool HasRealBold => GetFont(true) != GetFont(false);

        public static Sprite GetIcon(HudIcon icon)
        {
            if (Icons.TryGetValue(icon, out Sprite cached) && cached != null) return cached;
            Sprite sprite = PaintIcon("Icon_" + icon, GetIconShape(icon));
            Icons[icon] = sprite;
            return sprite;
        }

        // ───────────────────────── Спрайты панелей ─────────────────────────

        /// <summary>
        /// Прямоугольник со срезанными левым верхним и правым нижним углами. borderWidth &gt; 0 — только рамка.
        /// Заливка кнопки светлее сверху: после тонировки цветом кнопки это читается как фаска.
        /// </summary>
        private static Sprite CreateChamfer(string name, int cut, float borderWidth, bool gradient)
        {
            var pixels = new Color32[SpriteSize * SpriteSize];
            float s = SpriteSize;
            for (int y = 0; y < SpriteSize; y++)
            {
                for (int x = 0; x < SpriteSize; x++)
                {
                    float px = x + 0.5f, py = y + 0.5f;
                    float d = Mathf.Min(Mathf.Min(px, py), Mathf.Min(s - px, s - py));
                    d = Mathf.Min(d, ((s - px) + py - cut) * 0.7071f);   // правый нижний срез
                    d = Mathf.Min(d, (px + (s - py) - cut) * 0.7071f);   // левый верхний срез
                    float coverage = Mathf.Clamp01(d + 0.5f);
                    if (borderWidth > 0f) coverage -= Mathf.Clamp01(d - borderWidth + 0.5f);
                    float shade = gradient ? Mathf.Lerp(0.78f, 1f, py / s) : 1f;
                    byte value = (byte)Mathf.RoundToInt(shade * 255f);
                    pixels[y * SpriteSize + x] = new Color32(value, value, value, (byte)Mathf.RoundToInt(Mathf.Clamp01(coverage) * 255f));
                }
            }
            var texture = new Texture2D(SpriteSize, SpriteSize, TextureFormat.RGBA32, false)
            {
                name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            float slice = cut + 3f;
            return Sprite.Create(texture, new Rect(0, 0, SpriteSize, SpriteSize), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(slice, slice, slice, slice));
        }

        // ───────────────────────── Иконки ─────────────────────────

        /// <summary>Рисует иконку по функции расстояния в координатах [−1, 1] (отрицательно внутри).</summary>
        private static Sprite PaintIcon(string name, Func<Vector2, float> shape)
        {
            const int size = 64;
            const float half = size * 0.5f;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2((x + 0.5f) / half - 1f, (y + 0.5f) / half - 1f);
                    float alpha = Mathf.Clamp01(0.5f - shape(p) * half);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = name, wrapMode = TextureWrapMode.Clamp };
            texture.SetPixels32(pixels);
            texture.Apply(true, true);
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        private static Func<Vector2, float> GetIconShape(HudIcon icon) => icon switch
        {
            HudIcon.Flag => p => Mathf.Min(
                Segment(p, new Vector2(-0.55f, -0.85f), new Vector2(-0.55f, 0.85f), 0.08f),
                Polygon(p, new Vector2(-0.5f, 0.85f), new Vector2(0.75f, 0.52f), new Vector2(-0.5f, 0.15f))),
            HudIcon.Crosshair => p => Mathf.Min(Mathf.Min(
                    Mathf.Abs(p.magnitude - 0.55f) - 0.07f,
                    Mathf.Min(Segment(p, new Vector2(0f, 0.32f), new Vector2(0f, 0.9f), 0.07f), Segment(p, new Vector2(0f, -0.32f), new Vector2(0f, -0.9f), 0.07f))),
                Mathf.Min(Mathf.Min(Segment(p, new Vector2(0.32f, 0f), new Vector2(0.9f, 0f), 0.07f), Segment(p, new Vector2(-0.32f, 0f), new Vector2(-0.9f, 0f), 0.07f)),
                    p.magnitude - 0.1f)),
            HudIcon.Eye => p =>
            {
                float almond = Mathf.Max((p - new Vector2(0f, -0.7f)).magnitude - 1.15f, (p - new Vector2(0f, 0.7f)).magnitude - 1.15f);
                return Mathf.Min(Mathf.Abs(almond) - 0.07f, p.magnitude - 0.27f);
            },
            HudIcon.Shield => p => Mathf.Max(
                Polygon(p, new Vector2(-0.7f, 0.78f), new Vector2(0.7f, 0.78f), new Vector2(0.7f, 0.05f), new Vector2(0f, -0.88f), new Vector2(-0.7f, 0.05f)),
                -Polygon(p, new Vector2(-0.06f, 0.62f), new Vector2(0.06f, 0.62f), new Vector2(0.06f, -0.6f), new Vector2(-0.06f, -0.6f))),
            HudIcon.Stop => p => Box(p, Vector2.zero, new Vector2(0.52f, 0.52f)) - 0.1f,
            HudIcon.Radiation => p => Mathf.Min(p.magnitude - 0.15f, RadiationBlades(p)),
            HudIcon.Gear => p =>
            {
                float angle = Mathf.Atan2(p.y, p.x);
                float radius = Mathf.Cos(angle * 8f) > 0f ? 0.8f : 0.62f;
                return Mathf.Max(p.magnitude - radius, -(p.magnitude - 0.26f));
            },
            HudIcon.Wrench => p => Mathf.Max(
                Mathf.Min(Segment(p, new Vector2(-0.62f, -0.62f), new Vector2(0.2f, 0.2f), 0.13f), (p - new Vector2(0.38f, 0.38f)).magnitude - 0.36f),
                -((p - new Vector2(0.62f, 0.62f)).magnitude - 0.22f)),
            _ => p => Mathf.Max(
                Mathf.Min(Polygon(p, new Vector2(-0.85f, 0.1f), new Vector2(0f, 0.82f), new Vector2(0.85f, 0.1f)), Box(p, new Vector2(0f, -0.38f), new Vector2(0.62f, 0.48f))),
                -Box(p, new Vector2(0f, -0.56f), new Vector2(0.22f, 0.32f)))
        };

        /// <summary>Три лопасти знака радиации: сектора кольца по 60° через 120°.</summary>
        private static float RadiationBlades(Vector2 p)
        {
            float r = p.magnitude;
            float ring = Mathf.Max(0.28f - r, r - 0.88f);
            float angle = Mathf.Atan2(p.y, p.x) * Mathf.Rad2Deg;
            float best = float.MaxValue;
            foreach (float center in new[] { 90f, 210f, 330f })
            {
                float delta = Mathf.Abs(Mathf.DeltaAngle(angle, center)) - 30f;
                best = Mathf.Min(best, Mathf.Sin(Mathf.Clamp(delta, -90f, 90f) * Mathf.Deg2Rad) * r);
            }
            return Mathf.Max(ring, best);
        }

        private static float Segment(Vector2 p, Vector2 a, Vector2 b, float radius)
        {
            Vector2 pa = p - a, ba = b - a;
            float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Vector2.Dot(ba, ba));
            return (pa - ba * h).magnitude - radius;
        }

        private static float Box(Vector2 p, Vector2 center, Vector2 halfSize)
        {
            Vector2 d = new Vector2(Mathf.Abs(p.x - center.x), Mathf.Abs(p.y - center.y)) - halfSize;
            return new Vector2(Mathf.Max(d.x, 0f), Mathf.Max(d.y, 0f)).magnitude + Mathf.Min(Mathf.Max(d.x, d.y), 0f);
        }

        /// <summary>Расстояние до выпуклого или невыпуклого многоугольника (знак — по правилу чётности).</summary>
        private static float Polygon(Vector2 p, params Vector2[] v)
        {
            float d = Vector2.Dot(p - v[0], p - v[0]);
            float sign = 1f;
            for (int i = 0, j = v.Length - 1; i < v.Length; j = i, i++)
            {
                Vector2 e = v[j] - v[i], w = p - v[i];
                Vector2 b = w - e * Mathf.Clamp01(Vector2.Dot(w, e) / Vector2.Dot(e, e));
                d = Mathf.Min(d, Vector2.Dot(b, b));
                bool c1 = p.y >= v[i].y, c2 = p.y < v[j].y, c3 = e.x * w.y > e.y * w.x;
                if ((c1 && c2 && c3) || (!c1 && !c2 && !c3)) sign = -sign;
            }
            return sign * Mathf.Sqrt(d);
        }
    }
}
