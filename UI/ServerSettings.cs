using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using NullServers.Network;
using NullServers.Room;
using NullServers.Server;
using UnityEngine;

namespace NullServers.UI
{
    internal sealed class ServerSettings
    {
        private readonly ServerControl _control;
        private readonly ColorPicker _picker;
        private RoomMultiplayerMenu _menu;
        private readonly PauseMenu _pause;
        private bool _open;
        private string _roomId;
        private string _name;
        private string _capacity;
        private string _color;
        private string _scene;
        private string _mode;
        private string _minutes;
        private string _custards;
        private int _difficulty;
        private bool _muteGuests;
        private bool _mapExpanded;
        private bool _modeExpanded;
        private int _tab;
        private Vector2 _scroll;
        private string _message;
        private GUIStyle _label;
        private GUIStyle _rich;
        private GUIStyle _input;
        private GUIStyle _button;
        private GUIStyle _small;
        private GUIStyle _title;
        private Rect _window;
        private Rect _viewport;
        private float _row;
        private float _width;

        internal ServerSettings(ServerControl control, ColorPicker picker, PauseMenu pause)
        {
            _control = control;
            _picker = picker;
            _pause = pause;

            pause.Add("Server Settings", Open, () => _control.Access.CanOpen);
        }

        internal void Update()
        {
            RoomMultiplayerMenu menu = RoomMultiplayerMenu.GJDGDIGECJN;
            if 
            (
                _open &&
                (!_control.Access.CanOpen ||
                !menu ||
                menu != _menu ||
                !menu.IOMIAHNBDOG ||
                _control.Room.Id != _roomId)
            )
            {
                Close();
            }
        }

        private void Open()
        {
            if (!_control.Access.CanOpen || !_pause.Hide(this)) return;

            _menu = _pause.Menu;

            Il2CppFishNet.Transporting.Relay.RelayRoomInfo room = _control.Room;
            _roomId = room.Id;
            _name = RoomName.ReadDisplay(room.RoomData, room.Name);
            _capacity = room.MaxPlayers.ToString();
            _color = RoomOptions.ReadNameColor(room.RoomData) ?? "#FFFFFF";

            RoomProperties properties = RoomProperties.HFBENCJFGPO(room.RoomData);
            _scene = properties.MapName;
            _mode = properties.GameMode;
            _minutes = Math.Max(1, properties.RoundDuration / 60).ToString();
            _custards = properties.Custards.ToString();
            _difficulty = properties.Difficulty;
            _muteGuests = properties.MuteGuests;

            _message = null;
            _mapExpanded = _modeExpanded = false;
            _scroll = default;
            _open = true;
        }

        private void Close()
        {
            _open = false;
            _picker.Close();
            _pause.Restore(this);
        }

        internal void Draw()
        {
            if (!_open || !_menu || !_control.Access.CanOpen) return;

            GUISkin oldSkin = GUI.skin;
            Color oldColor = GUI.color;
            Color oldContentColor = GUI.contentColor;
            Color oldBackgroundColor = GUI.backgroundColor;
            bool oldEnabled = GUI.enabled;
            int oldDepth = GUI.depth;

            GUI.depth = -100;
            GUI.skin = _menu.BIPMFIBNFBC;
            GUI.color = Color.white;
            GUI.contentColor = GUI.backgroundColor = Color.white;

            if (_label == null)
            {
                _label = new GUIStyle(GUI.skin.label)
                {
                    font = GUI.skin.font,
                    richText = false,
                    wordWrap = true,
                    alignment = TextAnchor.MiddleLeft
                };

                _rich = new GUIStyle(_label)
                {
                    richText = true
                };

                _small = new GUIStyle(_label);
                _title = new GUIStyle(_label);

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
                    padding = new RectOffset(12, 12, 4, 4)
                };
                _button.normal.background = ButtonBackground(new Color(0.09f, 0.1f, 0.12f), new Color(0.27f, 0.29f, 0.33f));
                _button.hover.background = ButtonBackground(new Color(0.24f, 0.1f, 0.11f), new Color(0.75f, 0.25f, 0.28f));
                _button.active.background = ButtonBackground(new Color(0.4f, 0.09f, 0.11f), new Color(0.9f, 0.3f, 0.33f));
                _button.onNormal.background = _button.hover.background;
                _button.onHover.background = _button.active.background;
                _button.onActive.background = _button.active.background;
                _button.focused.background = _button.hover.background;
                _button.onFocused.background = _button.active.background;

                foreach (GUIStyleState state in new[]
                {
                    _button.normal,
                    _button.hover,
                    _button.active,
                    _button.focused,
                    _button.onNormal,
                    _button.onHover,
                    _button.onActive,
                    _button.onFocused
                })
                {
                    state.textColor = Color.white;
                }

                _label.normal.textColor = new Color(0.8f, 0.81f, 0.84f);
                _small.normal.textColor = new Color(0.62f, 0.65f, 0.7f);
                _title.normal.textColor = Color.white;
            }

