using HarmonyLib;
using Il2Cpp;
using Il2CppFishNet.Transporting.Relay;
using NullServers.Network;
using NullServers.UI;
using UnityEngine;

namespace NullServers.Room
{
    internal sealed class RoomCustomization
    {
        private static RoomCustomization _instance;
        private readonly RelaySession _session;
        private readonly RoomOptions _options;
        private readonly ColorPicker _picker;
        private LobbyMenu _listMenu;
        private RelayRoomInfo _drawingRoom;
        private bool _drawingMode;
        private int _nextRoom;
        private string _playerInput = "12";
        private string _colorInput = "#FFFFFF";
        private GUIStyle _labelStyle;
        private GUIStyle _inputStyle;
        private GUIStyle _previewStyle;
        private LobbyMenu _hostMenu;
        private Rect _settingsPanel;
        private Rect _settingsViewport;
        private Vector2 _settingsScroll;
        private float _settingsContentWidth;
        private bool _modeScrollOpen;
        private float _settingsBottom;
        private bool _compactSettings;
        private float _settingsSectionY;
        private float _settingsSectionHeight;
        private LobbyMenu _nameMenu;

        internal RoomCustomization(HarmonyLib.Harmony harmony, RelaySession session, RoomOptions options, ColorPicker picker)
        {
            _instance = this;

            _session = session;
            _options = options;
            _picker = picker;

            harmony.Patch(
                AccessTools.Method(typeof(LobbyMenu), nameof(LobbyMenu.AJKAOGDBDMH)),
                prefix: new HarmonyMethod(typeof(RoomCustomization), nameof(BeforeHostMenu)),
                postfix: new HarmonyMethod(typeof(RoomCustomization), nameof(AfterHostMenu)),
                finalizer: new HarmonyMethod(typeof(RoomCustomization), nameof(EndHostMenu)));

            harmony.Patch(
                AccessTools.Method(typeof(GUI), nameof(GUI.Box), [typeof(Rect), typeof(string)]),
                prefix: new HarmonyMethod(typeof(RoomCustomization), nameof(BeforeSettingsPanel)));

            harmony.Patch(
                AccessTools.Method(
                    typeof(GUI),
                    nameof(GUI.TextField),
                    [
                        typeof(Rect),
                        typeof(string),
                        typeof(int),
                        typeof(GUIStyle)
                    ]),
                prefix: new HarmonyMethod(typeof(RoomCustomization), nameof(BeforeHostName)),
                postfix: new HarmonyMethod(typeof(RoomCustomization), nameof(AfterHostName)),
                finalizer: new HarmonyMethod(typeof(RoomCustomization), nameof(EndHostName)));

            harmony.Patch(
                AccessTools.Method(typeof(GUI), nameof(GUI.BeginScrollView), [ typeof(Rect), typeof(Vector2), typeof(Rect) ]),
                prefix: new HarmonyMethod(typeof(RoomCustomization), nameof(BeforeModeScrollView)));

            harmony.Patch(
                AccessTools.Method(typeof(GUI), nameof(GUI.EndScrollView), Type.EmptyTypes),
                postfix: new HarmonyMethod(typeof(RoomCustomization), nameof(AfterModeScrollView)));

            foreach (System.Reflection.MethodInfo method in new[]
            {
                AccessTools.Method(typeof(GUI), nameof(GUI.Button), [ typeof(Rect), typeof(GUIContent), typeof(GUIStyle) ]),
                AccessTools.Method(typeof(GUI), nameof(GUI.Toggle), [ typeof(Rect), typeof(bool), typeof(string) ]),
                AccessTools.Method(
                    typeof(GUI),
                    nameof(GUI.HorizontalSlider),
                    [
                        typeof(Rect),
                        typeof(float),
                        typeof(float),
                        typeof(float)
                    ])
            })
            {
                harmony.Patch(
                    method,
                    prefix: new HarmonyMethod(typeof(RoomCustomization), nameof(BeforeSettingsControl)),
                    finalizer: new HarmonyMethod(typeof(RoomCustomization), nameof(AfterSettingsControl)));
            }

            harmony.Patch(
                AccessTools.Method(typeof(LobbyMenu), nameof(LobbyMenu.BAGGECEDBML)),
                prefix: new HarmonyMethod(typeof(RoomCustomization), nameof(BeforeRoomBrowser)),
                finalizer: new HarmonyMethod(typeof(RoomCustomization), nameof(AfterRoomBrowser)));

            harmony.Patch(
                AccessTools.Method(typeof(GUI), nameof(GUI.Label), [ typeof(Rect), typeof(string), typeof(GUIStyle) ]),
                prefix: new HarmonyMethod(typeof(RoomCustomization), nameof(BeforeLabel)),
                finalizer: new HarmonyMethod(typeof(RoomCustomization), nameof(AfterSettingsControl)));
        }

