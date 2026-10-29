using Il2Cpp;
using Il2CppFishNet.Connection;
using Il2CppFishNet.Managing;
using Il2CppFishNet.Managing.Client;
using Il2CppFishNet.Managing.Server;
using Il2CppFishNet.Object;
using Il2CppFishNet.Serializing;
using Il2CppFishNet.Transporting;
using MelonLoader;
using MelonLoader.Utils;
using NullServers.Content;
using NullServers.Network;
using NullServers.Room;
using System.Text.Json;
using UnityEngine;

namespace NullServers.Players
{
    internal sealed class PlayerProfiles
    {
        private static PlayerProfiles _instance;
        private readonly RelaySession _session;
        private int _mtu;
        private readonly MelonLogger.Instance _logger;
        private readonly Dictionary<int, PlayerProfile> _profiles = new();
        private readonly Dictionary<int, PlayerProfile> _serverProfiles = new();
        private readonly Dictionary<IntPtr, (PlayerInfo Player, PlayerProfile Profile)> _playerProfiles = new();
        private readonly Dictionary<int, float> _lastSubmission = new();
        private readonly Dictionary<int, (string Account, string Nickname, float At)> _forcedNames = new();
        private readonly Dictionary<string, float> _sentNames = new(StringComparer.Ordinal);
        private string _nicknameOverride;
        private readonly Dictionary<string, PlayerProfile> _chatProfiles = new();
        private static PlayerProfile _drawingProfile;
        private readonly string _path = Path.Combine(MelonEnvironment.UserDataDirectory, "NullFrame", "profile.json");
        private NetworkManager _manager;
        private string _roomId;
        private int _localId = -1;
        private float _nextUpdate;
        private float _nextSubmit;
        private bool _protocolAvailable = true;

        internal FrameworkCompatibility Compatibility { get; set; }
        internal bool ProtocolAvailable => _protocolAvailable;
        internal event Action<NetworkConnection, FrameworkMessage> FrameworkReceived;
        internal event Action<FrameworkMessage> FrameworkReceivedFromHost;
        internal event Action<PlayerProfile> LocalChanged;
        internal event Action<NetworkConnection, RoundSignal> RoundSignalReceived;
        internal event Action<RoundSignal> RoundSignalReceivedFromServer;
        internal event Action<NetworkConnection, ServerMessage> ServerMessageReceived;
        internal event Action<ServerMessage> ServerMessageReceivedFromHost;
        internal event Action<NetworkConnection, SandboxMessage> SandboxReceived;
        internal event Action<SandboxMessage> SandboxReceivedFromHost;
        internal event Action<NetworkConnection, HatPacket> HatsReceived;
        internal event Action<NetworkConnection, ContentMessage> ContentReceived;
        internal event Action<ContentMessage> ContentReceivedFromHost;
        internal event Action<HatPacket> HatsReceivedFromHost;
        internal event Action<NetworkConnection, VoicePacket> VoiceReceived;
        internal event Action<VoicePacket> VoiceReceivedFromHost;

        internal PlayerProfile Local { get; private set; } = PlayerProfile.Default;

        internal string DefaultName => _session.Relay?.LocalIdentity?.Username ?? "Player";

        private bool Active => _protocolAvailable &&
                               _session.IsNullFrame &&
                               !_session.IsSwitching &&
                               _session.Relay &&
                               _session.Relay.CurrentRoom != null;

        // True while incoming NullServers messages are read here instead of by FishNet.
        internal static bool ReadsMessages => _instance?.Active == true;

