using HarmonyLib;
using Il2CppFishNet.Managing.Scened;
using NullServers.Room;
using NetworkSceneManager = Il2CppFishNet.Managing.Scened.SceneManager;

namespace NullServers.Patches
{
    [HarmonyPatch(typeof(NetworkSceneManager), nameof(NetworkSceneManager.QueueOperation), new[] { typeof(LoadQueueData) })]
    internal static class NetworkSceneManagerQueueOperationPatch
    {
        public static bool Prefix(LoadQueueData data) => RoundSync.BeforeSceneLoad(data);
    }
}
