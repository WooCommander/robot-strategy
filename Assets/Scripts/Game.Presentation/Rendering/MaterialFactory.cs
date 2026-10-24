using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Presentation.Rendering
{
    /// <summary>
    /// Единая точка создания процедурных материалов. При активном URP берёт URP/Lit
    /// (иначе «Standard» в URP рендерится розовым), без пайплайна — встроенный Standard.
    /// Материалы клонируются из шаблонов в Resources/RenderTemplates: ассеты-шаблоны гарантируют, что шейдер
    /// и нужные варианты (эмиссия, карты нормалей, прозрачность) попадут в сборку. Без шаблонов
    /// (например, до Setup URP) материал настраивается на лету — в редакторе это работает так же.
    /// </summary>
    public static class MaterialFactory
    {
        public const string TemplateFolder = "RenderTemplates";
        public const string LitTemplate = "Lit";
        public const string EmissiveTemplate = "LitEmissive";
        public const string TerrainTemplate = "Terrain";
        public const string WaterTemplate = "Water";
        public const string UnlitTemplate = "Unlit";

        private static bool UsesScriptablePipeline => GraphicsSettings.currentRenderPipeline != null;

        /// <summary>Освещаемый материал: metallic 0..1, smoothness 0..1.</summary>
        public static Material CreateLit(string name, Color color, float metallic = 0f, float smoothness = 0.3f)
        {
            Material mat = Instantiate(LitTemplate, name);
            SetColor(mat, color);
            SetSurface(mat, metallic, smoothness);
            return mat;
        }

        /// <summary>Освещаемый материал с текстурой цвета (палитра декора и т.п.).</summary>
        public static Material CreateTexturedLit(string name, Texture2D baseMap, float smoothness = 0.15f)
        {
            Material mat = CreateLit(name, Color.white, 0f, smoothness);
            SetBaseMap(mat, baseMap);
            return mat;
        }

        /// <summary>Самосветящийся материал для линз, индикаторов и маркеров: подхватывается bloom'ом.</summary>
        public static Material CreateEmissive(string name, Color color, float intensity = 2f)
        {
            Material mat = Instantiate(EmissiveTemplate, name);
            ConfigureEmissive(mat);
            SetColor(mat, color);
            SetSurface(mat, 0f, 0.6f);
            if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", color * intensity);
            return mat;
        }

        /// <summary>Рельеф: цветовая карта на всё поле плюс тайлящиеся детали цвета и нормалей вблизи.</summary>
        public static Material CreateTerrain(string name, Texture2D albedo, Texture2D detailAlbedo, Texture2D detailNormal, Vector2 detailTiling)
        {
            Material mat = Instantiate(TerrainTemplate, name);
            ConfigureTerrain(mat);
            SetColor(mat, Color.white);
            SetSurface(mat, 0f, 0.12f);
            SetBaseMap(mat, albedo);
            if (mat.HasProperty("_DetailAlbedoMap"))
            {
                mat.SetTexture("_DetailAlbedoMap", detailAlbedo);
                mat.SetTexture("_DetailNormalMap", detailNormal);
                mat.SetTextureScale("_DetailAlbedoMap", detailTiling);
            }
            return mat;
        }

        /// <summary>Полупрозрачная гладкая вода с картой нормалей; рябь двигается сдвигом UV.</summary>
        public static Material CreateWater(string name, Color color, Texture2D normalMap)
        {
            Material mat = Instantiate(WaterTemplate, name);
            ConfigureWater(mat);
            SetColor(mat, color);
            SetSurface(mat, 0f, 0.92f);
            if (mat.HasProperty("_BumpMap")) mat.SetTexture("_BumpMap", normalMap);
            return mat;
        }

        /// <summary>Неосвещаемый материал без прозрачности.</summary>
        public static Material CreateUnlit(string name, Color color)
        {
            Material template = UsesScriptablePipeline ? Resources.Load<Material>(TemplateFolder + "/" + UnlitTemplate) : null;
            var mat = template != null ? new Material(template) : new Material(FindUnlitShader());
            mat.name = name;
            SetColor(mat, color);
            return mat;
        }

        public static Shader FindUnlitShader()
        {
            Shader shader = UsesScriptablePipeline ? Shader.Find("Universal Render Pipeline/Unlit") : Shader.Find("Unlit/Color");
            if (shader == null || !shader.isSupported) shader = Shader.Find("Sprites/Default");
            return shader;
        }

        public static void SetColor(Material mat, Color color)
        {
            mat.color = color;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
        }

        /// <summary>Шейдер, на котором строятся освещаемые шаблоны в текущем пайплайне.</summary>
        public static Shader FindLitShader()
        {
            Shader shader = UsesScriptablePipeline ? Shader.Find("Universal Render Pipeline/Lit") : Shader.Find("Standard");
            if (shader == null || !shader.isSupported) shader = Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default");
            return shader;
        }

        public static void ConfigureEmissive(Material mat)
        {
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }

        public static void ConfigureTerrain(Material mat)
        {
            // Детальная карта нормалей в URP/Lit работает только вместе с _NORMALMAP; основная карта нормалей — плоская по умолчанию.
            mat.EnableKeyword("_NORMALMAP");
            mat.EnableKeyword("_DETAIL_MULX2");
            if (mat.HasProperty("_DetailAlbedoMapScale")) mat.SetFloat("_DetailAlbedoMapScale", 1f);
            if (mat.HasProperty("_DetailNormalMapScale")) mat.SetFloat("_DetailNormalMapScale", 0.8f);
        }

        public static void ConfigureWater(Material mat)
        {
            mat.EnableKeyword("_NORMALMAP");
            if (!mat.HasProperty("_Surface")) return;
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            if (mat.HasProperty("_SrcBlendAlpha")) mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            if (mat.HasProperty("_DstBlendAlpha")) mat.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = (int)RenderQueue.Transparent;
            mat.SetShaderPassEnabled("DepthOnly", false);
            mat.SetShaderPassEnabled("ShadowCaster", false);
        }

        private static Material Instantiate(string templateName, string name)
        {
            Material template = UsesScriptablePipeline ? Resources.Load<Material>(TemplateFolder + "/" + templateName) : null;
            var mat = template != null ? new Material(template) : new Material(FindLitShader());
            mat.name = name;
            return mat;
        }

        private static void SetSurface(Material mat, float metallic, float smoothness)
        {
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
            // URP называет гладкость _Smoothness, Standard — _Glossiness.
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", smoothness);
        }

        private static void SetBaseMap(Material mat, Texture texture)
        {
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", texture);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", texture);
        }
    }
}
