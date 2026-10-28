using Il2Cpp;
using Il2CppFishNet.Connection;
using Il2CppInterop.Runtime;
using NullServers.Network;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NullServers.Sandbox
{
    internal sealed class Hands
    {
        private sealed class Grip
        {
            internal CarryState State;
            internal Rigidbody Body;
            internal RigidGrip Constraint;
            internal Vector3 Target;
            internal Quaternion Rotation;
            internal float AimAt;
            internal int Sequence;
            internal bool Building;
        }

        private readonly SandboxWorld _world;
        private readonly Dictionary<int, Grip> _grips = new();
        private readonly Dictionary<IntPtr, (Renderer Renderer, bool Enabled)> _hidden = new();
        private readonly Dictionary<IntPtr, (WeaponManager Manager, bool Enabled)> _managers = new();
        private bool _building;
        private bool _releasePending;
        private bool _waitForButtons;
        private int _releaseFrame = -1;
        private int _sequence;
        private float _aimAt, _allowedAt, _distance = 2.5f;
        private Quaternion _rotation = Quaternion.identity;
        private Vector3 _localTarget;
        private bool _hasAim;

        internal int Holding => _grips.GetValueOrDefault(_world.LocalId)?.State.Holding ?? 0;
        internal bool Equipped => _building || Holding > 0 || _world.Living.IsDown(_world.LocalId);
        internal bool UsingTool => Equipped || _waitForButtons;
        internal bool Rotating => Holding > 0 && Input.GetKey(KeyCode.R);
        internal IEnumerable<int> HeldObjects => _grips.Values.Where(grip => grip.State.Holding > 0).Select(grip => grip.State.Holding);

        internal Hands(SandboxWorld world)
        {
            _world = world;
        }

        internal void Equip(bool enabled)
        {
            if (enabled && (!_world.Active || !_world.ConsoleEnabled && !_world.IsAdmin))
            {
                return;
            }

            _building = enabled;
            _world.Submit(_world.Message(SandboxKind.CarryState) with
            {
                Carry = new CarryState(_world.LocalId, enabled, Building: enabled)
            });
            if (!enabled)
            {
                _world.Building.Stop();
                RequestRelease();
            }
        }

        internal string Request(int sender, SandboxMessage message)
        {
            CarryState request = message.Carry;
            if (request?.Valid != true)
            {
                return "Invalid carry request.";
            }

            if (message.Kind == SandboxKind.CarryRelease)
            {
                Release(sender, request.Throw, request.Freeze);
                return null;
            }

            if (message.Kind == SandboxKind.CarryState && !request.Equipped)
            {
                Release(sender, false, false);
                _grips[sender] = new Grip { State = new CarryState(sender, false) };
                _world.Send(_world.Message(SandboxKind.CarryState) with
                {
                    Carry = _grips[sender].State
                });
                return null;
            }

            CarryTarget permission = message.Kind == SandboxKind.CarryState ? CarryTarget.Prop : request.Target;
            if (!CarryRules.Allowed(_world.ConsoleEnabled, _world.Administrator(sender), _world.Environment.ThrowAnyone, permission))
            {
                return "The host must enable the sandbox console or Throw Any One first.";
            }

            if (_world.Living.IsDown(sender))
            {
                return "You cannot carry an object while ragdolled.";
            }

            PlayerNetworkController actor = SandboxWorld.Avatar(sender);
            if (!actor || !actor.Damage || actor.Damage.Health <= 0f)
            {
                return "A living character is required.";
            }

            if (message.Kind == SandboxKind.CarryState)
            {
                Release(sender, false, false);
                _grips[sender] = new Grip { State = new CarryState(sender, true, Building: true), Building = true };
                _world.Send(_world.Message(SandboxKind.CarryState) with
                {
                    Carry = _grips[sender].State
                });
                return null;
            }

            if (message.Kind != SandboxKind.CarryGrab)
            {
                return "Unknown carry request.";
            }

            if (request.Position == null)
            {
                return "Aim at a nearby object.";
            }

            Vector3 point = SandboxWorld.Vector(request.Position);
            float reach = CarryRules.Reach * _world.Effects(sender).Scale;

            if (Vector3.Distance(actor.transform.position + Vector3.up * _world.Effects(sender).Scale, point) > reach + 1f)
            {
                return "Move closer to the object.";
            }

            int id = message.Id;
            bool createdLiving = false;
            string Failed(string error)
            {
                if (createdLiving)
                {
                    _world.Living.Cancel(id);
                }

                return error;
            }

            CarryTarget kind = request.Target;
            GameObject root;

            if (kind is CarryTarget.Player or CarryTarget.NPC)
            {
                string error = _world.Living.Take(sender, kind, id, point, out id);
                if (error != null)
                {
                    return error;
                }

                createdLiving = true;
                kind = CarryTarget.Ragdoll;
            }

            if (kind == CarryTarget.Prop)
            {
                BuildingObject state = _world.Building.State(id);
                if (state == null || !SandboxMessage.CanEdit(state.Owner, sender, _world.Administrator(sender)))
                {
                    return "You can carry your own props; administrators can carry all props.";
                }

                root = _world.Building.Root(id);
                if (!state.Dynamic)
                {
                    string error = _world.Building.Request(sender, _world.Message(SandboxKind.Edit) with
                    {
                        Object = state with
                        {
                            Dynamic = true
                        }
                    });
                    if (error != null)
                    {
                        return error;
                    }
                }
            }
            else
            {
                root = _world.Ragdolls.Root(id);
            }

            if (!root)
            {
                return Failed("This object is no longer available.");
            }

            Bounds bounds = SandboxAssets.BoundsOf(root);
            bounds.Expand(0.5f);

            if (!bounds.Contains(point))
            {
                return Failed("Aim at the object's surface.");
            }

            if (_grips.Any(pair => pair.Key != sender && pair.Value.State.Holding == id))
            {
                return Failed("Someone is already holding this object.");
            }

            Rigidbody body = kind == CarryTarget.Prop ? root.GetComponent<Rigidbody>() : _world.Ragdolls.PhysicsBody(id, request.Bone);
            if (!body)
            {
                return Failed("This object has no physics body.");
            }

            Release(sender, false, false);
            body.isKinematic = false;
            body.WakeUp();
            bool building = _grips.GetValueOrDefault(sender)?.Building == true;
            CarryState stateNow = new(sender, true, id, SandboxWorld.Point(point), SandboxWorld.Rotation(body.rotation),
                Grip: SandboxWorld.Point(body.transform.InverseTransformPoint(point)), Target: kind, Bone: request.Bone, Building: building);
            IEnumerable<Collider> holder = actor.GetComponentsInChildren<Collider>(true);
            if (sender == _world.LocalId)
            {
                holder = holder.Concat(Object.FindObjectsOfType<FPScontroller>().SelectMany(value => value.GetComponentsInChildren<Collider>(true)));
            }

            _grips[sender] = new Grip
            {
                State = stateNow,
                Body = body,
                Target = point,
                Rotation = body.rotation,
                AimAt = Time.unscaledTime,
                Building = building,
                Constraint = new RigidGrip(body, root, holder.ToArray())
            };
            if (sender == _world.LocalId)
            {
                _rotation = body.rotation;
                _sequence = 0;
                _releasePending = false;
            }

            _world.Living.Held(id, sender);
            _world.Send(_world.Message(SandboxKind.CarryState) with
            {
                Carry = stateNow
            });
            return null;
        }

        internal void Aim(int sender, CarryState state)
        {
            if (state?.Valid != true || state.Position == null || state.Rotation == null ||
                !_grips.TryGetValue(sender, out var grip) || grip.State.Holding == 0 || state.Sequence <= grip.Sequence)
            {
                return;
            }

            PlayerNetworkController actor = SandboxWorld.Avatar(sender);
            if (!actor)
            {
                return;
            }

            Vector3 point = SandboxWorld.Vector(state.Position);
            if (Vector3.Distance(point, actor.transform.position) > 7f * _world.Effects(sender).Scale)
            {
                return;
            }

            grip.Target = point;
            grip.Rotation = SandboxWorld.Quaternion(state.Rotation).normalized;
            grip.AimAt = Time.unscaledTime;
            grip.Sequence = state.Sequence;
            grip.State = grip.State with
            {
                Position = state.Position,
                Rotation = state.Rotation,
                Rotate = state.Rotate
            };
        }

        internal void Apply(CarryState state)
        {
            _grips[state.Player] = new Grip { State = state };
            if (state.Player == _world.LocalId)
            {
                _releasePending = false;
                _hasAim = false;

                if (state.Rotation != null)
                {
                    _rotation = SandboxWorld.Quaternion(state.Rotation);
                }
            }
        }

        internal void Release(int player, bool throwing, bool freeze)
        {
            if (!_grips.TryGetValue(player, out var grip) || grip.State.Holding == 0)
            {
                return;
            }

            CarryState previous = grip.State;
            int id = previous.Holding;
            grip.Constraint?.Dispose();
            grip.Constraint = null;

            if (grip.Body)
            {
                PlayerNetworkController actor = SandboxWorld.Avatar(player);
                Vector3 direction = actor ? (grip.Target - actor.transform.position - Vector3.up).normalized : Vector3.up;

                if (direction.sqrMagnitude < 0.1f)
                {
                    direction = actor ? actor.transform.forward : Vector3.up;
                }

                if (previous.Target == CarryTarget.Ragdoll)
                {
                    _world.Ragdolls.Throw(id, throwing ? direction * 18f : Vector3.zero);
                }
                else
                {
                    grip.Body.velocity = throwing ? direction * 18f : Vector3.zero;
                    grip.Body.angularVelocity = Vector3.zero;

                    BuildingObject state = _world.Building.State(id);
                    if (state != null)
                    {
                        _world.Building.Request(player, _world.Message(SandboxKind.Edit) with
                        {
                            Object = state with
                            {
                                Position = SandboxWorld.Point(grip.Body.position),
                                Rotation = SandboxWorld.Rotation(grip.Body.rotation),
                                Dynamic = !freeze
                            }
                        });
                    }
                }
            }

            grip.Body = null;
            grip.State = new CarryState(player, grip.Building, Building: grip.Building);
            _world.Living.Released(id);

            if (player == _world.LocalId)
            {
                _releasePending = false;
            }

            _world.Send(_world.Message(SandboxKind.CarryState) with
            {
                Carry = grip.State
            });
        }

        internal void Removed(int id)
        {
            foreach (int player in _grips.Where(pair => pair.Value.State.Holding == id).Select(pair => pair.Key).ToArray())
            {
                Release(player, false, false);
            }
        }

        internal void Disconnect(int player)
        {
            Release(player, false, false);
            _grips.Remove(player);
            _world.Living.Disconnect(player);
        }

        internal void Snapshot(NetworkConnection connection)
        {
            foreach (Grip grip in _grips.Values.Where(grip => grip.State.Equipped))
            {
                _world.Queue(_world.Message(SandboxKind.CarryState) with
                {
                    Carry = grip.State
                }, connection);
            }
        }

        private void RequestRelease(bool throwing = false, bool freeze = false)
        {
            if (_releasePending || Holding == 0)
            {
                return;
            }

            _releasePending = true;
            _waitForButtons = true;
            _releaseFrame = Time.frameCount;
            _world.Submit(_world.Message(SandboxKind.CarryRelease) with
            {
                Carry = new CarryState(_world.LocalId, true, Throw: throwing, Freeze: freeze)
            });
        }

        internal void Update()
        {
            if (!_world.Active)
            {
                return;
            }

            if (_waitForButtons && Time.frameCount > _releaseFrame &&
                !Input.GetMouseButton(0) && !Input.GetMouseButton(1) &&
                !Input.GetMouseButtonUp(0) && !Input.GetMouseButtonUp(1))
            {
                _waitForButtons = false;
            }

            bool allowed = Cursor.lockState == CursorLockMode.Locked && !SandboxWorld.Typing();
            if (!allowed)
            {
                if (Holding > 0 && Time.unscaledTime - _allowedAt > 0.5f)
                {
                    RequestRelease();
                }

                return;
            }

            _allowedAt = Time.unscaledTime;
            Camera camera = Camera.main;

            if (!camera || _world.Living.IsDown(_world.LocalId))
            {
                return;
            }

            if (Holding > 0)
            {
                if (_releasePending)
                {
                    return;
                }

                if (Input.GetKeyDown(KeyCode.E) || Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1))
                {
                    RequestRelease(Input.GetMouseButtonDown(0), Input.GetMouseButtonDown(1));
                    return;
                }

                _distance = Mathf.Clamp(_distance + Input.GetAxis("Mouse ScrollWheel") * 2f, 1f, 4f);
                if (Rotating)
                {
                    _rotation = Quaternion.AngleAxis(Input.GetAxis("Mouse X") * 4f, camera.transform.up) *
                                    Quaternion.AngleAxis(-Input.GetAxis("Mouse Y") * 4f, camera.transform.right) * _rotation;
                }

                _localTarget = camera.transform.position + camera.transform.forward * _distance;
                _hasAim = true;

                if (_world.IsHost && _grips.TryGetValue(_world.LocalId, out var local))
                {
                    local.Target = _localTarget;
                    local.Rotation = _rotation;
                    local.AimAt = Time.unscaledTime;
                }

                if (Time.unscaledTime >= _aimAt)
                {
                    _aimAt = Time.unscaledTime + 0.05f;
                    _world.SubmitAim(_world.Message(SandboxKind.CarryAim) with
                    {
                        Carry = new CarryState(_world.LocalId, true, Holding,
                            SandboxWorld.Point(_localTarget),
                            SandboxWorld.Rotation(_rotation), Sequence: ++_sequence, Rotate: Rotating)
                    });
                }

                return;
            }

            if (!Input.GetKeyDown(KeyCode.E) || _world.Building.Placing || !_world.ConsoleEnabled && !_world.IsAdmin && !_world.Environment.ThrowAnyone)
            {
                return;
            }

            float reach = CarryRules.Reach * _world.Effects(_world.LocalId).Scale;
            PlayerNetworkController actor = SandboxWorld.Avatar(_world.LocalId);
            float cameraOffset = actor ? Vector3.Distance(camera.transform.position, actor.transform.position + Vector3.up * _world.Effects(_world.LocalId).Scale) : 0f;

            if (!_world.Building.Surface(new Ray(camera.transform.position, camera.transform.forward), reach + cameraOffset, out var hit, true))
            {
                return;
            }

            CarryTarget kind;
            int id, bone = 0;

            if (_world.Ragdolls.Pick(hit, out id, out bone))
            {
                kind = CarryTarget.Ragdoll;
            }
            else if (_world.Building.Pick(camera, out id, out _))
            {
                kind = CarryTarget.Prop;
            }
            else
            {
                PlayerNetworkController player = hit.transform.GetComponentInParent<PlayerNetworkController>();
                Bot npc = hit.transform.GetComponentInParent<Bot>();
                BossBot boss = hit.transform.GetComponentInParent<BossBot>();

                if (player)
                {
                    kind = CarryTarget.Player;
                    id = player.OwnerId;
                }
                else if (npc)
                {
                    kind = CarryTarget.NPC;
                    id = npc.NetworkObject.ObjectId;
                }
                else if (boss)
                {
                    kind = CarryTarget.NPC;
                    id = boss.NetworkObject.ObjectId;
                }
                else
                {
                    return;
                }
            }

            _distance = Mathf.Clamp(hit.distance, 1f, 4f);
            _world.Building.Stop();
            _world.Submit(_world.Message(SandboxKind.CarryGrab) with
            {
                Id = id,
                Carry = new CarryState(_world.LocalId, true, Position: SandboxWorld.Point(hit.point), Target: kind, Bone: bone)
            });
        }

        internal void FixedUpdate()
        {
            if (!_world.Active || !_world.IsHost)
            {
                return;
            }

            foreach (var pair in _grips.ToArray())
            {
                Grip grip = pair.Value;
                if (grip.State.Holding == 0)
                {
                    continue;
                }

                PlayerNetworkController actor = SandboxWorld.Avatar(pair.Key);
                if (!grip.Body || !actor || Time.unscaledTime - grip.AimAt > 2f ||
                    !CarryRules.Allowed(_world.ConsoleEnabled, _world.Administrator(pair.Key), _world.Environment.ThrowAnyone, grip.State.Target) || _world.Living.IsDown(pair.Key))
                {
                    Release(pair.Key, false, false);
                    continue;
                }

                grip.Constraint?.Move(grip.State.Grip == null ? Vector3.zero : SandboxWorld.Vector(grip.State.Grip), grip.Target, grip.Rotation);
            }
        }

        internal void LateUpdate()
        {
            if (!_world.Active)
            {
                RestoreWeapons();
                return;
            }

            if (!_world.IsHost && _hasAim && !_releasePending && Cursor.lockState == CursorLockMode.Locked &&
                _grips.TryGetValue(_world.LocalId, out var local) && local.State.Holding > 0 && local.State.Grip != null)
            {
                if (local.State.Target == CarryTarget.Ragdoll)
                {
                    _world.Ragdolls.DisplayGrip(local.State.Holding, local.State.Bone, SandboxWorld.Vector(local.State.Grip), _localTarget, _rotation);
                }
                else
                {
                    Rigidbody body = _world.Building.Root(local.State.Holding)?.GetComponent<Rigidbody>();
                    if (body)
                    {
                        body.position = RigidGrip.Position(body, SandboxWorld.Vector(local.State.Grip), _localTarget, _rotation);
                        body.rotation = _rotation;
                    }
                }
            }

            var desired = new HashSet<IntPtr>();
            foreach (Grip grip in _grips.Values)
            {
                if (!grip.State.Equipped)
                {
                    continue;
                }

                PlayerNetworkController avatar = SandboxWorld.Avatar(grip.State.Player);
                if (!avatar)
                {
                    continue;
                }

                Transform weapons = avatar.WeaponRoot;
                if (!weapons)
                {
                    continue;
                }

                foreach (Renderer renderer in weapons.GetComponentsInChildren<Renderer>(true))
                {
                    desired.Add(renderer.Pointer);
                    _hidden.TryAdd(renderer.Pointer, (renderer, renderer.enabled));
                    renderer.enabled = false;
                }
            }

            FPScontroller movement = _world.LocalMovement;
            if (UsingTool && movement)
            {
                foreach (WeaponManager manager in movement.GetComponentsInChildren<WeaponManager>(true))
                {
                    _managers.TryAdd(manager.Pointer, (manager, manager.enabled));
                    manager.enabled = false;

                    foreach (WeaponScript weapon in manager.GetComponentsInChildren<WeaponScript>(true))
                    {
                        foreach (Renderer renderer in weapon.GetComponentsInChildren<Renderer>(true))
                        {
                            _hidden.TryAdd(renderer.Pointer, (renderer, renderer.enabled));
                            desired.Add(renderer.Pointer);
                            renderer.enabled = false;
                        }
                    }
                }
            }

            foreach (IntPtr key in _hidden.Keys.Where(key => !desired.Contains(key)).ToArray())
            {
                var item = _hidden[key];
                if (item.Renderer)
                {
                    item.Renderer.enabled = item.Enabled;
                }

                _hidden.Remove(key);
            }

            if (!UsingTool)
            {
                foreach (var item in _managers.Values)
                {
                    if (item.Manager)
                    {
                        item.Manager.enabled = item.Enabled;
                    }
                }

                _managers.Clear();
            }
        }

        private void RestoreWeapons()
        {
            foreach (var item in _hidden.Values)
            {
                if (item.Renderer)
                {
                    item.Renderer.enabled = item.Enabled;
                }
            }

            foreach (var item in _managers.Values)
            {
                if (item.Manager)
                {
                    item.Manager.enabled = item.Enabled;
                }
            }

            _hidden.Clear();
            _managers.Clear();
        }

        internal void Clear()
        {
            if (_world.IsHost)
            {
                foreach (int id in _grips.Keys.ToArray())
                {
                    Release(id, false, false);
                }
            }

            _grips.Clear();
            _waitForButtons = false;
            _releaseFrame = -1;
            _building = _releasePending = _hasAim = false;
            _sequence = 0;
            RestoreWeapons();
        }

        internal void Dispose() => Clear();
    }
}
