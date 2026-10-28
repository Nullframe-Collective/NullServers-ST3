using HarmonyLib;
using Il2Cpp;
using Il2CppInterop.Runtime;
using NullServers.Additional;
using NullServers.Customization;
using NullServers.Network;
using NullServers.Players;
using NullServers.Room;
using NullServers.Sandbox;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using Object = UnityEngine.Object;

namespace NullServers.UI
{
    internal sealed class SandboxMenu
    {
        private sealed class Sidebar
        {
            internal Volume Volume;
            internal GameUi Ui;
            internal RectTransform Root, Content;
            internal (RectTransform Rect, Transform Parent, Vector2 Min, Vector2 Max, Vector2 Low, Vector2 High)[] Original;
        }

        private static SandboxMenu _instance;
        private readonly SandboxWorld _world;
        private readonly Dictionary<IntPtr, int> _categories = new();
        private readonly Dictionary<IntPtr, Sidebar> _sidebars = new();
        private readonly Dictionary<IntPtr, Toggle> _throwToggles = new();
        private readonly Dictionary<IntPtr, (RectTransform Rect, Vector2 Size, Vector2 Position, Vector3 Scale)> _footerOriginal = new();
        private readonly Dictionary<IntPtr, int> _selectedCategories = new();
        private readonly List<(GameObject Object, bool Active)> _hidden = new();
        private readonly List<(RawImage Image, SandboxAssets.Entry Entry)> _icons = new();
        private readonly List<(Button Button, PlayerCommand Command, string Name)> _actions = new();
        private Volume _volume;
        private RoomMultiplayerMenu _roomMenu;
        private GameUi _ui;
        private RectTransform _panel, _body, _catalog, _players, _palette, _wheel, _marker, _hudRoot;
        private Text _status, _selection, _count, _hud;
        private InputField[] _position, _size, _angles;
        private InputField _hex;
        private Button _colorButton, _physics, _effects;
        private Action<string> _paletteChanged;
        private string _color = "#FFFFFF", _texture = "", _search = "", _roster;
        private bool _dynamic, _effectsEnabled = true, _refreshCatalog, _wheelDragging;
        private float _refreshAt, _rosterAt, _hue, _saturation, _brightness = 1f;
        private int _tab = -1, _buildingTab, _filter, _target, _draftId = -1;

        internal SandboxMenu(HarmonyLib.Harmony harmony, SandboxWorld world)
        {
            _instance = this;
            _world = world;

            harmony.Patch(
                AccessTools.Method(typeof(Volume), nameof(Volume.CreateCatagoryButtons)),
                prefix: new HarmonyMethod(typeof(SandboxMenu), nameof(BeforeCategories)),
                postfix: new HarmonyMethod(typeof(SandboxMenu), nameof(AfterCategories)));

            harmony.Patch(
                AccessTools.Method(typeof(Volume), nameof(Volume.Update)),
                postfix: new HarmonyMethod(typeof(SandboxMenu), nameof(AfterNativeUpdate)));

            harmony.Patch(
                AccessTools.Method(typeof(UI_Button), nameof(UI_Button.SendToVolume)),
                prefix: new HarmonyMethod(typeof(SandboxMenu), nameof(BeforeNativeButton)));

            harmony.Patch(
                AccessTools.Method(typeof(WeaponScript), "Update"),
                prefix: new HarmonyMethod(typeof(SandboxMenu), nameof(BeforeWeapon)));

            harmony.Patch(
                AccessTools.Method(typeof(Il2CppInControl.mod.cInput), nameof(Il2CppInControl.mod.cInput.GetButtonDown)),
                prefix: new HarmonyMethod(typeof(SandboxMenu), nameof(BeforeSwitch)));
        }

        private static void BeforeCategories(Volume __instance)
        {
            SandboxMenu menu = _instance;
            if (!menu._world.Active || __instance.Categories == null) return;

            menu.ClearCategories(__instance);

            for (int index = 0; index + 1 < __instance.Categories.Count; index++)
            {
                if (__instance.Categories[index].catagoryName == "Advanced" &&
                    __instance.Categories[index + 1].catagoryName == "Building")
                {
                    menu._categories[__instance.Pointer] = index;
                    return;
                }
            }

            menu._categories[__instance.Pointer] = __instance.Categories.Count;
            foreach (string name in new[] { "Advanced", "Building" })
            {
                __instance.Categories.Add(new Volume.catagory
                {
                    catagoryName = name,
                    tagName = "",
                    options = new Il2CppSystem.Collections.Generic.List<Volume.option>(),
                    hostOnly = false
                });
            }
        }

