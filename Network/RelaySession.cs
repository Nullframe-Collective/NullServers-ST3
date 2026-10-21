using HarmonyLib;
using Il2Cpp;
using Il2CppFishNet.Transporting;
using Il2CppFishNet.Transporting.Relay;
using MelonLoader;
using S3_NullServers.Room;
using System.Collections;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using UnityEngine;

namespace S3_NullServers.Network
{
    internal sealed class RelaySession
    {
        internal const string NullFrameGameVersion = "S3-NS";
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        private static RelaySession _instance;
        private readonly MelonLogger.Instance _logger;
        private readonly Dictionary<string, string> _roomVersions = new();
        private readonly Dictionary<string, string> _roomNameColors = new();
        private readonly RoomOptions _options;
        private string _officialVersion;
        private string _connectionVersion;
        private int _listedRooms = -1;
        private int _visibleRooms = -1;
        private bool _editingRoom;
        private bool _returningToMenu;

        internal RelayTransport Relay
        {
            get; private set;
        }
        internal bool IsNullFrame { get; private set; } = true;
        internal bool IsSwitching
        {
            get; set;
        }
        internal bool HoldRoomClosed
        {
            get; set;
        }

        internal string SelectedVersion => IsNullFrame ? NullFrameGameVersion : _officialVersion;

        internal RelaySession(HarmonyLib.Harmony harmony, MelonLogger.Instance logger, RoomOptions options)
        {
            _instance = this;
            _logger = logger;
            _options = options;
            harmony.Patch(
                AccessTools.Method(typeof(RelayTransport), nameof(RelayTransport.Initialize)),
                postfix: new HarmonyMethod(typeof(RelaySession), nameof(AfterRelayInitialize)));

            harmony.Patch(
                AccessTools.Method(typeof(RelayTransport), nameof(RelayTransport.ConnectToRelay)),
                prefix: new HarmonyMethod(typeof(RelaySession), nameof(BeforeConnect)));

            harmony.Patch(
                AccessTools.Method(typeof(RelayTransport), nameof(RelayTransport.Inner_OnClientConnectionState)),
                prefix: new HarmonyMethod(typeof(RelaySession), nameof(BeforeAuthentication)));

            harmony.Patch(
                AccessTools.Method(typeof(RelayTransport), nameof(RelayTransport.Relay_OnAuthenticateResponse)),
                postfix: new HarmonyMethod(typeof(RelaySession), nameof(AfterAuthentication)));

            harmony.Patch(
                AccessTools.Method(typeof(RelayTransport), nameof(RelayTransport.Relay_OnRoomList)),
                prefix: new HarmonyMethod(typeof(RelaySession), nameof(BeforeRoomList)));

            foreach (string method in new[]
            {
                nameof(RelayTransport.HostRoom),
                nameof(RelayTransport.CreateRoom),
                nameof(RelayTransport.UpdateRoom)
            })
            {
                harmony.Patch(
                    AccessTools.Method(typeof(RelayTransport), method),
                    prefix: new HarmonyMethod(typeof(RelaySession), nameof(BeforeRoomSettings)));
            }

            harmony.Patch(
                AccessTools.Method(typeof(RelayTransport), nameof(RelayTransport.JoinRoom)),
                prefix: new HarmonyMethod(typeof(RelaySession), nameof(BeforeJoinRoom)));

            harmony.Patch(
                AccessTools.Method(typeof(RelayTransport), nameof(RelayTransport.Relay_OnRoomJoined)),
                prefix: new HarmonyMethod(typeof(RelaySession), nameof(BeforeRoomJoined)));

            harmony.Patch(
                AccessTools.Method(typeof(RelayTransport), nameof(RelayTransport.Relay_OnRoomCreated)),
                postfix: new HarmonyMethod(typeof(RelaySession), nameof(AfterRoomCreated)));
            harmony.Patch(
                AccessTools.Method(typeof(RelayTransport), nameof(RelayTransport.Relay_OnRoomUpdated)),
                postfix: new HarmonyMethod(typeof(RelaySession), nameof(AfterRoomUpdated)));
            harmony.Patch(
                AccessTools.Method(typeof(RelayTransport), nameof(RelayTransport.Relay_OnRoomLeft)),
                postfix: new HarmonyMethod(typeof(RelaySession), nameof(AfterRoomLeft)));
            harmony.Patch(
                AccessTools.Method(typeof(RelayTransport), nameof(RelayTransport.Relay_OnCreateRoomFailed)),
                postfix: new HarmonyMethod(typeof(RelaySession), nameof(AfterCreateRoomFailed)));

            harmony.Patch(
                AccessTools.Method(typeof(LobbyMenu), nameof(LobbyMenu.CAMKLBFGGNH)),
                prefix: new HarmonyMethod(typeof(RelaySession), nameof(BeforeRoomCompatibility)));
        }