        private void DrawOptions(LobbyMenu menu)
        {
            if (_settingsPanel.width <= 0f || !_session.IsNullFrame || menu.IsCreatingRoom)
            {
                return;
            }

            int fontSize = Mathf.Max(13, Screen.width / 70);
            if (_labelStyle == null)
            {
                _labelStyle = MenuUi.TextStyle(menu.Skin.customStyles[0]);
                _inputStyle = MenuUi.TextStyle(GUI.skin.textField, alignment: TextAnchor.MiddleCenter);
                _previewStyle = MenuUi.TextStyle(_labelStyle, richText: true, wordWrap: true);
            }

            _labelStyle.fontSize = _inputStyle.fontSize = _previewStyle.fontSize = fontSize;

            float row = fontSize * 1.8f;
            float padding = fontSize * 0.7f;
            float width = _settingsContentWidth;
            float y = (_compactSettings ? _settingsBottom : _settingsPanel.yMax) - _settingsViewport.y + padding;
            var sections = GameSettingsExtensions.Measure(menu, width, row, padding);
            bool inserted = _settingsSectionY >= 0f;
            float contentHeight = y + (inserted ? 0f : sections.Sum(section => section.Height)) + row * 6.5f;
            Color oldColor = GUI.color;
            bool oldEnabled = GUI.enabled;

            GUI.color = Color.white;
            GUI.enabled = oldEnabled && !_session.IsSwitching;

            _settingsScroll.y = Mathf.Clamp(_settingsScroll.y, 0f, Mathf.Max(0f, contentHeight - _settingsViewport.height));
            _settingsScroll = GUI.BeginScrollView(
                _settingsViewport,
                _settingsScroll,
                new Rect(0f, 0f, width, contentHeight),
                false,
                true,
                GUI.skin.horizontalScrollbar,
                GUI.skin.verticalScrollbar,
                GUIStyle.none);

            try
            {
                if (menu.IsModeListOpen) return;

                float sectionY = inserted ? _settingsSectionY - _settingsViewport.y : y;

                foreach (var section in sections)
                {
                    section.Draw(menu, new Rect(0f, sectionY, width, section.Height), row, padding);
                    sectionY += section.Height;
                }

                if (!inserted)
                {
                    y = sectionY;
                }

                float innerWidth = width - padding * 2f;
                GUI.Label(new Rect(padding, y, innerWidth * 0.58f, row), "Max Players", _labelStyle);
                GUI.SetNextControlName("Players");

                string input = GUI.TextField(new Rect(width * 0.68f, y, width * 0.25f, row), _playerInput, 2, _inputStyle);
                if (input.Length == 0)
                {
                    _playerInput = input;
                }
                else if (int.TryParse(input, out var players))
                {
                    _options.MaxPlayers = Math.Clamp(players, 1, RoomOptions.PlayerLimit);
                    _playerInput = _options.MaxPlayers.ToString();
                }

                if (GUI.GetNameOfFocusedControl() != "Players")
                {
                    _playerInput = _options.MaxPlayers.ToString();
                }

                y += row * 1.2f;
                int selected = Mathf.RoundToInt(GUI.HorizontalSlider(
                    new Rect(padding, y, innerWidth, row),
                    _options.MaxPlayers,
                    1f,
                    RoomOptions.PlayerLimit));
                if (selected != _options.MaxPlayers)
                {
                    _options.MaxPlayers = selected;
                    _playerInput = selected.ToString();
                }

                y += row * 1.1f;
                GUI.Label(new Rect(padding, y, innerWidth * 0.55f, row), "Room Name Color", _labelStyle);
                GUI.SetNextControlName("NameColor");

                _colorInput = GUI.TextField(
                    new Rect(width * 0.56f, y, width * 0.37f - row - padding, row),
                    _colorInput,
                    7,
                    _inputStyle);
                if (RoomOptions.IsValidColor(_colorInput))
                {
                    _options.NameColor = _colorInput.ToUpperInvariant();
                }
                else if (GUI.GetNameOfFocusedControl() != "NameColor")
                {
                    _colorInput = _options.NameColor;
                }

                Rect swatch = new(width * 0.93f - row, y, row, row);
                if (ColorPicker.Swatch(swatch, _options.NameColor))
                {
                    Rect anchor = new(
                        _settingsViewport.x + swatch.x,
                        _settingsViewport.y + swatch.y - _settingsScroll.y,
                        row,
                        row);

                    Action<string> updateColor = color => _colorInput = _options.NameColor = color;
                    RoomName.TryParse(_options.DisplayName ?? menu.RoomName, out _, out var sample, out _);
                    _picker.Open(anchor, _options.NameColor, updateColor, updateColor, sample ?? "Room Name");
                }

                y += row * 1.2f;
                if (RoomName.TryParse(_options.DisplayName ?? menu.RoomName, out _, out var display, out var error))
                {
                    GUI.Label(
                        new Rect(padding, y, innerWidth, row * 1.5f),
                        $"<color={_options.NameColor}>{display}</color>  1/{_options.MaxPlayers}",
                        _previewStyle);
                }
                else
                {
                    GUI.Label(new Rect(padding, y, innerWidth, row * 1.5f), error, _labelStyle);
                }

                GUI.Label(
                    new Rect(padding, y + row * 1.5f, innerWidth, row * 1.5f),
                    "Partial color: <color=#FF5555>Red</color> White",
                    _labelStyle);
            }
            finally
            {
                GUI.EndScrollView();
                GUI.color = oldColor;
                GUI.enabled = oldEnabled;
            }
        }