        private static void AfterCategories(Volume __instance)
        {
            SandboxMenu menu = _instance;
            if (!menu._categories.TryGetValue(__instance.Pointer, out var first) || __instance.CategoryButtons == null)
            {
                return;
            }

            menu.ThrowToggle(__instance);

            for (int tab = 0; tab < 2; tab++)
            {
                if (first + tab >= __instance.CategoryButtons.Count) continue;

                RectTransform rect = __instance.CategoryButtons[first + tab];
                rect.name = tab == 0 ? "NullFrameAdvanced" : "NullFrameBuilding";
                UI_Button native = rect.GetComponent(Il2CppType.Of<UI_Button>())?.Cast<UI_Button>();

                if (native)
                {
                    native.enabled = false;
                }

                Button button = rect.GetComponent(Il2CppType.Of<Button>())?.Cast<Button>();
                if (!button) continue;

                int selected = tab;
                GameUi.OnClick(button, () => menu.Open(__instance, selected));
            }

            if (menu._sidebars.ContainsKey(__instance.Pointer)) return;

            GameUi ui = new(__instance);
            RectTransform holder = GameUi.Rect("Categories", __instance.Panel);
            GameUi.Area(holder, 0.025f, 0.105f, 0.225f, 0.88f);
            ScrollRect scroll = ui.Scroll(holder, "CategoryScroll");

            var original = __instance.CategoryButtons.ToArray().Select(rect => (rect, rect.parent, rect.anchorMin, rect.anchorMax, rect.offsetMin, rect.offsetMax)).ToArray();

            foreach (RectTransform rect in __instance.CategoryButtons)
            {
                rect.SetParent(scroll.content, false);
                rect.localScale = Vector3.one;

                Text text = rect.GetComponentInChildren(Il2CppType.Of<Text>(), true)?.Cast<Text>();
                if (text)
                {
                    text.fontSize = Mathf.RoundToInt(27f * ui.Unit);
                    text.resizeTextForBestFit = true;
                    text.resizeTextMinSize = Mathf.RoundToInt(17f * ui.Unit);
                }
            }

            menu._sidebars[__instance.Pointer] = new Sidebar
            {
                Volume = __instance,
                Ui = ui,
                Root = holder,
                Content = scroll.content,
                Original = original
            };
        }

        private void ClearCategories(Volume volume)
        {
            if (_volume == volume)
            {
                Close();

                if (_panel)
                {
                    Object.Destroy(_panel.gameObject);
                    _panel = null;
                }
            }

            List<RectTransform> buttons = volume.CategoryButtons?.ToArray().ToList() ?? new();
            if (_sidebars.Remove(volume.Pointer, out var sidebar))
            {
                buttons.AddRange(sidebar.Original.Select(item => item.Rect));

                if (sidebar.Root)
                {
                    sidebar.Root.gameObject.SetActive(false);
                    Object.Destroy(sidebar.Root.gameObject);
                }
            }

            foreach (RectTransform rect in buttons.Where(rect => rect).DistinctBy(rect => rect.Pointer))
            {
                volume.FIKDALMIMAB?.Remove(rect);
                rect.gameObject.SetActive(false);
                Object.Destroy(rect.gameObject);
            }

            volume.CategoryButtons?.Clear();
            _selectedCategories.Remove(volume.Pointer);
        }

        private void ThrowToggle(Volume volume)
        {
            if (_throwToggles.TryGetValue(volume.Pointer, out var existing) && existing)
            {
                foreach (Text label in existing.GetComponentsInChildren<Text>(true))
                {
                    label.text = "Throw Any One";
                }

                return;
            }

            Toggle source = volume.Panel.GetComponentsInChildren<Toggle>(true).FirstOrDefault(toggle =>
                toggle.name != "NullFrameThrowAnyone" &&
                toggle.GetComponentsInChildren<Text>(true).Any(label => label.text?.Trim() == "Friendly Fire"));
            if (!source) return;

            Toggle copy = Object.Instantiate(source, volume.Panel, false);
            copy.name = "NullFrameThrowAnyone";

            foreach (TranslateText translation in copy.GetComponentsInChildren<TranslateText>(true))
            {
                translation.enabled = false;
                Object.DestroyImmediate(translation);
            }

            copy.onValueChanged = new Toggle.ToggleEvent();
            foreach (Text label in copy.GetComponentsInChildren<Text>(true))
            {
                label.text = "Throw Any One";
            }

            RectTransform original = source.GetComponent<RectTransform>();
            RectTransform rect = copy.GetComponent<RectTransform>();
            rect.SetParent(original.parent, false);
            rect.anchorMin = original.anchorMin;
            rect.anchorMax = original.anchorMax;
            rect.pivot = original.pivot;

            if (!_footerOriginal.ContainsKey(volume.Pointer))
            {
                _footerOriginal[volume.Pointer] = (original, original.sizeDelta, original.anchoredPosition, original.localScale);
            }

            var saved = _footerOriginal[volume.Pointer];
            const float width = 140f, scale = 0.8f;
            Vector2 center = saved.Position + Vector2.right * (original.pivot.x * (width * scale - saved.Size.x));
            float footerHeight = original.parent.Cast<RectTransform>().rect.height;
            float rowHeight = Mathf.Max(8f, footerHeight * 0.5f - 2f);
            original.sizeDelta = rect.sizeDelta = new Vector2(width, rowHeight / scale);
            original.localScale = rect.localScale = Vector3.Scale(saved.Scale, new Vector3(scale, scale, 1f));
            original.anchoredPosition = center + Vector2.down * (footerHeight * 0.25f);
            rect.anchoredPosition = center + Vector2.up * (footerHeight * 0.25f);
            copy.SetIsOnWithoutNotify(_world.Environment.ThrowAnyone);
            copy.interactable = _world.IsHost;
            copy.onValueChanged.AddListener(DelegateSupport.ConvertDelegate<UnityAction<bool>>((Action<bool>)(value =>
            {
                if (!_world.IsHost) return;

                if (!_world.SetEnvironment(_world.Environment with { ThrowAnyone = value }, out var error))
                {
                    _world.SetStatus(error);
                }
            })));
            _throwToggles[volume.Pointer] = copy;
        }

        private void RestoreFooter()
        {
            foreach (var saved in _footerOriginal.Values)
            {
                if (!saved.Rect) continue;

                saved.Rect.sizeDelta = saved.Size;
                saved.Rect.anchoredPosition = saved.Position;
                saved.Rect.localScale = saved.Scale;
            }

            _footerOriginal.Clear();
            foreach (Toggle toggle in _throwToggles.Values)
            {
                if (toggle)
                {
                    Object.Destroy(toggle.gameObject);
                }
            }

            _throwToggles.Clear();
        }

