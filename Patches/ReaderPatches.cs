using HarmonyLib;
using Il2CppFishNet.Serializing;
using NullServers.Server;

namespace NullServers.Patches
{
    [HarmonyPatch(typeof(Reader), nameof(Reader.IsPossibleAllocationAttack))]
    internal static class ReaderIsPossibleAllocationAttackPatch
    {
        public static void Postfix(Reader __instance, bool __result) => AntiCrash.AfterAllocationCheck(__instance, __result);
    }
}
