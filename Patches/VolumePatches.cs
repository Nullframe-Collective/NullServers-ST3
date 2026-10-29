using HarmonyLib;
using Il2Cpp;
using Il2CppFishNet.Connection;
using Il2CppFishNet.Serializing;
using NullServers.Sandbox;
using NullServers.Server;
using NullServers.UI;

namespace NullServers.Patches
{
    [HarmonyPatch(typeof(Volume), nameof(Volume.OnStartServer))]
    internal static class VolumeOnStartServerPatch
    {
        public static bool Prefix(Volume __instance) => HostTools.BeforeConsoleStart(__instance);
    }

    [HarmonyPatch(typeof(Volume), nameof(Volume.AKLPGOIKAHF))]
    internal static class VolumeConsoleRequestPatch
    {
        public static void Prefix(Volume __instance, NetworkConnection __2, out int __state) => SandboxWorld.BeforeConsoleRequest(__instance, __2, out __state);

        public static Exception Finalizer(Volume __instance, int __state, Exception __exception) => SandboxWorld.AfterConsoleRequest(__instance, __state, __exception);
    }

    [HarmonyPatch(typeof(Volume), nameof(Volume.CreateCatagoryButtons))]
    internal static class VolumeCreateCatagoryButtonsPatch
    {
        public static void Prefix(Volume __instance) => SandboxMenu.BeforeCategories(__instance);

        public static void Postfix(Volume __instance) => SandboxMenu.AfterCategories(__instance);
    }

    [HarmonyPatch(typeof(Volume), nameof(Volume.Update))]
    internal static class VolumeUpdatePatch
    {
        public static void Prefix(Volume __instance) => SandboxMenu.BeforeNativeUpdate(__instance);

        public static void Postfix(Volume __instance) => SandboxMenu.AfterNativeUpdate(__instance);
    }

    [HarmonyPatch(typeof(Volume), nameof(Volume.MFLDLBLDGKP))]
    internal static class VolumeOptionPatch
    {
        public static bool Prefix(Volume __instance, int __0, int __1) => SandboxMenu.BeforeOption(__instance, __0, __1);
    }

    [HarmonyPatch(typeof(Volume), nameof(Volume.AKLPGOIKAHF))]
    internal static class VolumeReadSpawnPatch
    {
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(PooledReader __0) => AntiCrash.CheckSpawn(__0);
    }
}
