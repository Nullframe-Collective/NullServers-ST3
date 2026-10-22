using HarmonyLib;
using Il2Cpp;
using Il2CppFishNet.Connection;
using Il2CppFishNet.Managing;
using Il2CppFishNet.Managing.Logging;
using Il2CppFishNet.Managing.Scened;
using Il2CppFishNet.Managing.Server;
using Il2CppFishNet.Object;
using Il2CppFishNet.Transporting.Relay;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader;
using MelonLoader.Utils;
using NullServers.Network;
using NullServers.Players;
using NullServers.Room;
using System.Text;
using System.Text.Json.Nodes;
using UnityEngine;
using SceneManager = UnityEngine.SceneManagement.SceneManager;
using SceneUtility = UnityEngine.SceneManagement.SceneUtility;

namespace NullServers.Server
{
    internal sealed class ServerControl
    {
        private static ServerControl _instance;
        private readonly RelaySession _session;
        private readonly PlayerProfiles _profiles;
        private readonly RoundSync _roundSync;
        private readonly MelonLogger.Instance _logger;
        private readonly Dictionary<int, float> _lastKick = new();
        private readonly Dictionary<int, float> _lastSceneSync = new();
        private readonly HashSet<int> _readyClients = new();
        private readonly HashSet<int> _pendingSceneProbes = new();
        private readonly Queue<(string Nickname, string Action)> _protectionNotices = new();
        private readonly List<string> _maps = new();
        private readonly Dictionary<string, string> _modes = new();
        private string _pendingRoom;
        private string _pendingRevision;
        private string _pendingScene;
        private string _pendingData;
        private bool _acknowledged;
        private bool _loading;
        private RoomMultiplayerMenu _previousMenu;
        private int _previousSceneHandle;
        private float _deadline;
        private float _nextUpdate;

        internal BanList Bans { get; }
        internal ServerAccess Access { get; }
        internal HostTools Tools { get; }
        internal AntiCrash Protection { get; }

        internal IReadOnlyList<string> Maps => _maps;
        internal IReadOnlyDictionary<string, string> Modes => _modes;

        internal RelayRoomInfo Room => _session.Relay ? _session.Relay.CurrentRoom : null;
        internal NetworkManager Manager => NetBootstrap.GJDGDIGECJN ? NetBootstrap.GJDGDIGECJN.HLNKMGIBPIG : null;

        internal bool CanManage => 
            _session.IsNullFrame &&
            !_session.IsSwitching &&
            _session.Relay &&
            !_session.Relay.OfflineMode &&
            Room != null &&
            _session.Relay.IsRoomHost &&
            _session.Relay.LocalPlayerId == RelayProtocol.HostPlayerId &&
            Manager &&
            Manager.ServerManager.Started;

        internal bool Busy => _pendingRevision != null;
        internal string Status { get; private set; }

