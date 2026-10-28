using HarmonyLib;
using Il2Cpp;
using Il2CppFishNet.Object;
using UnityEngine;

namespace NullServers.Server
{
    internal sealed class HostTools
    {
        private static HostTools _instance;
        private readonly ServerControl _control;
        private bool _placingConsole;
        private NetworkObject _console;

        internal HostTools(HarmonyLib.Harmony harmony, ServerControl control)
        {
            _instance = this;
            _control = control;

            harmony.Patch(
                AccessTools.Method(typeof(Volume), nameof(Volume.OnStartServer)),
                prefix: new HarmonyMethod(typeof(HostTools), nameof(BeforeConsoleStart)));
        }

        private static bool BeforeConsoleStart(Volume __instance)
        {
            return !(_instance._placingConsole || (_instance._console && __instance.NetworkObject == _instance._console)) ||
                !_instance._control.CanManage ||
                RoomProperties.Current.GameMode == "SBX";
        }

        internal bool SpawnConsole(out string error)
        {
            error = "Only the host can spawn the sandbox console.";

            RoomMultiplayerMenu menu = RoomMultiplayerMenu.Current;
            if (!_control.CanManage || _control.Busy || !menu || !menu.IsInitialized)
            {
                return false;
            }

            NetworkObject existing = UnityEngine.Object.FindObjectsOfType<NetworkObject>()
                .FirstOrDefault(networkObject => networkObject.IsServerInitialized &&
                networkObject.gameObject.scene == menu.gameObject.scene &&
                networkObject.name.StartsWith("Wemon", StringComparison.Ordinal));

            if (existing)
            {
                error = "The sandbox console is already present on this map.";
                return false;
            }

            _placingConsole = true;

            try
            {
                _console = NetworkSpawner.Spawn("Wemon", menu.transform.position, menu.transform.rotation, null);
                error = _console ? null : "The sandbox console could not be spawned.";
                return _console;
            }
            finally
            {
                _placingConsole = false;
            }
        }
    }
}