        internal void BeginSwitch(bool useNullFrame)
        {
            IsSwitching = true;
            IsNullFrame = useNullFrame;

            _connectionVersion = null;
            _roomVersions.Clear();
            _roomNameColors.Clear();

            _listedRooms = _visibleRooms = -1;

            if (Relay)
            {
                Relay._lastRoomList.Clear();
            }
        }

        internal void ApplyVersion(RelayTransport relay)
        {
            Relay = relay;
            _officialVersion ??= relay.GameVersion;
            relay.GameVersion = SelectedVersion;
        }

        private bool IsReady(RelayTransport relay)
        {
            return !IsSwitching && relay.IsConnectedToRelay && _connectionVersion == SelectedVersion;
        }

        internal string GetRoomNameColor(string roomId)
        {
            return _roomNameColors.GetValueOrDefault(roomId);
        }

        internal bool UpdateRoom(string name, int maxPlayers, string color, JsonObject changes, out string error)
        {
            error = "Only the current NullFrame host can change this room.";
            RelayRoomInfo current = Relay ? Relay.CurrentRoom : null;
            if (!IsNullFrame || current == null || !Relay.IsRoomHost || !IsReady(Relay))
            {
                return false;
            }

            if (!RoomName.TryParse(name, out var plain, out var display, out error))
            {
                return false;
            }

            if (maxPlayers < Math.Max(1, current.PlayerCount) || maxPlayers > RoomOptions.PlayerLimit)
            {
                error = $"Choose {Math.Max(1, current.PlayerCount)}–{RoomOptions.PlayerLimit} player slots.";
                return false;
            }

            if (!RoomOptions.IsValidColor(color))
            {
                error = "Use a color in #RRGGBB format.";
                return false;
            }

            JsonObject properties = JsonNode.Parse(current.RoomData).AsObject();
            if (changes != null)
            {
                foreach (var property in changes)
                {
                    properties[property.Key] = property.Value == null ? null : JsonNode.Parse(property.Value.ToJsonString());
                }
            }

            properties["Version"] = NullFrameGameVersion;
            properties["DisplayName"] = display;
            properties["NameColor"] = color.ToUpperInvariant();
            string data = properties.ToJsonString(JsonOptions);
            if (System.Text.Encoding.UTF8.GetByteCount(data) > 1024)
            {
                error = "The formatted room settings exceed the relay limit.";
                return false;
            }

            _editingRoom = true;
            try
            {
                if (Relay.UpdateRoom(plain, maxPlayers, current.IsVisible, data))
                {
                    error = null;
                    return true;
                }

                error = "The relay could not accept this update.";
                return false;
            }
            finally
            {
                _editingRoom = false;
            }
        }

        private static void AfterRelayInitialize(RelayTransport __instance)
        {
            _instance.ApplyVersion(__instance);
        }

        private static void BeforeConnect(RelayTransport __instance)
        {
            _instance.ApplyVersion(__instance);
        }

        private static void BeforeAuthentication(RelayTransport __instance, ClientConnectionStateArgs __0)
        {
            if (__instance.OfflineMode || __0.ConnectionState != LocalConnectionState.Started)
            {
                return;
            }

            _instance.ApplyVersion(__instance);
            _instance._connectionVersion = _instance.SelectedVersion;
        }

        private static void AfterAuthentication(RelayTransport __instance, RelayAuthResult __0)
        {
            if (__0 == RelayAuthResult.Success)
            {
                _instance._logger.Msg($"Relay authenticated. GameVersion: {_instance._connectionVersion}.");
            }
            else
            {
                _instance._connectionVersion = null;
                _instance._logger.Warning($"Relay authentication failed: {__0}.");
            }
        }

