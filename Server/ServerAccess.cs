using HarmonyLib;
using Il2Cpp;
using Il2CppFishNet.Connection;
using Il2CppFishNet.Transporting.Relay;
using MelonLoader.Utils;
using NullServers.Network;
using NullServers.Players;
using NullServers.Room;
using System.Text.Json;
using UnityEngine;

namespace NullServers.Server
{
    internal sealed class ServerAccess
    {
        private static ServerAccess _instance;
        private readonly ServerControl _control;
        private readonly RelaySession _session;
        private readonly PlayerProfiles _profiles;
        private readonly RoomPermissions _permissions = new();
        private readonly Dictionary<int, string> _sentStates = new();
        private readonly Dictionary<int, float> _lastRequest = new();
        private readonly Dictionary<int, float> _lastMuteNotice = new();
        private ServerState _state;
        private string _roomId;
        private float _nextState;
        private string _pendingNotice;
        private EnvironmentSettings _environment = new();

        internal BanList Mutes { get; } = new(
            Path.Combine(MelonEnvironment.UserDataDirectory, "NullFrame", "mutes.json"),
            "mute list");

        internal bool CanOpen => _control.CanManage || (InRoom && _roomId == _control.Room.Id && _state?.Trusted == true);

        internal string Status { get; private set; }
        internal EnvironmentSettings Environment => _control.CanManage ? _environment : _state?.Environment ?? new EnvironmentSettings();

        internal bool IsAdministrator(int id)
        {
            return _control.CanManage &&
                (id == RelayProtocol.HostPlayerId ||
                _control.TryIdentity(id, out var identity) &&
                _permissions.Allows(id, identity.Username));
        }

        private bool InRoom => _session.IsNullFrame && !_session.IsSwitching && _control.Room != null;

        internal IReadOnlyList<RoomPlayer> Players => _control.CanManage ? HostPlayers() : _state?.Players ?? Array.Empty<RoomPlayer>();
        internal IReadOnlyList<BanEntry> Bans => _control.CanManage ? _control.Bans.Entries : _state?.Bans ?? Array.Empty<BanEntry>();
        internal IReadOnlyList<BanEntry> MutedAccounts => _control.CanManage ? Mutes.Entries : _state?.Mutes ?? Array.Empty<BanEntry>();

        internal ServerAccess(HarmonyLib.Harmony harmony, ServerControl control, RelaySession session, PlayerProfiles profiles)
        {
            _instance = this;
            _control = control;
            _session = session;
            _profiles = profiles;

            profiles.ServerMessageReceived += ReceiveRequest;
            profiles.ServerMessageReceivedFromHost += ReceiveState;

            harmony.Patch(
                AccessTools.Method(typeof(MultiplayerChat), nameof(MultiplayerChat.NBHNJMHGGCL)),
                prefix: new HarmonyMethod(typeof(ServerAccess), nameof(BeforeChatMessage)));
        }

        internal void Update()
        {
            string roomId = InRoom ? _control.Room.Id : null;
            if (_roomId != roomId)
            {
                _roomId = roomId;
                _state = null;
                _permissions.Clear();
                _sentStates.Clear();
                _lastRequest.Clear();
                _lastMuteNotice.Clear();
                Status = _pendingNotice = null;
                _nextState = 0f;
                _environment = new EnvironmentSettings();
            }

            _control.Rendering.Update(InRoom && RoomMultiplayerMenu.Current?.IsInitialized == true, Environment.Rendering);
            _control.Rendering.Geometry.Update(InRoom && RoomMultiplayerMenu.Current?.IsInitialized == true, Environment.World);

            if (_pendingNotice != null && MultiplayerChat.Current && RoomMultiplayerMenu.Current?.IsInitialized == true)
            {
                MultiplayerChat.Current.AddLocalMessage("NullFrame: ", _pendingNotice, "");
                _pendingNotice = null;
            }

            if (!_control.CanManage || Time.unscaledTime < _nextState) return;

            _nextState = Time.unscaledTime + 1f;
            foreach (int id in _permissions.Clients.ToArray())
            {
                if (!_control.Manager.ServerManager.Clients.ContainsKey(id) ||
                    !_control.TryIdentity(id, out var identity) ||
                    !_permissions.Allows(id, identity.Username))
                {
                    _permissions.Revoke(id);
                }
            }

            RoomPlayer[] players = HostPlayers();
            foreach (NetworkConnection connection in _control.Connections())
            {
                if (!connection.IsAuthenticated) continue;

                bool trusted = players.Any(player => player.Id == connection.ClientId && player.Trusted);
                ServerState state = new(
                    trusted,
                    players,
                    trusted ? _control.Bans.Entries.Take(100).ToArray() : Array.Empty<BanEntry>(),
                    trusted ? Mutes.Entries.Take(100).ToArray() : Array.Empty<BanEntry>(),
                    _environment);
                string json = JsonSerializer.Serialize(state);

                if (_sentStates.GetValueOrDefault(connection.ClientId) == json)
                {
                    continue;
                }

                _sentStates[connection.ClientId] = json;
                _profiles.SendServerMessage(new ServerMessage(1, roomId, State: state), connection);

                if (players.Any(player => player.Id == connection.ClientId && player.Muted) &&
                    !_lastMuteNotice.ContainsKey(connection.ClientId))
                {
                    NotifyMuted(connection);
                }
            }

            foreach (int id in _sentStates.Keys.ToArray())
            {
                if (!_control.Manager.ServerManager.Clients.ContainsKey(id))
                {
                    _sentStates.Remove(id);
                    _lastRequest.Remove(id);
                    _lastMuteNotice.Remove(id);
                }
            }
        }

