using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace NullServers.UI
{
    internal sealed class GameChoices
    {
        private readonly GameUi _ui;
        private readonly Transform _parent;
        private RectTransform _root;

        internal bool IsOpen => _root;

        internal GameChoices(GameUi ui, Transform parent)
        {
            _ui = ui;
            _parent = parent;
        }

        internal void Open(string title, IEnumerable<(string Key, string Title)> choices, Action<string> selected)
        {
            Close();
            _root = GameUi.Rect("Choices", _parent);
            _ui.Image(_root, new Color(0f, 0f, 0f, 0.55f));
            RectTransform panel = GameUi.Rect("ChoicePanel", _root);
            GameUi.Area(panel, 0.3f, 0.17f, 0.92f, 0.82f);
            _ui.Image(panel, new Color(0.015f, 0.015f, 0.015f, 0.96f));
            _ui.Label(_ui.Row(panel, 12f, 58f), title, 34, TextAnchor.MiddleCenter);
            _ui.RowButton(_ui.Row(panel, 82f, 54f), "Back", Close);
            ScrollRect scroll = _ui.Scroll(panel, "Choices", 152f, 14f);
            float y = 0f;

            foreach (var choice in choices)
            {
                string key = choice.Key;
                _ui.RowButton(_ui.Row(scroll.content, y, 58f), choice.Title, () =>
                {
                    Close();
                    selected(key);
                });
                y += 70f;
            }

            _ui.Height(scroll.content, Mathf.Max(0f, y - 12f));
            _root.SetAsLastSibling();
        }

        internal void Close()
        {
            if (!_root) return;

            _root.gameObject.SetActive(false);
            Object.Destroy(_root.gameObject);
            _root = null;
        }
    }
}
