using HarmonyLib;
using Il2Cpp;
using NullServers.Players;
using UnityEngine;

namespace NullServers.Patches
{
    [HarmonyPatch(typeof(PlayerInfo), nameof(PlayerInfo.HOGIPFLCOLH), MethodType.Getter)]
    internal static class PlayerInfoPlayerNamePatch
    {
        public static void Postfix(PlayerInfo __instance, ref string __result) => PlayerProfiles.AfterPlayerName(__instance, ref __result);
    }

    [HarmonyPatch(typeof(PlayerInfo), nameof(PlayerInfo.OnStartClient))]
    internal static class PlayerInfoOnStartClientPatch
    {
        public static void Postfix(PlayerInfo __instance) => PlayerProfiles.AfterPlayerStarted(__instance);
    }

    [HarmonyPatch(typeof(PlayerInfo), nameof(PlayerInfo.DDOCMDMFLFE), MethodType.Getter)]
    internal static class PlayerInfoPlayerColorPatch
    {
        public static void Postfix(PlayerInfo __instance, ref Color __result) => PlayerProfiles.AfterPlayerColor(__instance, ref __result);
    }

    [HarmonyPatch(typeof(PlayerInfo), nameof(PlayerInfo.CDDPPCBBHGL), MethodType.Getter)]
    internal static class PlayerInfoHasPlayerColorPatch
    {
        public static void Postfix(PlayerInfo __instance, ref bool __result) => PlayerProfiles.AfterHasPlayerColor(__instance, ref __result);
    }
}
