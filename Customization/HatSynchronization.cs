using Il2Cpp;
using Il2CppFishNet.Connection;
using Il2CppFishNet.Managing;
using MelonLoader;
using MelonLoader.Utils;
using NullServers.Additional;
using NullServers.Network;
using NullServers.Players;
using System.Text.Json;
using UnityEngine;

namespace NullServers.Customization
{
    internal sealed class HatSynchronization
    {
        private readonly RelaySession _session;
        private readonly PlayerProfiles _profiles;
        private readonly MelonLogger.Instance _logger;
        private readonly Dictionary<int, string[]> _confirmed = new();
        private readonly Dictionary<int, (IntPtr Connection, string[] Hats)> _server = new();
        private readonly Dictionary<int, float> _submitted = new();
        private readonly string _path = Path.Combine(MelonEnvironment.UserDataDirectory, "NullFrame", "hats.json");
        private string[] _saved = ["", "", ""];
        private string _room;
        private int _roomObject = -1;
        private float _nextSubmit;
        private SyncCustomization _local;

        private NetworkManager Manager => NetBootstrap.Current ? NetBootstrap.Current.Network : null;

        internal bool Available =>
            _session.IsNullFrame && !_session.IsSwitching && _session.Relay &&
            _session.Relay.CurrentRoom != null &&
            _session.Addons.Any(addon => addon.Id == Dependencies.Customization) &&
            Frameworks.Customization?.Enabled == true &&
            Frameworks.Customization.Read<int>(Frameworks.Customization.Catalog, "SlotCount") == 4;

        internal HatSynchronization(RelaySession session, PlayerProfiles profiles, MelonLogger.Instance logger)
        {
            _session = session;
            _profiles = profiles;
            _logger = logger;
            profiles.HatsReceived += Receive;
            profiles.HatsReceivedFromHost += ReceiveFromHost;

            try
            {
                if (File.Exists(_path))
                {
                    string[] saved = JsonSerializer.Deserialize<string[]>(File.ReadAllText(_path));
                    if (!HatEntry.ValidHats(saved)) throw new InvalidDataException("Invalid saved hat slots.");

                    _saved = saved;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            {
                logger.Warning("Cannot load hat slots: " + exception.Message);
            }
        }

        private int Resolve(SyncCustomization source, string key) =>
            string.IsNullOrEmpty(key) ? -1 : Frameworks.Customization.Call<int>(Frameworks.Customization.Catalog, "ResolveHat", source, key);

        internal int[] HatsFor(SyncCustomization source)
        {
            if (!Available || !source) return null;

            string[] hats = source.IsOwner ? _saved : _confirmed.GetValueOrDefault(source.OwnerId);
            int primary = source.HatId;
            HashSet<int> selected = new() { primary };

            return hats?.Select(key =>
            {
                int hat = Resolve(source, key);
                return hat > 0 && selected.Add(hat) ? hat : -1;
            }).ToArray() ?? [-1, -1, -1];
        }

        internal bool Save(SyncCustomization source, int[] hats, out string error)
        {
            error = null;
            if (!Available) return hats == null || hats.All(hat => hat < 0);

            string[] keys = (hats ?? [-1, -1, -1]).Select(hat => hat < 0 ? "" :
                Frameworks.Customization.Call<string>(Frameworks.Customization.Catalog, "HatKey", source, hat)).ToArray();

            if (!HatEntry.ValidHats(keys))
            {
                error = "Choose three available extra hats or leave the slots empty.";
                return false;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path));
                File.WriteAllText(_path, JsonSerializer.Serialize(keys));
                _saved = keys;
                _nextSubmit = 0f;

                return true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                error = "Cannot save hat slots: " + exception.Message;
                return false;
            }
        }

        private HatPacket Packet(HatPacketKind kind, params HatEntry[] players) => new(1, _room, _roomObject, kind, players);

        private bool Matches(HatPacket packet) => Available && packet.Room == _room && packet.RoomObject == _roomObject;

        internal void Update(SyncCustomization local)
        {
            RoomMultiplayerMenu menu = RoomMultiplayerMenu.Current;
            string room = Available && menu && menu.NetworkObject ? _session.Relay.CurrentRoom.Id : null;
            int roomObject = room != null ? menu.NetworkObject.ObjectId : -1;

            if (_room != room || _roomObject != roomObject)
            {
                _confirmed.Clear();
                _server.Clear();
                _submitted.Clear();
                _nextSubmit = 0f;
                _room = room;
                _roomObject = roomObject;
            }

            _local = local;
            NetworkManager manager = Manager;

            if (room == null || !manager || _profiles.Compatibility?.Ready != true) return;

            if (manager.ServerManager.Started)
            {
                foreach (int player in _server.Keys.ToArray())
                {
                    if (manager.ServerManager.Clients.TryGetValue(player, out var peer) &&
                        peer.Pointer == _server[player].Connection && _profiles.Compatibility.Allows(peer)) continue;

                    _server.Remove(player);
                    _confirmed.Remove(player);
                    _submitted.Remove(player);
                    _profiles.SendHats(Packet(HatPacketKind.Remove, new HatEntry(player, ["", "", ""])));
                }
            }

            if (!local || !local.IsOwner || !local.IsClientInitialized || Time.unscaledTime < _nextSubmit) return;

            string[] selected = _saved.Select(key => Resolve(local, key) > 0 ? key : "").ToArray();

            if (_confirmed.TryGetValue(local.OwnerId, out var current) && current.SequenceEqual(selected)) return;

            _nextSubmit = Time.unscaledTime + 2f;
            _profiles.SendHats(Packet(HatPacketKind.Submit, new HatEntry(local.OwnerId, selected)), toServer: true);
        }

        private void Receive(NetworkConnection connection, HatPacket packet)
        {
            if (!Matches(packet) || !_local || !Manager || !Manager.ServerManager.Started ||
                _profiles.Compatibility?.Allows(connection) != true || !packet.Submission(connection.ClientId)) return;

            int player = connection.ClientId;
            if (_submitted.TryGetValue(player, out float previous) && Time.unscaledTime - previous < 0.25f) return;

            _submitted[player] = Time.unscaledTime;
            string[] hats = packet.Players[0].Hats;

            if (hats.Any(key => key.Length > 0 && Resolve(_local, key) <= 0)) return;

            bool changed = !_server.TryGetValue(player, out var current) ||
                current.Connection != connection.Pointer || !current.Hats.SequenceEqual(hats);
            _server[player] = (connection.Pointer, hats);

            foreach (var chunk in _server.Select(pair => new HatEntry(pair.Key, pair.Value.Hats)).Chunk(16))
            {
                _profiles.SendHats(Packet(HatPacketKind.Snapshot, chunk), connection);
            }

            if (changed)
            {
                _profiles.SendHats(Packet(HatPacketKind.Snapshot, new HatEntry(player, hats)));
                _logger.Debug($"Hat slots synchronized. Client: {player}.");
            }
        }

        private void ReceiveFromHost(HatPacket packet)
        {
            if (!Matches(packet) || _profiles.Compatibility?.Ready != true || packet.Kind == HatPacketKind.Submit) return;

            foreach (HatEntry player in packet.Players)
            {
                if (packet.Kind == HatPacketKind.Remove)
                {
                    _confirmed.Remove(player.Player);
                }
                else
                {
                    _confirmed[player.Player] = player.Hats;
                }
            }
        }
    }
}
