using HarmonyLib;
using Il2Cpp;
using Il2CppFishNet.Connection;
using Il2CppFishNet.Managing;
using Il2CppFishNet.Managing.Client;
using Il2CppFishNet.Managing.Server;
using Il2CppFishNet.Object;
using Il2CppFishNet.Serializing;
using Il2CppFishNet.Transporting;
using Il2CppFishNet.Transporting.Relay;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader;
using MelonLoader.Utils;
using S3_NullServers.Network;
using S3_NullServers.Room;
using System.Text.Json;
using UnityEngine;

namespace S3_NullServers.Players
{
    internal sealed class PlayerProfiles
    {
        private static PlayerProfiles _instance;
        private readonly RelaySession _session;
        private readonly MelonLogger.Instance _logger;
        private readonly Dictionary<int, PlayerProfile> _profiles = new();
        private readonly Dictionary<int, PlayerProfile> _serverProfiles = new();
        private readonly Dictionary<IntPtr, (PlayerInfo Player, PlayerProfile Profile)> _playerProfiles = new();
        private readonly Dictionary<int, float> _lastSubmission = new();
        private readonly Dictionary<int, (string Account, string Nickname)> _forcedNames = new();
        private readonly string _path = Path.Combine(MelonEnvironment.UserDataDirectory, "NullFrame", "profile.json");
        private NetworkManager _manager;
        private string _roomId;
        private int _localId = -1;
        private float _nextUpdate;
        private float _nextSubmit;
        private bool _protocolAvailable = true;

        internal PlayerProfile Local { get; private set; } = PlayerProfile.Default;

        internal event Action<PlayerProfile> LocalChanged;

        internal event Action<NetworkConnection, RoundSignal> RoundSignalReceived;

        internal event Action<RoundSignal> RoundSignalReceivedFromServer;

        internal event Action<NetworkConnection, ServerMessage> ServerMessageReceived;

        internal event Action<ServerMessage> ServerMessageReceivedFromHost;

        internal event Action<NetworkConnection, SandboxMessage> SandboxReceived;

        internal event Action<SandboxMessage> SandboxReceivedFromHost;

        internal string DefaultName => _session.Relay?.LocalIdentity?.Username ?? "Player";

        private bool Active => _protocolAvailable &&
            _session.IsNullFrame &&
            !_session.IsSwitching &&
            _session.Relay &&
            _session.Relay.CurrentRoom != null;