        private static void AfterNativeUpdate(Volume __instance)
        {
            if (!_instance._world.Active || !__instance.InfoText) return;

            string text = __instance.InfoText.text;
            if (text?.Contains("<color=clear>", StringComparison.Ordinal) == true)
            {
                __instance.InfoText.text = text.Replace("<color=clear>", "<color=#30383F>", StringComparison.Ordinal);
            }
        }

        private static bool BeforeNativeButton(UI_Button __instance)
        {
            if (__instance.name.StartsWith("NullFrame", StringComparison.Ordinal))
            {
                return false;
            }

            if (_instance._tab >= 0 && __instance.Volume == _instance._volume)
            {
                _instance.Close();
            }

            Volume volume = __instance.Volume;
            if (volume && volume.CategoryButtons != null)
            {
                for (int index = 0; index < volume.CategoryButtons.Count; index++)
                {
                    RectTransform rect = volume.CategoryButtons[index];
                    if (rect && rect.GetComponent(Il2CppType.Of<UI_Button>())?.Pointer == __instance.Pointer)
                    {
                        _instance._selectedCategories[volume.Pointer] = index;
                        break;
                    }
                }
            }

            return true;
        }

        private static bool BeforeSwitch(string __0, ref bool __result)
        {
            if (!_instance._world.Active || _instance._world.Hands.Holding == 0 ||
                __0 is not ("Switch" or "Switch2") || Input.GetAxis("Mouse ScrollWheel") == 0f) return true;
            __result = false;
            return false;
        }

        private static bool BeforeWeapon(Component __instance)
        {
            if (!_instance._world.Active || !_instance._world.Hands.UsingTool) return true;

            if (!__instance.GetComponentInParent(Il2CppType.Of<FPScontroller>()))
            {
                return true;
            }

            WeaponScript weapon = __instance.TryCast<WeaponScript>();
            if (weapon && weapon.isActiveAndEnabled)
            {
                weapon.transform.localPosition = weapon.RestPosition;
            }

            return false;
        }

        private void HideNative(GameObject target)
        {
            if (!target || _hidden.Any(item => item.Object == target)) return;

            _hidden.Add((target, target.activeSelf));
            target.SetActive(false);
        }

        private void Open(Volume volume, int tab)
        {
            if (!_world.Active || tab == 0 && !_world.IsAdmin) return;

            Close();
            _volume = volume;
            _tab = tab;

            if (tab == 1)
            {
                _world.Hands.Equip(true);
                if (_world.Building.Draft == null && _world.Building.TargetId > 0)
                {
                    _world.Building.SelectId(_world.Building.TargetId);
                }
            }

            _selectedCategories[volume.Pointer] = _categories[volume.Pointer] + tab;
            _ui = new GameUi(volume);

            if (volume.KDABMADBABK != null)
            {
                foreach (RectTransform rect in volume.KDABMADBABK)
                {
                    if (!rect) continue;

                    HideNative(rect.gameObject);
                }
            }

            if (volume.DKOOOHKFCDK)
            {
                HideNative(volume.DKOOOHKFCDK.gameObject);
            }

            if (_panel)
            {
                Object.Destroy(_panel.gameObject);
            }

            _panel = GameUi.Rect("SandboxPanel", volume.Panel);
            HideFooter(volume);
            GameUi.Area(_panel, 0.29f, 0.025f, 0.95f, 0.88f);
            _ui.Image(_panel, new Color(0.94f, 0.95f, 0.97f, 0.98f));

            Text title = _ui.Label(_panel, tab == 0 ? "Advanced" : "Building", 32);
            title.fontStyle = FontStyle.Bold;
            _ui.SetRow(title.rectTransform, 8f, 48f, 18f, 18f);
            _status = _ui.Label(_panel, "", 19);

            GameUi.Area(_status.rectTransform, 0f, 0f, 1f, 0f);
            _status.rectTransform.offsetMin = new Vector2(18f, 7f) * _ui.Unit;
            _status.rectTransform.offsetMax = new Vector2(-18, 56f) * _ui.Unit;
            _body = GameUi.Rect("Page", _panel);
            _body.offsetMin = new Vector2(16f, 64f) * _ui.Unit;
            _body.offsetMax = new Vector2(-16, -66) * _ui.Unit;
            _world.Assets.Capture(_world.Volumes);
            _actions.Clear();
            _icons.Clear();
            _catalog = null;

            if (tab == 0)
            {
                Advanced();
            }
            else
            {
                Building();
            }

            _panel.SetAsLastSibling();
        }

        private void Close()
        {
            foreach (var item in _hidden)
            {
                if (!item.Object) continue;

                item.Object.SetActive(item.Active);
            }

            _hidden.Clear();
            _tab = -1;

            if (_panel)
            {
                _panel.gameObject.SetActive(false);
            }

            _palette = null;
            _wheelDragging = false;
        }

        private void HideFooter(Volume volume)
        {
            foreach (Text label in volume.Panel.GetComponentsInChildren<Text>(true))
            {
                string text = label.text?.Trim();
                if (text is "Remove My AI" or "Remove All AI" or "Friendly Fire" or "Disable AI" or "Throw Any One" ||
                    text?.StartsWith("Total AI:", StringComparison.Ordinal) == true)
                {
                    Button button = label.GetComponentInParent(Il2CppType.Of<Button>())?.Cast<Button>();
                    Toggle toggle = label.GetComponentInParent(Il2CppType.Of<Toggle>())?.Cast<Toggle>();
                    HideNative(button ? button.gameObject : toggle ? toggle.gameObject : label.gameObject);
                }
            }
        }

