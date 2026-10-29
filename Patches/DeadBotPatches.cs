using HarmonyLib;
using Il2Cpp;
using NullServers.Sandbox;

namespace NullServers.Patches
{
    [HarmonyPatch(typeof(DeadBot), nameof(DeadBot.Start))]
    internal static class DeadBotStartPatch
    {
        public static bool Prefix(DeadBot __instance) => RagdollSync.BeforeDeadBot(__instance);
    }
}
