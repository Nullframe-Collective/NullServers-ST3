using Il2Cpp;
using Il2CppFishNet.Connection;
using Il2CppFishNet.Managing;
using Il2CppFishNet.Managing.Logging;
using Il2CppFishNet.Transporting.Relay;
using MelonLoader;
using NullServers.Additional;
using NullServers.Content;
using NullServers.Players;
using UnityEngine;

namespace NullServers.Network
{
    internal sealed class FrameworkCompatibility
    {
        private sealed class Peer
        {
            internal IntPtr Connection;
            internal bool Accepted;
            internal string Error;
            internal float Deadline;
            internal float NextOffer;
        }

        private readonly RelaySession _session;
        private readonly PlayerProfiles _profiles;
        private readonly MelonLogger.Instance _logger;

        private readonly Dictionary<int, Peer> _peers = new();
        private RoomMultiplayerMenu _menu;
        private RoomMultiplayerMenu _queuedMenu;
        private string _room;
        private string _revision;
        private string _queuedTeam;
        private float _deadline;
        private FrameworkMessage _pendingOffer;
        private bool _accepted;
        private FrameworkManifest _required;
        private int _readyFrame = -1;
        private bool _ready;
        internal string Status { get; private set; }

        private NetworkManager Manager => NetBootstrap.Current ? NetBootstrap.Current.Network : null;

        private bool Active =>
            _session.IsNullFrame &&
            !_session.IsSwitching &&
            _session.Relay &&
            !_session.Relay.OfflineMode &&
            _session.Relay.CurrentRoom?.Id == _room &&
            _menu &&
            _menu == RoomMultiplayerMenu.Current;

        internal bool Ready
        {
            get
            {
                if (_readyFrame != Time.frameCount)
                {
                    _readyFrame = Time.frameCount;
                    _ready = Active &&
                        Manager &&
                        _profiles.ProtocolAvailable &&
                        (Manager.ServerManager.Started || _accepted) &&
                        FrameworkContent.Ready(Manager.ServerManager.Started ? _session.Content : _required);
                }

                return _ready;
            }
        }

        private void Invalidate() => _readyFrame = -1;

        internal ContentDownloads Downloads { get; set; }

        internal void KeepAlive(int clientId)
        {
            if (_peers.TryGetValue(clientId, out var peer) && !peer.Accepted && peer.Error == null)
            {
                peer.Deadline = Math.Max(peer.Deadline, Time.unscaledTime + 30f);
            }
        }

        internal FrameworkCompatibility(RelaySession session, PlayerProfiles profiles, MelonLogger.Instance logger)
        {
            _session = session;
            _profiles = profiles;
            _logger = logger;
            profiles.Compatibility = this;
            profiles.FrameworkReceived += ReceiveRequest;
            profiles.FrameworkReceivedFromHost += Receive;
        }

        internal bool Allows(NetworkConnection connection) =>
            Ready &&
            connection != null &&
            connection.IsAuthenticated &&
            (connection.ClientId == RelayProtocol.HostPlayerId ||
                _peers.TryGetValue(connection.ClientId, out var peer) &&
                peer.Accepted &&
                peer.Connection == connection.Pointer);

        internal bool HoldSpawn(RoomMultiplayerMenu menu, string team)
        {
            if (!_session.IsNullFrame ||
                _session.IsSwitching ||
                !_session.Relay ||
                _session.Relay.OfflineMode ||
                _session.Relay.CurrentRoom == null)
            {
                return false;
            }

            if (Ready)
            {
                return false;
            }

            _queuedMenu = menu;
            _queuedTeam = team;

            return true;
        }

        private FrameworkMessage Message(FrameworkStage stage, FrameworkManifest manifest = null, string error = null)
        {
            return new FrameworkMessage(1, _room, _menu.NetworkObject.ObjectId, _revision, stage, manifest, error) { InteractionApi = 3 };
        }

