using Il2Cpp;
using System.Text;
using System.Text.RegularExpressions;
using NullServers.Players;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace NullServers.UI
{
    // Shows "NullServers <version>" in rainbow colors above the game's own version in the main menu.
    internal sealed class VersionLabel
    {
        private static readonly Regex GameVersionText = new(@"^\d+(\.\d+)+[a-z]?$", RegexOptions.CultureInvariant);

        private readonly PlayerProfile _rainbowStyle;
        private readonly StringBuilder _coloredText = new();
        private Text _label;
        private float _nextSearch;
        private float _nextRecolor;

        public VersionLabel(string version)
        {
            _rainbowStyle = new PlayerProfile("NullServers " + version, "#FFFFFF", NameColorMode.Rgb, colorSpeed: 1.5f);
        }

        public void Update()
        {
            if (SceneManager.GetActiveScene().name != "MainMenu")
            {
                _label = null;
                return;
            }

            if (_label == null)
            {
                if (Time.unscaledTime < _nextSearch) return;

                _nextSearch = Time.unscaledTime + 1f;
                CreateLabel();
                if (_label == null) return;
            }

            if (Time.unscaledTime < _nextRecolor) return;

            _nextRecolor = Time.unscaledTime + 1f / 30f;
            Recolor();
        }

        private void Recolor()
        {
            double time = NameColors.Now;
            string text = _rainbowStyle.Nickname;
            _coloredText.Clear();

            for (int index = 0; index < text.Length; index++)
            {
                string color = NameColors.Color(_rainbowStyle, time, -index * 0.05);
                _coloredText.Append("<color=").Append(color).Append('>').Append(text[index]).Append("</color>");
            }

            _label.text = _coloredText.ToString();
        }

        private void CreateLabel()
        {
            Text gameVersion = FindGameVersion();
            if (gameVersion == null) return;

            GameObject copy = Object.Instantiate(gameVersion.gameObject, gameVersion.transform.parent, false);
            copy.name = "NullServersVersion";

            foreach (TranslateText translation in copy.GetComponentsInChildren<TranslateText>(true))
            {
                translation.enabled = false;
                Object.DestroyImmediate(translation);
            }

            _label = copy.GetComponent<Text>();
            _label.supportRichText = true;
            _label.horizontalOverflow = HorizontalWrapMode.Overflow;
            _label.verticalOverflow = VerticalWrapMode.Overflow;
            _label.fontSize = Mathf.Max(10, Mathf.RoundToInt(gameVersion.fontSize * 0.7f));
            _label.text = _rainbowStyle.Nickname;

            float lineHeight = Mathf.Max(_label.preferredHeight, _label.fontSize) * 1.1f;
            _label.rectTransform.anchoredPosition = gameVersion.rectTransform.anchoredPosition + Vector2.up * lineHeight;
            _nextRecolor = 0f;
        }

        private static Text FindGameVersion()
        {
            Scene scene = SceneManager.GetActiveScene();
            Text found = null;

            foreach (Text text in Object.FindObjectsOfType<Text>(true))
            {
                if (text.gameObject.scene != scene || text.text == null) continue;
                if (!GameVersionText.IsMatch(text.text.Trim())) continue;

                if (found == null || text.gameObject.activeInHierarchy && !found.gameObject.activeInHierarchy)
                {
                    found = text;
                }
            }

            return found;
        }
    }
}
