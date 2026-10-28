using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using NullServers.Additional;
using NullServers.Network;
using NullServers.Server;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace NullServers.UI
{
    internal sealed class CommunityMenu : IDisposable
    {
        private sealed record Notice(string Title, string Text, string Button, Action Accepted);
        private static CommunityMenu _instance;
        private readonly RelaySession _session;
        private readonly MelonLogger.Instance _logger;
        private readonly CommunityPreferences _preferences = new(key => PlayerPrefs.GetString(key, ""), PlayerPrefs.SetString, PlayerPrefs.Save);
        private readonly List<Text> _blocks = new();
        private Canvas _canvas;
        private GameUi _ui;
        private RectTransform _root;
        private ScrollRect _scroll;
        private Notice _notice;
        private Vector2 _screen;
        private CancellationTokenSource _updatesCancellation;
        private Action _continue;
        private int _generation;
        private bool _entering;
        private float _scanAt;

        internal bool Visible => _root;

        internal CommunityMenu(HarmonyLib.Harmony harmony, RelaySession session, MelonLogger.Instance logger)
        {
            _instance = this;
            _session = session;
            _logger = logger;
            session.AccessDenied += Denied;

            foreach (Type type in new[] { typeof(MainMenu), typeof(LobbyMenu) })
            {
                harmony.Patch(AccessTools.Method(type, "OnGUI"), prefix: new HarmonyMethod(typeof(CommunityMenu), nameof(BeforeGUI)));
            }
        }

        private static bool BeforeGUI() => !_instance.Visible;

        internal void Enter(Action enter)
        {
            if (_entering || _notice != null)
            {
                return;
            }

            _entering = true;
            _continue = enter;
            _session.CommunityAccepted = false;
            MelonCoroutines.Start(Check(++_generation));
        }

        private IEnumerator Check(int generation)
        {
            _session.Blacklist.Refresh();
            _updatesCancellation = new CancellationTokenSource();
            Task<string> updates = CommunityText.Download(CommunityText.UpdatesUrl, _updatesCancellation.Token);
            bool rulesAccepted = _preferences.Next(null) != CommunityPrompt.Rules;
            void Waiting() => Show(new Notice("NullFrame Servers", "Checking community access and looking for updates...", "Cancel", Cancel));

            if (!rulesAccepted)
            {
                Show(new Notice("Rules", CommunityDocument.Rules, "OK", () =>
                {
                    Acknowledge(CommunityPrompt.Rules, null);
                    rulesAccepted = true;
                    Waiting();
                }));
            }
            else
            {
                Waiting();
            }

            while (_session.Blacklist.Updating || !updates.IsCompleted || !rulesAccepted)
            {
                if (generation != _generation)
                {
                    _ = updates.ContinueWith(task => { _ = task.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                    yield break;
                }

                yield return null;
            }

            if (generation != _generation)
            {
                yield break;
            }

            _updatesCancellation.Dispose();
            _updatesCancellation = null;

            if (!_session.Blacklist.Ready)
            {
                Denied(GlobalBlacklist.UnavailableMessage);
                yield break;
            }

            if (_session.Blacklist.Contains(_session.Relay?.LocalIdentity?.Username))
            {
                Denied(GlobalBlacklist.BannedMessage);
                yield break;
            }

            string text = null;
            try
            {
                text = updates.GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                _logger.Warning("Community update log could not be loaded: " + exception.Message);
            }

            if (_preferences.Next(text) == CommunityPrompt.Updates)
            {
                Show(new Notice("Update Log", text, "OK", () =>
                {
                    Acknowledge(CommunityPrompt.Updates, text);
                    Finish();
                }));
            }
            else
            {
                Finish();
            }
        }

        private void Acknowledge(CommunityPrompt prompt, string text)
        {
            try
            {
                _preferences.Acknowledge(prompt, text);
            }
            catch (Exception exception)
            {
                _logger.Warning("Community preferences could not be saved: " + exception.Message);
            }
        }

        private void Finish()
        {
            Action enter = _continue;
            _continue = null;
            _entering = false;
            Hide();
            _session.CommunityAccepted = true;
            enter?.Invoke();
        }

        private void Cancel()
        {
            _generation++;
            _entering = false;
            _continue = null;
            _updatesCancellation?.Cancel();
            _updatesCancellation?.Dispose();
            _updatesCancellation = null;
            Hide();
        }

        private void Denied(string reason)
        {
            Cancel();
            _session.CommunityAccepted = false;
            Show(new Notice(reason == GlobalBlacklist.BannedMessage ? "Access denied" : "Connection unavailable", reason, "OK", () =>
            {
                Hide();
                MainMenu menu = Object.FindObjectOfType<MainMenu>();

                if (menu)
                {
                    menu.ShowLobby();
                }
            }));
        }

        private void Show(Notice notice)
        {
            Hide();
            _notice = notice;
            Build();
        }

        private bool Bind()
        {
            if (_canvas)
            {
                return true;
            }

            Button source = Object.FindObjectsOfType<Button>(true).FirstOrDefault(button => button.name == "CreditButton" && button.transform.parent.name == "PCButtons");
            if (!source)
            {
                return false;
            }

            _canvas = source.GetComponentInParent<Canvas>();
            _ui = new GameUi(source);
            return _canvas;
        }

        private void Build()
        {
            if (_notice == null || !Bind())
            {
                return;
            }

            GameObject target = new("NullFrameCommunityPanel") { layer = _canvas.gameObject.layer };
            _root = GameUi.Add<RectTransform>(target);
            Canvas canvas = GameUi.Add<Canvas>(target);
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32766;
            canvas.scaleFactor = _canvas.scaleFactor;
            CanvasScaler original = _canvas.GetComponent<CanvasScaler>();

            if (original)
            {
                CanvasScaler scaler = GameUi.Add<CanvasScaler>(target);
                scaler.uiScaleMode = original.uiScaleMode;
                scaler.referenceResolution = original.referenceResolution;
                scaler.screenMatchMode = original.screenMatchMode;
                scaler.matchWidthOrHeight = original.matchWidthOrHeight;
                scaler.scaleFactor = original.scaleFactor;
                scaler.referencePixelsPerUnit = original.referencePixelsPerUnit;
            }

            GameUi.Add<GraphicRaycaster>(target);
            _ui.Image(_root, new Color(0f, 0f, 0f, 0.84f));
            RectTransform card = GameUi.Rect("CommunityCard", _root);
            GameUi.Area(card, 0.11f, 0.10f, 0.89f, 0.90f);
            _ui.Image(card, new Color(0.025f, 0.03f, 0.045f, 1f));
            RectTransform body = GameUi.Rect("CommunityBody", card);
            GameUi.Area(body, 0.045f, 0.045f, 0.955f, 0.955f);
            Text brand = _ui.Label(body, "NULLFRAME COLLECTIVE", 21);
            brand.color = new Color(0.8f, 0.16f, 0.2f);
            GameUi.Area(brand.rectTransform, 0f, 0.92f, 1f, 1f);
            Text title = _ui.Label(body, _notice.Title, 42);
            GameUi.Area(title.rectTransform, 0f, 0.80f, 1f, 0.93f);
            RectTransform line = GameUi.Rect("CommunityDivider", body);
            GameUi.Area(line, 0f, 0.78f, 1f, 0.783f);
            _ui.Image(line, new Color(1f, 1f, 1f, 0.18f), false);
            _scroll = _ui.Scroll(body, "CommunityDocument");
            GameUi.Area(_scroll.GetComponent<RectTransform>(), 0f, 0.16f, 1f, 0.74f);

            foreach (string block in CommunityDocument.Blocks(_notice.Text))
            {
                Text label = _ui.Label(_scroll.content, block, 27, TextAnchor.UpperLeft);
                label.verticalOverflow = VerticalWrapMode.Truncate;
                _blocks.Add(label);
            }

            Text footer = _ui.Label(body, "NULLFRAME SERVERS", 20);
            footer.color = new Color(1f, 1f, 1f, 0.45f);
            GameUi.Area(footer.rectTransform, 0f, 0f, 0.60f, 0.10f);
            Button button = _ui.Button(body, _notice.Button, _notice.Accepted);
            GameUi.Area(button.GetComponent<RectTransform>(), 0.70f, 0f, 1f, 0.11f);
            Layout();
            _scroll.verticalNormalizedPosition = 1f;

            if (EventSystem.current)
            {
                EventSystem.current.SetSelectedGameObject(button.gameObject);
            }
        }

        private void Layout()
        {
            foreach (Text label in _blocks)
            {
                _ui.SetRow(label.rectTransform, 0f, 1f, 0f, 8f);
            }

            Canvas.ForceUpdateCanvases();
            float top = 0f;

            foreach (Text label in _blocks)
            {
                float height = Mathf.Max(36f, label.preferredHeight / _ui.Unit + 8f);
                _ui.SetRow(label.rectTransform, top, height, 0f, 8f);
                top += height;
            }

            _ui.Height(_scroll.content, top);
            _screen = new Vector2(Screen.width, Screen.height);
        }

        internal void Update()
        {
            if (_notice != null && !_root && Time.unscaledTime >= _scanAt)
            {
                _scanAt = Time.unscaledTime + 0.5f;
                _blocks.Clear();
                Build();
            }

            if (_root && _screen != new Vector2(Screen.width, Screen.height))
            {
                Layout();
            }
        }

        private void Hide()
        {
            if (_root)
            {
                _root.gameObject.SetActive(false);
                Object.Destroy(_root.gameObject);
            }

            _root = null;
            _scroll = null;
            _notice = null;
            _blocks.Clear();
        }

        public void Dispose()
        {
            Cancel();
            _session.AccessDenied -= Denied;
        }
    }
}
