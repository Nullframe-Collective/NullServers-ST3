using HarmonyLib;
using Il2CppFishNet.Transporting.Relay;
using Il2CppFishNet.Transporting;
using NullServers.Network;
using NullServers.Server;
using System.Reflection;

namespace NullServers.Patches
{
    [HarmonyPatch(typeof(RelayTransport), nameof(RelayTransport.Initialize))]
    internal static class RelayTransportInitializePatch
    {
        public static void Postfix(RelayTransport __instance) => RelaySession.AfterRelayInitialize(__instance);
    }

    [HarmonyPatch(typeof(RelayTransport), nameof(RelayTransport.ConnectToRelay))]
    internal static class RelayTransportConnectToRelayPatch
    {
        public static void Prefix(RelayTransport __instance) => RelaySession.BeforeConnect(__instance);
    }

    [HarmonyPatch(typeof(RelayTransport), nameof(RelayTransport.Inner_OnClientConnectionState))]
    internal static class RelayTransportClientConnectionStatePatch
    {
        public static void Prefix(RelayTransport __instance, ClientConnectionStateArgs args) => RelaySession.BeforeAuthentication(__instance, args);
    }

    [HarmonyPatch(typeof(RelayTransport), nameof(RelayTransport.Relay_OnAuthenticateResponse))]
    internal static class RelayTransportAuthenticateResponsePatch
    {
        public static void Postfix(RelayTransport __instance, RelayAuthResult result) => RelaySession.AfterAuthentication(__instance, result);
    }

    [HarmonyPatch(typeof(RelayTransport), nameof(RelayTransport.Relay_OnRoomList))]
    internal static class RelayTransportRoomListPatch
    {
        public static bool Prefix(RelayTransport __instance, ref Il2CppSystem.Collections.Generic.IReadOnlyList<RelayRoomInfo> list) => RelaySession.BeforeRoomList(__instance, ref list);
    }

    [HarmonyPatch(typeof(RelayTransport), nameof(RelayTransport.JoinRoom))]
    internal static class RelayTransportJoinRoomPatch
    {
        public static bool Prefix(RelayTransport __instance, string roomId, ref bool __result) => RelaySession.BeforeJoinRoom(__instance, roomId, ref __result);

        public static void Postfix(bool __result) => RelaySession.AfterJoinRoom(__result);
    }

    [HarmonyPatch(typeof(RelayTransport), nameof(RelayTransport.Relay_OnRoomJoined))]
    internal static class RelayTransportRoomJoinedPatch
    {
        public static bool Prefix(RelayTransport __instance, RelayRoomInfo info) => RelaySession.BeforeRoomJoined(__instance, info);
    }

    [HarmonyPatch(typeof(RelayTransport), nameof(RelayTransport.Relay_OnRoomCreated))]
    internal static class RelayTransportRoomCreatedPatch
    {
        public static void Postfix(RelayRoomInfo info) => RelaySession.AfterRoomCreated(info);
    }

    [HarmonyPatch(typeof(RelayTransport), nameof(RelayTransport.Relay_OnRoomUpdated))]
    internal static class RelayTransportRoomUpdatedForRelaySessionPatch
    {
        [HarmonyPriority(Priority.HigherThanNormal)]
        public static void Postfix(RelayRoomInfo info) => RelaySession.AfterRoomUpdated(info);
    }

    [HarmonyPatch(typeof(RelayTransport), nameof(RelayTransport.Relay_OnRoomLeft))]
    internal static class RelayTransportRoomLeftPatch
    {
        public static void Postfix(RelayTransport __instance, RelayRoomLeaveReason reason) => RelaySession.AfterRoomLeft(__instance, reason);
    }

    [HarmonyPatch(typeof(RelayTransport), nameof(RelayTransport.Relay_OnCreateRoomFailed))]
    internal static class RelayTransportCreateRoomFailedPatch
    {
        public static void Postfix(RelayRoomResult result) => RelaySession.AfterCreateRoomFailed(result);
    }

    [HarmonyPatch(typeof(RelayTransport), nameof(RelayTransport.Relay_OnJoinRoomFailed))]
    internal static class RelayTransportJoinRoomFailedPatch
    {
        public static void Postfix(RelayRoomResult result) => RelaySession.AfterJoinRoomFailed(result);
    }

    [HarmonyPatch(typeof(RelayTransport), nameof(RelayTransport.Relay_OnRoomUpdated))]
    internal static class RelayTransportRoomUpdatedForServerControlPatch
    {
        [HarmonyPriority(Priority.Normal)]
        public static void Postfix(RelayRoomInfo info) => ServerControl.AfterRoomUpdated(info);
    }

    [HarmonyPatch(typeof(RelayTransport), nameof(RelayTransport.CanSpawnNetworkObject))]
    internal static class RelayTransportCanSpawnNetworkObjectPatch
    {
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(ref bool __result) => AntiCrash.BeforeObjectSpawn(ref __result);
    }

    [HarmonyPatch]
    internal static class RelayTransportRoomSettingsPatch
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(RelayTransport), nameof(RelayTransport.HostRoom));
            yield return AccessTools.Method(typeof(RelayTransport), nameof(RelayTransport.CreateRoom));
            yield return AccessTools.Method(typeof(RelayTransport), nameof(RelayTransport.UpdateRoom));
        }

        public static bool Prefix(RelayTransport __instance, ref string name, ref int maxPlayers, ref string roomData, ref bool __result) =>
            RelaySession.BeforeRoomSettings(__instance, ref name, ref maxPlayers, ref roomData, ref __result);
    }
}
