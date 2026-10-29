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
        private readonly List<(float At, bool Accepted)> _departures = new();
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

        // Only inside a real relay room. Play Solo runs without one, and when it ends the game turns offline mode off
        // before the round is gone; without a room the client would otherwise time out and show a room notice.
        private bool Active =>
            _session.IsNullFrame &&
            !_session.IsSwitching &&
            _session.Relay &&
            !_session.Relay.OfflineMode &&
            _room != null &&
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
            string room = _session.IsNullFrame && !_session.IsSwitching && _session.Relay && !_session.Relay.OfflineMode
                ? _session.Relay.CurrentRoom?.Id : null;

            if (_menu != menu || _room != room)
            {
                ResetForRoom(menu, room);
            }

            if (!Active || !Manager || !_menu.IsInitialized || !_profiles.ProtocolAvailable) return;

            UpdateDownloads();

            if (Manager.ServerManager.Started)
            {
                _revision ??= Guid.NewGuid().ToString("N");
                OfferToNewClients();
                CollectDepartures();
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

            // A spawn that was held until the content was confirmed happens now.
            if (Ready && _queuedMenu == _menu)
            {
                RoomMultiplayerMenu queued = _queuedMenu;
                _queuedMenu = null;
                queued.SpawnPlayer(_queuedTeam);
            }
        }

        private void ResetForRoom(RoomMultiplayerMenu menu, string room)
        {
            _menu = menu;
            _room = room;
            _revision = null;
            _accepted = false;
            _required = null;
            _peers.Clear();
            _departures.Clear();
            _deadline = Time.unscaledTime + 30f;
            _pendingOffer = null;
            Status = null;

            if (_queuedMenu != menu)
            {
                _queuedMenu = null;
            }
        }

        // While packs download, or the player decides whether to download them, the connection is not timed out.
        // When the download is approved or finished, the host's last offer is answered again.
        private void UpdateDownloads()
        {
            if (Downloads == null) return;

            if (Downloads.Busy || Downloads.Offered != null)
            {
                _deadline = Time.unscaledTime + 30f;
                return;
            }

            if (_pendingOffer == null) return;
            if (!Downloads.TakeAccepted() && !Downloads.TakeCompleted()) return;

            FrameworkMessage offer = _pendingOffer;
            _pendingOffer = null;
            _deadline = Time.unscaledTime + 30f;
            Receive(offer);
        }

        // The host offers its content list to every client that has not confirmed it yet, and kicks clients that run out of time.
        private void OfferToNewClients()
        {
            List<NetworkConnection> connections = new();
            foreach (NetworkConnection connection in Manager.ServerManager.Clients.Values)
            {
                connections.Add(connection);
            }

            foreach (NetworkConnection connection in connections)
            {
                if (!connection.IsAuthenticated || connection.ClientId == RelayProtocol.HostPlayerId) continue;

                if (!_peers.TryGetValue(connection.ClientId, out Peer peer) || peer.Connection != connection.Pointer)
                {
                    peer = new Peer { Connection = connection.Pointer, Deadline = Time.unscaledTime + 30f };
                    _peers[connection.ClientId] = peer;
                }

                if (peer.Accepted) continue;

                if (Time.unscaledTime > peer.Deadline)
                {
                    string reason = peer.Error ?? "NullServers requires compatible frameworks and content.";
                    Manager.ServerManager.Kick(connection, Il2CppFishNet.Managing.Server.KickReason.Unset, LoggingType.Common, reason);
                    peer.Deadline = Time.unscaledTime + 5f;
                    continue;
                }

                if (peer.Error != null || Time.unscaledTime < peer.NextOffer) continue;

                peer.NextOffer = Time.unscaledTime + 2f;
                _profiles.SendFramework(Message(FrameworkStage.Offer, _session.Content), connection);
            }
        }

        private void CollectDepartures()
        {
            NetworkManager manager = Manager;
            if (!manager || !manager.ServerManager.Started) return;

            foreach (var pair in _peers.ToArray())
            {
                if (manager.ServerManager.Clients.TryGetValue(pair.Key, out var connection) &&
                    connection.Pointer == pair.Value.Connection &&
                    connection.IsValid)
                {
                    continue;
                }

                _departures.Add((Time.unscaledTime, pair.Value.Accepted));
                _peers.Remove(pair.Key);
            }
        }

        internal bool QuietDeparture()
        {
            CollectDepartures();
            _departures.RemoveAll(item => Time.unscaledTime - item.At > 5f);

            int index = _departures.FindIndex(item => !item.Accepted);
            if (index < 0 || _departures.Any(item => item.Accepted)) return false;

            _departures.RemoveAt(index);
            return true;
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
                _logger.Debug("Frameworks confirmed for client " + connection.ClientId + ".");
            }
            else if (error != null)
            {
                _profiles.SendFramework(Message(FrameworkStage.Rejected, error: error), connection);
                peer.Accepted = false;
                peer.Error = error;
                peer.Deadline = Time.unscaledTime + 1f;
            }
        }

        // A client receives the host's content offer and the host's answer to our content request here.
        private void Receive(FrameworkMessage message)
        {
            if (!Matches(message) || !Manager || Manager.ServerManager.Started) return;

            if (message.Stage == FrameworkStage.Offer)
            {
                ReceiveOffer(message);
            }
            else if (message.Revision == _revision && message.Stage == FrameworkStage.Accepted)
            {
                _accepted = true;
                Invalidate();
                Status = null;
                _logger.Debug("Frameworks and content confirmed by the host.");
            }
            else if (message.Revision == _revision && message.Stage == FrameworkStage.Rejected)
            {
                Status = message.Error;
                _session.ShowAddonError(Status);
                _logger.Error(Status);
                _session.Relay.StopConnection(false);
            }
        }

        // Missing room content is downloaded first (after asking the player if needed),
        // then the client requests to join with the content it has selected.
        private void ReceiveOffer(FrameworkMessage message)
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

            if (matches && Downloads?.Ask(message.Manifest) == true)
            {
                _pendingOffer = message;
                Status = "Waiting for download approval...";
                return;
            }

            if (!matches || !_session.SelectContent(message.Manifest))
            {
                Status = matches ? _session.AddonNotice?.Error : "The host's addon requirements changed. Refresh the room list before joining.";
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

            if (!FrameworkContent.Ready(message.Manifest)) return;

            _revision = message.Revision;
            _profiles.SendFramework(Message(FrameworkStage.Request, _session.Content.For(message.Manifest)));
        }
    }
}