        internal PlayerProfiles(HarmonyLib.Harmony harmony, RelaySession session, MelonLogger.Instance logger)
        {
            _instance = this;
            _session = session;
            _logger = logger;
            try
            {
                if (File.Exists(_path))
                {
                    PlayerProfile saved = JsonSerializer.Deserialize<PlayerProfile>(File.ReadAllText(_path));
                    if (saved != null)
                    {
                        Local = PlayerProfile.Normalize(saved.Nickname, saved.ColorHex);
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                logger.Warning($"Cannot load player profile: {exception.Message}");
            }

            harmony.Patch(
                AccessTools.Method(typeof(ServerManager), "ParseBroadcast"),
                prefix: new HarmonyMethod(typeof(PlayerProfiles), nameof(BeforeServerBroadcast)));
            harmony.Patch(
                AccessTools.Method(typeof(ClientManager), "ParseBroadcast"),
                prefix: new HarmonyMethod(typeof(PlayerProfiles), nameof(BeforeClientBroadcast)));
            harmony.Patch(
                AccessTools.PropertyGetter(typeof(PlayerInfo), nameof(PlayerInfo.HOGIPFLCOLH)),
                postfix: new HarmonyMethod(typeof(PlayerProfiles), nameof(AfterPlayerName)));
            harmony.Patch(
                AccessTools.Method(typeof(PlayerInfo), nameof(PlayerInfo.OnStartClient)),
                postfix: new HarmonyMethod(typeof(PlayerProfiles), nameof(AfterPlayerStarted)));
            harmony.Patch(
                AccessTools.PropertyGetter(typeof(PlayerInfo), nameof(PlayerInfo.DDOCMDMFLFE)),
                postfix: new HarmonyMethod(typeof(PlayerProfiles), nameof(AfterPlayerColor)));
            harmony.Patch(
                AccessTools.PropertyGetter(typeof(PlayerInfo), nameof(PlayerInfo.CDDPPCBBHGL)),
                postfix: new HarmonyMethod(typeof(PlayerProfiles), nameof(AfterHasPlayerColor)));
            harmony.Patch(
                AccessTools.Method(typeof(MultiplayerChat), nameof(MultiplayerChat.PECNIPKAEPC)),
                postfix: new HarmonyMethod(typeof(PlayerProfiles), nameof(AfterChatName)));
            harmony.Patch(
                AccessTools.Method(
                    typeof(GUILayout),
                    nameof(GUILayout.Label),
                    new[]
            {
                    typeof(string),
                    typeof(GUIStyle),
                    typeof(Il2CppReferenceArray<GUILayoutOption>)
                }),
                prefix: new HarmonyMethod(typeof(PlayerProfiles), nameof(BeforeChatLabel)),
                finalizer: new HarmonyMethod(typeof(PlayerProfiles), nameof(AfterChatLabel)));
            harmony.Patch(
                AccessTools.Method(typeof(DrawPlayerName), nameof(DrawPlayerName.OnGUI)),
                prefix: new HarmonyMethod(typeof(PlayerProfiles), nameof(BeforeDrawName)));
        }

        internal void Save(string nickname, string colorHex)
        {
            PlayerProfile profile = PlayerProfile.Normalize(nickname, colorHex);
            if (profile.Matches(Local))
            {
                return;
            }

            Local = profile;
            LocalChanged?.Invoke(profile);
            _nextSubmit = 0f;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path));
                File.WriteAllText(_path, JsonSerializer.Serialize(Local));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _logger.Warning($"Cannot save player profile: {exception.Message}");
            }
        }

        internal bool TryGet(int clientId, out PlayerProfile profile)
        {
            profile = null;
            return Active && _profiles.TryGetValue(clientId, out profile);
        }

        internal void Update()
        {
            if (Time.unscaledTime < _nextUpdate)
            {
                return;
            }

            _nextUpdate = Time.unscaledTime + 0.25f;
            foreach (IntPtr key in _playerProfiles.Where(pair => !pair.Value.Player).Select(pair => pair.Key).ToArray())
            {
                _playerProfiles.Remove(key);
            }

            NetBootstrap bootstrap = NetBootstrap.GJDGDIGECJN;
            NetworkManager manager = bootstrap ? bootstrap.HLNKMGIBPIG : null;
            SetConnection(manager);
            int clientId = _localId;
            if (clientId < 0)
            {
                return;
            }

            if (manager.ServerManager.Started)
            {
                foreach (int id in _serverProfiles.Keys.ToArray())
                {
                    if (!manager.ServerManager.Clients.ContainsKey(id))
                    {
                        _serverProfiles.Remove(id);
                        _lastSubmission.Remove(id);
                        _forcedNames.Remove(id);
                        Send(new ProfilePacket(1, ProfilePacketKind.Remove, new[] { new ProfileEntry(id, null) }));
                    }
                }
            }

            PlayerProfile local = PlayerProfile.Normalize(
                string.IsNullOrWhiteSpace(Local.Nickname) ? DefaultName : Local.Nickname,
                Local.ColorHex);
            if (Time.unscaledTime >= _nextSubmit &&
                (!_profiles.TryGetValue(clientId, out var confirmed) ||
                !local.Matches(confirmed)))
            {
                _nextSubmit = Time.unscaledTime + 2f;
                Send(ProfilePacket.Update(clientId, local), toServer: true);
            }
        }

