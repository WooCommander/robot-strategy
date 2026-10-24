using UnityEngine;

namespace Game.Presentation.Rendering
{
    /// <summary>
    /// Детерминированный value-noise без UnityEngine.Random: одна и та же карта всегда выглядит одинаково,
    /// а период позволяет строить бесшовно тайлящиеся текстуры.
    /// </summary>
    public static class ProceduralNoise
    {
        /// <summary>Псевдослучайное число 0..1 для целочисленной точки.</summary>
        public static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777215f;
            }
        }

        /// <summary>Сглаженный value-noise 0..1. period &gt; 0 зацикливает решётку для бесшовного тайлинга.</summary>
        public static float Value(float x, float y, int seed, int period = 0)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float tx = x - x0, ty = y - y0;
            tx = tx * tx * (3f - 2f * tx);
            ty = ty * ty * (3f - 2f * ty);
            int x1 = x0 + 1, y1 = y0 + 1;
            if (period > 0)
            {
                x0 = Wrap(x0, period); x1 = Wrap(x1, period);
                y0 = Wrap(y0, period); y1 = Wrap(y1, period);
            }
            float a = Hash(x0, y0, seed), b = Hash(x1, y0, seed);
            float c = Hash(x0, y1, seed), d = Hash(x1, y1, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
        }

        /// <summary>Фрактальная сумма октав, результат 0..1.</summary>
        public static float Fbm(float x, float y, int seed, int octaves = 3, int period = 0)
        {
            float sum = 0f, amplitude = 1f, total = 0f;
            for (int octave = 0; octave < octaves; octave++)
            {
                sum += Value(x, y, seed + octave * 101, period) * amplitude;
                total += amplitude;
                amplitude *= 0.5f;
                x *= 2f; y *= 2f;
                if (period > 0) period *= 2;
            }
            return sum / total;
        }

        private static int Wrap(int value, int period)
        {
            int result = value % period;
            return result < 0 ? result + period : result;
        }
    }
}
