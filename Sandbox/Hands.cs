using Il2Cpp;
using Il2CppFishNet.Connection;
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
        private readonly Dictionary<IntPtr, (float Until, Renderer[] Renderers)> _renderers = new();
        private readonly HashSet<IntPtr> _desired = new();
        private readonly List<IntPtr> _stale = new();
        private (IntPtr Movement, float Until, WeaponManager[] Managers) _localManagers;
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

        internal void CollectHeldProps(HashSet<int> target)
        {
            foreach (Grip grip in _grips.Values)
            {
                if (grip.State.Target == CarryTarget.Prop && grip.State.Holding > 0)
                {
                    target.Add(grip.State.Holding);
                }
            }
        }

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

        // The host checks every carry request: releasing, equipping the building hands, or grabbing an object.
        internal string Request(int sender, SandboxMessage message)
        {
            CarryState request = message.Carry;
            if (request?.Valid != true) return "Invalid carry request.";

            if (message.Kind == SandboxKind.CarryRelease)
            {
                Release(sender, request.Throw, request.Freeze);
                return null;
            }

            if (message.Kind == SandboxKind.CarryState && !request.Equipped)
            {
                SetHands(sender, new CarryState(sender, false), false);
                return null;
            }

            CarryTarget permission = message.Kind == SandboxKind.CarryState ? CarryTarget.Prop : request.Target;
            string error = CarryError(sender, permission, out PlayerNetworkController actor);
            if (error != null) return error;

            if (message.Kind == SandboxKind.CarryState)
            {
                SetHands(sender, new CarryState(sender, true, Building: true), true);
                return null;
            }

            if (message.Kind != SandboxKind.CarryGrab) return "Unknown carry request.";

            return Grab(sender, message.Id, request, actor);
        }

        private void SetHands(int sender, CarryState state, bool building)
        {
            Release(sender, false, false);
            _grips[sender] = new Grip { State = state, Building = building };
            _world.Send(_world.Message(SandboxKind.CarryState) with { Carry = state });
        }

        private string CarryError(int sender, CarryTarget permission, out PlayerNetworkController actor)
        {
            actor = null;
            if (!CarryRules.Allowed(_world.ConsoleEnabled, _world.Administrator(sender), _world.Environment.ThrowAnyone, permission))
            {
                return "The host must enable the sandbox console or Throw Any One first.";
            }

            if (_world.Living.IsDown(sender)) return "You cannot carry an object while ragdolled.";

            actor = SandboxWorld.Avatar(sender);
            if (!actor || !actor.Damage || actor.Damage.Health <= 0f) return "A living character is required.";

            return null;
        }

        private string Grab(int sender, int id, CarryState request, PlayerNetworkController actor)
        {
            if (request.Position == null) return "Aim at a nearby object.";

            Vector3 point = SandboxWorld.Vector(request.Position);
            float scale = _world.Effects(sender).Scale;
            float reach = CarryRules.Reach * scale;
            if (Vector3.Distance(actor.transform.position + Vector3.up * scale, point) > reach + 1f) return "Move closer to the object.";

            // A living player or NPC is first turned into a ragdoll, which is then carried like any other ragdoll.
            CarryTarget kind = request.Target;
            bool createdLiving = false;
            if (kind is CarryTarget.Player or CarryTarget.NPC)
            {
                string error = _world.Living.Take(sender, kind, id, point, out id);
                if (error != null) return error;

                createdLiving = true;
                kind = CarryTarget.Ragdoll;
            }

            string failure = TryHold(sender, id, kind, point, request.Bone, actor);
            if (failure != null && createdLiving)
            {
                _world.Living.Cancel(id);
            }

            return failure;
        }

        private string TryHold(int sender, int id, CarryTarget kind, Vector3 point, int bone, PlayerNetworkController actor)
        {
            GameObject root;
            if (kind == CarryTarget.Prop)
            {
                string error = PrepareProp(sender, id, out root);
                if (error != null) return error;
            }
            else
            {
                root = _world.Ragdolls.Root(id);
            }

            if (!root) return "This object is no longer available.";

            Bounds bounds = SandboxAssets.BoundsOf(root);
            bounds.Expand(0.5f);
            if (!bounds.Contains(point)) return "Aim at the object's surface.";

            if (_grips.Any(pair => pair.Key != sender && pair.Value.State.Holding == id)) return "Someone is already holding this object.";

            Rigidbody body = kind == CarryTarget.Prop ? root.GetComponent<Rigidbody>() : _world.Ragdolls.PhysicsBody(id, bone);
            if (!body) return "This object has no physics body.";

            Hold(sender, id, kind, point, bone, body, root, actor);
            return null;
        }

        // A static prop becomes dynamic before it can be carried.
        private string PrepareProp(int sender, int id, out GameObject root)
        {
            root = null;
            BuildingObject state = _world.Building.State(id);
            if (state == null || !SandboxMessage.CanEdit(state.Owner, sender, _world.Administrator(sender)))
            {
                return "You can carry your own props; administrators can carry all props.";
            }

            root = _world.Building.Root(id);
            if (state.Dynamic) return null;

            SandboxMessage edit = _world.Message(SandboxKind.Edit) with { Object = state with { Dynamic = true } };
            return _world.Building.Request(sender, edit);
        }

        private void Hold(int sender, int id, CarryTarget kind, Vector3 point, int bone, Rigidbody body, GameObject root, PlayerNetworkController actor)
        {
            Release(sender, false, false);
            body.isKinematic = false;
            body.WakeUp();

            bool building = _grips.GetValueOrDefault(sender)?.Building == true;
            Point3 grip = SandboxWorld.Point(body.transform.InverseTransformPoint(point));
            CarryState state = new(sender, true, id, SandboxWorld.Point(point), SandboxWorld.Rotation(body.rotation),
                Grip: grip, Target: kind, Bone: bone, Building: building);

            // The carrier's own colliders must not push the object it holds.
            IEnumerable<Collider> holder = actor.GetComponentsInChildren<Collider>(true);
            if (sender == _world.LocalId)
            {
                holder = holder.Concat(Object.FindObjectsOfType<FPScontroller>().SelectMany(value => value.GetComponentsInChildren<Collider>(true)));
            }

            _grips[sender] = new Grip
            {
                State = state,
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
            _world.Send(_world.Message(SandboxKind.CarryState) with { Carry = state });
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
            if (!_world.Active) return;

            // After a release, clicks are ignored until both mouse buttons are up again.
            if (_waitForButtons && Time.frameCount > _releaseFrame &&
                !Input.GetMouseButton(0) && !Input.GetMouseButton(1) &&
                !Input.GetMouseButtonUp(0) && !Input.GetMouseButtonUp(1))
            {
                _waitForButtons = false;
            }

            bool allowed = Cursor.lockState == CursorLockMode.Locked && !SandboxWorld.Typing();
            if (!allowed)
            {
                // An open menu or chat drops the held object after half a second.
                if (Holding > 0 && Time.unscaledTime - _allowedAt > 0.5f)
                {
                    RequestRelease();
                }

                return;
            }

            _allowedAt = Time.unscaledTime;
            Camera camera = Camera.main;
            if (!camera || _world.Living.IsDown(_world.LocalId)) return;

            if (Holding > 0)
            {
                UpdateHeldObject(camera);
                return;
            }

            if (!Input.GetKeyDown(KeyCode.E) || _world.Building.Placing) return;
            if (!_world.ConsoleEnabled && !_world.IsAdmin && !_world.Environment.ThrowAnyone) return;

            TryGrab(camera);
        }

        private void UpdateHeldObject(Camera camera)
        {
            if (_releasePending) return;

            if (Input.GetKeyDown(KeyCode.E) || Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1))
            {
                RequestRelease(Input.GetMouseButtonDown(0), Input.GetMouseButtonDown(1));
                return;
            }

            _distance = Mathf.Clamp(_distance + Input.GetAxis("Mouse ScrollWheel") * 2f, 1f, 4f);
            if (Rotating)
            {
                Quaternion yaw = Quaternion.AngleAxis(Input.GetAxis("Mouse X") * 4f, camera.transform.up);
                Quaternion pitch = Quaternion.AngleAxis(-Input.GetAxis("Mouse Y") * 4f, camera.transform.right);
                _rotation = yaw * pitch * _rotation;
            }

            _localTarget = camera.transform.position + camera.transform.forward * _distance;
            _hasAim = true;

            if (_world.IsHost && _grips.TryGetValue(_world.LocalId, out Grip local))
            {
                local.Target = _localTarget;
                local.Rotation = _rotation;
                local.AimAt = Time.unscaledTime;
            }

            if (Time.unscaledTime < _aimAt) return;

            _aimAt = Time.unscaledTime + 0.05f;
            CarryState aim = new(_world.LocalId, true, Holding, SandboxWorld.Point(_localTarget), SandboxWorld.Rotation(_rotation),
                Sequence: ++_sequence, Rotate: Rotating);
            _world.SubmitAim(_world.Message(SandboxKind.CarryAim) with { Carry = aim });
        }

        private void TryGrab(Camera camera)
        {
            float scale = _world.Effects(_world.LocalId).Scale;
            float reach = CarryRules.Reach * scale;
            PlayerNetworkController actor = SandboxWorld.Avatar(_world.LocalId);
            float cameraOffset = actor ? Vector3.Distance(camera.transform.position, actor.transform.position + Vector3.up * scale) : 0f;

            Ray ray = new(camera.transform.position, camera.transform.forward);
            if (!_world.Building.Surface(ray, reach + cameraOffset, out var hit, true)) return;
            if (!PickTarget(camera, hit, out CarryTarget kind, out int id, out int bone)) return;

            _distance = Mathf.Clamp(hit.distance, 1f, 4f);
            _world.Building.Stop();
            CarryState grab = new(_world.LocalId, true, Position: SandboxWorld.Point(hit.point), Target: kind, Bone: bone);
            _world.Submit(_world.Message(SandboxKind.CarryGrab) with { Id = id, Carry = grab });
        }

        // Finds what the player looks at: a ragdoll, a placed prop, another player or an NPC.
        private bool PickTarget(Camera camera, RaycastHit hit, out CarryTarget kind, out int id, out int bone)
        {
            kind = CarryTarget.Ragdoll;
            bone = 0;

            if (_world.Ragdolls.Pick(hit, out id, out bone)) return true;

            kind = CarryTarget.Prop;
            if (_world.Building.Pick(camera, out id, out _)) return true;

            PlayerNetworkController player = hit.transform.GetComponentInParent<PlayerNetworkController>();
            if (player)
            {
                kind = CarryTarget.Player;
                id = player.OwnerId;
                return true;
            }

            kind = CarryTarget.NPC;
            Bot npc = hit.transform.GetComponentInParent<Bot>();
            if (npc)
            {
                id = npc.NetworkObject.ObjectId;
                return true;
            }

            BossBot boss = hit.transform.GetComponentInParent<BossBot>();
            if (boss)
            {
                id = boss.NetworkObject.ObjectId;
                return true;
            }

            return false;
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

            ShowLocalGrip();

            bool equipped = false;
            foreach (Grip grip in _grips.Values)
            {
                equipped |= grip.State.Equipped;
            }

            if (!equipped && !UsingTool && _hidden.Count == 0 && _managers.Count == 0) return;

            _desired.Clear();
            HideHeldWeapons();
            if (UsingTool)
            {
                HideLocalWeapons();
            }

            ShowWeaponsNoLongerHidden();

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

        // A client moves the object it holds right away, without waiting for the host's next pose.
        private void ShowLocalGrip()
        {
            if (_world.IsHost || !_hasAim || _releasePending || Cursor.lockState != CursorLockMode.Locked) return;
            if (!_grips.TryGetValue(_world.LocalId, out Grip local) || local.State.Holding <= 0 || local.State.Grip == null) return;

            Vector3 grip = SandboxWorld.Vector(local.State.Grip);
            if (local.State.Target == CarryTarget.Ragdoll)
            {
                _world.Ragdolls.DisplayGrip(local.State.Holding, local.State.Bone, grip, _localTarget, _rotation);
                return;
            }

            Rigidbody body = _world.Building.Root(local.State.Holding)?.GetComponent<Rigidbody>();
            if (!body) return;

            Vector3 position = RigidGrip.Position(body, grip, _localTarget, _rotation);
            body.transform.SetPositionAndRotation(position, _rotation);
            body.position = position;
            body.rotation = _rotation;
        }

        // Players holding the building hands do not show the weapon in their hands.
        private void HideHeldWeapons()
        {
            foreach (Grip grip in _grips.Values)
            {
                if (!grip.State.Equipped) continue;

                PlayerNetworkController avatar = SandboxWorld.Avatar(grip.State.Player);
                if (!avatar) continue;

                Transform weapons = avatar.WeaponRoot;
                if (!weapons) continue;

                Hide(Renderers(weapons, () => weapons.GetComponentsInChildren<Renderer>(true).ToArray()));
            }
        }

        // The local player's weapons are switched off while a sandbox tool is in use.
        private void HideLocalWeapons()
        {
            FPScontroller movement = _world.LocalMovement;
            if (!movement) return;

            if (_localManagers.Movement != movement.Pointer || Time.unscaledTime >= _localManagers.Until)
            {
                _localManagers = (movement.Pointer, Time.unscaledTime + 0.5f, movement.GetComponentsInChildren<WeaponManager>(true).ToArray());
            }

            foreach (WeaponManager manager in _localManagers.Managers)
            {
                if (!manager) continue;

                _managers.TryAdd(manager.Pointer, (manager, manager.enabled));
                manager.enabled = false;
                Hide(Renderers(manager, () => manager.GetComponentsInChildren<WeaponScript>(true)
                    .SelectMany(weapon => weapon.GetComponentsInChildren<Renderer>(true)).ToArray()));
            }
        }

        private void ShowWeaponsNoLongerHidden()
        {
            _stale.Clear();
            foreach (IntPtr key in _hidden.Keys)
            {
                if (!_desired.Contains(key))
                {
                    _stale.Add(key);
                }
            }

            foreach (IntPtr key in _stale)
            {
                var item = _hidden[key];
                if (item.Renderer)
                {
                    item.Renderer.enabled = item.Enabled;
                }

                _hidden.Remove(key);
            }
        }

        private Renderer[] Renderers(Component root, Func<Renderer[]> find)
        {
            if (_renderers.TryGetValue(root.Pointer, out var cached) && Time.unscaledTime < cached.Until)
            {
                return cached.Renderers;
            }

            if (_renderers.Count > 64) _renderers.Clear();

            Renderer[] renderers = find();
            _renderers[root.Pointer] = (Time.unscaledTime + 0.5f, renderers);
            return renderers;
        }

        private void Hide(Renderer[] renderers)
        {
            foreach (Renderer renderer in renderers)
            {
                if (!renderer) continue;

                _desired.Add(renderer.Pointer);
                _hidden.TryAdd(renderer.Pointer, (renderer, renderer.enabled));
                renderer.enabled = false;
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
            _renderers.Clear();
            _localManagers = default;
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
