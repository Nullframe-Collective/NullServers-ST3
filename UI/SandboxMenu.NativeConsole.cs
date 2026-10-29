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
        private void ThrowToggle(Volume volume)
        {
            if (_throwToggles.TryGetValue(volume.Pointer, out var existing) && existing)
            {
                foreach (Text label in existing.GetComponentsInChildren<Text>(true))
                {
                    label.text = "Throw Any One";
                }

                return;
            }

            Toggle source = volume.Panel.GetComponentsInChildren<Toggle>(true).FirstOrDefault(toggle =>
                toggle.name != "NullFrameThrowAnyone" &&
                toggle.GetComponentsInChildren<Text>(true).Any(label => label.text?.Trim() == "Friendly Fire"));
            if (!source) return;

            Toggle copy = Object.Instantiate(source, volume.Panel, false);
            copy.name = "NullFrameThrowAnyone";

            foreach (TranslateText translation in copy.GetComponentsInChildren<TranslateText>(true))
            {
                translation.enabled = false;
                Object.DestroyImmediate(translation);
            }

            copy.onValueChanged = new Toggle.ToggleEvent();
            foreach (Text label in copy.GetComponentsInChildren<Text>(true))
            {
                label.text = "Throw Any One";
            }

            RectTransform original = source.GetComponent<RectTransform>();
            RectTransform rect = copy.GetComponent<RectTransform>();
            rect.SetParent(original.parent, false);
            rect.anchorMin = original.anchorMin;
            rect.anchorMax = original.anchorMax;
            rect.pivot = original.pivot;

            if (!_footerOriginal.ContainsKey(volume.Pointer))
            {
                _footerOriginal[volume.Pointer] = (original, original.sizeDelta, original.anchoredPosition, original.localScale);
            }

            var saved = _footerOriginal[volume.Pointer];
            const float width = 140f, scale = 0.8f;
            Vector2 center = saved.Position + Vector2.right * (original.pivot.x * (width * scale - saved.Size.x));
            float footerHeight = original.parent.Cast<RectTransform>().rect.height;
            float rowHeight = Mathf.Max(8f, footerHeight * 0.5f - 2f);
            original.sizeDelta = rect.sizeDelta = new Vector2(width, rowHeight / scale);
            original.localScale = rect.localScale = Vector3.Scale(saved.Scale, new Vector3(scale, scale, 1f));
            original.anchoredPosition = center + Vector2.down * (footerHeight * 0.25f);
            rect.anchoredPosition = center + Vector2.up * (footerHeight * 0.25f);
            copy.SetIsOnWithoutNotify(_world.Environment.ThrowAnyone);
            copy.interactable = _world.IsHost || _world.IsAdmin;
            copy.onValueChanged.AddListener(DelegateSupport.ConvertDelegate<UnityAction<bool>>((Action<bool>)(value =>
            {
                if (!_world.IsHost && !_world.IsAdmin) return;

                if (!_world.SetEnvironment(_world.Environment with { ThrowAnyone = value }, out var error))
                {
                    _world.SetStatus(error);
                }
            })));
            _throwToggles[volume.Pointer] = copy;
        }

        private void RestoreFooter()
        {
            foreach (var saved in _footerOriginal.Values)
            {
                if (!saved.Rect) continue;

                saved.Rect.sizeDelta = saved.Size;
                saved.Rect.anchoredPosition = saved.Position;
                saved.Rect.localScale = saved.Scale;
            }

            _footerOriginal.Clear();
            foreach (Toggle toggle in _throwToggles.Values)
            {
                if (toggle)
                {
                    Object.Destroy(toggle.gameObject);
                }
            }

            _throwToggles.Clear();
        }

        internal static void AfterNativeUpdate(Volume __instance)
        {
            _instance.ConsoleToggle(__instance);

            if (!_instance._world.Active || !__instance.InfoText) return;

            string text = __instance.InfoText.text;
            if (text?.Contains("<color=clear>", StringComparison.Ordinal) == true)
            {
                __instance.InfoText.text = text.Replace("<color=clear>", "<color=#30383F>", StringComparison.Ordinal);
            }
        }

        internal static bool BeforeNativeButton(UI_Button __instance)
        {
            if (__instance.name.StartsWith("NullFrame", StringComparison.Ordinal))
            {
                return false;
            }

            if (_instance._tab >= 0 && __instance.Volume == _instance._volume)
            {
                _instance.Close();
            }

            Volume volume = __instance.Volume;
            if (volume && volume.CategoryButtons != null)
            {
                for (int index = 0; index < volume.CategoryButtons.Count; index++)
                {
                    RectTransform rect = volume.CategoryButtons[index];
                    if (rect && rect.GetComponent(Il2CppType.Of<UI_Button>())?.Pointer == __instance.Pointer)
                    {
                        _instance._selectedCategories[volume.Pointer] = index;
                        break;
                    }
                }
            }

            return true;
        }

        internal static bool BeforeSwitch(string __0, ref bool __result)
        {
            if (!_instance._world.Active || _instance._world.Hands.Holding == 0 ||
                __0 is not ("Switch" or "Switch2") || Input.GetAxis("Mouse ScrollWheel") == 0f) return true;
            __result = false;
            return false;
        }

        internal static bool BeforeWeapon(Component __instance)
        {
            if (!_instance._world.Active || !_instance._world.Hands.UsingTool) return true;

            if (!__instance.GetComponentInParent(Il2CppType.Of<FPScontroller>()))
            {
                return true;
            }

            WeaponScript weapon = __instance.TryCast<WeaponScript>();
            if (weapon && weapon.isActiveAndEnabled)
            {
                weapon.transform.localPosition = weapon.RestPosition;
            }

            return false;
        }

        private bool ConsoleAdmin => _world.Active && !_world.IsHost && _world.IsAdmin;

        internal static void BeforeNativeUpdate(Volume __instance)
        {
            SandboxMenu menu = _instance;
            if (menu.ConsoleAdmin)
            {
                menu._forcedConsoles.Add(__instance.Pointer);
                __instance.ABFHNIPOBBK(1);
            }
            else if (menu._forcedConsoles.Remove(__instance.Pointer))
            {
                __instance.ABFHNIPOBBK(__instance.ConsoleState?.Value ?? 0);
            }
        }

        internal static bool BeforeOption(Volume __instance, int __0, int __1)
        {
            SandboxMenu menu = _instance;
            if (!menu.ConsoleAdmin || __instance.Categories == null || __0 < 0 || __0 >= __instance.Categories.Count ||
                __instance.Categories[__0] is not { } category || !(category.hostOnly || category.isMusic))
            {
                return true;
            }

            menu._world.Admin(PlayerCommand.Option, menu._world.LocalId, __0, __1.ToString(CultureInfo.InvariantCulture));
            return false;
        }

        private void Overlay(Selectable control, Action clicked)
        {
            if (!control) return;

            control.interactable = true;
            if (_overlays.TryGetValue(control.Pointer, out var existing) && existing)
            {
                if (!existing.activeSelf)
                {
                    existing.SetActive(true);
                }

                return;
            }

            RectTransform hit = GameUi.Rect("AdminClick", control.transform);
            Image area = GameUi.Add<Image>(hit.gameObject);
            area.color = Color.clear;
            Button button = GameUi.Add<Button>(hit.gameObject);
            button.transition = Selectable.Transition.None;
            button.targetGraphic = area;
            GameUi.OnClick(button, () =>
            {
                if (ConsoleAdmin)
                {
                    clicked();
                }
            });
            _overlays[control.Pointer] = hit.gameObject;
        }

        private void HostControls(Volume volume)
        {
            if (volume.FIKDALMIMAB != null)
            {
                foreach (RectTransform rect in volume.FIKDALMIMAB)
                {
                    Button category = rect ? rect.GetComponent(Il2CppType.Of<Button>())?.Cast<Button>() : null;
                    if (category)
                    {
                        category.interactable = true;
                    }
                }
            }

            Toggle friendly = volume.AKKCLMBPAKO, disabled = volume.MPOHPEJNMDB;
            Overlay(friendly, () => _world.Admin(PlayerCommand.FriendlyFire, _world.LocalId, friendly && friendly.isOn ? 0f : 1f));
            Overlay(disabled, () => _world.Admin(PlayerCommand.DisableAI, _world.LocalId, disabled && disabled.isOn ? 0f : 1f));

            if (!_removeAll.TryGetValue(volume.Pointer, out var remove) || !remove)
            {
                Text label = volume.Panel ? volume.Panel.GetComponentsInChildren<Text>(true).FirstOrDefault(text => text.text?.Trim() == "Remove All AI") : null;
                remove = label ? label.GetComponentInParent(Il2CppType.Of<Button>())?.Cast<Button>() : null;
                if (!remove) return;

                _removeAll[volume.Pointer] = remove;
            }

            Overlay(remove, () => _world.Admin(PlayerCommand.RemoveAllAI, _world.LocalId));
        }

        private void ConsoleToggle(Volume volume)
        {
            if (!ConsoleAdmin)
            {
                foreach (GameObject overlay in _overlays.Values)
                {
                    if (overlay && overlay.activeSelf)
                    {
                        overlay.SetActive(false);
                    }
                }

                return;
            }

            HostControls(volume);

            if (volume.DKOOOHKFCDK && volume.DKOOOHKFCDK.gameObject.activeSelf)
            {
                volume.DKOOOHKFCDK.gameObject.SetActive(false);
            }

            if (!_consoleToggles.TryGetValue(volume.Pointer, out Toggle toggle) || !toggle)
            {
                if (Time.unscaledTime < _consoleScanAt) return;

                _consoleScanAt = Time.unscaledTime + 1f;
                Transform root = volume.Panel ? volume.Panel.root : volume.transform;
                toggle = root.GetComponentsInChildren<Toggle>(true).FirstOrDefault(item =>
                    item.GetComponentsInChildren<Text>(true).Any(label => label.text?.Contains("Enable Console", StringComparison.OrdinalIgnoreCase) == true));
                if (!toggle) return;

                _consoleToggles[volume.Pointer] = toggle;
            }

            Overlay(toggle, () =>
            {
                if (!volume) return;

                int shown = _consolePending.Volume == volume ? _consolePending.State : volume.ConsoleState?.Value ?? 0;
                int state = shown == 1 ? 0 : 1;
                _consolePending = (volume, state, Time.unscaledTime + 3f);
                _world.Admin(PlayerCommand.Console, _world.LocalId, state);
            });

            int synced = volume.ConsoleState?.Value ?? 0;
            if (_consolePending.Volume == volume && (synced == _consolePending.State || Time.unscaledTime > _consolePending.Until))
            {
                _consolePending = default;
            }

            toggle.SetIsOnWithoutNotify((_consolePending.Volume == volume ? _consolePending.State : synced) == 1);
        }

        private void ThemeButton(Volume volume)
        {
            if (volume.Panel.Find("NullFrameTheme")) return;

            GameUi ui = new(volume);
            Button button = ui.Button(volume.Panel, ThemeText, ToggleTheme);
            button.name = "NullFrameTheme";
            GameUi.Area(button.GetComponent<RectTransform>(), 0.265f, 0.922f, 0.395f, 0.982f);
            Text label = button.GetComponentInChildren(Il2CppType.Of<Text>(), true)?.Cast<Text>();
            if (label)
            {
                _themeLabels.Add(label);
            }
        }

        private string ThemeText => _theme.Dark ? "Light mode" : "Dark mode";

        private void ToggleTheme()
        {
            _theme.Toggle();
            _themeDirty = true;
            _themeLabels.RemoveAll(label => !label);

            foreach (Text label in _themeLabels)
            {
                label.text = ThemeText;
            }
        }

        private void HideFooter(Volume volume)
        {
            foreach (Text label in volume.Panel.GetComponentsInChildren<Text>(true))
            {
                string text = label.text?.Trim();
                if (text is "Remove My AI" or "Remove All AI" or "Friendly Fire" or "Disable AI" or "Throw Any One" ||
                    text?.StartsWith("Total AI:", StringComparison.Ordinal) == true)
                {
                    Button button = label.GetComponentInParent(Il2CppType.Of<Button>())?.Cast<Button>();
                    Toggle toggle = label.GetComponentInParent(Il2CppType.Of<Toggle>())?.Cast<Toggle>();
                    HideNative(button ? button.gameObject : toggle ? toggle.gameObject : label.gameObject);
                }
            }
        }
    }
}
