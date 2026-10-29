using MelonLoader;
using NullServers.Additional;
using NullServers.Content;
using NullServers.Customization;
using NullServers.Network;
using NullServers.Players;
using NullServers.Room;
using NullServers.Sandbox;
using NullServers.Server;
using NullServers.UI;
using NullServers.Voice;

[assembly: MelonInfo(typeof(NullServers.Core), "NullServers", "0.0.80", "NullFrame Collective", "https://github.com/Nullframe-Collective/NullServers-ST3")]
[assembly: MelonOptionalDependencies("CustomWeaponsFramework", "CustomPlayerCustomization", "CustomNPCFramework", "CustomGameModesFramework", "CustomMapsFramework")]
[assembly: MelonPriority(100)]
[assembly: MelonGame("ZeoWorks", "Slendytubbies 3")]
[assembly: MelonColor(8, 115, 115, 115)]
[assembly: HarmonyDontPatchAll]

namespace NullServers
{
    public class Core : MelonMod
    {
        private PlayerProfiles _profiles;
        private FrameworkCompatibility _compatibility;
        private JoinNotices _joinNotices;
        private AddonMenu _addons;
        private ProfileMenu _profileMenu;
        private ServerControl _serverControl;
        private ServerSettings _serverSettings;
        private PauseMenu _pauseMenu;
        private LiveAppearance _appearance;
        private AppearanceMenu _appearanceMenu;
        private SandboxWorld _sandboxWorld;
        private SandboxMenu _sandboxMenu;
        private ServerMenu _serverMenu;
        private RoomCustomization _roomCustomization;
        private PlayerList _playerList;
        private RelaySession _session;
        private CommunityMenu _community;
        private ContentDownloads _downloads;
        private ContentServer _contentServer;
        private DownloadPanel _downloadPanel;
        private VersionLabel _versionLabel;
        private VoiceRelay _voice;
        private VoiceIndicators _voiceIndicators;
        private VoiceSettingsEntry _voiceSettings;

        public override void OnInitializeMelon()
        {
            Frameworks.Initialize(HarmonyInstance, LoggerInstance);

            RoomOptions options = new();
            RelaySession session = new(options, LoggerInstance);
            _session = session;
            _community = new CommunityMenu(session, LoggerInstance, Info.Version);
            session.RefreshAddons();
            ColorPicker picker = new();

            _addons = new AddonMenu(session);

            _profiles = new PlayerProfiles(session, LoggerInstance);
            _compatibility = new FrameworkCompatibility(session, _profiles, LoggerInstance);
            _joinNotices = new JoinNotices(session, _compatibility);
            _profileMenu = new ProfileMenu(session, _profiles, picker);
            _serverControl = new ServerControl(session, _profiles, LoggerInstance);
            _pauseMenu = new PauseMenu(session);
            _serverSettings = new ServerSettings(_serverControl, _pauseMenu);
            _appearance = new LiveAppearance(session, _profiles, LoggerInstance);
            _appearanceMenu = new AppearanceMenu(_pauseMenu, _appearance, _profiles);
            _sandboxWorld = new SandboxWorld(_serverControl, _profiles, session);
            _sandboxMenu = new SandboxMenu(_sandboxWorld);
            _serverMenu = new ServerMenu(LoggerInstance, session, _community);
            _roomCustomization = new RoomCustomization(session, options, picker);
            _playerList = new PlayerList(session, _profiles);

            _downloads = new ContentDownloads(session, _profiles, LoggerInstance);
            session.Downloads = _downloads;
            _compatibility.Downloads = _downloads;
            _contentServer = new ContentServer(session, _serverControl, _profiles);
            _downloadPanel = new DownloadPanel(_downloads, _pauseMenu);
            _versionLabel = new VersionLabel(Info.Version);
            _voice = new VoiceRelay(session, _profiles);
            _voiceIndicators = new VoiceIndicators();
            _voiceSettings = new VoiceSettingsEntry();

            // The patch classes call into the objects created above, so they are applied only after everything exists.
            HarmonyInstance.PatchAll(typeof(Core).Assembly);

            LoggerInstance.Msg($"Initialized. NullFrame GameVersion: {RelaySession.NullFrameGameVersion}.");
        }

        public override void OnUpdate()
        {
            _session?.UpdateCommunity();
            _community?.Update();
            _addons?.Update();
            _profiles?.Update();
            _compatibility?.Update();
            _joinNotices?.Update();
            _downloads?.Update();
            _contentServer?.Update();
            _serverControl?.Update();
            _appearance?.Update();
            _pauseMenu?.Update();
            _serverSettings?.Update();
            _appearanceMenu?.Update();
            _sandboxWorld?.Update();
            _sandboxMenu?.Update();
            _downloadPanel?.Update();
            _versionLabel?.Update();
            _voice?.Update();
            _voiceIndicators?.Update();
            _voiceSettings?.Update();
        }

        public override void OnGUI() => _voiceIndicators?.Draw();

        public override void OnLateUpdate()
        {
            _sandboxWorld?.Ragdolls.LateUpdate();
            _sandboxWorld?.Hands.LateUpdate();
            _sandboxWorld?.Living.LateUpdate();
            _sandboxMenu?.LateUpdate();
        }

        public override void OnFixedUpdate() => _sandboxWorld?.Hands.FixedUpdate();

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            if (sceneName is "MainMenu" or "Updater")
            {
                Frameworks.RestoreContent();
                Frameworks.Enable(Frameworks.Selected);
            }
        }

        public override void OnApplicationQuit()
        {
            _community?.Dispose();
            _downloads?.Reset();
            _session?.Blacklist.Dispose();
            Frameworks.RestoreContent();
            _addons?.Dispose();
            _sandboxWorld?.Hands.Dispose();
            _profileMenu?.Commit();
            MenuUi.Dispose();
        }
    }
}
