using Il2Cpp;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace S3_NullServers.UI
{
    internal sealed class SandboxUi
    {
        internal static readonly Color Ink = new(0.19f, 0.22f, 0.26f);
        internal static readonly Color Blue = new(0.18f, 0.42f, 0.64f);
        internal readonly float Unit;
        private readonly Font _font;
        private readonly Sprite _buttonSprite;

        internal SandboxUi(Volume volume)
        {
            Text text = volume.ONPICMKGDHB.GetComponentInChildren(Il2CppType.Of<Text>(), true)?.Cast<Text>();
            _font = text ? text.font : Resources.GetBuiltinResource(Il2CppType.Of<Font>(), "LegacyRuntime.ttf").Cast<Font>();
            _buttonSprite = volume.ONPICMKGDHB.GetComponent(Il2CppType.Of<Image>())?.Cast<Image>()?.sprite;
            Unit = Mathf.Max(0.5f, volume.GGNOPAOFGDO.rect.height / 1080f);
        }

        internal static T Add<T>(GameObject target) where T : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
        {
            return target.AddComponent(Il2CppType.Of<T>()).Cast<T>();
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

        internal void Row(RectTransform rect, float top, float height, float left = 0f, float right = 0f)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.offsetMin = new Vector2(left * Unit, -(top + height) * Unit);
            rect.offsetMax = new Vector2(-right * Unit, -top * Unit);
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
            text.fontSize = Mathf.RoundToInt(size * Unit);
            text.alignment = alignment;
            text.color = Ink;
            text.supportRichText = false;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        internal Button Button(Transform parent, string value, Action clicked, Color? tint = null)
        {
            RectTransform rect = Rect("Button", parent);
            Image image = Image(rect, tint ?? Color.white);
            image.sprite = _buttonSprite;
            image.type = UnityEngine.UI.Image.Type.Sliced;
            Button button = Add<Button>(rect.gameObject);
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.87f, 0.93f, 0.98f);
            colors.pressedColor = new Color(0.67f, 0.81f, 0.92f);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = new Color(0.75f, 0.75f, 0.75f, 0.55f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            Navigation navigation = button.navigation;
            navigation.mode = Navigation.Mode.None;
            button.navigation = navigation;
            Text label = Label(rect, value, 23, TextAnchor.MiddleCenter);
            label.rectTransform.offsetMin = new Vector2(8f, 2f) * Unit;
            label.rectTransform.offsetMax = new Vector2(-8, -2) * Unit;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = Mathf.RoundToInt(14f * Unit);
            label.resizeTextMaxSize = Mathf.RoundToInt(23f * Unit);
            button.onClick.AddListener(DelegateSupport.ConvertDelegate<UnityAction>(clicked));
            return button;
        }

        internal InputField Input(Transform parent, string value, string placeholder, Action<string> changed, int limit = 64)
        {
            RectTransform rect = Rect("Input", parent);
            Image image = Image(rect, Color.white);
            image.sprite = _buttonSprite;
            image.type = UnityEngine.UI.Image.Type.Sliced;
            InputField input = Add<InputField>(rect.gameObject);
            input.targetGraphic = image;
            Text label = Label(rect, value, 22);
            label.rectTransform.offsetMin = new Vector2(10f, 2f) * Unit;
            label.rectTransform.offsetMax = new Vector2(-10, -2) * Unit;
            input.textComponent = label;
            Text hint = Label(rect, placeholder, 21);
            hint.color = new Color(0.46f, 0.49f, 0.53f);
            hint.rectTransform.offsetMin = new Vector2(10f, 2f) * Unit;
            hint.rectTransform.offsetMax = new Vector2(-10, -2) * Unit;
            input.placeholder = hint;
            input.characterLimit = limit;
            input.lineType = InputField.LineType.SingleLine;
            input.text = value ?? "";
            input.onValueChanged.AddListener(DelegateSupport.ConvertDelegate<UnityAction<string>>(changed));
            return input;
        }

        internal ScrollRect Scroll(RectTransform parent, string name)
        {
            RectTransform rect = Rect(name, parent);
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
            content.sizeDelta = Vector2.zero;
            scroll.viewport = viewport;
            scroll.content = content;
            RectTransform track = Rect("Scrollbar", rect);
            Area(track, 1f, 0f, 1f, 1f);
            track.offsetMin = new Vector2(-12 * Unit, 0f);
            track.offsetMax = Vector2.zero;
            Image(track, new Color(0.79f, 0.81f, 0.84f));
            RectTransform handle = Rect("Handle", track);
            Image handleImage = Image(handle, new Color(0.43f, 0.47f, 0.52f));
            Scrollbar bar = Add<Scrollbar>(track.gameObject);
            bar.targetGraphic = handleImage;
            bar.handleRect = handle;
            bar.direction = Scrollbar.Direction.BottomToTop;
            scroll.verticalScrollbar = bar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            return scroll;
        }

        internal void Height(RectTransform rect, float height) => rect.sizeDelta = new Vector2(rect.sizeDelta.x, height * Unit);

        internal Slider Slider(Transform parent, float value, Action<float> changed)
        {
            RectTransform rect = Rect("Slider", parent);
            Image(rect, new Color(0.75f, 0.78f, 0.81f));
            RectTransform handle = Rect("Handle", rect);
            Image(handle, Blue);
            handle.sizeDelta = new Vector2(16f * Unit, 0f);
            Slider slider = Add<Slider>(rect.gameObject);
            slider.handleRect = handle;
            slider.targetGraphic = handle.GetComponent(Il2CppType.Of<Image>()).Cast<Image>();
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = value;
            slider.onValueChanged.AddListener(DelegateSupport.ConvertDelegate<UnityAction<float>>(changed));
            return slider;
        }
    }
}
