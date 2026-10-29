using HarmonyLib;
using Il2Cpp;
using Il2CppFishNet.Connection;
using Il2CppFishNet.Object;
using NullServers.Server;
using UnityEngine;

namespace NullServers.Patches
{
    [HarmonyPatch(typeof(CPPBDGGKHCB), nameof(CPPBDGGKHCB.FONDGACBGAB), new[] { typeof(GameObject), typeof(Vector3), typeof(Quaternion), typeof(NetworkConnection) })]
    internal static class CPPBDGGKHCBSpawnPrefabPatch
    {
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(GameObject __0, NetworkConnection __3, out bool __state) => AntiCrash.BeforeSpawnPrefab(__0, __3, out __state);

        [HarmonyPriority(Priority.First)]
        public static void Postfix(NetworkObject __result) => AntiCrash.AfterSpawned(__result);

        [HarmonyPriority(Priority.First)]
        public static void Finalizer(bool __state) => AntiCrash.AfterSpawnPrefab(__state);
    }
}
