using HarmonyLib;
using Il2Cpp;
using Il2CppFishNet.Managing;
using Il2CppFishNet.Managing.Scened;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader;
using NullServers.Network;
using NullServers.Players;
using System.Text;
using System.Text.Json;
using UnityEngine;
using NetworkSceneManager = Il2CppFishNet.Managing.Scened.SceneManager;
using SceneManager = UnityEngine.SceneManagement.SceneManager;

namespace NullServers.Room
{
    internal sealed class RoundSync
    {
        private static RoundSync _instance;
        private readonly RelaySession _session;
        private readonly PlayerProfiles _profiles;
        private readonly MelonLogger.Instance _logger;
        private string _roomId;
        private string _revision;
        private string _scene;
        private int _previousSceneHandle;
        private RoomMultiplayerMenu _deferredStart;
        private bool _readySent;
        private float _deadline;
        private float _nextRequest;
        private int _probeObjectId = -1;
        private ulong _probeSceneId;
        private int _probeFrame;
        private bool _reportedSceneMismatch;
        private bool _holdSpawns;
        private bool _roundRejected;
        private RoomMultiplayerMenu _queuedSpawnMenu;
        private string _queuedSpawnTeam;

        internal RoundSync(HarmonyLib.Harmony harmony, RelaySession session, PlayerProfiles profiles, MelonLogger.Instance logger)
        {
            _instance = this;
            _session = session;
            _profiles = profiles;
            _logger = logger;

            profiles.RoundSignalReceivedFromServer += OnServerSignal;

            harmony.Patch(
                AccessTools.Method(
                    typeof(NetworkSceneManager),
                    nameof(NetworkSceneManager.QueueOperation),
                    [typeof(LoadQueueData)]),
                prefix: new HarmonyMethod(typeof(RoundSync), nameof(BeforeSceneLoad)));

            harmony.Patch(
                AccessTools.Method(
                    typeof(DefaultSceneProcessor),
                    nameof(DefaultSceneProcessor.UnloadStart),
                    [typeof(UnloadQueueData)]),
                prefix: new HarmonyMethod(typeof(RoundSync), nameof(BeforeSceneUnload)));

            harmony.Patch(
                AccessTools.Method(typeof(RoomMultiplayerMenu), nameof(RoomMultiplayerMenu.Start)),
                prefix: new HarmonyMethod(typeof(RoundSync), nameof(BeforeRoomStart)));

            harmony.Patch(
                AccessTools.Method(typeof(RoomMultiplayerMenu), nameof(RoomMultiplayerMenu.SpawnPlayer)),
                prefix: new HarmonyMethod(typeof(RoundSync), nameof(BeforePlayerSpawn)));
        }

        private static bool BeforeSceneLoad(LoadQueueData data)
        {
            RoundSync instance = _instance;
            NetBootstrap bootstrap = NetBootstrap.Current;

            if (data == null ||
                data.AsServer ||
                !instance._session.IsNullFrame ||
                instance._session.IsSwitching ||
                !bootstrap ||
                bootstrap.Network.ServerManager.Started ||
                (instance._revision == null && !RoomMultiplayerMenu.Current))
            {
                return true;
            }

            instance.Begin(data.SceneLoadData?.Params?.ClientParams);
            return !instance._roundRejected;
        }

        private static void BeforeSceneUnload(UnloadQueueData queueData)
        {
            if (!_instance.Begin(queueData?.SceneUnloadData?.Params?.ClientParams))
            {
                return;
            }

            NetworkManager manager = NetBootstrap.Current.Network;
            PreservePlayers(manager);
            manager.SceneManager.GetFallbackActiveScene();
        }

