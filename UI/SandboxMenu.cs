using Il2Cpp;
using Il2CppInterop.Runtime;
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
    internal sealed partial class SandboxMenu
    {
        private sealed class Sidebar
        {
            internal Volume Volume;
            internal GameUi Ui;
            internal RectTransform Root, Content;
            internal (RectTransform Rect, Transform Parent, Vector2 Min, Vector2 Max, Vector2 Low, Vector2 High)[] Original;
            internal (int Selected, bool Admin, float Height) Layout = (-2, false, -1f);
            internal Image[] Images;
            internal Text[] Labels;
            internal (Color Image, Color Text) Normal = (Color.white, GameUi.Ink), Selected = (new Color(0.48f, 0.48f, 0.48f), Color.white);
            internal bool Open;
        }

        private static SandboxMenu _instance;
        private readonly SandboxWorld _world;
        private readonly Dictionary<IntPtr, int> _categories = new();
        private readonly Dictionary<IntPtr, Sidebar> _sidebars = new();
        private readonly Dictionary<IntPtr, Toggle> _throwToggles = new();
        private readonly Dictionary<IntPtr, (RectTransform Rect, Vector2 Size, Vector2 Position, Vector3 Scale)> _footerOriginal = new();
        private readonly Dictionary<IntPtr, int> _selectedCategories = new();
        private readonly ConsoleTheme _theme = new();
        private readonly Dictionary<IntPtr, Toggle> _consoleToggles = new();
        private float _consoleScanAt;
        private (Volume Volume, int State, float Until) _consolePending;
        private readonly HashSet<IntPtr> _forcedConsoles = new();
        private readonly Dictionary<IntPtr, GameObject> _overlays = new();
        private readonly Dictionary<IntPtr, Button> _removeAll = new();
        private readonly List<Text> _themeLabels = new();
        private bool _themeDirty;
        private int _assetRevision = -1;
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

        internal SandboxMenu(SandboxWorld world)
        {
            _instance = this;
            _world = world;
        }

        private void HideNative(GameObject target)
        {
            if (!target || _hidden.Any(item => item.Object == target)) return;

            _hidden.Add((target, target.activeSelf));
            target.SetActive(false);
        }

        private void Open(Volume volume, int tab)
        {
            if (!_world.Active || tab == 0 && !_world.IsAdmin || tab == 1 && Locked(volume)) return;

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
            _themeDirty = true;
        }

        private bool Locked(Volume volume) => !_world.IsHost && !_world.IsAdmin && volume && volume.ConsoleState?.Value != 1;

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

        internal void Update()
        {
            RoomMultiplayerMenu room = RoomMultiplayerMenu.Current;
            if (room != _roomMenu)
            {
                ResetForNewRoom();
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

                UpdateVolume(volume);
            }

            if (_tab >= 0 && !CanStayOpen())
            {
                Close();
            }

            if (_tab >= 0)
            {
                UpdateOpenTab();
            }

            Hud(_world.Hands.UsingTool && Cursor.lockState == CursorLockMode.Locked);
        }

        private void ResetForNewRoom()
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
                RestoreNativeButtons(sidebar);
            }

            _sidebars.Clear();
            if (_panel)
            {
                Object.Destroy(_panel.gameObject);
            }
        }

        private void UpdateVolume(Volume volume)
        {
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

            if (!_sidebars.TryGetValue(volume.Pointer, out Sidebar sidebar)) return;

            if (_throwToggles.TryGetValue(volume.Pointer, out Toggle toggle) && toggle)
            {
                toggle.interactable = _world.IsHost || _world.IsAdmin;
                toggle.SetIsOnWithoutNotify(_world.Environment.ThrowAnyone);
            }

            LayoutSidebar(sidebar, _selectedCategories.GetValueOrDefault(volume.Pointer));
        }

        private bool CanStayOpen()
        {
            if (!_volume || !_volume.Panel.gameObject.activeInHierarchy) return false;
            if (_tab == 0 && !_world.IsAdmin) return false;
            if (_tab == 1 && Locked(_volume)) return false;

            return true;
        }

        private void UpdateOpenTab()
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
                UpdateBuildingTab();
            }

            if (_palette && _wheel)
            {
                UpdatePaletteWheel();
            }
        }

        internal void LateUpdate()
        {
            if (!_world.Active) return;

            bool input = Input.GetMouseButtonDown(0) || Input.GetMouseButtonUp(0) || Input.anyKeyDown;
            foreach (Sidebar sidebar in _sidebars.Values.ToArray())
            {
                Volume volume = sidebar.Volume;
                if (!volume || !volume.Panel) continue;

                bool open = volume.Panel.gameObject.activeInHierarchy;
                bool opened = open && !sidebar.Open;
                sidebar.Open = open;
                if (!open) continue;

                int selected = _selectedCategories.GetValueOrDefault(volume.Pointer);
                if (opened)
                {
                    RestorePage(volume, selected);
                }
                else
                {
                    SampleCategories(sidebar, selected);
                }

                _theme.Apply(volume.Panel, opened || input || _themeDirty);
            }

            _themeDirty = false;
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
                box.anchoredPosition = new Vector2(0f, 90f);
                box.sizeDelta = new Vector2(1180f, 64f);

                _ui.Image(box, new Color(0f, 0f, 0f, 0.6f), false);
                _hud = _ui.Label(box, "", 30, TextAnchor.MiddleCenter);
                Font slender = Resources.FindObjectsOfTypeAll<Font>().FirstOrDefault(font => font && font.name.Contains("Slender", StringComparison.OrdinalIgnoreCase));
                if (slender)
                {
                    _hud.font = slender;
                }

                _hud.fontSize = 30;
                _hud.color = new Color(0.93f, 0.93f, 0.93f);
                Shadow shadow = GameUi.Add<Shadow>(_hud.gameObject);
                shadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
                shadow.effectDistance = new Vector2(2f, -2f);
            }

            if (!_hudRoot || !_hud) return;

            _hudRoot.gameObject.SetActive(visible);
            if (!visible) return;

            _hud.text = _world.Living.Local is { } living ? living.Locked ? "Held by an administrator" : "Press Space to escape: " + living.Escapes + " / 15" :
                _world.Hands.Holding > 0 ? "E: drop   |   LMB: throw   |   R + mouse: rotate   |   Wheel: distance   |   RMB: freeze prop" :
                _world.Building.Selecting ? "Select object   |   Aim at a placed object and left-click   |   ~: cancel" :
                _world.Building.Editing ? "Selected #" + _world.Building.SelectedId + "   |   Z: delete   |   Building > Edit selected   |   ~: cancel" :
                _world.Building.Placing ? "Building   |   LMB: place   |   Q/E: rotate   |   Shift: pitch   |   Alt: roll   |   RMB: cancel" :
                "Empty hands   |   LMB: select   |   Shift + LMB: add/remove selection   |   E: carry a nearby object";
        }
    }
}
