using HarmonyLib;
using Il2Cpp;
using NullServers.Sandbox;

namespace NullServers.Patches
{
    [HarmonyPatch(typeof(RagdollController), nameof(RagdollController.Awake))]
    internal static class RagdollControllerAwakePatch
    {
        public static void Postfix(RagdollController __instance) => RagdollSync.Created(__instance);
    }

    [HarmonyPatch(typeof(RagdollController), "Start")]
    internal static class RagdollControllerStartPatch
    {
        public static bool Prefix() => RagdollSync.BeforeFreeze();
    }
}
