using HarmonyLib;
using Il2Cpp;
using Il2CppFishNet.Transporting.Relay;
using NullServers.Network;
using NullServers.Room;
using NullServers.Server;
using NullServers.UI;

namespace NullServers.Patches
{
    [HarmonyPatch(typeof(LobbyMenu), nameof(LobbyMenu.CAMKLBFGGNH))]
    internal static class LobbyMenuRoomCompatibilityPatch
    {
        public static bool Prefix(RelayRoomInfo __0, RoomProperties __1, ref bool __result) => RelaySession.BeforeRoomCompatibility(__0, __1, ref __result);
    }

    [HarmonyPatch(typeof(LobbyMenu), nameof(LobbyMenu.OnGUI))]
    internal static class LobbyMenuOnGUIForAddonMenuPatch
    {
        [HarmonyPriority(Priority.HigherThanNormal)]
        public static bool Prefix() => AddonMenu.BeforeLobbyGUI();
    }

    [HarmonyPatch(typeof(LobbyMenu), nameof(LobbyMenu.BAGGECEDBML))]
    internal static class LobbyMenuBrowserForProfileMenuPatch
    {
        [HarmonyPriority(Priority.HigherThanNormal)]
        public static void Prefix(LobbyMenu __instance) => ProfileMenu.BeforeBrowser(__instance);

        [HarmonyPriority(Priority.HigherThanNormal)]
        public static void Postfix(LobbyMenu __instance) => ProfileMenu.AfterBrowser(__instance);

        [HarmonyPriority(Priority.HigherThanNormal)]
        public static void Finalizer() => ProfileMenu.EndBrowser();
    }

    [HarmonyPatch(typeof(LobbyMenu), nameof(LobbyMenu.OnGUI))]
    internal static class LobbyMenuOnGUIForProfileMenuPatch
    {
        [HarmonyPriority(Priority.Normal)]
        public static void Prefix(out bool __state) => ProfileMenu.BeforeLobbyGUI(out __state);

        [HarmonyPriority(Priority.Normal)]
        public static void Postfix(LobbyMenu __instance, bool __state) => ProfileMenu.AfterLobbyGUI(__instance, __state);

        [HarmonyPriority(Priority.Normal)]
        public static void Finalizer(bool __state) => ProfileMenu.EndLobbyGUI(__state);
    }

    [HarmonyPatch(typeof(LobbyMenu), nameof(LobbyMenu.Start))]
    internal static class LobbyMenuStartPatch
    {
        public static void Postfix(LobbyMenu __instance) => ServerControl.CaptureCatalog(__instance);
    }

    [HarmonyPatch(typeof(LobbyMenu), nameof(LobbyMenu.AJKAOGDBDMH))]
    internal static class LobbyMenuHostMenuPatch
    {
        public static void Prefix(LobbyMenu __instance) => RoomCustomization.BeforeHostMenu(__instance);

        public static void Postfix(LobbyMenu __instance) => RoomCustomization.AfterHostMenu(__instance);

        public static void Finalizer() => RoomCustomization.EndHostMenu();
    }

    [HarmonyPatch(typeof(LobbyMenu), nameof(LobbyMenu.BAGGECEDBML))]
    internal static class LobbyMenuRoomBrowserForRoomCustomizationPatch
    {
        [HarmonyPriority(Priority.Normal)]
        public static void Prefix(LobbyMenu __instance) => RoomCustomization.BeforeRoomBrowser(__instance);

        [HarmonyPriority(Priority.Normal)]
        public static void Finalizer() => RoomCustomization.AfterRoomBrowser();
    }

    [HarmonyPatch(typeof(LobbyMenu), nameof(LobbyMenu.OnGUI))]
    internal static class LobbyMenuOnGUIForCommunityMenuPatch
    {
        [HarmonyPriority(Priority.High)]
        public static bool Prefix() => CommunityMenu.BeforeGUI();
    }
}
