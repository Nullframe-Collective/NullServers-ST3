using NullServers.Customization;
using NullServers.Room;
using System.Text;
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
        GunState,
        GunAim,
        GunGrab,
        GunRelease
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
            return 
                float.IsFinite(X) &&
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

    internal sealed record BuildingObject
    (
        int Id,
        int Owner,
        string Asset,
        Point3 Position,
        Rotation4 Rotation,
        Point3 Scale,
        string Color,
        string Texture,
        bool Dynamic,
        int Revision = 0
    )
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

    internal sealed record PlayerEffects(int Id, float Scale = 1f, bool Fly = false, bool Frozen = false, bool Invincible = false);

    internal sealed record SandboxCommand
    (
        PlayerCommand Action,
        int Target,
        float Value = 0f,
        string Text = null,
        Point3 Position = null
    );

    internal sealed record RagdollDefinition(int Id, string Name, Point3 Position, string[] Bones, int Player = -1);

    internal sealed record GunState
    (
        int Player,
        bool Equipped,
        int Holding = 0,
        Point3 Position = null,
        Rotation4 Rotation = null,
        bool Throw = false,
        bool Freeze = false,
        int Sequence = 0,
        Point3 Grip = null
    )
    {
        internal bool Valid => 
            Player is >= 0 and <= 4095 &&
            Holding is >= 0 and < 1000000 &&
            Sequence >= 0 &&
            (Position == null || Position.Valid()) &&
            (Rotation == null || Rotation.Valid) &&
            (Grip == null || Grip.Valid(1000f));
    }

    internal sealed record SandboxMessage
    (
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
        GunState Gun = null
    )
    {
        internal const ushort BroadcastId = 0x4E49;
        internal const int MaxBytes = 16384;
        internal const int MaxObjects = 8192;
        internal const int MaxObjectsPerPlayer = 1024;
        internal static readonly JsonSerializerOptions JsonOptions = new()
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        internal static bool CanEdit(int owner, int requester, bool administrator) => owner == requester || administrator;

        internal static bool TryParse(string json, out SandboxMessage message)
        {
            message = null;

            if (string.IsNullOrEmpty(json) || Encoding.UTF8.GetByteCount(json) > MaxBytes)
            {
                return false;
            }

            try
            {
                SandboxMessage value = JsonSerializer.Deserialize<SandboxMessage>(json);
                if 
                (
                    value == null ||
                    value.Version != 1 ||
                    value.Room is not { Length: > 0 and <= 64 } ||
                    value.RoomObject < 0 ||
                    value.Epoch is not { Length: <= 32 } ||
                    !Enum.IsDefined(typeof(SandboxKind), value.Kind) ||
                    value.Id < 0 ||
                    value.Sequence < 0 ||
                    value.Data?.Length > 2048 ||
                    value.Gun != null &&
                    !value.Gun.Valid ||
                    value.Object != null &&
                    !value.Object.Valid ||
                    value.Effects is { } effects &&
                    (effects.Id < 0 || !float.IsFinite(effects.Scale) || effects.Scale is < 0.2f or > 5f) ||
                    value.Command is { } command &&
                    (!Enum.IsDefined(typeof(PlayerCommand), command.Action) ||
                    command.Target < 0 ||
                    !float.IsFinite(command.Value) ||
                    command.Text?.Length > 128 ||
                    command.Position != null &&
                    !command.Position.Valid()) ||
                    value.Ragdoll is { } ragdoll &&
                    (ragdoll.Id < 1 ||
                    ragdoll.Player < -1 ||
                    ragdoll.Player > 4095 ||
                    ragdoll.Name is not { Length: > 0 and <= 192 } ||
                    ragdoll.Position?.Valid() != true ||
                    ragdoll.Bones is not { Length: > 0 and <= 64 } ||
                    ragdoll.Bones.Any(path => path == null || path.Length > 256) ||
                    ragdoll.Bones.Distinct().Count() != ragdoll.Bones.Length)
                )
                {
                    return false;
                }

                if 
                (
                    value.Kind is SandboxKind.Create or SandboxKind.Edit or SandboxKind.Object &&
                    value.Object == null ||
                    value.Kind == SandboxKind.Admin &&
                    value.Command == null ||
                    value.Kind == SandboxKind.Effects &&
                    value.Effects == null ||
                    value.Kind == SandboxKind.Ragdoll &&
                    value.Ragdoll == null ||
                    value.Kind is SandboxKind.Pose or SandboxKind.PoseBatch or SandboxKind.Notice &&
                    value.Data == null ||
                    value.Kind is SandboxKind.GunState or SandboxKind.GunAim or SandboxKind.GunGrab or SandboxKind.GunRelease &&
                    value.Gun == null ||
                    value.Kind == SandboxKind.GunAim &&
                    (value.Gun.Position == null || value.Gun.Rotation == null)
                )
                {
                    return false;
                }

                message = value;

                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }
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
                System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian
                (
                    bytes.AsSpan(2 + index * 4),
                    BitConverter.SingleToInt32Bits(values[index])
                );
            }

            return Convert.ToBase64String(bytes);
        }

        internal static bool TryDecode(string data, out int offset, out float[] values)
        {
            offset = 0;
            values = null;

            if (data == null || data.Length > 604) return false;

            Span<byte> bytes = stackalloc byte[450];
            if 
            (
                !Convert.TryFromBase64String(data, bytes, out var count) ||
                count < 30 ||
                bytes[1] is < 1 or > 16 ||
                bytes[0] + bytes[1] > 64 ||
                count != 2 + bytes[1] * 28
            )
            {
                return false;
            }

            float[] decoded = new float[bytes[1] * 7];
            for (int index = 0; index < decoded.Length; index++)
            {
                decoded[index] = BitConverter.Int32BitsToSingle(System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(2 + index * 4, 4)));
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