            int fontSize = Mathf.Clamp(Screen.height / 38, 16, 30);
            _label.fontSize = _rich.fontSize = _input.fontSize = _button.fontSize = fontSize;
            _small.fontSize = Mathf.Max(13, fontSize - 3);
            _title.fontSize = fontSize + 5;
            _row = fontSize * 1.65f;

            float width = Mathf.Min(Screen.width * 0.78f, 1200f);
            _window = new Rect((Screen.width - width) / 2f, Screen.height * 0.09f, width, Screen.height * 0.82f);
            _picker.HandleInput();

            if (_picker.IsOpen && 
                Event.current.type != EventType.Repaint && 
                Event.current.type != EventType.Layout)
            {
                GUI.enabled = false;
            }

            GUI.color = new Color(0f, 0f, 0f, 0.97f);
            GUI.DrawTexture(_window, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.BeginGroup(_window);

            try
            {
                GUI.Label(new Rect(20f, 10f, width - 140f, _row), "Server Settings", _title);
                if (GUI.Button(new Rect(width - 110f, 10f, 90f, _row), "Close", _button))
                {
                    Close();
                }

                string[] tabs =
                [
                    "Room",
                    "Round",
                    "Players",
                    "Bans",
                    "Chat"
                ];
                float tabWidth = (width - 40f) / tabs.Length;

                for (int index = 0; index < tabs.Length; index++)
                {
                    if (GUI.Toggle
                    (
                        new Rect(20f + index * tabWidth, _row + 18f, tabWidth - 5f, _row),
                        _tab == index,
                        tabs[index],
                        _button
                    ) && _tab != index)
                    {
                        _tab = index;
                        _scroll = default;
                        _message = null;
                    }
                }

                _viewport = new Rect(20f, _row * 2f + 30f, width - 40f, _window.height - _row * 4f - 50f);
                _width = _viewport.width - 24f;

                float contentRows = _tab switch
                {
                    0 => 14f,
                    1 => 13 + (_mapExpanded ? _control.Maps.Count : 0) + (_modeExpanded ? _control.Modes.Count : 0),
                    2 => 2f + _control.Access.Players.Count * 3.1f,
                    3 => 3 + _control.Access.Bans.Count * 2,
                    _ => 3 + _control.Access.MutedAccounts.Count * 2
                };

                _scroll = GUI.BeginScrollView(_viewport, _scroll, new Rect(0f, 0f, _width, contentRows * _row));

                try
                {
                    GUI.enabled = GUI.enabled && !_control.Busy;

                    switch (_tab)
                    {
                        case 0:
                            DrawRoom();
                            break;
                        case 1:
                            DrawRound();
                            break;
                        case 2:
                            DrawPlayers();
                            break;
                        case 3:
                            DrawBans();
                            break;
                        case 4:
                            DrawMutes();
                            break;
                    }
                }
                finally
                {
                    GUI.EndScrollView();
                }

                GUI.enabled = oldEnabled;
                GUI.Label
                (
                    new Rect(20f, _window.height - _row * 1.8f, width - 40f, _row * 1.6f),
                    _message ?? _control.Access.Status ?? _control.Status ?? "Room management",
                    _small
                );
            }
            finally
            {
                GUI.EndGroup();
                GUI.enabled = oldEnabled;

                if (_open)
                {
                    _picker.Draw(_menu.BIPMFIBNFBC);
                }

                GUI.skin = oldSkin;
                GUI.color = oldColor;
                GUI.contentColor = oldContentColor;
                GUI.backgroundColor = oldBackgroundColor;
                GUI.depth = oldDepth;
            }
        }

