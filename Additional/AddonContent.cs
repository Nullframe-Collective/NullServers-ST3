using HarmonyLib;
using Il2Cpp;
using Il2CppCodeStage.AntiCheat.Storage;
using System.Collections;
using System.Reflection;
using UnityEngine;

namespace NullServers.Additional
{
    internal sealed class AddonContent : IDisposable
    {
        private static readonly Dictionary<MethodBase, AddonContent> Hooks = new();
        private readonly HarmonyLib.Harmony _harmony;
        private readonly FrameworkAddon _addon;
        private readonly FieldInfo _field;
        private readonly IList _original;
        private readonly object[] _selected;
        private readonly List<(MethodInfo Method, MethodInfo Patch)> _patches = new();
        private readonly bool _customization;
        private bool _loaded;
        private bool _reloaded;
        private int? _hat;
        private int _mappedHat;
        private int _hatBase;

        internal AddonContent(FrameworkAddon addon, HarmonyLib.Harmony harmony, string[] ids)
        {
            _addon = addon;
            _harmony = harmony;
            _customization = addon.Requirement.Id == Dependencies.Customization;
            _field = addon.Core.GetField(_customization ? "CustomizationList" : "LoadedWeapons");
            _original = _field?.GetValue(null) as IList;

            if (_original == null || _field.IsInitOnly)
            {
                throw new InvalidOperationException("This addon does not expose a selectable content registry.");
            }

            object[] available = _original.Cast<object>().ToArray();
            _selected = ids.Select(id => available.Single(item => item != null && addon.Text(item, "Id") == id)).ToArray();
            IList selected = (IList)Activator.CreateInstance(_original.GetType());

            foreach (object item in _selected)
            {
                selected.Add(item);
            }

            try
            {
                Patch(addon.Core.GetProperty("Error")?.GetMethod, nameof(BeforeError));
                Patch(addon.Core.GetProperty("AssetsReady")?.GetMethod, nameof(BeforeAssetsReady));
                Patch(addon.Catalog.GetProperty("IsReady")?.GetMethod, nameof(BeforeReady));

                if (_customization)
                {
                    MethodInfo registration = addon.Core.GetMethod("InstantiateInGameCustomization");
                    if (registration != null)
                    {
                        Patch(registration, nameof(BeforeCustomization));
                    }
                }
                else
                {
                    Patch(addon.Core.GetMethod("LoadBundles", BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic),
                        nameof(AfterLoad), true);
                }

                _loaded = _customization;
                _field.SetValue(null, selected);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        private void Patch(MethodInfo method, string name, bool postfix = false)
        {
            if (method == null || Hooks.ContainsKey(method))
            {
                throw new InvalidOperationException("The addon does not expose supported content readiness hooks.");
            }

            MethodInfo patch = typeof(AddonContent).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic);
            Hooks.Add(method, this);
            _patches.Add((method, patch));
            HarmonyMethod handler = new(patch) { priority = Priority.First };
            _harmony.Patch(method, prefix: postfix ? null : handler, postfix: postfix ? handler : null);
        }

        private string AssetError()
        {
            if (!_addon.Read<bool>(_addon.Core, "IsInitialized"))
            {
                return "The addon is still initializing. Try again shortly.";
            }

            if (!_loaded) return null;

            string missing = MissingPack();
            if (missing != null && _customization && !_reloaded)
            {
                _reloaded = true;
                _addon.Catalog.GetMethod("Refresh", Type.EmptyTypes)?.Invoke(null, null);
                missing = MissingPack();
            }

            return missing == null ? null : "Required pack " + missing + " could not load its " +
                (_customization ? "menu/TPS prefabs." : "FPS/TPS prefabs and Animation component.");
        }

        private string MissingPack()
        {
            IDictionary bundles = _addon.Core.GetField("LoadedBundles")?.GetValue(null) as IDictionary;

            foreach (object item in _selected)
            {
                string name = _addon.Text(item, "Name");
                GameObject[] prefabs = name == null ? null : bundles?[name] as GameObject[];

                if (prefabs is not { Length: >= 2 } || !prefabs[0] || !prefabs[1] ||
                    !_customization && !prefabs[0].GetComponentInChildren<Animation>(true))
                {
                    return name ?? "";
                }
            }

            return null;
        }

        private static bool BeforeError(MethodBase __originalMethod, ref string __result)
        {
            __result = Hooks[__originalMethod].AssetError();
            return false;
        }

        private static bool BeforeAssetsReady(MethodBase __originalMethod, ref bool __result)
        {
            AddonContent content = Hooks[__originalMethod];
            __result = (content._loaded || content._selected.Length == 0) && content.AssetError() == null;
            return false;
        }

        private static bool BeforeReady(MethodBase __originalMethod, ref bool __result)
        {
            __result = Hooks[__originalMethod].AssetError() == null;
            return false;
        }

        private static void AfterLoad(MethodBase __originalMethod) => Hooks[__originalMethod]._loaded = true;

        private static void BeforeCustomization(MethodBase __originalMethod, object[] __args)
        {
            if (__args.FirstOrDefault() is not SyncCustomization customization || !customization) return;

            AddonContent content = Hooks[__originalMethod];

            if (content._hat != null || content._addon.Read<int>(content._addon.Catalog, "SelectionApi") == 1) return;

            content._hatBase = customization.Hats.Count;
            content._hat = ObscuredPrefs.GetInt("HatID", 0);
            content._mappedHat = content._hat.Value;

            if (content._hat >= content._hatBase)
            {
                object[] available = content._original.Cast<object>()
                    .Where(item => item != null && content._addon.Read<bool>(item.GetType(), "IsAvailable", item)).ToArray();
                int index = content._hat.Value - content._hatBase;
                object selected = index < available.Length ? available[index] : null;
                int mapped = Array.IndexOf(content._selected, selected);
                content._mappedHat = mapped < 0 ? 0 : content._hatBase + mapped;
                ObscuredPrefs.SetInt("HatID", content._mappedHat);
            }
        }

        public void Dispose()
        {
            if (_hat is { } original)
            {
                int current = ObscuredPrefs.GetInt("HatID", 0);
                int restored = current == _mappedHat ? original : current;

                if (current != _mappedHat && current >= _hatBase && current - _hatBase < _selected.Length)
                {
                    object item = _selected[current - _hatBase];
                    object[] available = _original.Cast<object>()
                        .Where(value => value != null && _addon.Read<bool>(value.GetType(), "IsAvailable", value)).ToArray();
                    int index = Array.IndexOf(available, item);
                    restored = index < 0 ? 0 : _hatBase + index;
                }

                ObscuredPrefs.SetInt("HatID", restored);
                _hat = null;
            }

            if (_original != null)
            {
                _field.SetValue(null, _original);
            }

            foreach (var patch in _patches)
            {
                _harmony.Unpatch(patch.Method, patch.Patch);
                Hooks.Remove(patch.Method);
            }

            _patches.Clear();
        }
    }
}