        private RoomPlayer[] HostPlayers()
        {
            return _control.Connections()
            .Where(connection => connection.IsAuthenticated)
            .Select(
                connection => _control.TryIdentity(connection.ClientId, out var identity)
                ? new RoomPlayer(
                    connection.ClientId,
                    identity.Username,
                    _control.PlayerName(connection.ClientId, identity),
                    identity.IsGuest,
                    _permissions.Allows(connection.ClientId, identity.Username),
                    Mutes.Contains(identity.Username)) : null)
            .Where(player => player != null).OrderBy(player => player.Id).ToArray();
        }

        internal bool Submit(ServerRequest request, out string error)
        {
            error = "Only the host or a trusted administrator can do this.";

            if (!CanOpen || _control.CanManage) return false;

            _profiles.SendServerMessage(new ServerMessage(1, _control.Room.Id, Request: request), toServer: true);

            error = null;
            Status = "Command sent to the host...";

            return true;
        }

        internal bool SetTrusted(int id, string account, bool trusted, out string error)
        {
            error = "Only the host can grant or revoke administrator access.";

            if (!_control.CanManage ||
                id == RelayProtocol.HostPlayerId ||
                !_control.TryIdentity(id, out var identity) ||
                identity.Username != account ||
                !_control.Manager.ServerManager.Clients.ContainsKey(id))
            {
                return false;
            }

            if (trusted)
            {
                if (!_permissions.Grant(id, account))
                {
                    error = "This account cannot receive administrator access.";
                    return false;
                }
            }
            else
            {
                _permissions.Revoke(id);
            }

            _nextState = 0f;
            Notice(id, trusted ? "The host granted you Server Settings access." : "The host revoked your Server Settings access.");
            error = null;

            return true;
        }

        internal bool SetEnvironment(EnvironmentSettings environment, out string error)
        {
            if (!_control.CanManage)
            {
                return Submit(new ServerRequest(ServerAction.Environment, Environment: environment), out error);
            }

            error = "Check the environment settings.";
            if (environment?.Valid != true) return false;

            if (!_control.Rendering.Geometry.Validate(environment.World, out error))
            {
                return false;
            }

            if (environment.Rendering is { Skybox: true, SkyboxUrl: "" } rendering &&
                !_control.Rendering.Skyboxes.ContainsKey(rendering.SkyboxName))
            {
                error = "Choose an available skybox.";
                return false;
            }

            _environment = environment;
            _nextState = 0f;
            error = null;
            return true;
        }