        private void DrawRoom()
        {
            GUI.Label(Line(0f), "Room Name", _label);

            _name = GUI.TextField(Line(1f), _name, RoomName.MaxDisplayBytes, _input);

            bool valid = RoomName.TryParse(_name, out _, out var display, out var error);
            string previewColor = RoomOptions.IsValidColor(_color) ? _color : "#FFFFFF";
            GUI.Label
            (
                new Rect(0f, _row * 2.2f, _width, _row * 1.5f),
                valid ? $"<color={previewColor}>{display}</color>" : error,
                valid ? _rich : _small
            );

            GUI.Label(new Rect(0f, _row * 3.6f, _width, _row), "Partial color: <color=#FF5555>Red</color> White", _small);

            GUI.Label(new Rect(0f, _row * 4.8f, _width * 0.6f, _row), "Max Players (up to 50)", _label);
            _capacity = GUI.TextField(new Rect(_width * 0.7f, _row * 4.8f, _width * 0.3f, _row), _capacity, 2, _input);

            GUI.Label(new Rect(0f, _row * 6f, _width * 0.5f, _row), "Default Name Color", _label);
            _color = GUI.TextField(new Rect(_width * 0.52f, _row * 6f, _width * 0.48f - _row - 8f, _row), _color, 7, _input);

            Rect swatch = new(_width - _row, _row * 6f, _row, _row);
            if (ColorPicker.Swatch(swatch, previewColor))
            {
                Action<string> updateColor = color => _color = color;
                _picker.Open
                (
                    new Rect
                    (
                        _window.x + _viewport.x + swatch.x,
                        _window.y + _viewport.y + swatch.y - _scroll.y,
                        _row,
                        _row
                    ),
                    previewColor,
                    updateColor,
                    updateColor,
                    valid ? display : "Room Name"
                );
            }

            if (GUI.Button(new Rect(0f, _row * 7.5f, _width, _row * 1.2f), "Apply Room Settings", _button))
            {
                if (!int.TryParse(_capacity, out var capacity))
                {
                    _message = "Enter a valid player limit.";
                }
                else
                {
                    _control.ApplyRoom(_name, capacity, _color, out var message);
                    _message = message;
                }
            }

            if (!RoomProperties.HFBENCJFGPO(_control.Room.RoomData).IsOpen &&
                GUI.Button(new Rect(0f, _row * 9f, _width, _row), "Reopen Room", _button))
            {
                _control.Reopen(out var message);
                _message = message;
            }

            if (_control.CanManage && GUI.Button(new Rect(0f, _row * 10.5f, _width, _row), "Spawn Sandbox Console", _button))
            {
                _message = _control.Tools.SpawnConsole(out var message) ? "Sandbox console enabled. Use the game's console key to open it." : message;
            }

            if (_control.CanManage)
            {
                GUI.Label
                (
                    new Rect(0f, _row * 12f, _width, _row * 1.5f),
                    $"Anti-Crash: ACTIVE · Blocked: {_control.Protection.Blocked} · Removed: {_control.Protection.Removed}",
                    _small
                );
            }
        }

