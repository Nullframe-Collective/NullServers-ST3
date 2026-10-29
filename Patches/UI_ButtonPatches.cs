using HarmonyLib;
using Il2Cpp;
using NullServers.UI;

namespace NullServers.Patches
{
    [HarmonyPatch(typeof(UI_Button), nameof(UI_Button.SendToVolume))]
    internal static class UI_ButtonSendToVolumePatch
    {
        public static bool Prefix(UI_Button __instance) => SandboxMenu.BeforeNativeButton(__instance);
    }
}
