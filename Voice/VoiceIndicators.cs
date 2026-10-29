using Il2Cpp;
using UnityEngine;

namespace NullServers.Voice
{
    internal sealed class VoiceIndicators
    {
        private readonly Dictionary<int, PlayerNetworkController> _players = new();
        private float _nextScan;

        internal void Update()
        {
            foreach (int id in VoiceApi.Speaking.Keys.ToArray())
            {
                if (VoiceApi.Speaking[id] < Time.unscaledTime)
                {
                    VoiceApi.Speaking.Remove(id);
                }
            }

            if (!VoiceApi.Active || Time.unscaledTime < _nextScan) return;

            _nextScan = Time.unscaledTime + 0.5f;
            _players.Clear();

            foreach (PlayerNetworkController player in UnityEngine.Object.FindObjectsOfType<PlayerNetworkController>())
            {
                if (player.IsSpawned)
                {
                    _players[player.OwnerId] = player;
                }
            }
        }

        internal void Draw()
        {
            if (!VoiceApi.Active || Event.current.type != EventType.Repaint) return;

            Camera camera = Camera.main;
            if (!camera) return;

            Color previous = GUI.color;
            GUI.color = new Color(0.35f, 1f, 0.55f);

            foreach (int id in VoiceApi.Speaking.Keys)
            {
                if (id == VoiceApi.LocalId || !_players.TryGetValue(id, out var player) || !player) continue;

                Vector3 point = camera.WorldToScreenPoint(player.transform.position + Vector3.up * 2.5f);
                if (point.z <= 0f) continue;

                float size = Mathf.Clamp(45f / MathF.Sqrt(point.z), 18f, 36f);
                GUI.DrawTexture(new Rect(point.x - size / 2f, Screen.height - point.y - size, size, size), VoiceIcons.Speaker);
            }

            GUI.color = previous;
        }
    }
}
