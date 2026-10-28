using Il2Cpp;
using Il2CppCodeStage.AntiCheat.Storage;
using MelonLoader;
using MelonLoader.Utils;
using NullServers.Network;
using System.Collections;
using System.Reflection;
using UnityEngine;

namespace NullServers.Additional
{
    internal sealed class FrameworkAddon
    {
        private readonly Dictionary<(Type, string), PropertyInfo> _properties = new();
        private readonly Dictionary<(Type, string), MethodInfo> _methods = new();
        private readonly AddonControl _control;
        private readonly HarmonyLib.Harmony _harmony;
        private readonly object _mod;
        private IDisposable _content;
        internal readonly AddonRequirement Requirement;
        internal readonly Type Catalog;
        internal readonly Type Core;
        internal readonly Type Inventory;
        internal string ContentError { get; set; }

        internal FrameworkAddon(AddonRequirement requirement, Assembly assembly, HarmonyLib.Harmony harmony, object mod)
        {
            Requirement = requirement;
            _harmony = harmony;
            _mod = mod;
            Core = assembly.GetType(requirement.Id + ".Core");
            string catalog = requirement.Id switch
            {
                Dependencies.Weapons => "WeaponCatalog",
                Dependencies.Maps => "MapCatalog",
                Dependencies.NPCs => "NPCCatalog",
                Dependencies.Modes => "GameModeCatalog",
                _ => "CustomizationCatalog"
            };
            Catalog = assembly.GetType(requirement.Id + "." + catalog);
            Inventory = assembly.GetType(requirement.Id + ".WeaponInventory");

            if (Core == null || Catalog == null || Read<int>(Catalog, "ApiVersion") != 1)
            {
                throw new InvalidOperationException("The optional addon does not expose a supported API.");
            }

            PropertyInfo enabled = Catalog.GetProperty("Enabled");
            _control = new AddonControl(harmony, enabled, enabled == null ? ContentHooks(assembly) : Array.Empty<MethodInfo>());

            if (requirement.Id == Dependencies.Customization)
            {
                _control.Disabled = arguments =>
                {
                    SyncCustomization customization = arguments[0] as SyncCustomization;

                    if (customization && ObscuredPrefs.GetInt("HatID", 0) >= customization.Hats.Count)
                    {
                        ObscuredPrefs.SetInt("HatID", 0);
                    }
                };
            }
        }

        private MethodInfo[] ContentHooks(Assembly assembly)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

            if (Requirement.Id == Dependencies.Customization)
            {
                return new[] { Core.GetMethod("InstantiateInGameCustomization", flags) };
            }

            return new[]
            {
                Core.GetMethod(nameof(MelonMod.OnSceneWasInitialized), flags),
                assembly.GetType(Requirement.Id + ".VolumePatch")?.GetMethod("Postfix", flags),
                assembly.GetType(Requirement.Id + ".ShopSystemAwake")?.GetMethod("Postfix", flags),
                assembly.GetType(Requirement.Id + ".OnPlayerSpawned")?.GetMethod("Postfix", flags),
                Inventory?.GetProperty("IsReady", flags)?.GetMethod,
                Inventory?.GetMethod("Grant", flags),
                Inventory?.GetMethod("Select", flags),
                Inventory?.GetMethod("WorldModel", flags),
                Inventory?.GetMethod("ViewModel", flags)
            };
        }

        internal IEnumerable<object> Items => Read<IEnumerable>(Catalog, "Items")?.Cast<object>() ?? Enumerable.Empty<object>();

        internal IEnumerable<object> Installed => Requirement.Id == Dependencies.Maps ?
            (Read<IEnumerable>(Catalog, "Installed") ?? Read<IEnumerable>(Core, "CustomMaps"))?.Cast<object>() ?? Items : Items;

        internal T Read<T>(Type type, string name, object instance = null)
        {
            if (type == null)
            {
                return default;
            }

            if (!_properties.TryGetValue((type, name), out var property))
            {
                _properties[(type, name)] = property = type.GetProperty(name);
            }

            object value = property != null ? property.GetValue(instance) : type.GetField(name)?.GetValue(instance);

            return value is T result ? result : default;
        }

        internal T Call<T>(Type type, string name, params object[] arguments)
        {
            if (type == null)
            {
                return default;
            }

            if (!_methods.TryGetValue((type, name), out var method))
            {
                _methods[(type, name)] = method = type.GetMethod(name);
            }

            return method?.Invoke(null, arguments) is T value ? value : default;
        }

        internal void Enable(bool enabled) => _control.Set(enabled);

        internal void UseContent(string[] ids)
        {
            _content = Requirement.Id is Dependencies.NPCs or Dependencies.Maps ?
                Call<IDisposable>(Catalog, "UseContent", new object[] { ids }) : new AddonContent(this, _harmony, ids);
            if (_content == null) throw new InvalidOperationException("The addon did not select its required content.");
        }

        internal void RestoreContent()
        {
            _content?.Dispose();
            _content = null;
        }

        internal void ReloadContent(int index, string name)
        {
            if (Requirement.Id is Dependencies.Weapons or Dependencies.NPCs && Enabled)
            {
                Core.GetMethod(nameof(MelonMod.OnSceneWasInitialized))?.Invoke(_mod, new object[] { index, name });
            }
        }

        internal void Dispose()
        {
            RestoreContent();
            _control.Dispose();
        }

        internal bool Enabled => _control.Enabled;

        internal string Error => Read<string>(Core, "Error") ?? ContentError;

        internal bool Ready =>
            Enabled &&
            Error == null &&
            Read<bool>(Catalog, "IsReady") &&
            Read<bool>(Core, "AssetsReady");

        internal string Text(object item, string name) => Read<string>(item.GetType(), name, item);