        internal ServerControl
        (
            HarmonyLib.Harmony harmony,
            RelaySession session,
            PlayerProfiles profiles,
            MelonLogger.Instance logger = null
        )
        {
            _instance = this;
            _session = session;
            _profiles = profiles;
            _logger = logger;
            _roundSync = new RoundSync(harmony, session, profiles, logger);

            profiles.RoundSignalReceived += OnRoundSignal;

            Bans = new BanList(Path.Combine(MelonEnvironment.UserDataDirectory, "NullFrame", "bans.json"));
            if (Bans.LoadError != null)
            {
                logger?.Warning(Bans.LoadError);
            }

            Access = new ServerAccess(harmony, this, session, profiles);
            Tools = new HostTools(harmony, this);
            Protection = new AntiCrash(harmony, this, logger);
            if (Access.Mutes.LoadError != null)
            {
                logger?.Warning(Access.Mutes.LoadError);
            }

            harmony.Patch(
                AccessTools.Method(typeof(LobbyMenu), nameof(LobbyMenu.Start)),
                postfix: new HarmonyMethod(typeof(ServerControl), nameof(CaptureCatalog)));
            harmony.Patch(
                AccessTools.Method(typeof(RelayTransport), nameof(RelayTransport.Relay_OnRoomUpdated)),
                postfix: new HarmonyMethod(typeof(ServerControl), nameof(AfterRoomUpdated)));
            harmony.Patch(
                AccessTools.PropertySetter(typeof(UnityEngine.UI.Text), nameof(UnityEngine.UI.Text.text)),
                prefix: new HarmonyMethod(typeof(ServerControl), nameof(BeforeCaptionText)));

            harmony.Patch(
                AccessTools.Method
                (
                    typeof(GUI),
                    nameof(GUI.Label),
                    [
                        typeof(Rect),
                        typeof(string),
                        typeof(GUIStyle)
                    ]
                ),
                prefix: new HarmonyMethod(typeof(ServerControl), nameof(BeforeRoomCaption)),
                finalizer: new HarmonyMethod(typeof(ServerControl), nameof(AfterRoomCaption)));
        }

        private static void CaptureCatalog(LobbyMenu __instance)
        {
            ServerControl instance = _instance;
            instance._maps.Clear();
            instance._modes.Clear();

            foreach (LobbyMenu.AllModes mode in __instance.MHDLCNKEEGN)
            {
                if (mode != null && !string.IsNullOrEmpty(mode.modeID) && mode.modeID != "THR")
                {
                    instance._modes[mode.modeID] = mode.modeName;
                }
            }

            HashSet<string> names = new(StringComparer.Ordinal);
            foreach (var list in new[]
            {
                __instance.IILJODEKLIC,
                __instance.EMDCLAPMCEB,
                __instance.CFJBONPPILK,
                __instance.KFFNCDCDCPI
            })
            {
                if (list == null) continue;

                foreach (LobbyMenu.AllMaps map in list)
                {
                    if (map == null) continue;

                    names.Add((string)map.mapName);
                }
            }

            for (int index = 0; index < SceneManager.sceneCountInBuildSettings; index++)
            {
                string scene = Path.GetFileNameWithoutExtension(SceneUtility.GetScenePathByBuildIndex(index));
                if 
                (
                    names.Contains(scene) ||
                    names.Any
                    (
                        name => scene == name + " (Day)" ||
                        scene == name + " (Dusk)" ||
                        scene == name + " (Night)"
                    )
               )
                {
                    instance._maps.Add(scene);
                }
            }

            instance._maps.Sort(StringComparer.Ordinal);
            instance._logger?.Msg($"Server settings catalog: {instance._maps.Count} maps, {instance._modes.Count} modes.");
        }

        internal string PlayerName(int id, RelayPlayerIdentity identity)
        {
            return _profiles.TryGet(id, out var profile)
            ? profile.Nickname : identity?.Username ?? $"Player {id}";
        }

        internal bool TryIdentity(int id, out RelayPlayerIdentity identity)
        {
            identity = null;
            return _session.Relay && _session.Relay.TryGetPlayerIdentity(id, out identity);
        }

        internal NetworkConnection[] Connections()
        {
            List<NetworkConnection> connections = new();
            if (CanManage)
            {
                foreach (NetworkConnection connection in Manager.ServerManager.Clients.Values)
                {
                    connections.Add(connection);
                }
            }

            return connections.ToArray();
        }

