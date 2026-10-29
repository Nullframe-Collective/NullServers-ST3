using Il2Cpp;
using Il2CppInterop.Runtime;
using NullServers.Room;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace NullServers.UI
{
    internal sealed class GameUi
    {
        internal static readonly Color Ink = new(0.19f, 0.22f, 0.26f);
        internal static readonly Color Blue = new(0.18f, 0.42f, 0.64f);
        internal readonly float Unit;
        private readonly Font _font;
        private readonly Sprite _buttonSprite;
        private readonly Button _buttonTemplate;
        private readonly bool _styled;
        private readonly Slider _sliderTemplate;
        private readonly Color _textColor = Ink;

        internal GameUi(Volume volume)
        {
            Text text = volume.ButtonTemplate.GetComponentInChildren(Il2CppType.Of<Text>(), true)?.Cast<Text>();
            _font = text ? text.font : Resources.GetBuiltinResource(Il2CppType.Of<Font>(), "LegacyRuntime.ttf").Cast<Font>();
            _buttonSprite = volume.ButtonTemplate.GetComponent(Il2CppType.Of<Image>())?.Cast<Image>()?.sprite;
            Unit = Mathf.Max(0.5f, volume.Panel.rect.height / 1080f);
        }

        internal GameUi(Button button, float? unit = null)
        {
            _buttonTemplate = button;
            _styled = true;
            _font = button.GetComponentInChildren(Il2CppType.Of<Text>(), true).Cast<Text>().font;
            _textColor = Color.white;
            _sliderTemplate = button.transform.parent.parent.GetComponentsInChildren<Slider>(true)
                .FirstOrDefault(slider => slider.fillRect && slider.handleRect);
            Canvas canvas = button.GetComponentInParent(Il2CppType.Of<Canvas>(), true).Cast<Canvas>();
            Unit = unit ?? canvas.GetComponent<RectTransform>().rect.height / 1080f;
        }

        internal static T Add<T>(GameObject target) where T : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
        {
            return target.AddComponent(Il2CppType.Of<T>()).Cast<T>();
        }

        internal static T Create<T>(string name, Transform parent) where T : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
        {
            GameObject target = new(name);
            Add<RectTransform>(target).SetParent(parent, false);
            return Add<T>(target);
        }

        internal static RectTransform Rect(string name, Transform parent)
        {
            GameObject target = new("NullFrame" + name);
            target.layer = parent.gameObject.layer;
            RectTransform rect = Add<RectTransform>(target);
            rect.SetParent(parent, false);
            Area(rect, 0f, 0f, 1f, 1f);

            return rect;
        }

        internal static void Area(RectTransform rect, float left, float bottom, float right, float top, float inset = 0f)
        {
            rect.anchorMin = new Vector2(left, bottom);
            rect.anchorMax = new Vector2(right, top);
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
            rect.localScale = Vector3.one;
        }

        internal void SetRow(RectTransform rect, float top, float height, float left = 0f, float right = 0f)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.offsetMin = new Vector2(left * Unit, -(top + height) * Unit);
            rect.offsetMax = new Vector2(-right * Unit, -top * Unit);
        }

        internal RectTransform Row(Transform parent, float top, float height = 46f)
        {
            RectTransform rect = Rect("Row", parent);
            SetRow(rect, top, height);
            return rect;
        }

        private void Inset(Text text, float horizontal, float vertical)
        {
            Vector2 inset = new Vector2(horizontal, vertical) * Unit;
            text.rectTransform.offsetMin = inset;
            text.rectTransform.offsetMax = -inset;
        }

        internal static void Fit(Text text, int minimum, int maximum)
        {
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = minimum;
            text.resizeTextMaxSize = maximum;
        }

        internal static void OnClick(Button button, Action clicked)
        {
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(DelegateSupport.ConvertDelegate<UnityAction>(clicked));
        }

        internal static void Tint(Button button, string hex)
        {
            if (button && ColorUtility.TryParseHtmlString(hex, out var color))
            {
                button.targetGraphic.color = color;
            }
        }

        internal static void Clear(RectTransform parent)
        {
            for (int index = parent.childCount - 1; index >= 0; index--)
            {
                GameObject child = parent.GetChild(index).gameObject;
                child.SetActive(false);
                Object.Destroy(child);
            }
        }

        internal Image Image(RectTransform rect, Color color, bool raycast = true)
        {
            Image image = Add<Image>(rect.gameObject);
            image.color = color;
            image.raycastTarget = raycast;

            return image;
        }

        internal Text Label(Transform parent, string value, int size = 22, TextAnchor alignment = TextAnchor.MiddleLeft)
        {
            Text text = Add<Text>(Rect("Label", parent).gameObject);
            text.font = _font;
            text.text = value;
            text.fontSize = Mathf.RoundToInt(size * Unit * (_styled ? 1.25f : 1f));
            text.alignment = alignment;
            text.color = _textColor;
            text.supportRichText = false;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = _styled ? VerticalWrapMode.Overflow : VerticalWrapMode.Truncate;

            return text;
        }

        internal Button Button(Transform parent, string value, Action clicked, Color? tint = null)
        {
            Button button = Control<Button>("Button", parent, _styled ? Color.white : tint ?? Color.white);
            if (_buttonTemplate)
            {
                Frame(button);
                Select(button, false);
            }
            else
            {
                ColorBlock colors = button.colors;
                colors.normalColor = Color.white;
                colors.highlightedColor = new Color(0.87f, 0.93f, 0.98f);
                colors.pressedColor = new Color(0.67f, 0.81f, 0.92f);
                colors.selectedColor = colors.highlightedColor;
                colors.disabledColor = new Color(0.75f, 0.75f, 0.75f, 0.55f);
                colors.fadeDuration = 0.08f;
                button.colors = colors;
            }

            Navigation navigation = button.navigation;
            navigation.mode = Navigation.Mode.None;
            button.navigation = navigation;
            int size = _styled ? 38 : 23;
            Text label = Label(button.transform, value, size, TextAnchor.MiddleCenter);
            Inset(label, 8f, 2f);
            Fit(label, Mathf.RoundToInt((_styled ? 26f : 14f) * Unit), Mathf.RoundToInt(size * Unit));
            OnClick(button, clicked);

            return button;
        }

        internal void Frame(Selectable control)
        {
            for (int index = 0; index < 4; index++)
            {
                RectTransform edge = Rect("Border", control.transform);
                if (index < 2)
                {
                    edge.anchorMin = new Vector2(0f, index);
                    edge.anchorMax = new Vector2(1f, index);
                    edge.sizeDelta = new Vector2(0f, Unit);
                    edge.anchoredPosition = new Vector2(0f, index == 0 ? Unit * 0.5f : -Unit * 0.5f);
                }
                else
                {
                    edge.anchorMin = new Vector2(index - 2, 0f);
                    edge.anchorMax = new Vector2(index - 2, 1f);
                    edge.sizeDelta = new Vector2(Unit, 0f);
                    edge.anchoredPosition = new Vector2(index == 2 ? Unit * 0.5f : -Unit * 0.5f, 0f);
                }

                Image(edge, new Color(1f, 1f, 1f, 0.22f), false);
            }
        }

        internal static void Select(Button button, bool selected)
        {
            ColorBlock colors = button.colors;
            colors.normalColor = colors.selectedColor = selected
                ? new Color(0f, 0f, 0f, 0.52f)
                : new Color(0f, 0f, 0f, 0.25f);
            colors.highlightedColor = new Color(0f, 0f, 0f, 0.48f);
            colors.pressedColor = new Color(0f, 0f, 0f, 0.65f);
            colors.disabledColor = new Color(0f, 0f, 0f, 0.12f);
            colors.fadeDuration = 0.1f;
            button.colors = colors;
            Text label = button.GetComponentInChildren(Il2CppType.Of<Text>(), true)?.Cast<Text>();

            if (label)
            {
                label.color = selected ? Color.red : Color.white;
            }
        }

        internal Button RowButton(
            Transform parent,
            string value,
            Action clicked,
            float left = 0f,
            float right = 1f,
            Color? color = null)
        {
            Button button = Button(parent, value, clicked, color);
            Area(button.GetComponent<RectTransform>(), left, 0f, right, 1f, 3f * Unit);
            return button;
        }

        private T Control<T>(string name, Transform parent, Color color) where T : Selectable
        {
            RectTransform rect = Rect(name, parent);
            Image image = Image(rect, color);
            image.sprite = _buttonSprite;
            image.type = UnityEngine.UI.Image.Type.Sliced;
            T control = Add<T>(rect.gameObject);
            control.targetGraphic = image;

            return control;
        }

        internal InputField Input(Transform parent, string value, string placeholder, Action<string> changed = null, int limit = 64)
        {
            InputField input = Control<InputField>("Input", parent, Color.white);
            if (_buttonTemplate)
            {
                Frame(input);
                ColorBlock colors = input.colors;
                colors.normalColor = new Color(0f, 0f, 0f, 0.28f);
                colors.highlightedColor = new Color(0f, 0f, 0f, 0.42f);
                colors.selectedColor = colors.pressedColor = new Color(0f, 0f, 0f, 0.55f);
                input.colors = colors;
            }

            Text label = Label(input.transform, value, _styled ? 27 : 22);
            Inset(label, 10f, 2f);
            input.textComponent = label;
            Text hint = Label(input.transform, placeholder, _styled ? 27 : 21);
            hint.color = _styled ? new Color(1f, 1f, 1f, 0.4f) : new Color(0.46f, 0.49f, 0.53f);
            Inset(hint, 10f, 2f);
            input.placeholder = hint;
            input.characterLimit = limit;
            input.lineType = InputField.LineType.SingleLine;
            input.text = value ?? "";

            if (changed != null)
            {
                input.onValueChanged.AddListener(DelegateSupport.ConvertDelegate<UnityAction<string>>(changed));
            }

            return input;
        }

        internal void Caption(Transform parent, string title)
        {
            Text label = Label(parent, title, 28);
            Area(label.rectTransform, 0f, 0f, 0.43f, 1f);
        }

        internal InputField ColorField(Transform parent, string value, GameColorPicker picker, Action<string> changed)
        {
            Image swatch = null;
            void Change(string hex)
            {
                changed(hex);
                if (swatch && ColorUtility.TryParseHtmlString(hex, out var color))
                {
                    swatch.color = color;
                }
            }

            InputField input = Field(parent, 0.46f, 0.74f, value, "#RRGGBB", Change, 7);
            Button button = RowButton(parent, "Choose", () =>
                picker.Open(RoomOptions.IsValidColor(input.text) ? input.text : "#FFFFFF", hex =>
                {
                    input.SetTextWithoutNotify(hex);
                    Change(hex);
                }), 0.76f);
            RectTransform chip = Rect("Swatch", button.transform);
            Area(chip, 0.06f, 0.25f, 0.22f, 0.75f);
            swatch = Image(chip, ColorUtility.TryParseHtmlString(value, out var initial) ? initial : Color.white, false);
            Text label = button.GetComponentInChildren(Il2CppType.Of<Text>(), true).Cast<Text>();
            Area(label.rectTransform, 0.26f, 0f, 1f, 1f, 2f * Unit);
            return input;
        }

        internal Slider Range(Transform parent, string title, float value, float minimum, float maximum, Action<float> changed)
        {
            Text label = Label(parent, title + ": " + value.ToString("0.###"), 26);
            Area(label.rectTransform, 0f, 0f, 0.5f, 1f);
            Slider slider = Slider(parent, value, current =>
            {
                changed(current);
                label.text = title + ": " + current.ToString("0.###");
            }, minimum, maximum);
            Area(slider.GetComponent<RectTransform>(), 0.56f, 0.2f, 0.98f, 0.8f);
            return slider;
        }

        internal InputField Field(
            Transform parent,
            float left,
            float right,
            string value,
            string placeholder,
            Action<string> changed = null,
            int limit = 64)
        {
            InputField input = Input(parent, value, placeholder, changed, limit);
            Area(input.GetComponent<RectTransform>(), left, 0f, right, 1f, 3f * Unit);
            return input;
        }

        internal ScrollRect Scroll(RectTransform parent, string name, float top = 0f, float bottom = 0f)
        {
            RectTransform rect = Rect(name, parent);
            rect.offsetMin = new Vector2(0f, bottom * Unit);
            rect.offsetMax = new Vector2(0f, -top * Unit);
            ScrollRect scroll = Add<ScrollRect>(rect.gameObject);
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 42f * Unit;

            RectTransform viewport = Rect("Viewport", rect);
            viewport.offsetMax = new Vector2(-18 * Unit, 0f);
            Image(viewport, new Color(1f, 1f, 1f, 0.005f));
            Add<RectMask2D>(viewport.gameObject);

            RectTransform content = Rect("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = Vector2.one;
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = new Vector2(-4f * Unit, 0f);
            scroll.viewport = viewport;
            scroll.content = content;

            RectTransform track = Rect("Scrollbar", rect);
            Area(track, 1f, 0f, 1f, 1f);
            track.offsetMin = new Vector2(-12 * Unit, 0f);
            track.offsetMax = Vector2.zero;
            Image(track, _styled ? new Color(1f, 1f, 1f, 0.08f) : new Color(0.79f, 0.81f, 0.84f));

            RectTransform handle = Rect("Handle", track);
            Image handleImage = Image(handle, _styled ? new Color(0.8f, 0.83f, 0.87f, 0.75f) : new Color(0.43f, 0.47f, 0.52f));

            Scrollbar bar = Add<Scrollbar>(track.gameObject);
            bar.targetGraphic = handleImage;
            bar.handleRect = handle;
            bar.direction = Scrollbar.Direction.BottomToTop;
            scroll.verticalScrollbar = bar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

            return scroll;
        }

        internal void Height(RectTransform rect, float height) => rect.sizeDelta = new Vector2(rect.sizeDelta.x, height * Unit);

        internal Slider Slider(Transform parent, float value, Action<float> changed, float minimum = 0f, float maximum = 1f)
        {
            if (_sliderTemplate)
            {
                Slider copy = Object.Instantiate(_sliderTemplate.gameObject, parent).GetComponent<Slider>();
                copy.name = "NullFrameSlider";
                copy.onValueChanged = new Slider.SliderEvent();
                copy.minValue = minimum;
                copy.maxValue = maximum;
                copy.wholeNumbers = false;
                copy.SetValueWithoutNotify(value);
                Area(copy.GetComponent<RectTransform>(), 0f, 0f, 1f, 1f);
                RectTransform knob = copy.handleRect;
                RectTransform area = knob.parent.Cast<RectTransform>();
                area.anchorMin = new Vector2(area.anchorMin.x, 0.5f);
                area.anchorMax = new Vector2(area.anchorMax.x, 0.5f);
                area.sizeDelta = new Vector2(area.sizeDelta.x, 0f);
                area.anchoredPosition = new Vector2(area.anchoredPosition.x, 0f);
                knob.sizeDelta = Vector2.one * (36f * Unit);
                knob.anchoredPosition = new Vector2(knob.anchoredPosition.x, 0f);
                copy.onValueChanged.AddListener(DelegateSupport.ConvertDelegate<UnityAction<float>>(changed));
                copy.gameObject.SetActive(true);

                return copy;
            }

            RectTransform rect = Rect("Slider", parent);
            Image(rect, new Color(0.75f, 0.78f, 0.81f));

            RectTransform handle = Rect("Handle", rect);
            Image handleImage = Image(handle, Blue);
            handle.sizeDelta = new Vector2(16f * Unit, 0f);

            Slider slider = Add<Slider>(rect.gameObject);
            slider.handleRect = handle;
            slider.targetGraphic = handleImage;
            slider.minValue = minimum;
            slider.maxValue = maximum;
            slider.value = value;
            slider.onValueChanged.AddListener(DelegateSupport.ConvertDelegate<UnityAction<float>>(changed));

            return slider;
        }
    }
}
