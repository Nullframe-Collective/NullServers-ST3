using HarmonyLib;
using Il2Cpp;
using NullServers.Customization;
using NullServers.Sandbox;

namespace NullServers.Patches
{
    [HarmonyPatch(typeof(PlayerDamage), nameof(PlayerDamage.KDDDGBFEFOC))]
    internal static class PlayerDamageDeathForRagdollSyncPatch
    {
        [HarmonyPriority(Priority.HigherThanNormal)]
        public static void Prefix(PlayerDamage __instance, out (int Player, RagdollSkin Skin) __state) => RagdollSync.BeforeDeath(__instance, out __state);

        [HarmonyPriority(Priority.HigherThanNormal)]
        public static Exception Finalizer(PlayerDamage __instance, Exception __exception, (int Player, RagdollSkin Skin) __state) => RagdollSync.AfterDeath(__instance, __exception, __state);
    }

    [HarmonyPatch(typeof(PlayerDamage), nameof(PlayerDamage.KDDDGBFEFOC))]
    internal static class PlayerDamageDamageForSandboxWorldPatch
    {
        [HarmonyPriority(Priority.Normal)]
        public static bool Prefix(PlayerDamage __instance, float __0) => SandboxWorld.BeforeDamage(__instance, __0);
    }
}
