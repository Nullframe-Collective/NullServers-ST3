using MelonLoader;
using S3_NullServers.Customization;
using S3_NullServers.Network;
using S3_NullServers.Players;
using S3_NullServers.Room;
using S3_NullServers.Sandbox;
using S3_NullServers.Server;
using S3_NullServers.UI;

[assembly: MelonInfo(typeof(S3_NullServers.Core), "S3-NullServers", "0.0.20", "NullFrame Collective", null)]
[assembly: MelonGame("ZeoWorks", "Slendytubbies 3")]

namespace S3_NullServers
{
    public class Core : MelonMod
    {
        private PlayerProfiles _profiles;
        private ProfileMenu _profileMenu;
        private ServerControl _serverControl;
        private ServerSettings _serverSettings;
        private PauseMenu _pauseMenu;
        private LiveAppearance _appearance;
        private AppearanceMenu _appearanceMenu;
        private SandboxWorld _sandbox;
        private SandboxMenu _sandboxMenu;

        public override void OnInitializeMelon()
        {
            RoomOptions options = new();
            RelaySession session = new(HarmonyInstance, LoggerInstance, options);
            _profiles = new PlayerProfiles(HarmonyInstance, session, LoggerInstance);
            ColorPicker picker = new();
            _profileMenu = new ProfileMenu(HarmonyInstance, session, _profiles, picker);
            _serverControl = new ServerControl(HarmonyInstance, session, _profiles, LoggerInstance);
            _pauseMenu = new PauseMenu(session);
            _serverSettings = new ServerSettings(_serverControl, picker, _pauseMenu);
            _appearance = new LiveAppearance(HarmonyInstance, session, LoggerInstance);
            _appearanceMenu = new AppearanceMenu(_pauseMenu, _appearance, _profiles, picker);
            _sandbox = new SandboxWorld(HarmonyInstance, _serverControl, _profiles, session);
            _sandboxMenu = new SandboxMenu(HarmonyInstance, _sandbox);

            _ = new ServerMenu(HarmonyInstance, LoggerInstance, session);
            _ = new RoomCustomization(HarmonyInstance, session, options, picker);
            _ = new PlayerList(HarmonyInstance, session, _profiles);

            LoggerInstance.Msg($"Initialized. NullFrame GameVersion: {RelaySession.NullFrameGameVersion}.");
        }

        public override void OnUpdate()
        {
            _profiles?.Update();
            _serverControl?.Update();
            _appearance?.Update();
            _pauseMenu?.Update();
            _serverSettings?.Update();
            _appearanceMenu?.Update();
            _sandbox?.Update();
            _sandboxMenu?.Update();
        }

        public override void OnGUI()
        {
            _serverSettings?.Draw();
            _appearanceMenu?.Draw();
        }

        public override void OnLateUpdate()
        {
            _sandbox?.Ragdolls.LateUpdate();
            _sandbox?.Gun.LateUpdate();
        }

        public override void OnFixedUpdate() => _sandbox?.Gun.FixedUpdate();

        public override void OnApplicationQuit()
        {
            _sandbox?.Gun.Dispose();
            _profileMenu?.Commit();
        }
    }
}
