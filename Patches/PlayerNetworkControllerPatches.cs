using HarmonyLib;
using Il2Cpp;
using Il2CppFishNet.Serializing;
using NullServers.Server;
using System.Reflection;

namespace NullServers.Patches
{
    [HarmonyPatch(typeof(PlayerNetworkController), nameof(PlayerNetworkController.EFIBFBEGIOE))]
    internal static class PlayerNetworkControllerReadStatePatch
    {
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(PooledReader __0) => AntiCrash.CheckPlayerState(__0);
    }

    [HarmonyPatch]
    internal static class PlayerNetworkControllerReadStringFloatPatch
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(PlayerNetworkController), nameof(PlayerNetworkController.DGMPNAIOHNG));
            yield return AccessTools.Method(typeof(PlayerNetworkController), nameof(PlayerNetworkController.FKGFMBJEFAM));
            yield return AccessTools.Method(typeof(PlayerNetworkController), nameof(PlayerNetworkController.PPFKMGCCMKI));
        }

        [HarmonyPriority(Priority.First)]
        public static bool Prefix(PooledReader __0) => AntiCrash.CheckStringFloat(__0);
    }
}
