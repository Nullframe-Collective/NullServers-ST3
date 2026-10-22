using MelonLoader;
using NullServers.Customization;
using NullServers.Network;
using NullServers.Players;
using NullServers.Room;
using NullServers.Sandbox;
using NullServers.Server;
using NullServers.UI;

[assembly: MelonInfo(typeof(NullServers.Core), "NullServers", "0.0.20", "NullFrame Collective", "https://github.com/Nullframe-Collective/NullServers-ST3")]
[assembly: MelonGame("ZeoWorks", "Slendytubbies 3")]
[assembly: MelonColor(8, 115, 115, 115)]

namespace NullServers
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
        private SandboxWorld _sandboxWorld;
        private SandboxMenu _sandboxMenu;
        private ServerMenu _serverMenu;
        private RoomCustomization _roomCustomization;
        private PlayerList _playerList;

        public override void OnInitializeMelon()
        {
            RoomOptions options = new();
            RelaySession session = new(HarmonyInstance, options, LoggerInstance);
            ColorPicker picker = new();

            _profiles = new PlayerProfiles(HarmonyInstance, session, LoggerInstance);
            _profileMenu = new ProfileMenu(HarmonyInstance, session, _profiles, picker);
            _serverControl = new ServerControl(HarmonyInstance, session, _profiles, LoggerInstance);
            _pauseMenu = new PauseMenu(session);
            _serverSettings = new ServerSettings(_serverControl, picker, _pauseMenu);
            _appearance = new LiveAppearance(HarmonyInstance, session, LoggerInstance);
            _appearanceMenu = new AppearanceMenu(_pauseMenu, _appearance, _profiles, picker);
            _sandboxWorld = new SandboxWorld(HarmonyInstance, _serverControl, _profiles, session);
            _sandboxMenu = new SandboxMenu(HarmonyInstance, _sandboxWorld);
            _serverMenu = new ServerMenu(HarmonyInstance, LoggerInstance, session);
            _roomCustomization = new RoomCustomization(HarmonyInstance, session, options, picker);
            _playerList = new PlayerList(HarmonyInstance, session, _profiles);

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
            _sandboxWorld?.Update();
            _sandboxMenu?.Update();
        }

        public override void OnGUI()
        {
            _serverSettings?.Draw();
            _appearanceMenu?.Draw();
        }

        public override void OnLateUpdate()
        {
            _sandboxWorld?.Ragdolls.LateUpdate();
            _sandboxWorld?.Gun.LateUpdate();
        }

        public override void OnFixedUpdate() => _sandboxWorld?.Gun.FixedUpdate();

        public override void OnApplicationQuit()
        {
            _sandboxWorld?.Gun.Dispose();
            _profileMenu?.Commit();
        }
    }
}
