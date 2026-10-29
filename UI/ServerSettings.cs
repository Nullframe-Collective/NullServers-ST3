using Il2Cpp;
using Il2CppInterop.Runtime;
using NullServers.Network;
using NullServers.Room;
using NullServers.Server;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using System.Globalization;
using Object = UnityEngine.Object;

namespace NullServers.UI
{
    internal sealed class ServerSettings
    {
        private readonly ServerControl _control;
        private readonly PauseMenu _pause;
        private RoomMultiplayerMenu _menu;
        private GameUi _ui;
        private GameColorPicker _picker;
        private GameChoices _choices;
        private RectTransform _root, _content;
        private ScrollRect _scroll;
        private CanvasGroup _controls;
        private Text _status, _preview, _protection;
        private Slider _fogEnd;
        private float? _flightSpeed;
        private float _flightSpeedAt;
        private string _roomId, _name, _capacity, _color, _scene, _mode, _minutes, _custards, _message, _state;
        private RenderOptions _render;
        private int _tab, _difficulty;
        private bool _muteGuests;
        private string[] _worldScale, _worldRotation, _worldGravity, _partPosition, _partScale, _partRotation;
        private MapPart _part;
        private float _row, _refreshAt;
        private readonly List<Button> _tabs = new();

        internal ServerSettings(ServerControl control, PauseMenu pause)
        {
            _control = control;
            _pause = pause;
            pause.Add("Server Settings", Open, () => _control.Access.CanOpen);
        }

        internal void Update()
        {
            if (!_root) return;

            RoomMultiplayerMenu menu = RoomMultiplayerMenu.Current;
            if (!_control.Access.CanOpen || !menu || menu != _menu || !menu.IsPaused || _control.Room.Id != _roomId)
            {
                Close();
                return;
            }

            _controls.interactable = !_control.Busy;
            if (_flightSpeed is { } speed && Time.unscaledTime >= _flightSpeedAt)
            {
                _flightSpeed = null;
                _control.Access.SetEnvironment(_control.Access.Environment with { FlightSpeed = speed }, out _message);
            }

            _status.text = _message ?? _control.Access.Status ?? _control.Status ?? "Room management";
            _picker.Update();

            if (_preview)
            {
                bool valid = RoomName.TryParse(_name, out _, out var display, out var error);
                string color = RoomOptions.IsValidColor(_color) ? _color : "#FFFFFF";
                _preview.text = valid ? $"<color={color}>{display}</color>" : error;
            }

            if (_protection)
            {
                _protection.text = $"Anti-Crash: ACTIVE · Blocked: {_control.Protection.Blocked} · Removed: {_control.Protection.Removed}";
            }

            if (Time.unscaledTime >= _refreshAt)
            {
                _refreshAt = Time.unscaledTime + 0.25f;
                if (State() != _state)
                {
                    Page(false);
                }
            }
        }

        private void ReadRoomState()
        {
            Il2CppFishNet.Transporting.Relay.RelayRoomInfo room = _control.Room;
            _roomId = room.Id;
            _name = RoomName.ReadDisplay(room.RoomData, room.Name);
            _capacity = room.MaxPlayers.ToString();
            _color = RoomOptions.ReadNameColor(room.RoomData) ?? "#FFFFFF";

            RoomProperties properties = RoomProperties.Parse(room.RoomData);
            _scene = properties.MapName;
            _mode = properties.GameMode;
            _minutes = Math.Max(1, properties.RoundDuration / 60).ToString();
            _custards = properties.Custards.ToString();
            _difficulty = properties.Difficulty;
            _muteGuests = properties.MuteGuests;

            WorldSettings world = _control.Access.Environment.World ?? WorldSettings.Default;
            _render = _control.Rendering.Current;
            _worldScale = VectorText(world.Scale);
            _worldRotation = VectorText(world.Rotation);
            _worldGravity = VectorText(world.Gravity);

            _part = _control.Rendering.Geometry.Aimed(Camera.main);
            _partPosition = _partScale = _partRotation = null;
        }

