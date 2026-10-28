using HarmonyLib;
using Il2Cpp;
using Il2CppFishNet.Managing;
using Il2CppFishNet.Transporting;
using Il2CppFishNet.Transporting.Relay;
using MelonLoader;
using NullServers.Additional;
using NullServers.Content;
using NullServers.Room;
using NullServers.Server;
using System.Collections;
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NullServers.Network
{
    internal sealed class RelaySession
    {
        internal const string NullFrameGameVersion = "S3-NC";
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        private static RelaySession _instance;
        private readonly MelonLogger.Instance _logger;
        private readonly Dictionary<string, string> _roomVersions = new();
        private readonly Dictionary<string, string> _roomNameColors = new();
        private readonly RoomOptions _options;
        private readonly Dictionary<string, RoomRequirements> _roomContent = new();
        private AddonRequirement[] _activeAddons = Array.Empty<AddonRequirement>();
        private string _activeContent;
        private FrameworkManifest _content;
        private FrameworkManifest _contentSource;
        private AddonRequirement[] _contentAddons;
        private FrameworkManifest _contentView;

        internal FrameworkManifest Content
        {
            get
            {
                AddonRequirement[] addons = Addons;
                if (!ReferenceEquals(_contentSource, _content) || !ReferenceEquals(_contentAddons, addons))
                {
                    _contentSource = _content;
                    _contentAddons = addons;
                    _contentView = _content.For(Uses(addons, Dependencies.Weapons), Uses(addons, Dependencies.Customization),
                        Uses(addons, Dependencies.NPCs), Uses(addons, Dependencies.Maps));
                }

                return _contentView;
            }
            set => _content = value;
        }

        private static bool Uses(AddonRequirement[] addons, string id) => addons.Any(addon => addon.Id == id);

        internal string ContentHash => Content.Fingerprint;
        internal AddonRequirement[] Addons => Frameworks.Selected;
        internal AddonNotice AddonNotice { get; set; }
        internal string LastRoomId { get; private set; }
        internal ContentDownloads Downloads { get; set; }

        internal bool UsesWeapons => Uses(_activeAddons, Dependencies.Weapons);
        private string _officialVersion;
        private string _connectionVersion;
        private int _listedRooms = -1;
        private int _visibleRooms = -1;
        private bool _editingRoom;
        private bool _returningToMenu;
        private string _communityDenial;
        private string _blacklistError;

        internal GlobalBlacklist Blacklist { get; } = new();
        internal bool CommunityAccepted { get; set; }
        internal event Action<string> AccessDenied;

        internal RelayTransport Relay { get; private set; }
        internal bool IsNullFrame { get; private set; } = true;
        internal bool IsSwitching { get; set; }
        internal bool HoldRoomClosed { get; set; }

        internal string SelectedVersion => IsNullFrame ? NullFrameGameVersion : _officialVersion;

        internal void RefreshAddons() => _content = FrameworkContent.Create();

        internal void UpdateCommunity()
        {
            Blacklist.Update();
            if (_blacklistError != Blacklist.Error)
            {
                _blacklistError = Blacklist.Error;
                if (_blacklistError != null)
                {
                    _logger.Warning("Community blacklist refresh failed: " + _blacklistError);
                }
            }

            if (!IsNullFrame || IsSwitching || !Relay || Relay.OfflineMode || !Relay.IsConnectedToRelay)
            {
                _communityDenial = null;
                return;
            }

            if (Blacklist.Contains(Relay.LocalIdentity?.Username))
            {
                RejectCommunity(GlobalBlacklist.BannedMessage);
            }
        }

        private bool CheckCommunity(RelayTransport relay)
        {
            if (!IsNullFrame)
            {
                return true;
            }

            string reason = !Blacklist.Ready ? GlobalBlacklist.UnavailableMessage :
                Blacklist.Contains(relay.LocalIdentity?.Username) ? GlobalBlacklist.BannedMessage :
                !CommunityAccepted ? "Open NullFrame Servers from the server selection menu and acknowledge the community notices first." :
                string.IsNullOrWhiteSpace(relay.LocalIdentity?.Username) ? "Your relay account is not available. Please reconnect to NullFrame Servers." : null;
            if (reason == null)
            {
                return true;
            }

            RejectCommunity(reason);
            return false;
        }

        internal void RejectCommunity(string reason)
        {
            CommunityAccepted = false;
            if (_communityDenial != reason)
            {
                _communityDenial = reason;
                AccessDenied?.Invoke(reason);
            }

            if (Relay && Relay.CurrentRoom != null)
            {
                if (!_returningToMenu)
                {
                    _returningToMenu = true;
                    MelonCoroutines.Start(ReturnToMenu(Relay, Relay.CurrentRoom.Id));
                }
            }
            else if (Relay)
            {
                Relay.DisconnectFromRelay();
            }
        }

        internal RelaySession(HarmonyLib.Harmony harmony, RoomOptions options, MelonLogger.Instance logger)
        {
            _instance = this;
            _options = options;
            _logger = logger;

            harmony.Patch(
                AccessTools.Method(typeof(RelayTransport), nameof(RelayTransport.Initialize)),
                postfix: new HarmonyMethod(typeof(RelaySession), nameof(AfterRelayInitialize)));

            harmony.Patch(
                AccessTools.Method(typeof(RelayTransport), nameof(RelayTransport.ConnectToRelay)),
                prefix: new HarmonyMethod(typeof(RelaySession), nameof(BeforeConnect)));

            harmony.Patch(
                AccessTools.Method(typeof(RelayTransport), nameof(RelayTransport.Inner_OnClientConnectionState)),
                prefix: new HarmonyMethod(typeof(RelaySession), nameof(BeforeAuthentication)));

            harmony.Patch(
                AccessTools.Method(typeof(RelayTransport), nameof(RelayTransport.Relay_OnAuthenticateResponse)),
                postfix: new HarmonyMethod(typeof(RelaySession), nameof(AfterAuthentication)));

            harmony.Patch(
                AccessTools.Method(typeof(RelayTransport), nameof(RelayTransport.Relay_OnRoomList)),
                prefix: new HarmonyMethod(typeof(RelaySession), nameof(BeforeRoomList)));

            foreach (string method in new[]
            {
                nameof(RelayTransport.HostRoom),
                nameof(RelayTransport.CreateRoom),
                nameof(RelayTransport.UpdateRoom)
            })
            {
                harmony.Patch(
                    AccessTools.Method(typeof(RelayTransport), method),
                    prefix: new HarmonyMethod(typeof(RelaySession), nameof(BeforeRoomSettings)));
            }

            harmony.Patch(
                AccessTools.Method(typeof(RelayTransport), nameof(RelayTransport.JoinRoom)),
                prefix: new HarmonyMethod(typeof(RelaySession), nameof(BeforeJoinRoom)),
                postfix: new HarmonyMethod(typeof(RelaySession), nameof(AfterJoinRoom)));

            harmony.Patch(
                AccessTools.Method(typeof(RelayTransport), nameof(RelayTransport.Relay_OnRoomJoined)),
                prefix: new HarmonyMethod(typeof(RelaySession), nameof(BeforeRoomJoined)));

            harmony.Patch(
                AccessTools.Method(typeof(RelayTransport), nameof(RelayTransport.Relay_OnRoomCreated)),
                postfix: new HarmonyMethod(typeof(RelaySession), nameof(AfterRoomCreated)));
            harmony.Patch(
                AccessTools.Method(typeof(RelayTransport), nameof(RelayTransport.Relay_OnRoomUpdated)),
                postfix: new HarmonyMethod(typeof(RelaySession), nameof(AfterRoomUpdated)));
            harmony.Patch(
                AccessTools.Method(typeof(RelayTransport), nameof(RelayTransport.Relay_OnRoomLeft)),
                postfix: new HarmonyMethod(typeof(RelaySession), nameof(AfterRoomLeft)));
            harmony.Patch(
                AccessTools.Method(typeof(RelayTransport), nameof(RelayTransport.Relay_OnCreateRoomFailed)),
                postfix: new HarmonyMethod(typeof(RelaySession), nameof(AfterCreateRoomFailed)));

            harmony.Patch(
                AccessTools.Method(typeof(RelayTransport), nameof(RelayTransport.Relay_OnJoinRoomFailed)),
                postfix: new HarmonyMethod(typeof(RelaySession), nameof(AfterJoinRoomFailed)));

            harmony.Patch(
                AccessTools.Method(typeof(LobbyMenu), nameof(LobbyMenu.CAMKLBFGGNH)),
                prefix: new HarmonyMethod(typeof(RelaySession), nameof(BeforeRoomCompatibility)));
        }

        internal void BeginSwitch(bool useNullFrame)
        {
            _communityDenial = null;
            RestoreContent();
            IsSwitching = true;
            IsNullFrame = useNullFrame;
            AddonNotice = null;
            _activeAddons = Array.Empty<AddonRequirement>();
            _activeContent = null;
            Frameworks.Enable(Addons);
            RefreshAddons();

            _connectionVersion = null;
            _roomVersions.Clear();
            _roomContent.Clear();
            _roomNameColors.Clear();

            _listedRooms = _visibleRooms = -1;

            if (Relay)
            {
                Relay._lastRoomList.Clear();
            }
        }

        internal void ApplyVersion(RelayTransport relay)
        {
            Relay = relay;
            _officialVersion ??= relay.GameVersion;
            relay.GameVersion = SelectedVersion;
        }

        private bool IsReady(RelayTransport relay)
        {
            return !IsSwitching && relay.IsConnectedToRelay && _connectionVersion == SelectedVersion;
        }

        internal string GetRoomNameColor(string roomId)
        {
            return _roomNameColors.GetValueOrDefault(roomId);
        }

        internal bool UpdateRoom(string name, int maxPlayers, string color, JsonObject changes, out string error)
        {
            error = "Only the current NullFrame Servers host can change this room.";

            RelayRoomInfo current = Relay ? Relay.CurrentRoom : null;
            if (!IsNullFrame || current == null || !Relay.IsRoomHost || !IsReady(Relay))
            {
                return false;
            }

            if (!RoomName.TryParse(name, out var plain, out var display, out error))
            {
                return false;
            }

            if (maxPlayers < Math.Max(1, current.PlayerCount) || maxPlayers > RoomOptions.PlayerLimit)
            {
                error = $"Choose {Math.Max(1, current.PlayerCount)}–{RoomOptions.PlayerLimit} player slots.";
                return false;
            }

            if (!RoomOptions.IsValidColor(color))
            {
                error = "Use a color in #RRGGBB format.";
                return false;
            }

            if (!RoomData.TryParse(current.RoomData, out JsonObject properties))
            {
                error = "The current room settings could not be read.";
                return false;
            }

            if (changes != null)
            {
                foreach (var property in changes)
                {
                    properties[property.Key] = RoomData.Clone(property.Value);
                }
            }

            properties["Version"] = NullFrameGameVersion;
            if (!MapRooms.Prepare(properties, Content, true, out var selected, out error)) return false;

            RoomRequirements.Publish(properties, Addons, selected);
            properties["DisplayName"] = display;
            properties["NameColor"] = color.ToUpperInvariant();
            string data = properties.ToJsonString(JsonOptions);

            if (Encoding.UTF8.GetByteCount(data) > 1024)
            {
                error = "The formatted room settings exceed the relay limit.";
                return false;
            }

            _editingRoom = true;
            FrameworkManifest previous = _content;
            bool updated = false;

            try
            {
                _content = selected;
                if (Relay.UpdateRoom(plain, maxPlayers, current.IsVisible, data))
                {
                    updated = true;
                    error = null;
                    return true;
                }

                error = "The relay could not accept this update.";

                return false;
            }
            finally
            {
                if (!updated)
                {
                    _content = previous;
                }

                _editingRoom = false;
            }
        }

        private static void AfterRelayInitialize(RelayTransport __instance) => _instance.ApplyVersion(__instance);

        private static void BeforeConnect(RelayTransport __instance) => _instance.ApplyVersion(__instance);

        private static void BeforeAuthentication(RelayTransport __instance, ClientConnectionStateArgs args)
        {
            if (__instance.OfflineMode || args.ConnectionState != LocalConnectionState.Started) return;

            _instance.ApplyVersion(__instance);
            _instance._connectionVersion = _instance.SelectedVersion;
        }

        private static void AfterAuthentication(RelayTransport __instance, RelayAuthResult result)
        {
            if (result == RelayAuthResult.Success)
            {
                _instance._logger.Msg($"Relay authenticated. GameVersion: {_instance._connectionVersion}.");
                _instance.UpdateCommunity();
            }
            else
            {
                _instance._connectionVersion = null;
                _instance._logger.Warning($"Relay authentication failed: {result}.");
            }
        }

        private bool PrepareJoin(RoomRequirements requirements, string roomId)
        {
            LastRoomId = roomId;

            if (requirements == null)
            {
                AddonNotice = new AddonNotice(Array.Empty<AddonRequirement>(), Array.Empty<AddonRequirement>(),
                    "This room does not publish valid addon requirements. The host must update NullServers.", AddonNoticeKind.Room);
                return false;
            }

            RestoreContent();

            if (requirements.Packs != null)
            {
                _content = FrameworkContent.Create(requirements.Packs).For(requirements.Packs);
            }

            AddonNotice = requirements.Check(Frameworks.Detected, Content, Frameworks.Settings, deferContent: true);
            bool download = AddonNotice != null && Downloads?.Covers(roomId, AddonNotice) == true;

            if (AddonNotice != null && !download)
            {
                return false;
            }

            AddonNotice = null;
            _activeAddons = requirements.Addons;
            _activeContent = requirements.Fingerprint;

            if (requirements.Packs != null && !download)
            {
                return SelectContent(requirements.Packs);
            }

            if (requirements.Map != null)
            {
                Frameworks.Maps?.UseContent(new[] { requirements.Map.Id });
            }

            Frameworks.Enable(requirements.Addons.Where(addon => addon.Id is Dependencies.Modes or Dependencies.Maps));

            return true;
        }

        internal bool PrepareRound(string data)
        {
            if (!RoomRequirements.TryRead(data, out var requirements) || !requirements.TryRoundContent(Content, out var required))
            {
                ShowAddonError("The next round changes unsupported addon content. Rejoin the room to confirm its requirements.", AddonNoticeKind.Room);
                return false;
            }

            FrameworkManifest local = FrameworkContent.Create(required).Select(required);
            AddonNotice = requirements.Check(Frameworks.Detected, local, Frameworks.Settings, Frameworks.Problem);
            if (AddonNotice != null) return false;

            try
            {
                Frameworks.Maps?.RestoreContent();
                Frameworks.Maps?.UseContent(required.MapAssets.Select(asset => asset.Id).ToArray());
                Frameworks.Maps?.Enable(requirements.UsesMaps);
                _content = local;
                _activeAddons = requirements.Addons;
                _activeContent = required.Fingerprint;
                return true;
            }
            catch (Exception exception) when (exception is TargetInvocationException or InvalidOperationException or ArgumentException)
            {
                ShowAddonError(exception.InnerException?.Message ?? exception.Message, AddonNoticeKind.Unavailable);
                return false;
            }
        }

        internal bool SelectContent(FrameworkManifest required)
        {
            RoomRequirements requirements = new(_activeAddons, required.Fingerprint, required);

            if (Frameworks.ContentSelected && Content.Fingerprint == required.Fingerprint)
            {
                AddonNotice = requirements.Check(Frameworks.Detected, Content, Frameworks.Settings, Frameworks.Problem);
                return AddonNotice == null;
            }

            _content = FrameworkContent.Create(required).For(required);
            AddonNotice = requirements.Check(Frameworks.Detected, Content, Frameworks.Settings);

            if (AddonNotice != null) return false;

            if (!Frameworks.UseContent(required, out var error))
            {
                ShowAddonError(error, AddonNoticeKind.Unavailable);
                RestoreContent();
                return false;
            }

            Frameworks.Enable(_activeAddons);
            _content = FrameworkContent.Create().For(required);
            AddonNotice = requirements.Check(Frameworks.Detected, Content, Frameworks.Settings, Frameworks.Problem);

            if (AddonNotice != null)
            {
                RestoreContent();
                return false;
            }

            Scene scene = SceneManager.GetActiveScene();

            if (Relay && Relay.CurrentRoom != null && scene.name is not "MainMenu" and not "Updater")
            {
                try
                {
                    Frameworks.Weapons?.ReloadContent(scene.buildIndex, scene.name);
                    Frameworks.NPCs?.ReloadContent(scene.buildIndex, scene.name);

                    foreach (SyncCustomization customization in UnityEngine.Object.FindObjectsOfType<SyncCustomization>())
                    {
                        if (Frameworks.Customization?.Enabled == true)
                        {
                            Frameworks.Customization.Call<object>(Frameworks.Customization.Core, "InstantiateInGameCustomization", customization);
                        }
                    }

                    AddonNotice = requirements.Check(Frameworks.Detected, Content, Frameworks.Settings, Frameworks.Problem);
                }
                catch (Exception exception) when (exception is TargetInvocationException or InvalidOperationException)
                {
                    ShowAddonError(exception.InnerException?.Message ?? exception.Message, AddonNoticeKind.Unavailable);
                }

                if (AddonNotice != null)
                {
                    RestoreContent();
                    return false;
                }
            }

            return true;
        }

        internal void RestoreContent()
        {
            Frameworks.RestoreContent();
            Frameworks.Enable(Addons);
            RefreshAddons();
        }

        internal bool SetAddonEnabled(string id, bool enabled, out string error)
        {
            if (Relay && Relay.CurrentRoom != null)
            {
                error = "Leave the current room before changing addons.";
                return false;
            }

            RestoreContent();

            if (!Frameworks.Settings.SetEnabled(id, enabled, out error))
            {
                return false;
            }

            Frameworks.Enable(Addons);
            RefreshAddons();

            return true;
        }

        private static string ReadRoomVersion(string roomData)
        {
            if (string.IsNullOrWhiteSpace(roomData)) return null;

            try
            {
                using var document = JsonDocument.Parse(roomData);
                return document.RootElement.ValueKind == JsonValueKind.Object &&
                    document.RootElement.TryGetProperty("Version", out var version) &&
                    version.ValueKind == JsonValueKind.String
                    ? version.GetString()
                    : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        internal bool MatchesContent(FrameworkManifest content) => content.Fingerprint == _activeContent;

        internal void ShowAddonError(string error, AddonNoticeKind kind = AddonNoticeKind.Content, FrameworkManifest required = null)
        {
            AddonNotice = new AddonNotice(_activeAddons, Array.Empty<AddonRequirement>(), error, kind,
                required?.PackDifferences(Content.For(required)));
        }

        private static bool BeforeRoomList(
            RelayTransport __instance,
            ref Il2CppSystem.Collections.Generic.IReadOnlyList<RelayRoomInfo> list)
        {
            if (__instance.OfflineMode) return true;
            if (!_instance.IsReady(__instance)) return false;

            Il2CppSystem.Collections.Generic.List<RelayRoomInfo> rooms = new();
            int count = list.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<RelayRoomInfo>>().Count;

            _instance._roomVersions.Clear();
            _instance._roomContent.Clear();
            _instance._roomNameColors.Clear();

            for (int i = 0; i < count; i++)
            {
                RelayRoomInfo room = list[i];
                string version = ReadRoomVersion(room.RoomData);

                _instance._roomVersions[room.Id] = version;
                RoomRequirements.TryRead(room.RoomData, out var requirements);
                _instance._roomContent[room.Id] = requirements;

                if (version != _instance.SelectedVersion) continue;

                rooms.Add(room);
                _instance._roomNameColors[room.Id] = RoomOptions.ReadNameColor(room.RoomData);
            }

            if (_instance._listedRooms != count || _instance._visibleRooms != rooms.Count)
            {
                _instance._logger.Msg($"Room list: {rooms.Count}/{count} rooms match {_instance.SelectedVersion}.");
            }

            _instance._listedRooms = count;
            _instance._visibleRooms = rooms.Count;

            list = rooms.Cast<Il2CppSystem.Collections.Generic.IReadOnlyList<RelayRoomInfo>>();

            return true;
        }

        private static bool BeforeRoomSettings(
            RelayTransport __instance,
            ref string name,
            ref int maxPlayers,
            ref string roomData,
            ref bool __result)
        {
            if (__instance.OfflineMode) return true;

            if (!_instance.CheckCommunity(__instance))
            {
                __result = false;
                return false;
            }

            if (_instance.IsReady(__instance))
            {
                if (!RoomData.TryParse(roomData, out JsonObject properties))
                {
                    __result = false;
                    _instance._logger.Warning("Room action blocked: the room settings are not a JSON object.");
                    return false;
                }

                properties["Version"] = _instance.SelectedVersion;

                if (_instance.IsNullFrame && _instance.HoldRoomClosed)
                {
                    properties["IsOpen"] = false;
                }

                if (_instance.IsNullFrame)
                {
                    if (__instance.CurrentRoom == null)
                    {
                        _instance.RestoreContent();
                        if (!MapRooms.Prepare(properties, _instance.Content, false, out var selected, out var mapError))
                        {
                            __result = false;
                            _instance.AddonNotice = new AddonNotice(_instance.Addons, Array.Empty<AddonRequirement>(), mapError, AddonNoticeKind.Unavailable);
                            _instance._logger.Warning("Room creation blocked: " + mapError);
                            return false;
                        }

                        _instance._content = selected;
                        RoomRequirements requirements = new(_instance.Addons, _instance.ContentHash, _instance.Content);
                        _instance.AddonNotice = requirements.Check(Frameworks.Detected, _instance.Content, Frameworks.Settings, Frameworks.Problem);

                        if (_instance.AddonNotice != null)
                        {
                            __result = false;
                            _instance._logger.Warning("Room creation blocked: " + _instance.AddonNotice.Error);
                            return false;
                        }
                    }

                    _instance._activeAddons = _instance.Addons;
                    _instance._activeContent = _instance.ContentHash;
                    Frameworks.Enable(_instance.Addons);
                    RelayRoomInfo current = __instance.CurrentRoom;

                    if (!_instance._editingRoom)
                    {
                        maxPlayers = Math.Clamp(current?.MaxPlayers ?? _instance._options.MaxPlayers, 1, RoomOptions.PlayerLimit);
                        properties["NameColor"] = current == null ?
                                                  _instance._options.NameColor :
                                                  RoomOptions.ReadNameColor(current.RoomData) ?? _instance._options.NameColor;

                        if (current != null)
                        {
                            name = current.Name;
                            properties["DisplayName"] = RoomName.ReadDisplay(current.RoomData, current.Name);
                        }
                        else if (_instance._options.DisplayName != null)
                        {
                            if (!RoomName.TryParse(_instance._options.DisplayName, out var plain, out var display, out var error))
                            {
                                __result = false;
                                _instance._logger.Warning(error);
                                return false;
                            }

                            name = plain;
                            properties["DisplayName"] = display;
                        }
                    }

                    if (current != null && RoomData.TryParse(current.RoomData, out JsonObject previous))
                    {
                        RoomData.CopyMissing(properties, previous);
                    }
                }
                else
                {
                    if (MapRooms.Find(RoomData.Text(properties, "MapName")) != null)
                    {
                        __result = false;
                        _instance._logger.Warning("Custom maps are available on NullFrame servers only.");
                        return false;
                    }

                    properties.Remove("CustomMap");
                    properties.Remove("Content");
                    properties.Remove("Addons");
                    properties.Remove("Packs");
                    properties.Remove("NameColor");
                    properties.Remove("DisplayName");
                }

                if (_instance.IsNullFrame)
                {
                    if (!MapRooms.Prepare(properties, _instance.Content, __instance.CurrentRoom != null, out var selected, out var mapError))
                    {
                        __result = false;
                        _instance._logger.Warning(mapError);
                        return false;
                    }

                    _instance._content = selected;
                    RoomRequirements.Publish(properties, _instance.Addons, _instance.Content);
                }

                roomData = properties.ToJsonString(JsonOptions);

                if (Encoding.UTF8.GetByteCount(roomData) > 1024)
                {
                    __result = false;
                    _instance._logger.Warning("Room settings exceed the relay limit, including the required content signature.");
                    return false;
                }

                return true;
            }

            __result = false;

            _instance._logger.Warning("Room action blocked: the selected server group is not connected.");

            return false;
        }

        private static bool BeforeJoinRoom(RelayTransport __instance, string roomId, ref bool __result)
        {
            if (__instance.OfflineMode) return true;

            if (!_instance.CheckCommunity(__instance))
            {
                __result = false;
                return false;
            }

            if (_instance.IsReady(__instance) &&
                !string.IsNullOrEmpty(roomId) &&
                (!_instance._roomVersions.TryGetValue(roomId, out var version) || version == _instance.SelectedVersion))
            {
                if (!_instance.IsNullFrame || _instance.PrepareJoin(_instance._roomContent.GetValueOrDefault(roomId), roomId))
                {
                    return true;
                }
            }

            __result = false;
            string detail = _instance.AddonNotice is { } notice
                ? notice.Error ?? "Missing addon DLLs: " + string.Join(", ", notice.Missing.Select(addon => addon.Id))
                : "The selected server group is not connected or the room version differs.";
            _instance._logger.Warning("Join blocked: " + detail);

            return false;
        }

        private static void AfterJoinRoom(bool __result)
        {
            if (!__result)
            {
                _instance.RestoreContent();
            }
        }

        private static void AfterJoinRoomFailed(RelayRoomResult result) => _instance.RestoreContent();

        private static bool BeforeRoomJoined(RelayTransport __instance, RelayRoomInfo info)
        {
            if (__instance.OfflineMode) return true;

            if (!_instance.CheckCommunity(__instance))
            {
                __instance.StopConnection(false);
                return false;
            }

            RoomRequirements.TryRead(info.RoomData, out var requirements);

            if (_instance.IsReady(__instance) &&
                ReadRoomVersion(info.RoomData) == _instance.SelectedVersion &&
                info.MaxPlayers >= 1 &&
                info.MaxPlayers <= (_instance.IsNullFrame ? RoomOptions.PlayerLimit : 12) &&
                (!_instance.IsNullFrame || _instance.PrepareJoin(requirements, info.Id)))
            {
                return true;
            }

            __instance.StopConnection(false);
            _instance._logger.Warning("Join cancelled: the room requirements or selected server group changed.");

            return false;
        }

        private static void AfterRoomCreated(RelayRoomInfo info)
        {
            if (!_instance.IsNullFrame) return;

            string color = RoomOptions.ReadNameColor(info.RoomData);
            _instance._roomNameColors[info.Id] = color;
            _instance._logger.Msg($"NullFrame Servers room created. Id: {info.Id}; Players: {info.PlayerCount}/{info.MaxPlayers}; NameColor: {color ?? "none"}.");
        }

        private static void AfterRoomUpdated(RelayRoomInfo info)
        {
            if (!_instance.IsNullFrame) return;

            string color = RoomOptions.ReadNameColor(info.RoomData);
            _instance._roomNameColors[info.Id] = color;
            _instance._logger.Msg($"NullFrame Servers room updated. Players: {info.PlayerCount}/{info.MaxPlayers}; NameColor: {color ?? "none"}.");
        }

        private static void AfterCreateRoomFailed(RelayRoomResult result)
        {
            if (!_instance.IsNullFrame) return;
           _instance._logger.Warning($"NullFrame Servers room creation failed: {result}.");
        }

        private static void AfterRoomLeft(RelayTransport __instance, RelayRoomLeaveReason reason)
        {
            RelaySession instance = _instance;
            instance.RestoreContent();

            if (!instance.IsNullFrame ||
                instance.IsSwitching ||
                instance._returningToMenu ||
                (reason != RelayRoomLeaveReason.Kicked && reason != RelayRoomLeaveReason.KickedByModerator))
            {
                return;
            }

            instance._returningToMenu = true;
            MelonCoroutines.Start(instance.ReturnToMenu(__instance, __instance.CurrentRoom?.Id));
        }

        private IEnumerator ReturnToMenu(RelayTransport relay, string removedRoomId)
        {
            bool Cancelled() => IsSwitching ||
                                Relay != relay ||
                                !relay ||
                                (relay.CurrentRoom != null && relay.CurrentRoom.Id != removedRoomId);

            try
            {
                yield return null;

                if (Cancelled()) yield break;

                NetBootstrap bootstrap = NetBootstrap.Current;
                if (!bootstrap) yield break;

                NetworkManager manager = bootstrap.Network;
                string menuScene = bootstrap._menuScene;
                bootstrap.PendingScene = menuScene;
                bootstrap.NNLCAJPMCPG = null;

                if (manager.ClientManager.Started)
                {
                    manager.ClientManager.StopConnection();
                }

                if (relay)
                {
                    relay.DisconnectFromRelay();
                }

                float deadline = Time.realtimeSinceStartup + 10f;
                while (manager &&
                    (manager.ClientManager.Started || manager.SceneManager.IteratingQueue) &&
                    Time.realtimeSinceStartup < deadline)
                {
                    if (Cancelled()) yield break;
                    yield return null;
                }

                yield return null;

                if (Cancelled()) yield break;

                if (SceneManager.GetActiveScene().name != menuScene)
                {
                    SceneManager.LoadScene(menuScene);
                }

                _logger.Msg("Removed from the room. Returned to the main menu.");
            }
            finally
            {
                _returningToMenu = false;
            }
        }

        private static bool BeforeRoomCompatibility(RelayRoomInfo __0, RoomProperties __1, ref bool __result)
        {
            if (_instance.Relay && _instance.Relay.OfflineMode) return true;

            __result = __0.MaxPlayers >= 1 &&
                __0.MaxPlayers <= (_instance.IsNullFrame ? RoomOptions.PlayerLimit : 12) &&
                !string.IsNullOrEmpty(__1.MapName) &&
                (Application.CanStreamedLevelBeLoaded(__1.MapName) || _instance.IsNullFrame &&
                    _instance._roomContent.GetValueOrDefault(__0.Id) is { UsesMaps: true }) &&
                __1.Version == _instance.SelectedVersion &&
                __1.IsOpen;

            return false;
        }
    }
}
