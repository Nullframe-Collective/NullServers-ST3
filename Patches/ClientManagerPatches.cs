using HarmonyLib;
using Il2CppFishNet.Managing.Client;
using Il2CppFishNet.Serializing;
using Il2CppFishNet.Transporting;
using NullServers.Players;

namespace NullServers.Patches
{
    [HarmonyPatch(typeof(ClientManager), nameof(ClientManager.ParseBroadcast))]
    internal static class ClientManagerParseBroadcastPatch
    {
        public static bool Prefix(ClientManager __instance, PooledReader reader, Channel channel) => PlayerProfiles.BeforeClientBroadcast(__instance, reader, channel);
    }
}
