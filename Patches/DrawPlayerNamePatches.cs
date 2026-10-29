using HarmonyLib;
using Il2Cpp;
using NullServers.Players;

namespace NullServers.Patches
{
    [HarmonyPatch(typeof(DrawPlayerName), nameof(DrawPlayerName.OnGUI))]
    internal static class DrawPlayerNameOnGUIPatch
    {
        public static void Prefix(DrawPlayerName __instance) => PlayerProfiles.BeforeDrawName(__instance);

        public static void Finalizer() => PlayerProfiles.AfterDrawName();
    }
}
