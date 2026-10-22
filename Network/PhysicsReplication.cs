using NullServers.Sandbox;
using System.Text;
using System.Text.Json;
using UnityEngine;

namespace NullServers.Network
{
    internal sealed class PhysicsReplication
    {
        private sealed class Sample
        {
            internal PhysicsPose Pose;
            internal int Revision;
        }

        private sealed class Peer
        {
            internal readonly TrafficBudget Budget = new(8192, 2200);
            internal readonly Dictionary<(int, byte), (int Revision, int Copies, float Time)> Sent = new();
            internal int Cursor;
        }

        private readonly SandboxWorld _world;
        private readonly Dictionary<(int, byte), Sample> _samples = new();
        private readonly Dictionary<int, Peer> _peers = new();
        private TrafficBudget _total = new(65536, 8800);
        private float _next;
        private int _sequence;
        private int _peerCursor;

        internal PhysicsReplication(SandboxWorld world)
        {
            _world = world;
        }

        internal void Capture(int id, byte bone, Vector3 position, Quaternion rotation)
        {
            var key = (id, bone);
            if (!_samples.TryGetValue(key, out var sample))
            {
                _samples[key] = sample = new Sample();
            }

            if 
            (
                sample.Pose.Position != null &&
                (SandboxWorld.Vector(sample.Pose.Position) - position).sqrMagnitude < 0.000001f &&
                Quaternion.Angle(SandboxWorld.Quaternion(sample.Pose.Rotation), rotation) < 0.15f
            )
            {
                return;
            }

            sample.Pose = new PhysicsPose(id, bone, SandboxWorld.Point(position), SandboxWorld.Rotation(rotation));
            sample.Revision++;
        }

        internal void Forget(int id)
        {
            foreach (var key in _samples.Keys.Where(key => key.Item1 == id).ToArray())
            {
                _samples.Remove(key);

                foreach (Peer peer in _peers.Values)
                {
                    peer.Sent.Remove(key);
                }
            }
        }

        internal void Disconnect(int id) => _peers.Remove(id);

        internal void Update()
        {
            float now = Time.unscaledTime;
            if (!_world.IsHost || now < _next) return;

            _next = now + 0.1f;
            Il2CppFishNet.Connection.NetworkConnection[] connections = _world.Peers().ToArray();
            foreach (int id in _peers.Keys.Where(id => !connections.Any(connection => connection.ClientId == id)).ToArray())
            {
                _peers.Remove(id);
            }

            if (connections.Length == 0 || _samples.Count == 0) return;

            var samples = _samples.ToArray();
            var held = _world.Gun.HeldObjects.ToHashSet();
            var priority = samples.Where(pair => held.Contains(pair.Key.Item1)).ToArray();

            for (int connectionIndex = 0; connectionIndex < connections.Length; connectionIndex++)
            {
                Il2CppFishNet.Connection.NetworkConnection connection = connections[(_peerCursor + connectionIndex) % connections.Length];
                if (!_peers.TryGetValue(connection.ClientId, out var peer))
                {
                    _peers[connection.ClientId] = peer = new Peer();
                }

                Il2Cpp.PlayerNetworkController avatar = SandboxWorld.Avatar(connection.ClientId);
                if (!avatar) continue;

                Vector3 origin = avatar.transform.position;
                List<PhysicsPose> poses = new(PhysicsPacket.MaxPoses);
                List<(int, byte)> keys = new(PhysicsPacket.MaxPoses);
                foreach (var pair in priority)
                {
                    var last = peer.Sent.GetValueOrDefault(pair.Key);
                    if (last.Revision == pair.Value.Revision && last.Copies >= 3 || now - last.Time < 0.09f)
                    {
                        continue;
                    }

                    keys.Add(pair.Key);
                    poses.Add(pair.Value.Pose);

                    if (poses.Count == PhysicsPacket.MaxPoses)
                    {
                        Flush();
                    }
                }

                if (poses.Count > 0) Flush();

                for (int checkedCount = 0; checkedCount < samples.Length; checkedCount++)
                {
                    var pair = samples[peer.Cursor++ % samples.Length];
                    if (peer.Cursor >= samples.Length)
                    {
                        peer.Cursor = 0;
                    }

                    var last = peer.Sent.GetValueOrDefault(pair.Key);
                    if (held.Contains(pair.Key.Item1)) continue;

                    float distance = (SandboxWorld.Vector(pair.Value.Pose.Position) - origin).sqrMagnitude;
                    if 
                    (
                        distance > 14400f ||
                        now - last.Time < (distance > 900f ? 0.5f : last.Revision == pair.Value.Revision ? 0.25f : 0.09f) ||
                        last.Revision == pair.Value.Revision &&
                        last.Copies >= 3
                    )
                    {
                        continue;
                    }

                    keys.Add(pair.Key);
                    poses.Add(pair.Value.Pose);
                    if (poses.Count < PhysicsPacket.MaxPoses && checkedCount + 1 < samples.Length)
                    {
                        continue;
                    }

                    if (!Flush()) break;
                }

                if (poses.Count > 0) Flush();

                bool Flush()
                {
                    SandboxMessage message = _world.Message(SandboxKind.PoseBatch) with
                    {
                        Sequence = ++_sequence,
                        Data = PhysicsPacket.Encode(poses)
                    };
                    int bytes = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(message, SandboxMessage.JsonOptions)) + 32;

                    if (!peer.Budget.Spend(bytes, now) || !_total.Spend(bytes, now))
                    {
                        poses.Clear();
                        keys.Clear();
                        return false;
                    }

                    _world.Send(message, connection, unreliable: true);
                    foreach (var key in keys)
                    {
                        int revision = _samples[key].Revision;
                        var last = peer.Sent.GetValueOrDefault(key);
                        peer.Sent[key] = (revision, last.Revision == revision ? last.Copies + 1 : 1, now);
                    }

                    poses.Clear();
                    keys.Clear();

                    return true;
                }
            }

            _peerCursor = (_peerCursor + 1) % connections.Length;
        }

        internal void Receive(SandboxMessage message)
        {
            if (!PhysicsPacket.TryDecode(message.Data, out var poses)) return;

            foreach (PhysicsPose pose in poses)
            {
                if (pose.Bone == 255)
                {
                    _world.Building.Pose(pose, message.Sequence);
                }
                else
                {
                    _world.Ragdolls.Pose(pose, message.Sequence);
                }
            }
        }

        internal void Clear()
        {
            _samples.Clear();
            _peers.Clear();
            _sequence = _peerCursor = 0;
            _next = 0f;
            _total = new TrafficBudget(65536, 8800);
        }
    }
}