        private void SetConnection(NetworkManager manager)
        {
            if (manager && manager != _manager)
            {
                _protocolAvailable = !manager.ClientManager._broadcastHandlers.ContainsKey(ProfilePacket.BroadcastId) &&
                    !manager.ServerManager._broadcastHandlers.ContainsKey(ProfilePacket.BroadcastId) &&
                    !manager.ClientManager._broadcastHandlers.ContainsKey(RoundSignal.BroadcastId) &&
                    !manager.ServerManager._broadcastHandlers.ContainsKey(RoundSignal.BroadcastId) &&
                    !manager.ClientManager._broadcastHandlers.ContainsKey(ServerMessage.BroadcastId) &&
                    !manager.ServerManager._broadcastHandlers.ContainsKey(ServerMessage.BroadcastId) &&
                    !manager.ClientManager._broadcastHandlers.ContainsKey(SandboxMessage.BroadcastId) &&
                    !manager.ServerManager._broadcastHandlers.ContainsKey(SandboxMessage.BroadcastId);
                if (!_protocolAvailable)
                {
                    _logger.Error("Player profile message identifier is already registered by another component.");
                }
            }

            NetworkConnection connection = manager ? manager.ClientManager.Connection : null;
            string roomId = Active ? _session.Relay.CurrentRoom.Id : null;
            int clientId = roomId != null &&
                manager &&
                manager.ClientManager.Started &&
                connection != null &&
                connection.IsAuthenticated
                ? connection.ClientId : -1;
            if (roomId != _roomId || (_localId >= 0 && clientId != _localId) || manager != _manager)
            {
                _profiles.Clear();
                _serverProfiles.Clear();
                _playerProfiles.Clear();
                _lastSubmission.Clear();
                _forcedNames.Clear();
                _nextSubmit = 0f;
            }

            _manager = manager;
            _roomId = roomId;
            _localId = clientId;
        }

        private void Send(ProfilePacket packet, NetworkConnection target = null, bool toServer = false)
            => SendPacket(ProfilePacket.BroadcastId, packet.Serialize(), target, toServer);

        internal void Synchronize()
        {
            if (Active && _manager && _manager.ServerManager.Started)
            {
                Send(new ProfilePacket(
                        1,
                        ProfilePacketKind.Snapshot,
                        _serverProfiles
                    .Where(pair => _manager.ServerManager.Clients.TryGetValue(pair.Key, out var connection) &&
                    connection.IsAuthenticated)
                    .Select(pair => new ProfileEntry(pair.Key, pair.Value)).ToArray()));
            }
        }

        internal void SendRoundSignal(RoundSignal signal, NetworkConnection target = null)
        {
            SendPacket(RoundSignal.BroadcastId, JsonSerializer.Serialize(signal), target, target == null);
        }

        internal void SendServerMessage(ServerMessage message, NetworkConnection target = null, bool toServer = false)
        {
            SendPacket(ServerMessage.BroadcastId, JsonSerializer.Serialize(message), target, toServer);
        }

        internal void SendSandbox(
            SandboxMessage message,
            NetworkConnection target = null,
            bool toServer = false,
            Channel channel = Channel.Reliable)
        {
            SendPacket(
                SandboxMessage.BroadcastId,
                JsonSerializer.Serialize(message, SandboxMessage.JsonOptions),
                target,
                toServer,
                channel);
        }

        internal void ForceNickname(int id, string name)
        {
            if (name == null)
            {
                _forcedNames.Remove(id);
            }
            else if (_session.Relay && _session.Relay.TryGetPlayerIdentity(id, out var identity))
            {
                _forcedNames[id] = (identity.Username, PlayerProfile.Normalize(name, "#FFFFFF").Nickname);
            }

            if (!_serverProfiles.TryGetValue(id, out var profile) || name == null)
            {
                return;
            }

            _serverProfiles[id] = PlayerProfile.Normalize(name, profile.ColorHex);
            RememberPlayers();
            Synchronize();
        }

