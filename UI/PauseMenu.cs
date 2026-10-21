using Il2Cpp;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using S3_NullServers.Network;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace S3_NullServers.UI
{
    internal sealed class PauseMenu
    {
        private sealed record Entry(string Label, Action Open, Func<bool> Visible)
        {
            internal Button Button;
        }

        private readonly RelaySession _session;
        private readonly List<Entry> _entries = new();
        private readonly Dictionary<RectTransform, (Vector2 Position, Vector2 Size)> _original = new();
        private Button _settings;
        private RectTransform _resume;
        private RectTransform _quit;
        private CanvasGroup _group;
        private object _owner;
        private float _alpha;
        private bool _interactable;
        private bool _blocksRaycasts;
        private float _nextScan;

        internal RoomMultiplayerMenu Menu
        {
            get; private set;
        }

        internal bool Available => _session.IsNullFrame &&
            !_session.IsSwitching &&
            _session.Relay &&
            _session.Relay.CurrentRoom != null &&
            Menu &&
            Menu.CJNLEDLNGID;

        internal PauseMenu(RelaySession session)
        {
            _session = session;
        }

        internal void Add(string label, Action open, Func<bool> visible) => _entries.Add(new Entry(label, open, visible));

        internal void Update()
        {
            RoomMultiplayerMenu current = RoomMultiplayerMenu.GJDGDIGECJN;
            if (current != Menu ||
                !_session.IsNullFrame ||
                _session.IsSwitching ||
                !_session.Relay ||
                _session.Relay.CurrentRoom == null)
            {
                Restore(_owner);
                foreach (Entry entry in _entries)
                {
                    if (entry.Button)
                    {
                        UnityEngine.Object.Destroy(entry.Button.gameObject);
                    }
                }

                foreach (var pair in _original)
                {
                    if (pair.Key)
                    {
                        pair.Key.anchoredPosition = pair.Value.Position;
                        pair.Key.sizeDelta = pair.Value.Size;
                    }
                }

                _original.Clear();
                _settings = null;
                _group = null;
                Menu = current;
            }

            if (!_session.IsNullFrame ||
                _session.IsSwitching ||
                !current ||
                !_session.Relay ||
                _session.Relay.CurrentRoom == null)
            {
                return;
            }

            if (!_settings)
            {
                if (Time.unscaledTime < _nextScan)
                {
                    return;
                }

                _nextScan = Time.unscaledTime + 1f;
                Button[] buttons = UnityEngine.Object.FindObjectsOfType<Button>(true)
                    .Where(button => button.gameObject.scene == current.gameObject.scene).ToArray();
                _settings = buttons.FirstOrDefault(button => button.gameObject.name == "Settings" ||
                    FindText(button)?.text?.Trim().Equals("Settings", StringComparison.OrdinalIgnoreCase) == true);
                if (!_settings)
                {
                    return;
                }

                Transform parent = _settings.transform.parent;
                _resume = buttons.FirstOrDefault(button => button.transform.parent == parent &&
                    FindText(button)?.text?.Trim().Equals("Resume", StringComparison.OrdinalIgnoreCase) == true)?.transform.Cast<RectTransform>();
                _quit = buttons.FirstOrDefault(button => button.transform.parent == parent &&
                    FindText(button)?.text?.Trim().Equals("Quit", StringComparison.OrdinalIgnoreCase) == true)?.transform.Cast<RectTransform>();
                foreach (RectTransform rect in new[]
                {
                    _resume,
                    _quit,
                    _settings.transform.Cast<RectTransform>()
                })
                {
                    if (rect)
                    {
                        _original[rect] = (rect.anchoredPosition, rect.sizeDelta);
                    }
                }

                _group = parent.GetComponent(Il2CppType.Of<CanvasGroup>())?.Cast<CanvasGroup>() ??
                    parent.gameObject.AddComponent(Il2CppType.Of<CanvasGroup>()).Cast<CanvasGroup>();
                foreach (Entry entry in _entries)
                {
                    entry.Button = CreateButton(_settings, entry);
                }

                Layout();
            }

            bool changed = false;
            foreach (Entry entry in _entries)
            {
                bool visible = entry.Visible();
                if (!entry.Button || entry.Button.gameObject.activeSelf == visible)
                {
                    continue;
                }

                entry.Button.gameObject.SetActive(visible);
                changed = true;
            }

            if (changed)
            {
                Layout();
            }
        }

        private void Layout()
        {
            if (!_resume || !_quit || _settings.transform.parent.GetComponent(Il2CppType.Of<VerticalLayoutGroup>()))
            {
                return;
            }

            List<RectTransform> buttons = new()

            {
                _resume,
                _settings.transform.Cast<RectTransform>()
            };
            buttons.AddRange(_entries.Where(entry => entry.Button && entry.Button.gameObject.activeSelf)
                .Select(entry => entry.Button.transform.Cast<RectTransform>()));
            buttons.Add(_quit);
            Vector2 step = (_original[_quit].Position - _original[_resume].Position) / (buttons.Count - 1);
            for (int index = 0; index < buttons.Count; index++)
            {
                buttons[index].anchoredPosition = _original[_resume].Position + step * index;
                buttons[index].SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Abs(step.y) * 0.9f);
            }
        }

        private static Button CreateButton(Button template, Entry entry)
        {
            GameObject clone = new(
                "NullFrame" + entry.Label.Replace(" ", ""),
                new Il2CppReferenceArray<Il2CppSystem.Type>(
                    new[] { Il2CppType.Of<RectTransform>(), Il2CppType.Of<Button>() }));
            RectTransform sourceRect = template.transform.Cast<RectTransform>();
            RectTransform rect = clone.transform.Cast<RectTransform>();
            rect.SetParent(template.transform.parent, false);
            rect.anchorMin = sourceRect.anchorMin;
            rect.anchorMax = sourceRect.anchorMax;
            rect.pivot = sourceRect.pivot;
            rect.sizeDelta = sourceRect.sizeDelta;
            rect.anchoredPosition = sourceRect.anchoredPosition;
            rect.SetSiblingIndex(template.transform.GetSiblingIndex() + 1);
            Button button = clone.GetComponent(Il2CppType.Of<Button>()).Cast<Button>();
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(DelegateSupport.ConvertDelegate<UnityAction>(entry.Open));
            button.transition = template.transition;
            button.colors = template.colors;
            Text sourceText = FindText(template);
            GameObject label = new(
                "Label",
                new Il2CppReferenceArray<Il2CppSystem.Type>(
                    new[]
            {
                    Il2CppType.Of<RectTransform>(),
                    Il2CppType.Of<CanvasRenderer>(),
                    Il2CppType.Of<Text>()
                }));
            RectTransform labelRect = label.transform.Cast<RectTransform>();
            labelRect.SetParent(rect, false);
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
            Text text = label.GetComponent(Il2CppType.Of<Text>()).Cast<Text>();
            text.font = sourceText.font;
            text.fontSize = sourceText.fontSize;
            text.fontStyle = sourceText.fontStyle;
            text.color = sourceText.color;
            text.alignment = sourceText.alignment;
            text.resizeTextMinSize = sourceText.resizeTextMinSize;
            text.resizeTextMaxSize = sourceText.resizeTextMaxSize;
            text.resizeTextForBestFit = true;
            text.text = entry.Label;
            button.targetGraphic = text;
            clone.SetActive(entry.Visible());
            return button;
        }

        private static Text FindText(Component component) => component.GetComponentInChildren(Il2CppType.Of<Text>(), true)?.Cast<Text>();

        internal bool Hide(object owner)
        {
            if (_owner != null || !_group || !Available || !Menu.IOMIAHNBDOG)
            {
                return false;
            }

            _owner = owner;
            _alpha = _group.alpha;
            _interactable = _group.interactable;
            _blocksRaycasts = _group.blocksRaycasts;
            _group.alpha = 0f;
            _group.interactable = _group.blocksRaycasts = false;
            return true;
        }

        internal void Restore(object owner)
        {
            if (_owner != owner)
            {
                return;
            }

            if (_owner != null && _group)
            {
                _group.alpha = _alpha;
                _group.interactable = _interactable;
                _group.blocksRaycasts = _blocksRaycasts;
            }

            _owner = null;
        }
    }
}