        private static string ReadRoomVersion(string roomData)
        {
            if (string.IsNullOrWhiteSpace(roomData))
            {
                return null;
            }

            try
            {
                using var document = JsonDocument.Parse(roomData);

                return document.RootElement.ValueKind == JsonValueKind.Object &&
                    document.RootElement.TryGetProperty("Version", out var version) &&
                    version.ValueKind == JsonValueKind.String
                    ? version.GetString()
                    : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static bool BeforeRoomList(
            RelayTransport __instance,
            ref Il2CppSystem.Collections.Generic.IReadOnlyList<RelayRoomInfo> __0)
        {
            if (__instance.OfflineMode)
            {
                return true;
            }

            if (!_instance.IsReady(__instance))
            {
                return false;
            }

            Il2CppSystem.Collections.Generic.List<RelayRoomInfo> rooms = new();
            int count = __0.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<RelayRoomInfo>>().Count;

            _instance._roomVersions.Clear();
            _instance._roomNameColors.Clear();

            for (int i = 0; i < count; i++)
            {
                RelayRoomInfo room = __0[i];

                string version = ReadRoomVersion(room.RoomData);

                _instance._roomVersions[room.Id] = version;

                if (version == _instance.SelectedVersion)
                {
                    rooms.Add(room);
                    _instance._roomNameColors[room.Id] = RoomOptions.ReadNameColor(room.RoomData);
                }
            }

            if (_instance._listedRooms != count || _instance._visibleRooms != rooms.Count)
            {
                _instance._logger.Msg($"Room list: {rooms.Count}/{count} rooms match {_instance.SelectedVersion}.");
            }

            _instance._listedRooms = count;
            _instance._visibleRooms = rooms.Count;

            __0 = rooms.Cast<Il2CppSystem.Collections.Generic.IReadOnlyList<RelayRoomInfo>>();
            return true;
        }

        private static bool BeforeRoomSettings(
            RelayTransport __instance,
            ref string __0,
            ref int __1,
            ref string __3,
            ref bool __result)
        {
            if (__instance.OfflineMode)
            {
                return true;
            }

            if (_instance.IsReady(__instance))
            {
                JsonObject properties = JsonNode.Parse(__3).AsObject();
                properties["Version"] = _instance.SelectedVersion;
                if (_instance.IsNullFrame && _instance.HoldRoomClosed)
                {
                    properties["IsOpen"] = false;
                }

                if (_instance.IsNullFrame)
                {
                    RelayRoomInfo current = __instance.CurrentRoom;
                    if (!_instance._editingRoom)
                    {
                        __1 = Math.Clamp(current?.MaxPlayers ?? _instance._options.MaxPlayers, 1, RoomOptions.PlayerLimit);
                        properties["NameColor"] = current == null
                            ? _instance._options.NameColor
                            : RoomOptions.ReadNameColor(current.RoomData) ?? _instance._options.NameColor;
                        if (current != null)
                        {
                            __0 = current.Name;
                            properties["DisplayName"] = RoomName.ReadDisplay(current.RoomData, current.Name);
                        }
                        else if (_instance._options.DisplayName != null)
                        {
                            if (!RoomName.TryParse(_instance._options.DisplayName, out var plain, out var display, out var error))
                            {
                                __result = false;
                                _instance._logger.Warning(error);
                                return false;
                            }

                            __0 = plain;
                            properties["DisplayName"] = display;
                        }
                    }

                    if (current != null)
                    {
                        foreach (var property in JsonNode.Parse(current.RoomData).AsObject())
                        {
                            if (!properties.ContainsKey(property.Key))
                            {
                                properties[property.Key] = property.Value == null ? null : JsonNode.Parse(property.Value.ToJsonString());
                            }
                        }
                    }
                }
                else
                {
                    properties.Remove("NameColor");
                    properties.Remove("DisplayName");
                }

                __3 = properties.ToJsonString(JsonOptions);
                return true;
            }

            __result = false;

            _instance._logger.Warning("Room action blocked: the selected server group is not connected.");
            return false;
        }

        private static bool BeforeJoinRoom(RelayTransport __instance, string __0, ref bool __result)
        {
            if (__instance.OfflineMode)
            {
                return true;
            }

            if (_instance.IsReady(__instance) &&
                (string.IsNullOrEmpty(__0) ||
                !_instance._roomVersions.TryGetValue(__0, out var version) ||
                version == _instance.SelectedVersion))
            {
                return true;
            }

            __result = false;

            _instance._logger.Warning("Join blocked: the room belongs to another server group or the connection is not ready.");
            return false;
        }

        private static void BeforeRoomJoined(RelayTransport __instance, RelayRoomInfo __0)
        {
            if (__instance.OfflineMode ||
                (_instance.IsReady(__instance) &&
                ReadRoomVersion(__0.RoomData) == _instance.SelectedVersion &&
                __0.MaxPlayers >= 1 &&
                __0.MaxPlayers <= (_instance.IsNullFrame ? RoomOptions.PlayerLimit : 12)))
            {
                return;
            }

            __instance.StopConnection(false);
            _instance._logger.Warning("Join cancelled: the room does not belong to the selected server group.");
        }

        private static void AfterRoomCreated(RelayRoomInfo __0)
        {
            if (!_instance.IsNullFrame)
            {
                return;
            }

            string color = RoomOptions.ReadNameColor(__0.RoomData);
            _instance._roomNameColors[__0.Id] = color;
            _instance._logger.Msg($"NullFrame room created. Id: {__0.Id}; Players: {__0.PlayerCount}/{__0.MaxPlayers}; NameColor: {color ?? "none"}.");
        }

        private static void AfterRoomUpdated(RelayRoomInfo __0)
        {
            if (_instance.IsNullFrame)
            {
                string color = RoomOptions.ReadNameColor(__0.RoomData);
                _instance._roomNameColors[__0.Id] = color;
                _instance._logger.Msg($"NullFrame room updated. Players: {__0.PlayerCount}/{__0.MaxPlayers}; NameColor: {color ?? "none"}.");
            }
        }

        private static void AfterCreateRoomFailed(RelayRoomResult __0)
        {
            if (_instance.IsNullFrame)
            {
                _instance._logger.Warning($"NullFrame room creation failed: {__0}.");
            }
        }

        private static void AfterRoomLeft(RelayTransport __instance, RelayRoomLeaveReason __0)
        {
            RelaySession instance = _instance;
            if (!instance.IsNullFrame ||
                instance.IsSwitching ||
                instance._returningToMenu ||
                (__0 != RelayRoomLeaveReason.Kicked && __0 != RelayRoomLeaveReason.KickedByModerator))
            {
                return;
            }

            instance._returningToMenu = true;
            MelonCoroutines.Start(instance.ReturnToMenu(__instance, __instance.CurrentRoom?.Id));
        }

        private IEnumerator ReturnToMenu(RelayTransport relay, string removedRoomId)
        {
            bool Cancelled() => IsSwitching ||
                Relay != relay ||
                !relay ||
                (relay.CurrentRoom != null && relay.CurrentRoom.Id != removedRoomId);

            try
            {
                yield return null;
                if (Cancelled())
                {
                    yield break;
                }

                NetBootstrap bootstrap = NetBootstrap.GJDGDIGECJN;
                if (!bootstrap)
                {
                    yield break;
                }

                Il2CppFishNet.Managing.NetworkManager manager = bootstrap.HLNKMGIBPIG;
                string menuScene = bootstrap._menuScene;
                bootstrap.HNHLFANHCFG = menuScene;
                bootstrap.NNLCAJPMCPG = null;
                if (manager.ClientManager.Started)
                {
                    manager.ClientManager.StopConnection();
                }

                if (relay)
                {
                    relay.DisconnectFromRelay();
                }

                float deadline = Time.realtimeSinceStartup + 10f;
                while (manager &&
                    (manager.ClientManager.Started || manager.SceneManager.IteratingQueue) &&
                    Time.realtimeSinceStartup < deadline)
                {
                    if (Cancelled())
                    {
                        yield break;
                    }

                    yield return null;
                }

                yield return null;
                if (Cancelled())
                {
                    yield break;
                }

                if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != menuScene)
                {
                    UnityEngine.SceneManagement.SceneManager.LoadScene(menuScene);
                }

                _logger.Msg("Removed from the room. Returned to the main menu.");
            }
            finally
            {
                _returningToMenu = false;
            }
        }

        private static bool BeforeRoomCompatibility(RelayRoomInfo __0, RoomProperties __1, ref bool __result)
        {
            if (_instance.Relay && _instance.Relay.OfflineMode)
            {
                return true;
            }

            __result = __0.MaxPlayers >= 1 &&
                __0.MaxPlayers <= (_instance.IsNullFrame ? RoomOptions.PlayerLimit : 12) &&
                !string.IsNullOrEmpty(__1.MapName) &&
                Application.CanStreamedLevelBeLoaded(__1.MapName) &&
                __1.Version == _instance.SelectedVersion &&
                __1.IsOpen;
            return false;
        }
    }
}
