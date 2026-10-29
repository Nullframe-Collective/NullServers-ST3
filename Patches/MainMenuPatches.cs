using HarmonyLib;
using Il2Cpp;
using NullServers.UI;

namespace NullServers.Patches
{
    [HarmonyPatch(typeof(MainMenu), nameof(MainMenu.OnGUI))]
    internal static class MainMenuOnGUIForAddonMenuPatch
    {
        public static bool Prefix() => AddonMenu.BeforeMainGUI();

        public static void Postfix() => AddonMenu.AfterMainGUI();
    }

    [HarmonyPatch(typeof(MainMenu), nameof(MainMenu.ShowLobby))]
    internal static class MainMenuShowLobbyPatch
    {
        public static void Postfix(MainMenu __instance) => ServerMenu.AfterShowLobby(__instance);
    }

    [HarmonyPatch(typeof(MainMenu), nameof(MainMenu.OnGUI))]
    internal static class MainMenuOnGUIForCommunityMenuPatch
    {
        [HarmonyPriority(Priority.High)]
        public static bool Prefix() => CommunityMenu.BeforeGUI();
    }
}