        private void SendPacket(
            ushort messageId,
            string json,
            NetworkConnection target,
            bool toServer,
            Channel channel = Channel.Reliable)
        {
            if (!_manager)
            {
                return;
            }

            PooledWriter body = WriterPool.Retrieve();
            PooledWriter writer = WriterPool.Retrieve();
            try
            {
                body.WriteString(json);
                writer.WritePacketIdUnpacked(PacketId.Broadcast);
                writer.WriteUInt16(messageId);
                writer.WriteInt32(body.Length);
                writer.WriteArraySegment(body.GetArraySegment());
                if (toServer)
                {
                    _manager.TransportManager.SendToServer((byte)channel, writer.GetArraySegment(), DataOrderType.Default);
                }
                else if (target != null)
                {
                    _manager.TransportManager.SendToClient(
                        (byte)channel,
                        writer.GetArraySegment(),
                        target,
                        DataOrderType.Default);
                }
                else
                {
                    foreach (NetworkConnection client in _manager.ServerManager.Clients.Values)
                    {
                        if (client.IsAuthenticated)
                        {
                            _manager.TransportManager.SendToClient(
                                (byte)channel,
                                writer.GetArraySegment(),
                                client,
                                DataOrderType.Default);
                        }
                    }
                }
            }
            finally
            {
                writer.Store();
                body.Store();
            }
        }

        private static bool Read(PooledReader reader, out ProfilePacket packet)
        {
            packet = null;
            if (!ReadJson(reader, ProfilePacket.BroadcastId, ProfilePacket.MaxBytes, out var json))
            {
                return false;
            }

            ProfilePacket.TryParse(json, out packet);
            return true;
        }

        private static bool ReadJson(PooledReader reader, ushort messageId, int maxBytes, out string json)
        {
            json = null;
            if (reader.Remaining < 2)
            {
                return false;
            }

            int position = reader.Position;
            if (reader.ReadUInt16() != messageId)
            {
                reader.Position = position;
                return false;
            }

            try
            {
                int length = reader.ReadInt32();
                if (length < 1 || length > maxBytes || length > reader.Remaining)
                {
                    reader.Skip(reader.Remaining);
                    return true;
                }

                int end = reader.Position + length;
                int textPosition = reader.Position;
                int textLength = reader.ReadInt32();
                if (textLength > 0 && textLength <= end - reader.Position)
                {
                    reader.Position = textPosition;
                    json = reader.ReadString();
                }

                reader.Position = end;
            }
            catch (Il2CppInterop.Runtime.Il2CppException exception)
            {
                if (reader.Remaining > 0)
                {
                    reader.Skip(reader.Remaining);
                }

                _instance._logger.Warning($"Invalid player profile packet: {exception.Message}");
            }

            return true;
        }

