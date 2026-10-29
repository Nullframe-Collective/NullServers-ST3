using HarmonyLib;
using Il2Cpp;
using Il2CppFishNet.Connection;
using Il2CppFishNet.Serializing;
using NullServers.Players;
using NullServers.Server;

namespace NullServers.Patches
{
    [HarmonyPatch(typeof(MultiplayerChat), nameof(MultiplayerChat.PECNIPKAEPC))]
    internal static class MultiplayerChatChatNamePatch
    {
        public static void Postfix(int __0, ref string __result) => PlayerProfiles.AfterChatName(__0, ref __result);
    }

    [HarmonyPatch(typeof(MultiplayerChat), nameof(MultiplayerChat.NBHNJMHGGCL))]
    internal static class MultiplayerChatChatMessagePatch
    {
        public static bool Prefix(NetworkConnection __2) => ServerAccess.BeforeChatMessage(__2);
    }

    [HarmonyPatch(typeof(MultiplayerChat), nameof(MultiplayerChat.NKOMILAFHFN))]
    internal static class MultiplayerChatReadMessagePatch
    {
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(PooledReader __0) => AntiCrash.CheckChat(__0);
    }
}
