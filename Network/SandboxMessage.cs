using NullServers.Customization;
using NullServers.Room;
using System.Buffers.Binary;
using System.Text.Json;

namespace NullServers.Network
{
    internal enum SandboxKind
    {
        Hello,
        Ready,
        Create,
        Edit,
        Delete,
        Clear,
        Admin,
        Object,
        Effects,
        Ragdoll,
        RemoveRagdoll,
        Pose,
        Notice,
        PoseBatch,
        CarryState,
        CarryAim,
        CarryGrab,
        CarryRelease,
        Flight,
        BatchEdit,
        BatchDelete,
        Living,
        Escape
    }

    internal enum PlayerCommand
    {
        Scale,
        Fly,
        Freeze,
        Invincible,
        Kill,
        Respawn,
        Remove,
        Heal,
        Bring,
        GoTo,
        Launch,
        Rename,
        Reset
    }

    internal sealed record Point3(float X, float Y, float Z)
    {
        internal bool Valid(float limit = 50000f)
        {
            return float.IsFinite(X) &&
                float.IsFinite(Y) &&
                float.IsFinite(Z) &&
                Math.Abs(X) <= limit &&
                Math.Abs(Y) <= limit &&
                Math.Abs(Z) <= limit;
        }
    }

    internal sealed record Rotation4(float X, float Y, float Z, float W)
    {
        internal bool Valid =>
            float.IsFinite(X) &&
            float.IsFinite(Y) &&
            float.IsFinite(Z) &&
            float.IsFinite(W) &&
            X * X + Y * Y + Z * Z + W * W is > 0.95f and < 1.05f;
    }

    internal sealed record BuildingObject(
        int Id,
        int Owner,
        string Asset,
        Point3 Position,
        Rotation4 Rotation,
        Point3 Scale,
        string Color,
        string Texture,
        bool Dynamic,
        int Revision = 0,
        bool Effects = true)
    {
        internal bool Valid =>
            Id >= 0 &&
            Owner >= 0 &&
            Revision >= 0 &&
            Asset is { Length: > 0 and <= 192 } &&
            Position?.Valid() == true &&
            Rotation?.Valid == true &&
            Scale?.Valid(30f) == true &&
            Scale.X >= 0.05f &&
            Scale.Y >= 0.05f &&
            Scale.Z >= 0.05f &&
            RoomOptions.IsValidColor(Color) &&
            Appearance.ValidUrl(Texture);
    }

    internal sealed record PlayerEffects(
        int Id,
        float Scale = 1f,
        bool Fly = false,
        bool Frozen = false,
        bool Invincible = false,
        bool Noclip = false);

    internal sealed record SandboxCommand(
        PlayerCommand Action,
        int Target,
        float Value = 0f,
        string Text = null,
        Point3 Position = null);

    internal sealed record RagdollDefinition(
        int Id,
        string Name,
        Point3 Position,
        string[] Bones,
        int Player = -1,
        RagdollSkin Skin = null,
        bool Living = false,
        Point3 Scale = null,
        int NPC = -1);

