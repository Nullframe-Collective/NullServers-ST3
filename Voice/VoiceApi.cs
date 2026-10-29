using Il2Cpp;
using NullServers.Sandbox;
using UnityEngine;

namespace NullServers.Voice
{
    public static class VoiceApi
    {
        internal static Action<uint, byte[]> Sender;
        internal static Action Settings;
        internal static readonly Dictionary<int, float> Speaking = new();
        internal static readonly Dictionary<int, PlayerNetworkController> Players = new();
        public static event Action<int, uint, byte[]> Received;
        private static bool _enabled;

        // Turning VoiceChat off in ADDONS stops the local microphone and playback; the host still forwards other players.
        public static bool Enabled
        {
            get => _enabled && AllowedInAddons;
            set => _enabled = value;
        }

        public static bool Installed => Settings != null && AllowedInAddons;

        private static bool AllowedInAddons => Additional.Frameworks.Settings?.Enabled(Additional.Dependencies.Voice) != false;
        public static bool Active { get; internal set; }
        public static int LocalId { get; internal set; } = -1;
        public static string RoomId { get; internal set; }
        public static bool InputAllowed => Active && Enabled && Application.isFocused &&
            Cursor.lockState == CursorLockMode.Locked && !SandboxWorld.Typing();

        public static void Register(Action settings) => Settings = settings;

        public static void Unregister()
        {
            Settings = null;
            Enabled = false;
        }

        public static void Send(uint sequence, byte[] frame)
        {
            if (!Active || !Enabled || frame?.Length != Network.VoicePacket.AudioBytes) return;

            Sender?.Invoke(sequence, frame);
        }

        public static bool TryPosition(int clientId, out Vector3 position)
        {
            Players.TryGetValue(clientId, out var player);
            if (player && (!player.IsSpawned || player.OwnerId != clientId))
            {
                player = null;
            }

            position = player ? player.transform.position + Vector3.up * 1.6f : default;
            return player;
        }

        internal static void Receive(int clientId, uint sequence, byte[] data)
        {
            Speaking[clientId] = Time.unscaledTime + 0.35f;
            if (clientId != LocalId)
            {
                Received?.Invoke(clientId, sequence, data);
            }
        }
    }
}
