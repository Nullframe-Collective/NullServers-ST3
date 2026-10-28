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

[assembly: MelonInfo(typeof(NullServers.Core), "NullServers", "0.0.37", "NullFrame Collective", "https://github.com/Nullframe-Collective/NullServers-ST3")]
[assembly: MelonOptionalDependencies("CustomWeaponsFramework", "CustomPlayerCustomization", "CustomNPCFramework", "CustomGameModesFramework", "CustomMapsFramework")]
[assembly: MelonPriority(100)]
[assembly: MelonGame("ZeoWorks", "Slendytubbies 3")]
[assembly: MelonColor(8, 115, 115, 115)]

namespace NullServers
{
    public class Core : MelonMod
    {
        private PlayerProfiles _profiles;
        private FrameworkCompatibility _compatibility;
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

        public override void OnInitializeMelon()
        {
            Frameworks.Initialize(HarmonyInstance, LoggerInstance);

            RoomOptions options = new();
            RelaySession session = new(HarmonyInstance, options, LoggerInstance);
            _session = session;
            _community = new CommunityMenu(HarmonyInstance, session, LoggerInstance);
            session.RefreshAddons();
            ColorPicker picker = new();

            _addons = new AddonMenu(HarmonyInstance, session);

            _profiles = new PlayerProfiles(HarmonyInstance, session, LoggerInstance);
            _compatibility = new FrameworkCompatibility(session, _profiles, LoggerInstance);
            _profileMenu = new ProfileMenu(HarmonyInstance, session, _profiles, picker);
            _serverControl = new ServerControl(HarmonyInstance, session, _profiles, LoggerInstance);
            _pauseMenu = new PauseMenu(session);
            _serverSettings = new ServerSettings(_serverControl, _pauseMenu);
            _appearance = new LiveAppearance(HarmonyInstance, session, _profiles, LoggerInstance);
            _appearanceMenu = new AppearanceMenu(_pauseMenu, _appearance, _profiles);
            _sandboxWorld = new SandboxWorld(HarmonyInstance, _serverControl, _profiles, session);
            _sandboxMenu = new SandboxMenu(HarmonyInstance, _sandboxWorld);
            _serverMenu = new ServerMenu(HarmonyInstance, LoggerInstance, session, _community);
            _roomCustomization = new RoomCustomization(HarmonyInstance, session, options, picker);
            _playerList = new PlayerList(HarmonyInstance, session, _profiles);

            _downloads = new ContentDownloads(session, _profiles, LoggerInstance);
            session.Downloads = _downloads;
            _compatibility.Downloads = _downloads;
            _contentServer = new ContentServer(session, _serverControl, _profiles);
            _downloadPanel = new DownloadPanel(_downloads, _pauseMenu);

            LoggerInstance.Msg($"Initialized. NullFrame GameVersion: {RelaySession.NullFrameGameVersion}.");
        }

        public override void OnUpdate()
        {
            _session?.UpdateCommunity();
            _community?.Update();
            _addons?.Update();
            _profiles?.Update();
            _compatibility?.Update();
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
        }

        public override void OnLateUpdate()
        {
            _sandboxWorld?.Ragdolls.LateUpdate();
            _sandboxWorld?.Hands.LateUpdate();
            _sandboxWorld?.Living.LateUpdate();
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
