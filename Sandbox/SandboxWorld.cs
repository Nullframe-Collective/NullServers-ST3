using HarmonyLib;
using Il2Cpp;
using Il2CppFishNet.Connection;
using Il2CppFishNet.Managing;
using Il2CppFishNet.Transporting;
using Il2CppInterop.Runtime;
using NullServers.Additional;
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
        private bool _flying;
        private string _room;
        private float _helloAt;
        private float _scanAt;
        private Vector3 _launch;
        private float _movementGravity;

        private static readonly Dictionary<int, PlayerNetworkController> Avatars = new();
        private static int _avatarFrame = -1;
        private Volume[] _volumes = Array.Empty<Volume>();
        private int _volumeFrame = -1;

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
                if (_volumeFrame != Time.frameCount)
                {
                    _volumeFrame = Time.frameCount;
                    _volumes = Object.FindObjectsOfType<Volume>().ToArray();
                }

                return _volumes;
            }
        }

        internal bool ConsoleEnabled => Active && Volumes.Any(volume => volume && volume.IsClientInitialized && volume.ConsoleState?.Value == 1);

        internal FPScontroller LocalMovement => _movement ? _movement : null;

        internal SandboxWorld(HarmonyLib.Harmony harmony, ServerControl control, PlayerProfiles profiles, RelaySession session)
        {
            _instance = this;
            _control = control;
            _profiles = profiles;
            _session = session;

            Building = new SandboxBuilding(this);
            Ragdolls = new RagdollSync(harmony, this);
            Physics = new PhysicsReplication(this);
            Living = new LivingRagdolls(this);
            Hands = new Hands(this);

            profiles.SandboxReceived += ReceiveRequest;
            profiles.SandboxReceivedFromHost += Receive;

            harmony.Patch(
                AccessTools.Method(typeof(FPScontroller), nameof(FPScontroller.Update)),
                prefix: new HarmonyMethod(typeof(SandboxWorld), nameof(BeforeMovement)));

            harmony.Patch(
                AccessTools.Method(typeof(FPScontroller), nameof(FPScontroller.FixedUpdate)),
                prefix: new HarmonyMethod(typeof(SandboxWorld), nameof(BeforeFixedMovement)));

            harmony.Patch(
                AccessTools.Method(typeof(FPSMouseLook), nameof(FPSMouseLook.Update)),
                prefix: new HarmonyMethod(typeof(SandboxWorld), nameof(BeforeLook)));

            harmony.Patch(
                AccessTools.Method(typeof(PlayerDamage), nameof(PlayerDamage.KDDDGBFEFOC)),
                prefix: new HarmonyMethod(typeof(SandboxWorld), nameof(BeforeDamage)));
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

        private void ReceiveRequest(NetworkConnection connection, SandboxMessage message)
        {
            if (!Active ||
                !IsHost ||
                !connection.IsValid ||
                !connection.IsAuthenticated ||
                message.Room != _room ||
                message.RoomObject != _menu.NetworkObject.ObjectId)
            {
                return;
            }

            float now = Time.unscaledTime;
            if (message.Kind == SandboxKind.CarryAim)
            {
                if (message.Epoch == Epoch && _peers.GetValueOrDefault(connection.ClientId) == connection.Pointer)
                {
                    Hands.Aim(connection.ClientId, message.Carry);
                }

                return;
            }

            if (message.Kind == SandboxKind.Escape)
            {
                if (message.Epoch == Epoch && _peers.GetValueOrDefault(connection.ClientId) == connection.Pointer)
                {
                    Living.Escape(connection.ClientId);
                }

                return;
            }

            var budget = _requests.GetValueOrDefault(connection.ClientId);
            if (now - budget.Time >= 1f)
            {
                budget = (now, 0);
            }

            if (budget.Count >= 8) return;

            _requests[connection.ClientId] = (budget.Time, budget.Count + 1);
            if (message.Kind == SandboxKind.Hello)
            {
                if (_peers.GetValueOrDefault(connection.ClientId) == connection.Pointer)
                {
                    return;
                }

                Hands.Disconnect(connection.ClientId);
                Physics.Disconnect(connection.ClientId);

                _snapshotBudgets.Remove(connection.ClientId);
                _peers[connection.ClientId] = connection.Pointer;

                Send(Message(SandboxKind.Ready), connection);

                foreach (BuildingObject state in Building.States)
                {
                    Queue(Message(SandboxKind.Object) with
                    {
                        Object = state
                    }, connection);
                }

                foreach (PlayerEffects state in _effects.Values)
                {
                    Queue(Message(SandboxKind.Effects) with
                    {
                        Effects = state
                    }, connection);
                }

                Ragdolls.Snapshot(connection);
                Living.Snapshot(connection);
                Hands.Snapshot(connection);

                return;
            }

            if (message.Epoch != Epoch || _peers.GetValueOrDefault(connection.ClientId) != connection.Pointer)
            {
                return;
            }

            Process(connection.ClientId, message);
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

        private void Receive(SandboxMessage message)
        {
            if (!Active || IsHost || message.Room != _room || message.RoomObject != _menu.NetworkObject.ObjectId)
            {
                return;
            }

            if (message.Kind == SandboxKind.Ready)
            {
                if (Epoch != message.Epoch)
                {
                    Hands.Clear();
                    Living.Clear();
                    Building.Clear();
                    Ragdolls.Clear();
                    Physics.Clear();
                    _effects.Clear();
                }

                Epoch = message.Epoch;
                Status = "NullFrame sandbox connected.";

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
                case SandboxKind.Pose:
                    if (message.Id < 1000000)
                    {
                        Building.Pose(message);
                    }
                    else
                    {
                        Ragdolls.Pose(message);
                    }

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

        private string Execute(int sender, SandboxCommand command)
        {
            if (command == null ||
                !_control.Manager.ServerManager.Clients.TryGetValue(command.Target, out var target) ||
                !target.IsAuthenticated)
            {
                return "This player has left the room.";
            }

            if (command.Target != sender && command.Target == 0 && sender != 0)
            {
                return "Only the host can change the host's character.";
            }

            if (command.Target != 0 && !_peers.ContainsKey(command.Target))
            {
                return "This action requires the updated NullFrame mod on the selected player.";
            }

            PlayerEffects current = Effects(command.Target);
            PlayerNetworkController avatar = Avatar(command.Target);

            switch (command.Action)
            {
                case PlayerCommand.Scale:
                    if (command.Value is < 0.2f or > 5f)
                    {
                        return "Player scale must be between 0.2 and 5.";
                    }

                    current = current with
                    {
                        Scale = command.Value
                    };
                    break;
                case PlayerCommand.Fly:
                    current = current with
                    {
                        Fly = !current.Fly
                    };
                    break;
                case PlayerCommand.Freeze:
                    current = current with
                    {
                        Frozen = !current.Frozen
                    };
                    break;
                case PlayerCommand.Invincible:
                    current = current with
                    {
                        Invincible = !current.Invincible
                    };
                    break;
                case PlayerCommand.Reset:
                    current = new PlayerEffects(command.Target);
                    _profiles.ForceNickname(command.Target, null);
                    break;
                case PlayerCommand.Rename:
                    if (string.IsNullOrWhiteSpace(command.Text))
                    {
                        return "Enter a nickname.";
                    }

                    _profiles.ForceNickname(command.Target, command.Text);
                    CommandTo(target, command);
                    return null;
                case PlayerCommand.Kill:
                case PlayerCommand.Heal:
                    PlayerDamage damage = avatar ? avatar.Damage : null;
                    if (!damage)
                    {
                        return "This player does not have a living character.";
                    }

                    if (command.Action == PlayerCommand.Kill)
                    {
                        _effects[command.Target] = current with
                        {
                            Invincible = false
                        };

                        Send(Message(SandboxKind.Effects) with
                        {
                            Effects = _effects[command.Target]
                        });

                        damage.TakeDamage(100000f, null);
                    }
                    else
                    {
                        damage.TakeDamage(damage.Health - 100f, null);
                    }

                    return null;
                case PlayerCommand.Remove:
                case PlayerCommand.Respawn:
                    if (avatar && avatar.IsServerInitialized && avatar.NetworkObject)
                    {
                        NetworkSpawner.Despawn(avatar.gameObject);
                    }

                    if (command.Action == PlayerCommand.Remove)
                    {
                        PlayerInfo player = Object.FindObjectsOfType<PlayerInfo>().FirstOrDefault(value => value.IsServerInitialized &&
                            value.ClientId == command.Target);
                        if (player)
                        {
                            player.SetTeam("Spectators");
                        }
                    }

                    CommandTo(target, command);
                    return null;
                case PlayerCommand.Bring:
                case PlayerCommand.GoTo:
                    PlayerNetworkController source = Avatar(sender);
                    if (!source || !avatar)
                    {
                        return "Both players must have a character.";
                    }

                    Transform destination = command.Action == PlayerCommand.Bring ? source.transform : avatar.transform;
                    NetworkConnection receiver = command.Action == PlayerCommand.Bring ? target : _control.Manager.ServerManager.Clients[sender];
                    CommandTo(
                        receiver,
                        new SandboxCommand(
                            PlayerCommand.Bring,
                            receiver.ClientId,
                            Position: Point(destination.position + destination.forward * 2f + Vector3.up)));
                    return null;
                case PlayerCommand.Launch:
                    CommandTo(target, command);
                    return null;
                default:
                    return "Unknown Advanced action.";
            }

            _effects[command.Target] = current;
            Send(Message(SandboxKind.Effects) with
            {
                Effects = current
            });

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
            if (_avatarFrame != Time.frameCount)
            {
                _avatarFrame = Time.frameCount;
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
                for (int index = 0; index < 4 && _outgoing.TryPeek(out var item); index++)
                {
                    if (!item.Connection.IsValid || _peers.GetValueOrDefault(item.Connection.ClientId) != item.Connection.Pointer)
                    {
                        _outgoing.Dequeue();
                        continue;
                    }

                    SandboxMessage snapshot = item.Message;
                    if (snapshot.Kind == SandboxKind.Object)
                    {
                        BuildingObject current = Building.State(snapshot.Object.Id);
                        if (current == null)
                        {
                            _outgoing.Dequeue();
                            continue;
                        }

                        snapshot = snapshot with
                        {
                            Object = current
                        };
                    }

                    if (snapshot.Kind == SandboxKind.Ragdoll && !Ragdolls.Contains(snapshot.Ragdoll.Id))
                    {
                        _outgoing.Dequeue();
                        continue;
                    }

                    if (!_snapshotBudgets.TryGetValue(item.Connection.ClientId, out var budget))
                    {
                        _snapshotBudgets[item.Connection.ClientId] = budget = new TrafficBudget(16384, SandboxMessage.MaxBytes + 64);
                    }

                    string json = SandboxMessage.Serialize(snapshot);
                    if (!budget.Spend(System.Text.Encoding.UTF8.GetByteCount(json) + 32, Time.unscaledTime))
                    {
                        break;
                    }

                    _outgoing.Dequeue();
                    SendTo(item.Connection, json);
                }

                foreach (int id in _peers.Keys.ToArray())
                {
                    if (!_control.Manager.ServerManager.Clients.TryGetValue(id, out var peer) || peer.Pointer != _peers[id])
                    {
                        _peers.Remove(id);
                        _requests.Remove(id);
                        _snapshotBudgets.Remove(id);
                        Hands.Disconnect(id);

                        if (_effects.Remove(id))
                        {
                            Send(Message(SandboxKind.Effects) with
                            {
                                Effects = new PlayerEffects(id)
                            });
                        }
                    }
                }
            }

            if (Time.unscaledTime >= _scanAt)
            {
                _scanAt = Time.unscaledTime + 0.25f;

                foreach (PlayerNetworkController avatar in Object.FindObjectsOfType<PlayerNetworkController>())
                {
                    if (!avatar.IsClientInitialized) continue;

                    if (!_scaled.TryGetValue(avatar.Pointer, out var item))
                    {
                        _scaled[avatar.Pointer] = item = (avatar, avatar.transform.localScale);
                    }

                    avatar.transform.localScale = item.Scale * Effects(avatar.OwnerId).Scale;
                }

                foreach (IntPtr pointer in _scaled.Where(pair => !pair.Value.Player).Select(pair => pair.Key).ToArray())
                {
                    _scaled.Remove(pointer);
                }

                FPScontroller movement = Object.FindObjectsOfType<FPScontroller>().FirstOrDefault(value => value.gameObject.activeInHierarchy);
                if (movement != _movement)
                {
                    RestoreMovement();
                    _movement = movement;

                    if (movement)
                    {
                        _movementScale = movement.transform.localScale;
                        _movementGravity = movement.Movement?.gravity ?? 20f;
                        _controllerEnabled = movement.Controller && movement.Controller.enabled;
                    }
                }

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

        private void MoveLocal()
        {
            if (!_movement) return;

            if (_movement.Movement != null)
            {
                _movement.Movement.gravity = Environment.World == null ? _movementGravity : _movementGravity * (-Environment.World.Gravity.Y / 9.81f);
            }

            if (_launch.sqrMagnitude > 0.01f && _movement.Movement != null)
            {
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

            PlayerEffects effects = Effects(LocalId);
            _movement.transform.localScale = _movementScale * effects.Scale;
            CharacterController controller = _movement.Controller;
            bool flying = effects.Fly || effects.Noclip;

            if (controller && flying != _flying)
            {
                _flying = flying;
                controller.enabled = _flying ? false : _controllerEnabled;
            }

            if (effects.Frozen || _menu.IsPaused || Cursor.lockState != CursorLockMode.Locked)
            {
                return;
            }

            if (flying)
            {
                Camera camera = Camera.main;
                if (!camera) return;

                Vector3 direction = camera.transform.forward * ((Input.GetKey(KeyCode.W) ? 1 : 0) - (Input.GetKey(KeyCode.S) ? 1 : 0)) +
                    camera.transform.right * ((Input.GetKey(KeyCode.D) ? 1 : 0) - (Input.GetKey(KeyCode.A) ? 1 : 0)) +
                    Vector3.up * ((Input.GetKey(KeyCode.Space) ? 1 : 0) - (Input.GetKey(KeyCode.LeftControl) ? 1 : 0));

                _movement.transform.position += direction.normalized * (Input.GetKey(KeyCode.LeftShift) ? 18 : 7) * Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            }
        }

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
            _flying = false;
            _launch = Vector3.zero;
        }

        private static bool BeforeLook(FPSMouseLook __instance)
        {
            SandboxWorld world = _instance;
            if (!world.Active || !(world.Hands.Rotating || world.Living.IsDown(world.LocalId))) return true;

            FPScontroller controller = __instance.GetComponentInParent(Il2CppType.Of<FPScontroller>())?.Cast<FPScontroller>();
            return !controller || controller != world._movement;
        }

        private static bool BeforeMovement(FPScontroller __instance)
        {
            SandboxWorld world = _instance;
            if (!world.Active || __instance != world._movement) return true;

            PlayerEffects effects = world.Effects(world.LocalId);
            return !(effects.Fly || effects.Noclip || effects.Frozen || world.Hands.Rotating || world.Living.IsDown(world.LocalId));
        }

        private static bool BeforeFixedMovement(FPScontroller __instance)
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

        private static bool BeforeDamage(PlayerDamage __instance, float __0)
        {
            return __0 <= 0f || !_instance.Active ||
                !_instance.Effects(DamageOwner(__instance)).Invincible;
        }
    }
}
