using HarmonyLib;
using Il2Cpp;
using NullServers.Network;
using NullServers.Players;
using UnityEngine;

namespace NullServers.UI
{
    internal sealed class ProfileMenu
    {
        private static ProfileMenu _instance;
        private readonly RelaySession _session;
        private readonly PlayerProfiles _profiles;
        private readonly ColorPicker _picker;
        private bool _drawingBrowser;
        private Rect _roomField;
        private Rect _nicknameField;
        private string _nickname;
        private bool _editing;
        private GUIStyle _label;
        private GUIStyle _input;

        internal ProfileMenu(HarmonyLib.Harmony harmony, RelaySession session, PlayerProfiles profiles, ColorPicker picker)
        {
            _instance = this;
            _session = session;
            _profiles = profiles;
            _picker = picker;
            _nickname = profiles.Local.Nickname;

            profiles.LocalChanged += profile =>
            {
                _nickname = profile.Nickname;
                _editing = false;
            };

            harmony.Patch(
                AccessTools.Method(typeof(LobbyMenu), nameof(LobbyMenu.BAGGECEDBML)),
                prefix: new HarmonyMethod(typeof(ProfileMenu), nameof(BeforeBrowser)),
                postfix: new HarmonyMethod(typeof(ProfileMenu), nameof(AfterBrowser)),
                finalizer: new HarmonyMethod(typeof(ProfileMenu), nameof(EndBrowser)));
            harmony.Patch(
                AccessTools.Method(typeof(GUI), nameof(GUI.Box), [typeof(Rect), typeof(string)]),
                prefix: new HarmonyMethod(typeof(ProfileMenu), nameof(BeforeBrowserBox)));
            harmony.Patch(
                AccessTools.Method(typeof(LobbyMenu), nameof(LobbyMenu.OnGUI)),
                prefix: new HarmonyMethod(typeof(ProfileMenu), nameof(BeforeLobbyGUI)),
                postfix: new HarmonyMethod(typeof(ProfileMenu), nameof(AfterLobbyGUI)),
                finalizer: new HarmonyMethod(typeof(ProfileMenu), nameof(EndLobbyGUI)));

            harmony.Patch(
                AccessTools.Method(typeof(GUI), nameof(GUI.TextField), [ typeof(Rect), typeof(string), typeof(int) ]),
                prefix: new HarmonyMethod(typeof(ProfileMenu), nameof(BeforeRoomField)));
        }

        private static void BeforeBrowser(LobbyMenu __instance)
        {
            _instance._drawingBrowser = _instance._session.IsNullFrame && !__instance.BAJDIMCNBDM;
            _instance._roomField = default;
        }

        private static void BeforeRoomField(Rect __0)
        {
            if (_instance._drawingBrowser && __0.y > Screen.height * 0.6f)
            {
                _instance._roomField = __0;
            }
        }

        private static void BeforeBrowserBox(ref Rect __0)
        {
            if (_instance._drawingBrowser && __0.height > Screen.height * 0.5f)
            {
                __0.height = Mathf.Min(__0.height + Screen.height / 17f + 4f, Screen.height - Screen.height / 17f * 1.1f - __0.y);
            }
        }

        private static void AfterBrowser(LobbyMenu __instance)
        {
            ProfileMenu instance = _instance;
            instance._drawingBrowser = false;

            Rect rect = instance._roomField;
            if (rect.width <= 0f || __instance.IsCreatingRoom) return;

            if (instance._label == null)
            {
                instance._label = MenuUi.TextStyle(__instance.Skin.customStyles[0]);
                instance._input = MenuUi.TextStyle(GUI.skin.textField);
            }

            bool oldEnabled = GUI.enabled;
            GUI.enabled = oldEnabled && !instance._session.IsSwitching;

            rect.y += rect.height + 4f;
            instance._label.fontSize = instance._input.fontSize = Mathf.Max(13, Screen.height / 30);

            GUI.Label(
                new Rect(rect.x - Screen.width * 0.14f, rect.y, Screen.width * 0.13f, rect.height),
                "Nickname",
                instance._label);

            if (!instance._editing && string.IsNullOrWhiteSpace(instance._nickname))
            {
                instance._nickname = instance._profiles.DefaultName;
            }

            GUI.SetNextControlName("Nickname");
            float modeWidth = Mathf.Max(100f, rect.width * 0.24f);
            instance._nicknameField = new Rect(rect.x, rect.y, rect.width - rect.height - modeWidth - 16f, rect.height);
            instance._nickname = GUI.TextField(
                instance._nicknameField,
                instance._nickname,
                PlayerProfile.NameLimit * 2,
                instance._input);

            bool focused = GUI.GetNameOfFocusedControl() == "Nickname";
            if ((instance._editing && !focused) ||
                (focused &&
                Event.current.type == EventType.KeyDown &&
                Event.current.keyCode == KeyCode.Return))
            {
                instance.Commit();
                GUI.FocusControl(null);
            }

            instance._editing = focused;
            if (GUI.Button(new Rect(rect.xMax - rect.height - modeWidth - 8f, rect.y, modeWidth, rect.height),
                instance._profiles.Local.ColorMode.ToString()))
            {
                instance.Commit();
                instance._profiles.Save(
                    instance._nickname,
                    instance._profiles.Local.ColorHex,
                    (NameColorMode)(((int)instance._profiles.Local.ColorMode + 1) % 3));
            }

            Rect swatch = new(rect.xMax - rect.height, rect.y, rect.height, rect.height);
            if (ColorPicker.Swatch(swatch, instance._profiles.Local.ColorHex))
            {
                instance.Commit();
                instance._picker.Open(
                    swatch,
                    instance._profiles.Local.ColorHex,
                    color => instance._profiles.Save(instance._nickname, color),
                    sample: instance._nickname);
            }

            GUI.enabled = oldEnabled;
        }

        internal void Commit()
        {
            _profiles.Save(_nickname, _profiles.Local.ColorHex);
            _nickname = _profiles.Local.Nickname;
        }

        private static void EndBrowser() => _instance._drawingBrowser = false;

        private static void BeforeLobbyGUI(out bool __state)
        {
            __state = GUI.enabled;

            if (_instance._session.AddonNotice != null)
            {
                _instance._picker.Close();
                return;
            }

            if (_instance._editing &&
                Event.current.type == EventType.MouseDown &&
                !_instance._nicknameField.Contains(Event.current.mousePosition))
            {
                _instance.Commit();
            }

            if (_instance._editing &&
                Event.current.type == EventType.KeyDown &&
                Event.current.keyCode == KeyCode.Return)
            {
                _instance.Commit();
                GUI.FocusControl(null);
                Event.current.Use();
            }

            _instance._picker.BlockInput();
        }

        private static void AfterLobbyGUI(LobbyMenu __instance, bool __state)
        {
            GUI.enabled = __state;

            if (_instance._session.IsNullFrame && _instance._session.AddonNotice == null && !__instance.IsCreatingRoom)
            {
                _instance._picker.Draw(__instance.Skin);
            }
            else
            {
                _instance._picker.Close();
            }
        }

        private static void EndLobbyGUI(bool __state) => GUI.enabled = __state;
    }
}
