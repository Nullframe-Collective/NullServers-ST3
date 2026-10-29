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
        internal static void BeforeCategories(Volume __instance)
        {
            SandboxMenu menu = _instance;
            if (!menu._world.Active || __instance.Categories == null) return;

            menu.ClearCategories(__instance);

            for (int index = 0; index + 1 < __instance.Categories.Count; index++)
            {
                if (__instance.Categories[index].catagoryName == "Advanced" &&
                    __instance.Categories[index + 1].catagoryName == "Building")
                {
                    menu._categories[__instance.Pointer] = index;
                    return;
                }
            }

            menu._categories[__instance.Pointer] = __instance.Categories.Count;
            foreach (string name in new[] { "Advanced", "Building" })
            {
                __instance.Categories.Add(new Volume.catagory
                {
                    catagoryName = name,
                    tagName = "",
                    options = new Il2CppSystem.Collections.Generic.List<Volume.option>(),
                    hostOnly = false
                });
            }
        }

        internal static void AfterCategories(Volume __instance)
        {
            SandboxMenu menu = _instance;
            if (!menu._categories.TryGetValue(__instance.Pointer, out var first) || __instance.CategoryButtons == null)
            {
                return;
            }

            menu.ThrowToggle(__instance);

            for (int tab = 0; tab < 2; tab++)
            {
                if (first + tab >= __instance.CategoryButtons.Count) continue;

                RectTransform rect = __instance.CategoryButtons[first + tab];
                rect.name = tab == 0 ? "NullFrameAdvanced" : "NullFrameBuilding";
                UI_Button native = rect.GetComponent(Il2CppType.Of<UI_Button>())?.Cast<UI_Button>();

                if (native)
                {
                    native.enabled = false;
                }

                Button button = rect.GetComponent(Il2CppType.Of<Button>())?.Cast<Button>();
                if (!button) continue;

                int selected = tab;
                GameUi.OnClick(button, () => menu.Open(__instance, selected));
            }

            if (menu._sidebars.ContainsKey(__instance.Pointer)) return;

            menu.ThemeButton(__instance);

            GameUi ui = new(__instance);
            RectTransform holder = GameUi.Rect("Categories", __instance.Panel);
            GameUi.Area(holder, 0.025f, 0.105f, 0.225f, 0.88f);
            ScrollRect scroll = ui.Scroll(holder, "CategoryScroll");

            var original = __instance.CategoryButtons.ToArray().Select(rect => (rect, rect.parent, rect.anchorMin, rect.anchorMax, rect.offsetMin, rect.offsetMax)).ToArray();
            Image[] images = original.Select(item => item.rect.GetComponent(Il2CppType.Of<Image>())?.Cast<Image>()).ToArray();
            Text[] labels = original.Select(item => item.rect.GetComponentInChildren(Il2CppType.Of<Text>(), true)?.Cast<Text>()).ToArray();

            foreach (RectTransform rect in __instance.CategoryButtons)
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
                Original = original,
                Images = images,
                Labels = labels
            };

            int sample = Array.FindIndex(original, item => !Ours(item.rect));
            if (sample >= 0 && images[sample] && labels[sample])
            {
                menu._sidebars[__instance.Pointer].Normal = (images[sample].color, labels[sample].color);
            }
        }

        private void ClearCategories(Volume volume)
        {
            if (_volume == volume)
            {
                Close();

                if (_panel)
                {
                    Object.Destroy(_panel.gameObject);
                    _panel = null;
                }
            }

            List<RectTransform> buttons = volume.CategoryButtons?.ToArray().ToList() ?? new();
            if (_sidebars.Remove(volume.Pointer, out var sidebar))
            {
                buttons.AddRange(sidebar.Original.Select(item => item.Rect));

                if (sidebar.Root)
                {
                    sidebar.Root.gameObject.SetActive(false);
                    Object.Destroy(sidebar.Root.gameObject);
                }
            }

            foreach (RectTransform rect in buttons.Where(rect => rect).DistinctBy(rect => rect.Pointer))
            {
                volume.FIKDALMIMAB?.Remove(rect);
                rect.gameObject.SetActive(false);
                Object.Destroy(rect.gameObject);
            }

            volume.CategoryButtons?.Clear();
            _selectedCategories.Remove(volume.Pointer);
        }

        private void RestorePage(Volume volume, int index)
        {
            if (index <= 0 || volume.CategoryButtons == null || index >= volume.CategoryButtons.Count) return;

            RectTransform rect = volume.CategoryButtons[index];
            Button button = rect && rect.gameObject.activeInHierarchy ? rect.GetComponent(Il2CppType.Of<Button>())?.Cast<Button>() : null;
            if (button && button.interactable)
            {
                button.onClick.Invoke();
            }
        }

        private static bool Ours(RectTransform rect) => rect && rect.name.StartsWith("NullFrame", StringComparison.Ordinal);

        private static bool OurSelection(Sidebar sidebar, int selected) =>
            selected >= 0 && selected < sidebar.Original.Length && Ours(sidebar.Original[selected].Rect);

        private void SampleCategories(Sidebar sidebar, int selected)
        {
            if (selected < 0 || selected >= sidebar.Original.Length || OurSelection(sidebar, selected)) return;

            for (int index = 0; index < sidebar.Original.Length; index++)
            {
                Image image = sidebar.Images[index];
                Text label = sidebar.Labels[index];
                if (Ours(sidebar.Original[index].Rect) || !image || !label) continue;

                var style = (_theme.Original(image), _theme.Original(label));
                if (index != selected)
                {
                    if (style != sidebar.Normal)
                    {
                        sidebar.Normal = style;
                        sidebar.Layout = (-2, false, -1f);
                    }
                }
                else if (style != sidebar.Normal)
                {
                    sidebar.Selected = style;
                }
            }
        }

        private static void CategoryStyle(Sidebar sidebar, int index, (Color Image, Color Text) style)
        {
            if (sidebar.Images[index])
            {
                sidebar.Images[index].color = style.Image;
            }

            if (sidebar.Labels[index])
            {
                sidebar.Labels[index].color = style.Text;
            }
        }

        private static void RestoreNativeButtons(Sidebar sidebar)
        {
            foreach (var item in sidebar.Original)
            {
                if (!item.Rect || !item.Parent) continue;

                item.Rect.SetParent(item.Parent, false);
                item.Rect.anchorMin = item.Min;
                item.Rect.anchorMax = item.Max;
                item.Rect.offsetMin = item.Low;
                item.Rect.offsetMax = item.High;
            }

            if (sidebar.Root)
            {
                Object.Destroy(sidebar.Root.gameObject);
            }
        }

        // Lays the category buttons out again only when the selection, admin rights or panel height changed.
        private void LayoutSidebar(Sidebar sidebar, int selected)
        {
            var layout = (selected, _world.IsAdmin, sidebar.Root.rect.height);
            if (sidebar.Layout == layout) return;

            sidebar.Layout = layout;
            GameUi ui = sidebar.Ui;
            int visible = sidebar.Original.Count(item => item.Rect && (item.Rect.name != "NullFrameAdvanced" || _world.IsAdmin));
            float rowHeight = Mathf.Clamp(sidebar.Root.rect.height / ui.Unit / Mathf.Max(1, visible) - 6f, 46f, 76f);
            float y = 0f;

            for (int index = 0; index < sidebar.Original.Length; index++)
            {
                RectTransform rect = sidebar.Original[index].Rect;
                if (!rect) continue;

                if (OurSelection(sidebar, selected) || Ours(rect))
                {
                    CategoryStyle(sidebar, index, selected == index ? sidebar.Selected : sidebar.Normal);
                }

                if (rect.name == "NullFrameAdvanced")
                {
                    rect.gameObject.SetActive(_world.IsAdmin);
                }

                if (rect.gameObject.activeSelf)
                {
                    ui.SetRow(rect, y, rowHeight);
                    y += rowHeight + 6f;
                }
            }

            ui.Height(sidebar.Content, y);
        }
    }
}
