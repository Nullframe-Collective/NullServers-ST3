using UnityEngine;

namespace NullServers.UI
{
    internal sealed class MenuUi
    {
        internal readonly struct State : IDisposable
        {
            private readonly GUISkin _skin;
            private readonly Color _color;
            private readonly Color _contentColor;
            private readonly Color _backgroundColor;
            private readonly int _depth;

            internal bool Enabled { get; }

            internal State(GUISkin skin)
            {
                _skin = GUI.skin;
                _color = GUI.color;
                _contentColor = GUI.contentColor;
                _backgroundColor = GUI.backgroundColor;
                _depth = GUI.depth;
                Enabled = GUI.enabled;

                GUI.skin = skin;
                GUI.depth = -100;
                GUI.color = GUI.contentColor = GUI.backgroundColor = Color.white;
            }

            public void Dispose()
            {
                GUI.skin = _skin;
                GUI.color = _color;
                GUI.contentColor = _contentColor;
                GUI.backgroundColor = _backgroundColor;
                GUI.enabled = Enabled;
                GUI.depth = _depth;
            }
        }

        private static Texture2D _normal;
        private static Texture2D _hover;
        private static Texture2D _pressed;
        private static Texture2D _wheel;

        internal readonly GUIStyle Label;
        internal readonly GUIStyle Input;
        internal readonly GUIStyle Button;
        internal readonly GUIStyle Small;

        internal MenuUi(GUISkin skin, int buttonPadding = 10, bool toggleButtons = false, bool? wordWrap = null)
        {
            Label = TextStyle(skin.label, alignment: TextAnchor.MiddleLeft, wordWrap: wordWrap);
            Label.font = skin.font;
            Small = TextStyle(Label, wordWrap: true);
            Input = TextStyle(skin.textField);
            Input.font = skin.font;
            Button = TextStyle(skin.button, alignment: TextAnchor.MiddleCenter);
            Button.font = skin.font;
            Button.border = new RectOffset(1, 1, 1, 1);
            Button.padding = new RectOffset(buttonPadding, buttonPadding, 4, 4);

            if (!_normal)
            {
                _normal = Background(new Color(0.09f, 0.1f, 0.12f), new Color(0.27f, 0.29f, 0.33f));
                _hover = Background(new Color(0.24f, 0.1f, 0.11f), new Color(0.75f, 0.25f, 0.28f));
                _pressed = Background(new Color(0.4f, 0.09f, 0.11f), new Color(0.9f, 0.3f, 0.33f));
            }

            Button.normal.background = _normal;
            Button.hover.background = _hover;
            Button.active.background = toggleButtons ? _pressed : _hover;
            Button.focused.background = toggleButtons ? _hover : _normal;
            Button.normal.textColor = Button.hover.textColor = Button.active.textColor = Button.focused.textColor = Color.white;

            if (toggleButtons)
            {
                Button.onNormal.background = _hover;
                Button.onHover.background = Button.onActive.background = Button.onFocused.background = _pressed;
                Button.onNormal.textColor = Button.onHover.textColor = Button.onActive.textColor = Button.onFocused.textColor = Color.white;
            }
        }

        internal static GUIStyle TextStyle(
            GUIStyle source,
            bool richText = false,
            TextAnchor? alignment = null,
            bool? wordWrap = null)
        {
            GUIStyle style = new(source)
            {
                richText = richText
            };

            if (alignment.HasValue)
            {
                style.alignment = alignment.Value;
            }

            if (wordWrap.HasValue)
            {
                style.wordWrap = wordWrap.Value;
            }

            return style;
        }

        internal void Resize(int font, int minimum = 12)
        {
            Label.fontSize = Input.fontSize = Button.fontSize = font;
            Small.fontSize = Mathf.Max(minimum, font - 3);
        }

        internal static void BeginWindow(Rect rect, Color background)
        {
            GUI.color = background;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.BeginGroup(rect);
        }

        internal string Field(Rect rect, string title, string value, int limit)
        {
            GUI.Label(new Rect(rect.x, rect.y, rect.width * 0.55f, rect.height), title, Label);
            return GUI.TextField(new Rect(rect.x + rect.width * 0.56f, rect.y, rect.width * 0.44f, rect.height), value, limit, Input);
        }

        internal float Slider(Rect rect, string title, float value, float minimum, float maximum)
        {
            GUI.Label(new Rect(rect.x, rect.y, rect.width * 0.55f, rect.height), title + ": " + value.ToString("0.###"), Label);
            return GUI.HorizontalSlider(new Rect(rect.x + rect.width * 0.56f, rect.y + rect.height * 0.4f, rect.width * 0.44f, rect.height),
                value, minimum, maximum);
        }

        internal bool ActionButton(Rect rect, string text, Color color)
        {
            Color previous = GUI.contentColor;
            GUI.contentColor = color;

            try
            {
                return GUI.Button(rect, text, Button);
            }
            finally
            {
                GUI.contentColor = previous;
            }
        }

        private static Texture2D Background(Color fill, Color border)
        {
            Texture2D texture = new(3, 3, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            Color[] pixels = Enumerable.Repeat(border, 9).ToArray();
            pixels[4] = fill;
            texture.SetPixels(pixels);
            texture.Apply();

            return texture;
        }

        internal static Texture2D ColorWheel
        {
            get
            {
                if (_wheel) return _wheel;

                const int size = 256;
                Color[] pixels = new Color[size * size];

                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        Vector2 point = new((x + 0.5f) / size * 2f - 1f, (y + 0.5f) / size * 2f - 1f);
                        Color color = Color.HSVToRGB(
                            Mathf.Repeat(Mathf.Atan2(point.y, point.x) / (2f * Mathf.PI), 1f),
                            Mathf.Min(1f, point.magnitude),
                            1f);
                        color.a = Mathf.Clamp01((1f - point.magnitude) * 128f);
                        pixels[y * size + x] = color;
                    }
                }

                _wheel = new Texture2D(size, size, TextureFormat.RGBA32, false)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                _wheel.SetPixels(pixels);
                _wheel.Apply(false, true);

                return _wheel;
            }
        }

        internal static void Dispose()
        {
            foreach (Texture2D texture in new[] { _normal, _hover, _pressed, _wheel })
            {
                if (texture)
                {
                    UnityEngine.Object.Destroy(texture);
                }
            }

            _normal = _hover = _pressed = _wheel = null;
        }
    }
}
