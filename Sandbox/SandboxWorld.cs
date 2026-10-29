using Il2Cpp;
using Il2CppFishNet.Connection;
using Il2CppFishNet.Managing;
using Il2CppFishNet.Transporting;
using Il2CppInterop.Runtime;
using NullServers.Network;
using NullServers.Players;
using NullServers.Server;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NullServers.Sandbox
{
    internal sealed class SandboxWorld
    {
        private static SandboxWorld _instance;
        private readonly ServerControl _control;
        private readonly PlayerProfiles _profiles;
        private readonly RelaySession _session;
        private readonly Dictionary<int, IntPtr> _peers = new();
        private readonly Dictionary<int, (float Time, int Count)> _requests = new();
        private readonly Dictionary<int, PlayerEffects> _effects = new();
        private readonly Dictionary<IntPtr, (PlayerNetworkController Player, Vector3 Scale)> _scaled = new();
        private readonly Queue<(NetworkConnection Connection, SandboxMessage Message)> _outgoing = new();
        private readonly Dictionary<int, TrafficBudget> _snapshotBudgets = new();
        private RoomMultiplayerMenu _menu;
        private FPScontroller _movement;
        private Vector3 _movementScale;
        private bool _controllerEnabled;
        private bool _flying, _ghost;
        private bool _selfFlight;
        private string _room;
        private float _helloAt;
        private float _scanAt;
        private Vector3 _launch;
        private float _movementGravity;

        private static readonly Dictionary<int, PlayerNetworkController> Avatars = new();
        private static float _avatarAt = float.MinValue;
        private Volume[] _volumes = Array.Empty<Volume>();
        private float _volumeAt = float.MinValue;

        internal SandboxAssets Assets { get; } = new();
        internal SandboxBuilding Building { get; }
        internal RagdollSync Ragdolls { get; }
        internal PhysicsReplication Physics { get; }
        internal Hands Hands { get; }
        internal LivingRagdolls Living { get; }
        internal Room.EnvironmentSettings Environment => _control.Access.Environment;
        internal bool SetEnvironment(Room.EnvironmentSettings settings, out string error) => _control.Access.SetEnvironment(settings, out error);

        internal bool Active =>
            _session.IsNullFrame &&
            !_session.IsSwitching &&
            _control.Room != null &&
            _menu &&
            _menu.IsInitialized &&
            _control.Compatibility.Ready;

        internal bool IsHost => _control.CanManage;
        internal bool IsAdmin => _control.Access.CanOpen;
        internal int LocalId => _session.Relay ? _session.Relay.LocalPlayerId : -1;

        internal string Epoch { get; private set; }
        internal string Status { get; private set; }

        internal IReadOnlyList<RoomPlayer> Players => _control.Access.Players;

        internal Volume[] Volumes
        {
            get
            {
                if (Time.unscaledTime - _volumeAt >= 0.5f || _volumes.Any(volume => !volume))
                {
                    _volumeAt = Time.unscaledTime;
                    _volumes = Object.FindObjectsOfType<Volume>().ToArray();
                }

                return _volumes;
            }
        }

        internal bool ConsoleEnabled => Active && Volumes.Any(volume => volume && volume.IsClientInitialized && volume.ConsoleState?.Value == 1);

        internal FPScontroller LocalMovement => _movement ? _movement : null;

        internal SandboxWorld(ServerControl control, PlayerProfiles profiles, RelaySession session)
        {
            _instance = this;
            _control = control;
            _profiles = profiles;
            _session = session;

            Building = new SandboxBuilding(this);
            Ragdolls = new RagdollSync(this);
            Physics = new PhysicsReplication(this);
            Living = new LivingRagdolls(this);
            Hands = new Hands(this);

            profiles.SandboxReceived += ReceiveRequest;
            profiles.SandboxReceivedFromHost += Receive;
        }

        private const int ConsoleValueOffset = 0x74;

        private string ConsoleCommand(SandboxCommand command)
        {
            bool enabled = command.Value > 0.5f;
            foreach (Volume volume in Volumes)
            {
                if (!volume || !volume.IsServerInitialized) continue;

                switch (command.Action)
                {
                    case PlayerCommand.FriendlyFire:
                        if (volume.AKKCLMBPAKO)
                        {
                            volume.AKKCLMBPAKO.isOn = enabled;
                        }

                        volume.GHHDLLKGPHJ(enabled ? 1 : 0);
                        break;
                    case PlayerCommand.DisableAI:
                        if (volume.MPOHPEJNMDB)
                        {
                            volume.MPOHPEJNMDB.isOn = enabled;
                        }

                        volume.JBFNPGLLHOA(enabled ? 1 : 0);
                        break;
                    case PlayerCommand.RemoveAllAI:
                        volume.DestroyAll(true);
                        break;
                    default:
                        int index = (int)command.Value;
                        if (!int.TryParse(command.Text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int option) ||
                            volume.Categories == null || index < 0 || index >= volume.Categories.Count ||
                            volume.Categories[index] is not { } category || !(category.hostOnly || category.isMusic) ||
                            category.options == null || option >= category.options.Count)
                        {
                            return "This console option is not available.";
                        }

                        volume.SendOption(index, option);
                        break;
                }
            }

            return null;
        }

        internal static void BeforeConsoleRequest(Volume __instance, NetworkConnection __2, out int __state)
        {
            __state = -1;
            SandboxWorld world = _instance;
            if (!world.Active || !world.IsHost || __2 == null || !world.Administrator(__2.ClientId)) return;

            var console = __instance.ConsoleState;
            if (console == null) return;

            IntPtr value = console.Pointer + ConsoleValueOffset;
            int current = System.Runtime.InteropServices.Marshal.ReadInt32(value);
            if (current == 1 || current != console.Value) return;

            __state = current;
            System.Runtime.InteropServices.Marshal.WriteInt32(value, 1);
        }

        internal static Exception AfterConsoleRequest(Volume __instance, int __state, Exception __exception)
        {
            if (__state >= 0 && __instance.ConsoleState is { } console)
            {
                System.Runtime.InteropServices.Marshal.WriteInt32(console.Pointer + ConsoleValueOffset, __state);
            }

            return __exception;
        }

        internal SandboxMessage Message(SandboxKind kind) => new(2, _room, _menu.NetworkObject.ObjectId, Epoch ?? "", kind);
        internal PlayerEffects Effects(int id) => _effects.GetValueOrDefault(id) ?? new PlayerEffects(id);
        internal bool Administrator(int id) => _control.Access.IsAdministrator(id);
        internal void SetStatus(string value) => Status = value;
        internal void Submit(SandboxMessage message)
        {
            if (!Active || Epoch == null)
            {
                Status = "Waiting for the host's NullFrame sandbox support...";
                return;
            }

            if (IsHost)
            {
                Process(LocalId, message);
            }
            else
            {
                _profiles.SendSandbox(message, toServer: true);
            }
        }

        internal void Admin(PlayerCommand action, int target, float value = 0f, string text = null)
        {
            Submit(Message(SandboxKind.Admin) with
            {
                Command = new SandboxCommand(action, target, value, text)
            });
        }

        internal void Send(SandboxMessage message, NetworkConnection target = null, bool unreliable = false)
        {
            if (!Active || !IsHost) return;

            if (target != null)
            {
                SendTo(target, SandboxMessage.Serialize(message), unreliable);
                return;
            }

            foreach (NetworkConnection connection in _control.Connections())
            {
                if (connection.ClientId != LocalId &&
                    connection.IsAuthenticated &&
                    _peers.GetValueOrDefault(connection.ClientId) == connection.Pointer)
                {
                    _profiles.SendSandbox(message, connection, channel: unreliable ? Channel.Unreliable : Channel.Reliable);
                }
            }
        }

        internal void SendTo(NetworkConnection target, string json, bool unreliable = false)
        {
            if (!Active || !IsHost || target.ClientId == LocalId) return;

            _profiles.SendSandboxJson(json, target, channel: unreliable ? Channel.Unreliable : Channel.Reliable);
        }

        internal void Queue(SandboxMessage message, NetworkConnection connection) => _outgoing.Enqueue((connection, message));

        internal IEnumerable<NetworkConnection> Peers()
        {
            return _control.Connections().Where(connection => connection.ClientId != LocalId &&
                connection.IsAuthenticated &&
                _peers.GetValueOrDefault(connection.ClientId) == connection.Pointer);
        }

        internal void SubmitAim(SandboxMessage message)
        {
            if (!Active || Epoch == null) return;

            if (IsHost)
            {
                Hands.Aim(LocalId, message.Carry);
            }
            else
            {
                _profiles.SendSandbox(message, toServer: true, channel: Channel.Unreliable);
            }
        }

        // The host receives every sandbox request from the clients here.
        private void ReceiveRequest(NetworkConnection connection, SandboxMessage message)
        {
            if (!Active || !IsHost || !connection.IsValid || !connection.IsAuthenticated) return;
            if (message.Room != _room || message.RoomObject != _menu.NetworkObject.ObjectId) return;

            // Aiming and escaping are sent many times per second, so they skip the request budget.
            if (message.Kind == SandboxKind.CarryAim)
            {
                if (message.Epoch == Epoch && IsPeer(connection))
                {
                    Hands.Aim(connection.ClientId, message.Carry);
                }

                return;
            }

            if (message.Kind == SandboxKind.Escape)
            {
                if (message.Epoch == Epoch && IsPeer(connection))
                {
                    Living.Escape(connection.ClientId);
                }

                return;
            }

            if (!TakeRequest(connection.ClientId)) return;

            if (message.Kind == SandboxKind.Hello)
            {
                if (!IsPeer(connection))
                {
                    Welcome(connection);
                }

                return;
            }

            if (message.Epoch != Epoch || !IsPeer(connection)) return;

            Process(connection.ClientId, message);
        }

        private bool IsPeer(NetworkConnection connection) =>
            _peers.GetValueOrDefault(connection.ClientId) == connection.Pointer;

        // Every player may send up to 8 requests per second.
        private bool TakeRequest(int clientId)
        {
            float now = Time.unscaledTime;
            var budget = _requests.GetValueOrDefault(clientId);
            if (now - budget.Time >= 1f)
            {
                budget = (now, 0);
            }

            if (budget.Count >= 8) return false;

            _requests[clientId] = (budget.Time, budget.Count + 1);
            return true;
        }

        // A new client gets the current sandbox: building objects, player effects, ragdolls and held objects.
        private void Welcome(NetworkConnection connection)
        {
            Hands.Disconnect(connection.ClientId);
            Physics.Disconnect(connection.ClientId);

            _snapshotBudgets.Remove(connection.ClientId);
            _peers[connection.ClientId] = connection.Pointer;

            Send(Message(SandboxKind.Ready), connection);

            foreach (BuildingObject state in Building.States)
            {
                Queue(Message(SandboxKind.Object) with { Object = state }, connection);
            }

            foreach (PlayerEffects state in _effects.Values)
            {
                Queue(Message(SandboxKind.Effects) with { Effects = state }, connection);
            }

            Ragdolls.Snapshot(connection);
            Living.Snapshot(connection);
            Hands.Snapshot(connection);
        }

        private void Process(int sender, SandboxMessage message)
        {
            string error;

            if (message.Kind == SandboxKind.Flight)
            {
                error = "The flight shortcut has been removed.";
            }
            else if (message.Kind == SandboxKind.Admin)
            {
                error = Administrator(sender) ? Execute(sender, message.Command) : "Only the host and trusted administrators can use Advanced.";
            }
            else if (message.Kind == SandboxKind.Escape)
            {
                error = Living.Escape(sender);
            }
            else if (message.Kind is SandboxKind.BatchEdit or SandboxKind.BatchDelete)
            {
                error = Building.RequestBatch(sender, message);
            }
            else if (message.Kind is SandboxKind.CarryState or SandboxKind.CarryGrab or SandboxKind.CarryRelease)
            {
                error = Hands.Request(sender, message);
            }
            else
            {
                error = Building.Request(sender, message);
            }

            if (sender == LocalId)
            {
                Status = error ?? "Done.";
                if (error != null)
                {
                    Building.RequestFailed();
                }
            }
            else if (error != null && _control.Manager.ServerManager.Clients.TryGetValue(sender, out var connection))
            {
                Send(Message(SandboxKind.Notice) with
                {
                    Data = error
                }, connection);
            }
        }

        // Clients receive the host's sandbox messages here.
        private void Receive(SandboxMessage message)
        {
            if (!Active || IsHost || message.Room != _room || message.RoomObject != _menu.NetworkObject.ObjectId) return;

            if (message.Kind == SandboxKind.Ready)
            {
                Connect(message.Epoch);
                return;
            }

            if (Epoch == null || message.Epoch != Epoch) return;

            switch (message.Kind)
            {
                case SandboxKind.Object:
                    Building.Apply(message.Object);
                    break;
                case SandboxKind.Delete:
                    Building.Remove(message.Id);
                    break;
                case SandboxKind.Clear:
                    Building.Clear();
                    break;
                case SandboxKind.Effects:
                    _effects[message.Effects.Id] = message.Effects;
                    break;
                case SandboxKind.Admin:
                    ApplyLocal(message.Command);
                    break;
                case SandboxKind.Notice:
                    Status = message.Data;
                    Building.RequestFailed();
                    break;
                case SandboxKind.Pose when message.Id < 1000000:
                    Building.Pose(message);
                    break;
                case SandboxKind.Pose:
                    Ragdolls.Pose(message);
                    break;
                case SandboxKind.Ragdoll:
                    Ragdolls.Define(message.Ragdoll);
                    break;
                case SandboxKind.RemoveRagdoll:
                    Ragdolls.Remove(message.Id);
                    break;
                case SandboxKind.PoseBatch:
                    Physics.Receive(message);
                    break;
                case SandboxKind.Living:
                    Living.Apply(message.Living);
                    break;
                case SandboxKind.CarryState:
                    Hands.Apply(message.Carry);
                    break;
            }
        }

        // A new epoch means the host restarted the sandbox, so everything we had from the old one is dropped.
        private void Connect(string epoch)
        {
            if (Epoch != epoch)
            {
                Hands.Clear();
                Living.Clear();
                Building.Clear();
                Ragdolls.Clear();
                Physics.Clear();
                _effects.Clear();
            }

            Epoch = epoch;
            Status = "NullFrame sandbox connected.";
        }

        // Runs an Advanced action that the host or an administrator asked for.
        private string Execute(int sender, SandboxCommand command)
        {
            if (command?.Action is PlayerCommand.Option or PlayerCommand.FriendlyFire or PlayerCommand.DisableAI or PlayerCommand.RemoveAllAI)
            {
                return ConsoleCommand(command);
            }

            if (command?.Action == PlayerCommand.Console)
            {
                SetConsole(command.Value > 0.5f);
                return null;
            }

            string error = TargetError(sender, command, out NetworkConnection target);
            if (error != null) return error;

            PlayerNetworkController avatar = Avatar(command.Target);
            switch (command.Action)
            {
                case PlayerCommand.Scale:
                case PlayerCommand.Fly:
                case PlayerCommand.Freeze:
                case PlayerCommand.Invincible:
                case PlayerCommand.Reset:
                    return ChangeEffects(command);
                case PlayerCommand.Rename:
                    if (string.IsNullOrWhiteSpace(command.Text)) return "Enter a nickname.";

                    _profiles.ForceNickname(command.Target, command.Text);
                    CommandTo(target, command);
                    return null;
                case PlayerCommand.Kill:
                case PlayerCommand.Heal:
                    return ChangeHealth(command, avatar);
                case PlayerCommand.Remove:
                case PlayerCommand.Respawn:
                    Despawn(command, avatar);
                    CommandTo(target, command);
                    return null;
                case PlayerCommand.Bring:
                case PlayerCommand.GoTo:
                    return Teleport(sender, command, avatar, target);
                case PlayerCommand.Launch:
                    CommandTo(target, command);
                    return null;
                default:
                    return "Unknown Advanced action.";
            }
        }

        private void SetConsole(bool enabled)
        {
            foreach (Volume volume in Volumes)
            {
                if (volume && volume.IsServerInitialized)
                {
                    volume.ABFHNIPOBBK(enabled ? 1 : 0);
                }
            }
        }

        private string TargetError(int sender, SandboxCommand command, out NetworkConnection target)
        {
            target = null;
            if (command == null) return "This player has left the room.";
            if (!_control.Manager.ServerManager.Clients.TryGetValue(command.Target, out target) || !target.IsAuthenticated)
            {
                return "This player has left the room.";
            }

            if (command.Target != sender && command.Target == 0 && sender != 0) return "Only the host can change the host's character.";
            if (command.Target != 0 && !_peers.ContainsKey(command.Target)) return "This action requires the updated NullFrame mod on the selected player.";

            return null;
        }

        private string ChangeEffects(SandboxCommand command)
        {
            PlayerEffects effects = Effects(command.Target);
            switch (command.Action)
            {
                case PlayerCommand.Scale:
                    if (command.Value is < 0.2f or > 5f) return "Player scale must be between 0.2 and 5.";

                    effects = effects with { Scale = command.Value };
                    break;
                case PlayerCommand.Fly:
                    effects = effects with { Fly = !effects.Fly };
                    break;
                case PlayerCommand.Freeze:
                    effects = effects with { Frozen = !effects.Frozen };
                    break;
                case PlayerCommand.Invincible:
                    effects = effects with { Invincible = !effects.Invincible };
                    break;
                case PlayerCommand.Reset:
                    effects = new PlayerEffects(command.Target);
                    _profiles.ForceNickname(command.Target, null);
                    break;
            }

            PublishEffects(effects);
            return null;
        }

        private void PublishEffects(PlayerEffects effects)
        {
            _effects[effects.Id] = effects;
            Send(Message(SandboxKind.Effects) with { Effects = effects });
        }

        private string ChangeHealth(SandboxCommand command, PlayerNetworkController avatar)
        {
            PlayerDamage damage = avatar ? avatar.Damage : null;
            if (!damage) return "This player does not have a living character.";

            if (command.Action == PlayerCommand.Heal)
            {
                damage.TakeDamage(damage.Health - 100f, null);
                return null;
            }

            // Invincibility is switched off first, or the kill would not land.
            PublishEffects(Effects(command.Target) with { Invincible = false });
            damage.TakeDamage(100000f, null);
            return null;
        }

        private static void Despawn(SandboxCommand command, PlayerNetworkController avatar)
        {
            if (avatar && avatar.IsServerInitialized && avatar.NetworkObject)
            {
                NetworkSpawner.Despawn(avatar.gameObject);
            }

            if (command.Action != PlayerCommand.Remove) return;

            PlayerInfo player = Object.FindObjectsOfType<PlayerInfo>().FirstOrDefault(value => value.IsServerInitialized && value.ClientId == command.Target);
            if (player)
            {
                player.SetTeam("Spectators");
            }
        }

        // Bring moves the target to the sender, Go to moves the sender to the target.
        private string Teleport(int sender, SandboxCommand command, PlayerNetworkController avatar, NetworkConnection target)
        {
            PlayerNetworkController source = Avatar(sender);
            if (!source || !avatar) return "Both players must have a character.";

            bool bring = command.Action == PlayerCommand.Bring;
            Transform destination = bring ? source.transform : avatar.transform;
            NetworkConnection receiver = bring ? target : _control.Manager.ServerManager.Clients[sender];
            Vector3 position = destination.position + destination.forward * 2f + Vector3.up;

            CommandTo(receiver, new SandboxCommand(PlayerCommand.Bring, receiver.ClientId, Position: Point(position)));
            return null;
        }

        private void CommandTo(NetworkConnection target, SandboxCommand command)
        {
            if (target.ClientId == LocalId)
            {
                ApplyLocal(command);
            }
            else
            {
                Send(Message(SandboxKind.Admin) with
                {
                    Command = command
                }, target);
            }
        }

        private void ApplyLocal(SandboxCommand command)
        {
            if (command == null || command.Target != LocalId)
            {
                return;
            }

            switch (command.Action)
            {
                case PlayerCommand.Rename:
                    _profiles.Save(command.Text, _profiles.Local.ColorHex);
                    break;
                case PlayerCommand.Remove:
                    _menu.SpawnPlayer("Spectators");
                    break;
                case PlayerCommand.Respawn:
                    string team = PlayerInfo.Local?.Team;
                    if (string.IsNullOrEmpty(team) || team == "Spectators")
                    {
                        team = "";
                    }

                    _menu.SpawnPlayer(team);
                    break;
                case PlayerCommand.Bring:
                    if (_movement && command.Position != null)
                    {
                        CharacterController controller = _movement.Controller;
                        bool enabled = controller && controller.enabled;

                        if (controller)
                        {
                            controller.enabled = false;
                        }

                        _movement.transform.position = Vector(command.Position);
                        if (controller)
                        {
                            controller.enabled = enabled;
                        }
                    }

                    break;
                case PlayerCommand.Launch:
                    _launch = Vector3.up * 16f;
                    break;
            }
        }

        internal void Delete(int id) => Submit(Message(SandboxKind.Delete) with
        {
            Id = id
        });

        internal static PlayerNetworkController Avatar(int id)
        {
            float age = Time.unscaledTime - _avatarAt;
            bool missing = !Avatars.TryGetValue(id, out var cached) || !cached;
            if (age >= 0.2f || missing && age >= 0.05f)
            {
                _avatarAt = Time.unscaledTime;
                Avatars.Clear();

                foreach (PlayerNetworkController player in Object.FindObjectsOfType<PlayerNetworkController>())
                {
                    if (player.IsClientInitialized && player.gameObject.activeInHierarchy)
                    {
                        Avatars.TryAdd(player.OwnerId, player);
                    }
                }
            }

            return Avatars.TryGetValue(id, out var avatar) && avatar ? avatar : null;
        }

        internal static GameObject Spawned(int objectId)
        {
            NetworkManager manager = NetBootstrap.Current ? NetBootstrap.Current.Network : null;
            return manager && manager.ClientManager.Objects.Spawned.TryGetValue(objectId, out var value) &&
                value && value.IsClientInitialized ? value.gameObject : null;
        }

        internal static Point3 Point(Vector3 value) => new(value.x, value.y, value.z);
        internal static Vector3 Vector(Point3 value) => new(value.X, value.Y, value.Z);
        internal static Rotation4 Rotation(Quaternion value) => new(value.x, value.y, value.z, value.w);
        internal static Quaternion Quaternion(Rotation4 value) => new(value.X, value.Y, value.Z, value.W);

        internal void Update()
        {
            RoomMultiplayerMenu menu = RoomMultiplayerMenu.Current;
            string room = _session.IsNullFrame && !_session.IsSwitching ? _control.Room?.Id : null;

            if (menu != _menu || room != _room)
            {
                ResetForRoom(menu, room);
            }

            if (!Active) return;

            if (IsHost && Epoch == null)
            {
                Epoch = Guid.NewGuid().ToString("N");
            }

            if (!IsHost && Epoch == null && Time.unscaledTime >= _helloAt)
            {
                _helloAt = Time.unscaledTime + 3f;
                _profiles.SendSandbox(Message(SandboxKind.Hello), toServer: true);
            }

            if (IsHost)
            {
                SendSnapshots();
                ForgetDisconnectedPeers();
            }

            if (Time.unscaledTime >= _scanAt)
            {
                _scanAt = Time.unscaledTime + 0.25f;
                ScaleAvatars();
                FindLocalMovement();
                Assets.Capture(Volumes);
            }

            MoveLocal();
            Assets.Update();
            Building.Update();
            Ragdolls.Update();
            Living.Update();
            Hands.Update();
            Physics.Update();
        }

        private void ResetForRoom(RoomMultiplayerMenu menu, string room)
        {
            RestoreMovement();
            foreach (var item in _scaled.Values)
            {
                if (item.Player)
                {
                    item.Player.transform.localScale = item.Scale;
                }
            }

            _scaled.Clear();
            _effects.Clear();
            _peers.Clear();
            _requests.Clear();
            _outgoing.Clear();
            _snapshotBudgets.Clear();
            Hands.Clear();
            Living.Clear();
            Building.Clear();
            Ragdolls.Clear();
            Physics.Clear();

            _menu = menu;
            _room = room;

            Epoch = IsHost && room != null ? Guid.NewGuid().ToString("N") : null;
            _helloAt = _scanAt = 0f;

            Status = null;
        }

        // Sends a few queued room snapshots to newly joined players each frame, within each player's traffic budget.
        private void SendSnapshots()
        {
            for (int index = 0; index < 4 && _outgoing.TryPeek(out var item); index++)
            {
                NetworkConnection connection = item.Connection;
                if (!connection.IsValid || _peers.GetValueOrDefault(connection.ClientId) != connection.Pointer)
                {
                    _outgoing.Dequeue();
                    continue;
                }

                SandboxMessage snapshot = CurrentSnapshot(item.Message);
                if (snapshot == null)
                {
                    _outgoing.Dequeue();
                    continue;
                }

                if (!_snapshotBudgets.TryGetValue(connection.ClientId, out TrafficBudget budget))
                {
                    budget = new TrafficBudget(16384, SandboxMessage.MaxBytes + 64);
                    _snapshotBudgets[connection.ClientId] = budget;
                }

                string json = SandboxMessage.Serialize(snapshot);
                if (!budget.Spend(System.Text.Encoding.UTF8.GetByteCount(json) + 32, Time.unscaledTime)) break;

                _outgoing.Dequeue();
                SendTo(connection, json);
            }
        }

        // A queued snapshot is refreshed with the object's latest state, or dropped when the object is gone.
        private SandboxMessage CurrentSnapshot(SandboxMessage snapshot)
        {
            if (snapshot.Kind == SandboxKind.Object)
            {
                BuildingObject current = Building.State(snapshot.Object.Id);
                return current == null ? null : snapshot with { Object = current };
            }

            if (snapshot.Kind == SandboxKind.Ragdoll && !Ragdolls.Contains(snapshot.Ragdoll.Id)) return null;

            return snapshot;
        }

        private void ForgetDisconnectedPeers()
        {
            foreach (int id in _peers.Keys.ToArray())
            {
                if (_control.Manager.ServerManager.Clients.TryGetValue(id, out var peer) && peer.Pointer == _peers[id]) continue;

                _peers.Remove(id);
                _requests.Remove(id);
                _snapshotBudgets.Remove(id);
                Hands.Disconnect(id);

                if (_effects.Remove(id))
                {
                    Send(Message(SandboxKind.Effects) with { Effects = new PlayerEffects(id) });
                }
            }
        }

        private void ScaleAvatars()
        {
            foreach (PlayerNetworkController avatar in Object.FindObjectsOfType<PlayerNetworkController>())
            {
                if (!avatar.IsClientInitialized) continue;

                if (!_scaled.TryGetValue(avatar.Pointer, out var item))
                {
                    item = (avatar, avatar.transform.localScale);
                    _scaled[avatar.Pointer] = item;
                }

                avatar.transform.localScale = item.Scale * Effects(avatar.OwnerId).Scale;
            }

            foreach (IntPtr pointer in _scaled.Where(pair => !pair.Value.Player).Select(pair => pair.Key).ToArray())
            {
                _scaled.Remove(pointer);
            }
        }

        private void FindLocalMovement()
        {
            FPScontroller movement = Object.FindObjectsOfType<FPScontroller>().FirstOrDefault(value => value.gameObject.activeInHierarchy);
            if (movement == _movement) return;

            RestoreMovement();
            _movement = movement;
            if (!movement) return;

            _movementScale = movement.transform.localScale;
            _movementGravity = movement.Movement?.gravity ?? 20f;
            _controllerEnabled = movement.Controller && movement.Controller.enabled;
        }

        // Applies the room's gravity, admin effects, flight and prone to the local player's own movement.
        private void MoveLocal()
        {
            if (!_movement) return;

            if (_movement.Movement != null)
            {
                float gravityScale = Environment.World == null ? 1f : -Environment.World.Gravity.Y / 9.81f;
                _movement.Movement.gravity = _movementGravity * gravityScale;
            }

            ApplyLaunch();

            PlayerEffects effects = Effects(LocalId);
            _movement.transform.localScale = _movementScale * effects.Scale;
            UpdateFlightToggle();

            bool flying = Flying(effects);
            bool ghost = flying && (effects.Noclip || Environment.FlightNoclip);
            CharacterController controller = _movement.Controller;

            // Flying through objects needs the character controller switched off.
            if (controller && (flying != _flying || ghost != _ghost))
            {
                _flying = flying;
                _ghost = ghost;
                controller.enabled = ghost ? false : _controllerEnabled;
            }

            if (!Environment.AllowProne && _movement.Prone)
            {
                _movement.Prone = false;
            }

            if (effects.Frozen || _menu.IsPaused || Cursor.lockState != CursorLockMode.Locked) return;

            if (!flying && Environment.AllowProne && Input.GetKeyDown(KeyCode.C) && !Typing())
            {
                ToggleProne();
            }

            if (flying)
            {
                Fly(controller, ghost);
            }
        }

        // An admin "Launch" throws the player straight up.
        private void ApplyLaunch()
        {
            if (_launch.sqrMagnitude <= 0.01f || _movement.Movement == null) return;

            Vector3 velocity = _movement.Movement.velocity;
            velocity.y = _launch.y;
            _movement.Movement.velocity = velocity;
            _movement.Grounded = false;

            if (_movement.Jumping != null)
            {
                _movement.Jumping.jumping = true;
                _movement.Jumping.jumpDir = Vector3.up;
                _movement.Jumping.lastStartTime = Time.time;
            }

            _launch = Vector3.zero;
        }

        private void UpdateFlightToggle()
        {
            if (!Environment.AllowFlight)
            {
                _selfFlight = false;
                return;
            }

            bool canToggle = !Typing() && !_menu.IsPaused && Cursor.lockState == CursorLockMode.Locked && !Living.IsDown(LocalId);
            if (canToggle && Input.GetKeyDown(KeyCode.X))
            {
                _selfFlight = !_selfFlight;
            }
        }

        private void ToggleProne()
        {
            if (_movement.Movement != null)
            {
                _movement.Movement.canProne = true;
                _movement.Movement.proneHeight = 0.8f;
            }

            _movement.Prone = !_movement.Prone;
        }

        // WASD moves along the camera, Space and Ctrl move up and down, Shift flies 2.5 times faster.
        private void Fly(CharacterController controller, bool ghost)
        {
            Camera camera = Camera.main;
            if (!camera) return;

            float forward = (Input.GetKey(KeyCode.W) ? 1 : 0) - (Input.GetKey(KeyCode.S) ? 1 : 0);
            float right = (Input.GetKey(KeyCode.D) ? 1 : 0) - (Input.GetKey(KeyCode.A) ? 1 : 0);
            float up = (Input.GetKey(KeyCode.Space) ? 1 : 0) - (Input.GetKey(KeyCode.LeftControl) ? 1 : 0);
            Vector3 direction = camera.transform.forward * forward + camera.transform.right * right + Vector3.up * up;

            float speed = Environment.FlightSpeed * (Input.GetKey(KeyCode.LeftShift) ? 2.5f : 1f);
            Vector3 delta = direction.normalized * speed * Mathf.Min(Time.unscaledDeltaTime, 0.05f);

            if (ghost || !controller || !controller.enabled)
            {
                _movement.transform.position += delta;
            }
            else
            {
                controller.Move(delta);
            }
        }

        private bool Flying(PlayerEffects effects) => effects.Fly || effects.Noclip || _selfFlight && Environment.AllowFlight;

        private void RestoreMovement()
        {
            if (_movement)
            {
                _movement.transform.localScale = _movementScale;
                if (_movement.Movement != null)
                {
                    _movement.Movement.gravity = _movementGravity;
                }

                if (_movement.Controller)
                {
                    _movement.Controller.enabled = _controllerEnabled;
                }
            }

            _movement = null;
            _flying = _ghost = _selfFlight = false;
            _launch = Vector3.zero;
        }

        internal static bool BeforeLook(FPSMouseLook __instance)
        {
            SandboxWorld world = _instance;
            if (!world.Active || !(world.Hands.Rotating || world.Living.IsDown(world.LocalId))) return true;

            FPScontroller controller = __instance.GetComponentInParent(Il2CppType.Of<FPScontroller>())?.Cast<FPScontroller>();
            return !controller || controller != world._movement;
        }

        internal static bool BeforeMovement(FPScontroller __instance)
        {
            SandboxWorld world = _instance;
            if (!world.Active || __instance != world._movement) return true;

            PlayerEffects effects = world.Effects(world.LocalId);
            return !(world.Flying(effects) || effects.Frozen || world.Hands.Rotating || world.Living.IsDown(world.LocalId));
        }

        internal static bool BeforeFixedMovement(FPScontroller __instance)
        {
            SandboxWorld world = _instance;
            bool allowed = BeforeMovement(__instance);

            if (allowed && world.Active && __instance == world._movement && world.Environment.World is { } settings && __instance.Movement != null)
            {
                Vector3 velocity = __instance.Movement.velocity;
                velocity += new Vector3(settings.Gravity.X, 0f, settings.Gravity.Z) * Time.fixedDeltaTime;
                __instance.Movement.velocity = velocity;
            }

            return allowed;
        }

        internal static int DamageOwner(PlayerDamage damage)
        {
            if (damage.IsSpawned && damage.OwnerId >= 0)
            {
                return damage.OwnerId;
            }

            return Object.FindObjectsOfType<PlayerNetworkController>().FirstOrDefault(player => player.Damage == damage)?.OwnerId ?? -1;
        }

        internal static bool Typing()
        {
            MultiplayerChat chat = MultiplayerChat.Current;
            if (chat && chat.Input && chat.Input.isFocused) return true;

            GameObject selected = UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject;
            UnityEngine.UI.InputField field = selected ? selected.GetComponent(Il2CppType.Of<UnityEngine.UI.InputField>())?.Cast<UnityEngine.UI.InputField>() : null;
            return field && field.isFocused;
        }

        internal static bool BeforeDamage(PlayerDamage __instance, float __0)
        {
            return __0 <= 0f || !_instance.Active ||
                !_instance.Effects(DamageOwner(__instance)).Invincible;
        }
    }
}