        internal PlayerProfiles(RelaySession session, MelonLogger.Instance logger)
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
                        Local = PlayerProfile.Normalize(saved.Nickname, saved.ColorHex, saved.ColorMode, saved.GradientColor, saved.ColorSpeed);
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                logger.Warning($"Cannot load player profile: {exception.Message}");
            }
        }

        internal void Save(
            string nickname,
            string colorHex,
            NameColorMode? colorMode = null,
            string gradientColor = null,
            float? colorSpeed = null)
        {
            PlayerProfile profile = PlayerProfile.Normalize(
                nickname,
                colorHex,
                colorMode ?? Local.ColorMode,
                gradientColor ?? Local.GradientColor,
                colorSpeed ?? Local.ColorSpeed);
            if (_nicknameOverride != null)
            {
                _nicknameOverride = null;
                _nextSubmit = 0f;
            }

            if (profile.Matches(Local)) return;

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
            if (Time.unscaledTime < _nextUpdate) return;

            _nextUpdate = Time.unscaledTime + 0.25f;

            foreach (IntPtr key in _playerProfiles.Where(pair => !pair.Value.Player).Select(pair => pair.Key).ToArray())
            {
                _playerProfiles.Remove(key);
            }

            NetBootstrap bootstrap = NetBootstrap.Current;
            NetworkManager manager = bootstrap ? bootstrap.Network : null;
            SetConnection(manager);

            int clientId = _localId;

            if (clientId < 0 || Compatibility?.Ready != true)
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
                        Send(new ProfilePacket(1, ProfilePacketKind.Remove, [new ProfileEntry(id, null)]));
                    }
                }
            }

            PlayerProfile local = Submitted();

            if (Time.unscaledTime >= _nextSubmit &&
                (!_profiles.TryGetValue(clientId, out var confirmed) ||
                !local.Matches(confirmed)))
            {
                _nextSubmit = Time.unscaledTime + 2f;
                _sentNames[local.Nickname] = Time.unscaledTime;
                Send(ProfilePacket.Update(clientId, local), toServer: true);
            }
        }

        private PlayerProfile Submitted() => PlayerProfile.Normalize(
            _nicknameOverride ?? (string.IsNullOrWhiteSpace(Local.Nickname) ? DefaultName : Local.Nickname),
            Local.ColorHex,
            Local.ColorMode,
            Local.GradientColor,
            Local.ColorSpeed);

        private void SetConnection(NetworkManager manager)
        {
            if (manager && manager != _manager)
            {
                _protocolAvailable = !manager.ClientManager._broadcastHandlers.ContainsKey(FrameworkMessage.BroadcastId) &&
                    !manager.ServerManager._broadcastHandlers.ContainsKey(FrameworkMessage.BroadcastId) &&
                    !manager.ClientManager._broadcastHandlers.ContainsKey(ProfilePacket.BroadcastId) &&
                    !manager.ServerManager._broadcastHandlers.ContainsKey(ProfilePacket.BroadcastId) &&
                    !manager.ClientManager._broadcastHandlers.ContainsKey(RoundSignal.BroadcastId) &&
                    !manager.ServerManager._broadcastHandlers.ContainsKey(RoundSignal.BroadcastId) &&
                    !manager.ClientManager._broadcastHandlers.ContainsKey(ServerMessage.BroadcastId) &&
                    !manager.ServerManager._broadcastHandlers.ContainsKey(ServerMessage.BroadcastId) &&
                    !manager.ClientManager._broadcastHandlers.ContainsKey(SandboxMessage.BroadcastId) &&
                    !manager.ServerManager._broadcastHandlers.ContainsKey(SandboxMessage.BroadcastId) &&
                    !manager.ClientManager._broadcastHandlers.ContainsKey(HatPacket.BroadcastId) &&
                    !manager.ServerManager._broadcastHandlers.ContainsKey(HatPacket.BroadcastId) &&
                    !manager.ClientManager._broadcastHandlers.ContainsKey(ContentMessage.BroadcastId) &&
                    !manager.ServerManager._broadcastHandlers.ContainsKey(ContentMessage.BroadcastId) &&
                    !manager.ClientManager._broadcastHandlers.ContainsKey(VoicePacket.BroadcastId) &&
                    !manager.ServerManager._broadcastHandlers.ContainsKey(VoicePacket.BroadcastId);

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
                _chatProfiles.Clear();
                _sentNames.Clear();
                _nicknameOverride = null;
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
                Send(
                    new ProfilePacket(
                        1,
                        ProfilePacketKind.Snapshot,
                        _serverProfiles
                        .Where(pair => _manager.ServerManager.Clients.TryGetValue(pair.Key, out var connection) &&
                        connection.IsAuthenticated)
                        .Select(pair => new ProfileEntry(pair.Key, pair.Value)).ToArray()));
            }
        }

        internal void SendFramework(FrameworkMessage message, NetworkConnection target = null)
        {
            SendPacket(FrameworkMessage.BroadcastId, JsonSerializer.Serialize(message), target, target == null);
        }

        internal void SendRoundSignal(RoundSignal signal, NetworkConnection target = null)
        {
            SendPacket(RoundSignal.BroadcastId, JsonSerializer.Serialize(signal), target, target == null);
        }

        internal void SendServerMessage(ServerMessage message, NetworkConnection target = null, bool toServer = false)
        {
            SendPacket(ServerMessage.BroadcastId, JsonSerializer.Serialize(message), target, toServer);
        }

        internal int PacketRoom
        {
            get
            {
                if (_mtu == 0 && _session.Relay)
                {
                    try
                    {
                        _mtu = Math.Clamp(_session.Relay.GetMTU((byte)Channel.Reliable), 512, 1200);
                    }
                    catch (Il2CppInterop.Runtime.Il2CppException)
                    {
                        _mtu = 1000;
                    }
                }

                return (_mtu == 0 ? 1000 : _mtu) - 64;
            }
        }

        internal void SendContent(ContentMessage message, NetworkConnection target = null, bool toServer = false)
        {
            SendPacket(ContentMessage.BroadcastId, message.Serialize(), target, toServer);
        }

        internal void SendVoice(VoicePacket packet, NetworkConnection target = null, bool toServer = false)
            => SendPacket(VoicePacket.BroadcastId, packet.Serialize(), target, toServer, Channel.Unreliable);

        internal void SendHats(HatPacket packet, NetworkConnection target = null, bool toServer = false)
        {
            SendPacket(HatPacket.BroadcastId, JsonSerializer.Serialize(packet), target, toServer);
        }

        internal void SendSandbox(SandboxMessage message, NetworkConnection target = null, bool toServer = false, Channel channel = Channel.Reliable)
            => SendSandboxJson(SandboxMessage.Serialize(message), target, toServer, channel);

        internal void SendSandboxJson(string json, NetworkConnection target = null, bool toServer = false, Channel channel = Channel.Reliable)
            => SendPacket(SandboxMessage.BroadcastId, json, target, toServer, channel);

        internal void ForceNickname(int id, string name)
        {
            if (name == null)
            {
                _forcedNames.Remove(id);
            }
            else if (_session.Relay && _session.Relay.TryGetPlayerIdentity(id, out var identity))
            {
                _forcedNames[id] = (identity.Username, PlayerProfile.Normalize(name, "#FFFFFF").Nickname, Time.unscaledTime);
            }

            if (!_serverProfiles.TryGetValue(id, out var profile) || name == null) return;

            _serverProfiles[id] = PlayerProfile.Normalize(name, profile.ColorHex, profile.ColorMode, profile.GradientColor, profile.ColorSpeed);
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
            if (!_manager) return;

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
                        if (!client.IsAuthenticated) continue;

                        _manager.TransportManager.SendToClient(
                            (byte)channel,
                            writer.GetArraySegment(),
                            client,
                            DataOrderType.Default);
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

            if (reader.Remaining < 2) return false;

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

                _instance._logger.Debug($"Invalid NullFrame packet 0x{messageId:X4}: {exception.Message}");
            }

            return true;
        }

        // Reads one NullServers message from a FishNet broadcast. Returns true when the broadcast was ours.
        private bool ReadMessages(PooledReader reader, Channel channel, NetworkConnection connection, bool fromHost)
        {
            if (!Active) return false;

            bool reliable = channel == Channel.Reliable;
            bool authenticated = fromHost || connection != null && connection.IsAuthenticated;

            if (reliable && ReadJson(reader, FrameworkMessage.BroadcastId, FrameworkMessage.MaxBytes, out string frameworkJson))
            {
                if (authenticated && FrameworkMessage.TryParse(frameworkJson, out var framework))
                {
                    Dispatch(fromHost, connection, framework, FrameworkReceivedFromHost, FrameworkReceived);
                }

                return true;
            }

            if (reliable && ReadJson(reader, ContentMessage.BroadcastId, ContentMessage.MaxBytes, out string contentJson))
            {
                if (authenticated && ContentMessage.TryParse(contentJson, out var content))
                {
                    Dispatch(fromHost, connection, content, ContentReceivedFromHost, ContentReceived);
                }

                return true;
            }

            // Everything else is accepted only after the framework handshake confirmed the player's content.
            bool allowed = fromHost ? Compatibility?.Ready == true : Compatibility?.Allows(connection) == true;

            if (ReadJson(reader, VoicePacket.BroadcastId, VoicePacket.MaxBytes, out string voiceJson))
            {
                if (allowed && VoicePacket.TryParse(voiceJson, out var voice))
                {
                    Dispatch(fromHost, connection, voice, VoiceReceivedFromHost, VoiceReceived);
                }

                return true;
            }

            if (ReadJson(reader, SandboxMessage.BroadcastId, SandboxMessage.MaxBytes, out string sandboxJson))
            {
                if (allowed && SandboxMessage.TryParse(sandboxJson, out var sandbox) && sandbox.AllowsChannel(fromHost, reliable))
                {
                    Dispatch(fromHost, connection, sandbox, SandboxReceivedFromHost, SandboxReceived);
                }

                return true;
            }

            if (!reliable) return false;

            if (ReadJson(reader, HatPacket.BroadcastId, HatPacket.MaxBytes, out string hatsJson))
            {
                if (allowed && HatPacket.TryParse(hatsJson, out var hats))
                {
                    Dispatch(fromHost, connection, hats, HatsReceivedFromHost, HatsReceived);
                }

                return true;
            }

            if (ReadJson(reader, ServerMessage.BroadcastId, ServerMessage.MaxBytes, out string controlJson))
            {
                if (allowed && ServerMessage.TryParse(controlJson, out var message))
                {
                    Dispatch(fromHost, connection, message, ServerMessageReceivedFromHost, ServerMessageReceived);
                }

                return true;
            }

            if (ReadJson(reader, RoundSignal.BroadcastId, RoundSignal.MaxBytes, out string signalJson))
            {
                if (authenticated && RoundSignal.TryParse(signalJson, out var signal))
                {
                    Dispatch(fromHost, connection, signal, RoundSignalReceivedFromServer, RoundSignalReceived);
                }

                return true;
            }

            return false;
        }

        private static void Dispatch<T>(bool fromHost, NetworkConnection connection, T message, Action<T> fromHostHandler, Action<NetworkConnection, T> fromClientHandler)
        {
            if (fromHost)
            {
                fromHostHandler?.Invoke(message);
            }
            else
            {
                fromClientHandler?.Invoke(connection, message);
            }
        }

        internal static bool BeforeServerBroadcast(
            ServerManager __instance,
            PooledReader reader,
            NetworkConnection conn,
            Channel channel)
        {
            PlayerProfiles instance = _instance;
            if (instance.ReadMessages(reader, channel, conn, fromHost: false)) return false;

            if (channel != Channel.Reliable || !instance.Active || !Read(reader, out var packet))
            {
                return true;
            }

            if (packet == null ||
                conn == null ||
                !conn.IsAuthenticated ||
                instance.Compatibility?.Allows(conn) != true ||
                packet.Kind != ProfilePacketKind.Profile)
            {
                return false;
            }

            int id = conn.ClientId;
            if (instance._lastSubmission.TryGetValue(id, out var last) && Time.unscaledTime - last < 0.5f)
            {
                return false;
            }

            instance.SetConnection(__instance.NetworkManager);
            instance._lastSubmission[id] = Time.unscaledTime;
            PlayerProfile profile = packet.Players[0].Profile;

            if (instance._forcedNames.TryGetValue(id, out var forced))
            {
                if (instance._session.Relay.TryGetPlayerIdentity(id, out var identity) && identity.Username == forced.Account &&
                    Time.unscaledTime - forced.At < 5f)
                {
                    profile = PlayerProfile.Normalize(forced.Nickname, profile.ColorHex, profile.ColorMode, profile.GradientColor, profile.ColorSpeed);
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
                conn);

            instance.Send(ProfilePacket.Update(id, profile));
            instance._logger.Debug($"Player profile accepted. Client: {id}; Nickname: {profile.Nickname}; Color: {profile.ColorHex}.");

            return false;
        }

        internal static bool BeforeClientBroadcast(ClientManager __instance, PooledReader reader, Channel channel)
        {
            PlayerProfiles instance = _instance;
            if (instance.ReadMessages(reader, channel, null, fromHost: true)) return false;

            if (channel != Channel.Reliable || !instance.Active || !Read(reader, out var packet))
            {
                return true;
            }

            if (packet == null || instance.Compatibility?.Ready != true)
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
            if (packet.Kind != ProfilePacketKind.Remove && instance._profiles.TryGetValue(instance._localId, out var mine) &&
                mine.Nickname != instance.Submitted().Nickname &&
                !(instance._sentNames.TryGetValue(mine.Nickname, out var sentAt) && Time.unscaledTime - sentAt < 3f))
            {
                instance._nicknameOverride = mine.Nickname;
            }

            if (instance._profiles.TryGetValue(instance._localId, out var local) && !local.Matches(previous))
            {
                instance._logger.Debug($"Player profile synchronized. Client: {instance._localId}; Nickname: {local.Nickname}; Color: {local.ColorHex}.");
            }

            return false;
        }

        internal static void AfterPlayerName(PlayerInfo __instance, ref string __result)
        {
            if (!_instance.TryPlayerProfile(__instance, out var profile)) return;

            __result = profile.Nickname;
        }

        internal static void AfterNoticeName(ref string __result)
        {
            PlayerProfiles profiles = _instance;
            if (!profiles.Active) return;

            PlayerProfile profile = profiles.Local;
            if (!string.IsNullOrWhiteSpace(profile.Nickname))
            {
                __result = profile.Nickname;
            }
        }

        private void RememberPlayers()
        {
            foreach (IntPtr key in _playerProfiles.Keys.ToArray())
            {
                PlayerInfo player = _playerProfiles[key].Player;
                if (player && _profiles.TryGetValue(player.ClientId, out var profile))
                {
                    _playerProfiles[key] = (player, profile);
                }
                else
                {
                    _playerProfiles.Remove(key);
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

            if (!TryGet(player.ClientId, out profile)) return false;

            _playerProfiles[player.Pointer] = (player, profile);

            return true;
        }

        internal static void AfterPlayerStarted(PlayerInfo __instance)
        {
            if (!_instance.TryGet(__instance.ClientId, out var profile)) return;

            _instance._playerProfiles[__instance.Pointer] = (__instance, profile);
        }

        internal static void AfterPlayerColor(PlayerInfo __instance, ref Color __result)
        {
            if (!_instance.TryPlayerProfile(__instance, out var profile)) return;

            ColorUtility.TryParseHtmlString(profile.CurrentColor, out __result);
        }

        internal static void AfterHasPlayerColor(PlayerInfo __instance, ref bool __result)
        {
            if (_instance.TryPlayerProfile(__instance, out _))
            {
                __result = true;
            }
        }

        internal static void AfterChatName(int __0, ref string __result)
        {
            if (_instance.TryGet(__0, out var profile))
            {
                __result = profile.RichName;
                if (_instance._chatProfiles.Count >= 1024)
                {
                    _instance._chatProfiles.Clear();
                }

                _instance._chatProfiles[__result] = profile;
            }
        }

        internal static void BeforeChatLabel(ref string __0, GUIStyle __1, out (bool Active, bool RichText, Color Color) __state)
        {
            __state = default;
            if (__0 == null || __0.Length < 4 || !__0.StartsWith("  ", StringComparison.Ordinal) ||
                !__0.EndsWith(": ", StringComparison.Ordinal) || !_instance.Active || __1 == null)
            {
                return;
            }

            MultiplayerChat chat = MultiplayerChat.Current;
            if (!chat ||
                chat.NameStyle == null ||
                __1.Pointer != chat.NameStyle.Pointer ||
                !_instance._chatProfiles.TryGetValue(__0[2..^2], out var profile))
            {
                return;
            }

            __0 = "  " + profile.RichName + ": ";
            __state = (true, __1.richText, GUI.color);
            __1.richText = true;
            GUI.color = Color.white;
        }

        internal static void AfterChatLabel(GUIStyle __1, (bool Active, bool RichText, Color Color) __state)
        {
            if (!__state.Active) return;

            __1.richText = __state.RichText;
            GUI.color = __state.Color;
        }

        internal static void BeforeNameLabel(ref string __1, GUIStyle __2, out (bool Active, bool RichText, Color Color) __state)
        {
            __state = default;
            if (_drawingProfile == null || __1 != _drawingProfile.Nickname || __2 == null) return;

            __state = (true, __2.richText, GUI.color);
            __1 = _drawingProfile.RichName;
            __2.richText = true;
            GUI.color = Color.white;
        }

        internal static void AfterNameLabel(GUIStyle __2, (bool Active, bool RichText, Color Color) __state)
        {
            if (!__state.Active) return;

            __2.richText = __state.RichText;
            GUI.color = __state.Color;
        }

        internal static void AfterDrawName() => _drawingProfile = null;

        internal static void BeforeDrawName(DrawPlayerName __instance)
        {
            _drawingProfile = null;
            PlayerNetworkController controller = __instance.Player;

            if (controller && _instance.TryGet(controller.OwnerId, out var profile))
            {
                _drawingProfile = profile;
                __instance.DisplayName = profile.Nickname;
                controller.DisplayName = profile.Nickname;
                controller.HasNameColor = true;

                ColorUtility.TryParseHtmlString(profile.CurrentColor, out Color color);
                controller.NameColor = color;
            }
        }
    }
}
