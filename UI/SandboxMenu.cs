using HarmonyLib;
using Il2Cpp;
using Il2CppInterop.Runtime;
using S3_NullServers.Customization;
using S3_NullServers.Network;
using S3_NullServers.Players;
using S3_NullServers.Room;
using S3_NullServers.Sandbox;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace S3_NullServers.UI
{
    internal sealed class SandboxMenu
    {
        private sealed class Sidebar
        {
            internal Volume Volume;
            internal SandboxUi Ui;
            internal RectTransform Root, Content;
            internal (RectTransform Rect, Transform Parent, Vector2 Min, Vector2 Max, Vector2 Low, Vector2 High)[] Original;
        }

        private static SandboxMenu _instance;
        private readonly SandboxWorld _world;
        private readonly Dictionary<IntPtr, int> _categories = new();
        private readonly Dictionary<IntPtr, Sidebar> _sidebars = new();
        private readonly List<(GameObject Object, bool Active)> _hidden = new();
        private readonly List<(RawImage Image, SandboxAssets.Entry Entry)> _icons = new();
        private readonly List<(Button Button, PlayerCommand Command, string Name)> _actions = new();
        private Volume _volume;
        private RoomMultiplayerMenu _roomMenu;
        private SandboxUi _ui;
        private RectTransform _panel, _body, _catalog, _players, _palette, _wheel, _marker, _hudRoot;
        private Text _status, _selection, _count, _hud;
        private InputField[] _position, _size, _angles;
        private InputField _hex;
        private Button _colorButton, _physics;
        private Texture2D _wheelTexture;
        private string _color = "#FFFFFF", _texture = "", _search = "", _roster;
        private bool _dynamic, _refreshCatalog, _wheelDragging;
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
                AccessTools.Method(typeof(UI_Button), nameof(UI_Button.SendToVolume)),
                prefix: new HarmonyMethod(typeof(SandboxMenu), nameof(BeforeNativeButton)));
            harmony.Patch(
                AccessTools.Method(typeof(WeaponScript), "Update"),
                prefix: new HarmonyMethod(typeof(SandboxMenu), nameof(BeforeWeapon)));
            harmony.Patch(
                AccessTools.Method(typeof(WeaponManager), nameof(WeaponManager.Update)),
                prefix: new HarmonyMethod(typeof(SandboxMenu), nameof(BeforeWeapon)));
        }

        private static void BeforeCategories(Volume __instance)
        {
            SandboxMenu menu = _instance;
            if (!menu._world.Active || __instance.PDKPIOHFCCK == null)
            {
                return;
            }

            for (int index = 0; index + 1 < __instance.PDKPIOHFCCK.Count; index++)
            {
                if (__instance.PDKPIOHFCCK[index].catagoryName == "Advanced" &&
                    __instance.PDKPIOHFCCK[index + 1].catagoryName == "Building")
                {
                    menu._categories[__instance.Pointer] = index;
                    return;
                }
            }

            menu._categories[__instance.Pointer] = __instance.PDKPIOHFCCK.Count;
            foreach (string name in new[] { "Advanced", "Building" })
            {
                __instance.PDKPIOHFCCK.Add(new Volume.catagory

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
            if (!menu._categories.TryGetValue(__instance.Pointer, out var first) || __instance.AIJBBOEEOIE == null)
            {
                return;
            }

            for (int tab = 0; tab < 2; tab++)
            {
                if (first + tab >= __instance.AIJBBOEEOIE.Count)
                {
                    continue;
                }

                RectTransform rect = __instance.AIJBBOEEOIE[first + tab];
                rect.name = tab == 0 ? "NullFrameAdvanced" : "NullFrameBuilding";
                UI_Button native = rect.GetComponent(Il2CppType.Of<UI_Button>())?.Cast<UI_Button>();
                if (native)
                {
                    native.enabled = false;
                }

                Button button = rect.GetComponent(Il2CppType.Of<Button>())?.Cast<Button>();
                if (!button)
                {
                    continue;
                }

                int selected = tab;
                button.onClick = new Button.ButtonClickedEvent();
                button.onClick.AddListener(DelegateSupport.ConvertDelegate<UnityEngine.Events.UnityAction>((Action)(() => menu.Open(__instance, selected))));
            }

            if (menu._sidebars.ContainsKey(__instance.Pointer))
            {
                return;
            }

            SandboxUi ui = new(__instance);
            RectTransform holder = SandboxUi.Rect("Categories", __instance.GGNOPAOFGDO);
            SandboxUi.Area(holder, 0.025f, 0.105f, 0.225f, 0.88f);
            ScrollRect scroll = ui.Scroll(holder, "CategoryScroll");
            var original = __instance.AIJBBOEEOIE.ToArray().Select(rect => (rect, rect.parent, rect.anchorMin, rect.anchorMax, rect.offsetMin, rect.offsetMax)).ToArray();
            foreach (RectTransform rect in __instance.AIJBBOEEOIE)
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

        private static bool BeforeNativeButton(UI_Button __instance)
        {
            if (__instance.name.StartsWith("NullFrame", StringComparison.Ordinal))
            {
                return false;
            }

            if (_instance._tab >= 0 && __instance.COIGCEOALIN == _instance._volume)
            {
                _instance.Close();
            }

            return true;
        }

        private static bool BeforeWeapon(Component __instance)
        {
            if (!_instance._world.Active)
            {
                return true;
            }

            if (!__instance.GetComponentInParent(Il2CppType.Of<FPScontroller>()))
            {
                return true;
            }

            if (!_instance._world.Gun.Equipped)
            {
                return true;
            }

            WeaponScript weapon = __instance.TryCast<WeaponScript>();
            if (weapon && weapon.isActiveAndEnabled)
            {
                weapon.transform.localPosition = weapon.KKCIIEDNLEO;
                weapon.BJKNIGHPNHG();
            }

            return false;
        }

        private void HideNative(GameObject target)
        {
            if (!target || _hidden.Any(item => item.Object == target))
            {
                return;
            }

            _hidden.Add((target, target.activeSelf));
            target.SetActive(false);
        }

        private void Open(Volume volume, int tab)
        {
            if (!_world.Active || tab == 0 && !_world.IsAdmin)
            {
                return;
            }

            Close();
            _volume = volume;
            _tab = tab;
            _ui = new SandboxUi(volume);
            if (volume.KDABMADBABK != null)
            {
                foreach (RectTransform rect in volume.KDABMADBABK)
                {
                    if (rect)
                    {
                        HideNative(rect.gameObject);
                    }
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

            _panel = SandboxUi.Rect("SandboxPanel", volume.GGNOPAOFGDO);
            SandboxUi.Area(_panel, 0.29f, 0.13f, 0.95f, 0.88f);
            _ui.Image(_panel, new Color(0.94f, 0.95f, 0.97f, 0.98f));
            Text title = _ui.Label(_panel, tab == 0 ? "Advanced" : "Building", 32);
            title.fontStyle = FontStyle.Bold;
            _ui.Row(title.rectTransform, 8f, 48f, 18f, 160f);
            Button back = _ui.Button(_panel, "Back", Close);
            RectTransform backRect = back.GetComponent<RectTransform>();
            SandboxUi.Area(backRect, 0.87f, 1f, 1f, 1f);
            backRect.offsetMin = new Vector2(0f, -52 * _ui.Unit);
            backRect.offsetMax = new Vector2(-12, -8) * _ui.Unit;
            _status = _ui.Label(_panel, "", 19);
            SandboxUi.Area(_status.rectTransform, 0f, 0f, 1f, 0f);
            _status.rectTransform.offsetMin = new Vector2(18f, 7f) * _ui.Unit;
            _status.rectTransform.offsetMax = new Vector2(-18, 56f) * _ui.Unit;
            _body = SandboxUi.Rect("Page", _panel);
            _body.offsetMin = new Vector2(16f, 64f) * _ui.Unit;
            _body.offsetMax = new Vector2(-16, -66) * _ui.Unit;
            _world.Assets.Capture();
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
                if (item.Object)
                {
                    item.Object.SetActive(item.Active);
                }
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

        private void CloseConsole()
        {
            Close();
            if (!_volume)
            {
                return;
            }

            _volume.EGNKPPJHIBA = false;
            _volume.GGNOPAOFGDO.gameObject.SetActive(false);
            UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private RectTransform Row(Transform parent, float top, float height = 46f)
        {
            RectTransform rect = SandboxUi.Rect("Row", parent);
            _ui.Row(rect, top, height);
            return rect;
        }

        private Button Button(
            Transform parent,
            string label,
            Action action,
            float left = 0f,
            float right = 1f,
            Color? color = null)
        {
            Button button = _ui.Button(parent, label, action, color);
            SandboxUi.Area(button.GetComponent<RectTransform>(), left, 0f, right, 1f, 3f * _ui.Unit);
            return button;
        }

        private void Advanced()
        {
            RectTransform left = SandboxUi.Rect("PlayerColumn", _body);
            SandboxUi.Area(left, 0f, 0f, 0.29f, 1f, 3f * _ui.Unit);
            Text title = _ui.Label(left, "Players", 23);
            _ui.Row(title.rectTransform, 0f, 38f);
            ScrollRect list = _ui.Scroll(left, "Players");
            list.GetComponent<RectTransform>().offsetMax = new Vector2(0f, -44 * _ui.Unit);
            _players = list.content;
            _roster = null;
            _rosterAt = 0f;
            RectTransform right = SandboxUi.Rect("Actions", _body);
            SandboxUi.Area(right, 0.31f, 0f, 1f, 1f);
            RectTransform content = _ui.Scroll(right, "Actions").content;
            _ui.Label(Row(content, 0f), "PLAYER ACTIONS", 22).fontStyle = FontStyle.Bold;
            RectTransform scaleRow = Row(content, 50f);
            Text scaleLabel = _ui.Label(scaleRow, "Size (0.2–5)");
            SandboxUi.Area(scaleLabel.rectTransform, 0f, 0f, 0.38f, 1f);
            InputField scale = _ui.Input(scaleRow, "1", "1", _ =>
            {
            }, 8);
            SandboxUi.Area(scale.GetComponent<RectTransform>(), 0.4f, 0f, 0.68f, 1f, 3f * _ui.Unit);
            Button(scaleRow, "Apply", () =>
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
                RectTransform row = Row(content, 106 + index / 2 * 56, 50f);
                float edge = index % 2 * 0.5f;
                Button button = Button(
                    row,
                    action.Item1,
                    () => _world.Admin(action.Item2, _target),
                    edge,
                    edge + 0.5f,
                    action.Item2 is PlayerCommand.Kill or PlayerCommand.Remove ? new Color(1f, 0.84f, 0.82f) : null);
                _actions.Add((button, action.Item2, action.Item1));
            }

            RectTransform rename = Row(content, 456f);
            InputField name = _ui.Input(rename, "", "New nickname", _ =>
            {
            }, PlayerProfile.NameLimit);
            SandboxUi.Area(name.GetComponent<RectTransform>(), 0f, 0f, 0.65f, 1f, 3f * _ui.Unit);
            Button(rename, "Rename", () => _world.Admin(PlayerCommand.Rename, _target, text: name.text), 0.67f);
            Button(
                Row(content, 522f),
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
                SandboxUi.Clear(_players);
                if (!players.Any(player => player.Id == _target))
                {
                    _target = players.FirstOrDefault()?.Id ?? 0;
                }

                float y = 0f;
                foreach (RoomPlayer player in players)
                {
                    int id = player.Id;
                    Button button = _ui.Button(_players, player.Nickname + (id == 0 ? " (Host)" : ""), () =>
                    {
                        _target = id;
                        _roster = null;
                    },
                        id == _target ? new Color(0.72f, 0.86f, 0.97f) : Color.white);
                    _ui.Row(button.GetComponent<RectTransform>(), y, 62f);
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
                action.Button.GetComponentInChildren(Il2CppType.Of<Text>(), true).Cast<Text>().text = action.Name +
                    (action.Command is PlayerCommand.Fly or PlayerCommand.Freeze or PlayerCommand.Invincible ? enabled ? ": ON" : ": OFF" : "");
            }
        }

        private void Building()
        {
            SandboxUi.Clear(_body);
            _icons.Clear();
            _catalog = null;
            if (_palette)
            {
                Object.Destroy(_palette.gameObject);
            }

            _palette = null;
            RectTransform tabs = Row(_body, 0f, 48f);
            Button(tabs, "Objects", () =>
            {
                _buildingTab = 0;
                Building();
            }, 0f, 0.23f, _buildingTab == 0 ? new Color(0.75f, 0.87f, 0.97f) : null);
            Button(tabs, "Edit selected", () =>
            {
                _buildingTab = 1;
                Building();
            }, 0.24f, 0.47f, _buildingTab == 1 ? new Color(0.75f, 0.87f, 0.97f) : null);
            _count = _ui.Label(tabs, "", 19, TextAnchor.MiddleRight);
            SandboxUi.Area(_count.rectTransform, 0.5f, 0f, 1f, 1f);
            RectTransform tools = Row(_body, 58f);
            Button(tools, "Equip Phys Gun [F6]", () =>
            {
                _world.Gun.Equip(true);
                _world.Building.Stop();
                CloseConsole();
            }, 0f, 0.42f);
            Button(tools, "Undo", () => _world.Building.Undo(), 0.43f, 0.69f);
            Button(tools, "Return to weapon", () =>
            {
                _world.Gun.Equip(false);
                Building();
            }, 0.7f);
            _selection = _ui.Label(Row(_body, 114f, 40f), "", 20);
            _selection.color = SandboxUi.Blue;
            if (_buildingTab == 0)
            {
                Catalog();
            }
            else
            {
                Editor();
            }
        }

        private void Catalog()
        {
            RectTransform row = Row(_body, 158f);
            InputField search = _ui.Input(row, _search, "Search objects...", value =>
            {
                _search = value;
                _refreshCatalog = true;
                _refreshAt = Time.unscaledTime + 0.15f;
            }, 80);
            SandboxUi.Area(search.GetComponent<RectTransform>(), 0f, 0f, 0.5f, 1f, 3f * _ui.Unit);
            string[] names = new[]
            {
                "All",
                "Shapes",
                "Props",
                "Recent"
            };
            for (int index = 0; index < names.Length; index++)
            {
                int filter = index;
                Button(row, names[index], () =>
                {
                    _filter = filter;
                    _refreshCatalog = true;
                    _refreshAt = 0f;
                }, 0.51f + index * 0.1225f, 0.51f + (index + 1) * 0.1225f);
            }

            ScrollRect scroll = _ui.Scroll(_body, "ObjectCatalog");
            scroll.GetComponent<RectTransform>().offsetMin = new Vector2(0f, 44f * _ui.Unit);
            scroll.GetComponent<RectTransform>().offsetMax = new Vector2(0f, -216 * _ui.Unit);
            _catalog = scroll.content;
            Text help = _ui.Label(
                _body,
                "Phys Gun: hold LMB to grab. E + mouse: rotate (Shift: roll). RMB: freeze. R: throw. F6: weapon.",
                19);
            SandboxUi.Area(help.rectTransform, 0f, 0f, 1f, 0f);
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

            SandboxUi.Clear(_catalog);
            _icons.Clear();
            _refreshCatalog = false;
            var source = _filter == 3 ? _world.Building.Recent.Select(id => _world.Assets.Find(id)).Where(entry => entry != null) : _world.Assets.Entries;
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
                RectTransform iconRect = SandboxUi.Rect("Thumbnail", rect);
                SandboxUi.Area(iconRect, 0.07f, 0.27f, 0.93f, 0.96f);
                if (entry.Icon)
                {
                    Image icon = _ui.Image(iconRect, Color.white, false);
                    icon.sprite = entry.Icon;
                    icon.preserveAspect = true;
                }
                else
                {
                    RawImage icon = SandboxUi.Add<RawImage>(iconRect.gameObject);
                    icon.raycastTarget = false;
                    icon.texture = _world.Assets.Icon(entry);
                    _icons.Add((icon, entry));
                }

                Text caption = _ui.Label(rect, entry.Name, 17, TextAnchor.MiddleCenter);
                SandboxUi.Area(caption.rectTransform, 0.04f, 0.02f, 0.96f, 0.26f);
            }

            _ui.Height(_catalog, Mathf.Ceil(entries.Length / (float)columns) * cellHeight / _ui.Unit);
            if (entries.Length == 0)
            {
                _ui.Label(Row(_catalog, 0f, 65f), "No objects found. Try another category or search.");
            }
        }

        private InputField[] VectorFields(Transform parent, float y, string title, Point3 value)
        {
            RectTransform row = Row(parent, y, 58f);
            Text titleText = _ui.Label(row, title);
            SandboxUi.Area(titleText.rectTransform, 0f, 0f, 0.25f, 1f);
            InputField[] result = new InputField[3];
            float[] values = new[]
            {
                value.X,
                value.Y,
                value.Z
            };
            for (int index = 0; index < 3; index++)
            {
                RectTransform axis = SandboxUi.Rect("Axis", row);
                SandboxUi.Area(axis, 0.26f + index * 0.245f, 0f, 0.495f + index * 0.245f, 1f, 3f * _ui.Unit);
                Text label = _ui.Label(axis, "XYZ"[index].ToString(), 18);
                SandboxUi.Area(label.rectTransform, 0f, 0f, 0.14f, 1f);
                InputField input = _ui.Input(
                    axis,
                    values[index].ToString("0.###", CultureInfo.InvariantCulture),
                    "0",
                    _ => Preview(),
                    12);
                SandboxUi.Area(input.GetComponent<RectTransform>(), 0.16f, 0f, 1f, 1f);
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
                    Row(_body, 180f, 90f),
                    "Double-click a placed object in the world to select it. A blue frame confirms your selection. Z deletes the selected object.",
                    23);
                return;
            }

            ScrollRect scroll = _ui.Scroll(_body, "ObjectEditor");
            scroll.GetComponent<RectTransform>().offsetMax = new Vector2(0f, -168 * _ui.Unit);
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
            RectTransform row = Row(content, 224f, 54f);
            Text label = _ui.Label(row, "Color");
            SandboxUi.Area(label.rectTransform, 0f, 0f, 0.25f, 1f);
            _colorButton = Button(row, "", Palette, 0.26f, 0.34f);
            _hex = _ui.Input(row, _color, "#FFFFFF", value =>
            {
                if (RoomOptions.IsValidColor(value))
                {
                    _color = value;
                    Preview();
                }
            }, 7);
            SandboxUi.Area(_hex.GetComponent<RectTransform>(), 0.36f, 0f, 0.67f, 1f, 3f * _ui.Unit);
            _physics = Button(row, "", () =>
            {
                _dynamic = !_dynamic;
                Preview();
            }, 0.7f);
            RectTransform textureRow = Row(content, 294f, 54f);
            Text textureLabel = _ui.Label(textureRow, "Texture URL");
            SandboxUi.Area(textureLabel.rectTransform, 0f, 0f, 0.25f, 1f);
            InputField texture = _ui.Input(textureRow, _texture, "https://.../texture.png", value =>
            {
                _texture = value;
                Preview();
            }, Appearance.UrlLimit);
            SandboxUi.Area(texture.GetComponent<RectTransform>(), 0.26f, 0f, 1f, 1f, 3f * _ui.Unit);
            RectTransform controls = Row(content, 374f, 54f);
            Button(controls, "Apply changes", () =>
            {
                Preview();
                _world.Building.Confirm();
                Building();
            }, 0f, 0.49f, new Color(0.74f, 0.9f, 0.79f));
            Button(controls, "Delete object", () =>
            {
                _world.Building.DeleteSelected();
                Building();
            }, 0.51f, 1f, new Color(1f, 0.83f, 0.8f));
            _ui.Label(
                Row(content, 444f, 60f),
                "The blue frame marks your selection. Apply changes to update it for everyone.",
                19);
            _ui.Height(content, 510f);
            Preview();
        }

        private static bool Number(string value, out float number) => float.TryParse(
            value.Replace(',', '.'),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out number) &&
            float.IsFinite(number);

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
                    _dynamic);
            }

            if (_colorButton && ColorUtility.TryParseHtmlString(_color, out var color))
            {
                _colorButton.GetComponent<Image>().color = color;
            }

            if (_physics)
            {
                _physics.GetComponentInChildren(Il2CppType.Of<Text>(), true).Cast<Text>().text = "Physics: " + (_dynamic ? "ON" : "OFF");
            }
        }

        private void Palette()
        {
            if (_palette)
            {
                Object.Destroy(_palette.gameObject);
            }

            _palette = SandboxUi.Rect("ColorPalette", _panel);
            _ui.Image(_palette, new Color(0.7f, 0.74f, 0.8f, 0.8f));
            RectTransform box = SandboxUi.Rect("PalettePanel", _palette);
            box.anchorMin = box.anchorMax = box.pivot = new Vector2(0.5f, 0.5f);
            box.sizeDelta = new Vector2(360f, 440f) * _ui.Unit;
            _ui.Image(box, new Color(0.96f, 0.97f, 0.98f));
            _ui.Label(Row(box, 12f, 38f), "Object color", 25, TextAnchor.MiddleCenter);
            _wheel = SandboxUi.Rect("ColorWheel", box);
            _wheel.anchorMin = _wheel.anchorMax = new Vector2(0.5f, 1f);
            _wheel.pivot = new Vector2(0.5f, 1f);
            _wheel.anchoredPosition = new Vector2(0f, -58 * _ui.Unit);
            _wheel.sizeDelta = new Vector2(270f, 270f) * _ui.Unit;
            if (!_wheelTexture)
            {
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

                _wheelTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
                _wheelTexture.SetPixels(pixels);
                _wheelTexture.Apply(false, true);
            }

            SandboxUi.Add<RawImage>(_wheel.gameObject).texture = _wheelTexture;
            _marker = SandboxUi.Rect("ColorCursor", _wheel);
            _marker.sizeDelta = new Vector2(10f, 10f) * _ui.Unit;
            _ui.Image(_marker, SandboxUi.Ink, false);
            ColorUtility.TryParseHtmlString(_color, out var selected);
            Color.RGBToHSV(selected, out _hue, out _saturation, out _brightness);
            Slider slider = _ui.Slider(Row(box, 342f, 20f), _brightness, value =>
            {
                _brightness = value;
                PaletteColor();
            });
            slider.GetComponent<RectTransform>().offsetMin += new Vector2(24f * _ui.Unit, 0f);
            slider.GetComponent<RectTransform>().offsetMax -= new Vector2(24f * _ui.Unit, 0f);
            Button(Row(box, 382f, 44f), "Done", () =>
            {
                Object.Destroy(_palette.gameObject);
                _palette = null;
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
            Preview();
        }

        internal void Update()
        {
            RoomMultiplayerMenu room = RoomMultiplayerMenu.GJDGDIGECJN;
            if (room != _roomMenu)
            {
                Close();
                _categories.Clear();
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
                Close();
                if (_hudRoot)
                {
                    _hudRoot.gameObject.SetActive(false);
                }

                return;
            }

            foreach (Volume volume in Object.FindObjectsOfType<Volume>())
            {
                if (!_volume)
                {
                    _volume = volume;
                }

                if (!_categories.ContainsKey(volume.Pointer) && volume.ONPICMKGDHB && volume.PDKPIOHFCCK != null)
                {
                    BeforeCategories(volume);
                    if (!_categories.ContainsKey(volume.Pointer))
                    {
                        continue;
                    }

                    if (volume.AIJBBOEEOIE != null)
                    {
                        foreach (RectTransform rect in volume.AIJBBOEEOIE)
                        {
                            if (rect)
                            {
                                volume.FIKDALMIMAB?.Remove(rect);
                                Object.Destroy(rect.gameObject);
                            }
                        }
                    }

                    volume.CreateCatagoryButtons();
                }

                if (!_sidebars.TryGetValue(volume.Pointer, out var sidebar))
                {
                    continue;
                }

                SandboxUi ui = sidebar.Ui;
                float y = 0f;
                int visible = sidebar.Original.Count(item => item.Rect &&
                    (item.Rect.name != "NullFrameAdvanced" || _world.IsAdmin));
                float rowHeight = Mathf.Clamp(sidebar.Root.rect.height / ui.Unit / Mathf.Max(1, visible) - 6f, 46f, 76f);
                foreach (var item in sidebar.Original)
                {
                    if (!item.Rect)
                    {
                        continue;
                    }

                    if (item.Rect.name == "NullFrameAdvanced")
                    {
                        item.Rect.gameObject.SetActive(_world.IsAdmin);
                    }

                    if (item.Rect.gameObject.activeSelf)
                    {
                        ui.Row(item.Rect, y, rowHeight);
                        y += rowHeight + 6f;
                    }
                }

                ui.Height(sidebar.Content, y);
            }

            if (_tab >= 0 && (!_volume || !_volume.GGNOPAOFGDO.gameObject.activeInHierarchy || _tab == 0 && !_world.IsAdmin))
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
                        _selection.text = _world.Building.Editing ? "Selected: " + (_world.Assets.Find(_world.Building.Draft.Asset)?.Name ?? "object") + " #" + _world.Building.SelectedId :
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

            Hud(_world.Gun.Equipped && Cursor.lockState == CursorLockMode.Locked);
        }

        private void Hud(bool visible)
        {
            if (!_hudRoot && visible && _volume && _roomMenu)
            {
                _ui ??= new SandboxUi(_volume);
                _hudRoot = SandboxUi.Rect("BuildingHud", _roomMenu.transform);
                Canvas canvas = SandboxUi.Add<Canvas>(_hudRoot.gameObject);
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 150;
                CanvasScaler scaler = SandboxUi.Add<CanvasScaler>(_hudRoot.gameObject);
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 0.5f;
                RectTransform box = SandboxUi.Rect("ToolHint", _hudRoot);
                box.anchorMin = box.anchorMax = new Vector2(0.5f, 0f);
                box.pivot = new Vector2(0.5f, 0f);
                box.anchoredPosition = new Vector2(0f, 94f);
                box.sizeDelta = new Vector2(980f, 72f);
                _ui.Image(box, new Color(0.08f, 0.12f, 0.18f, 0.88f), false);
                _hud = _ui.Label(box, "", 21, TextAnchor.MiddleCenter);
                _hud.color = Color.white;
            }

            if (!_hudRoot || !_hud)
            {
                return;
            }

            _hudRoot.gameObject.SetActive(visible);
            if (!visible)
            {
                return;
            }

            _hud.text = _world.Gun.Holding > 0 ? "PHYS GUN  •  E + mouse: rotate  •  Shift + E: roll  •  Wheel: distance  •  RMB: freeze  •  R: throw" :
                _world.Building.Selecting ? "SELECT OBJECT  •  Aim at a placed object and left-click  •  Esc: cancel" :
                _world.Building.Editing ? "SELECTED #" + _world.Building.SelectedId + "  •  Z: delete  •  Building → Edit selected  •  Esc: cancel" :
                _world.Building.Placing ? "BUILDING  •  LMB: place  •  Q/E: rotate  •  Shift: pitch  •  Alt: roll  •  RMB: cancel" :
                "PHYS GUN  •  Hold LMB: grab  •  Double-click: edit  •  Z: delete aimed prop  •  F6: return to weapon";
        }
    }
}
