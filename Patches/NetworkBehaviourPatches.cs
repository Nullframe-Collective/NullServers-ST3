using HarmonyLib;
using Il2CppFishNet.Object;
using Il2CppFishNet.Serializing;
using NullServers.Server;

namespace NullServers.Patches
{
    [HarmonyPatch(typeof(NetworkBehaviour), nameof(NetworkBehaviour.ReadServerRpc))]
    internal static class NetworkBehaviourReadServerRpcPatch
    {
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(NetworkBehaviour __instance, PooledReader reader) => AntiCrash.BeforeMethod(__instance, reader);
    }
}
