using Il2Cpp;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace NullServers.Room
{
    internal sealed class RoomEnvironment
    {
        private readonly Dictionary<string, Material> _skyboxes = new(StringComparer.Ordinal);
        private RenderOptions _original;
        private RenderOptions _applied;
        private Material _originalSkybox;
        private Material _skybox;
        private Light _sun;
        private AmbientMode _ambientMode;
        private Color _ambientLight;
        private int _scene = -1;
        private bool _active;
        private readonly SkyboxImage _image = new();
        internal WorldGeometry Geometry { get; } = new();
        internal string ImageError => _image.Error;

        internal IReadOnlyDictionary<string, Material> Skyboxes => _skyboxes;
        internal RenderOptions Current => _applied ?? _original;
        internal RenderOptions Original => _original;

        internal void Update(bool active, RenderOptions options)
        {
            int scene = SceneManager.GetActiveScene().handle;
            if (_scene != scene || _active != active)
            {
                if (_scene == scene && _active)
                {
                    Restore();
                }

                Release();
                _scene = scene;
                _active = active;
                if (!active) return;

                _originalSkybox = RenderSettings.skybox;
                _sun = RenderSettings.sun;
                _ambientMode = RenderSettings.ambientMode;
                _ambientLight = RenderSettings.ambientLight;
                _original = new RenderOptions(
                    RenderSettings.fog,
                    (int)RenderSettings.fogMode,
                    "#" + ColorUtility.ToHtmlStringRGB(RenderSettings.fogColor),
                    RenderSettings.fogDensity,
                    Mathf.Max(0f, RenderSettings.fogStartDistance),
                    Mathf.Max(RenderSettings.fogStartDistance, RenderSettings.fogEndDistance, 0f),
                    _originalSkybox,
                    _originalSkybox ? _originalSkybox.name : "",
                    _originalSkybox && _originalSkybox.HasProperty("_Exposure") ? _originalSkybox.GetFloat("_Exposure") : 1f,
                    "#" + ColorUtility.ToHtmlStringRGB(_ambientLight),
                    RenderSettings.ambientIntensity,
                    RenderSettings.reflectionIntensity,
                    _sun ? _sun.intensity : 1f);
                foreach (Material material in Resources.FindObjectsOfTypeAll<Material>())
                {
                    if (material && material.shader && material.shader.name.StartsWith("Skybox/", StringComparison.Ordinal) &&
                        material.name.Length <= 128)
                    {
                        _skyboxes.TryAdd(material.name, material);
                    }
                }

                if (_originalSkybox)
                {
                    _skyboxes[_originalSkybox.name] = _originalSkybox;
                }
            }

            Material image = _image.Update(active && options?.Skybox == true ? options.SkyboxUrl ?? "" : "", options?.Exposure ?? 1f);
            if (image)
            {
                RenderSettings.skybox = image;
            }

            if (!active || options == _applied) return;

            if (options == null)
            {
                Restore();
                return;
            }

            if (!options.Valid) return;

            Apply(options);
            _applied = options;
        }

        internal void Reapply()
        {
            if (_active && _applied != null)
            {
                Lighting(_applied);
            }
        }

        private void Apply(RenderOptions options)
        {
            Lighting(options);

            if (_skybox)
            {
                UnityEngine.Object.Destroy(_skybox);
            }

            _skybox = null;

            Material material = _skyboxes.GetValueOrDefault(options.SkyboxName) ?? _originalSkybox;
            if (options.Skybox && material)
            {
                _skybox = new Material(material);
                if (_skybox.HasProperty("_Exposure"))
                {
                    _skybox.SetFloat("_Exposure", options.Exposure);
                }
            }

            RenderSettings.skybox = _skybox;
        }

        private void Lighting(RenderOptions options)
        {
            RenderSettings.fog = options.Fog;
            RenderSettings.fogMode = (FogMode)options.FogMode;
            ColorUtility.TryParseHtmlString(options.FogColor, out Color fog);
            RenderSettings.fogColor = fog;
            RenderSettings.fogDensity = options.FogDensity;
            RenderSettings.fogStartDistance = options.FogStart;
            RenderSettings.fogEndDistance = options.FogEnd;
            ColorUtility.TryParseHtmlString(options.AmbientColor, out Color ambient);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = ambient;
            MapView(options.Fog, options.FogDensity, fog, AmbientMode.Flat, ambient);
            NightVision(options.FogDensity);
            RenderSettings.ambientIntensity = options.AmbientIntensity;
            RenderSettings.reflectionIntensity = options.ReflectionIntensity;

            if (_sun)
            {
                _sun.intensity = options.SunIntensity;
            }
        }

        private void Restore()
        {
            if (_original == null) return;

            Apply(_original);
            RenderSettings.skybox = _originalSkybox;
            RenderSettings.ambientMode = _ambientMode;
            RenderSettings.ambientLight = _ambientLight;
            MapView(RenderSettings.fog, RenderSettings.fogDensity, RenderSettings.fogColor, _ambientMode, RenderSettings.ambientSkyColor);
            NightVision(RenderSettings.fogDensity);

            if (_skybox)
            {
                UnityEngine.Object.Destroy(_skybox);
            }

            _skybox = null;
            _applied = null;
        }

        internal void NightVision(ToggleNightVision vision)
        {
            if (_active && _applied != null && vision)
            {
                vision.JDJOBDIHCFL = _applied.FogDensity;
            }
        }

        private static void NightVision(float density)
        {
            foreach (ToggleNightVision vision in UnityEngine.Object.FindObjectsOfType<ToggleNightVision>(true))
            {
                vision.JDJOBDIHCFL = density;
            }
        }

        private static void MapView(bool fog, float density, Color color, AmbientMode mode, Color ambient)
        {
            MapViewSettings.AKLDPIDNJFI = fog;
            MapViewSettings.EMLABEHACJO = density;
            MapViewSettings.EHODIHCACEE = color;
            MapViewSettings.OIHNFFOHANC = mode;
            MapViewSettings.DMPDAAPAHMD = ambient;
        }

        private void Release()
        {
            _image.Dispose();
            if (_skybox)
            {
                UnityEngine.Object.Destroy(_skybox);
            }

            _skybox = null;
            _original = _applied = null;
            _originalSkybox = null;
            _sun = null;
            _skyboxes.Clear();
        }
    }
}