        internal bool Kick(int id, string expectedAccount, bool ban, out string error)
        {
            if (!CanManage)
            {
                return Access.Submit(
                    new ServerRequest(ban ? ServerAction.Ban : ServerAction.Kick, id, expectedAccount),
                    out error);
            }

            error = "This player is no longer available.";
            if 
            (
                !CanManage ||
                id == _session.Relay.LocalPlayerId ||
                !Manager.ServerManager.Clients.TryGetValue(id, out var connection) ||
                !connection.IsValid ||
                !TryIdentity(id, out var identity) ||
                identity.Username != expectedAccount
            )
            {
                return false;
            }

            string nickname = PlayerName(id, identity);
            if (ban && identity.IsGuest)
            {
                error = "Persistent bans require an authenticated account.";
                return false;
            }

            if (ban && !Bans.Add(identity.Username, nickname, out error))
            {
                return false;
            }

            Disconnect(connection, ban ? "Banned by host." : "Kicked by host.");
            Announce(nickname, ban ? "was banned by the host" : "was kicked by the host");
            Status = $"{nickname}: {(ban ? "banned" : "kicked")}.";
            error = null;

            return true;
        }

        internal void Announce(string nickname, string action)
        {
            RoomMultiplayerMenu menu = RoomMultiplayerMenu.GJDGDIGECJN;
            WhoKilledWho feed = menu ? menu.GetComponent(Il2CppInterop.Runtime.Il2CppType.Of<WhoKilledWho>())?.Cast<WhoKilledWho>() : null;
            if (CanManage && feed && feed.IsServerInitialized)
            {
                feed.networkAddMessage(nickname, "", action, "");
            }
        }

        internal bool Unban(string account, out string error)
        {
            if (!CanManage)
            {
                return Access.Submit(new ServerRequest(ServerAction.Unban, Account: account), out error);
            }

            error = "Only the current host can remove a ban.";
            if (!CanManage || !Bans.Remove(account, out error))
            {
                return false;
            }

            Status = $"Ban removed: {account}.";

            return true;
        }

        private void Disconnect(NetworkConnection connection, string reason)
        {
            _lastKick[connection.ClientId] = Time.unscaledTime;
            Manager.ServerManager.Kick(connection, KickReason.Unset, LoggingType.Common, reason);
        }

        internal bool RemoveAbusivePlayer(NetworkConnection connection, string account, string nickname, string reason)
        {
            if (!CanManage || connection.ClientId == 0)
            {
                return false;
            }

            bool banned = false;
            if (!string.IsNullOrEmpty(account))
            {
                banned = Bans.Add(account, nickname, out var error);
                if (!banned)
                {
                    _logger?.Warning("Anti-Crash could not persist the ban: " + error);
                }
            }

            string action = banned ? "banned" : "kicked";
            if 
            (
                Manager.ServerManager.Clients.TryGetValue(connection.ClientId, out var current) &&
                current.Pointer == connection.Pointer &&
                current.IsValid
            )
            {
                Disconnect(connection, "NullFrame Anti-Crash: " + reason + ".");
            }

            Status = $"{nickname} was {action} by NullFrame Anti-Crash.";
            _logger?.Warning($"Anti-Crash {action} client {connection.ClientId} ({account}): {reason}.");
            _protectionNotices.Enqueue((nickname, action));

            return true;
        }

        private void FlushProtectionNotices()
        {
            if (_protectionNotices.Count == 0) return;

            RoomMultiplayerMenu menu = RoomMultiplayerMenu.GJDGDIGECJN;
            WhoKilledWho feed = menu ? menu.GetComponent(Il2CppInterop.Runtime.Il2CppType.Of<WhoKilledWho>())?.Cast<WhoKilledWho>() : null;
            if (!menu || !menu.CJNLEDLNGID || !feed || !feed.IsServerInitialized) return;

            while (_protectionNotices.TryDequeue(out var notice))
            {
                feed.networkAddMessage(notice.Nickname, "", $"was {notice.Action} by NullFrame Anti-Crash", "");
            }
        }

        internal bool ApplyRoom(string name, int capacity, string color, out string error)
        {
            if (!CanManage)
            {
                return Access.Submit
                (
                    new ServerRequest(ServerAction.Room, Name: name, Capacity: capacity, Color: color),
                    out error
                );
            }

            error = "Wait for the current update to finish.";
            return CanManage && !Busy && RequestUpdate(name, capacity, color, new JsonObject(), null, out error);
        }

