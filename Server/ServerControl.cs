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
        internal FrameworkCompatibility Compatibility => _profiles.Compatibility;
        internal HostTools Tools { get; }
        internal AntiCrash Protection { get; }
        internal RoomEnvironment Rendering { get; } = new();

        internal IReadOnlyList<string> Maps => _maps;
        internal IReadOnlyDictionary<string, string> Modes => _modes;

        internal RelayRoomInfo Room => _session.Relay ? _session.Relay.CurrentRoom : null;
        internal NetworkManager Manager => NetBootstrap.Current ? NetBootstrap.Current.Network : null;

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

        internal ServerControl(RelaySession session, PlayerProfiles profiles, MelonLogger.Instance logger)
        {
            _instance = this;
            _session = session;
            _profiles = profiles;
            _logger = logger;
            _roundSync = new RoundSync(session, profiles, logger);

            profiles.RoundSignalReceived += OnRoundSignal;

            Bans = new BanList(Path.Combine(MelonEnvironment.UserDataDirectory, "NullFrame", "bans.json"));
            if (Bans.LoadError != null)
            {
                logger.Warning(Bans.LoadError);
            }

            Access = new ServerAccess(this, session, profiles);
            Tools = new HostTools(this);
            Protection = new AntiCrash(this, logger);

            if (Access.Mutes.LoadError != null)
            {
                logger.Warning(Access.Mutes.LoadError);
            }
        }

        internal static void CaptureCatalog(LobbyMenu __instance)
        {
            ServerControl instance = _instance;
            instance._maps.Clear();
            instance._modes.Clear();

            foreach (LobbyMenu.AllModes mode in __instance.Modes)
            {
                if (mode != null && !string.IsNullOrEmpty(mode.modeID) && mode.modeID != "THR")
                {
                    instance._modes[mode.modeID] = mode.modeName;
                }
            }

            HashSet<string> names = __instance.MapEntries.Where(map => map != null).Select(map => (string)map.mapName).ToHashSet(StringComparer.Ordinal);

            for (int index = 0; index < SceneManager.sceneCountInBuildSettings; index++)
            {
                string scene = Path.GetFileNameWithoutExtension(SceneUtility.GetScenePathByBuildIndex(index));
                if (names.Contains(scene) || names.Any(name => scene == name + " (Day)" || scene == name + " (Dusk)" || scene == name + " (Night)"))
                {
                    instance._maps.Add(scene);
                }
            }

            instance._maps.AddRange(MapRooms.Scenes.Where(scene => !instance._maps.Contains(scene)));
            instance._maps.Sort(StringComparer.Ordinal);
            instance._logger.Debug($"Server settings catalog: {instance._maps.Count} maps, {instance._modes.Count} modes.");
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
            if (id == _session.Relay.LocalPlayerId ||
                !Manager.ServerManager.Clients.TryGetValue(id, out var connection) ||
                !connection.IsValid ||
                !TryIdentity(id, out var identity) ||
                identity.Username != expectedAccount)
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

        internal static void AfterMapViewRestored() => _instance.Rendering.Reapply();

        internal static void AfterNightVisionEnabled(ToggleNightVision __instance) => _instance.Rendering.NightVision(__instance);

        private static WhoKilledWho KillFeed()
        {
            RoomMultiplayerMenu menu = RoomMultiplayerMenu.Current;
            WhoKilledWho feed = menu && menu.IsInitialized ? menu.GetComponent(Il2CppInterop.Runtime.Il2CppType.Of<WhoKilledWho>())?.Cast<WhoKilledWho>() : null;
            return feed && feed.IsServerInitialized ? feed : null;
        }

        internal void Announce(string nickname, string action)
        {
            WhoKilledWho feed = KillFeed();
            if (CanManage && feed)
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

            if (!Bans.Remove(account, out error))
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
                    _logger.Warning("Anti-Crash could not persist the ban: " + error);
                }
            }

            string action = banned ? "banned" : "kicked";
            if (Manager.ServerManager.Clients.TryGetValue(connection.ClientId, out var current) &&
                current.Pointer == connection.Pointer &&
                current.IsValid)
            {
                Disconnect(connection, "NullFrame Anti-Crash: " + reason + ".");
            }

            Status = $"{nickname} was {action} by NullFrame Anti-Crash.";
            _logger.Warning($"Anti-Crash {action} client {connection.ClientId} ({account}): {reason}.");
            _protectionNotices.Enqueue((nickname, action));

            return true;
        }

        private void FlushProtectionNotices()
        {
            if (_protectionNotices.Count == 0) return;

            WhoKilledWho feed = KillFeed();
            if (!feed) return;

            while (_protectionNotices.TryDequeue(out var notice))
            {
                feed.networkAddMessage(notice.Nickname, "", $"was {notice.Action} by NullFrame Anti-Crash", "");
            }
        }

        internal bool ApplyRoom(string name, int capacity, string color, out string error)
        {
            if (!CanManage)
            {
                return Access.Submit(
                    new ServerRequest(ServerAction.Room, Name: name, Capacity: capacity, Color: color),
                    out error);
            }

            error = "Wait for the current update to finish.";
            return !Busy && RequestUpdate(name, capacity, color, new JsonObject(), null, out error);
        }

        internal bool RestartRound(
            string scene,
            string mode,
            int minutes,
            int custards,
            int difficulty,
            bool muteGuests,
            out string error)
        {
            if (!CanManage)
            {
                return Access.Submit(
                    new ServerRequest(
                        ServerAction.Round,
                        Scene: scene,
                        Mode: mode,
                        Minutes: minutes,
                        Custards: custards,
                        Difficulty: difficulty,
                        MuteGuests: muteGuests),
                    out error);
            }

            error = "Wait for the current update to finish.";
            if (Busy) return false;

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

            if (RequestUpdate(changes, scene, out error))
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

            error = "Wait for the current update to finish.";
            return !Busy && RequestUpdate(new JsonObject { ["IsOpen"] = true }, null, out error);
        }

        private bool RequestUpdate(JsonObject changes, string scene, out string error) => RequestUpdate(
            RoomName.ReadDisplay(Room.RoomData, Room.Name),
            Room.MaxPlayers,
            RoomOptions.ReadNameColor(Room.RoomData) ?? "#FFFFFF",
            changes,
            scene,
            out error);

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

        internal static void AfterRoomUpdated(RelayRoomInfo info)
        {
            ServerControl instance = _instance;
            if (info.Id != instance._pendingRoom || instance._pendingRevision == null)
            {
                return;
            }

            if (RoomData.TryParse(info.RoomData, out JsonObject properties) &&
                RoomData.Text(properties, "SettingsRevision") == instance._pendingRevision &&
                (instance._pendingScene == null || RoomData.Text(properties, "MapName") == instance._pendingScene))
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
                CancelPending();
                _lastKick.Clear();
                return;
            }

            KickBannedPlayers();

            if (!Busy)
            {
                FlushProtectionNotices();
                return;
            }

            if (Room.Id != _pendingRoom)
            {
                CancelPending();
                return;
            }

            if (Time.unscaledTime > _deadline)
            {
                Status = _loading ? "Loading timed out. Check players, then reopen the room or retry the round." :
                    "The relay did not confirm the update. Check the current room settings before retrying.";
                _logger.Warning(Status);
                CancelPending();
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
            else if (RoundReady())
            {
                StartRound();
            }
            else if (!Manager.SceneManager.IteratingQueue && SceneManager.GetActiveScene().name == _pendingScene)
            {
                string waiting = WaitingPlayers();
                Status = waiting.Length == 0 ? "Initializing the room..." : "Waiting for player initialization: " + waiting;
            }
        }

        private void CancelPending()
        {
            if (_loading)
            {
                _roundSync.CancelSpawns();
            }

            ClearPending();
        }

        // Banned accounts and accounts on the community blacklist are removed, at most once every 2 seconds per player.
        private void KickBannedPlayers()
        {
            foreach (NetworkConnection connection in Connections())
            {
                if (connection.ClientId == _session.Relay.LocalPlayerId) continue;
                if (!TryIdentity(connection.ClientId, out var identity)) continue;

                bool blacklisted = connection.IsAuthenticated && _session.Blacklist.Contains(identity.Username);
                bool banned = !identity.IsGuest && Bans.Contains(identity.Username);
                if (!blacklisted && !banned) continue;
                if (_lastKick.TryGetValue(connection.ClientId, out float last) && Time.unscaledTime - last < 2f) continue;

                if (blacklisted)
                {
                    _profiles.SendServerMessage(new ServerMessage(1, Room.Id, Notice: GlobalBlacklist.BannedMessage), connection);
                    _logger.Warning("Community blacklist rejected authenticated account " + identity.Username + ".");
                }

                Disconnect(connection, blacklisted ? GlobalBlacklist.BannedMessage : "This account is banned by the host.");
            }

            foreach (int id in _lastKick.Keys.ToArray())
            {
                if (!Manager.ServerManager.Clients.ContainsKey(id))
                {
                    _lastKick.Remove(id);
                }
            }
        }

        // The new round starts once its scene is loaded on the host and every player reported that they are ready.
        private bool RoundReady()
        {
            if (Manager.SceneManager.IteratingQueue) return false;

            UnityEngine.SceneManagement.Scene active = SceneManager.GetActiveScene();
            if (active.name != _pendingScene || active.handle == _previousSceneHandle) return false;

            RoomMultiplayerMenu menu = RoomMultiplayerMenu.Current;
            if (!menu || menu == _previousMenu || !menu.IsServerInitialized || !menu.IsInitialized) return false;

            UnityEngine.SceneManagement.Scene pending = SceneManager.GetSceneByName(_pendingScene);
            foreach (NetworkConnection connection in Connections())
            {
                if (!connection.IsAuthenticated) continue;
                if (!connection.Scenes.Contains(pending)) return false;
                if (connection.ClientId != _session.Relay.LocalPlayerId && !_readyClients.Contains(connection.ClientId)) return false;
            }

            return true;
        }

        private void StartRound()
        {
            _logger.Debug($"Round ready: {_pendingScene}; {RoomProperties.Current.GameMode}; {Connections().Length} players.");
            _profiles.Synchronize();

            NetworkObject networkObject = RoomMultiplayerMenu.Current.NetworkObject;
            RoundSignal signal = new(1, _pendingRoom, _pendingRevision, _pendingScene, true, networkObject.ObjectId, sceneId: networkObject.SceneId);

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
            Reopen(out string error);

            if (error != null)
            {
                Status = error;
            }
        }

        private string WaitingPlayers()
        {
            List<string> names = new();
            foreach (NetworkConnection connection in Connections())
            {
                if (!connection.IsAuthenticated || connection.ClientId == _session.Relay.LocalPlayerId) continue;
                if (_readyClients.Contains(connection.ClientId)) continue;

                names.Add(TryIdentity(connection.ClientId, out var identity) ? PlayerName(connection.ClientId, identity) : $"Player {connection.ClientId}");
            }

            return string.Join(", ", names);
        }

        // While a new round loads, clients report their room object: ready, missing, or not spawned yet.
        private void OnRoundSignal(NetworkConnection connection, RoundSignal signal)
        {
            if (!IsCurrentRoundSignal(connection, signal)) return;

            RoomMultiplayerMenu menu = RoomMultiplayerMenu.Current;
            if (!menu || menu == _previousMenu || !menu.IsServerInitialized) return;
            if (menu.gameObject.scene.name != _pendingScene || Manager.SceneManager.IteratingQueue) return;

            NetworkObject networkObject = menu.NetworkObject;
            if (signal.Ready && signal.ObjectId == networkObject.ObjectId)
            {
                _pendingSceneProbes.Remove(connection.ClientId);
                if (_readyClients.Add(connection.ClientId))
                {
                    _logger.Debug($"Client {connection.ClientId} confirmed round initialization: {_pendingScene}; room object {signal.ObjectId}.");
                }

                return;
            }

            if (signal.Missing)
            {
                RespawnRoomObject(connection, signal, networkObject);
                return;
            }

            if (_lastSceneSync.TryGetValue(connection.ClientId, out float last) && Time.unscaledTime - last < 2f) return;

            _lastSceneSync[connection.ClientId] = Time.unscaledTime;
            Manager.ServerManager.Objects.RebuildObservers(connection, false);

            if (signal.ObjectId < 0)
            {
                ProbeRoomObject(connection, networkObject);
            }
        }

        private bool IsCurrentRoundSignal(NetworkConnection connection, RoundSignal signal)
        {
            if (!CanManage || !_loading) return false;
            if (signal.RoomId != _pendingRoom || signal.Revision != _pendingRevision || signal.Scene != _pendingScene) return false;
            if (!connection.IsValid || !connection.IsAuthenticated || connection.ClientId == _session.Relay.LocalPlayerId) return false;

            return connection.Scenes.Contains(SceneManager.GetSceneByName(_pendingScene));
        }

        // The client asked again for a room object it never received, so its observer entry is rebuilt.
        private void RespawnRoomObject(NetworkConnection connection, RoundSignal signal, NetworkObject networkObject)
        {
            if (signal.ObjectId != networkObject.ObjectId || signal.SceneId != networkObject.SceneId) return;
            if (!_pendingSceneProbes.Remove(connection.ClientId)) return;

            bool wasObserver = networkObject.RemoveObserver(connection);
            Manager.ServerManager.Objects.RebuildObservers(networkObject, connection, false);
            bool observing = networkObject.Observers.Contains(connection);
            string details = $"client {connection.ClientId}; object {networkObject.ObjectId}; sceneId {networkObject.SceneId}; " +
                $"observerBefore={wasObserver}; observerAfter={observing}.";

            if (observing)
            {
                _logger.Debug("Room spawn queued again: " + details);
            }
            else
            {
                _logger.Debug("Room spawn still blocked by observer state: " + details);
            }
        }

        // Tells a client which room object to expect, so it can ask for it if it does not arrive.
        private void ProbeRoomObject(NetworkConnection connection, NetworkObject networkObject)
        {
            if (!_pendingSceneProbes.Contains(connection.ClientId))
            {
                _logger.Debug($"Room spawn probe for client {connection.ClientId}: object {networkObject.ObjectId}; " +
                    $"sceneId {networkObject.SceneId}; observer={networkObject.Observers.Contains(connection)}.");
            }

            _pendingSceneProbes.Add(connection.ClientId);
            RoundSignal probe = new(1, _pendingRoom, _pendingRevision, _pendingScene, false, networkObject.ObjectId, sceneId: networkObject.SceneId);
            _profiles.SendRoundSignal(probe, connection);
        }

        private void LoadRound()
        {
            SurvivalGuard.Reset();
            RoundSync.ApplyProperties(_pendingData);
            _readyClients.Clear();
            _lastSceneSync.Clear();
            _pendingSceneProbes.Clear();

            PlayerInfo[] players = UnityEngine.Object.FindObjectsOfType<PlayerInfo>().Where(player => player.IsServerInitialized).ToArray();
            foreach (PlayerInfo player in players)
            {
                player.SetTeam("Spectators");
                player.ResetScore();
            }

            SceneLoadData load = new(_pendingScene)
            {
                ReplaceScenes = ReplaceOption.All
            };

            load.MovedNetworkObjects = new Il2CppReferenceArray<NetworkObject>(players.Select(player => player.NetworkObject)
                .Where(networkObject => !networkObject.IsGlobal).ToArray());
            load.Params.ClientParams = new Il2CppStructArray<byte>(Encoding.UTF8.GetBytes("NFROUND1|" + _pendingRoom + "|" + _pendingData));

            _roundSync.HoldSpawns();
            _loading = true;
            _previousMenu = RoomMultiplayerMenu.Current;
            _previousSceneHandle = SceneManager.GetActiveScene().handle;
            _deadline = Time.unscaledTime + 120f;

            Status = "Loading the new round for all players...";
            _logger.Debug($"Round transition: {_pendingScene}; {RoomProperties.Current.GameMode}.");

            try
            {
                UnityEngine.SceneManagement.Scene current = SceneManager.GetSceneByName(_pendingScene);
                if (current.IsValid() && current.isLoaded)
                {
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
                _logger.Error(exception);
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

        private static bool TryRenderCaption(string text, out string rendered)
        {
            rendered = null;
            if (text == null || !text.Contains("Room ID: ", StringComparison.Ordinal) || !_instance._session.IsNullFrame)
            {
                return false;
            }

            RelayRoomInfo room = _instance.Room;
            if (room == null) return false;

            string name = room.Name;
            if (!text.StartsWith(name, StringComparison.Ordinal) || !text.Contains("Room ID: " + room.Id, StringComparison.Ordinal))
            {
                return false;
            }

            rendered = RoomName.Render(name, room.RoomData) + text[name.Length..];
            return true;
        }

        internal static void BeforeRoomCaption(ref string __1, GUIStyle __2, out bool __state)
        {
            __state = false;
            if (!TryRenderCaption(__1, out var rendered)) return;

            __1 = rendered;
            if (__2 != null && !__2.richText)
            {
                __state = true;
                __2.richText = true;
            }
        }

        internal static void BeforeCaptionText(UnityEngine.UI.Text __instance, ref string __0)
        {
            if (!TryRenderCaption(__0, out var rendered)) return;

            __0 = rendered;
            __instance.supportRichText = true;
        }

        internal static void AfterRoomCaption(GUIStyle __2, bool __state)
        {
            if (__state)
            {
                __2.richText = false;
            }
        }
    }
}
