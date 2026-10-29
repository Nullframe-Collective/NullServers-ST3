using HarmonyLib;
using Il2Cpp;
using NullServers.Sandbox;

namespace NullServers.Patches
{
    [HarmonyPatch(typeof(FPSMouseLook), nameof(FPSMouseLook.Update))]
    internal static class FPSMouseLookUpdatePatch
    {
        public static bool Prefix(FPSMouseLook __instance) => SandboxWorld.BeforeLook(__instance);
    }
}
