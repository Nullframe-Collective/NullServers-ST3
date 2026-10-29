using HarmonyLib;
using Il2Cpp;
using NullServers.Network;

namespace NullServers.Patches
{
    [HarmonyPatch(typeof(NetBootstrap), nameof(NetBootstrap.FPFDMJBMCAN))]
    internal static class NetBootstrapBootstrapJoinPatch
    {
        public static bool Prefix(string __0, ref bool __result) => RelaySession.BeforeBootstrapJoin(__0, ref __result);
    }
}