    internal sealed record SandboxMessage(
        int Version,
        string Room,
        int RoomObject,
        string Epoch,
        SandboxKind Kind,
        BuildingObject Object = null,
        PlayerEffects Effects = null,
        SandboxCommand Command = null,
        RagdollDefinition Ragdoll = null,
        int Id = 0,
        int Sequence = 0,
        string Data = null,
        CarryState Carry = null,
        LivingState Living = null,
        BuildingObject[] Objects = null,
        double SentAt = 0d)
    {
        internal const ushort BroadcastId = 0x4E49;
        internal const int MaxBytes = 131072;
        internal const int MaxObjects = 8192;
        internal const int MaxObjectsPerPlayer = 1024;
        internal static readonly JsonSerializerOptions JsonOptions = new()
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        internal static string Serialize(SandboxMessage message) => JsonSerializer.Serialize(message, JsonOptions);

        internal static bool CanEdit(int owner, int requester, bool administrator) => owner == requester || administrator;

        internal bool AllowsChannel(bool fromHost, bool reliable) => reliable ||
            (fromHost ? Kind is SandboxKind.Pose or SandboxKind.PoseBatch or SandboxKind.CarryState : Kind == SandboxKind.CarryAim);

        internal static bool TryParse(string json, out SandboxMessage message)
        {
            message = JsonPacket.Read<SandboxMessage>(json, MaxBytes);
            if (message == null || !ValidHeader(message) || !ValidPayload(message) || !HasRequiredPayload(message))
            {
                message = null;
                return false;
            }

            return true;
        }

        private static bool ValidHeader(SandboxMessage value) =>
            value.Version == 2 &&
            value.Room is { Length: > 0 and <= 64 } &&
            value.RoomObject >= 0 &&
            value.Epoch is { Length: <= 32 } &&
            Enum.IsDefined(typeof(SandboxKind), value.Kind) &&
            value.Id >= 0 &&
            value.Sequence >= 0 &&
            double.IsFinite(value.SentAt) && value.SentAt is >= 0d and <= 1000000000d &&
            !(value.Data?.Length > 2048);

        private static bool ValidPayload(SandboxMessage value) =>
            (value.Carry == null || value.Carry.Valid) &&
            (value.Living == null || value.Living.Valid) &&
            (value.Objects == null || ValidObjects(value.Objects)) &&
            (value.Object == null || value.Object.Valid) &&
            (value.Effects == null || ValidEffects(value.Effects)) &&
            (value.Command == null || ValidCommand(value.Command)) &&
            (value.Ragdoll == null || ValidRagdoll(value.Ragdoll));

        private static bool ValidObjects(BuildingObject[] objects) =>
            objects.Length is >= 1 and <= 32 &&
            objects.All(item => item?.Valid == true) &&
            objects.Select(item => item.Id).Distinct().Count() == objects.Length;

        private static bool ValidEffects(PlayerEffects effects) =>
            effects.Id >= 0 && float.IsFinite(effects.Scale) && effects.Scale is >= 0.2f and <= 5f;

        private static bool ValidCommand(SandboxCommand command) =>
            Enum.IsDefined(typeof(PlayerCommand), command.Action) &&
            command.Target >= 0 &&
            float.IsFinite(command.Value) &&
            !(command.Text?.Length > 128) &&
            (command.Position == null || command.Position.Valid());

        private static bool ValidRagdoll(RagdollDefinition ragdoll) =>
            ragdoll.Id >= 1 &&
            ragdoll.Player is >= -1 and <= 4095 &&
            ragdoll.NPC is >= -1 and < 2000000 &&
            !(ragdoll.Living && (ragdoll.Player >= 0) == (ragdoll.NPC >= 0)) &&
            ragdoll.Name is { Length: > 0 and <= 192 } &&
            ragdoll.Position?.Valid() == true &&
            (ragdoll.Scale == null || ragdoll.Scale.Valid(100f) && ragdoll.Scale.X > 0f && ragdoll.Scale.Y > 0f && ragdoll.Scale.Z > 0f) &&
            ragdoll.Bones is { Length: > 0 and <= 64 } &&
            ragdoll.Bones.All(path => path is { Length: <= 256 }) &&
            (ragdoll.Skin == null || ragdoll.Skin.Valid) &&
            ragdoll.Bones.Distinct().Count() == ragdoll.Bones.Length;

        private static bool HasRequiredPayload(SandboxMessage value) => value.Kind switch
        {
            SandboxKind.Create or SandboxKind.Edit or SandboxKind.Object => value.Object != null,
            SandboxKind.BatchEdit or SandboxKind.BatchDelete => value.Objects != null,
            SandboxKind.Living => value.Living != null,
            SandboxKind.Admin => value.Command != null,
            SandboxKind.Effects => value.Effects != null,
            SandboxKind.Ragdoll => value.Ragdoll != null,
            SandboxKind.Pose or SandboxKind.PoseBatch or SandboxKind.Notice => value.Data != null,
            SandboxKind.CarryAim => value.Carry is { Position: not null, Rotation: not null },
            SandboxKind.CarryState or SandboxKind.CarryGrab or SandboxKind.CarryRelease => value.Carry != null,
            _ => true
        };
    }

    internal static class SandboxPose
    {
        internal static string Encode(int offset, IReadOnlyList<float> values)
        {
            if (offset < 0 || offset > 63 || values.Count is < 7 or > 112 || values.Count % 7 != 0)
            {
                throw new ArgumentException("Invalid pose size.");
            }

            byte[] bytes = new byte[2 + values.Count * 4];
            bytes[0] = (byte)offset;
            bytes[1] = (byte)(values.Count / 7);

            for (int index = 0; index < values.Count; index++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(
                    bytes.AsSpan(2 + index * 4),
                    BitConverter.SingleToInt32Bits(values[index]));
            }

            return Convert.ToBase64String(bytes);
        }

        internal static bool TryDecode(string data, out int offset, out float[] values)
        {
            offset = 0;
            values = null;

            if (data == null || data.Length > 604) return false;

            Span<byte> bytes = stackalloc byte[450];
            if (!Convert.TryFromBase64String(data, bytes, out var count) ||
                count < 30 ||
                bytes[1] is < 1 or > 16 ||
                bytes[0] + bytes[1] > 64 ||
                count != 2 + bytes[1] * 28)
            {
                return false;
            }

            float[] decoded = new float[bytes[1] * 7];
            for (int index = 0; index < decoded.Length; index++)
            {
                decoded[index] = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(2 + index * 4, 4)));
                if (!float.IsFinite(decoded[index]) || Math.Abs(decoded[index]) > 50000f)
                {
                    return false;
                }
            }

            for (int index = 0; index < decoded.Length; index += 7)
            {
                if (!new Rotation4(decoded[index + 3], decoded[index + 4], decoded[index + 5], decoded[index + 6]).Valid)
                {
                    return false;
                }
            }

            offset = bytes[0];
            values = decoded;

            return true;
        }
    }
}
