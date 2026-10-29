using System.Buffers.Binary;
using System.Text;

namespace NullServers.Content
{
    internal sealed class BundleLimits
    {
        internal long MaxUncompressed { get; init; } = 512L * 1024 * 1024;
        internal int MaxBlocks { get; init; } = 65536;
        internal int MaxNodes { get; init; } = 64;
        internal int MaxObjects { get; init; } = 50000;
        internal int MaxTypes { get; init; } = 512;
        internal int MaxTreeNodes { get; init; } = 4096;
        internal int MaxReadSteps { get; init; } = 20_000_000;
    }

    internal sealed record BundleNode(string Path, long Offset, long Size);

    internal sealed class UnityBundle
    {
        private const uint BlocksInfoAtEnd = 0x80;
        private const uint PaddingAtStart = 0x200;

        internal string EngineVersion { get; private set; }
        internal byte[] Data { get; private set; }
        internal List<BundleNode> Nodes { get; } = new();

        internal static UnityBundle Read(byte[] file, BundleLimits limits)
        {
            UnityBundle bundle = new();
            ByteReader reader = new(file, bigEndian: true);

            if (reader.CString(16) != "UnityFS")
            {
                throw new InvalidDataException("Not a UnityFS bundle.");
            }

            uint version = reader.UInt32();
            if (version is < 6 or > 8)
            {
                throw new InvalidDataException("Unsupported bundle format version " + version + ".");
            }

            reader.CString(32);
            bundle.EngineVersion = reader.CString(64);
            long size = reader.Int64();
            uint compressedInfo = reader.UInt32();
            uint uncompressedInfo = reader.UInt32();
            uint flags = reader.UInt32();

            if (size != file.Length)
            {
                throw new InvalidDataException("The bundle size does not match its header.");
            }

            if (compressedInfo > 1024 * 1024 || uncompressedInfo > 4 * 1024 * 1024)
            {
                throw new InvalidDataException("The bundle directory is too large.");
            }

            if (version >= 7)
            {
                reader.Align(16);
            }

            byte[] packedInfo;
            if ((flags & BlocksInfoAtEnd) != 0)
            {
                packedInfo = new ByteReader(file, true) { Position = checked(file.Length - (int)compressedInfo) }.Bytes((int)compressedInfo);
            }
            else
            {
                packedInfo = reader.Bytes((int)compressedInfo);
            }

            byte[] info = Decompress(packedInfo, (int)uncompressedInfo, flags & 0x3F);
            ByteReader directory = new(info, bigEndian: true);
            directory.Skip(16);

            int blockCount = directory.Int32();
            if (blockCount < 0 || blockCount > limits.MaxBlocks)
            {
                throw new InvalidDataException("Invalid block count.");
            }

            (uint Uncompressed, uint Compressed, ushort Flags)[] blocks = new (uint Uncompressed, uint Compressed, ushort Flags)[blockCount];
            long total = 0;

            for (int index = 0; index < blockCount; index++)
            {
                blocks[index] = (directory.UInt32(), directory.UInt32(), directory.UInt16());
                total += blocks[index].Uncompressed;
            }

            if (total > limits.MaxUncompressed)
            {
                throw new InvalidDataException("The bundle unpacks to more than the allowed size.");
            }

            int nodeCount = directory.Int32();
            if (nodeCount < 1 || nodeCount > limits.MaxNodes)
            {
                throw new InvalidDataException("Invalid number of files inside the bundle.");
            }

            for (int index = 0; index < nodeCount; index++)
            {
                long offset = directory.Int64();
                long length = directory.Int64();
                directory.UInt32();
                string path = directory.CString(256);

                if (offset < 0 || length < 0 || offset + length > total)
                {
                    throw new InvalidDataException("A file inside the bundle points outside its data.");
                }

                bundle.Nodes.Add(new BundleNode(path, offset, length));
            }

            if ((flags & PaddingAtStart) != 0)
            {
                reader.Align(16);
            }

            byte[] data = new byte[total];
            int written = 0;

            foreach (var block in blocks)
            {
                byte[] packed = reader.Bytes(checked((int)block.Compressed));
                byte[] unpacked = Decompress(packed, checked((int)block.Uncompressed), block.Flags & 0x3Fu);
                Buffer.BlockCopy(unpacked, 0, data, written, unpacked.Length);
                written += unpacked.Length;
            }

            if (reader.Position != ((flags & BlocksInfoAtEnd) != 0 ? file.Length - compressedInfo : file.Length))
            {
                throw new InvalidDataException("The bundle contains data outside its declared blocks.");
            }

            bundle.Data = data;
            return bundle;
        }

