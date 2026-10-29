using Il2Cpp;
using Il2CppFishNet.Connection;
using Il2CppFishNet.Managing;
using NullServers.Network;
using NullServers.Players;
using UnityEngine;

namespace NullServers.Voice
{
    internal sealed class VoiceRelay
    {
        private sealed class Peer
        {
            internal IntPtr Connection;
            internal float ListeningUntil;
            internal float NextIndicator;
            internal float NextHello;
            internal readonly VoiceBudget Budget = new();
        }

        private readonly RelaySession _session;
        private readonly PlayerProfiles _profiles;
        private readonly Dictionary<int, Peer> _peers = new();
        private string _room;
        private float _nextHello;
        private bool _enabled;
        private float _nextPlayers;
        private NetworkManager Manager => NetBootstrap.Current ? NetBootstrap.Current.Network : null;

        internal VoiceRelay(RelaySession session, PlayerProfiles profiles)
        {
            _session = session;
            _profiles = profiles;
            profiles.VoiceReceived += FromClient;
            profiles.VoiceReceivedFromHost += FromHost;
            VoiceApi.Sender = Send;
        }

        internal void Update()
        {
            NetworkManager manager = Manager;
            string room = _session.IsNullFrame && !_session.IsSwitching ? _session.Relay?.CurrentRoom?.Id : null;

            if (_room != room)
            {
                _peers.Clear();
                VoiceApi.Speaking.Clear();
                VoiceApi.Players.Clear();
                _nextPlayers = 0f;
                _nextHello = 0f;
                _room = room;
            }

            VoiceApi.RoomId = room;
            VoiceApi.LocalId = manager && manager.ClientManager.Connection != null
                ? manager.ClientManager.Connection.ClientId : -1;
            VoiceApi.Active = room != null && _profiles.Compatibility?.Ready == true && VoiceApi.LocalId >= 0;
            if (!VoiceApi.Active) return;

            if (Time.unscaledTime >= _nextPlayers)
            {
                _nextPlayers = Time.unscaledTime + 0.25f;
                VoiceApi.Players.Clear();

                foreach (PlayerNetworkController player in UnityEngine.Object.FindObjectsOfType<PlayerNetworkController>())
                {
                    if (player.IsSpawned)
                    {
                        VoiceApi.Players[player.OwnerId] = player;
                    }
                }
            }

            foreach (int id in _peers.Keys.ToArray())
            {
                if (!manager.ServerManager.Clients.TryGetValue(id, out var connection) ||
                    connection.Pointer != _peers[id].Connection || !connection.IsAuthenticated)
                {
                    _peers.Remove(id);
                }
            }

            if (Time.unscaledTime >= _nextHello || _enabled != VoiceApi.Enabled)
            {
                _enabled = VoiceApi.Enabled;
                _nextHello = Time.unscaledTime + 3f;
                _profiles.SendVoice(new VoicePacket(1, _enabled ? 0 : 2, -1, 0), toServer: true);
            }
        }

        private void Send(uint sequence, byte[] frame)
        {
            _profiles.SendVoice(new VoicePacket(1, 1, -1, sequence, Convert.ToBase64String(frame)), toServer: true);
        }

        private void FromClient(NetworkConnection connection, VoicePacket packet)
        {
            if (!VoiceApi.Active || !Manager.ServerManager.Started || connection == null ||
                !connection.IsAuthenticated || !_profiles.Compatibility.Allows(connection)) return;

            int id = connection.ClientId;
            if (!_peers.TryGetValue(id, out var peer) || peer.Connection != connection.Pointer)
            {
                peer = new Peer { Connection = connection.Pointer };
                _peers[id] = peer;
            }

            if (packet.Kind != 1)
            {
                if (Time.unscaledTime < peer.NextHello) return;

                peer.NextHello = Time.unscaledTime + 0.5f;
                peer.ListeningUntil = packet.Kind == 0 ? Time.unscaledTime + 10f : 0f;
                return;
            }

            if (peer.ListeningUntil <= Time.unscaledTime || !peer.Budget.Accept(packet.Sequence, Time.unscaledTime)) return;

            VoicePacket audio = packet with { ClientId = id };
            bool indicate = Time.unscaledTime >= peer.NextIndicator;

            if (indicate)
            {
                peer.NextIndicator = Time.unscaledTime + 0.2f;
            }

            foreach (NetworkConnection target in Manager.ServerManager.Clients.Values)
            {
                if (target.ClientId == id || !target.IsAuthenticated || !_profiles.Compatibility.Allows(target)) continue;

                bool listens = _peers.TryGetValue(target.ClientId, out var listener) &&
                    listener.Connection == target.Pointer && listener.ListeningUntil > Time.unscaledTime;
                if (listens && VoiceApi.TryPosition(id, out var speaker) && VoiceApi.TryPosition(target.ClientId, out var receiver) &&
                    (speaker - receiver).sqrMagnitude <= 1600f)
                {
                    _profiles.SendVoice(audio, target);
                }
                else if (indicate)
                {
                    _profiles.SendVoice(new VoicePacket(1, 2, id, packet.Sequence), target);
                }
            }
        }

        private void FromHost(VoicePacket packet)
        {
            if (!VoiceApi.Active || packet.ClientId < 0 || packet.Kind == 0) return;

            byte[] data = packet.Kind == 1 && VoiceApi.Enabled ? Convert.FromBase64String(packet.Data) : null;
            VoiceApi.Receive(packet.ClientId, packet.Sequence, data);
        }
    }
}
