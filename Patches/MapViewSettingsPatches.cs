using HarmonyLib;
using Il2Cpp;
using NullServers.Server;

namespace NullServers.Patches
{
    [HarmonyPatch(typeof(MapViewSettings), nameof(MapViewSettings.GLDBOLLIPNA))]
    internal static class MapViewSettingsMapViewRestoredPatch
    {
        public static void Postfix() => ServerControl.AfterMapViewRestored();
    }
}