        private void Open()
        {
            if (!_control.Access.CanOpen || !_pause.Hide(this)) return;

            _menu = _pause.Menu;
            _message = null;
            ReadRoomState();

            try
            {
                _ui = new GameUi(_pause.Settings);
                _root = GameUi.Rect("ServerSettings", _pause.Root);

                Text title = _ui.Label(_root, "Server Settings", 42);
                GameUi.Area(title.rectTransform, 0.32f, 0.82f, 0.92f, 0.91f);

                RectTransform navigation = GameUi.Rect("Sections", _root);
                GameUi.Area(navigation, 0.08f, 0.18f, 0.26f, 0.76f);

                _tabs.Clear();
                string[] tabs = ["Room", "Round", "Players", "Bans", "Chat", "Rendering", "World"];

                for (int index = 0; index < tabs.Length; index++)
                {
                    int tab = index;
                    Button button = _ui.RowButton(_ui.Row(navigation, index * 64f, 54f), tabs[index], () =>
                    {
                        _tab = tab;
                        _message = null;
                        Page();
                    });
                    Text label = button.GetComponentInChildren(Il2CppType.Of<Text>(), true).Cast<Text>();
                    GameUi.Fit(label, Mathf.RoundToInt(18f * _ui.Unit), Mathf.RoundToInt(42f * _ui.Unit));
                    _tabs.Add(button);
                }

                Button back = _ui.Button(_root, "BACK", Close);
                GameUi.Area(back.GetComponent<RectTransform>(), 0.08f, 0.07f, 0.26f, 0.14f);
                _scroll = _ui.Scroll(_root, "ServerPages");
                GameUi.Area(_scroll.GetComponent<RectTransform>(), 0.32f, 0.17f, 0.92f, 0.79f);
                _content = _scroll.content;
                _controls = GameUi.Add<CanvasGroup>(_content.gameObject);
                _status = _ui.Label(_root, "", 23);
                _status.color = new Color(1f, 1f, 1f, 0.65f);
                GameUi.Area(_status.rectTransform, 0.32f, 0.055f, 0.92f, 0.14f);
                _picker = new GameColorPicker(_ui, _root);
                _choices = new GameChoices(_ui, _root);
                Page();
                _root.SetAsLastSibling();
            }
            catch
            {
                Close();
                throw;
            }
        }

        private void Close()
        {
            if (_root)
            {
                _root.gameObject.SetActive(false);
                Object.Destroy(_root.gameObject);
            }

            _root = null;
            _picker = null;
            _choices = null;
            _pause.Restore(this);
        }

        private string State()
        {
            return _tab switch
            {
                0 => _control.CanManage + ":" + RoomProperties.Parse(_control.Room.RoomData).IsOpen,
                2 => _control.CanManage + ":" + string.Join("|", _control.Access.Players.Select(player =>
                    $"{player.Id}:{player.Account}:{player.Nickname}:{player.Guest}:{player.Trusted}:{player.Muted}")),
                3 or 4 => string.Join("|", (_tab == 4 ? _control.Access.MutedAccounts : _control.Access.Bans)
                    .Select(entry => $"{entry.Account}:{entry.Nickname}:{entry.CreatedAt}")),
                5 => _render == null && _control.Rendering.Current != null ? "ready" : "",
                _ => ""
            };
        }

        private void Page(bool reset = true)
        {
            _choices.Close();
            _picker.Close();
            float position = reset ? 1f : _scroll.verticalNormalizedPosition;
            _scroll.StopMovement();
            GameUi.Clear(_content);
            _preview = _protection = null;
            _fogEnd = null;
            _row = 0f;
            _render ??= _control.Rendering.Current;

            switch (_tab)
            {
                case 0: Room(); break;
                case 1: Round(); break;
                case 2: Players(); break;
                case 3: Accounts(false); break;
                case 4: Accounts(true); break;
                case 5: Rendering(); break;
                case 6: World(); break;
            }

            _ui.Height(_content, Mathf.Max(0f, _row - 16f));
            Canvas.ForceUpdateCanvases();
            _scroll.verticalNormalizedPosition = position;

            for (int index = 0; index < _tabs.Count; index++)
            {
                GameUi.Select(_tabs[index], index == _tab);
            }

            _state = State();
        }

        private RectTransform Line(float height = 52f)
        {
            RectTransform row = _ui.Row(_content, _row, height);
            _row += height + 16f;
            return row;
        }

        private Text Label(string text, int size = 26, float height = 52f) => _ui.Label(Line(height), text, size);

        private void Field(string title, string value, int limit, Action<string> changed)
        {
            RectTransform row = Line();
            _ui.Caption(row, title);
            _ui.Field(row, 0.46f, 1f, value, "", changed, limit);
        }

