using HarmonyLib;
using Il2Cpp;
using NullServers.Sandbox;

namespace NullServers.Patches
{
    [HarmonyPatch(typeof(FPScontroller), nameof(FPScontroller.Update))]
    internal static class FPScontrollerUpdatePatch
    {
        public static bool Prefix(FPScontroller __instance) => SandboxWorld.BeforeMovement(__instance);
    }

    [HarmonyPatch(typeof(FPScontroller), nameof(FPScontroller.FixedUpdate))]
    internal static class FPScontrollerFixedUpdatePatch
    {
        public static bool Prefix(FPScontroller __instance) => SandboxWorld.BeforeFixedMovement(__instance);
    }
}