        internal void Update()
        {
            Invalidate();
            RoomMultiplayerMenu menu = RoomMultiplayerMenu.Current;
            string room = _session.IsNullFrame && !_session.IsSwitching ? _session.Relay?.CurrentRoom?.Id : null;

            if (_menu != menu || _room != room)
            {
                _menu = menu;
                _room = room;
                _revision = null;
                _accepted = false;
                _required = null;
                _peers.Clear();
                _deadline = Time.unscaledTime + 30f;
                _pendingOffer = null;
                Status = null;

                if (_queuedMenu != menu)
                {
                    _queuedMenu = null;
                }
            }

            if (!Active || !Manager || !_menu.IsInitialized || !_profiles.ProtocolAvailable)
            {
                return;
            }

            if (Downloads?.Busy == true)
            {
                _deadline = Time.unscaledTime + 30f;
            }
            else if (_pendingOffer != null && Downloads?.TakeCompleted() == true)
            {
                FrameworkMessage offer = _pendingOffer;
                _pendingOffer = null;
                _deadline = Time.unscaledTime + 30f;
                Receive(offer);
            }

            if (Manager.ServerManager.Started)
            {
                _revision ??= Guid.NewGuid().ToString("N");

                List<NetworkConnection> connections = new();

                foreach (NetworkConnection connection in Manager.ServerManager.Clients.Values)
                {
                    connections.Add(connection);
                }

                foreach (NetworkConnection connection in connections)
                {
                    if (!connection.IsAuthenticated || connection.ClientId == RelayProtocol.HostPlayerId)
                    {
                        continue;
                    }

                    if (!_peers.TryGetValue(connection.ClientId, out var peer) || peer.Connection != connection.Pointer)
                    {
                        _peers[connection.ClientId] = peer = new Peer
                        {
                            Connection = connection.Pointer,
                            Deadline = Time.unscaledTime + 30f
                        };
                    }

                    if (peer.Accepted)
                    {
                        continue;
                    }

                    if (Time.unscaledTime > peer.Deadline)
                    {
                        Manager.ServerManager.Kick(
                            connection,
                            Il2CppFishNet.Managing.Server.KickReason.Unset,
                            LoggingType.Common,
                            peer.Error ?? "NullServers requires compatible frameworks and content.");

                        peer.Deadline = Time.unscaledTime + 5f;
                        continue;
                    }

                    if (peer.Error != null)
                    {
                        continue;
                    }

                    if (Time.unscaledTime >= peer.NextOffer)
                    {
                        peer.NextOffer = Time.unscaledTime + 2f;
                        _profiles.SendFramework(Message(FrameworkStage.Offer, _session.Content), connection);
                    }
                }

                foreach (int id in _peers.Keys.Where(id => !Manager.ServerManager.Clients.ContainsKey(id)).ToArray())
                {
                    _peers.Remove(id);
                }
            }
            else if (!Ready && Time.unscaledTime > _deadline)
            {
                string waiting = FrameworkContent.Waiting(_required);
                Status = waiting ?? "The host did not confirm addon compatibility before the connection timed out.";
                _session.ShowAddonError(Status, waiting == null ? AddonNoticeKind.Room : AddonNoticeKind.Unavailable);
                _logger.Error(Status);
                _session.Relay.StopConnection(false);

                return;
            }

            if (Ready && _queuedMenu == _menu)
            {
                RoomMultiplayerMenu queued = _queuedMenu;
                string team = _queuedTeam;
                _queuedMenu = null;
                queued.SpawnPlayer(team);
            }
        }

        private bool Matches(FrameworkMessage message) =>
            Active &&
            message.Room == _room &&
            message.RoomObject == _menu.NetworkObject.ObjectId;

        private void ReceiveRequest(NetworkConnection connection, FrameworkMessage message)
        {
            if (!Matches(message) ||
                !Manager ||
                !Manager.ServerManager.Started ||
                message.Stage != FrameworkStage.Request ||
                !connection.IsValid ||
                !connection.IsAuthenticated ||
                message.Revision != _revision ||
                !_peers.TryGetValue(connection.ClientId, out var peer) ||
                peer.Connection != connection.Pointer)
            {
                return;
            }

            string error = message.InteractionApi != 3 ? "Update NullServers on both the host and client to use this room's interaction protocol." :
                _session.Content.Difference(message.Manifest.For(_session.Content));

            if (error == null && FrameworkContent.Ready(_session.Content))
            {
                peer.Accepted = true;
                _profiles.SendFramework(Message(FrameworkStage.Accepted), connection);
                _logger.Msg("Frameworks confirmed for client " + connection.ClientId + ".");
            }
            else if (error != null)
            {
                _profiles.SendFramework(Message(FrameworkStage.Rejected, error: error), connection);
                peer.Accepted = false;
                peer.Error = error;
                peer.Deadline = Time.unscaledTime + 1f;
            }
        }

        private void Receive(FrameworkMessage message)
        {
            if (!Matches(message) || !Manager || Manager.ServerManager.Started)
            {
                return;
            }

            if (message.Stage == FrameworkStage.Offer)
            {
                if (message.InteractionApi != 3)
                {
                    Status = "Update NullServers on both the host and client to use this room's interaction protocol.";
                    _session.ShowAddonError(Status, AddonNoticeKind.Room);
                    _session.Relay.StopConnection(false);
                    return;
                }

                bool matches = _session.MatchesContent(message.Manifest);
                if (matches && Downloads?.Prepare(message.Manifest) == true)
                {
                    _pendingOffer = message;
                    Status = "Downloading room content from the host...";
                    return;
                }

                if (!matches || !_session.SelectContent(message.Manifest))
                {
                    Status = matches ? _session.AddonNotice?.Error :
                        "The host's addon requirements changed. Refresh the room list before joining.";

                    if (!matches)
                    {
                        _session.ShowAddonError(Status, AddonNoticeKind.Room);
                    }

                    _logger.Error(Status);
                    _session.Relay.StopConnection(false);

                    return;
                }

                _required = message.Manifest;
                Invalidate();

                if (!FrameworkContent.Ready(message.Manifest))
                {
                    return;
                }

                _revision = message.Revision;
                _profiles.SendFramework(Message(FrameworkStage.Request, _session.Content.For(message.Manifest)));
            }
            else if (message.Revision == _revision && message.Stage == FrameworkStage.Accepted)
            {
                _accepted = true;
                Invalidate();
                Status = null;
                _logger.Msg("Frameworks and content confirmed by the host.");
            }
            else if (message.Revision == _revision && message.Stage == FrameworkStage.Rejected)
            {
                Status = message.Error;
                _session.ShowAddonError(Status);
                _logger.Error(Status);
                _session.Relay.StopConnection(false);
            }
        }
    }
}
