using HarmonyLib;
using Il2CppFishNet.Connection;
using Il2CppFishNet.Managing.Server;
using Il2CppFishNet.Serializing;
using Il2CppFishNet.Transporting;
using NullServers.Server;

namespace NullServers.Patches
{
    [HarmonyPatch(typeof(ServerObjects), nameof(ServerObjects.ParseServerRpc))]
    internal static class ServerObjectsParseServerRpcPatch
    {
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(PooledReader reader, NetworkConnection conn, Channel channel, out AntiCrash.Frame __state) => AntiCrash.BeforeRpc(reader, conn, channel, out __state);

        [HarmonyPriority(Priority.First)]
        public static Exception Finalizer(Exception __exception, AntiCrash.Frame __state) => AntiCrash.AfterPacket(__exception, __state);
    }
}