        private static void CategoryStyle(RectTransform rect, bool selected)
        {
            Button button = rect.GetComponent(Il2CppType.Of<Button>())?.Cast<Button>();
            Image image = rect.GetComponent(Il2CppType.Of<Image>())?.Cast<Image>();
            Text label = rect.GetComponentInChildren(Il2CppType.Of<Text>(), true)?.Cast<Text>();

            if (label)
            {
                label.color = selected ? Color.white : GameUi.Ink;
            }

            if (image)
            {
                image.color = selected ? new Color(0.48f, 0.48f, 0.48f) : Color.white;
            }

            if (!button) return;

            button.transition = Selectable.Transition.ColorTint;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.selectedColor = Color.white;
            colors.highlightedColor = new Color(0.92f, 0.92f, 0.92f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f);
            colors.disabledColor = new Color(1f, 1f, 1f, 0.5f);
            button.colors = colors;
        }

        private void CloseConsole()
        {
            Close();

            if (!_volume) return;

            _volume.EGNKPPJHIBA = false;
            _volume.Panel.gameObject.SetActive(false);
            UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void Advanced()
        {
            RectTransform left = GameUi.Rect("PlayerColumn", _body);
            GameUi.Area(left, 0f, 0f, 0.29f, 1f, 3f * _ui.Unit);
            Text title = _ui.Label(left, "Players", 23);
            _ui.SetRow(title.rectTransform, 0f, 38f);

            ScrollRect list = _ui.Scroll(left, "Players", 44f);

            _players = list.content;
            _roster = null;
            _rosterAt = 0f;

            RectTransform right = GameUi.Rect("Actions", _body);
            GameUi.Area(right, 0.31f, 0f, 1f, 1f);
            RectTransform content = _ui.Scroll(right, "Actions").content;
            _ui.Label(_ui.Row(content, 0f), "PLAYER ACTIONS", 22).fontStyle = FontStyle.Bold;

            RectTransform scaleRow = _ui.Row(content, 50f);
            Text scaleLabel = _ui.Label(scaleRow, "Size (0.2–5)");
            GameUi.Area(scaleLabel.rectTransform, 0f, 0f, 0.38f, 1f);

            InputField scale = _ui.Field(scaleRow, 0.4f, 0.68f, "1", "1", limit: 8);

            _ui.RowButton(scaleRow, "Apply", () =>
            {
                if (Number(scale.text, out var value))
                {
                    _world.Admin(PlayerCommand.Scale, _target, value);
                }
            }, 0.7f);

            var actions = new[]
            {
                ("Flight", PlayerCommand.Fly),
                ("Freeze", PlayerCommand.Freeze),
                ("Invincible", PlayerCommand.Invincible),
                ("Heal", PlayerCommand.Heal),
                ("Bring here", PlayerCommand.Bring),
                ("Go to player", PlayerCommand.GoTo),
                ("Launch upwards", PlayerCommand.Launch),
                ("Respawn", PlayerCommand.Respawn),
                ("Kill", PlayerCommand.Kill),
                ("Remove character", PlayerCommand.Remove),
                ("Reset effects", PlayerCommand.Reset)
            };
            for (int index = 0; index < actions.Length; index++)
            {
                var action = actions[index];
                RectTransform row = _ui.Row(content, 106 + index / 2 * 56, 50f);

                float edge = index % 2 * 0.5f;
                Button button = _ui.RowButton(
                    row,
                    action.Item1,
                    () => _world.Admin(action.Item2, _target),
                    edge,
                    edge + 0.5f,
                    action.Item2 is PlayerCommand.Kill or PlayerCommand.Remove ? new Color(1f, 0.84f, 0.82f) : null);

                _actions.Add((button, action.Item2, action.Item1));
            }

            RectTransform rename = _ui.Row(content, 456f);
            InputField name = _ui.Field(rename, 0f, 0.65f, "", "New nickname", limit: PlayerProfile.NameLimit);

            _ui.RowButton(rename, "Rename", () => _world.Admin(PlayerCommand.Rename, _target, text: name.text), 0.67f);
            _ui.RowButton(
                _ui.Row(content, 522f),
                "Clear all building objects",
                () => _world.Submit(_world.Message(SandboxKind.Clear)),
                color: new Color(1f, 0.9f, 0.78f));

            _ui.Height(content, 584f);
        }

        private void RefreshPlayers()
        {
            var players = _world.Players;
            string roster = string.Join("|", players.Select(player => player.Id + ":" + player.Nickname)) + ":" + _target;

            if (roster != _roster)
            {
                _roster = roster;
                GameUi.Clear(_players);

                if (!players.Any(player => player.Id == _target))
                {
                    _target = players.FirstOrDefault()?.Id ?? 0;
                }

                float y = 0f;
                foreach (RoomPlayer player in players)
                {
                    int id = player.Id;
                    Button button = _ui.Button(
                        _players, player.Nickname + (id == 0 ? " (Host)" : ""), () =>
                        {
                            _target = id;
                            _roster = null;
                        },
                        id == _target ? new Color(0.72f, 0.86f, 0.97f) : Color.white);

                    _ui.SetRow(button.GetComponent<RectTransform>(), y, 62f);

                    y += 68f;
                }

                _ui.Height(_players, y);
            }

            PlayerEffects effects = _world.Effects(_target);

            foreach (var action in _actions)
            {
                action.Button.interactable = _world.IsAdmin && (_world.IsHost || _target != 0);

                bool enabled = action.Command switch
                {
                    PlayerCommand.Fly => effects.Fly,
                    PlayerCommand.Freeze => effects.Frozen,
                    PlayerCommand.Invincible => effects.Invincible,
                    _ => false
                };

                action.Button.GetComponentInChildren(Il2CppType.Of<Text>(), true).Cast<Text>().text =
                    action.Name + (action.Command is PlayerCommand.Fly or PlayerCommand.Freeze or PlayerCommand.Invincible ? enabled ? ": ON" : ": OFF" : "");
            }
        }

        private void Building()
        {
            GameUi.Clear(_body);
            _icons.Clear();
            _catalog = null;

            if (_palette)
            {
                Object.Destroy(_palette.gameObject);
            }

            _palette = null;

            RectTransform tabs = _ui.Row(_body, 0f, 48f);
            var pages = new[]
            {
                ("Objects", 0f, 0.23f),
                ("Object settings", 0.24f, 0.55f)
            };
            for (int index = 0; index < pages.Length; index++)
            {
                int tab = index;
                var page = pages[index];
                _ui.RowButton(tabs, page.Item1, () =>
                {
                    _buildingTab = tab;
                    Building();
                }, page.Item2, page.Item3, _buildingTab == tab ? new Color(0.75f, 0.87f, 0.97f) : null);
            }

            _count = _ui.Label(tabs, "", 19, TextAnchor.MiddleRight);
            GameUi.Area(_count.rectTransform, 0.72f, 0f, 1f, 1f);

            RectTransform tools = _ui.Row(_body, 58f);
            _ui.RowButton(tools, "Build with empty hands", () =>
            {
                _world.Building.BeginSelection();
                CloseConsole();
            }, 0f, 0.42f);

            _ui.RowButton(tools, "Undo", () => _world.Building.Undo(), 0.43f, 0.69f);

            _ui.RowButton(tools, "Return to weapon", () =>
            {
                _world.Hands.Equip(false);
                Building();
            }, 0.7f);

            _selection = _ui.Label(_ui.Row(_body, 114f, 40f), "", 20);
            _selection.color = GameUi.Blue;

            if (_buildingTab == 0)
            {
                Catalog();
            }
            else if (_buildingTab == 1)
            {
                Editor();
            }
        }

        private void Catalog()
        {
            RectTransform row = _ui.Row(_body, 158f);
            _ui.Field(row, 0f, 0.5f, _search, "Search objects...", value =>
            {
                _search = value;
                _refreshCatalog = true;
                _refreshAt = Time.unscaledTime + 0.15f;
            }, 80);

            string[] names =
            [
                "All",
                "Shapes",
                "Props",
                "Recent"
            ];
            for (int index = 0; index < names.Length; index++)
            {
                int filter = index;

                _ui.RowButton(row, names[index], () =>
                {
                    _filter = filter;
                    _refreshCatalog = true;
                    _refreshAt = 0f;
                }, 0.51f + index * 0.1225f, 0.51f + (index + 1) * 0.1225f);
            }

            ScrollRect scroll = _ui.Scroll(_body, "ObjectCatalog", 216f, 44f);

            _catalog = scroll.content;

            Text help = _ui.Label(
                _body,
                "LMB: place/select. Shift + LMB: group selection. E: carry/drop. LMB while carrying: throw. R + mouse: rotate.",
                19);

            GameUi.Area(help.rectTransform, 0f, 0f, 1f, 0f);
            help.rectTransform.offsetMax = new Vector2(0f, 40f * _ui.Unit);

            _refreshCatalog = true;
            _refreshAt = 0f;
        }

        private void RefreshCatalog()
        {
            float width = _catalog.rect.width;
            if (width <= 0f)
            {
                _refreshAt = Time.unscaledTime + 0.05f;
                return;
            }

            GameUi.Clear(_catalog);

            _icons.Clear();
            _refreshCatalog = false;

            var source = _filter == 3 ?
                _world.Building.Recent.Select(id => _world.Assets.Find(id)).Where(entry => entry != null) :
                _world.Assets.Entries;

            SandboxAssets.Entry[] entries = source.Where(entry => (_filter != 1 || entry.Primitive.HasValue) &&
                (_filter != 2 ||
                !entry.Primitive.HasValue) &&
                (string.IsNullOrWhiteSpace(_search) || entry.Name.Contains(_search, StringComparison.OrdinalIgnoreCase))).ToArray();

            int columns = Mathf.Max(2, Mathf.FloorToInt(width / (146f * _ui.Unit)));
            float cellWidth = width / columns;
            float cellHeight = 154f * _ui.Unit;

            for (int index = 0; index < entries.Length; index++)
            {
                SandboxAssets.Entry entry = entries[index];
                Button tile = _ui.Button(_catalog, "", () =>
                {
                    _world.Building.Start(entry.Id);
                    CloseConsole();
                });

                RectTransform rect = tile.GetComponent<RectTransform>();
                rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.anchoredPosition = new Vector2(index % columns * cellWidth, -(index / columns) * cellHeight);
                rect.sizeDelta = new Vector2(cellWidth - 7f * _ui.Unit, cellHeight - 7f * _ui.Unit);

                RectTransform iconRect = GameUi.Rect("Thumbnail", rect);
                GameUi.Area(iconRect, 0.07f, 0.27f, 0.93f, 0.96f);

                if (entry.Icon)
                {
                    Image icon = _ui.Image(iconRect, Color.white, false);
                    icon.sprite = entry.Icon;
                    icon.preserveAspect = true;
                }
                else
                {
                    RawImage icon = GameUi.Add<RawImage>(iconRect.gameObject);
                    icon.raycastTarget = false;
                    icon.texture = _world.Assets.Icon(entry);
                    _icons.Add((icon, entry));
                }

                Text caption = _ui.Label(rect, entry.Name, 17, TextAnchor.MiddleCenter);
                GameUi.Area(caption.rectTransform, 0.04f, 0.02f, 0.96f, 0.26f);
            }

            _ui.Height(_catalog, Mathf.Ceil(entries.Length / (float)columns) * cellHeight / _ui.Unit);
            if (entries.Length == 0)
            {
                _ui.Label(_ui.Row(_catalog, 0f, 65f), "No objects found. Try another category or search.");
            }
        }

        private InputField[] VectorFields(Transform parent, float y, string title, Point3 value)
        {
            RectTransform row = _ui.Row(parent, y, 58f);
            Text titleText = _ui.Label(row, title);
            GameUi.Area(titleText.rectTransform, 0f, 0f, 0.25f, 1f);
            InputField[] result = new InputField[3];

            float[] values =
            [
                value.X,
                value.Y,
                value.Z
            ];

            for (int index = 0; index < 3; index++)
            {
                RectTransform axis = GameUi.Rect("Axis", row);
                GameUi.Area(axis, 0.26f + index * 0.245f, 0f, 0.495f + index * 0.245f, 1f, 3f * _ui.Unit);

                Text label = _ui.Label(axis, "XYZ"[index].ToString(), 18);
                GameUi.Area(label.rectTransform, 0f, 0f, 0.14f, 1f);

                InputField input = _ui.Input(
                    axis,
                    values[index].ToString("0.###", CultureInfo.InvariantCulture),
                    "0",
                    _ => Preview(),
                    12);

                GameUi.Area(input.GetComponent<RectTransform>(), 0.16f, 0f, 1f, 1f);

                result[index] = input;
            }

            return result;
        }

        private void Editor()
        {
            BuildingObject draft = _world.Building.Draft;
            _draftId = draft?.Id ?? -1;

            if (draft == null)
            {
                _ui.Label(
                    _ui.Row(_body, 180f, 90f),
                    "Aim at a prop and left-click in build mode. Hold Shift to add more objects. E picks up nearby props and ragdolls.",
                    23);

                return;
            }

            ScrollRect scroll = _ui.Scroll(_body, "ObjectEditor", 168f);
            RectTransform content = scroll.content;

            _position = _size = _angles = null;
            _position = VectorFields(content, 0f, "Position", draft.Position);
            _size = VectorFields(content, 72f, "Size", draft.Scale);

            foreach (InputField field in _position)
            {
                field.interactable = _world.Building.Editing;
            }

            _angles = VectorFields(
                content,
                144f,
                "Rotation",
                SandboxWorld.Point(SandboxWorld.Quaternion(draft.Rotation).eulerAngles));
            _color = draft.Color;
            _texture = draft.Texture ?? "";
            _dynamic = draft.Dynamic;
            _effectsEnabled = draft.Effects;

            RectTransform row = _ui.Row(content, 224f, 54f);
            Text label = _ui.Label(row, "Color");
            GameUi.Area(label.rectTransform, 0f, 0f, 0.25f, 1f);

            _colorButton = _ui.RowButton(row, "", () => Palette(), 0.26f, 0.34f);
            _hex = _ui.Field(row, 0.36f, 0.67f, _color, "#FFFFFF", value =>
            {
                if (RoomOptions.IsValidColor(value))
                {
                    _color = value;
                    Preview();
                }
            }, 7);

            _physics = _ui.RowButton(row, "", () =>
            {
                _dynamic = !_dynamic;
                Preview();
            }, 0.7f);

            _effects = _ui.RowButton(_ui.Row(content, 288f, 46f), "", () =>
            {
                _effectsEnabled = !_effectsEnabled;
                Preview();
            });

            RectTransform textureRow = _ui.Row(content, 350f, 54f);
            Text textureLabel = _ui.Label(textureRow, "Texture URL");
            GameUi.Area(textureLabel.rectTransform, 0f, 0f, 0.25f, 1f);

            _ui.Field(textureRow, 0.26f, 1f, _texture, "https://.../texture.png", value =>
            {
                _texture = value;
                Preview();
            }, Appearance.UrlLimit);

            RectTransform controls = _ui.Row(content, 430f, 54f);

            _ui.RowButton(controls, "Apply to selection", () =>
            {
                Preview();
                _world.Building.Confirm();
                Building();
            }, 0f, 0.49f, new Color(0.74f, 0.9f, 0.79f));

            _ui.RowButton(controls, "Delete selection", () =>
            {
                _world.Building.DeleteSelected();
                Building();
            }, 0.51f, 1f, new Color(1f, 0.83f, 0.8f));

            _ui.Label(
                _ui.Row(content, 500f, 60f),
                "Type exact X/Y/Z values. Changed properties apply to every selected object; other properties are preserved.",
                19);
            _ui.Height(content, 566f);

            Preview();
        }

        private static bool Number(string value, out float number) => float.TryParse(
            value.Replace(',', '.'),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out number) && float.IsFinite(number);

        private static Point3 Values(InputField[] fields)
        {
            if (fields == null || fields.Any(field => !field || !Number(field.text, out _)))
            {
                return null;
            }

            float[] values = fields.Select(field => float.Parse(field.text.Replace(',', '.'), CultureInfo.InvariantCulture)).ToArray();
            return new Point3(values[0], values[1], values[2]);
        }

        private void Preview()
        {
            Point3 position = Values(_position);
            Point3 size = Values(_size);
            Point3 angles = Values(_angles);

            if (position != null)
            {
                _world.Building.MoveSelected(position);
            }

            if (size != null && angles != null)
            {
                _world.Building.Change(
                    size,
                    SandboxWorld.Rotation(Quaternion.Euler(angles.X, angles.Y, angles.Z)),
                    _color,
                    _texture,
                    _dynamic,
                    _effectsEnabled);
            }

            GameUi.Tint(_colorButton, _color);

            if (_physics)
            {
                _physics.GetComponentInChildren(Il2CppType.Of<Text>(), true).Cast<Text>().text = "Physics: " + (_dynamic ? "ON" : "OFF");
            }

            if (_effects)
            {
                _effects.GetComponentInChildren(Il2CppType.Of<Text>(), true).Cast<Text>().text =
                    "Effects: " + (_effectsEnabled ? "ON" : "OFF");
            }
        }

        private void Palette(Action<string> changed = null)
        {
            _paletteChanged = changed;
            if (_palette)
            {
                Object.Destroy(_palette.gameObject);
            }

            _palette = GameUi.Rect("ColorPalette", _panel);
            _ui.Image(_palette, new Color(0.7f, 0.74f, 0.8f, 0.8f));

            RectTransform box = GameUi.Rect("PalettePanel", _palette);
            box.anchorMin = box.anchorMax = box.pivot = new Vector2(0.5f, 0.5f);
            box.sizeDelta = new Vector2(360f, 440f) * _ui.Unit;

            _ui.Image(box, new Color(0.96f, 0.97f, 0.98f));
            _ui.Label(_ui.Row(box, 12f, 38f), "Object color", 25, TextAnchor.MiddleCenter);
            _wheel = GameUi.Rect("ColorWheel", box);
            _wheel.anchorMin = _wheel.anchorMax = new Vector2(0.5f, 1f);
            _wheel.pivot = new Vector2(0.5f, 1f);
            _wheel.anchoredPosition = new Vector2(0f, -58 * _ui.Unit);
            _wheel.sizeDelta = new Vector2(270f, 270f) * _ui.Unit;

            GameUi.Add<RawImage>(_wheel.gameObject).texture = MenuUi.ColorWheel;

            _marker = GameUi.Rect("ColorCursor", _wheel);
            _marker.sizeDelta = new Vector2(10f, 10f) * _ui.Unit;
            _ui.Image(_marker, GameUi.Ink, false);

            ColorUtility.TryParseHtmlString(_color, out var selected);
            Color.RGBToHSV(selected, out _hue, out _saturation, out _brightness);

            Slider slider = _ui.Slider(_ui.Row(box, 342f, 20f), _brightness, value =>
            {
                _brightness = value;
                PaletteColor();
            });
            slider.GetComponent<RectTransform>().offsetMin += new Vector2(24f * _ui.Unit, 0f);
            slider.GetComponent<RectTransform>().offsetMax -= new Vector2(24f * _ui.Unit, 0f);

            _ui.RowButton(_ui.Row(box, 382f, 44f), "Done", () =>
            {
                Object.Destroy(_palette.gameObject);
                _palette = null;
                _paletteChanged = null;
            });

            PaletteColor();
        }

        private void PaletteColor()
        {
            _color = "#" + ColorUtility.ToHtmlStringRGB(Color.HSVToRGB(_hue, _saturation, _brightness));
            if (_hex)
            {
                _hex.SetTextWithoutNotify(_color);
            }

            _marker.anchorMin = _marker.anchorMax = new Vector2(
                Mathf.Cos(_hue * 2f * Mathf.PI),
                Mathf.Sin(_hue * 2f * Mathf.PI)) * (_saturation * 0.5f) + Vector2.one * 0.5f;
            _marker.anchoredPosition = Vector2.zero;

            if (_paletteChanged != null)
            {
                _paletteChanged(_color);
                GameUi.Tint(_colorButton, _color);
            }
            else
            {
                Preview();
            }
        }

        internal void Update()
        {
            RoomMultiplayerMenu room = RoomMultiplayerMenu.Current;
            if (room != _roomMenu)
            {
                Close();
                RestoreFooter();
                _categories.Clear();
                _selectedCategories.Clear();

                if (_hudRoot)
                {
                    Object.Destroy(_hudRoot.gameObject);
                }

                _hudRoot = null;
                _hud = null;

                foreach (Sidebar sidebar in _sidebars.Values)
                {
                    foreach (var item in sidebar.Original)
                    {
                        if (item.Rect && item.Parent)
                        {
                            item.Rect.SetParent(item.Parent, false);
                            item.Rect.anchorMin = item.Min;
                            item.Rect.anchorMax = item.Max;
                            item.Rect.offsetMin = item.Low;
                            item.Rect.offsetMax = item.High;
                        }
                    }

                    if (sidebar.Root)
                    {
                        Object.Destroy(sidebar.Root.gameObject);
                    }
                }

                _sidebars.Clear();
                if (_panel)
                {
                    Object.Destroy(_panel.gameObject);
                }

                _roomMenu = room;
            }

            if (!_world.Active)
            {
                RestoreFooter();
                Close();

                if (_hudRoot)
                {
                    _hudRoot.gameObject.SetActive(false);
                }

                return;
            }

            foreach (Volume volume in _world.Volumes)
            {
                if (!volume) continue;

                ThrowToggle(volume);
                if (!_volume)
                {
                    _volume = volume;
                }

                if (!_sidebars.ContainsKey(volume.Pointer) && volume.ButtonTemplate &&
                    volume.Categories != null && volume.CategoryButtons != null)
                {
                    volume.CreateCatagoryButtons();
                }

                if (!_sidebars.TryGetValue(volume.Pointer, out var sidebar))
                {
                    continue;
                }

                if (_throwToggles.TryGetValue(volume.Pointer, out var toggle) && toggle)
                {
                    toggle.interactable = _world.IsHost;
                    toggle.SetIsOnWithoutNotify(_world.Environment.ThrowAnyone);
                }

                GameUi ui = sidebar.Ui;
                float y = 0f;
                int visible = sidebar.Original.Count(item => item.Rect &&
                    (item.Rect.name != "NullFrameAdvanced" || _world.IsAdmin));
                float rowHeight = Mathf.Clamp(sidebar.Root.rect.height / ui.Unit / Mathf.Max(1, visible) - 6f, 46f, 76f);

                int selected = _selectedCategories.GetValueOrDefault(volume.Pointer);
                for (int index = 0; index < sidebar.Original.Length; index++)
                {
                    var item = sidebar.Original[index];
                    if (!item.Rect) continue;

                    CategoryStyle(item.Rect, selected == index);

                    if (item.Rect.name == "NullFrameAdvanced")
                    {
                        item.Rect.gameObject.SetActive(_world.IsAdmin);
                    }

                    if (item.Rect.gameObject.activeSelf)
                    {
                        ui.SetRow(item.Rect, y, rowHeight);
                        y += rowHeight + 6f;
                    }
                }

                ui.Height(sidebar.Content, y);
            }

            if (_tab >= 0 && (!_volume || !_volume.Panel.gameObject.activeInHierarchy || _tab == 0 && !_world.IsAdmin))
            {
                Close();
            }

            if (_tab >= 0)
            {
                if (_status)
                {
                    _status.text = _world.Status ?? "";
                }

                if (_tab == 0 && Time.unscaledTime >= _rosterAt)
                {
                    _rosterAt = Time.unscaledTime + 0.25f;
                    RefreshPlayers();
                }

                if (_tab == 1)
                {
                    if (_count)
                    {
                        _count.text = _world.Building.States.Count(item => item.Owner == _world.LocalId) + " / " + SandboxMessage.MaxObjectsPerPlayer + " objects";
                    }

                    if (_selection)
                    {
                        _selection.text =                         _world.Building.Editing ? "Selected: " + _world.Building.SelectionCount + " object(s)" :
                        _world.Building.Draft != null ? "Placing: " + (_world.Assets.Find(_world.Building.Draft.Asset)?.Name ?? "object") : "Choose a picture or select a placed object.";
                    }

                    if (_buildingTab == 1 && _draftId != (_world.Building.Draft?.Id ?? -1))
                    {
                        Building();
                    }

                    if (_catalog && _refreshCatalog && Time.unscaledTime >= _refreshAt)
                    {
                        RefreshCatalog();
                    }

                    foreach (var icon in _icons)
                    {
                        if (icon.Image && !icon.Image.texture)
                        {
                            icon.Image.texture = _world.Assets.Icon(icon.Entry);
                        }
                    }
                }

                if (_palette && _wheel)
                {
                    Canvas canvas = _palette.GetComponentInParent<Canvas>();
                    if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                        _wheel,
                        Input.mousePosition,
                        canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera,
                        out var local))
                    {
                        Vector2 point = (local - _wheel.rect.center) / (_wheel.rect.width * 0.5f);
                        if (Input.GetMouseButtonDown(0) && point.sqrMagnitude <= 1f)
                        {
                            _wheelDragging = true;
                        }

                        if (_wheelDragging && Input.GetMouseButton(0))
                        {
                            _hue = Mathf.Repeat(Mathf.Atan2(point.y, point.x) / (Mathf.PI * 2f), 1f);
                            _saturation = Mathf.Clamp01(point.magnitude);
                            PaletteColor();
                        }
                    }

                    if (Input.GetMouseButtonUp(0))
                    {
                        _wheelDragging = false;
                    }
                }
            }

