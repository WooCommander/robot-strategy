using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Presentation.Rendering
{
    /// <summary>
    /// Единая точка создания процедурных материалов. При активном URP берёт URP/Lit
    /// (иначе «Standard» в URP рендерится розовым), без пайплайна — встроенный Standard.
    /// </summary>
    public static class MaterialFactory
    {
        private static bool UsesScriptablePipeline => GraphicsSettings.currentRenderPipeline != null;

        /// <summary>Освещаемый материал: metallic 0..1, smoothness 0..1.</summary>
        public static Material CreateLit(string name, Color color, float metallic = 0f, float smoothness = 0.3f)
        {
            Shader shader = UsesScriptablePipeline ? Shader.Find("Universal Render Pipeline/Lit") : Shader.Find("Standard");
            if (shader == null || !shader.isSupported) shader = Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default");

            var mat = new Material(shader) { name = name };
            SetColor(mat, color);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
            // URP называет гладкость _Smoothness, Standard — _Glossiness.
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", smoothness);
            return mat;
        }

        /// <summary>Самосветящийся материал для линз, индикаторов и маркеров: подхватывается bloom'ом.</summary>
        public static Material CreateEmissive(string name, Color color, float intensity = 2f)
        {
            Material mat = CreateLit(name, color, 0f, 0.6f);
            if (mat.HasProperty("_EmissionColor"))
            {
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                mat.SetColor("_EmissionColor", color * intensity);
            }
            return mat;
        }

        /// <summary>Неосвещаемый материал без прозрачности.</summary>
        public static Material CreateUnlit(string name, Color color)
        {
            Shader shader = UsesScriptablePipeline ? Shader.Find("Universal Render Pipeline/Unlit") : Shader.Find("Unlit/Color");
            if (shader == null || !shader.isSupported) shader = Shader.Find("Sprites/Default");
            var mat = new Material(shader) { name = name };
            SetColor(mat, color);
            return mat;
        }

        public static void SetColor(Material mat, Color color)
        {
            mat.color = color;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
        }
    }
}