        private Button Action(string title, Action clicked, bool enabled = true, Color? color = null)
        {
            Button button = _ui.RowButton(Line(), title, clicked, color: color);
            button.interactable = enabled;
            return button;
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

        private void Room()
        {
            Label("Room Name", 28, 40f);
            _ui.Input(Line(), _name, "Room name", value => _name = value, RoomName.MaxDisplayBytes);
            _preview = Label("", 30, 60f);
            _preview.supportRichText = true;
            Text hint = Label("Partial color: <color=#FF5555>Red</color> White", 22, 36f);
            hint.supportRichText = true;
            Field("Max Players (up to 50)", _capacity, 2, value => _capacity = value);
            ColorField("Default Name Color", _color, value => _color = value);
            Action("Apply Room Settings", () =>
            {
                if (!int.TryParse(_capacity, out var capacity))
                {
                    _message = "Enter a valid player limit.";
                }
                else
                {
                    _control.ApplyRoom(_name, capacity, _color, out _message);
                }
            });
            if (!RoomProperties.Parse(_control.Room.RoomData).IsOpen)
            {
                Action("Reopen Room", () => _control.Reopen(out _message));
            }

            if (_control.CanManage)
            {
                Action("Spawn Sandbox Console", () =>
                    _message = _control.Tools.SpawnConsole(out var error) ? "Sandbox console enabled. Use the game's console key to open it." : error);
            }

            if (_control.CanManage)
            {
                _protection = Label("", 22, 64f);
            }

            Check("Keep ragdolls (up to 512)", _control.Access.Environment.KeepRagdolls, value =>
                _control.Access.SetEnvironment(_control.Access.Environment with { KeepRagdolls = value }, out _message));
            Check("Throw Any One", _control.Access.Environment.ThrowAnyone, value =>
                _control.Access.SetEnvironment(_control.Access.Environment with { ThrowAnyone = value }, out _message));
            Check("Allow flight for everyone (X)", _control.Access.Environment.AllowFlight, value =>
                _control.Access.SetEnvironment(_control.Access.Environment with { AllowFlight = value }, out _message));
            Check("Flight passes through objects", _control.Access.Environment.FlightNoclip, value =>
                _control.Access.SetEnvironment(_control.Access.Environment with { FlightNoclip = value }, out _message));
            Slider speed = Range("Flight speed", _control.Access.Environment.FlightSpeed, EnvironmentSettings.MinFlightSpeed, EnvironmentSettings.MaxFlightSpeed, value =>
            {
                _flightSpeed = Mathf.Round(value * 2f) / 2f;
                _flightSpeedAt = Time.unscaledTime + 0.4f;
            });
            speed.interactable = _control.CanManage;
            Check("Allow prone for everyone (C)", _control.Access.Environment.AllowProne, value =>
                _control.Access.SetEnvironment(_control.Access.Environment with { AllowProne = value }, out _message));
        }

        private void Round()
        {
            Choice("Map", _scene ?? "Choose map", _control.Maps.Select(scene => (scene, scene)), value => _scene = value);
            Choice("Game Mode", _control.Modes.GetValueOrDefault(_mode ?? "") ?? _mode ?? "Choose mode",
                _control.Modes.Select(mode => (mode.Key, mode.Value)), value => _mode = value);
            Field("Timer (minutes, 1–60)", _minutes, 3, value => _minutes = value);
            Field("Custards (1–200)", _custards, 3, value => _custards = value);
            string[] difficulty = ["Easy", "Normal", "Hard", "Impossible"];
            Choice("Difficulty", difficulty[Math.Clamp(_difficulty, 0, 3)],
                difficulty.Select((name, index) => (index.ToString(), name)), value => _difficulty = int.Parse(value));
            Action("Mute Guests: " + (_muteGuests ? "ON" : "OFF"), () =>
            {
                _muteGuests = !_muteGuests;
                Page(false);
            });
            Label("Starts a fresh round for everyone. Scores, teams and map objects reset.", 23, 72f);
            Action("Start New Round", () =>
            {
                if (!int.TryParse(_minutes, out var minutes) || !int.TryParse(_custards, out var custards))
                {
                    _message = "Enter valid round settings.";
                    return;
                }

                _control.RestartRound(_scene, _mode, minutes, custards, _difficulty, _muteGuests, out _message);
                if (_message == null)
                {
                    Close();
                }
            });
        }

        private Slider Range(string title, float value, float minimum, float maximum, Action<float> changed) =>
            _ui.Range(Line(), title, value, minimum, maximum, changed);

        private void Rendering()
        {
            if (_render == null)
            {
                Label("Wait for the map to initialize.");
                return;
            }

            Action("Fog: " + (_render.Fog ? "ON" : "OFF"), () =>
            {
                _render = _render with { Fog = !_render.Fog };
                Page(false);
            });
            Choice("Fog mode", ((FogMode)_render.FogMode).ToString(),
                Enumerable.Range(1, 3).Select(mode => (mode.ToString(), ((FogMode)mode).ToString())),
                value => _render = _render with { FogMode = int.Parse(value) });
            ColorField("Fog color", _render.FogColor, value => _render = _render with { FogColor = value });
            Range("Fog density", _render.FogDensity, 0f, 0.1f, value => _render = _render with { FogDensity = value });
            Range("Fog start", _render.FogStart, 0f, 2000f, value =>
            {
                _render = _render with { FogStart = value };
                if (_fogEnd)
                {
                    _fogEnd.minValue = value;
                }
            });
            _fogEnd = Range("Fog end", _render.FogEnd, _render.FogStart, 5000f, value => _render = _render with { FogEnd = value });

            var skyboxes = _control.Rendering.Skyboxes.Keys.OrderBy(name => name, StringComparer.Ordinal)
                .Select(name => (name, name)).Prepend(("", "OFF"));
            Choice("Skybox", _render.Skybox ? _render.SkyboxName : "OFF", skyboxes, value =>
                _render = _render with { Skybox = value.Length > 0, SkyboxName = value.Length > 0 ? value : _render.SkyboxName });
            Field("Skybox image URL", _render.SkyboxUrl ?? "", Customization.Appearance.UrlLimit,
                value => _render = _render with { SkyboxUrl = value.Trim(), Skybox = value.Trim().Length > 0 || _render.Skybox });
            Label("Direct PNG/JPEG link, up to 8 MB and 2048 pixels per side. A 2:1 panoramic image wraps around the sky. Clear the URL to use the selected built-in skybox.", 22, 105f);
            if (_control.Rendering.ImageError != null)
            {
                Label(_control.Rendering.ImageError, 22, 70f);
            }

            Range("Skybox exposure", _render.Exposure, 0f, 5f, value => _render = _render with { Exposure = value });

            ColorField("Ambient color", _render.AmbientColor, value => _render = _render with { AmbientColor = value });
            Range("Ambient light", _render.AmbientIntensity, 0f, 5f, value => _render = _render with { AmbientIntensity = value });
            Range("Reflections", _render.ReflectionIntensity, 0f, 2f, value => _render = _render with { ReflectionIntensity = value });
            Range("Sun light", _render.SunIntensity, 0f, 5f, value => _render = _render with { SunIntensity = value });

            Label("Applies to all players in this room. Original map settings return when you leave.", 23, 72f);
            Action("Apply Rendering", () =>
                _control.Access.SetEnvironment(_control.Access.Environment with { Rendering = _render }, out _message));
            Action("Restore Map Defaults", () =>
            {
                _control.Access.SetEnvironment(_control.Access.Environment with { Rendering = null }, out _message);
                _render = _control.Rendering.Original;
                Page(false);
            });
        }

        private void Check(string title, bool value, Action<bool> changed)
        {
            RectTransform row = Line();
            _ui.Label(row, title, 26);
            RectTransform box = GameUi.Rect("Check", row);
            box.anchorMin = box.anchorMax = new Vector2(1f, 0.5f);
            box.pivot = new Vector2(1f, 0.5f);
            box.sizeDelta = Vector2.one * (40f * _ui.Unit);
            box.anchoredPosition = new Vector2(-10f * _ui.Unit, 0f);
            Image background = _ui.Image(box, Color.white);
            Toggle toggle = GameUi.Add<Toggle>(box.gameObject);
            ColorBlock colors = toggle.colors;
            colors.normalColor = colors.selectedColor = new Color(0f, 0f, 0f, 0.28f);
            colors.highlightedColor = new Color(0f, 0f, 0f, 0.48f);
            colors.pressedColor = new Color(0f, 0f, 0f, 0.65f);
            colors.disabledColor = new Color(0f, 0f, 0f, 0.12f);
            colors.fadeDuration = 0.1f;
            toggle.colors = colors;
            _ui.Frame(toggle);
            RectTransform check = GameUi.Rect("Mark", box);
            GameUi.Area(check, 0.24f, 0.24f, 0.76f, 0.76f);
            toggle.targetGraphic = background;
            toggle.graphic = _ui.Image(check, new Color(1f, 1f, 1f, 0.9f), false);
            toggle.SetIsOnWithoutNotify(value);
            toggle.interactable = _control.CanManage;
            toggle.onValueChanged.AddListener(DelegateSupport.ConvertDelegate<UnityAction<bool>>(changed));
        }

        private static string[] VectorText(Point3 point) => new[] { point.X, point.Y, point.Z }.Select(value => value.ToString("0.###", CultureInfo.InvariantCulture)).ToArray();

        private static bool ParseVector(string[] values, out Point3 point)
        {
            point = null;
            if (values is not { Length: 3 }) return false;

            float[] parsed = new float[3];
            for (int index = 0; index < 3; index++)
                if (!float.TryParse(values[index].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed[index]) || !float.IsFinite(parsed[index])) return false;
            point = new Point3(parsed[0], parsed[1], parsed[2]);
            return true;
        }

        private void VectorFields(string title, string[] values)
        {
            for (int index = 0; index < 3; index++)
            {
                int axis = index;
                Field(title + " " + "XYZ"[index], values[index], 16, value => values[axis] = value);
            }
        }

        private void World()
        {
            Label("Map transform and gravity", 30);
            VectorFields("Map scale", _worldScale);
            VectorFields("Map rotation", _worldRotation);
            VectorFields("Gravity", _worldGravity);
            Label("Scale: 0.1–10 per axis. Gravity affects props, ragdolls and players. Changes apply to everyone and reset when the room closes.", 22, 85f);
            Label("NPC navigation follows the original map: this game build has no runtime navigation builder. Large geometry changes can affect AI routes.", 22, 85f);
            Action("Apply World Settings", () =>
            {
                if (!ParseVector(_worldScale, out var scale) || !ParseVector(_worldRotation, out var rotation) || !ParseVector(_worldGravity, out var gravity))
                {
                    _message = "Enter finite X, Y and Z values.";
                    return;
                }

                WorldSettings world = new(scale, rotation, gravity, _control.Access.Environment.World?.Parts);
                _control.Access.SetEnvironment(_control.Access.Environment with { World = world }, out _message);
            });
            Action("Restore World Defaults", () =>
            {
                _control.Access.SetEnvironment(_control.Access.Environment with { World = null }, out _message);
                _worldScale = VectorText(WorldSettings.Default.Scale);
                _worldRotation = VectorText(WorldSettings.Default.Rotation);
                _worldGravity = VectorText(WorldSettings.Default.Gravity);
                Page(false);
            });

            Label("Aimed map part: " + _control.Rendering.Geometry.Name(_part), 27, 75f);
            Label("Aim at a static map object before opening Server Settings. Networked characters, UI and scripted network objects are excluded.", 22, 75f);
            if (_part == null) return;

            _partPosition ??= VectorText(_part.Position);
            _partScale ??= VectorText(_part.Scale);
            _partRotation ??= VectorText(Sandbox.SandboxWorld.Point(Sandbox.SandboxWorld.Quaternion(_part.Rotation).eulerAngles));
            VectorFields("Part position", _partPosition);
            VectorFields("Part size", _partScale);
            VectorFields("Part rotation", _partRotation);
            ColorField("Part color", _part.Color, value => _part = _part with { Color = value });
            Action("Visible: " + (_part.Visible ? "ON" : "OFF"), () => { _part = _part with { Visible = !_part.Visible }; Page(false); });
            Action("Collisions: " + (_part.Solid ? "ON" : "OFF"), () => { _part = _part with { Solid = !_part.Solid }; Page(false); });
            Action("Apply Map Part", () =>
            {
                if (!ParseVector(_partPosition, out var position) || !ParseVector(_partScale, out var size) || !ParseVector(_partRotation, out var angles))
                {
                    _message = "Enter finite values for the map part.";
                    return;
                }

                MapPart changed = _part with { Position = position, Scale = size, Rotation = Sandbox.SandboxWorld.Rotation(Quaternion.Euler(angles.X, angles.Y, angles.Z)) };
                WorldSettings world = _control.Access.Environment.World ?? WorldSettings.Default;
                MapPart[] parts = (world.Parts ?? Array.Empty<MapPart>()).Where(part => part.Path != changed.Path).Append(changed).ToArray();
                _control.Access.SetEnvironment(_control.Access.Environment with { World = world with { Parts = parts } }, out _message);

                if (_message == null)
                {
                    _part = changed;
                }
            });
            Action("Restore Map Part", () =>
            {
                WorldSettings world = _control.Access.Environment.World ?? WorldSettings.Default;
                _control.Access.SetEnvironment(_control.Access.Environment with { World = world with { Parts = world.Parts?.Where(part => part.Path != _part.Path).ToArray() } }, out _message);
            });
        }

        private void Players()
        {
            if (_control.Access.Players.Count == 0)
            {
                Label("No players connected.");
            }

            foreach (RoomPlayer player in _control.Access.Players)
            {
                RectTransform row = Line(160f);
                Text name = _ui.Label(row, player.Nickname + (player.Id == 0 ? " (Host)" : player.Trusted ? " (Admin)" : ""), 30);
                GameUi.Area(name.rectTransform, 0f, 0.62f, 0.6f, 1f);
                Text account = _ui.Label(row, $"Account: {player.Account} · ID: {player.Id}" + (player.Guest ? " · Guest" : ""), 22);
                GameUi.Area(account.rectTransform, 0f, 0.32f, 0.6f, 0.62f);
                if (player.Id == 0) continue;

                Button kick = _ui.Button(row, "KICK", () => _control.Kick(player.Id, player.Account, false, out _message));
                GameUi.Area(kick.GetComponent<RectTransform>(), 0.64f, 0.62f, 0.8f, 1f);
                Button ban = _ui.Button(row, "BAN", () => _control.Kick(player.Id, player.Account, true, out _message));
                GameUi.Area(ban.GetComponent<RectTransform>(), 0.82f, 0.62f, 1f, 1f);
                ban.interactable = !player.Guest;
                Button mute = _ui.Button(row, player.Muted ? "UNMUTE CHAT" : "MUTE CHAT",
                    () => _control.Access.SetMuted(player.Id, player.Account, !player.Muted, out _message));
                GameUi.Area(mute.GetComponent<RectTransform>(), 0.64f, 0.22f, 1f, 0.6f);
                if (_control.CanManage)
                {
                    Button trusted = _ui.Button(row, player.Trusted ? "Revoke Server Settings Access" : "Grant Server Settings Access",
                        () => _control.Access.SetTrusted(player.Id, player.Account, !player.Trusted, out _message));
                    GameUi.Area(trusted.GetComponent<RectTransform>(), 0f, 0f, 0.6f, 0.3f);
                }
            }
        }

        private void Accounts(bool muted)
        {
            Label(muted ? _control.Access.Mutes.LoadError ??
                "Chat mutes use account names and remain until removed. Nickname changes do not remove a mute." :
                _control.Bans.LoadError ?? "Bans use authenticated accounts and survive nickname changes. Guest accounts cannot be banned persistently.", 23, 90f);
            IReadOnlyList<BanEntry> entries = muted ? _control.Access.MutedAccounts : _control.Access.Bans;
            if (entries.Count == 0)
            {
                Label(muted ? "No muted accounts." : "No banned accounts.");
            }

            foreach (BanEntry entry in entries.ToArray())
            {
                RectTransform row = Line(110f);
                Text name = _ui.Label(row, muted ? entry.Nickname : entry.Account, 28);
                GameUi.Area(name.rectTransform, 0f, 0.5f, 0.7f, 1f);
                Text detail = _ui.Label(row, muted ? entry.Account : entry.Nickname + " · " + entry.CreatedAt.ToLocalTime().ToString("g"), 22);
                GameUi.Area(detail.rectTransform, 0f, 0f, 0.7f, 0.5f);
                Button remove = _ui.Button(row, muted ? "UNMUTE" : "UNBAN", () =>
                {
                    if (muted)
                    {
                        _control.Access.SetMuted(-1, entry.Account, false, out _message);
                    }
                    else
                    {
                        _control.Unban(entry.Account, out _message);
                    }
                });
                GameUi.Area(remove.GetComponent<RectTransform>(), 0.76f, 0.5f, 1f, 1f);
            }
        }
    }
}
