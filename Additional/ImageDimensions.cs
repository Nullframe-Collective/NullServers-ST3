using System.Buffers.Binary;

namespace S3_NullServers.Additional
{
    internal static class ImageDimensions
    {
        internal static bool Allowed(ReadOnlySpan<byte> data, int maximum = 2048)
        {
            if (data.Length >= 24 &&
                data[..8].SequenceEqual(new byte[]
            {
                137,
                80,
                78,
                71,
                13,
                10,
                26,
                10
                }) &&
                data.Slice(12, 4).SequenceEqual(new byte[]
            {
                73,
                72,
                68,
                82
                }))
            {
                return Fits(
                    BinaryPrimitives.ReadUInt32BigEndian(data.Slice(16, 4)),
                    BinaryPrimitives.ReadUInt32BigEndian(data.Slice(20, 4)),
                    maximum);
            }

            if (data.Length < 4 || data[0] != 0xff || data[1] != 0xd8)
            {
                return false;
            }

            int offset = 2;
            while (offset + 3 < data.Length)
            {
                if (data[offset++] != 0xff)
                {
                    return false;
                }

                while (offset < data.Length && data[offset] == 0xff)
                {
                    offset++;
                }

                if (offset >= data.Length)
                {
                    return false;
                }

                byte marker = data[offset++];
                if (marker is 0xd8 or 0x01 || marker is >= 0xd0 and <= 0xd7)
                {
                    continue;
                }

                if (marker is 0xd9 or 0xda || offset + 2 > data.Length)
                {
                    return false;
                }

                ushort length = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset, 2));
                if (length < 2 || offset + length > data.Length)
                {
                    return false;
                }

                if (marker is >= 0xc0 and <= 0xcf && marker is not (0xc4 or 0xc8 or 0xcc))
                {
                    return length >= 8 &&
                        Fits(
                        BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset + 5, 2)),
                        BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset + 3, 2)),
                        maximum);
                }

                offset += length;
            }

            return false;
        }

        private static bool Fits(uint width, uint height, int maximum) => width > 0 && height > 0 && width <= maximum && height <= maximum;
    }
}
