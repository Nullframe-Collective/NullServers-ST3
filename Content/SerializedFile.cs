namespace NullServers.Content
{
    internal sealed record TypeTreeNode(string Type, string Name, int Level, int Size, int Flags)
    {
        internal bool Aligned => (Flags & 0x4000) != 0;
    }

    internal sealed class SerializedType
    {
        internal int ClassId;
        internal short ScriptIndex;
        internal List<TypeTreeNode> Tree = new();
    }

    internal sealed record SerializedObject(long PathId, long Start, uint Size, SerializedType Type);

    internal sealed record SerializedExternal(string Path, int Type);

    internal sealed class SerializedFile
    {
        internal int Version { get; private set; }
        internal bool HasTypeTree { get; private set; }
        internal List<SerializedType> Types { get; } = new();
        internal List<SerializedObject> Objects { get; } = new();
        internal List<SerializedExternal> Externals { get; } = new();
        internal byte[] Data { get; private set; }
        internal int Start { get; private set; }
        internal int Length { get; private set; }
        internal bool LittleEndian { get; private set; }

        internal static SerializedFile Read(byte[] data, int start, int length, BundleLimits limits)
        {
            SerializedFile file = new() { Data = data, Start = start, Length = length };
            ByteReader reader = new(data, bigEndian: true, start, length);

            reader.UInt32();
            reader.UInt32();
            file.Version = reader.Int32();
            reader.UInt32();

            if (file.Version is < 21 or > 23)
            {
                throw new InvalidDataException("Unsupported serialized file version " + file.Version + ".");
            }

            file.LittleEndian = reader.Byte() == 0;
            reader.Skip(3);
            reader.UInt32();
            long fileSize = reader.Int64();
            long dataOffset = reader.Int64();
            reader.Int64();

            if (fileSize != length || dataOffset < 0 || dataOffset > length)
            {
                throw new InvalidDataException("Serialized file header is inconsistent.");
            }

            reader.BigEndian = !file.LittleEndian;
            reader.CString(64);
            reader.Int32();
            file.HasTypeTree = reader.Bool();

            int typeCount = reader.Int32();
            if (typeCount < 0 || typeCount > limits.MaxTypes)
            {
                throw new InvalidDataException("Too many types.");
            }

            for (int index = 0; index < typeCount; index++)
            {
                file.Types.Add(ReadType(reader, file, limits, reference: false));
            }

            int objectCount = reader.Int32();
            if (objectCount < 0 || objectCount > limits.MaxObjects)
            {
                throw new InvalidDataException("Too many objects.");
            }

            for (int index = 0; index < objectCount; index++)
            {
                reader.Align(4);
                long pathId = reader.Int64();
                long objectStart = reader.Int64();
                uint size = reader.UInt32();
                int typeIndex = reader.Int32();

                if (typeIndex < 0 || typeIndex >= file.Types.Count || objectStart < 0 || dataOffset + objectStart + size > length)
                {
                    throw new InvalidDataException("An object points outside the serialized file.");
                }

                file.Objects.Add(new SerializedObject(pathId, start + dataOffset + objectStart, size, file.Types[typeIndex]));
            }

            int scriptCount = reader.Int32();
            if (scriptCount < 0 || scriptCount > limits.MaxObjects)
            {
                throw new InvalidDataException("Too many script references.");
            }

            for (int index = 0; index < scriptCount; index++)
            {
                reader.Int32();
                reader.Align(4);
                reader.Int64();
            }

            int externalCount = reader.Int32();
            if (externalCount < 0 || externalCount > 64)
            {
                throw new InvalidDataException("Too many external references.");
            }

            for (int index = 0; index < externalCount; index++)
            {
                reader.CString(256);
                reader.Skip(16);
                int type = reader.Int32();
                file.Externals.Add(new SerializedExternal(reader.CString(512), type));
            }

            int referenceCount = reader.Int32();
            if (referenceCount < 0 || referenceCount > limits.MaxTypes)
            {
                throw new InvalidDataException("Too many reference types.");
            }

            for (int index = 0; index < referenceCount; index++)
            {
                ReadType(reader, file, limits, reference: true);
            }

            return file;
        }

        private static SerializedType ReadType(ByteReader reader, SerializedFile file, BundleLimits limits, bool reference)
        {
            SerializedType type = new() { ClassId = reader.Int32() };
            reader.Bool();
            type.ScriptIndex = reader.Int16();

            if (reference && type.ScriptIndex >= 0 || type.ClassId == 114)
            {
                reader.Skip(16);
            }

            reader.Skip(16);

            if (file.HasTypeTree)
            {
                int nodeCount = reader.Int32();
                int stringSize = reader.Int32();

                if (nodeCount < 0 || nodeCount > limits.MaxTreeNodes || stringSize < 0 || stringSize > 1024 * 1024)
                {
                    throw new InvalidDataException("Type tree is too large.");
                }

                (int Level, uint TypeOffset, uint NameOffset, int Size, int Flags)[] raw = new (int Level, uint TypeOffset, uint NameOffset, int Size, int Flags)[nodeCount];
                for (int index = 0; index < nodeCount; index++)
                {
                    reader.UInt16();
                    int level = reader.Byte();
                    reader.Byte();
                    uint typeOffset = reader.UInt32();
                    uint nameOffset = reader.UInt32();
                    int size = reader.Int32();
                    reader.Int32();
                    int flags = reader.Int32();
                    reader.Int64();
                    raw[index] = (level, typeOffset, nameOffset, size, flags);
                }

                byte[] strings = reader.Bytes(stringSize);
                foreach (var node in raw)
                {
                    type.Tree.Add(new TypeTreeNode(Text(strings, node.TypeOffset), Text(strings, node.NameOffset), node.Level, node.Size, node.Flags));
                }

                if (reference)
                {
                    reader.CString(256);
                    reader.CString(256);
                    reader.CString(256);
                }
                else
                {
                    int dependencies = reader.Int32();
                    if (dependencies < 0 || dependencies > limits.MaxTypes)
                    {
                        throw new InvalidDataException("Too many type dependencies.");
                    }

                    reader.Skip(dependencies * 4L);
                }
            }

            return type;
        }

        private static string Text(byte[] strings, uint offset)
        {
            if ((offset & 0x80000000) != 0)
            {
                return UnityCommonStrings.Get(offset & 0x7FFFFFFF) ?? throw new InvalidDataException("Unknown common type string.");
            }

            int end = Array.IndexOf(strings, (byte)0, (int)Math.Min(offset, (uint)strings.Length));
            if (offset >= strings.Length || end < 0)
            {
                throw new InvalidDataException("Type string outside its table.");
            }

            return System.Text.Encoding.UTF8.GetString(strings, (int)offset, end - (int)offset);
        }

        internal TypeTreeValue ReadObject(SerializedObject item, BundleLimits limits)
        {
            ByteReader reader = new(Data, !LittleEndian, checked((int)item.Start), checked((int)item.Size));
            int steps = 0;
            int index = 0;
            TypeTreeValue value = TypeTreeValue.Read(item.Type.Tree, ref index, reader, ref steps, limits.MaxReadSteps);

            if (reader.Remaining != 0)
            {
                throw new InvalidDataException("Object data does not match its type tree.");
            }

            return value;
        }
    }

    internal sealed class TypeTreeValue
    {
        internal string Type;
        internal string Name;
        internal object Scalar;
        internal long Count;
        internal List<TypeTreeValue> Children;

        internal TypeTreeValue Field(string name) => Children?.FirstOrDefault(child => child.Name == name);

        internal IEnumerable<TypeTreeValue> Descendants()
        {
            yield return this;
            if (Children == null) yield break;

            foreach (TypeTreeValue child in Children)
            {
                foreach (TypeTreeValue nested in child.Descendants())
                {
                    yield return nested;
                }
            }
        }

        internal long Number => Scalar switch
        {
            long value => value,
            ulong value => (long)value,
            _ => 0
        };

        internal string Text => Scalar as string;

        private static int PrimitiveSize(string type) => type switch
        {
            "bool" or "char" or "SInt8" or "UInt8" => 1,
            "SInt16" or "UInt16" or "short" or "unsigned short" => 2,
            "int" or "SInt32" or "UInt32" or "unsigned int" or "float" or "Type*" => 4,
            "long long" or "unsigned long long" or "SInt64" or "UInt64" or "double" or "FileSize" => 8,
            _ => 0
        };

        private static int End(List<TypeTreeNode> tree, int index)
        {
            int level = tree[index].Level;
            int end = index + 1;

            while (end < tree.Count && tree[end].Level > level)
            {
                end++;
            }

            return end;
        }

        internal static TypeTreeValue Read(List<TypeTreeNode> tree, ref int index, ByteReader reader, ref int steps, int maxSteps)
        {
            if (++steps > maxSteps)
            {
                throw new InvalidDataException("Object is too complex to inspect.");
            }

            TypeTreeNode node = tree[index];
            int end = End(tree, index);
            TypeTreeValue value = new() { Type = node.Type, Name = node.Name };
            int size = PrimitiveSize(node.Type);
            bool align = node.Aligned;

            if (size > 0 && end == index + 1)
            {
                value.Scalar = size switch
                {
                    1 => (long)reader.Byte(),
                    2 => (long)reader.UInt16(),
                    4 => node.Type == "float" ? reader.Int32() : node.Type is "int" or "SInt32" ? (long)reader.Int32() : (long)reader.UInt32(),
                    _ => reader.Int64()
                };
            }
            else if (node.Type == "string")
            {
                value.Scalar = reader.String(1024 * 1024);
                align |= end > index + 1 && tree[index + 1].Aligned;
            }
            else if (node.Type == "TypelessData")
            {
                int length = reader.Int32();
                value.Count = length;
                reader.Skip(length);
            }
            else if (end > index + 1 && tree[index + 1].Type == "Array")
            {
                int arrayIndex = index + 1;
                value.Count = ReadArray(tree, arrayIndex, reader, ref steps, maxSteps, out value.Children);
                align |= tree[arrayIndex].Aligned;
            }
            else if (node.Type == "Array")
            {
                value.Count = ReadArray(tree, index, reader, ref steps, maxSteps, out value.Children);
            }
            else if (end == index + 1 && node.Size > 0)
            {
                reader.Skip(node.Size);
            }
            else
            {
                value.Children = new List<TypeTreeValue>();
                int child = index + 1;

                while (child < end)
                {
                    value.Children.Add(Read(tree, ref child, reader, ref steps, maxSteps));
                }
            }

            if (align)
            {
                reader.Align(4);
            }

            index = end;
            return value;
        }

        private static long ReadArray(List<TypeTreeNode> tree, int arrayIndex, ByteReader reader, ref int steps, int maxSteps, out List<TypeTreeValue> items)
        {
            int count = reader.Int32();
            int element = arrayIndex + 2;
            items = null;

            if (count < 0 || element >= tree.Count)
            {
                throw new InvalidDataException("Invalid array.");
            }

            int elementSize = PrimitiveSize(tree[element].Type);
            if (elementSize > 0 && End(tree, element) == element + 1)
            {
                reader.Skip((long)count * elementSize);
                return count;
            }

            items = new List<TypeTreeValue>();
            for (int item = 0; item < count; item++)
            {
                int cursor = element;
                items.Add(Read(tree, ref cursor, reader, ref steps, maxSteps));
            }

            return count;
        }
    }
}
