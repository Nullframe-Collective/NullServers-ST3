using HarmonyLib;
using Il2Cpp;
using NullServers.Server;

namespace NullServers.Patches
{
    [HarmonyPatch(typeof(ToggleNightVision), nameof(ToggleNightVision.OnEnable))]
    internal static class ToggleNightVisionOnEnablePatch
    {
        public static void Postfix(ToggleNightVision __instance) => ServerControl.AfterNightVisionEnabled(__instance);
    }

    [HarmonyPatch(typeof(ToggleNightVision), nameof(ToggleNightVision.OnDisable))]
    internal static class ToggleNightVisionOnDisablePatch
    {
        public static void Postfix() => ServerControl.AfterMapViewRestored();
    }
}
