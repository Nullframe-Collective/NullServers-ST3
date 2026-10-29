using Il2Cpp;
using Il2CppFishNet.Transporting.Relay;
using Il2CppInterop.Runtime;
using NullServers.Network;
using NullServers.UI;
using UnityEngine;
using UnityEngine.UI;

namespace NullServers.Players
{
    internal sealed class PlayerList
    {
        private static PlayerList _instance;
        private readonly RelaySession _session;
        private readonly PlayerProfiles _profiles;
        private float _nextUpdate;

        internal PlayerList(RelaySession session, PlayerProfiles profiles)
        {
            _instance = this;
            _session = session;
            _profiles = profiles;
        }

        internal static void AfterListBuilt(RoomMultiplayerMenu __instance) => _instance.Refresh(__instance, true);

        internal static void AfterMenuUpdate(RoomMultiplayerMenu __instance)
        {
            PlayerList instance = _instance;
            if (!__instance.IsPaused || Time.unscaledTime < instance._nextUpdate)
            {
                return;
            }

            instance._nextUpdate = Time.unscaledTime + 0.25f;
            instance.Refresh(__instance, false);
        }

        // Replaces each row of the game's player list with our name (colored, "(Host)") and a kills/deaths column.
        private void Refresh(RoomMultiplayerMenu menu, bool configure)
        {
            if (!_session.IsNullFrame || _session.IsSwitching || !_session.Relay || _session.Relay.CurrentRoom == null) return;
            if (menu.PlayerRows == null) return;

            foreach (var entry in menu.PlayerRows)
            {
                PlayerInfo player = entry.Key;
                Text name = entry.Value;
                if (!player || !name) continue;

                Transform row = name.transform.parent;
                HideNativeButtons(row);

                Transform info = row.Find("ExtraInfo");
                Text stats = info ? info.GetComponentInChildren(Il2CppType.Of<Text>(), true)?.Cast<Text>() : null;
                if (!stats)
                {
                    stats = GameUi.Create<Text>("ExtraInfo", row);
                    configure = true;
                }

                if (configure)
                {
                    LayoutRow(name, stats);
                }

                if (!stats.gameObject.activeSelf)
                {
                    stats.gameObject.SetActive(true);
                }

                ShowPlayer(player, name, stats);
            }
        }

        // Kicking and banning happen in Server Settings, so the game's own row buttons are hidden.
        private static void HideNativeButtons(Transform row)
        {
            foreach (string childName in new[] { "KickButton", "BanButton", "ExtraInfoAlt" })
            {
                Transform child = row.Find(childName);
                if (child && child.gameObject.activeSelf)
                {
                    child.gameObject.SetActive(false);
                }
            }
        }

        private static void LayoutRow(Text name, Text stats)
        {
            RectTransform nameRect = name.rectTransform;
            nameRect.anchorMin = Vector2.zero;
            nameRect.anchorMax = new Vector2(0.57f, 1f);
            nameRect.offsetMin = new Vector2(8f, 0f);
            nameRect.offsetMax = new Vector2(-8, 0f);

            name.alignment = TextAnchor.MiddleLeft;
            name.supportRichText = true;
            GameUi.Fit(name, 12, Mathf.Max(12, name.fontSize));

            RectTransform statsRect = stats.rectTransform;
            statsRect.anchorMin = new Vector2(0.59f, 0f);
            statsRect.anchorMax = Vector2.one;
            statsRect.offsetMin = Vector2.zero;
            statsRect.offsetMax = new Vector2(-8, 0f);

            stats.font = name.font;
            stats.fontSize = name.fontSize;
            GameUi.Fit(stats, 12, Mathf.Max(12, name.fontSize));
            stats.alignment = TextAnchor.MiddleRight;
            stats.supportRichText = false;
            stats.raycastTarget = false;
            stats.color = Color.white;
        }

        // A player whose profile has not arrived yet is shown as "Joining..." instead of their relay account name.
        private void ShowPlayer(PlayerInfo player, Text name, Text stats)
        {
            bool known = _profiles.TryGet(player.ClientId, out var profile);
            string nickname = known ? profile.RichName : "<color=#9A9A9A>Joining...</color>";
            string title = player.ClientId == RelayProtocol.HostPlayerId ? nickname + " (Host)" : nickname;
            if (name.text != title)
            {
                name.text = title;
            }

            string score = known ? $"Kills: {player.Kills}   Deaths: {player.Deaths}" : "";
            if (stats.text != score)
            {
                stats.text = score;
            }
        }
    }
}
