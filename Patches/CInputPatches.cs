using HarmonyLib;
using NullServers.UI;

namespace NullServers.Patches
{
    [HarmonyPatch(typeof(Il2CppInControl.mod.cInput), nameof(Il2CppInControl.mod.cInput.GetButtonDown))]
    internal static class cInputGetButtonDownPatch
    {
        public static bool Prefix(string __0, ref bool __result) => SandboxMenu.BeforeSwitch(__0, ref __result);
    }
}
