using HarmonyLib;
using Il2Cpp;
using Il2CppFishNet.Serializing;
using NullServers.Customization;
using NullServers.Server;

namespace NullServers.Patches
{
    [HarmonyPatch(typeof(SyncCustomization), nameof(SyncCustomization.MKFLOHBILFG))]
    internal static class SyncCustomizationNativeAppearancePatch
    {
        public static bool Prefix(SyncCustomization __instance) => LiveAppearance.BeforeNativeAppearance(__instance);
    }

    [HarmonyPatch(typeof(SyncCustomization), nameof(SyncCustomization.LAEBMFBEDKM))]
    internal static class SyncCustomizationReadAppearancePatch
    {
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(PooledReader __0) => AntiCrash.CheckAppearance(__0);
    }
}