        internal bool SetMuted(int id, string account, bool muted, out string error)
        {
            if (!_control.CanManage)
            {
                return Submit(new ServerRequest(muted ? ServerAction.Mute : ServerAction.Unmute, id, account), out error);
            }

            error = "The selected account is unavailable.";

            if (string.IsNullOrWhiteSpace(account) ||
                (id >= 0 &&
                (!_control.TryIdentity(id, out var identity) ||
                identity.Username != account)) ||
                id == RelayProtocol.HostPlayerId ||
                (_control.TryIdentity(RelayProtocol.HostPlayerId, out var host) &&
                string.Equals(host.Username, account, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            if (!(muted ? Mutes.Add(account, id >= 0 ?
                _control.PlayerName(id, null) : account, out error) :
                Mutes.Remove(account, out error)))
            {
                return false;
            }

            _nextState = 0f;
            if (id >= 0)
            {
                Notice(
                    id,
                    muted ?
                    "The host muted your chat. You cannot send messages until you are unmuted." :
                    "The host unmuted your chat.");

                if (muted)
                {
                    _lastMuteNotice[id] = Time.unscaledTime;
                }
                else
                {
                    _lastMuteNotice.Remove(id);
                }
            }

            return true;
        }

        private void ReceiveRequest(NetworkConnection connection, ServerMessage message)
        {
            if (!_control.CanManage ||
                message.RoomId != _control.Room.Id ||
                message.Request == null ||
                !connection.IsValid ||
                !connection.IsAuthenticated)
            {
                return;
            }

            int id = connection.ClientId;
            if (_lastRequest.TryGetValue(id, out var time) && Time.unscaledTime - time < 0.5f)
            {
                return;
            }

            _lastRequest[id] = Time.unscaledTime;
            if (!_control.TryIdentity(id, out var identity) || !_permissions.Allows(id, identity.Username))
            {
                Notice(id, "You do not have permission to manage this room.");
                return;
            }

            ServerRequest request = message.Request;
            if (request.Environment is { } environment &&
                (environment.KeepRagdolls != _environment.KeepRagdolls || environment.ThrowAnyone != _environment.ThrowAnyone))
            {
                Notice(id, "Only the host can change ragdoll persistence and Throw Any One.");
                return;
            }

            string error;

            bool success = request.Action switch
            {
                ServerAction.Kick => _control.Kick(request.PlayerId, request.Account, false, out error),
                ServerAction.Ban => _control.Kick(request.PlayerId, request.Account, true, out error),
                ServerAction.Unban => _control.Unban(request.Account, out error),
                ServerAction.Room => _control.ApplyRoom(request.Name, request.Capacity, request.Color, out error),
                ServerAction.Round => _control.RestartRound(
                request.Scene,
                request.Mode,
                request.Minutes,
                request.Custards,
                request.Difficulty,
                request.MuteGuests,
                out error),
                ServerAction.Reopen => _control.Reopen(out error),
                ServerAction.Mute => SetMuted(request.PlayerId, request.Account, true, out error),
                ServerAction.Unmute => SetMuted(request.PlayerId, request.Account, false, out error),
                ServerAction.Environment => SetEnvironment(request.Environment, out error),
                _ => InvalidAction(out error)
            };

            _nextState = 0f;
            Notice(id, success ? "Command accepted by the host." : error ?? "The command could not be completed.");
        }

        private static bool InvalidAction(out string error)
        {
            error = "Unknown room command.";
            return false;
        }

        private void ReceiveState(ServerMessage message)
        {
            if (!InRoom || message.RoomId != _control.Room.Id) return;

            if (message.State != null)
            {
                _roomId = message.RoomId;
                _state = message.State;
            }

            if (message.Notice != null)
            {
                if (message.Notice == GlobalBlacklist.BannedMessage)
                {
                    _session.RejectCommunity(message.Notice);
                    return;
                }

                Status = _pendingNotice = message.Notice;
            }
        }

        private void Notice(int id, string text)
        {
            if (_control.Manager.ServerManager.Clients.TryGetValue(id, out var connection) && connection.IsAuthenticated)
            {
                _profiles.SendServerMessage(new ServerMessage(1, _control.Room.Id, Notice: text), connection);
            }
        }

        private void NotifyMuted(NetworkConnection connection)
        {
            if (_lastMuteNotice.TryGetValue(connection.ClientId, out var last) && Time.unscaledTime - last < 5f)
            {
                return;
            }

            _lastMuteNotice[connection.ClientId] = Time.unscaledTime;
            Notice(connection.ClientId, "The host muted your chat. You cannot send messages until you are unmuted.");
        }

        private static bool BeforeChatMessage(NetworkConnection __2)
        {
            ServerAccess instance = _instance;
            if (!instance._control.CanManage ||
                __2 == null ||
                !__2.IsAuthenticated ||
                !instance._control.TryIdentity(__2.ClientId, out var identity) ||
                !instance.Mutes.Contains(identity.Username))
            {
                return true;
            }

            instance.NotifyMuted(__2);

            return false;
        }
    }
}
