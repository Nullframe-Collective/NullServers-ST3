using HarmonyLib;
using Il2Cpp;
using Il2CppInterop.Runtime;
using NullServers.Additional;
using NullServers.Content;
using NullServers.Network;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace NullServers.UI
{
    internal sealed class AddonMenu
    {
        private static AddonMenu _instance;
        private readonly RelaySession _session;
        private readonly List<(RectTransform Rect, Vector2 Position)> _shifted = new();
        private Button _button;
        private Text _label;
        private Canvas _canvas;
        private GameUi _ui;
        private RectTransform _root;
        private ScrollRect _catalogScroll;
        private AddonNotice _notice;
        private bool _catalog;
        private string _error;
        private float _nextScan;

        internal AddonMenu(HarmonyLib.Harmony harmony, RelaySession session)
        {
            _instance = this;
            _session = session;

            harmony.Patch(
                AccessTools.Method(typeof(LobbyMenu), nameof(LobbyMenu.OnGUI)),
                prefix: new HarmonyMethod(typeof(AddonMenu), nameof(BeforeLobbyGUI)));

            harmony.Patch(
                AccessTools.Method(typeof(MainMenu), nameof(MainMenu.OnGUI)),
                prefix: new HarmonyMethod(typeof(AddonMenu), nameof(BeforeMainGUI)),
                postfix: new HarmonyMethod(typeof(AddonMenu), nameof(AfterMainGUI)));
        }

        private static bool BeforeLobbyGUI() => _instance._session.AddonNotice == null;

        private static bool BeforeMainGUI() => !_instance._root;

        private static void AfterMainGUI()
        {
            if (_instance._label && _instance._label.text != "ADDONS")
            {
                _instance._label.text = "ADDONS";
            }
        }

        internal void Update()
        {
            if (!_button && Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + 1f;
                Bind();
            }

            if (!_canvas)
            {
                _catalog = false;
                _notice = null;
                return;
            }

            if (_session.AddonNotice != _notice)
            {
                _notice = _session.AddonNotice;

                if (_notice != null)
                {
                    _catalog = false;
                    ShowRequired(_notice);
                }
                else
                {
                    Hide();
                }
            }

            if (_catalog && (!_button || !_button.gameObject.activeInHierarchy))
            {
                Close();
            }
        }

        private void Bind()
        {
            Button source = Object.FindObjectsOfType<Button>().FirstOrDefault(button =>
                button.name == "CreditButton" && button.transform.parent.name == "PCButtons");

            if (!source)
            {
                return;
            }

            _canvas = source.GetComponentInParent(Il2CppType.Of<Canvas>())?.Cast<Canvas>();

            if (!_canvas)
            {
                return;
            }

            _ui = new GameUi(source);
            _shifted.Clear();

            if (!source.transform.parent.GetComponent(Il2CppType.Of<LayoutGroup>()))
            {
                RectTransform credit = source.GetComponent<RectTransform>();
                Transform options = source.transform.parent.Find("OptionGameButton");
                RectTransform option = options ? options.GetComponent<RectTransform>() : null;
                float step = option ? option.anchoredPosition.y - credit.anchoredPosition.y : credit.rect.height;

                if (step <= 0f)
                {
                    return;
                }

                foreach (RectTransform sibling in source.transform.parent.GetComponentsInChildren<RectTransform>(true))
                {
                    if (sibling.parent == source.transform.parent && sibling.GetSiblingIndex() >= source.transform.GetSiblingIndex())
                    {
                        _shifted.Add((sibling, sibling.anchoredPosition));
                    }
                }

                foreach (var entry in _shifted)
                {
                    entry.Rect.anchoredPosition -= new Vector2(0f, step);
                }
            }

            _button = Object.Instantiate(source.gameObject, source.transform.parent, false)
                .GetComponent(Il2CppType.Of<Button>()).Cast<Button>();
            _button.name = "NullFrameAddons";
            _button.transform.SetSiblingIndex(source.transform.GetSiblingIndex());

            if (_shifted.Count > 0)
            {
                _button.GetComponent<RectTransform>().anchoredPosition = _shifted[0].Position;
            }

            _label = _button.GetComponentInChildren(Il2CppType.Of<Text>(), true).Cast<Text>();
            _label.text = "ADDONS";

            foreach (Button button in _button.GetComponentsInChildren<Button>(true))
            {
                for (int index = 0; index < button.onClick.GetPersistentEventCount(); index++)
                {
                    button.onClick.SetPersistentListenerState(index, UnityEngine.Events.UnityEventCallState.Off);
                }

                GameUi.OnClick(button, () =>
                {
                    _catalog = true;
                    _error = null;
                    ShowCatalog();
                });
            }
        }

        private RectTransform Panel(Color background)
        {
            Hide();
            GameObject target = new("NullFrameAddonsPanel");
            target.layer = _canvas.gameObject.layer;
            _root = GameUi.Add<RectTransform>(target);
            Canvas canvas = GameUi.Add<Canvas>(_root.gameObject);
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32760;
            canvas.scaleFactor = _canvas.scaleFactor;

            CanvasScaler original = _canvas.GetComponent<CanvasScaler>();

            if (original)
            {
                CanvasScaler scaler = GameUi.Add<CanvasScaler>(_root.gameObject);
                scaler.uiScaleMode = original.uiScaleMode;
                scaler.referenceResolution = original.referenceResolution;
                scaler.screenMatchMode = original.screenMatchMode;
                scaler.matchWidthOrHeight = original.matchWidthOrHeight;
                scaler.scaleFactor = original.scaleFactor;
                scaler.referencePixelsPerUnit = original.referencePixelsPerUnit;
            }

            GameUi.Add<GraphicRaycaster>(_root.gameObject);
            _ui.Image(_root, background);

            RectTransform body = GameUi.Rect("Body", _root);
            GameUi.Area(body, 0.10f, 0.10f, 0.90f, 0.90f);

            return body;
        }

        private void ShowCatalog()
        {
            float position = _catalogScroll ? _catalogScroll.verticalNormalizedPosition : 1f;
            RectTransform panel = Panel(new Color(0f, 0f, 0f, 0.7f));
            GameUi.Area(panel, 0.12f, 0.12f, 0.88f, 0.88f);
            _ui.Image(panel, new Color(0.025f, 0.03f, 0.045f, 0.98f));
            RectTransform body = GameUi.Rect("Content", panel);
            GameUi.Area(body, 0.04f, 0.04f, 0.96f, 0.96f);
            Text title = _ui.Label(body, "ADDONS", 42);
            GameUi.Area(title.rectTransform, 0f, 0.89f, 1f, 1f);
            Text description = _ui.Label(body, "Manage your official addons. Your selection is saved for the next game.", 24);
            GameUi.Area(description.rectTransform, 0f, 0.80f, 1f, 0.89f);

            AddonRequirement[] installed = Frameworks.Detected;
            ScrollRect scroll = _ui.Scroll(body, "Addons");
            _catalogScroll = scroll;
            GameUi.Area(scroll.GetComponent<RectTransform>(), 0f, 0.20f, 1f, 0.76f);
            float top = 0f;

            foreach (bool present in new[] { true, false })
            {
                AddonRequirement[] addons = Dependencies.Available
                    .Where(addon => installed.Any(local => local.Id == addon.Id) == present).ToArray();

                if (addons.Length == 0) continue;

                RectTransform section = _ui.Row(scroll.content, top, 34f);
                Text heading = _ui.Label(section, present ? "INSTALLED" : "GET MORE ADDONS", 24);
                top += 42f;

                foreach (AddonRequirement addon in addons)
                {
                    CatalogRow(scroll.content, addon, installed.FirstOrDefault(local => local.Id == addon.Id), top);
                    top += 92f;
                }

                top += 12f;
            }

            _ui.Height(scroll.content, top);
            scroll.verticalNormalizedPosition = position;

            Text hint = _ui.Label(body, _error ??
                "Download opens our Discord. Install the files, then restart the game.", 21);
            GameUi.Area(hint.rectTransform, 0f, 0.09f, 1f, 0.15f);
            Button close = _ui.Button(body, "Close", Close);
            GameUi.Area(close.GetComponent<RectTransform>(), 0.76f, 0f, 1f, 0.085f);
        }

        private void CatalogRow(RectTransform body, AddonRequirement addon, AddonRequirement installed, float top)
        {
            RectTransform row = _ui.Row(body, top, 84f);
            _ui.SetRow(row, top, 84f, 0f, 18f);
            _ui.Image(row, new Color(1f, 1f, 1f, 0.035f), false);
            Text name = _ui.Label(row, addon.Name, 28);
            GameUi.Area(name.rectTransform, 0.02f, 0.47f, 0.61f, 1f);
            string state = installed == null ? "Not installed" : "Installed: " + installed.Version;

            if (installed != null && Frameworks.Problem(addon.Id) != null)
            {
                state += "   |   Initialization error (see log)";
            }

            Text status = _ui.Label(row, state, 21);
            GameUi.Area(status.rectTransform, 0.02f, 0f, 0.61f, 0.47f);

            if (installed != null)
            {
                bool available = Frameworks.Installed.Any(local => local.Id == addon.Id);
                bool enabled = Frameworks.Settings.Enabled(addon.Id);
                Button toggle = _ui.Button(row, available ? enabled ? "Enabled" : "Disabled" : "Unavailable", () =>
                {
                    _session.SetAddonEnabled(addon.Id, !enabled, out _error);
                    ShowCatalog();
                });
                toggle.interactable = available;
                GameUi.Select(toggle, available && enabled);
                GameUi.Area(toggle.GetComponent<RectTransform>(), 0.80f, 0.22f, 0.98f, 0.78f);
            }

            if (installed == null)
            {
                Button download = _ui.Button(row, "Download",
                    () => Application.OpenURL(Dependencies.DownloadUrl));
                GameUi.Area(download.GetComponent<RectTransform>(), 0.82f, 0.22f, 0.98f, 0.78f);
            }
        }

        private void ShowRequired(AddonNotice notice)
        {
            RectTransform body = Panel(new Color(0.025f, 0.03f, 0.045f, 1f));
            Text title = _ui.Label(body, notice.Title, 42, TextAnchor.MiddleCenter);
            GameUi.Area(title.rectTransform, 0f, 0.87f, 1f, 1f);

            string message = notice.Error ?? "This server requires addons you do not have. Download the missing addons to join?";
            Text description = _ui.Label(body, message, 27);
            GameUi.Area(description.rectTransform, 0f, 0.69f, 1f, 0.85f);
            GameUi.Fit(description, Mathf.RoundToInt(16f * _ui.Unit), description.fontSize);
            description.verticalOverflow = VerticalWrapMode.Truncate;

            ScrollRect scroll = _ui.Scroll(body, "Requirements");
            GameUi.Area(scroll.GetComponent<RectTransform>(), 0f, 0.29f, 1f, 0.65f);
            float top = 0f;
            AddonPack[] packs = notice.Packs ?? Array.Empty<AddonPack>();
            AddonRequirement[] list = notice.Missing.Length > 0 ? notice.Missing :
                packs.Length == 0 ? notice.Required : Array.Empty<AddonRequirement>();

            foreach (AddonRequirement addon in list)
            {
                AddonRequirement installed = Frameworks.Detected.FirstOrDefault(local => local.Id == addon.Id);
                string status = installed == null ? "Not installed" :
                    "Installed: " + installed.Version + (Frameworks.Settings.Enabled(addon.Id) ? "" : " (disabled)");
                RequiredRow(scroll.content, top, addon.Name, "Host version: " + addon.Version, status);
                top += 118f;
            }

            foreach (AddonPack pack in packs)
            {
                RequiredRow(scroll.content, top, pack.Name, pack.Path + "\n" + pack.Id, pack.Status);
                top += 118f;
            }

            _ui.Height(scroll.content, top);
            scroll.verticalNormalizedPosition = 1f;

            bool fetch = ContentDownloads.CanFetch(notice) && _session.LastRoomId != null && _session.Downloads != null;
            string help = fetch
                ? "Download fetches these packs from the host and checks them before they load. Checks reduce the risk, but cannot guarantee that host content is safe."
                : notice.Kind switch
            {
                AddonNoticeKind.Disabled => "Open ADDONS in the main menu and enable the required addon.",
                AddonNoticeKind.Unavailable => "The DLL is installed. Repair the required pack named above, then restart the game.",
                AddonNoticeKind.Room => "Refresh the room list and try again. Both players should use the current NullServers build.",
                AddonNoticeKind.Content => "Download the missing or different packs from our Discord, then restart. Unused local packs are disabled for this room.",
                _ => "Download opens our Discord server. Install the addons and restart the game."
            };
            Text hint = _ui.Label(body, help, 23);
            GameUi.Area(hint.rectTransform, 0f, 0.14f, 1f, 0.28f);
            Button close = _ui.Button(body, "Close", Close);
            GameUi.Area(close.GetComponent<RectTransform>(), notice.Download ? 0f : 0.25f, 0f, notice.Download ? 0.49f : 0.75f, 0.10f);

            if (notice.Download)
            {
                Button download = _ui.Button(body, "Download", fetch ? () => Fetch(notice) : () => Application.OpenURL(Dependencies.DownloadUrl));
                GameUi.Area(download.GetComponent<RectTransform>(), 0.51f, 0f, 1f, 0.10f);
            }
        }

        private void Fetch(AddonNotice notice)
        {
            string room = _session.LastRoomId;
            _session.Downloads.Consent(room, notice.Packs);
            Close();

            NetBootstrap bootstrap = NetBootstrap.Current;
            if (!bootstrap || !bootstrap.JoinRoom(room))
            {
                _session.ShowAddonError("Could not join the room to download its packs. Refresh the room list and try again.", AddonNoticeKind.Room);
            }
        }

        private void RequiredRow(RectTransform parent, float top, string name, string detail, string status)
        {
            RectTransform row = _ui.Row(parent, top, 106f);
            _ui.SetRow(row, top, 84f, 0f, 18f);
            _ui.Image(row, new Color(1f, 1f, 1f, 0.035f), false);
            Text title = _ui.Label(row, name, 28);
            GameUi.Area(title.rectTransform, 0.02f, 0.55f, 0.64f, 1f);
            GameUi.Fit(title, Mathf.RoundToInt(16f * _ui.Unit), title.fontSize);
            title.verticalOverflow = VerticalWrapMode.Truncate;
            Text path = _ui.Label(row, detail, 19);
            GameUi.Area(path.rectTransform, 0.02f, 0.02f, 0.70f, 0.55f);
            GameUi.Fit(path, Mathf.RoundToInt(12f * _ui.Unit), path.fontSize);
            path.verticalOverflow = VerticalWrapMode.Truncate;
            Text state = _ui.Label(row, status, 23, TextAnchor.MiddleRight);
            GameUi.Area(state.rectTransform, 0.72f, 0.08f, 0.98f, 0.92f);
        }

        private void Hide()
        {
            if (_root)
            {
                _root.gameObject.SetActive(false);
                Object.Destroy(_root.gameObject);
            }

            _root = null;
        }

        private void Close()
        {
            _session.AddonNotice = null;
            _notice = null;
            _catalog = false;
            Hide();
        }

        internal void Dispose()
        {
            Close();

            if (_button)
            {
                _button.gameObject.SetActive(false);
                Object.Destroy(_button.gameObject);
            }

            _button = null;
            _label = null;

            foreach (var entry in _shifted)
            {
                if (entry.Rect)
                {
                    entry.Rect.anchoredPosition = entry.Position;
                }
            }

            _shifted.Clear();
        }
    }
}
