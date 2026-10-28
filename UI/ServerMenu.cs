using HarmonyLib;
using Il2Cpp;
using Il2CppFishNet.Transporting;
using Il2CppFishNet.Transporting.Relay;
using MelonLoader;
using NullServers.Network;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace NullServers.UI
{
    internal sealed class ServerMenu
    {
        private static ServerMenu _instance;
        private readonly RelaySession _session;
        private readonly MelonLogger.Instance _logger;
        private readonly CommunityMenu _community;
        private MainMenu _menu;
        private Button _officialButton;
        private Button _nullFrameButton;

        internal ServerMenu(HarmonyLib.Harmony harmony, MelonLogger.Instance logger, RelaySession session, CommunityMenu community)
        {
            _instance = this;
            _session = session;
            _logger = logger;
            _community = community;

            harmony.Patch(
                AccessTools.Method(typeof(MainMenu), nameof(MainMenu.ShowLobby)),
                postfix: new HarmonyMethod(typeof(ServerMenu), nameof(AfterShowLobby)));
        }

        private void ConfigureServerButtons(MainMenu menu)
        {
            _menu = menu;

            if (!menu.ServerPanel.activeInHierarchy) return;

            UpdaterV2 updater = menu.Updater;
            CheckForUpdatesManager manager = menu.UpdateManagerObject.GetComponent<CheckForUpdatesManager>();
            var labels = manager.ServerLabels;

            if (updater.Servers == null || updater.Servers.Count == 0 || labels.Length < 2)
            {
                _logger.Error("The server list or the built-in server buttons are unavailable.");
                return;
            }

            UpdaterV2.serverInfo source = updater.Servers[0];
            Il2CppSystem.Collections.Generic.List<UpdaterV2.serverInfo> servers = new();

            foreach (string name in new[] { "OFFICIAL SERVERS", "NULLFRAME SERVERS" })
            {
                servers.Add(new UpdaterV2.serverInfo
                {
                    serverName = name,
                    serverType = source.serverType,
                    serverIP = source.serverIP,
                    cloudRegion = source.cloudRegion
                });
            }

            updater.Servers = servers;
            manager.RefreshServers();

            for (int i = 0; i < labels.Length; i++)
            {
                labels[i].transform.parent.gameObject.SetActive(i < servers.Count);
                if (i < servers.Count)
                {
                    labels[i].text = servers[i].serverName;
                }
            }

            _officialButton = labels[0].GetComponent<Button>();
            _nullFrameButton = labels[1].GetComponent<Button>();

            GameUi.OnClick(_officialButton, () => SelectServers(false));

            GameUi.OnClick(_nullFrameButton, () => SelectServers(true));

            _logger.Msg("Official and NullFrame servers configured through UpdaterV2 using the built-in menu buttons.");
        }

        private void SelectServers(bool useNullFrame)
        {
            if (_session.IsSwitching) return;

            if (_session.Relay && _session.Relay.CurrentRoom != null)
            {
                _logger.Warning("Leave the current room before switching server groups.");
                return;
            }

            if (useNullFrame)
            {
                _community.Enter(() => MelonCoroutines.Start(SwitchServers(true)));
            }
            else
            {
                MelonCoroutines.Start(SwitchServers(false));
            }
        }

        private IEnumerator SwitchServers(bool useNullFrame)
        {
            _session.BeginSwitch(useNullFrame);
            _officialButton.interactable = _nullFrameButton.interactable = false;
            ClearLobbyRoomLists();

            try
            {
                if (_session.Relay && !_session.Relay.OfflineMode)
                {
                    _session.Relay.DisconnectFromRelay();

                    float timeout = Time.realtimeSinceStartup + 10f;

                    while (_session.Relay &&
                        (_session.Relay.RelayState != RelayConnectionState.Disconnected ||
                        (_session.Relay.ClientTransport &&
                        _session.Relay.ClientTransport.GetConnectionState(false) != LocalConnectionState.Stopped)))
                    {
                        if (!_menu || !_menu.ServerPanel.activeInHierarchy) yield break;

                        if (Time.realtimeSinceStartup >= timeout)
                        {
                            _logger.Warning("Timed out waiting for the previous relay connection to stop.");
                            yield break;
                        }

                        yield return null;
                    }

                    if (_session.Relay)
                    {
                        _session.ApplyVersion(_session.Relay);
                    }
                }

                if (!_menu || !_menu.ServerPanel.activeInHierarchy)
                {
                    yield break;
                }

                _session.IsSwitching = false;

                _logger.Msg($"Selected {(_session.IsNullFrame ? "NullFrame" : "official")} servers. GameVersion: {_session.SelectedVersion ?? Application.version}.");
                _menu.SelectedServer(_session.IsNullFrame ? 1 : 0);
            }
            finally
            {
                _session.IsSwitching = false;

                if (_officialButton)
                {
                    _officialButton.interactable = true;
                }

                if (_nullFrameButton)
                {
                    _nullFrameButton.interactable = true;
                }
            }
        }

        private void ClearLobbyRoomLists()
        {
            if (!_menu || !_menu.LobbyObject) return;

            LobbyMenu lobby = _menu.LobbyObject.GetComponent<LobbyMenu>();
            if (!lobby) return;

            lobby.FAENEDGHCPN?.Clear();
            lobby.Rooms?.Clear();
        }

        private static void AfterShowLobby(MainMenu __instance)
        {
            _instance.ConfigureServerButtons(__instance);
        }
    }
}
