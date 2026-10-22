using System.Text;
using System.Text.Json;

namespace NullServers.Room
{
    internal sealed class RoundSignal
    {
        internal const ushort BroadcastId = 0x4E47;
        internal const int MaxBytes = 1024;

        public int Version { get; }
        public string RoomId { get; }
        public string Revision { get; }
        public string Scene { get; }
        public bool Ready { get; }
        public int ObjectId { get; }
        public bool Missing { get; }
        public ulong SceneId { get; }

        public RoundSignal
        (
            int version,
            string roomId,
            string revision,
            string scene,
            bool ready,
            int objectId,
            bool missing = false,
            ulong sceneId = 0
        )
        {
            Version = version;
            RoomId = roomId;
            Revision = revision;
            Scene = scene;
            Ready = ready;
            ObjectId = objectId;
            Missing = missing;
            SceneId = sceneId;
        }

        internal static bool TryParse(string json, out RoundSignal signal)
        {
            signal = null;

            if (string.IsNullOrEmpty(json) || Encoding.UTF8.GetByteCount(json) > MaxBytes)
            {
                return false;
            }

            try
            {
                RoundSignal value = JsonSerializer.Deserialize<RoundSignal>(json);
                if 
                (
                    value == null ||
                    value.Version != 1 ||
                    string.IsNullOrEmpty(value.RoomId) ||
                    value.RoomId.Length > 64 ||
                    value.RoomId.Any(char.IsControl) ||
                    value.Revision == null ||
                    !Guid.TryParseExact(value.Revision, "N", out _) ||
                    string.IsNullOrWhiteSpace(value.Scene) ||
                    value.Scene.Length > 128 ||
                    value.Scene.Any(char.IsControl) ||
                    value.ObjectId < -1 ||
                    (value.Ready && (value.ObjectId < 0 || value.Missing)) ||
                    (value.Missing && (value.ObjectId < 0 || value.SceneId == 0))
                )
                {
                    return false;
                }

                signal = value;

                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }
}
