using HarmonyLib;
using Il2Cpp;
using NullServers.Sandbox;

namespace NullServers.Patches
{
    [HarmonyPatch(typeof(WaitForDestroy), nameof(WaitForDestroy.Awake))]
    internal static class WaitForDestroyAwakePatch
    {
        public static bool Prefix(WaitForDestroy __instance) => RagdollSync.BeforeCorpseTimer(__instance);
    }
}