        internal bool RestartRound
        (
            string scene,
            string mode,
            int minutes,
            int custards,
            int difficulty,
            bool muteGuests,
            out string error
        )
        {
            if (!CanManage)
            {
                return Access.Submit
                (
                    new ServerRequest
                    (
                        ServerAction.Round,
                        Scene: scene,
                        Mode: mode,
                        Minutes: minutes,
                        Custards: custards,
                        Difficulty: difficulty,
                        MuteGuests: muteGuests
                    ),
                    out error
                );
            }

            error = "Only the current host can restart the round.";
            if (!CanManage || Busy) return false;

            if (!_maps.Contains(scene) || !_modes.ContainsKey(mode) || !Application.CanStreamedLevelBeLoaded(scene))
            {
                error = "Choose an available map and game mode.";

                return false;
            }

            if (minutes < 1 || minutes > 60 || custards < 1 || custards > 200 || difficulty < 0 || difficulty > 3)
            {
                error = "Check the round duration, custards and difficulty.";

                return false;
            }

            JsonObject changes = new()
            {
                ["MapName"] = scene,
                ["GameMode"] = mode,
                ["RoundDuration"] = minutes * 60,
                ["Custards"] = custards,
                ["Difficulty"] = difficulty,
                ["MuteGuests"] = muteGuests,
                ["IsOpen"] = false
            };

            _session.HoldRoomClosed = true;

            if (RequestUpdate
            (
                RoomName.ReadDisplay(Room.RoomData, Room.Name),
                Room.MaxPlayers,
                RoomOptions.ReadNameColor(Room.RoomData) ?? "#FFFFFF",
                changes,
                scene,
                out error
            ))
            {
                return true;
            }

            _session.HoldRoomClosed = false;

            return false;
        }

        internal bool Reopen(out string error)
        {
            if (!CanManage)
            {
                return Access.Submit(new ServerRequest(ServerAction.Reopen), out error);
            }

            error = "Only the current host can reopen the room.";

            return 
                CanManage &&
                !Busy &&
                RequestUpdate
                (
                    RoomName.ReadDisplay(Room.RoomData, Room.Name),
                    Room.MaxPlayers,
                    RoomOptions.ReadNameColor(Room.RoomData) ?? "#FFFFFF",
                    new JsonObject
                    {
                    ["IsOpen"] = true
                    },
                    null,
                    out error
                );
        }

        private bool RequestUpdate(string name, int capacity, string color, JsonObject changes, string scene, out string error)
        {
            string revision = Guid.NewGuid().ToString("N");
            changes["SettingsRevision"] = revision;

            _pendingRoom = Room.Id;
            _pendingRevision = revision;
            _pendingScene = scene;
            _acknowledged = _loading = false;
            _deadline = Time.unscaledTime + 15f;

            if (_session.UpdateRoom(name, capacity, color, changes, out error))
            {
                Status = "Waiting for the relay to confirm the settings...";

                return true;
            }

            ClearPending();

            return false;
        }

        private static void AfterRoomUpdated(RelayRoomInfo info)
        {
            ServerControl instance = _instance;
            if (info.Id != instance._pendingRoom || instance._pendingRevision == null)
            {
                return;
            }

            if (JsonNode.Parse(info.RoomData)?["SettingsRevision"]?.GetValue<string>() == instance._pendingRevision)
            {
                instance._pendingData = info.RoomData;
                instance._acknowledged = true;
            }
        }