        private bool Begin(Il2CppStructArray<byte> data)
        {
            _roundRejected = false;
            NetBootstrap bootstrap = NetBootstrap.Current;

            if (!_session.IsNullFrame ||
                _session.IsSwitching ||
                !_session.Relay ||
                _session.Relay.CurrentRoom == null ||
                !bootstrap ||
                data == null ||
                data.Length > 2048)
            {
                return false;
            }

            string[] parts = Encoding.UTF8.GetString(data.ToArray()).Split('|', 3);
            if (parts.Length != 3 || parts[0] != "NFROUND1" || parts[1] != _session.Relay.CurrentRoom.Id)
            {
                return false;
            }

            string revision, scene;

            try
            {
                using var document = JsonDocument.Parse(parts[2]);
                JsonElement properties = document.RootElement;
                if (properties.ValueKind != JsonValueKind.Object ||
                    !properties.TryGetProperty("Version", out var version) ||
                    version.ValueKind != JsonValueKind.String ||
                    version.GetString() != RelaySession.NullFrameGameVersion ||
                    !properties.TryGetProperty("SettingsRevision", out var revisionValue) ||
                    revisionValue.ValueKind != JsonValueKind.String ||
                    !Guid.TryParseExact(revision = revisionValue.GetString(), "N", out _) ||
                    !properties.TryGetProperty("MapName", out var map) ||
                    map.ValueKind != JsonValueKind.String ||
                    string.IsNullOrEmpty(scene = map.GetString()))
                {
                    return false;
                }
            }
            catch (JsonException)
            {
                return false;
            }

            NetworkManager manager = bootstrap.Network;
            if (!manager || manager.ServerManager.Started) return true;

            if (_revision == revision) return true;

            if (_revision == null && !RoomMultiplayerMenu.Current) return false;

            if (!_session.PrepareRound(parts[2]) || !Application.CanStreamedLevelBeLoaded(scene))
            {
                _roundRejected = true;
                if (_session.AddonNotice == null)
                {
                    _session.ShowAddonError("The selected map scene is unavailable: " + scene);
                }

                _logger.Warning("Round blocked: " + (_session.AddonNotice?.Error ?? scene));
                _session.Relay.StopConnection(false);
                return false;
            }

            _roomId = parts[1];
            _revision = revision;
            _scene = scene;
            _previousSceneHandle = SceneManager.GetActiveScene().handle;
            _deferredStart = null;
            _readySent = false;
            _deadline = Time.unscaledTime + 120f;
            _nextRequest = 0f;
            _probeObjectId = -1;
            _reportedSceneMismatch = false;

            HoldSpawns();
            bootstrap._MADNGLFHLMO_k__BackingField = false;
            ApplyProperties(parts[2]);

            _logger.Msg($"Client round transition: {_scene}; revision {_revision}.");

            return true;
        }

        private static bool BeforeRoomStart(RoomMultiplayerMenu __instance)
        {
            RoundSync instance = _instance;
            if (instance._revision == null ||
                !instance._session.IsNullFrame ||
                instance._session.IsSwitching ||
                !NetBootstrap.Current ||
                __instance.gameObject.scene.name != instance._scene ||
                __instance.IsServerInitialized)
            {
                return true;
            }

            NetworkManager manager = NetBootstrap.Current.Network;
            if (!__instance.IsClientInitialized || manager.SceneManager.IteratingQueue)
            {
                instance._deferredStart = __instance;
                return false;
            }

            return true;
        }

        private static bool BeforePlayerSpawn(RoomMultiplayerMenu __instance, string teamName)
        {
            RoundSync instance = _instance;

            if (instance._profiles.Compatibility?.HoldSpawn(__instance, teamName) == true)
            {
                return false;
            }

            if (!instance._holdSpawns || !instance._session.IsNullFrame || instance._session.IsSwitching)
            {
                return true;
            }

            instance._queuedSpawnMenu = __instance;
            instance._queuedSpawnTeam = teamName;

            return false;
        }

        internal void HoldSpawns()
        {
            CancelSpawns();
            _holdSpawns = true;
        }

        internal void ReleaseSpawns() => _holdSpawns = false;

        internal void CancelSpawns()
        {
            _holdSpawns = false;
            _queuedSpawnMenu = null;
            _queuedSpawnTeam = null;
        }

        internal void Update()
        {
            UpdateClient();

            if (_holdSpawns || !_queuedSpawnMenu) return;

            RoomMultiplayerMenu menu = _queuedSpawnMenu;
            string team = _queuedSpawnTeam;

            CancelSpawns();

            if (_session.IsNullFrame &&
                !_session.IsSwitching &&
                _session.Relay &&
                _session.Relay.CurrentRoom != null &&
                menu == RoomMultiplayerMenu.Current &&
                menu.IsClientInitialized &&
                menu.IsInitialized &&
                menu.gameObject.activeInHierarchy)
            {
                menu.SpawnPlayer(team);
            }
        }

        private void UpdateClient()
        {
            if (_revision == null) return;

            NetBootstrap bootstrap = NetBootstrap.Current;
            NetworkManager manager = bootstrap ? bootstrap.Network : null;
            if (!_session.IsNullFrame ||
                _session.IsSwitching ||
                !_session.Relay ||
                _session.Relay.CurrentRoom?.Id != _roomId ||
                !manager ||
                !manager.ClientManager.Started ||
                manager.ServerManager.Started)
            {
                Clear();
                return;
            }

            RoomMultiplayerMenu menu = RoomMultiplayerMenu.Current;
            if (Time.unscaledTime > _deadline)
            {
                bool hasMenu = menu;

                _logger.Warning(
                    $"Client round initialization timed out: {_scene}; menu={hasMenu}; " +
                    $"active={hasMenu && menu.gameObject.activeInHierarchy}; client={hasMenu && menu.IsClientInitialized}; " +
                    $"initialized={hasMenu && menu.IsInitialized}; sceneId={(hasMenu ? menu.NetworkObject.SceneId : 0)}; " +
                    $"objectId={(hasMenu ? menu.NetworkObject.ObjectId : -1)}; " +
                    $"registered={hasMenu &&
                    manager.ClientManager.Objects.SceneObjects.ContainsKey(menu.NetworkObject.SceneId)}."
                );

                Clear();

                return;
            }