        private static bool BeforeServerBroadcast(ServerManager __instance, PooledReader __0, NetworkConnection __1, Channel __2)
        {
            PlayerProfiles instance = _instance;
            if (instance.Active && ReadJson(__0, SandboxMessage.BroadcastId, SandboxMessage.MaxBytes, out var sandboxJson))
            {
                if (__1 != null &&
                    __1.IsAuthenticated &&
                    SandboxMessage.TryParse(sandboxJson, out var sandbox) &&
                    (__2 == Channel.Reliable || sandbox.Kind == SandboxKind.GunAim))
                {
                    instance.SandboxReceived?.Invoke(__1, sandbox);
                }

                return false;
            }

            if (__2 == Channel.Reliable &&
                instance.Active &&
                ReadJson(
                __0,
                ServerMessage.BroadcastId,
                ServerMessage.MaxBytes,
                out var controlJson))
            {
                if (__1 != null && __1.IsAuthenticated && ServerMessage.TryParse(controlJson, out var message))
                {
                    instance.ServerMessageReceived?.Invoke(__1, message);
                }

                return false;
            }

            if (__2 == Channel.Reliable &&
                instance.Active &&
                ReadJson(
                __0,
                RoundSignal.BroadcastId,
                RoundSignal.MaxBytes,
                out var json))
            {
                if (__1 != null && __1.IsAuthenticated && RoundSignal.TryParse(json, out var signal))
                {
                    instance.RoundSignalReceived?.Invoke(__1, signal);
                }

                return false;
            }

            if (__2 != Channel.Reliable || !instance.Active || !Read(__0, out var packet))
            {
                return true;
            }

            if (packet == null || __1 == null || !__1.IsAuthenticated || packet.Kind != ProfilePacketKind.Profile)
            {
                return false;
            }

            int id = __1.ClientId;
            if (instance._lastSubmission.TryGetValue(id, out var last) && Time.unscaledTime - last < 0.5f)
            {
                return false;
            }

            instance.SetConnection(__instance.NetworkManager);
            instance._lastSubmission[id] = Time.unscaledTime;
            PlayerProfile profile = packet.Players[0].Profile;
            if (instance._forcedNames.TryGetValue(id, out var forced))
            {
                if (instance._session.Relay.TryGetPlayerIdentity(id, out var identity) && identity.Username == forced.Account)
                {
                    profile = PlayerProfile.Normalize(forced.Nickname, profile.ColorHex);
                }
                else
                {
                    instance._forcedNames.Remove(id);
                }
            }

            instance._serverProfiles[id] = profile;
            instance.RememberPlayers();
            instance.Send(
                new ProfilePacket(
                    1,
                    ProfilePacketKind.Snapshot,
                    instance._serverProfiles.Where(pair => __instance.Clients.ContainsKey(pair.Key))
                    .Select(pair => new ProfileEntry(pair.Key, pair.Value)).ToArray()),
                __1);
            instance.Send(ProfilePacket.Update(id, profile));
            instance._logger.Msg($"Player profile accepted. Client: {id}; Nickname: {profile.Nickname}; Color: {profile.ColorHex}.");
            return false;
        }

        private static bool BeforeClientBroadcast(ClientManager __instance, PooledReader __0, Channel __1)
        {
            PlayerProfiles instance = _instance;
            if (instance.Active && ReadJson(__0, SandboxMessage.BroadcastId, SandboxMessage.MaxBytes, out var sandboxJson))
            {
                if (SandboxMessage.TryParse(sandboxJson, out var sandbox) &&
                    (__1 == Channel.Reliable ||
                    sandbox.Kind is SandboxKind.Pose or SandboxKind.PoseBatch or SandboxKind.GunState))
                {
                    instance.SandboxReceivedFromHost?.Invoke(sandbox);
                }

                return false;
            }

            if (__1 == Channel.Reliable &&
                instance.Active &&
                ReadJson(
                __0,
                ServerMessage.BroadcastId,
                ServerMessage.MaxBytes,
                out var controlJson))
            {
                if (ServerMessage.TryParse(controlJson, out var message))
                {
                    instance.ServerMessageReceivedFromHost?.Invoke(message);
                }

                return false;
            }

            if (__1 == Channel.Reliable &&
                instance.Active &&
                ReadJson(
                __0,
                RoundSignal.BroadcastId,
                RoundSignal.MaxBytes,
                out var json))
            {
                if (RoundSignal.TryParse(json, out var signal))
                {
                    instance.RoundSignalReceivedFromServer?.Invoke(signal);
                }

                return false;
            }

            if (__1 != Channel.Reliable || !instance.Active || !Read(__0, out var packet))
            {
                return true;
            }

            if (packet == null)
            {
                return false;
            }

            instance.SetConnection(__instance.NetworkManager);
            PlayerProfile previous = instance._profiles.GetValueOrDefault(instance._localId);
            if (packet.Kind == ProfilePacketKind.Snapshot)
            {
                instance._profiles.Clear();
            }

            foreach (ProfileEntry entry in packet.Players)
            {
                if (packet.Kind == ProfilePacketKind.Remove)
                {
                    instance._profiles.Remove(entry.ClientId);
                }
                else
                {
                    instance._profiles[entry.ClientId] = entry.Profile;
                }
            }

            instance.RememberPlayers();
            if (instance._profiles.TryGetValue(instance._localId, out var local) && !local.Matches(previous))
            {
                instance._logger.Msg($"Player profile synchronized. Client: {instance._localId}; Nickname: {local.Nickname}; Color: {local.ColorHex}.");
            }

            return false;
        }