        internal void PlayerReady(Action<SyncCustomization> handler)
        {
            Catalog.GetEvent("PlayerReady")?.AddEventHandler(null, handler);
        }
    }

    internal static class Frameworks
    {
        private static readonly Dictionary<string, string> Errors = new();
        internal static FrameworkAddon Weapons { get; private set; }
        internal static FrameworkAddon Customization { get; private set; }
        internal static FrameworkAddon NPCs { get; private set; }
        internal static FrameworkAddon Modes { get; private set; }
        internal static FrameworkAddon Maps { get; private set; }
        internal static AddonSettings Settings { get; private set; }

        internal static bool ContentSelected { get; private set; }

        internal static AddonRequirement[] Detected { get; private set; } = Array.Empty<AddonRequirement>();

        internal static AddonRequirement[] Installed { get; private set; } = Array.Empty<AddonRequirement>();

        private static AddonRequirement[] _selected;
        private static int _selectedRevision = -1;

        internal static AddonRequirement[] Selected
        {
            get
            {
                if (_selected == null || _selectedRevision != Settings.Revision)
                {
                    _selected = Settings.Select(Installed);
                    _selectedRevision = Settings.Revision;
                }

                return _selected;
            }
        }

        internal static void Initialize(HarmonyLib.Harmony harmony, MelonLogger.Instance logger, AddonSettings settings = null)
        {
            Errors.Clear();
            Settings = settings ?? new AddonSettings(Path.Combine(MelonEnvironment.UserDataDirectory, "NullFrame", "addons.json"));

            if (Settings.LoadError != null)
            {
                logger.Warning(Settings.LoadError);
            }

            Detected = MelonBase.RegisteredMelons
                .Where(mod => Dependencies.Official.Contains(mod.Info.Name))
                .Select(mod => new AddonRequirement(mod.Info.Name, mod.Info.Version))
                .ToArray();

            Weapons = Find(Dependencies.Weapons, harmony, logger);
            Customization = Find(Dependencies.Customization, harmony, logger);
            NPCs = Find(Dependencies.NPCs, harmony, logger);
            Modes = Find(Dependencies.Modes, harmony, logger);
            Maps = Find(Dependencies.Maps, harmony, logger);
            Installed = new[] { Weapons, Customization, NPCs, Modes, Maps }
                .Where(addon => addon != null)
                .Select(addon => addon.Requirement)
                .OrderBy(addon => addon.Id, StringComparer.Ordinal)
                .ToArray();
            _selected = null;
            Enable(Selected);
        }

        private static FrameworkAddon Find(string name, HarmonyLib.Harmony harmony, MelonLogger.Instance logger)
        {
            var matches = MelonBase.RegisteredMelons.Where(mod => mod.Info.Name == name).ToArray();

            if (matches.Length == 0)
            {
                return null;
            }

            try
            {
                if (matches.Length != 1)
                {
                    throw new InvalidOperationException("Install only one copy of this official addon.");
                }

                Assembly assembly = matches[0].MelonAssembly?.Assembly;

                if (assembly == null)
                {
                    throw new InvalidOperationException("The optional addon assembly is unavailable.");
                }

                FrameworkAddon addon = new(new AddonRequirement(name, matches[0].Info.Version), assembly, harmony, matches[0]);
                string error = addon.Read<string>(addon.Core, "Error");

                if (error != null)
                {
                    logger.Warning(name + " content error: " + error);
                }

                return addon;
            }
            catch (Exception exception) when (exception is InvalidOperationException or TargetInvocationException or ArgumentException)
            {
                Errors[name] = exception.InnerException?.Message ?? exception.Message;
                logger.Warning(name + " integration disabled: " + Errors[name]);
                return null;
            }
        }

        internal static string Problem(string id)
        {
            FrameworkAddon addon = id == Dependencies.Weapons ? Weapons : id == Dependencies.NPCs ? NPCs :
                id == Dependencies.Modes ? Modes : id == Dependencies.Maps ? Maps : Customization;

            if (addon == null)
            {
                return Errors.GetValueOrDefault(id) ?? "The addon integration is unavailable.";
            }

            return addon.Error ?? (!addon.Read<bool>(addon.Core, "IsInitialized") ? "The addon is still initializing. Try again shortly." : null);
        }

        internal static bool UseContent(FrameworkManifest required, out string error)
        {
            RestoreContent();

            try
            {
                Weapons?.UseContent(required.Weapons.Select(asset => asset.Id).ToArray());
                Customization?.UseContent(required.Customization.Select(asset => asset.Id).ToArray());
                NPCs?.UseContent(required.NPCAssets.Select(asset => asset.Id).ToArray());
                Maps?.UseContent(required.MapAssets.Select(asset => asset.Id).ToArray());
                ContentSelected = true;
                error = null;

                return true;
            }
            catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or TargetInvocationException or MemberAccessException)
            {
                RestoreContent();
                error = exception.InnerException?.Message ?? exception.Message;
                return false;
            }
        }

        internal static void RestoreContent()
        {
            Weapons?.RestoreContent();
            Customization?.RestoreContent();
            NPCs?.RestoreContent();
            Maps?.RestoreContent();
            ContentSelected = false;
        }

        internal static void Enable(IEnumerable<AddonRequirement> required)
        {
            Weapons?.Enable(required.Any(addon => addon.Id == Dependencies.Weapons));
            Customization?.Enable(required.Any(addon => addon.Id == Dependencies.Customization));
            NPCs?.Enable(required.Any(addon => addon.Id == Dependencies.NPCs));
            Modes?.Enable(required.Any(addon => addon.Id == Dependencies.Modes));
            Maps?.Enable(required.Any(addon => addon.Id == Dependencies.Maps));
        }
    }
}
