using System.Buffers.Binary;

namespace S3_NullServers.Network
{
    internal enum RpcSchema
    {
        Chat,
        Appearance,
        Spawn,
        PlayerState,
        StringFloat,
        Notification
    }

    internal static class RpcPacket
    {
        internal const int MaxBytes = 65536;

        internal readonly record struct Header(int ObjectId, byte Component, int PayloadOffset, int PayloadLength);

        internal static bool TryHeader(ReadOnlySpan<byte> data, bool reliable, out Header header)
        {
            header = default;
            Cursor cursor = new(data);
            if (!cursor.Int(out var objectId) ||
                objectId < 0 ||
                objectId >= 65535 ||
                !cursor.Byte(out var spawned) ||
                spawned != 1 ||
                !cursor.Byte(out var component))
            {
                return false;
            }

            int length = cursor.Remaining;
            if (reliable && (!cursor.Int(out length) || length < 1 || length > cursor.Remaining))
            {
                return false;
            }

            if (length < 1 || length > MaxBytes)
            {
                return false;
            }

            header = new Header(objectId, component, cursor.Position, length);
            return true;
        }

        internal static bool TryBroadcast(ReadOnlySpan<byte> data, out int length)
        {
            length = 0;
            Cursor cursor = new(data);
            return cursor.Skip(2) && cursor.Int(out length) && length > 0 && length <= MaxBytes && length <= cursor.Remaining;
        }

        internal static bool ValidBody(ReadOnlySpan<byte> data, RpcSchema schema, bool exact)
        {
            Cursor cursor = new(data);
            bool valid = schema switch

            {
                RpcSchema.Chat => cursor.String(2048) && cursor.String(2048),
                RpcSchema.Appearance => cursor.Int(out var hat) &&
                hat >= 0 &&
                hat <= 255 &&
                cursor.Color() &&
                cursor.String(8192),
                RpcSchema.Spawn => cursor.String(256) && cursor.Vector() && cursor.Skip(4) && cursor.Int(out _),
                RpcSchema.PlayerState => cursor.Vector() &&
                cursor.Skip(4) &&
                cursor.Float() &&
                cursor.Int(out _) &&
                cursor.Int(out _) &&
                cursor.String(8192) &&
                cursor.String(8192) &&
                cursor.Float(),
                RpcSchema.StringFloat => cursor.String(512) && cursor.Float(),
                RpcSchema.Notification => cursor.String(512) &&
                cursor.String(512) &&
                cursor.String(512) &&
                cursor.String(512),
                _ => false
            };
            return valid && (!exact || cursor.Remaining == 0);
        }

        private ref struct Cursor
        {
            private readonly ReadOnlySpan<byte> _data;

            internal int Position
            {
                get; private set;
            }

            internal int Remaining => _data.Length - Position;

            internal Cursor(ReadOnlySpan<byte> data)
            {
                _data = data;
                Position = 0;
            }

            internal bool Byte(out byte value)
            {
                value = 0;
                if (Remaining == 0)
                {
                    return false;
                }

                value = _data[Position++];
                return true;
            }

            internal bool Int(out int value)
            {
                value = 0;
                uint packed = 0;
                for (int shift = 0; shift < 35; shift += 7)
                {
                    if (!Byte(out var current) || shift == 28 && current > 15)
                    {
                        return false;
                    }

                    packed |= (uint)(current & 127) << shift;
                    if ((current & 128) != 0)
                    {
                        continue;
                    }

                    value = (int)(packed >> 1) ^ -(int)(packed & 1);
                    return true;
                }

                return false;
            }

            internal bool Skip(int count)
            {
                if (count < 0 || count > Remaining)
                {
                    return false;
                }

                Position += count;
                return true;
            }

            internal bool String(int maximum)
            {
                return Int(out var length) && length >= 0 &&
                    length <= maximum && Skip(length);
            }

            internal bool Float(bool color = false)
            {
                if (Remaining < 4)
                {
                    return false;
                }

                float value = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(_data.Slice(Position, 4)));
                Position += 4;
                return float.IsFinite(value) && (!color || value >= 0f && value <= 1f);
            }

            internal bool Vector() => Float() && Float() && Float();

            internal bool Color() => Float(true) && Float(true) && Float(true);
        }
    }
}
