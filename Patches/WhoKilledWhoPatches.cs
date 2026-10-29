using HarmonyLib;
using Il2Cpp;
using NullServers.Players;

namespace NullServers.Patches
{
    [HarmonyPatch(typeof(WhoKilledWho), nameof(WhoKilledWho.FKAKKHPGFJP))]
    internal static class WhoKilledWhoSendNoticeToServerPatch
    {
        public static bool Prefix(WhoKilledWho __instance, string __0, string __1, string __2, string __3)
        {
            JoinNotices notices = JoinNotices.Instance;
            if (notices == null) return true;

            return notices.ShouldSendToServer(__instance, __0, __1, __2, __3);
        }
    }

    [HarmonyPatch(typeof(WhoKilledWho), nameof(WhoKilledWho.HEIHFBKKFPI))]
    internal static class WhoKilledWhoBroadcastNoticePatch
    {
        public static bool Prefix(string __0, string __2)
        {
            JoinNotices notices = JoinNotices.Instance;
            if (notices == null) return true;

            return notices.ShouldBroadcast(__0, __2);
        }
    }

    [HarmonyPatch(typeof(WhoKilledWho), nameof(WhoKilledWho.DCIELGDLOAB))]
    internal static class WhoKilledWhoNoticeNamePatch
    {
        public static void Postfix(ref string __result) => PlayerProfiles.AfterNoticeName(ref __result);
    }
}
