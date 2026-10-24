using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Presentation.Rendering
{
    /// <summary>
    /// Настраивает «тёплый» дневной свет, рассеянное освещение и пост-обработку поля боя.
    /// Профиль Volume собирается в коде, чтобы настройки жили рядом с остальным процедурным визуалом.
    /// </summary>
    public class SceneAtmosphere : MonoBehaviour
    {
        [Header("Sun")]
        [SerializeField] private Vector3 _sunEuler = new Vector3(45f, 115f, 0f);
        [SerializeField] private Color _sunColor = new Color(1f, 0.93f, 0.8f);
        [SerializeField] private float _sunIntensity = 1.6f;

        [Header("Ambient")]
        [SerializeField] private Color _skyAmbient = new Color(0.56f, 0.66f, 0.8f);
        [SerializeField] private Color _equatorAmbient = new Color(0.52f, 0.55f, 0.58f);
        [SerializeField] private Color _groundAmbient = new Color(0.34f, 0.31f, 0.27f);
        [SerializeField] private Color _hazeColor = new Color(0.62f, 0.7f, 0.78f);
        [SerializeField] private float _hazeDensity = 0.003f;

        private VolumeProfile _profile;

        public void Apply(UnityEngine.Camera cam)
        {
            SetupSun();
            SetupAmbient();
            if (cam != null) SetupCamera(cam);
            // Volume работает только под URP; во встроенном пайплайне он просто ничего не делает.
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset) SetupVolume();
        }

        private void SetupSun()
        {
            Light sun = RenderSettings.sun;
            if (sun == null)
            {
                foreach (Light light in FindObjectsByType<Light>())
                {
                    if (light.type == LightType.Directional) { sun = light; break; }
                }
            }
            if (sun == null)
            {
                sun = new GameObject("Sun").AddComponent<Light>();
                sun.type = LightType.Directional;
            }

            sun.transform.rotation = Quaternion.Euler(_sunEuler);
            sun.color = _sunColor;
            sun.intensity = _sunIntensity;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.85f;
            sun.shadowBias = 0.05f;
            sun.shadowNormalBias = 0.4f;
            RenderSettings.sun = sun;
        }

        private void SetupAmbient()
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = _skyAmbient;
            RenderSettings.ambientEquatorColor = _equatorAmbient;
            RenderSettings.ambientGroundColor = _groundAmbient;

            // Лёгкая дымка отделяет дальний план и смягчает край карты.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = _hazeColor;
            RenderSettings.fogDensity = _hazeDensity;
        }

        private void SetupCamera(UnityEngine.Camera cam)
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = _hazeColor * 0.55f;
            cam.allowHDR = true;
            if (GraphicsSettings.currentRenderPipeline is not UniversalRenderPipelineAsset) return;

            UniversalAdditionalCameraData data = cam.GetUniversalAdditionalCameraData();
            // Сглаживание краёв даёт MSAA из URP asset; SMAA поверх него только тратил бы мобильный GPU.
            data.renderPostProcessing = true;
            data.renderShadows = true;
        }

        private void SetupVolume()
        {
            _profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _profile.name = "BattleAtmosphere";

            Tonemapping tonemapping = _profile.Add<Tonemapping>(true);
            tonemapping.mode.Override(TonemappingMode.ACES);

            ColorAdjustments color = _profile.Add<ColorAdjustments>(true);
            color.postExposure.Override(0.15f);
            color.contrast.Override(10f);
            color.saturation.Override(4f);

            WhiteBalance whiteBalance = _profile.Add<WhiteBalance>(true);
            whiteBalance.temperature.Override(6f);

            // Порог выше 1: светятся только эмиссивные линзы, маркеры и вспышки, а не солнечные блики на земле.
            Bloom bloom = _profile.Add<Bloom>(true);
            bloom.threshold.Override(1.1f);
            bloom.intensity.Override(0.7f);
            bloom.scatter.Override(0.65f);

            Vignette vignette = _profile.Add<Vignette>(true);
            vignette.intensity.Override(0.24f);
            vignette.smoothness.Override(0.45f);

            var volume = new GameObject("AtmosphereVolume").AddComponent<Volume>();
            volume.transform.SetParent(transform, false);
            volume.isGlobal = true;
            volume.priority = 10f;
            volume.sharedProfile = _profile;
        }

        private void OnDestroy()
        {
            if (_profile != null) Destroy(_profile);
        }
    }
}
