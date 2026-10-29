using NullServers.Network;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace NullServers.Content
{
    internal enum ContentStage
    {
        ListFiles,
        Files,
        Range,
        Chunk,
        Failed
    }

    internal sealed record ContentFile(string Name, long Size, string Sha256);

    internal sealed record ContentMessage(
        int Version,
        string Room,
        ContentStage Stage,
        string Pack,
        string Hash,
        string File = null,
        long Offset = 0,
        int Length = 0,
        string Data = null,
        ContentFile[] Files = null,
        string Error = null)
    {
        internal const ushort BroadcastId = 0x4E4C;
        internal const int MaxBytes = 65536;
        internal const int ChunkSize = 32768;
        internal const int MaxRange = 131072;

        private static readonly JsonSerializerOptions Options = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        internal string Serialize() => JsonSerializer.Serialize(this, Options);

        internal static bool TryParse(string json, out ContentMessage message)
        {
            message = JsonPacket.Read<ContentMessage>(json, MaxBytes);
            if (message == null || !ValidHeader(message) || !ValidStage(message))
            {
                message = null;
                return false;
            }

            return true;
        }

        private static bool ValidHeader(ContentMessage value) =>
            value.Version == 1 &&
            value.Room is { Length: > 0 and <= 64 } &&
            !value.Room.Any(char.IsControl) &&
            Enum.IsDefined(typeof(ContentStage), value.Stage) &&
            new FrameworkAsset(value.Pack, value.Hash).Valid &&
            (value.File == null || PackFiles.KnownName(value.File)) &&
            value.Offset >= 0 &&
            value.Length is >= 0 and <= MaxRange &&
            !(value.Data?.Length > ChunkSize / 3 * 4 + 4) &&
            !(value.Error?.Length > 256) &&
            !(value.Files?.Length > 8);

        private static bool ValidStage(ContentMessage value) => value.Stage switch
        {
            ContentStage.ListFiles => value.File == null && value.Data == null && value.Files == null,
            ContentStage.Files => value.Files is { Length: > 0 } files && files.All(ValidFile) &&
                files.Select(file => file.Name).Distinct(StringComparer.Ordinal).Count() == files.Length,
            ContentStage.Range => value.File != null && value.Length > 0 && value.Data == null,
            ContentStage.Chunk => value.File != null && value.Data != null,
            ContentStage.Failed => !string.IsNullOrWhiteSpace(value.Error),
            _ => false
        };

        private static bool ValidFile(ContentFile file) =>
            file != null &&
            PackFiles.KnownName(file.Name) &&
            file.Size >= 0 &&
            file.Sha256 is { Length: 64 } &&
            file.Sha256.All(Uri.IsHexDigit);
    }
}