            UnityEngine.SceneManagement.Scene scene = SceneManager.GetActiveScene();
            if (manager.SceneManager.IteratingQueue || scene.name != _scene || scene.handle == _previousSceneHandle)
            {
                return;
            }

            bool ready = false;
            int objectId = -1;
            ulong sceneId = 0;
            bool registered = false;

            if (menu && menu.gameObject.scene == scene)
            {
                Il2CppFishNet.Object.NetworkObject networkObject = menu.NetworkObject;

                sceneId = networkObject.SceneId;
                registered = manager.ClientManager.Objects.SceneObjects.TryGetValue(sceneId, out var sceneObject) &&
                    sceneObject == networkObject;

                if (menu.IsClientInitialized &&
                    manager.ClientManager.Objects.Spawned.TryGetValue(networkObject.ObjectId, out var spawned) &&
                    spawned == networkObject)
                {
                    objectId = networkObject.ObjectId;
                    if (!menu.gameObject.activeSelf)
                    {
                        menu.gameObject.SetActive(true);
                        _logger.Msg($"Reactivated initialized room object {objectId}.");
                    }

                    if (_deferredStart == menu && menu.gameObject.activeInHierarchy)
                    {
                        _deferredStart = null;
                        menu.Start();
                    }

                    ready = menu.gameObject.activeInHierarchy && menu.IsInitialized && PlayerInfo.Local;
                }
            }

            if (_probeObjectId >= 0)
            {
                if (Time.frameCount < _probeFrame) return;

                if (sceneId != _probeSceneId && !_reportedSceneMismatch)
                {
                    _reportedSceneMismatch = true;
                    _logger.Warning($"Room scene object mismatch: client sceneId={sceneId}; server sceneId={_probeSceneId}.");
                }

                if (objectId < 0 && registered && sceneId == _probeSceneId)
                {
                    _logger.Msg($"Requesting room spawn: object {_probeObjectId}; sceneId {sceneId}; registered={registered}.");
                    _profiles.SendRoundSignal(new RoundSignal(
                            1,
                            _roomId,
                            _revision,
                            _scene,
                            false,
                            _probeObjectId,
                            missing: true,
                            sceneId: sceneId));

                    _nextRequest = Time.unscaledTime + 2f;
                    _probeObjectId = -1;

                    return;
                }

                _probeObjectId = -1;
            }

            if ((!ready || _readySent) && Time.unscaledTime < _nextRequest) return;

            _nextRequest = Time.unscaledTime + 2f;
            _profiles.SendRoundSignal(new RoundSignal(1, _roomId, _revision, _scene, ready, objectId, sceneId: sceneId));

            if (ready && !_readySent)
            {
                _readySent = true;
                _logger.Msg($"Client round initialized: {_scene}; room object {objectId}; revision {_revision}.");
            }
        }

        private void OnServerSignal(RoundSignal signal)
        {
            if (signal.RoomId != _roomId || signal.Revision != _revision || signal.Scene != _scene || signal.Missing)
            {
                return;
            }

            if (!signal.Ready)
            {
                if (signal.ObjectId >= 0 && signal.SceneId != 0)
                {
                    _probeObjectId = signal.ObjectId;
                    _probeSceneId = signal.SceneId;
                    _probeFrame = Time.frameCount + 2;
                }

                return;
            }

            RoomMultiplayerMenu menu = RoomMultiplayerMenu.Current;
            if (_readySent && menu && menu.IsClientInitialized && signal.ObjectId == menu.NetworkObject.ObjectId)
            {
                Clear(releaseSpawns: true);
            }
        }

        internal static void PreservePlayers(NetworkManager manager)
        {
            UnityEngine.SceneManagement.Scene holder = manager.SceneManager.GetMovedObjectsScene();

            foreach (PlayerInfo player in UnityEngine.Object.FindObjectsOfType<PlayerInfo>())
            {
                if ((player.IsServerInitialized || player.IsClientInitialized) &&
                    !player.NetworkObject.IsGlobal &&
                    player.gameObject.scene != holder)
                {
                    SceneManager.MoveGameObjectToScene(player.gameObject, holder);
                }
            }
        }

        internal static void ApplyProperties(string data)
        {
            RoomProperties properties = RoomProperties.Parse(data);
            if (properties.Version == RelaySession.NullFrameGameVersion && NetBootstrap.Current)
            {
                NetBootstrap.Current.RoomSettings = properties;
            }
        }

        private void Clear(bool releaseSpawns = false)
        {
            _roomId = _revision = _scene = null;
            _deferredStart = null;
            _readySent = false;
            _probeObjectId = -1;

            if (releaseSpawns)
            {
                ReleaseSpawns();
            }
            else
            {
                CancelSpawns();
            }
        }
    }
}
