using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Game.Presentation.Rendering;

namespace Game.Presentation.Editor
{
    /// <summary>
    /// Создаёт URP asset с renderer'ом и SSAO и назначает его проекту и всем уровням качества.
    /// Без назначенного asset'а Unity рендерит встроенным пайплайном, и пост-обработка URP не работает.
    /// Повторный запуск безопасен: существующие ассеты переиспользуются, настройки перезаписываются.
    /// Batchmode: -executeMethod Game.Presentation.Editor.RenderPipelineSetup.Setup
    /// </summary>
    public static class RenderPipelineSetup
    {
        private const string Folder = "Assets/Settings";
        private const string PipelinePath = Folder + "/RobotStrategy_URP.asset";
        private const string RendererPath = Folder + "/RobotStrategy_URP_Renderer.asset";

        [MenuItem("Robot Strategy/Rendering/Setup URP")]
        public static void Setup()
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "Settings");

            UniversalRendererData renderer = LoadOrCreateRenderer();
            EnsureSsao(renderer);

            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (pipeline == null)
            {
                pipeline = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(pipeline, PipelinePath);
            }
            ConfigurePipeline(pipeline);

            GraphicsSettings.defaultRenderPipeline = pipeline;
            int currentLevel = QualitySettings.GetQualityLevel();
            for (int level = 0; level < QualitySettings.count; level++)
            {
                QualitySettings.SetQualityLevel(level, false);
                QualitySettings.renderPipeline = pipeline;
            }
            QualitySettings.SetQualityLevel(currentLevel, false);

            UpgradeProjectMaterials();
            CreateMaterialTemplates();
            AssetDatabase.SaveAssets();
            Debug.Log($"[RenderPipelineSetup] URP назначен: {PipelinePath}");
        }

        private static UniversalRendererData LoadOrCreateRenderer()
        {
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (renderer != null) return renderer;

            // Штатный путь создания из меню Assets/Create заполняет postProcessData и ресурсы шейдеров,
            // но он internal — вызываем его так же, как это делает сам пункт меню.
            MethodInfo create = typeof(UniversalRenderPipelineAsset).GetMethod("CreateRendererAsset", BindingFlags.NonPublic | BindingFlags.Static);
            if (create != null)
            {
                return (UniversalRendererData)create.Invoke(null, new object[] { RendererPath, RendererType.UniversalRenderer, false, "Renderer" });
            }

            renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
            AssetDatabase.CreateAsset(renderer, RendererPath);
            return renderer;
        }

        private static void EnsureSsao(UniversalRendererData renderer)
        {
            ScreenSpaceAmbientOcclusion ssao = null;
            foreach (ScriptableRendererFeature feature in renderer.rendererFeatures)
            {
                if (feature is ScreenSpaceAmbientOcclusion existing) ssao = existing;
            }
            if (ssao == null)
            {
                ssao = ScriptableObject.CreateInstance<ScreenSpaceAmbientOcclusion>();
                ssao.name = "ScreenSpaceAmbientOcclusion";
                AssetDatabase.AddObjectToAsset(ssao, renderer);
                renderer.rendererFeatures.Add(ssao);
            }

            // Масштаб мира крупный (робот ~1 м, камера в 30–70 м), поэтому радиус больше стандартного.
            var settings = new SerializedObject(ssao);
            settings.FindProperty("m_Settings.Intensity").floatValue = 1.8f;
            settings.FindProperty("m_Settings.Radius").floatValue = 0.3f;
            settings.FindProperty("m_Settings.DirectLightingStrength").floatValue = 0.3f;
            settings.ApplyModifiedPropertiesWithoutUndo();

            // Карта localId нужна renderer'у, чтобы найти подобъекты-фичи после перезагрузки.
            var data = new SerializedObject(renderer);
            SerializedProperty map = data.FindProperty("m_RendererFeatureMap");
            map.arraySize = renderer.rendererFeatures.Count;
            for (int i = 0; i < renderer.rendererFeatures.Count; i++)
            {
                if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(renderer.rendererFeatures[i], out string _, out long localId))
                    map.GetArrayElementAtIndex(i).longValue = localId;
            }
            data.ApplyModifiedPropertiesWithoutUndo();
            renderer.SetDirty();
            EditorUtility.SetDirty(renderer);
        }

        private static void ConfigurePipeline(UniversalRenderPipelineAsset pipeline)
        {
            pipeline.supportsHDR = true;
            pipeline.msaaSampleCount = 4;
            pipeline.supportsCameraDepthTexture = true;
            pipeline.shadowDistance = 110f;
            pipeline.shadowCascadeCount = 2;
            pipeline.mainLightShadowmapResolution = 2048;

            var serialized = new SerializedObject(pipeline);
            serialized.FindProperty("m_SoftShadowsSupported").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(pipeline);
        }

        /// <summary>
        /// Материалы-шаблоны в Resources: через них шейдеры URP и нужные варианты ключевых слов
        /// попадают в сборку, хотя все игровые материалы создаются в коде.
        /// </summary>
        private static void CreateMaterialTemplates()
        {
            string folder = "Assets/Resources/" + MaterialFactory.TemplateFolder;
            if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/Resources", MaterialFactory.TemplateFolder);

            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (lit == null || unlit == null)
            {
                Debug.LogError("[RenderPipelineSetup] Шейдеры URP не найдены, шаблоны материалов не созданы.");
                return;
            }

            SaveTemplate(folder, MaterialFactory.LitTemplate, lit, null);
            SaveTemplate(folder, MaterialFactory.EmissiveTemplate, lit, MaterialFactory.ConfigureEmissive);
            SaveTemplate(folder, MaterialFactory.TerrainTemplate, lit, MaterialFactory.ConfigureTerrain);
            SaveTemplate(folder, MaterialFactory.WaterTemplate, lit, MaterialFactory.ConfigureWater);
            SaveTemplate(folder, MaterialFactory.UnlitTemplate, unlit, null);
        }

        private static void SaveTemplate(string folder, string name, Shader shader, System.Action<Material> configure)
        {
            string path = $"{folder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            configure?.Invoke(material);
            EditorUtility.SetDirty(material);
        }

        /// <summary>Переводит материалы проекта со Standard на URP/Lit, сохраняя цвет.</summary>
        private static void UpgradeProjectMaterials()
        {
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null) return;
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets/Materials" }))
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (material == null || material.shader == null || material.shader.name != "Standard") continue;
                Color color = material.HasProperty("_Color") ? material.GetColor("_Color") : Color.white;
                material.shader = lit;
                material.SetColor("_BaseColor", color);
                EditorUtility.SetDirty(material);
            }
        }
    }
}
