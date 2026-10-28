using NullServers.Room;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace NullServers.UI
{
    internal sealed class GameColorPicker
    {
        private readonly RectTransform _root, _wheel, _marker;
        private readonly Canvas _canvas;
        private readonly InputField _hex;
        private readonly Slider _brightness;
        private Action<string> _changed;
        private float _hue, _saturation, _value;
        private bool _dragging;

        internal bool IsOpen => _root && _root.gameObject.activeInHierarchy;

        internal GameColorPicker(GameUi ui, Transform parent)
        {
            _root = GameUi.Rect("ColorPicker", parent);
            ui.Image(_root, new Color(0f, 0f, 0f, 0.45f));
            _canvas = _root.GetComponentInParent(Il2CppType.Of<Canvas>()).Cast<Canvas>();

            RectTransform panel = GameUi.Rect("ColorPanel", _root);
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(480f, 590f) * ui.Unit;
            ui.Image(panel, new Color(0.015f, 0.015f, 0.015f, 0.96f));
            ui.Label(ui.Row(panel, 20f, 44f), "Choose color", 32, TextAnchor.MiddleCenter);

            _wheel = GameUi.Rect("ColorWheel", panel);
            _wheel.anchorMin = _wheel.anchorMax = _wheel.pivot = new Vector2(0.5f, 1f);
            _wheel.anchoredPosition = new Vector2(0f, -80f * ui.Unit);
            _wheel.sizeDelta = new Vector2(300f, 300f) * ui.Unit;
            GameUi.Add<RawImage>(_wheel.gameObject).texture = MenuUi.ColorWheel;

            _marker = GameUi.Rect("ColorCursor", _wheel);
            _marker.anchorMin = _marker.anchorMax = new Vector2(0.5f, 0.5f);
            _marker.sizeDelta = new Vector2(12f, 12f) * ui.Unit;
            ui.Image(_marker, Color.black, false);

            RectTransform brightness = ui.Row(panel, 400f, 30f);
            brightness.offsetMin += new Vector2(50f * ui.Unit, 0f);
            brightness.offsetMax -= new Vector2(50f * ui.Unit, 0f);
            _brightness = ui.Slider(brightness, 1f, value =>
            {
                _value = value;
                Changed();
            });

            _hex = ui.Field(ui.Row(panel, 448f, 54f), 0.25f, 0.75f, "", "#RRGGBB", value =>
            {
                if (!RoomOptions.IsValidColor(value) || !ColorUtility.TryParseHtmlString(value, out var color)) return;

                Color.RGBToHSV(color, out _hue, out _saturation, out _value);
                _brightness.SetValueWithoutNotify(_value);
                Changed(false);
            }, 7);
            ui.RowButton(ui.Row(panel, 518f, 50f), "Done", Close);
            _root.gameObject.SetActive(false);
        }

        internal void Open(string hex, Action<string> changed)
        {
            ColorUtility.TryParseHtmlString(hex, out var color);
            Color.RGBToHSV(color, out _hue, out _saturation, out _value);
            _changed = changed;
            _dragging = false;
            _brightness.SetValueWithoutNotify(_value);
            _root.gameObject.SetActive(true);
            _root.SetAsLastSibling();
            Changed();
        }

        internal void Close()
        {
            _root.gameObject.SetActive(false);
            _dragging = false;
            _changed = null;
        }

        private void Changed(bool updateText = true)
        {
            string hex = "#" + ColorUtility.ToHtmlStringRGB(Color.HSVToRGB(_hue, _saturation, _value));
            if (updateText)
            {
                _hex.SetTextWithoutNotify(hex);
            }

            _marker.anchorMin = _marker.anchorMax = new Vector2(
                Mathf.Cos(_hue * Mathf.PI * 2f),
                Mathf.Sin(_hue * Mathf.PI * 2f)) * (_saturation * 0.5f) + Vector2.one * 0.5f;
            _marker.anchoredPosition = Vector2.zero;
            _changed?.Invoke(hex);
        }

        internal void Update()
        {
            if (!_root.gameObject.activeInHierarchy) return;

            Camera camera = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_wheel, Input.mousePosition, camera, out var local))
            {
                Vector2 point = (local - _wheel.rect.center) / (_wheel.rect.width * 0.5f);
                if (Input.GetMouseButtonDown(0) && point.sqrMagnitude <= 1f)
                {
                    _dragging = true;
                }

                if (_dragging && Input.GetMouseButton(0))
                {
                    _hue = Mathf.Repeat(Mathf.Atan2(point.y, point.x) / (Mathf.PI * 2f), 1f);
                    _saturation = Mathf.Clamp01(point.magnitude);
                    Changed();
                }
            }

            if (Input.GetMouseButtonUp(0))
            {
                _dragging = false;
            }
        }
    }
}
