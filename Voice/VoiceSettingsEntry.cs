using Il2CppInterop.Runtime;
using NullServers.UI;
using UnityEngine;
using UnityEngine.UI;

namespace NullServers.Voice
{
    // Adds a "Voice Chat" button to the game's own Settings panel. It is not in the pause menu,
    // so the pause buttons keep their normal size.
    internal sealed class VoiceSettingsEntry
    {
        private Button _button;
        private float _nextScan;

        internal void Update()
        {
            if (_button)
            {
                _button.gameObject.SetActive(VoiceApi.Installed);
            }

            if (!VoiceApi.Installed || Time.unscaledTime < _nextScan) return;

            _nextScan = Time.unscaledTime + 1f;
            foreach (Slider slider in UnityEngine.Object.FindObjectsOfType<Slider>())
            {
                Transform panel = slider.transform.parent;
                while (panel && !panel.name.Contains("Option", StringComparison.OrdinalIgnoreCase) &&
                    !panel.name.Contains("Settings", StringComparison.OrdinalIgnoreCase))
                {
                    panel = panel.parent;
                }

                if (!panel || panel.name.StartsWith("NullFrame")) continue;
                if (_button && _button.transform.parent == panel) return;

                Button template = panel.GetComponentsInChildren<Button>(true).FirstOrDefault();
                if (!template) continue;

                if (_button)
                {
                    UnityEngine.Object.Destroy(_button.gameObject);
                }

                GameUi ui = new(template);
                _button = ui.Button(panel, "Voice Chat", () => VoiceApi.Settings?.Invoke());
                _button.name = "NullFrameVoiceSettingsEntry";
                GameUi.Area(_button.GetComponent<RectTransform>(), 0.42f, 0.20f, 0.62f, 0.26f);
                return;
            }
        }
    }
}