        private void DrawRound()
        {
            float y = 0f;
            GUI.Label(new Rect(0f, y, _width * 0.28f, _row), "Map", _label);

            if (GUI.Button(new Rect(_width * 0.3f, y, _width * 0.7f, _row), (_scene ?? "Choose map") + "  [v]", _button))
            {
                _mapExpanded = !_mapExpanded;
            }

            y += _row * 1.2f;
            if (_mapExpanded)
            {
                foreach (string scene in _control.Maps)
                {
                    if (GUI.Button(new Rect(_width * 0.3f, y, _width * 0.7f, _row), scene, _button))
                    {
                        _scene = scene;
                        _mapExpanded = false;
                    }

                    y += _row;
                }
            }

            GUI.Label(new Rect(0f, y, _width * 0.28f, _row), "Game Mode", _label);
            if
            (
                GUI.Button
                (
                    new Rect(_width * 0.3f, y, _width * 0.7f, _row),
                    (_control.Modes.GetValueOrDefault(_mode ?? "") ?? _mode ?? "Choose mode") + "  [v]",
                    _button
                )
            )
            {
                _modeExpanded = !_modeExpanded;
            }

            y += _row * 1.2f;
            if (_modeExpanded)
            {
                foreach (var mode in _control.Modes)
                {
                    if (GUI.Button(new Rect(_width * 0.3f, y, _width * 0.7f, _row), mode.Value, _button))
                    {
                        _mode = mode.Key;
                        _modeExpanded = false;
                    }

                    y += _row;
                }
            }

            _minutes = NumberField("Timer (minutes, 1–60)", _minutes, y);
            y += _row * 1.2f;
            _custards = NumberField("Custards (1–200)", _custards, y);
            y += _row * 1.2f;

            GUI.Label(new Rect(0f, y, _width * 0.55f, _row), "Difficulty", _label);

            string[] difficulty =
            [
                "Easy",
                "Normal",
                "Hard",
                "Impossible"
            ];
            if
            (
                GUI.Button
                (
                    new Rect(_width * 0.56f, y, _width * 0.44f, _row),
                    difficulty[Math.Clamp(_difficulty, 0, 3)],
                    _button
                )
            )
            {
                _difficulty = (_difficulty + 1) % difficulty.Length;
            }

            y += _row * 1.2f;
            GUI.Label(new Rect(0f, y, _width * 0.55f, _row), "Mute Guests", _label);
            if (GUI.Button(new Rect(_width * 0.56f, y, _width * 0.44f, _row), _muteGuests ? "ON" : "OFF", _button))
            {
                _muteGuests = !_muteGuests;
            }

            y += _row * 1.4f;
            GUI.Label
            (
                new Rect(0f, y, _width, _row * 1.5f),
                "Starts a fresh round for everyone. Scores, teams and map objects reset.",
                _small
            );
            y += _row * 1.6f;

            if (GUI.Button(new Rect(0f, y, _width, _row * 1.2f), "Start New Round", _button))
            {
                if (!int.TryParse(_minutes, out var minutes) || !int.TryParse(_custards, out var custards))
                {
                    _message = "Enter valid round settings.";
                }
                else
                {
                    _control.RestartRound(_scene, _mode, minutes, custards, _difficulty, _muteGuests, out var message);
                    _message = message;

                    if (message == null) Close();
                }
            }
        }