        internal void Update()
        {
            Protection.Update();
            if (Time.unscaledTime < _nextUpdate) return;

            _nextUpdate = Time.unscaledTime + 0.25f;
            _roundSync.Update();
            Access.Update();

            if (!CanManage)
            {
                _protectionNotices.Clear();
                if (_loading)
                {
                    _roundSync.CancelSpawns();
                }

                ClearPending();
                _lastKick.Clear();

                return;
            }

            foreach (NetworkConnection connection in Connections())
            {
                if 
                (
                    connection.ClientId != _session.Relay.LocalPlayerId &&
                    TryIdentity(connection.ClientId, out var identity) &&
                    !identity.IsGuest &&
                    Bans.Contains(identity.Username) &&
                    (!_lastKick.TryGetValue(connection.ClientId, out var last) || Time.unscaledTime - last >= 2f)
                )
                {
                    Disconnect(connection, "This account is banned by the host.");
                }
            }

            foreach (int id in _lastKick.Keys.ToArray())
            {
                if (!Manager.ServerManager.Clients.ContainsKey(id))
                {
                    _lastKick.Remove(id);
                }
            }

            if (!Busy)
            {
                FlushProtectionNotices();

                return;
            }

            if (Room.Id != _pendingRoom)
            {
                if (_loading)
                {
                    _roundSync.CancelSpawns();
                }

                ClearPending();

                return;
            }

            if (Time.unscaledTime > _deadline)
            {
                Status = _loading ? "Loading timed out. Check players, then reopen the room or retry the round." :
                    "The relay did not confirm the update. Check the current room settings before retrying.";
                _logger?.Warning(Status);

                if (_loading)
                {
                    _roundSync.CancelSpawns();
                }

                ClearPending();

                return;
            }

            if (!_acknowledged) return;

            if (_pendingScene == null)
            {
                Status = "Room settings updated.";
                ClearPending();
            }
            else if (!_loading)
            {
                LoadRound();
            }
            else if 
            (
                !Manager.SceneManager.IteratingQueue &&
                SceneManager.GetActiveScene().name == _pendingScene &&
                SceneManager.GetActiveScene().handle != _previousSceneHandle &&
                RoomMultiplayerMenu.GJDGDIGECJN &&
                RoomMultiplayerMenu.GJDGDIGECJN != _previousMenu &&
                RoomMultiplayerMenu.GJDGDIGECJN.IsServerInitialized &&
                RoomMultiplayerMenu.GJDGDIGECJN.CJNLEDLNGID &&
                Connections().All(connection =>
                !connection.IsAuthenticated ||
                (connection.Scenes.Contains(SceneManager.GetSceneByName(_pendingScene)) &&
                (connection.ClientId == _session.Relay.LocalPlayerId ||
                _readyClients.Contains(connection.ClientId))))
            )
            {
                _logger?.Msg($"Round ready: {_pendingScene}; {RoomProperties.Current.GameMode}; {Connections().Length} players.");
                _profiles.Synchronize();

                NetworkObject networkObject = RoomMultiplayerMenu.GJDGDIGECJN.NetworkObject;
                RoundSignal signal = new
                (
                    1,
                    _pendingRoom,
                    _pendingRevision,
                    _pendingScene,
                    true,
                    networkObject.ObjectId,
                    sceneId: networkObject.SceneId
                );

                foreach (NetworkConnection connection in Connections())
                {
                    if (connection.IsAuthenticated && connection.ClientId != _session.Relay.LocalPlayerId)
                    {
                        _profiles.SendRoundSignal(signal, connection);
                    }
                }

                _roundSync.ReleaseSpawns();
                _session.HoldRoomClosed = false;
                ClearPending();
                Reopen(out var error);

                if (error != null)
                {
                    Status = error;
                }
            }
            else if (_loading && !Manager.SceneManager.IteratingQueue && SceneManager.GetActiveScene().name == _pendingScene)
            {
                string waiting = string.Join
                (
                    ", ",
                    Connections()
                    .Where(connection => connection.IsAuthenticated &&
                    connection.ClientId != _session.Relay.LocalPlayerId &&
                    !_readyClients.Contains(connection.ClientId))
                    .Select(connection => TryIdentity(connection.ClientId, out var identity) ? 
                    PlayerName(connection.ClientId, identity) : $"Player {connection.ClientId}")
                );
                Status = waiting.Length == 0 ? "Initializing the room..." : "Waiting for player initialization: " + waiting;
            }
        }

