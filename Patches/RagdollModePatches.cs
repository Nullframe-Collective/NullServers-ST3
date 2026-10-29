using HarmonyLib;
using Il2Cpp;
using NullServers.Sandbox;

namespace NullServers.Patches
{
    [HarmonyPatch(typeof(RagdollMode), nameof(RagdollMode.OnEnable))]
    internal static class RagdollModeOnEnablePatch
    {
        public static bool Prefix(RagdollMode __instance) => RagdollSync.BeforeEnable(__instance);
    }
}
