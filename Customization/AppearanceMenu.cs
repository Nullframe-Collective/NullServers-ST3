using Il2Cpp;
using Il2CppCodeStage.AntiCheat.Storage;
using NullServers.Players;
using NullServers.UI;
using UnityEngine;

namespace NullServers.Customization
{
    internal sealed class AppearanceMenu
    {
        private readonly PauseMenu _pause;
        private readonly LiveAppearance _appearance;
        private readonly PlayerProfiles _profiles;
        private readonly ColorPicker _picker;
        private ModelPreview _preview;
        private SyncCustomization _source;
        private RoomMultiplayerMenu _menu;
        private Appearance _draft;
        private Appearance _shown;
        private string _nickname;
        private string _nameColor;
        private string _message;
        private bool _open;
        private bool _hatExpanded;
        private Vector2 _hatScroll;
        private float _previewAt;
        private Rect _window;
        private GUIStyle _label;
        private GUIStyle _input;
        private GUIStyle _button;
        private GUIStyle _small;
        private GUIStyle _name;

        internal AppearanceMenu(PauseMenu pause, LiveAppearance appearance, PlayerProfiles profiles, ColorPicker picker)
        {
            _pause = pause;
            _appearance = appearance;
            _profiles = profiles;
            _picker = picker;
            pause.Add("Customization", Open, () => pause.Available);
        }

        private void Open()
        {
            if (!_appearance.Local || !_pause.Hide(this)) return;

            try
            {
                _source = _appearance.Local;
                _menu = _pause.Menu;
                _draft = LiveAppearance.Read(_source);

                if (!_draft.UseSkin)
                {
                    _draft = _draft with
                    {
                        HeadUrl = ObscuredPrefs.GetString("HeadURL"),
                        BodyUrl = ObscuredPrefs.GetString("BodyURL"),
                        ArmsUrl = ObscuredPrefs.GetString("ArmsURL"),
                        LegsUrl = ObscuredPrefs.GetString("LegsURL")
                    };
                }

                _shown = _draft;
                _nickname = string.IsNullOrWhiteSpace(_profiles.Local.Nickname) ? _profiles.DefaultName : _profiles.Local.Nickname;
                _nameColor = _profiles.Local.ColorHex;
                _message = null;
                _hatExpanded = false;
                _preview = new ModelPreview(_source, _appearance);
                _open = true;
            }
            finally
            {
                if (!_open) _pause.Restore(this);
            }
        }

        private void Close()
        {
            _open = false;
            _picker.Close();
            _preview?.Dispose();
            _preview = null;
            _pause.Restore(this);
        }

        internal void Update()
        {
            if (!_open) return;

            if 
            (
                !_pause.Available ||
                !_source ||
                _appearance.Local != _source ||
                !_menu ||
                _menu != _pause.Menu ||
                !_menu.IOMIAHNBDOG
            )
            {
                Close();

                return;
            }

            if (Time.unscaledTime >= _previewAt)
            {
                _shown = _draft;
            }

            _preview.Update(_shown, _appearance.Textures);
        }