        private void DrawPlayers()
        {
            float y = 0f;

            foreach (RoomPlayer player in _control.Access.Players)
            {
                int id = player.Id;
                GUI.Label
                (
                    new Rect(0f, y, _width * 0.6f, _row),
                    player.Nickname + (id == 0 ? " (Host)" : player.Trusted ? " (Admin)" : ""),
                    _label
                );
                GUI.Label
                (
                    new Rect(0f, y + _row, _width * 0.6f, _row),
                    $"Account: {player.Account} · ID: {id}" + (player.Guest ? " · Guest" : ""),
                    _small
                );

                if (id != 0)
                {
                    if (ActionButton(new Rect(_width * 0.64f, y, _width * 0.16f, _row), "KICK", new Color(1f, 0.65f, 0.2f)))
                    {
                        _control.Kick(id, player.Account, false, out var message);
                        _message = message;
                    }

                    bool enabled = GUI.enabled;
                    GUI.enabled = enabled && !player.Guest;

                    if (ActionButton(new Rect(_width * 0.82f, y, _width * 0.18f, _row), "BAN", new Color(1f, 0.3f, 0.3f)))
                    {
                        _control.Kick(id, player.Account, true, out var message);
                        _message = message;
                    }

                    GUI.enabled = enabled;
                    if (GUI.Button
                    (
                        new Rect(_width * 0.64f, y + _row * 1.1f, _width * 0.36f, _row),
                        player.Muted ? "UNMUTE CHAT" : "MUTE CHAT",
                        _button
                    ))
                    {
                        _control.Access.SetMuted(id, player.Account, !player.Muted, out var message);
                        _message = message;
                    }

                    if 
                    (
                        _control.CanManage &&
                        GUI.Button
                        (
                            new Rect(0f, y + _row * 2f, _width * 0.6f, _row * 0.85f),
                            player.Trusted ? "Revoke Server Settings Access" : "Grant Server Settings Access",
                            _button
                        )
                    )
                    {
                        _control.Access.SetTrusted(id, player.Account, !player.Trusted, out var message);
                        _message = message;
                    }
                }

                y += _row * 3.1f;
            }
        }

        private void DrawMutes()
        {
            GUI.Label
            (
                new Rect(0f, 0f, _width, _row * 2f),
                _control.Access.Mutes.LoadError ??
                "Chat mutes use account names and remain until removed. Nickname changes do not remove a mute.",
                _small
            );

            float y = _row * 2.2f;

            foreach (BanEntry entry in _control.Access.MutedAccounts.ToArray())
            {
                GUI.Label(new Rect(0f, y, _width * 0.72f, _row), entry.Nickname, _label);
                GUI.Label(new Rect(0f, y + _row, _width * 0.72f, _row), entry.Account, _small);

                if (GUI.Button(new Rect(_width * 0.76f, y, _width * 0.24f, _row), "UNMUTE", _button))
                {
                    _control.Access.SetMuted(-1, entry.Account, false, out var message);
                    _message = message;
                }

                y += _row * 2f;
            }
        }

        private void DrawBans()
        {
            GUI.Label
            (
                new Rect(0f, 0f, _width, _row * 2f),
                _control.Bans.LoadError ??
                "Bans use authenticated accounts and survive nickname changes. Guest accounts cannot be banned persistently.",
                _small
            );

            float y = _row * 2.2f;

            foreach (BanEntry entry in _control.Access.Bans.ToArray())
            {
                GUI.Label(new Rect(0f, y, _width * 0.72f, _row), entry.Account, _label);
                GUI.Label
                (
                    new Rect(0f, y + _row, _width * 0.72f, _row),
                    entry.Nickname + " · " + entry.CreatedAt.ToLocalTime().ToString("g"),
                    _small
                );

                if (GUI.Button(new Rect(_width * 0.76f, y, _width * 0.24f, _row), "UNBAN", _button))
                {
                    _control.Unban(entry.Account, out var message);
                    _message = message;
                }

                y += _row * 2f;
            }
        }

        private Rect Line(float index) => new(0f, _row * index, _width, _row);

        internal static Texture2D ButtonBackground(Color fill, Color border)
        {
            Texture2D texture = new(3, 3, TextureFormat.RGBA32, false)

            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };

            Color[] pixels = Enumerable.Repeat(border, 9).ToArray();
            pixels[4] = fill;

            texture.SetPixels(new Il2CppStructArray<Color>(pixels));
            texture.Apply();

            return texture;
        }

        private string NumberField(string label, string value, float y)
        {
            GUI.Label(new Rect(0f, y, _width * 0.55f, _row), label, _label);

            return GUI.TextField(new Rect(_width * 0.56f, y, _width * 0.44f, _row), value, 3, _input);
        }

        private bool ActionButton(Rect rect, string text, Color color)
        {
            Color previous = GUI.contentColor;
            GUI.contentColor = color;

            bool clicked = GUI.Button(rect, text, _button);

            GUI.contentColor = previous;

            return clicked;
        }
    }
}
