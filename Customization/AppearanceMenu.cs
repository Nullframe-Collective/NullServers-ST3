using Il2Cpp;
using Il2CppCodeStage.AntiCheat.Storage;
using Il2CppInterop.Runtime;
using NullServers.Players;
using NullServers.UI;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace NullServers.Customization
{
    internal sealed class AppearanceMenu
    {
        private readonly PauseMenu _pause;
        private readonly LiveAppearance _appearance;
        private readonly PlayerProfiles _profiles;
        private ModelPreview _preview;
        private SyncCustomization _source;
        private RoomMultiplayerMenu _menu;
        private GameUi _ui;
        private GameColorPicker _picker;
        private GameChoices _choices;
        private RectTransform _root, _model;
        private ScrollRect _scroll;
        private Canvas _canvas;
        private Text _name, _status, _rotate;
        private Slider _zoom;
        private readonly List<Button> _tabs = new();
        private Appearance _draft, _shown;
        private string _nickname, _nameColor, _gradientColor, _message;
        private NameColorMode _colorMode;
        private float _colorSpeed, _previewAt, _row;
        private float _lighting = 0.8f;
        private int _tab;
        private bool _dragging;
        private Vector2 _mouse;

        internal AppearanceMenu(PauseMenu pause, LiveAppearance appearance, PlayerProfiles profiles)
        {
            _pause = pause;
            _appearance = appearance;
            _profiles = profiles;
            pause.Add("Customization", Open, () => pause.Available);
        }

        // Replaces the pause menu with the customization page: the model preview on the left, the tabs on the right.
        private void Open()
        {
            if (!_appearance.Local || !_pause.Hide(this)) return;

            try
            {
                _source = _appearance.Local;
                _menu = _pause.Menu;
                LoadDraft();

                _message = null;
                _dragging = false;
                _mouse = Input.mousePosition;
                _previewAt = 0f;
                _preview = new ModelPreview(_source, _appearance);
                _ui = new GameUi(_pause.Settings);
                _root = GameUi.Rect("AppearanceMenu", _pause.Root);
                _canvas = _root.GetComponentInParent(Il2CppType.Of<Canvas>()).Cast<Canvas>();
                _picker = new GameColorPicker(_ui, _root);
                _choices = new GameChoices(_ui, _root);

                BuildTabs();
                BuildPreview();

                _scroll = _ui.Scroll(_root, "AppearancePages");
                GameUi.Area(_scroll.GetComponent<RectTransform>(), 0.48f, 0.23f, 0.93f, 0.73f);
                Button apply = _ui.Button(_root, "Apply Changes", Apply);
                GameUi.Area(apply.GetComponent<RectTransform>(), 0.48f, 0.14f, 0.93f, 0.2f);
                _status = _ui.Label(_root, "", 23);
                GameUi.Area(_status.rectTransform, 0.48f, 0.035f, 0.93f, 0.125f);
                Page();
                _root.SetAsLastSibling();
            }
            catch
            {
                Close();
                throw;
            }
        }

        // The draft starts from what the player wears now. Without a skin the game's saved part URLs are used.
        private void LoadDraft()
        {
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
            _gradientColor = _profiles.Local.GradientColor;
            _colorMode = _profiles.Local.ColorMode;
            _colorSpeed = _profiles.Local.ColorSpeed;
        }

        private void BuildTabs()
        {
            Text title = _ui.Label(_root, "Customization", 42);
            GameUi.Area(title.rectTransform, 0.48f, 0.82f, 0.93f, 0.91f);
            _tabs.Clear();
            string[] tabs = ["Profile", "Skin"];

            for (int index = 0; index < tabs.Length; index++)
            {
                int tab = index;
                Button button = _ui.Button(_root, tabs[index], () =>
                {
                    _tab = tab;
                    Page();
                });
                GameUi.Area(button.GetComponent<RectTransform>(), 0.48f + index * 0.23f, 0.75f, 0.7f + index * 0.23f, 0.81f);
                _tabs.Add(button);
            }
        }

        // The preview shows the player's name and health bar above the model, like in game.
        private void BuildPreview()
        {
            _name = _ui.Label(_root, "", 32, TextAnchor.MiddleCenter);
            _name.supportRichText = true;
            GameUi.Fit(_name, Mathf.RoundToInt(24f * _ui.Unit), Mathf.RoundToInt(40f * _ui.Unit));
            GameUi.Area(_name.rectTransform, 0.08f, 0.79f, 0.43f, 0.85f);
            RectTransform health = GameUi.Rect("PreviewHealth", _root);
            GameUi.Area(health, 0.14f, 0.78f, 0.37f, 0.786f);
            _ui.Image(health, new Color(0.1f, 0.92f, 0.15f), false);

            _model = GameUi.Rect("ModelPreview", _root);
            GameUi.Area(_model, 0.08f, 0.34f, 0.43f, 0.77f);
            _ui.Image(_model, new Color(1f, 1f, 1f, 0.005f));
            RectTransform image = GameUi.Rect("ModelImage", _model);
            RawImage model = GameUi.Add<RawImage>(image.gameObject);
            model.texture = _preview.Texture;
            model.raycastTarget = false;
            AspectRatioFitter aspect = GameUi.Add<AspectRatioFitter>(image.gameObject);
            aspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            aspect.aspectRatio = (float)_preview.Texture.width / _preview.Texture.height;
            Text hint = _ui.Label(_root, "Drag to rotate · Scroll to zoom", 22, TextAnchor.MiddleCenter);
            GameUi.Area(hint.rectTransform, 0.08f, 0.305f, 0.43f, 0.34f);

            RectTransform zoom = GameUi.Rect("PreviewZoom", _root);
            GameUi.Area(zoom, 0.08f, 0.24f, 0.43f, 0.295f);
            _zoom = _ui.Range(zoom, "Zoom", _preview.Zoom, 0.7f, 2.5f, value => _preview.Zoom = value);
            RectTransform light = GameUi.Rect("PreviewLight", _root);
            GameUi.Area(light, 0.08f, 0.18f, 0.43f, 0.235f);
            _ui.Range(light, "Preview Light", _lighting, 0.15f, 2f, value => _lighting = value);
            Button rotate = _ui.Button(_root, "", () => _preview.AutoRotate = !_preview.AutoRotate);
            GameUi.Area(rotate.GetComponent<RectTransform>(), 0.08f, 0.11f, 0.43f, 0.17f);
            _rotate = rotate.GetComponentInChildren(Il2CppType.Of<Text>(), true).Cast<Text>();
            Button back = _ui.Button(_root, "BACK", Close);
            GameUi.Area(back.GetComponent<RectTransform>(), 0.08f, 0.035f, 0.26f, 0.095f);
        }

        private void Close()
        {
            if (_root)
            {
                _root.gameObject.SetActive(false);
                Object.Destroy(_root.gameObject);
            }

            _root = null;
            _preview?.Dispose();
            _preview = null;
            _picker = null;
            _choices = null;
            _pause.Restore(this);
        }

        internal void Update()
        {
            if (_preview == null) return;

            if (!_root || !_pause.Available || !_source || _appearance.Local != _source || !_menu || _menu != _pause.Menu || !_menu.IsPaused)
            {
                Close();
                return;
            }

            _picker.Update();
            if (Time.unscaledTime >= _previewAt)
            {
                _shown = _draft;
            }

            _preview.Lighting = _lighting;
            _preview.Update(_shown, _appearance.Textures);
            _name.text = PlayerProfile.Normalize(
                string.IsNullOrWhiteSpace(_nickname) ? _profiles.DefaultName : _nickname,
                _nameColor, _colorMode, _gradientColor, _colorSpeed).RichName;
            _rotate.text = "Auto Rotate: " + (_preview.AutoRotate ? "ON" : "OFF");
            _status.text = (_draft.UseSkin ? _appearance.Textures.ErrorFor(_draft.Urls) : null) ?? _message ??
                "Apply updates your character without respawning.";

            Camera camera = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;
            Vector2 mouse = Input.mousePosition;
            bool inside = RectTransformUtility.RectangleContainsScreenPoint(_model, mouse, camera);

            if (_picker.IsOpen || _choices.IsOpen)
            {
                _dragging = false;
            }
            else
            {
                if (inside && Input.GetMouseButtonDown(0))
                {
                    _dragging = true;
                }

                if (_dragging && Input.GetMouseButton(0))
                {
                    Vector2 delta = mouse - _mouse;
                    if (delta.sqrMagnitude > 0f)
                    {
                        _preview.Rotate(new Vector2(delta.x, -delta.y));
                    }
                }

                if (inside && Input.mouseScrollDelta.y != 0f)
                {
                    _zoom.value = Mathf.Clamp(_preview.Zoom + Input.mouseScrollDelta.y * 0.08f, 0.7f, 2.5f);
                }

                if (Input.GetMouseButtonUp(0))
                {
                    _dragging = false;
                }
            }

            _mouse = mouse;
        }

        private RectTransform Line(float height = 58f)
        {
            RectTransform row = _ui.Row(_scroll.content, _row, height);
            _row += height + 16f;
            return row;
        }

        private void Page(bool reset = true)
        {
            _picker.Close();
            _choices.Close();
            float position = reset ? 1f : _scroll.verticalNormalizedPosition;
            _scroll.StopMovement();
            GameUi.Clear(_scroll.content);
            _row = 0f;

            if (_tab == 0)
            {
                Profile();
            }
            else
            {
                Skin();
            }

            _ui.Height(_scroll.content, Mathf.Max(0f, _row - 16f));
            Canvas.ForceUpdateCanvases();
            _scroll.verticalNormalizedPosition = position;

            for (int index = 0; index < _tabs.Count; index++)
            {
                GameUi.Select(_tabs[index], index == _tab);
            }
        }

        private void ColorField(string title, string value, Action<string> changed)
        {
            RectTransform row = Line();
            _ui.Caption(row, title);
            _ui.ColorField(row, value, _picker, changed);
        }

        private void Choice(string title, string value, IEnumerable<(string Key, string Title)> choices, Action<string> selected)
        {
            RectTransform row = Line();
            _ui.Caption(row, title);
            _ui.RowButton(row, value + "  [v]", () => _choices.Open(title, choices, key =>
            {
                selected(key);
                Page(false);
            }), 0.46f);
        }

        private void Profile()
        {
            _ui.Label(Line(40f), "Nickname", 28);
            _ui.Input(Line(), _nickname, "Nickname", value => _nickname = value, PlayerProfile.NameLimit * 2);
            ColorField("Nickname Color", _nameColor, value => _nameColor = value);
            Choice("Color Mode", _colorMode.ToString(), Enum.GetValues<NameColorMode>().Select(mode => (mode.ToString(), mode.ToString())),
                value => _colorMode = Enum.Parse<NameColorMode>(value));
            if (_colorMode == NameColorMode.Gradient)
            {
                ColorField("Second Color", _gradientColor, value => _gradientColor = value);
            }

            if (_colorMode != NameColorMode.Static)
            {
                _ui.Range(Line(), "Color Speed", _colorSpeed, 0.1f, 5f, value => _colorSpeed = value);
            }
        }

        private void Skin()
        {
            string HatName(int id) => id < 0 ? "None" : _appearance.Hats.TryGetValue(id, out var title) ? title : _source.Hats[id].name;

            for (int slot = 0; slot < (_appearance.SlotsAvailable ? 4 : 1); slot++)
            {
                int selectedSlot = slot;
                int[] hats = new[] { _draft.Hat }.Concat(_draft.ExtraHats ?? [-1, -1, -1]).ToArray();
                IEnumerable<int> choices = Enumerable.Range(0, Math.Min(256, _source.Hats.Count))
                    .Where(id => id == 0 ? selectedSlot == 0 : !hats.Where((_, index) => index != selectedSlot).Contains(id));
                if (slot > 0)
                {
                    choices = choices.Prepend(-1);
                }

                Choice(_appearance.SlotsAvailable ? "Hat Slot " + (slot + 1) : "Hat", HatName(hats[slot]),
                    choices.Select(id => (id.ToString(), HatName(id))), value =>
                    {
                        int[] selection = new[] { _draft.Hat }.Concat(_draft.ExtraHats ?? [-1, -1, -1]).ToArray();
                        selection[selectedSlot] = int.Parse(value);
                        _draft = _draft with { Hat = selection[0], ExtraHats = _appearance.SlotsAvailable ? selection[1..] : null };
                        _previewAt = 0f;
                    });
            }

            ColorField("Fur Color", _draft.FurColor, value =>
            {
                _draft = _draft with { FurColor = value };
                _previewAt = 0f;
            });
            _ui.RowButton(Line(), "Custom Skin: " + (_draft.UseSkin ? "ON" : "OFF"), () =>
            {
                _draft = _draft with { UseSkin = !_draft.UseSkin };
                _previewAt = 0f;
                Page(false);
            });
            UrlField("Head URL", _draft.HeadUrl, value => _draft = _draft with { HeadUrl = value });
            UrlField("Body URL", _draft.BodyUrl, value => _draft = _draft with { BodyUrl = value });
            UrlField("Arms URL", _draft.ArmsUrl, value => _draft = _draft with { ArmsUrl = value });
            UrlField("Legs URL", _draft.LegsUrl, value => _draft = _draft with { LegsUrl = value });
        }

        private void UrlField(string title, string value, Action<string> changed)
        {
            _ui.Label(Line(40f), title, 28);
            _ui.Input(Line(), value, "https://...", url =>
            {
                changed(url);
                _previewAt = Time.unscaledTime + 0.5f;
            }, Appearance.UrlLimit);
        }

        private void Apply()
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
                _profiles.Save(
                    string.IsNullOrWhiteSpace(_nickname) ? _profiles.DefaultName : _nickname,
                    _nameColor, _colorMode, _gradientColor, _colorSpeed);
                _nickname = _profiles.Local.Nickname;
                _message = "Appearance and nickname applied for all players.";
                Page(false);
            }
            else
            {
                _message = error;
            }
        }
    }
}
