using System.Buffers.Binary;

namespace NullServers.Network
{
    internal readonly record struct PhysicsPose(int Id, byte Bone, Point3 Position, Rotation4 Rotation);

    internal static class PhysicsPacket
    {
        internal const int MaxPoses = 24;
        private const int PoseBytes = 25;

        internal static string Encode(IReadOnlyList<PhysicsPose> poses)
        {
            if (poses.Count is < 1 or > MaxPoses)
            {
                throw new ArgumentException("Invalid physics packet size.");
            }

            byte[] bytes = new byte[poses.Count * PoseBytes];
            for (int index = 0; index < poses.Count; index++)
            {
                PhysicsPose pose = poses[index];
                var span = bytes.AsSpan(index * PoseBytes, PoseBytes);

                BinaryPrimitives.WriteInt32LittleEndian(span, pose.Id);
                span[4] = pose.Bone;
                BinaryPrimitives.WriteSingleLittleEndian(span[5..], pose.Position.X);
                BinaryPrimitives.WriteSingleLittleEndian(span[9..], pose.Position.Y);
                BinaryPrimitives.WriteSingleLittleEndian(span[13..], pose.Position.Z);
                Rotation4 rotation = pose.Rotation;

                BinaryPrimitives.WriteInt16LittleEndian(span[17..], Pack(rotation.X));
                BinaryPrimitives.WriteInt16LittleEndian(span[19..], Pack(rotation.Y));
                BinaryPrimitives.WriteInt16LittleEndian(span[21..], Pack(rotation.Z));
                BinaryPrimitives.WriteInt16LittleEndian(span[23..], Pack(rotation.W));
            }

            return Convert.ToBase64String(bytes);
        }

        private static short Pack(float value) => (short)Math.Clamp(MathF.Round(value * 32767f), -32767f, 32767f);

        internal static bool TryDecode(string data, out PhysicsPose[] poses)
        {
            poses = null;

            if (data == null || data.Length > 800) return false;

            Span<byte> bytes = stackalloc byte[MaxPoses * PoseBytes];
            if (!Convert.TryFromBase64String(data, bytes, out var count) || count == 0 || count % PoseBytes != 0)
            {
                return false;
            }

            PhysicsPose[] result = new PhysicsPose[count / PoseBytes];
            for (int index = 0; index < result.Length; index++)
            {
                var span = bytes.Slice(index * PoseBytes, PoseBytes);
                int id = BinaryPrimitives.ReadInt32LittleEndian(span);
                byte bone = span[4];

                Point3 position = new(
                    BinaryPrimitives.ReadSingleLittleEndian(span[5..]),
                    BinaryPrimitives.ReadSingleLittleEndian(span[9..]),
                    BinaryPrimitives.ReadSingleLittleEndian(span[13..]));
                Rotation4 rotation = new(
                    BinaryPrimitives.ReadInt16LittleEndian(span[17..]) / 32767f,
                    BinaryPrimitives.ReadInt16LittleEndian(span[19..]) / 32767f,
                    BinaryPrimitives.ReadInt16LittleEndian(span[21..]) / 32767f,
                    BinaryPrimitives.ReadInt16LittleEndian(span[23..]) / 32767f);

                if (id < 1 || (id < 1000000 ? bone != 255 : bone > 63) || !position.Valid() || !rotation.Valid)
                {
                    return false;
                }

                result[index] = new PhysicsPose(id, bone, position, rotation);
            }

            poses = result;

            return true;
        }
    }

    internal sealed class TrafficBudget
    {
        private readonly double _rate, _burst;
        private double _tokens, _time;

        internal TrafficBudget(double perSecond, double burst, double now = 0d)
        {
            _rate = perSecond;
            _burst = burst;
            _tokens = burst;
            _time = now;
        }

        internal bool CanSpend(int amount, double now)
        {
            _tokens = Math.Min(_burst, _tokens + Math.Max(0, now - _time) * _rate);
            _time = Math.Max(now, _time);
            return amount >= 0 && amount <= _tokens;
        }

        internal bool Spend(int bytes, double now)
        {
            if (!CanSpend(bytes, now)) return false;

            _tokens -= bytes;
            return true;
        }
    }
}