        private void OnRoundSignal(NetworkConnection connection, RoundSignal signal)
        {
            if 
            (
                !CanManage ||
                !_loading ||
                signal.RoomId != _pendingRoom ||
                signal.Revision != _pendingRevision ||
                signal.Scene != _pendingScene ||
                !connection.IsValid ||
                !connection.IsAuthenticated ||
                connection.ClientId == _session.Relay.LocalPlayerId ||
                !connection.Scenes.Contains(SceneManager.GetSceneByName(_pendingScene))
            )
            {
                return;
            }

            RoomMultiplayerMenu menu = RoomMultiplayerMenu.GJDGDIGECJN;
            if
            (
                !menu ||
                menu == _previousMenu ||
                !menu.IsServerInitialized ||
                menu.gameObject.scene.name != _pendingScene ||
                Manager.SceneManager.IteratingQueue
            )
            {
                return;
            }

            if (signal.Ready && signal.ObjectId == menu.NetworkObject.ObjectId)
            {
                _pendingSceneProbes.Remove(connection.ClientId);
                if (_readyClients.Add(connection.ClientId))
                {
                    _logger?.Msg($"Client {connection.ClientId} confirmed round initialization: {_pendingScene}; room object {signal.ObjectId}.");
                }

                return;
            }

            NetworkObject networkObject = menu.NetworkObject;
            if (signal.Missing)
            {
                if 
                (
                    signal.ObjectId != networkObject.ObjectId ||
                    signal.SceneId != networkObject.SceneId ||
                    !_pendingSceneProbes.Remove(connection.ClientId)
                )
                {
                    return;
                }

                // Rebuild alone returns Unchanged for existing observers and does not resend the missing spawn.
                bool wasObserver = networkObject.RemoveObserver(connection);
                Manager.ServerManager.Objects.RebuildObservers(networkObject, connection, false);
                bool observing = networkObject.Observers.Contains(connection);
                string details = $"client {connection.ClientId}; object {networkObject.ObjectId}; sceneId {networkObject.SceneId}; " +
                    $"observerBefore={wasObserver}; observerAfter={observing}.";

                if (observing)
                {
                    _logger?.Msg("Room spawn queued again: " + details);
                }
                else
                {
                    _logger?.Warning("Room spawn still blocked by observer state: " + details);
                }

                return;
            }

            if (_lastSceneSync.TryGetValue(connection.ClientId, out var last) && Time.unscaledTime - last < 2f)
            {
                return;
            }

            _lastSceneSync[connection.ClientId] = Time.unscaledTime;
            Manager.ServerManager.Objects.RebuildObservers(connection, false);
            if (signal.ObjectId < 0)
            {
                if (!_pendingSceneProbes.Contains(connection.ClientId))
                {
                    _logger?.Msg($"Room spawn probe for client {connection.ClientId}: object {networkObject.ObjectId}; " +
                        $"sceneId {networkObject.SceneId}; observer={networkObject.Observers.Contains(connection)}.");
                }

                _pendingSceneProbes.Add(connection.ClientId);
                _profiles.SendRoundSignal
                (
                    new RoundSignal
                    (
                        1,
                        _pendingRoom,
                        _pendingRevision,
                        _pendingScene,
                        false,
                        networkObject.ObjectId,
                        sceneId: networkObject.SceneId
                    ),
                    connection
                );
            }
        }

