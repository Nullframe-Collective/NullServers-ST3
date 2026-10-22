using NullServers.Room;
using UnityEngine;

namespace NullServers.UI
{
    internal sealed class ColorPicker
    {
        private Texture2D _wheel;
        private GUIStyle _label;
        private GUIStyle _input;
        private GUIStyle _previewStyle;
        private Rect _panel;
        private Action<string> _commit;
        private Action<string> _preview;
        private string _sample;
        private float _hue;
        private float _saturation;
        private float _value;
        private string _hex;
        private int _wheelControl;

        internal bool IsOpen => _commit != null;

        internal void Open(Rect anchor, string hex, Action<string> commit, Action<string> preview = null, string sample = null)
        {
            Close();
            ColorUtility.TryParseHtmlString(hex, out Color color);
            Color.RGBToHSV(color, out _hue, out _saturation, out _value);

            _hex = hex;
            _commit = commit;
            _preview = preview;
            _sample = sample;

            float width = Mathf.Clamp(Screen.height * 0.42f, 220f, 360f);
            float height = width + 150f;
            _panel = new Rect
            (
                Mathf.Clamp(anchor.xMax - width, 8f, Screen.width - width - 8f),
                anchor.yMax + 6f,
                width,
                height
            );

            if (_panel.yMax > Screen.height - 8)
            {
                _panel.y = Mathf.Max(8f, anchor.y - height - 6f);
            }

            GUI.FocusControl(null);
        }

        internal void Close()
        {
            var commit = _commit;
            _commit = null;
            _preview = null;

            if (_wheelControl != 0 && GUIUtility.hotControl == _wheelControl)
            {
                GUIUtility.hotControl = 0;
            }

            commit?.Invoke("#" + ColorUtility.ToHtmlStringRGB(Color.HSVToRGB(_hue, _saturation, _value)));
        }

        internal void HandleInput()
        {
            if (!IsOpen) return;

            Event current = Event.current;
            if
            (
                (current.type == EventType.MouseDown && !_panel.Contains(current.mousePosition)) ||
                (current.type == EventType.KeyDown && current.keyCode == KeyCode.Escape)
            )
            {
                Close();
                current.Use();
            }
        }

        internal static bool Swatch(Rect rect, string hex)
        {
            Color old = GUI.color;

            GUI.color = Color.white;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);

            ColorUtility.TryParseHtmlString(hex, out Color color);
            GUI.color = color;

            GUI.DrawTexture
            (
                new Rect(rect.x + 2f, rect.y + 2f, rect.width - 4f, rect.height - 4f),
                Texture2D.whiteTexture
            );

            GUI.color = old;

            return GUI.Button(rect, GUIContent.none, GUIStyle.none);
        }

        internal void Draw(GUISkin skin)
        {
            if (!IsOpen) return;

            if (!_wheel)
            {
                const int size = 256;
                Color[] pixels = new Color[size * size];

                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        Vector2 point = new((x + 0.5f) / size * 2f - 1f, (y + 0.5f) / size * 2f - 1f);
                        float saturation = point.magnitude;
                        Color color = Color.HSVToRGB
                        (
                            Mathf.Repeat(Mathf.Atan2(point.y, point.x) / (Mathf.PI * 2f), 1f),
                            Mathf.Min(1f, saturation),
                            1f
                        );
                        color.a = Mathf.Clamp01((1f - saturation) * size * 0.5f);
                        pixels[y * size + x] = color;
                    }
                }

                _wheel = new Texture2D(size, size, TextureFormat.RGBA32, false)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                _wheel.SetPixels(pixels);
                _wheel.Apply(false, true);

                _label = new GUIStyle(skin.customStyles[0])
                {
                    alignment = TextAnchor.MiddleLeft,
                    richText = false
                };

                _input = new GUIStyle(GUI.skin.textField)
                {
                    alignment = TextAnchor.MiddleCenter,
                    richText = false
                };