            Hud(_world.Hands.UsingTool && Cursor.lockState == CursorLockMode.Locked);
        }

        private void Hud(bool visible)
        {
            if (!_hudRoot && visible && _volume && _roomMenu)
            {
                _ui ??= new GameUi(_volume);
                _hudRoot = GameUi.Rect("BuildingHud", _roomMenu.transform);
                _hudRoot.SetParent(null, false);

                Canvas canvas = GameUi.Add<Canvas>(_hudRoot.gameObject);
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 150;

                CanvasScaler scaler = GameUi.Add<CanvasScaler>(_hudRoot.gameObject);
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 0.5f;

                RectTransform box = GameUi.Rect("ToolHint", _hudRoot);
                box.anchorMin = box.anchorMax = new Vector2(0.5f, 0f);
                box.pivot = new Vector2(0.5f, 0f);
                box.anchoredPosition = new Vector2(0f, 94f);
                box.sizeDelta = new Vector2(980f, 72f);

                _ui.Image(box, new Color(0.08f, 0.12f, 0.18f, 0.88f), false);
                _hud = _ui.Label(box, "", 21, TextAnchor.MiddleCenter);
                _hud.font = Resources.GetBuiltinResource(Il2CppType.Of<Font>(), "LegacyRuntime.ttf").Cast<Font>();
                _hud.fontSize = 21;
                _hud.color = Color.white;
            }

            if (!_hudRoot || !_hud) return;

            _hudRoot.gameObject.SetActive(visible);
            if (!visible) return;

            _hud.text = _world.Living.Local is { } living ? living.Locked ? "Held by an administrator" : "Press Space to escape: " + living.Escapes + " / 15" :
                _world.Hands.Holding > 0 ? "E: drop  •  LMB: throw  •  R + mouse: rotate  •  Wheel: distance  •  RMB: freeze prop" :
                _world.Building.Selecting ? "SELECT OBJECT  •  Aim at a placed object and left-click  •  Esc: cancel" :
                _world.Building.Editing ? "SELECTED #" + _world.Building.SelectedId + "  •  Z: delete  •  Building → Edit selected  •  Esc: cancel" :
                _world.Building.Placing ? "BUILDING  •  LMB: place  •  Q/E: rotate  •  Shift: pitch  •  Alt: roll  •  RMB: cancel" :
                "EMPTY HANDS  •  LMB: select  •  Shift + LMB: add/remove selection  •  E: carry a nearby object";
        }
    }
}
