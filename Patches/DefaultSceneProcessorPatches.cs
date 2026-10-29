using HarmonyLib;
using Il2CppFishNet.Managing.Scened;
using NullServers.Room;

namespace NullServers.Patches
{
    [HarmonyPatch(typeof(DefaultSceneProcessor), nameof(DefaultSceneProcessor.UnloadStart), new[] { typeof(UnloadQueueData) })]
    internal static class DefaultSceneProcessorUnloadStartPatch
    {
        public static void Prefix(UnloadQueueData queueData) => RoundSync.BeforeSceneUnload(queueData);
    }
}