        private static byte[] Decompress(byte[] source, int size, uint compression) => compression switch
        {
            0 when source.Length == size => source,
            1 => Lzma.Decode(source, size),
            2 or 3 => Lz4.Decode(source, size),
            _ => throw new InvalidDataException("Unsupported bundle compression " + compression + ".")
        };
    }

    internal static class Lz4
    {
        internal static byte[] Decode(byte[] source, int size)
        {
            byte[] output = new byte[size];
            int input = 0, written = 0;

            while (input < source.Length)
            {
                int token = source[input++];
                int literals = token >> 4;

                if (literals == 15)
                {
                    literals += Length(source, ref input);
                }

                if (literals > source.Length - input || literals > size - written)
                {
                    throw new InvalidDataException("Corrupt LZ4 literal run.");
                }

                Buffer.BlockCopy(source, input, output, written, literals);
                input += literals;
                written += literals;

                if (input == source.Length) break;

                if (input + 2 > source.Length)
                {
                    throw new InvalidDataException("Corrupt LZ4 match offset.");
                }

                int offset = source[input] | source[input + 1] << 8;
                input += 2;

                int match = token & 15;
                if (match == 15)
                {
                    match += Length(source, ref input);
                }

                match += 4;

                if (offset == 0 || offset > written || match > size - written)
                {
                    throw new InvalidDataException("Corrupt LZ4 match.");
                }

                for (int index = 0; index < match; index++)
                {
                    output[written] = output[written - offset];
                    written++;
                }
            }

            if (written != size)
            {
                throw new InvalidDataException("LZ4 block size mismatch.");
            }

            return output;
        }

        private static int Length(byte[] source, ref int input)
        {
            int total = 0;
            int value;
            do
            {
                if (input >= source.Length)
                {
                    throw new InvalidDataException("Corrupt LZ4 length.");
                }

                value = source[input++];
                total = checked(total + value);
            }
            while (value == 255);

            return total;
        }
    }

    internal sealed class ByteReader
    {
        private readonly byte[] _data;
        private readonly int _end;

        internal int Position { get; set; }
        internal bool BigEndian { get; set; }
        internal int Remaining => _end - Position;

        internal ByteReader(byte[] data, bool bigEndian, int start = 0, int length = -1)
        {
            _data = data;
            Position = start;
            _end = length < 0 ? data.Length : checked(start + length);
            BigEndian = bigEndian;

            if (_end > data.Length)
            {
                throw new InvalidDataException("Data range is outside the file.");
            }
        }

        private ReadOnlySpan<byte> Take(int count)
        {
            if (count < 0 || count > Remaining)
            {
                throw new InvalidDataException("Unexpected end of data.");
            }

            ReadOnlySpan<byte> span = new ReadOnlySpan<byte>(_data, Position, count);
            Position += count;
            return span;
        }

        internal void Skip(long count)
        {
            if (count < 0 || count > Remaining)
            {
                throw new InvalidDataException("Unexpected end of data.");
            }

            Position += (int)count;
        }

        internal void Align(int boundary)
        {
            int padding = (boundary - Position % boundary) % boundary;
            Skip(Math.Min(padding, Remaining));
        }

        internal byte[] Bytes(int count) => Take(count).ToArray();
        internal byte Byte() => Take(1)[0];
        internal bool Bool() => Byte() != 0;
        internal ushort UInt16() => BigEndian ? BinaryPrimitives.ReadUInt16BigEndian(Take(2)) : BinaryPrimitives.ReadUInt16LittleEndian(Take(2));
        internal short Int16() => (short)UInt16();
        internal uint UInt32() => BigEndian ? BinaryPrimitives.ReadUInt32BigEndian(Take(4)) : BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
        internal int Int32() => (int)UInt32();
        internal long Int64() => BigEndian ? BinaryPrimitives.ReadInt64BigEndian(Take(8)) : BinaryPrimitives.ReadInt64LittleEndian(Take(8));
        internal float Single() => BitConverter.Int32BitsToSingle(Int32());

        internal string CString(int maximum)
        {
            int start = Position;
            int end = Array.IndexOf(_data, (byte)0, start, Math.Min(maximum + 1, Remaining));

            if (end < 0)
            {
                throw new InvalidDataException("Unterminated string.");
            }

            Position = end + 1;
            return Encoding.UTF8.GetString(_data, start, end - start);
        }

        internal string String(int maximum)
        {
            int length = Int32();
            if (length < 0 || length > maximum)
            {
                throw new InvalidDataException("String length out of range.");
            }

            return Encoding.UTF8.GetString(Take(length));
        }
    }
}