                _previewStyle = new GUIStyle(_label)
                {
                    richText = true,
                    wordWrap = true
                };
            }

            Color oldColor = GUI.color;
            bool oldEnabled = GUI.enabled;

            GUI.enabled = true;
            GUI.color = new Color(0.65f, 0.65f, 0.65f, 1f);
            GUI.DrawTexture(_panel, Texture2D.whiteTexture);

            GUI.color = new Color(0.025f, 0.025f, 0.035f, 1f);
            GUI.DrawTexture
            (
                new Rect(_panel.x + 1f, _panel.y + 1f, _panel.width - 2f, _panel.height - 2f),
                Texture2D.whiteTexture
            );

            GUI.color = Color.white;
            _label.fontSize = _input.fontSize = Mathf.Max(14, Screen.height / 40);
            _previewStyle.fontSize = _label.fontSize;

            Rect wheel = new(_panel.x + 12f, _panel.y + 30f, _panel.width - 24f, _panel.width - 24f);
            GUI.Label(new Rect(_panel.x + 12f, _panel.y + 4f, _panel.width - 24f, 24f), "Color", _label);

            GUI.color = new Color(_value, _value, _value, 1f);
            GUI.DrawTexture(wheel, _wheel);

            GUI.color = Color.white;
            _wheelControl = GUIUtility.GetControlID(0x4E4657, FocusType.Passive);

            Event current = Event.current;
            Vector2 pointInWheel = (current.mousePosition - wheel.center) / (wheel.width * 0.5f);
            if (current.type == EventType.MouseDown && current.button == 0 && pointInWheel.sqrMagnitude <= 1f)
            {
                GUIUtility.hotControl = _wheelControl;
            }

            if (GUIUtility.hotControl == _wheelControl &&
                (current.type == EventType.MouseDown ||
                current.type == EventType.MouseDrag))
            {
                _hue = Mathf.Repeat(Mathf.Atan2(-pointInWheel.y, pointInWheel.x) / (Mathf.PI * 2f), 1f);
                _saturation = Mathf.Clamp01(pointInWheel.magnitude);

                UpdateHex();
                current.Use();
            }

            if (GUIUtility.hotControl == _wheelControl && current.type == EventType.MouseUp)
            {
                GUIUtility.hotControl = 0;
                current.Use();
            }

            float angle = _hue * Mathf.PI * 2f;
            Vector2 cursor = wheel.center + new Vector2(Mathf.Cos(angle), -Mathf.Sin(angle)) * (_saturation * wheel.width * 0.5f);

            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(cursor.x - 5f, cursor.y - 5f, 10f, 10f), Texture2D.whiteTexture);

            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(cursor.x - 3f, cursor.y - 3f, 6f, 6f), Texture2D.whiteTexture);

            float yRow = wheel.yMax + 8f;
            string selectedHex = "#" + ColorUtility.ToHtmlStringRGB(Color.HSVToRGB(_hue, _saturation, _value));
            GUI.Label
            (
                new Rect(_panel.x + 12f, yRow, _panel.width - 24f, 30f),
                $"<color={selectedHex}>{_sample ?? "Preview"}</color>",
                _previewStyle
            );

            yRow += 34f;
            GUI.Label(new Rect(_panel.x + 12f, yRow, _panel.width - 24f, 22f), "Brightness", _label);

            float value = GUI.HorizontalSlider
            (
                new Rect(_panel.x + 12f, yRow + 24f, _panel.width - 24f, 18f),
                _value,
                0f,
                1f
            );

            if (value != _value)
            {
                _value = value;
                UpdateHex();
            }

            yRow += 48f;
            Swatch
            (
                new Rect(_panel.x + 12f, yRow, 28f, 28f),
                "#" + ColorUtility.ToHtmlStringRGB(Color.HSVToRGB(_hue, _saturation, _value))
            );

            GUI.SetNextControlName("PickerHex");
            string hex = GUI.TextField(new Rect(_panel.x + 48f, yRow, _panel.width - 124f, 28f), _hex, 7, _input);
            if (hex != _hex)
            {
                _hex = hex;
                if (RoomOptions.IsValidColor(hex) && ColorUtility.TryParseHtmlString(hex, out var color))
                {
                    Color.RGBToHSV(color, out _hue, out _saturation, out _value);
                    _preview?.Invoke(hex.ToUpperInvariant());
                }
            }

            if (GUI.Button(new Rect(_panel.xMax - 68f, yRow, 56f, 28f), "Done"))
            {
                Close();
            }

            GUI.color = oldColor;
            GUI.enabled = oldEnabled;
        }

        private void UpdateHex()
        {
            _hex = "#" + ColorUtility.ToHtmlStringRGB(Color.HSVToRGB(_hue, _saturation, _value));
            _preview?.Invoke(_hex);
        }
    }
}
