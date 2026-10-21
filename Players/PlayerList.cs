using HarmonyLib;
using Il2Cpp;
using Il2CppFishNet.Transporting.Relay;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using S3_NullServers.Network;
using UnityEngine;
using UnityEngine.UI;

namespace S3_NullServers.Players
{
    internal sealed class PlayerList
    {
        private static PlayerList _instance;
        private readonly RelaySession _session;
        private readonly PlayerProfiles _profiles;
        private float _nextUpdate;

        internal PlayerList(HarmonyLib.Harmony harmony, RelaySession session, PlayerProfiles profiles)
        {
            _instance = this;
            _session = session;
            _profiles = profiles;
            harmony.Patch(
                AccessTools.Method(typeof(RoomMultiplayerMenu), nameof(RoomMultiplayerMenu.LMHENDCDNHN)),
                postfix: new HarmonyMethod(typeof(PlayerList), nameof(AfterListBuilt)));
            harmony.Patch(
                AccessTools.Method(typeof(RoomMultiplayerMenu), nameof(RoomMultiplayerMenu.Update)),
                postfix: new HarmonyMethod(typeof(PlayerList), nameof(AfterMenuUpdate)));
        }

        private static void AfterListBuilt(RoomMultiplayerMenu __instance) => _instance.Refresh(__instance, true);

        private static void AfterMenuUpdate(RoomMultiplayerMenu __instance)
        {
            PlayerList instance = _instance;
            if (!__instance.IOMIAHNBDOG || Time.unscaledTime < instance._nextUpdate)
            {
                return;
            }

            instance._nextUpdate = Time.unscaledTime + 0.25f;
            instance.Refresh(__instance, false);
        }

        private void Refresh(RoomMultiplayerMenu menu, bool configure)
        {
            if (!_session.IsNullFrame ||
                _session.IsSwitching ||
                !_session.Relay ||
                _session.Relay.CurrentRoom == null ||
                menu.HOKHMBNMGHC == null)
            {
                return;
            }

            foreach (var entry in menu.HOKHMBNMGHC)
            {
                PlayerInfo player = entry.Key;
                Text name = entry.Value;
                if (!player || !name)
                {
                    continue;
                }

                Transform row = name.transform.parent;
                foreach (string childName in new[]
                {
                    "KickButton",
                    "BanButton",
                    "ExtraInfoAlt"
                })
                {
                    Transform child = row.Find(childName);
                    if (child && child.gameObject.activeSelf)
                    {
                        child.gameObject.SetActive(false);
                    }
                }

                Transform info = row.Find("ExtraInfo");
                Text stats = info ? info.GetComponentInChildren(Il2CppType.Of<Text>(), true)?.Cast<Text>() : null;
                if (!stats)
                {
                    GameObject label = new(
                        "ExtraInfo",
                        new Il2CppReferenceArray<Il2CppSystem.Type>(
                            new[]
                    {
                            Il2CppType.Of<RectTransform>(),
                            Il2CppType.Of<CanvasRenderer>(),
                            Il2CppType.Of<Text>()
                        }));
                    label.transform.SetParent(row, false);
                    stats = label.GetComponent(Il2CppType.Of<Text>()).Cast<Text>();
                    configure = true;
                }

                if (configure)
                {
                    RectTransform nameRect = name.rectTransform;
                    nameRect.anchorMin = Vector2.zero;
                    nameRect.anchorMax = new Vector2(0.57f, 1f);
                    nameRect.offsetMin = new Vector2(8f, 0f);
                    nameRect.offsetMax = new Vector2(-8, 0f);
                    name.alignment = TextAnchor.MiddleLeft;
                    name.supportRichText = false;
                    name.resizeTextForBestFit = true;
                    name.resizeTextMinSize = 12;
                    name.resizeTextMaxSize = Mathf.Max(12, name.fontSize);
                    RectTransform statsRect = stats.rectTransform;
                    statsRect.anchorMin = new Vector2(0.59f, 0f);
                    statsRect.anchorMax = Vector2.one;
                    statsRect.offsetMin = Vector2.zero;
                    statsRect.offsetMax = new Vector2(-8, 0f);
                    stats.font = name.font;
                    stats.fontSize = name.fontSize;
                    stats.resizeTextForBestFit = true;
                    stats.resizeTextMinSize = 12;
                    stats.resizeTextMaxSize = Mathf.Max(12, name.fontSize);
                    stats.alignment = TextAnchor.MiddleRight;
                    stats.supportRichText = false;
                    stats.raycastTarget = false;
                    stats.color = Color.white;
                }

                if (!stats.gameObject.activeSelf)
                {
                    stats.gameObject.SetActive(true);
                }

                string nickname = _profiles.TryGet(player.COJDPMKCLCM, out var profile) ? profile.Nickname : player.HOGIPFLCOLH;
                string title = nickname + (player.COJDPMKCLCM == RelayProtocol.HostPlayerId ? " (Host)" : "");
                if (name.text != title)
                {
                    name.text = title;
                }

                string score = $"Kills: {player.ANGFKCCEJKD}   Deaths: {player.DDLEOEFHMKL}";
                if (stats.text != score)
                {
                    stats.text = score;
                }
            }
        }
    }
}