        private void LoadRound()
        {
            RoundSync.ApplyProperties(_pendingData);
            _readyClients.Clear();
            _lastSceneSync.Clear();
            _pendingSceneProbes.Clear();

            PlayerInfo[] players = UnityEngine.Object.FindObjectsOfType<PlayerInfo>().Where(player => player.IsServerInitialized).ToArray();
            foreach (PlayerInfo player in players)
            {
                player.EIDHGMOHFPI("Spectators");
                player.LEEDMMPNCJB();
            }

            SceneLoadData load = new(_pendingScene)
            {
                ReplaceScenes = ReplaceOption.All
            };

            load.MovedNetworkObjects = new Il2CppReferenceArray<NetworkObject>(players.Select(player => player.NetworkObject)
                .Where(networkObject => !networkObject.IsGlobal).ToArray());
            load.Params.ClientParams = new Il2CppStructArray<byte>(Encoding.UTF8.GetBytes("NFROUND1|" + _pendingRoom + "|" + _pendingData));

            // Player Awake looks up the active room; all clients must initialize it before any avatar is created.
            _roundSync.HoldSpawns();
            _loading = true;
            _previousMenu = RoomMultiplayerMenu.GJDGDIGECJN;
            _previousSceneHandle = SceneManager.GetActiveScene().handle;
            _deadline = Time.unscaledTime + 120f;

            Status = "Loading the new round for all players...";
            _logger?.Msg($"Round transition: {_pendingScene}; {RoomProperties.Current.GameMode}.");

            try
            {
                UnityEngine.SceneManagement.Scene current = SceneManager.GetSceneByName(_pendingScene);
                if (current.IsValid() && current.isLoaded)
                {
                    // This FishNet build skips an already loaded global scene, even with ReloadScenes set.
                    RoundSync.PreservePlayers(Manager);
                    Manager.SceneManager.GetFallbackActiveScene();
                    SceneUnloadData unload = new(current);
                    unload.Params.ClientParams = load.Params.ClientParams;
                    Manager.SceneManager.UnloadGlobalScenes(unload);
                }

                Manager.SceneManager.LoadGlobalScenes(load);
            }
            catch (Il2CppInterop.Runtime.Il2CppException exception)
            {
                Status = "The round could not be loaded. Reopen the room or retry.";
                _logger?.Error(exception);
                _roundSync.CancelSpawns();
                ClearPending();
            }
        }

        private void ClearPending()
        {
            _pendingRoom = _pendingRevision = _pendingScene = _pendingData = null;
            _acknowledged = _loading = false;
            _previousMenu = null;
            _previousSceneHandle = 0;
            _readyClients.Clear();
            _lastSceneSync.Clear();
            _pendingSceneProbes.Clear();
            _session.HoldRoomClosed = false;
        }

        private static void BeforeRoomCaption(ref string __1, GUIStyle __2, out bool __state)
        {
            __state = false;
            ServerControl instance = _instance;
            RelayRoomInfo room = instance.Room;

            if (!instance._session.IsNullFrame || room == null || __1 == null) return;

            if 
            (
                __1.StartsWith(room.Name, StringComparison.Ordinal) &&
                __1.Contains("Room ID: " + room.Id, StringComparison.Ordinal)
            )
            {
                __1 = RoomName.Render(room.Name, room.RoomData) + __1[room.Name.Length..];
                if (__2 != null && !__2.richText)
                {
                    __state = true;
                    __2.richText = true;
                }
            }
        }

        private static void BeforeCaptionText(UnityEngine.UI.Text __instance, ref string __0)
        {
            ServerControl instance = _instance;
            RelayRoomInfo room = instance.Room;

            if 
            (
                instance._session.IsNullFrame &&
                room != null &&
                __0 != null &&
                __0.StartsWith(room.Name, StringComparison.Ordinal) &&
                __0.Contains("Room ID: " + room.Id, StringComparison.Ordinal)
            )
            {
                __0 = RoomName.Render(room.Name, room.RoomData) + __0[room.Name.Length..];
                __instance.supportRichText = true;
            }
        }

        private static void AfterRoomCaption(GUIStyle __2, bool __state)
        {
            if (__state)
            {
                __2.richText = false;
            }
        }
    }
}
