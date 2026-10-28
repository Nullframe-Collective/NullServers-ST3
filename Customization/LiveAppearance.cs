using HarmonyLib;
using Il2Cpp;
using Il2CppCodeStage.AntiCheat.Storage;
using MelonLoader;
using NullServers.Additional;
using NullServers.Network;
using NullServers.Players;
using UnityEngine;

namespace NullServers.Customization
{
    internal sealed class LiveAppearance
    {
        private static LiveAppearance _instance;
        private readonly RelaySession _session;
        private readonly MelonLogger.Instance _logger;
        private readonly HatSynchronization _slots;
        private readonly Dictionary<IntPtr, (SyncCustomization Sync, AppearanceVisual Visual)> _visuals = new();
        private readonly Dictionary<int, string> _hats = new();
        private string _roomId;
        private float _nextUpdate;

        internal SkinTextures Textures { get; } = new();

        internal IReadOnlyDictionary<int, string> Hats => _hats;

        internal bool Active =>
            _session.IsNullFrame &&
            !_session.IsSwitching &&
            _session.Relay &&
            _session.Relay.CurrentRoom != null;

        internal SyncCustomization Local { get; private set; }

        internal bool SlotsAvailable => _slots.Available;

        internal LiveAppearance(HarmonyLib.Harmony harmony, RelaySession session, PlayerProfiles profiles, MelonLogger.Instance logger)
        {
            _instance = this;
            _session = session;
            _logger = logger;
            _slots = new HatSynchronization(session, profiles, logger);

            Frameworks.Customization?.PlayerReady(CaptureCustomHats);

            harmony.Patch(
                AccessTools.Method(typeof(CharacterCustomization), nameof(CharacterCustomization.Awake)),
                postfix: new HarmonyMethod(typeof(LiveAppearance), nameof(CaptureHats)));

            harmony.Patch(
                AccessTools.Method(typeof(SyncCustomization), nameof(SyncCustomization.MKFLOHBILFG)),
                prefix: new HarmonyMethod(typeof(LiveAppearance), nameof(BeforeNativeAppearance)));
        }

        private void CaptureCustomHats(SyncCustomization source)
        {
            FrameworkAddon addon = Frameworks.Customization;

            if (addon == null)
            {
                return;
            }

            foreach (object info in addon.Items)
            {
                object[] arguments = { source, addon.Text(info, "Id"), -1 };

                if (addon.Call<bool>(addon.Catalog, "TryGetHatId", arguments))
                {
                    _hats[(int)arguments[2]] = addon.Text(info, "Name");
                }
            }
        }

        private static void CaptureHats(CharacterCustomization __instance)
        {
            foreach (CharacterCustomization.hat hat in __instance.AllHats)
            {
                if (hat != null)
                {
                    _instance._hats[hat.hatID] = hat.hatName;
                }
            }
        }

        private static bool BeforeNativeAppearance(SyncCustomization __instance)
        {
            LiveAppearance instance = _instance;
            if (!instance.Active) return true;

            instance.VisualFor(__instance);

            return false;
        }

        private AppearanceVisual VisualFor(SyncCustomization sync)
        {
            EnsureRoom();

            if (_visuals.TryGetValue(sync.Pointer, out var current) && current.Visual.HatCount != sync.Hats.Count)
            {
                current.Visual.Dispose();
                _visuals.Remove(sync.Pointer);
            }

            if (!_visuals.TryGetValue(sync.Pointer, out var entry))
            {
                _visuals[sync.Pointer] = entry = (sync, new AppearanceVisual(sync));
            }

            return entry.Visual;
        }

        internal void Update()
        {
            Textures.Update();

            if (Time.unscaledTime < _nextUpdate) return;

            _nextUpdate = Time.unscaledTime + 0.2f;
            EnsureRoom();
            Local = null;

            if (!Active)
            {
                _slots.Update(null);
                return;
            }

            foreach (IntPtr pointer in _visuals.Where(pair => !pair.Value.Sync).Select(pair => pair.Key).ToArray())
            {
                _visuals[pointer].Visual.Dispose();
                _visuals.Remove(pointer);
            }

            foreach (SyncCustomization sync in UnityEngine.Object.FindObjectsOfType<SyncCustomization>())
            {
                if (!sync.IsClientInitialized || sync.HatId < 0 || sync.FromLegacyReplay) continue;

                if (sync.IsOwner)
                {
                    Local = sync;
                }

                Appearance appearance = Read(sync);

                if (sync.SkinsDisabled)
                {
                    appearance = appearance with
                    {
                        UseSkin = false
                    };
                }

                VisualFor(sync).Apply(appearance, Textures);
            }

            _slots.Update(Local);
        }

        private void EnsureRoom()
        {
            string roomId = Active ? _session.Relay.CurrentRoom.Id : null;
            if (_roomId == roomId) return;

            foreach (var entry in _visuals.Values)
            {
                entry.Visual.Dispose();
            }

            _visuals.Clear();
            Textures.Dispose();
            _roomId = roomId;
        }

        internal Material[] OriginalMaterials(SyncCustomization sync, Renderer renderer) => VisualFor(sync).OriginalMaterials(renderer);

        internal static Appearance Read(SyncCustomization sync)
        {
            Vector3 color = sync.FurColor;
            string[] urls = (sync.SkinData ?? "").Split('|');
            string Url(int index) => urls.Length > index ? urls[index] : "";

            return new Appearance(
                sync.HatId,
                "#" + ColorUtility.ToHtmlStringRGB(new Color(color.x, color.y, color.z)),
                !string.IsNullOrEmpty(sync.SkinData),
                Url(0),
                Url(1),
                Url(2),
                Url(3),
                _instance?._slots.HatsFor(sync));
        }

        internal bool Apply(Appearance appearance, out string error)
        {
            error = "Your character is not available yet.";

            SyncCustomization sync = Local;
            if (!Active ||
                !sync ||
                !sync.IsOwner ||
                !sync.IsClientInitialized ||
                !appearance.Validate(sync.Hats.Count, out error))
            {
                return false;
            }

            if (!_slots.Save(sync, appearance.ExtraHats, out error))
            {
                error ??= "Extra hat slots are only available on NullFrame servers with CustomPlayerCustomization enabled.";
                return false;
            }

            if (Frameworks.Customization?.Read<int>(Frameworks.Customization.Catalog, "SelectionApi") == 1)
            {
                Frameworks.Customization.Call<object>(Frameworks.Customization.Catalog, "SaveSelection", sync, appearance.Hat);
            }

            ColorUtility.TryParseHtmlString(appearance.FurColor, out var color);
            ObscuredPrefs.SetInt("HatID", appearance.Hat);
            ObscuredPrefs.SetColor("FurColor", color);
            ObscuredPrefs.SetBool("CustomSkin", appearance.UseSkin);

            string[] urls = appearance.Urls;
            string[] keys =
            [
                "HeadURL",
                "BodyURL",
                "ArmsURL",
                "LegsURL"
            ];

            for (int index = 0; index < keys.Length; index++)
            {
                ObscuredPrefs.SetString(keys[index], urls[index]);
            }

            ObscuredPrefs.Save();

            sync.HatId = appearance.Hat;
            sync.FurColor = new Vector3(color.r, color.g, color.b);
            sync.SkinData = appearance.SkinData;
            sync.SendAppearance(appearance.Hat, sync.FurColor, appearance.SkinData);
            VisualFor(sync).Apply(appearance, Textures);

            _logger.Msg($"Appearance updated: hat {appearance.Hat}; fur {appearance.FurColor}; skin={appearance.UseSkin}.");

            error = null;

            return true;
        }
    }
}