        private static void AfterPlayerName(PlayerInfo __instance, ref string __result)
        {
            if (_instance.TryPlayerProfile(__instance, out var profile))
            {
                __result = profile.Nickname;
            }
        }

        private void RememberPlayers()
        {
            foreach (PlayerInfo player in UnityEngine.Object.FindObjectsOfType<PlayerInfo>())
            {
                if (_serverProfiles.TryGetValue(player.COJDPMKCLCM, out var profile) ||
                    _profiles.TryGetValue(player.COJDPMKCLCM, out profile))
                {
                    _playerProfiles[player.Pointer] = (player, profile);
                }
            }
        }

        private bool TryPlayerProfile(PlayerInfo player, out PlayerProfile profile)
        {
            profile = null;
            if (_session.IsNullFrame && !_session.IsSwitching && _playerProfiles.TryGetValue(player.Pointer, out var cached))
            {
                profile = cached.Profile;
                return true;
            }

            if (!TryGet(player.COJDPMKCLCM, out profile))
            {
                return false;
            }

            _playerProfiles[player.Pointer] = (player, profile);
            return true;
        }

        private static void AfterPlayerStarted(PlayerInfo __instance)
        {
            if (_instance.TryGet(__instance.COJDPMKCLCM, out var profile))
            {
                _instance._playerProfiles[__instance.Pointer] = (__instance, profile);
            }
        }

        private static void AfterPlayerColor(PlayerInfo __instance, ref Color __result)
        {
            if (_instance.TryPlayerProfile(__instance, out var profile))
            {
                ColorUtility.TryParseHtmlString(profile.ColorHex, out __result);
            }
        }

        private static void AfterHasPlayerColor(PlayerInfo __instance, ref bool __result)
        {
            if (_instance.TryPlayerProfile(__instance, out _))
            {
                __result = true;
            }
        }

        private static void AfterChatName(int __0, ref string __result)
        {
            if (_instance.TryGet(__0, out var profile))
            {
                __result = profile.RichName;
            }
        }

        private static void BeforeChatLabel(ref string __0, GUIStyle __1, out Color? __state)
        {
            const string prefix = "  <color=";
            const string suffix = "</color>: ";
            const int colorLength = 7;
            int nameStart = prefix.Length + colorLength + 1;
            __state = null;
            if (__0 == null ||
                __0.Length <= nameStart + suffix.Length ||
                !__0.StartsWith(prefix, StringComparison.Ordinal) ||
                __0[prefix.Length] != '#' ||
                __0[nameStart - 1] != '>' ||
                !__0.EndsWith(suffix, StringComparison.Ordinal) ||
                !_instance.Active ||
                __1 == null)
            {
                return;
            }

            MultiplayerChat chat = MultiplayerChat.CFNPNLGIHMH;
            if (!chat ||
                chat.JELNPHBIKHK == null ||
                __1.Pointer != chat.JELNPHBIKHK.Pointer ||
                !ColorUtility.TryParseHtmlString(__0.Substring(prefix.Length, colorLength), out var color))
            {
                return;
            }

            // The native style is shared with the input and message body, which must keep treating tags as text.
            __0 = "  " + __0.Substring(nameStart, __0.Length - nameStart - suffix.Length) + ": ";
            __state = GUI.color;
            GUI.color = color;
        }

        private static void AfterChatLabel(Color? __state)
        {
            if (__state.HasValue)
            {
                GUI.color = __state.Value;
            }
        }

        private static void BeforeDrawName(DrawPlayerName __instance)
        {
            PlayerNetworkController controller = __instance.JGOGBDGJKAG;
            if (controller && _instance.TryGet(controller.OwnerId, out var profile))
            {
                __instance.DJHKBCAIADF = profile.Nickname;
                controller.DJHKBCAIADF = profile.Nickname;
                controller.AJGABBOMGJB = true;
                ColorUtility.TryParseHtmlString(profile.ColorHex, out var color);
                controller.NDHICLFEEKL = color;
            }
        }
    }
}