        internal static bool OwnsSettings(LobbyMenu menu) =>
            _instance != null && menu && _instance._session.IsNullFrame &&
            !menu.BAJDIMCNBDM && _instance._session.Relay &&
            !_instance._session.Relay.OfflineMode && !menu.IsCreatingRoom;

        private static void BeforeHostMenu(LobbyMenu __instance)
        {
            RoomCustomization instance = _instance;
            instance._hostMenu = OwnsSettings(__instance) ? __instance : null;

            instance._settingsPanel = default;

            if (instance._hostMenu && instance._nameMenu != __instance)
            {
                instance._nameMenu = __instance;
                instance._options.DisplayName = __instance.RoomName;
            }
        }

        private static void BeforeHostName(
            Rect __0,
            ref string __1,
            ref int __2,
            GUIStyle __3,
            out (bool Active, bool RichText) __state)
        {
            __state = (_instance._hostMenu && __0.x < Screen.width * 0.5f && __0.y < Screen.height * 0.3f, __3.richText);
            if (!__state.Active) return;

            __1 = _instance._options.DisplayName ?? __1;
            __2 = RoomName.MaxDisplayBytes;
            __3.richText = false;
        }

        private static void AfterHostName(ref string __result, (bool Active, bool RichText) __state)
        {
            if (!__state.Active) return;

            _instance._options.DisplayName = __result;
            __result = RoomName.TryParse(__result, out var plain, out _, out _) ? plain : _instance._hostMenu.RoomName;
        }

        private static void EndHostName(GUIStyle __3, (bool Active, bool RichText) __state)
        {
            if (__state.Active)
            {
                __3.richText = __state.RichText;
            }
        }

        private static void BeforeSettingsPanel(Rect __0)
        {
            RoomCustomization instance = _instance;
            if (!instance._hostMenu || __0.x < Screen.width * 0.5f || __0.height < Screen.height * 0.5f)
            {
                return;
            }

            instance._settingsPanel = __0;

            float padding = Mathf.Max(6f, Screen.width * 0.006f);
            float header = Mathf.Max(24f, Screen.height * 0.05f);
            instance._settingsViewport = new Rect(
                __0.x + padding,
                __0.y + header,
                __0.width - padding * 2f,
                __0.height - header - padding);

            instance._settingsContentWidth = instance._settingsViewport.width - Mathf.Max(16f, GUI.skin.verticalScrollbar.fixedWidth) - 4f;
            instance._settingsBottom = instance._settingsViewport.y;
            instance._settingsSectionY = -1f;
            float fontSize = Mathf.Max(13, Screen.width / 70);
            instance._settingsSectionHeight = GameSettingsExtensions.Measure(instance._hostMenu,
                instance._settingsContentWidth, fontSize * 1.8f, fontSize * 0.7f).Sum(section => section.Height);
            instance._compactSettings = instance._settingsSectionHeight > 0f;
        }

