using System.Text.Json;
using System.Text.Encodings.Web;

namespace NullServers.Network
{
    internal sealed record VoicePacket(int Version, int Kind, int ClientId, uint Sequence, string Data = null)
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        internal const ushort BroadcastId = 0x4E56;
        internal const int MaxBytes = 640;
        internal const int AudioBytes = 244;

        internal static bool TryParse(string json, out VoicePacket packet)
        {
            packet = JsonPacket.Read<VoicePacket>(json, MaxBytes);
            if (packet == null || packet.Version != 1 || packet.ClientId is < -1 or > 65534 || packet.Kind is < 0 or > 2)
            {
                packet = null;
                return false;
            }

            if (packet.Kind != 1) return packet.Data == null;
            if (packet.Data is not { Length: 328 }) return false;

            Span<byte> audio = stackalloc byte[AudioBytes];
            return Convert.TryFromBase64String(packet.Data, audio, out int length) && length == AudioBytes &&
                audio[2] <= 88 && audio[3] == 0;
        }

        internal string Serialize() => JsonSerializer.Serialize(this, JsonOptions);
    }
}
