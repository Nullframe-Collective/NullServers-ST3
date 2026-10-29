using Il2CppFishNet.Connection;
using NullServers.Sandbox;
using System.Text;
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
            internal readonly TrafficBudget Budget = new(PhysicsLimits.PeerRate, PhysicsLimits.PeerBurst);
            internal readonly Dictionary<(int, byte), (int Revision, int Copies, float Time)> Sent = new();
            internal int Cursor;
        }

        private sealed class PoseBatch
        {
            internal readonly List<PhysicsPose> Poses = new(PhysicsPacket.MaxPoses);
            internal readonly List<(int, byte)> Keys = new(PhysicsPacket.MaxPoses);

            internal int Count => Poses.Count;
            internal bool Full => Poses.Count == PhysicsPacket.MaxPoses;

            internal void Add((int, byte) key, PhysicsPose pose)
            {
                Keys.Add(key);
                Poses.Add(pose);
            }

            internal void Clear()
            {
                Poses.Clear();
                Keys.Clear();
            }
        }

        private readonly SandboxWorld _world;
        private readonly Dictionary<(int, byte), Sample> _samples = new();
        private readonly Dictionary<int, Peer> _peers = new();
        private TrafficBudget _total = new(PhysicsLimits.TotalRate, PhysicsLimits.TotalBurst);
        private float _next;
        private int _sequence;
        private int _peerCursor;
        private PhysicsClock _clock = new();
        internal double RenderTime => _clock.RenderTime(Time.unscaledTime);
        internal double Timestamp(SandboxMessage message) => _clock.Observe(message.SentAt, Time.unscaledTime);

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

            if (sample.Pose.Position != null &&
                (SandboxWorld.Vector(sample.Pose.Position) - position).sqrMagnitude < 0.000001f &&
                Quaternion.Angle(SandboxWorld.Quaternion(sample.Pose.Rotation), rotation) < 0.15f)
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

        // Every 50 ms the host sends physics poses to each player: held objects first, then nearby objects in turns.
        internal void Update()
        {
            float now = Time.unscaledTime;
            if (!_world.IsHost || now < _next) return;

            _next = now + 0.05f;
            NetworkConnection[] connections = _world.Peers().ToArray();

            foreach (int id in _peers.Keys.Where(id => !connections.Any(connection => connection.ClientId == id)).ToArray())
            {
                _peers.Remove(id);
            }

            if (connections.Length == 0 || _samples.Count == 0) return;

            KeyValuePair<(int, byte), Sample>[] samples = _samples.ToArray();
            HashSet<int> held = _world.Hands.HeldObjects.ToHashSet();
            KeyValuePair<(int, byte), Sample>[] heldSamples = samples.Where(pair => held.Contains(pair.Key.Item1)).ToArray();

            // Players are served in a rotating order, so the same player does not always get what is left of the budget.
            for (int index = 0; index < connections.Length; index++)
            {
                NetworkConnection connection = connections[(_peerCursor + index) % connections.Length];
                SendPoses(connection, samples, heldSamples, held, now);
            }

            _peerCursor = (_peerCursor + 1) % connections.Length;
        }

        private void SendPoses(NetworkConnection connection, KeyValuePair<(int, byte), Sample>[] samples,
            KeyValuePair<(int, byte), Sample>[] heldSamples, HashSet<int> held, float now)
        {
            if (!_peers.TryGetValue(connection.ClientId, out Peer peer))
            {
                peer = new Peer();
                _peers[connection.ClientId] = peer;
            }

            Il2Cpp.PlayerNetworkController avatar = SandboxWorld.Avatar(connection.ClientId);
            if (!avatar) return;

            Vector3 origin = avatar.transform.position;
            PoseBatch batch = new();

            foreach (var pair in heldSamples)
            {
                if (!HeldNeedsUpdate(peer.Sent.GetValueOrDefault(pair.Key), pair.Value, now)) continue;

                batch.Add(pair.Key, pair.Value.Pose);
                if (batch.Full)
                {
                    Flush(connection, peer, batch, now);
                }
            }

            if (batch.Count > 0)
            {
                Flush(connection, peer, batch, now);
            }

            for (int checkedCount = 0; checkedCount < samples.Length; checkedCount++)
            {
                var pair = samples[peer.Cursor++ % samples.Length];
                if (peer.Cursor >= samples.Length)
                {
                    peer.Cursor = 0;
                }

                if (held.Contains(pair.Key.Item1)) continue;
                if (!NearbyNeedsUpdate(peer.Sent.GetValueOrDefault(pair.Key), pair.Value, origin, now)) continue;

                batch.Add(pair.Key, pair.Value.Pose);
                if (!batch.Full && checkedCount + 1 < samples.Length) continue;

                if (!Flush(connection, peer, batch, now)) break;
            }

            if (batch.Count > 0)
            {
                Flush(connection, peer, batch, now);
            }
        }

        // A held object is sent at most every 45 ms, and an unchanged one only three times.
        private static bool HeldNeedsUpdate((int Revision, int Copies, float Time) last, Sample sample, float now)
        {
            if (last.Revision == sample.Revision && last.Copies >= 3) return false;

            return now - last.Time >= 0.045f;
        }

        // Objects farther than 120 m are skipped; objects farther than 30 m or unchanged are refreshed every 250 ms.
        private static bool NearbyNeedsUpdate((int Revision, int Copies, float Time) last, Sample sample, Vector3 origin, float now)
        {
            float distance = (SandboxWorld.Vector(sample.Pose.Position) - origin).sqrMagnitude;
            if (distance > 14400f) return false;

            bool unchanged = last.Revision == sample.Revision;
            float interval = distance > 900f || unchanged ? 0.25f : 0.045f;
            if (now - last.Time < interval) return false;
            if (unchanged && last.Copies >= 3) return false;

            return true;
        }

        private bool Flush(NetworkConnection connection, Peer peer, PoseBatch batch, float now)
        {
            SandboxMessage message = _world.Message(SandboxKind.PoseBatch) with
            {
                Sequence = ++_sequence,
                SentAt = now,
                Data = PhysicsPacket.Encode(batch.Poses)
            };
            string json = SandboxMessage.Serialize(message);
            int bytes = Encoding.UTF8.GetByteCount(json) + 32;

            if (!peer.Budget.CanSpend(bytes, now) || !_total.CanSpend(bytes, now))
            {
                batch.Clear();
                return false;
            }

            peer.Budget.Spend(bytes, now);
            _total.Spend(bytes, now);
            _world.SendTo(connection, json, unreliable: true);

            foreach (var key in batch.Keys)
            {
                int revision = _samples[key].Revision;
                var last = peer.Sent.GetValueOrDefault(key);
                peer.Sent[key] = (revision, last.Revision == revision ? last.Copies + 1 : 1, now);
            }

            batch.Clear();
            return true;
        }

        internal void Receive(SandboxMessage message)
        {
            if (!PhysicsPacket.TryDecode(message.Data, out var poses)) return;

            double stamp = Timestamp(message);
            foreach (PhysicsPose pose in poses)
            {
                if (pose.Bone == 255)
                {
                    _world.Building.Pose(pose, message.Sequence, stamp);
                }
                else
                {
                    _world.Ragdolls.Pose(pose, message.Sequence, stamp);
                }
            }
        }

        internal void Clear()
        {
            _clock = new PhysicsClock();
            _samples.Clear();
            _peers.Clear();
            _sequence = _peerCursor = 0;
            _next = 0f;
            _total = new TrafficBudget(PhysicsLimits.TotalRate, PhysicsLimits.TotalBurst);
        }
    }
}