        internal void Draw()
        {
            if (!_open || !_menu) return;

            GUISkin skin = GUI.skin;
            Color color = GUI.color;
            Color contentColor = GUI.contentColor;
            Color backgroundColor = GUI.backgroundColor;
            bool enabled = GUI.enabled;
            int depth = GUI.depth;

            GUI.skin = _menu.BIPMFIBNFBC;
            GUI.depth = -100;
            GUI.contentColor = GUI.backgroundColor = GUI.color = Color.white;

            if (_label == null)
            {
                _label = new GUIStyle(GUI.skin.label)
                {
                    font = GUI.skin.font,
                    richText = false,
                    alignment = TextAnchor.MiddleLeft
                };

                _small = new GUIStyle(_label)
                {
                    wordWrap = true
                };

                _name = new GUIStyle(_label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    clipping = TextClipping.Clip
                };

                _input = new GUIStyle(GUI.skin.textField)
                {
                    font = GUI.skin.font,
                    richText = false
                };

                _button = new GUIStyle(GUI.skin.button)
                {
                    font = GUI.skin.font,
                    richText = false,
                    alignment = TextAnchor.MiddleCenter,
                    border = new RectOffset(1, 1, 1, 1),
                    padding = new RectOffset(10, 10, 4, 4)
                };

                _button.normal.background = ServerSettings.ButtonBackground
                (
                    new Color(0.09f, 0.1f, 0.12f),
                    new Color(0.27f, 0.29f, 0.33f)
                );

                _button.hover.background = ServerSettings.ButtonBackground
                (
                    new Color(0.24f, 0.1f, 0.11f),
                    new Color(0.75f, 0.25f, 0.28f)
                );

                _button.active.background = _button.hover.background;
                _button.focused.background = _button.normal.background;

                foreach (GUIStyleState state in new[]
                {
                    _button.normal,
                    _button.hover,
                    _button.active,
                    _button.focused
                })
                {
                    state.textColor = Color.white;
                }

                _label.normal.textColor = _small.normal.textColor = Color.white;
            }

            int font = Mathf.Clamp(Screen.height / 42, 15, 27);
            _label.fontSize = _input.fontSize = _button.fontSize = font;
            _name.fontSize = font;
            _small.fontSize = Mathf.Max(12, font - 3);

            float row = font * 1.7f;
            float width = Mathf.Min(Screen.width * 0.88f, 1350f);

            _window = new Rect((Screen.width - width) / 2f, Screen.height * 0.06f, width, Screen.height * 0.88f);
            _picker.HandleInput();

            if (_picker.IsOpen && Event.current.type != EventType.Repaint && Event.current.type != EventType.Layout)
            {
                GUI.enabled = false;
            }

            GUI.color = new Color(0.015f, 0.02f, 0.03f, 0.99f);
            GUI.DrawTexture(_window, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.BeginGroup(_window);

            try
            {
                GUI.Label(new Rect(20f, 10f, width - 150f, row), "Player Customization", _label);
                if (GUI.Button(new Rect(width - 110f, 10f, 90f, row), "Close", _button))
                {
                    Close();

                    return;
                }

                float left = 20f;
                float sideWidth = width * 0.235f;
                float right = width - sideWidth - 20f;
                float centerX = left + sideWidth + 18f;
                float centerWidth = width - sideWidth * 2f - 76f;
                float controlsY = row + 28f;
                Rect hatArea = new(centerX, controlsY + row, centerWidth, Mathf.Min(row * 6f, _window.height * 0.45f));

                if 
                (
                    _hatExpanded &&
                    Event.current.type == EventType.MouseDown &&
                    !hatArea.Contains(Event.current.mousePosition)
                )
                {
                    _hatExpanded = false;
                    Event.current.Use();
                }

                bool controlsEnabled = GUI.enabled;
                if (_hatExpanded && Event.current.type != EventType.Layout && Event.current.type != EventType.Repaint)
                {
                    GUI.enabled = false;
                }

                Rect previewRect = new(centerX, controlsY + row * 2.5f, centerWidth, _window.height - controlsY - row * 5.5f);
                Rect modelRect = new
                (
                    previewRect.x,
                    previewRect.y + row * 1.3f,
                    previewRect.width,
                    previewRect.height - row * 1.3f
                );

                GUI.DrawTexture(modelRect, _preview.Texture, ScaleMode.ScaleToFit);
                DrawNameplate(previewRect, row);

                if (previewRect.Contains(Event.current.mousePosition) && !_picker.IsOpen && !_hatExpanded)
                {
                    if (Event.current.type == EventType.ScrollWheel)
                    {
                        _preview.Zoom = Mathf.Clamp(_preview.Zoom - Event.current.delta.y * 0.08f, 0.7f, 2.5f);
                        Event.current.Use();
                    }
                    else if (Event.current.type == EventType.MouseDrag && Event.current.button == 0)
                    {
                        _preview.Rotate(Event.current.delta);
                        Event.current.Use();
                    }
                }

                Appearance old = _draft;
                float fieldsY = controlsY + row * 3.2f;

                _draft = _draft with
                {
                    HeadUrl = UrlField("Head URL", _draft.HeadUrl, left, fieldsY, sideWidth, row),
                    ArmsUrl = UrlField("Arms URL", _draft.ArmsUrl, left, fieldsY + row * 2.6f, sideWidth, row),
                    BodyUrl = UrlField("Body URL", _draft.BodyUrl, right, fieldsY, sideWidth, row),
                    LegsUrl = UrlField("Legs URL", _draft.LegsUrl, right, fieldsY + row * 2.6f, sideWidth, row)
                };

                if (_draft != old)
                {
                    _previewAt = Time.unscaledTime + 0.5f;
                }

                float nicknameY = fieldsY + row * 5.5f;
                GUI.Label(new Rect(left, nicknameY, sideWidth, row), "Nickname", _label);

                _nickname = GUI.TextField
                (
                    new Rect(left, nicknameY + row, sideWidth, row),
                    _nickname,
                    PlayerProfile.NameLimit * 2,
                    _input
                );

                Rect nameSwatch = new(left, nicknameY + row * 2.3f, row, row);
                if (ColorPicker.Swatch(nameSwatch, _nameColor))
                {
                    OpenColor(nameSwatch, _nameColor, value => _nameColor = value, _nickname);
                }

                GUI.Label(new Rect(left + row + 8f, nameSwatch.y, sideWidth - row - 8f, row), "Nickname Color", _label);
                int[] hats = Enumerable.Range(0, _source.GKCNMADGAAF.Count).ToArray();
                string HatName(int id) => _appearance.Hats.TryGetValue(id, out var title) ? title : _source.GKCNMADGAAF[id].name;

                if 
                (
                    GUI.Button
                    (
                        new Rect(centerX, controlsY, centerWidth, row),
                        "Hat: " + HatName(_draft.Hat) + "  [v]",
                        _button
                    )
                )
                {
                    _hatExpanded = !_hatExpanded;
                }

                Rect furSwatch = new(centerX, controlsY + row * 1.2f, row, row);
                if (ColorPicker.Swatch(furSwatch, _draft.FurColor))
                {
                    OpenColor(furSwatch, _draft.FurColor, value =>
                    {
                        _draft = _draft with
                        {
                            FurColor = value
                        };
                        _previewAt = 0f;
                    }, "Fur Color");
                }

                GUI.Label(new Rect(centerX + row + 8f, controlsY + row * 1.2f, centerWidth * 0.38f, row), "Fur Color", _label);
                if 
                (
                    GUI.Button
                    (
                        new Rect(centerX + centerWidth * 0.52f, controlsY + row * 1.2f, centerWidth * 0.48f, row),
                        _draft.UseSkin ? "Skin: ON" : "Skin: OFF",
                        _button
                    )
                )
                {
                    _draft = _draft with
                    {
                        UseSkin = !_draft.UseSkin
                    };

                    _previewAt = 0f;
                }

                float bottom = _window.height - row * 2.8f;
                if 
                (
                    GUI.Button
                    (
                        new Rect(centerX, bottom - row * 1.2f, centerWidth * 0.62f, row),
                        _preview.AutoRotate ? "Auto Rotate: ON" : "Auto Rotate: OFF",
                        _button
                    )
                )
                {
                    _preview.AutoRotate = !_preview.AutoRotate;
                }

                _preview.Zoom = GUI.HorizontalSlider
                (
                    new Rect
                    (
                        centerX + centerWidth * 0.67f,
                        bottom - row * 0.9f,
                        centerWidth * 0.33f,
                        row
                    ),
                    _preview.Zoom,
                    0.7f,
                    2.5f
                );

                if (GUI.Button(new Rect(centerX, bottom, centerWidth, row), "Apply Changes", _button))
                {
                    string[] urls = _draft.Urls.Select(url => url.Trim()).ToArray();
                    _draft = _draft with
                    {
                        HeadUrl = urls[0],
                        BodyUrl = urls[1],
                        ArmsUrl = urls[2],
                        LegsUrl = urls[3]
                    };

                    if (_appearance.Apply(_draft, out var error))
                    {
                        _profiles.Save(string.IsNullOrWhiteSpace(_nickname) ? _profiles.DefaultName : _nickname, _nameColor);
                        _nickname = _profiles.Local.Nickname;
                        _message = "Appearance and nickname applied for all players.";
                    }
                    else
                    {
                        _message = error;
                    }
                }

                GUI.Label
                (
                    new Rect(20f, _window.height - row * 1.35f, width - 40f, row * 1.2f),
                    (_draft.UseSkin ? _appearance.Textures.ErrorFor(_draft.Urls) : null) ?? _message ??
                    "Drag the model to rotate. Scroll to zoom. Apply updates your character without respawning.",
                    _small
                );

                if (_hatExpanded)
                {
                    GUI.enabled = controlsEnabled;
                    Rect area = hatArea;

                    GUI.color = new Color(0.04f, 0.045f, 0.055f);
                    GUI.DrawTexture(area, Texture2D.whiteTexture);

                    GUI.color = Color.white;
                    _hatScroll = GUI.BeginScrollView(area, _hatScroll, new Rect(0f, 0f, centerWidth - 22f, hats.Length * row));

                    try
                    {
                        foreach (int id in hats)
                        {
                            if (GUI.Button(new Rect(0f, id * row, centerWidth - 22f, row), HatName(id), _button))
                            {
                                _draft = _draft with
                                {
                                    Hat = id
                                };

                                _previewAt = 0f;
                                _hatExpanded = false;
                            }
                        }
                    }
                    finally
                    {
                        GUI.EndScrollView();
                    }
                }
            }
            finally
            {
                GUI.EndGroup();
                GUI.enabled = enabled;

                if (_open)
                {
                    _picker.Draw(_menu.BIPMFIBNFBC);
                }

                GUI.skin = skin;
                GUI.color = color;
                GUI.contentColor = contentColor;
                GUI.backgroundColor = backgroundColor;
                GUI.depth = depth;
            }
        }

        private string UrlField(string title, string value, float x, float y, float width, float row)
        {
            GUI.Label(new Rect(x, y, width, row), title, _label);

            return GUI.TextField(new Rect(x, y + row, width, row), value ?? "", Appearance.UrlLimit, _input);
        }

        private void DrawNameplate(Rect area, float row)
        {
            PlayerProfile profile = PlayerProfile.Normalize
            (
                string.IsNullOrWhiteSpace(_nickname) ? _profiles.DefaultName : _nickname,
                _nameColor
            );

            ColorUtility.TryParseHtmlString(profile.ColorHex, out var color);
            _name.normal.textColor = color;

            float width = Mathf.Min
            (
                area.width - 16f,
                Mathf.Max
                (
                    row * 4f,
                    _name.CalcSize(new GUIContent(profile.Nickname)).x + 24f
                )
            );
            float x = area.center.x - width / 2f;

            GUI.Label(new Rect(x, area.y, width, row), profile.Nickname, _name);

            GUI.color = new Color(0.025f, 0.03f, 0.035f);
            GUI.DrawTexture(new Rect(x, area.y + row, width, 7f), Texture2D.whiteTexture);

            GUI.color = new Color(0.1f, 0.92f, 0.15f);

            GUI.DrawTexture(new Rect(x + 1f, area.y + row + 1f, width - 2f, 5f), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        private void OpenColor(Rect rect, string color, Action<string> changed, string sample)
        {
            _picker.Open
            (
                new Rect(_window.x + rect.x, _window.y + rect.y, rect.width, rect.height),
                color,
                changed,
                changed,
                sample
            );
        }
    }
}