        private static void BeforeSettingsControl(ref Rect __0, out bool __state, System.Reflection.MethodBase __originalMethod = null)
        {
            RoomCustomization instance = _instance;

            __state = instance._hostMenu &&
                !instance._modeScrollOpen &&
                instance._settingsPanel.width > 0f &&
                __0.x >= instance._settingsPanel.x - 1f &&
                __0.xMax <= instance._settingsPanel.xMax + 1f &&
                __0.y >= instance._settingsViewport.y &&
                __0.yMax <= instance._settingsPanel.yMax + 1f;

            if (!__state) return;

            if (instance._compactSettings && !instance._hostMenu.IsModeListOpen)
            {
                if (__originalMethod?.Name == nameof(GUI.Toggle))
                {
                    instance._settingsSectionY = instance._settingsBottom + Mathf.Max(8f, Screen.width * 0.01f);
                    __0.y = instance._settingsSectionY + instance._settingsSectionHeight;
                }

                instance._settingsBottom = Mathf.Max(instance._settingsBottom, __0.yMax);
            }

            Rect clip = instance._settingsViewport;
            clip.width = instance._settingsContentWidth;
            GUI.BeginGroup(clip);

            __0.position -= instance._settingsViewport.position + instance._settingsScroll;
        }

        private static void AfterSettingsControl(bool __state)
        {
            if (__state)
            {
                GUI.EndGroup();
            }
        }

        private static void BeforeModeScrollView(ref Rect __0, ref Rect __2)
        {
            RoomCustomization instance = _instance;
            if (!instance._hostMenu ||
                !instance._hostMenu.IsModeListOpen ||
                instance._settingsPanel.width <= 0f ||
                __0.x < instance._settingsPanel.x ||
                __0.x >= instance._settingsPanel.xMax)
            {
                return;
            }

            Rect clip = instance._settingsViewport;
            clip.width = instance._settingsContentWidth;
            GUI.BeginGroup(clip);

            __0.position -= clip.position + instance._settingsScroll;
            __0.width = Mathf.Min(__0.width, clip.width - __0.x);
            __0.height = Mathf.Max(1f, Mathf.Min(__0.height, clip.height - __0.y));
            __2.width = __0.width - Mathf.Max(16f, GUI.skin.verticalScrollbar.fixedWidth);

            instance._modeScrollOpen = true;
        }

        private static void AfterModeScrollView()
        {
            if (!_instance._modeScrollOpen) return;

            _instance._modeScrollOpen = false;
            GUI.EndGroup();
        }

        private static void AfterHostMenu(LobbyMenu __instance)
        {
            _instance._hostMenu = null;
            _instance.DrawOptions(__instance);
        }

        private static void EndHostMenu()
        {
            _instance._hostMenu = null;
        }

        private static void BeforeRoomBrowser(LobbyMenu __instance)
        {
            _instance._listMenu = _instance._session.IsNullFrame && !__instance.BAJDIMCNBDM ? __instance : null;
            _instance._nextRoom = 0;
            _instance._drawingRoom = null;
            _instance._drawingMode = false;
        }

        private static void AfterRoomBrowser()
        {
            _instance._listMenu = null;
            _instance._drawingRoom = null;
            _instance._drawingMode = false;
        }

        private static void BeforeLabel(ref Rect __0, ref string __1, out bool __state)
        {
            BeforeSettingsControl(ref __0, out __state);

            RoomCustomization instance = _instance;
            if (!instance._listMenu || !instance._session.IsNullFrame || string.IsNullOrEmpty(__1))
            {
                return;
            }

            var rooms = instance._listMenu.Rooms;
            if (rooms == null) return;

            if (__0.x == 0f && __1.StartsWith("<size=", StringComparison.Ordinal))
            {
                while (instance._nextRoom < rooms.Count && rooms[instance._nextRoom] == null)
                {
                    instance._nextRoom++;
                }

                if (instance._nextRoom >= rooms.Count) return;

                RelayRoomInfo room = rooms[instance._nextRoom];

                if (!RoomLabels.TryFormatName(
                    __1,
                    room.Name,
                    instance._session.GetRoomNameColor(room.Id),
                    out var formatted,
                    RoomName.ReadDisplay(room.RoomData, room.Name)))
                {
                    return;
                }

                instance._nextRoom++;
                instance._drawingRoom = room;
                instance._drawingMode = true;
                __1 = formatted;

                return;
            }

            if (instance._drawingRoom == null) return;

            if (instance._drawingMode && __0.x == __0.width && __1.StartsWith("<size=", StringComparison.Ordinal))
            {
                __1 = RoomLabels.FormatGameMode(__1, instance._drawingRoom.RoomData);
                instance._drawingMode = false;
                return;
            }

            string countLabel = RoomLabels.FormatPlayerCount(
                __1,
                instance._drawingRoom.PlayerCount,
                instance._drawingRoom.MaxPlayers);

            if (countLabel != __1)
            {
                __1 = countLabel;
                instance._drawingRoom = null;
                instance._drawingMode = false;
            }
        }
    }
}
