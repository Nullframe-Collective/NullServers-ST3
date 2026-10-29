using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

namespace NullServers.UI
{
    internal sealed class ConsoleTheme
    {
        private sealed class Item
        {
            internal Graphic Graphic;
            internal Image Image;
            internal bool Text, Skip, Active;
            internal Color Original, Applied;
        }

        private static readonly MelonPreferences_Category Preferences = MelonPreferences.CreateCategory("NullServers");
        private static readonly MelonPreferences_Entry<bool> Saved = Preferences.CreateEntry("ConsoleDarkMode", false);

        private readonly Dictionary<IntPtr, Item> _items = new();
        private readonly List<Item> _live = new();
        private readonly Dictionary<Color, Color> _sources = new();
        private int _rescans;
        private Transform _root;
        private float _scanAt;

        internal bool Dark { get; private set; } = Saved.Value;

        internal void Toggle()
        {
            Dark = !Dark;
            Saved.Value = Dark;
            Preferences.SaveToFile(false);
            _scanAt = 0f;

            if (!Dark)
            {
                Restore();
            }
        }

        internal void Apply(Transform root, bool rescan = false)
        {
            if (!Dark || !root) return;

            if (rescan)
            {
                _rescans = 3;
            }

            if (_rescans > 0 || root != _root || Time.unscaledTime >= _scanAt)
            {
                _rescans = Math.Max(0, _rescans - 1);
                Scan(root);
            }

            foreach (Item item in _live)
            {
                Paint(item);
            }
        }

        internal Color Original(Graphic graphic)
        {
            Color current = graphic.color;
            return _items.TryGetValue(graphic.Pointer, out var item) && item.Active && item.Applied == current ? item.Original : current;
        }

        private void Scan(Transform root)
        {
            _root = root;
            _scanAt = Time.unscaledTime + 0.25f;
            _live.Clear();

            foreach (Graphic graphic in root.GetComponentsInChildren<Graphic>(false))
            {
                if (!graphic) continue;

                if (!_items.TryGetValue(graphic.Pointer, out var item) || !item.Graphic)
                {
                    Image image = graphic.TryCast<Image>();
                    _items[graphic.Pointer] = item = new Item
                    {
                        Graphic = graphic,
                        Image = image,
                        Skip = graphic.TryCast<RawImage>() != null,
                        Text = !image && graphic.TryCast<Text>() != null
                    };
                }

                if (item.Skip) continue;

                if (item.Image && !Themed(item.Image))
                {
                    Color current = graphic.color;
                    if (item.Active && current == item.Applied)
                    {
                        graphic.color = item.Original;
                    }
                    else if (_sources.TryGetValue(current, out var source))
                    {
                        graphic.color = source;
                    }

                    item.Active = false;
                    continue;
                }

                _live.Add(item);
            }

            if (_sources.Count > 4096)
            {
                _sources.Clear();
            }

            if (_items.Count > _live.Count * 2 + 512)
            {
                foreach (IntPtr dead in _items.Where(pair => !pair.Value.Graphic).Select(pair => pair.Key).ToArray())
                {
                    _items.Remove(dead);
                }
            }
        }

        private void Paint(Item item)
        {
            Graphic graphic = item.Graphic;
            if (!graphic) return;

            Color current = graphic.color;
            if (item.Active && current == item.Applied) return;

            if (!item.Active && _sources.TryGetValue(current, out var source))
            {
                item.Original = source;
                item.Applied = current;
                item.Active = true;
                return;
            }

            item.Original = current;
            item.Applied = item.Text ? Light(current) : Dim(current);
            item.Active = true;

            if (item.Applied != current)
            {
                _sources[item.Applied] = current;
                graphic.color = item.Applied;
            }
        }

        internal void Restore()
        {
            foreach (Item item in _items.Values)
            {
                if (item.Active && item.Graphic && item.Graphic.color == item.Applied)
                {
                    item.Graphic.color = item.Original;
                }
            }

            _items.Clear();
            _live.Clear();
            _root = null;
        }

        private static bool Themed(Image image)
        {
            Sprite sprite = image.overrideSprite;
            return !sprite || image.type is Image.Type.Sliced or Image.Type.Tiled && sprite.border != Vector4.zero;
        }

        private static Color Dim(Color color)
        {
            Color.RGBToHSV(color, out float hue, out float saturation, out float value);
            if (value < 0.55f || saturation > 0.45f) return color;

            Color result = Color.HSVToRGB(hue, saturation * 0.6f, 0.11f + (1f - value) * 0.55f);
            result.a = color.a;
            return result;
        }

        private static Color Light(Color color)
        {
            Color.RGBToHSV(color, out float hue, out float saturation, out float value);
            if (value >= 0.6f) return color;

            Color result = Color.HSVToRGB(hue, saturation * 0.7f, 0.9f - value * 0.25f);
            result.a = color.a;
            return result;
        }
    }
}
