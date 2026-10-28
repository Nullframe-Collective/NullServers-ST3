using Il2Cpp;
using Il2CppFishNet.Connection;
using Il2CppFishNet.Object;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;
using NullServers.Network;

namespace NullServers.Sandbox
{
    internal sealed class LivingRagdolls
    {
        private sealed class Actor
        {
            internal LivingState State;
            internal GameObject Source;
            internal readonly LandingRecovery Recovery = new();
            internal Collider[] Supports;
            internal Vector3 Origin;
            internal float GroundOffset;
            internal float CheckAt;
            internal EscapeCounter Escape = new();
            internal readonly List<(Renderer Renderer, bool Enabled)> Renderers = new();
            internal readonly List<(Collider Collider, bool Enabled)> Colliders = new();
            internal readonly List<(Behaviour Behaviour, bool Enabled)> Behaviours = new();
            internal Transform Head;
            internal CharacterController LocalController;
            internal bool ControllerEnabled;
            internal Camera Camera;
            internal Vector3 CameraPosition;
            internal Quaternion CameraRotation;
            internal bool Bound;
            internal float BindAt;
            internal float CreatedAt = Time.unscaledTime;
        }

        private readonly SandboxWorld _world;
        private readonly Dictionary<int, Actor> _actors = new();
        private float _tapAt;
        internal LivingState Local => _actors.Values.FirstOrDefault(actor => actor.State.Active && actor.State.Player == _world.LocalId)?.State;
        internal bool Contains(int body) => _actors.ContainsKey(body);
        internal bool IsDown(int player) => player >= 0 && _actors.Values.Any(actor => actor.State.Active && actor.State.Player == player);

        internal LivingRagdolls(SandboxWorld world)
        {
            _world = world;
        }

        private GameObject Source(LivingState state)
        {
            if (state.Player >= 0)
            {
                return SandboxWorld.Avatar(state.Player)?.gameObject;
            }

            return SandboxWorld.Spawned(state.NPC);
        }

        internal string Take(int holder, CarryTarget kind, int target, Vector3 point, out int body)
        {
            body = 0;
            if (!_world.IsHost || !_world.Environment.ThrowAnyone)
            {
                return "Throw Any One is disabled by the host.";
            }

            int playerId = kind == CarryTarget.Player ? target : -1;
            if (!CarryRules.CanTakeLiving(_world.Environment.ThrowAnyone, holder, playerId))
            {
                return "This character cannot be picked up.";
            }

            if (playerId == holder)
            {
                return "You cannot pick yourself up.";
            }

            if (_actors.Values.Any(actor => playerId >= 0 ? actor.State.Player == playerId : actor.State.NPC == target))
            {
                return "This character is already ragdolled.";
            }

            PlayerNetworkController player = playerId >= 0 ? SandboxWorld.Avatar(playerId) : null;
            Bot npc = playerId < 0 ? Object.FindObjectsOfType<Bot>().FirstOrDefault(value => value.NetworkObject && value.NetworkObject.ObjectId == target) : null;
            BossBot boss = playerId < 0 && !npc ? Object.FindObjectsOfType<BossBot>().FirstOrDefault(value => value.NetworkObject && value.NetworkObject.ObjectId == target) : null;
            if (playerId >= 0 && (!player || !player.Damage || player.Damage.Health <= 0f) ||
                playerId < 0 && (!(npc || boss) || npc && npc.Health <= 0f || boss && boss.Health <= 0f))
            {
                return "Aim at a living player or NPC.";
            }

            GameObject source = player ? player.gameObject : npc ? npc.gameObject : boss.gameObject;
            Bounds bounds = SandboxAssets.BoundsOf(source);
            bounds.Expand(0.5f);

            if (!bounds.Contains(point))
            {
                return "Aim at the character's surface.";
            }

            GameObject prefab = player ? player.Damage.RagdollPrefab?.gameObject : npc ? npc.RagdollPrefab?.gameObject : boss.RagdollPrefab?.gameObject;
            if (!prefab)
            {
                return "This character has no supported ragdoll prefab.";
            }

            if (!_world.Ragdolls.CreateLiving(prefab, source, playerId, out body))
            {
                return "This character's ragdoll could not be prepared.";
            }

            LivingState state = new(body, playerId, playerId < 0 ? target : -1, Holder: holder, Locked: _world.Administrator(holder));
            Apply(state);
            _world.Send(_world.Message(SandboxKind.Living) with
            {
                Living = state
            });
            return null;
        }

        internal void Apply(LivingState state)
        {
            if (!state.Active)
            {
                if (_actors.Remove(state.Body, out var old))
                {
                    Restore(old, state.Position);
                }

                return;
            }

            if (!_actors.TryGetValue(state.Body, out var actor))
            {
                GameObject source = Source(state);
                _actors[state.Body] = actor = new Actor
                {
                    State = state,
                    Source = source,
                    Origin = source ? source.transform.position : Vector3.zero
                };
            }

            actor.State = state;
            if (!actor.Source)
            {
                actor.Source = Source(state);
            }

            if (state.Holder >= 0)
            {
                actor.Recovery.Reset();
            }
        }

        internal void Held(int body, int holder)
        {
            if (!_actors.TryGetValue(body, out var actor))
            {
                return;
            }

            actor.Recovery.Reset();
            actor.Escape = new EscapeCounter();
            actor.State = actor.State with
            {
                Holder = holder,
                Locked = _world.Administrator(holder),
                Escapes = 0
            };
            _world.Send(_world.Message(SandboxKind.Living) with
            {
                Living = actor.State
            });
        }

        internal void Released(int body)
        {
            if (!_actors.TryGetValue(body, out var actor))
            {
                return;
            }

            actor.State = actor.State with
            {
                Holder = -1,
                Locked = false
            };
            actor.Recovery.Reset();
            _world.Send(_world.Message(SandboxKind.Living) with
            {
                Living = actor.State
            });
        }

        internal string Escape(int player)
        {
            Actor actor = _actors.Values.FirstOrDefault(actor => actor.State.Player == player);
            if (actor == null || actor.State.Holder < 0)
            {
                return "You are not being held.";
            }

            if (actor.State.Locked)
            {
                return "A host or administrator is holding you.";
            }

            int taps = actor.Escape.Press(Time.unscaledTime, actor.State.Locked);
            if (taps == actor.State.Escapes)
            {
                return null;
            }

            actor.State = actor.State with
            {
                Escapes = Math.Min(taps, CarryRules.EscapeTaps)
            };
            if (CarryRules.CanEscape(true, false, taps))
            {
                _world.Hands.Release(actor.State.Holder, false, false);
                if (actor.State.Holder >= 0)
                {
                    Released(actor.State.Body);
                }
            }
            else
            {
                _world.Send(_world.Message(SandboxKind.Living) with
                {
                    Living = actor.State
                });
            }

            return null;
        }

        private void Bind(Actor actor)
        {
            GameObject root = _world.Ragdolls.Root(actor.State.Body);
            GameObject source = Source(actor.State);

            if (!root || !source)
            {
                return;
            }

            actor.Source = source;
            actor.Origin = source.transform.position;
            NavMeshAgent agent = source.GetComponent<NavMeshAgent>();

            if (actor.State.NPC >= 0 && agent)
            {
                actor.GroundOffset = agent.baseOffset * Mathf.Abs(source.transform.lossyScale.y);
            }

            actor.Supports = root.GetComponentsInChildren<Collider>(true).Where(value => !value.isTrigger).ToArray();
            if (actor.State.Player == _world.LocalId)
            {
                FPScontroller movement = _world.LocalMovement;
                actor.LocalController = movement ? movement.Controller : null;
                actor.ControllerEnabled = actor.LocalController && actor.LocalController.enabled;

                if (actor.LocalController)
                {
                    actor.GroundOffset = Mathf.Max(0f, actor.LocalController.height * 0.5f - actor.LocalController.center.y) *
                        Mathf.Abs(actor.LocalController.transform.lossyScale.y);
                }

                actor.Camera = Camera.main;
                if (actor.Camera)
                {
                    actor.CameraPosition = actor.Camera.transform.localPosition;
                    actor.CameraRotation = actor.Camera.transform.localRotation;
                }
            }

            actor.Head = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(value => value.name.Contains("head", StringComparison.OrdinalIgnoreCase)) ?? root.transform;
            foreach (Renderer renderer in source.GetComponentsInChildren<Renderer>(true))
            {
                actor.Renderers.Add((renderer, renderer.enabled));
                renderer.enabled = false;
            }

            foreach (Collider collider in source.GetComponentsInChildren<Collider>(true))
            {
                actor.Colliders.Add((collider, collider.enabled));
                collider.enabled = false;
            }

            foreach (Behaviour behaviour in source.GetComponentsInChildren<Behaviour>(true))
            {
                if (behaviour.TryCast<Animator>() || behaviour.TryCast<NavMeshAgent>() || behaviour.TryCast<Bot>() || behaviour.TryCast<BossBot>() ||
                    behaviour.TryCast<CharacterAnimation>() || behaviour.TryCast<HeadLookController>() || behaviour.TryCast<MecanimControl>())
                {
                    actor.Behaviours.Add((behaviour, behaviour.enabled));
                    behaviour.enabled = false;
                }
            }

            actor.Bound = true;
        }

        private void Restore(Actor actor, Point3 position)
        {
            if (!Alive(actor))
            {
                return;
            }

            Vector3 destination = position == null ? actor.Source.transform.position : SandboxWorld.Vector(position);
            NavMeshAgent agent = actor.Source.GetComponent<NavMeshAgent>();

            if (position != null && actor.Source)
            {
                actor.Source.transform.position = destination;
            }

            if (actor.State.Player == _world.LocalId)
            {
                FPScontroller movement = _world.LocalMovement;
                if (movement)
                {
                    bool enabled = actor.LocalController == movement.Controller ? actor.ControllerEnabled : movement.Controller && movement.Controller.enabled;
                    if (movement.Controller)
                    {
                        movement.Controller.enabled = false;
                    }

                    if (position != null)
                    {
                        movement.transform.position = SandboxWorld.Vector(position);
                    }

                    if (movement.Movement != null)
                    {
                        movement.Movement.velocity = Vector3.zero;
                    }

                    if (movement.Controller)
                    {
                        movement.Controller.enabled = enabled;
                    }
                }

                if (actor.Camera)
                {
                    actor.Camera.transform.localPosition = actor.CameraPosition;
                    actor.Camera.transform.localRotation = actor.CameraRotation;
                }
            }

            foreach (var item in actor.Renderers)
            {
                if (item.Renderer)
                {
                    item.Renderer.enabled = item.Enabled;
                }
            }

            foreach (var item in actor.Colliders)
            {
                if (item.Collider)
                {
                    item.Collider.enabled = item.Enabled;
                }
            }

            foreach (var item in actor.Behaviours)
            {
                if (item.Behaviour)
                {
                    item.Behaviour.enabled = item.Enabled;
                }
            }

            if (actor.State.NPC >= 0 && agent && agent.enabled && agent.isOnNavMesh)
            {
                agent.Warp(destination);
            }
        }

        private void Recover(int body, Point3 landed = null)
        {
            if (!_actors.TryGetValue(body, out var actor))
            {
                return;
            }

            _world.Hands.Removed(body);
            Vector3 position = landed == null ? actor.Origin : SandboxWorld.Vector(landed);
            NavMeshAgent agent = actor.Source ? actor.Source.GetComponent<NavMeshAgent>() : null;
            Vector3 up = Physics.gravity.sqrMagnitude > 0.01f ? -Physics.gravity.normalized : Vector3.up;
            float scale = actor.Source ? Mathf.Max(0.25f, actor.Source.transform.lossyScale.y) : 1f;
            if (landed != null && actor.State.NPC >= 0 && agent &&
                NavMesh.SamplePosition(position, out var hit, scale, agent.areaMask) &&
                Mathf.Abs(Vector3.Dot(hit.position - position, up)) < 0.3f * scale)
            {
                position = hit.position;
            }

            if (landed != null)
            {
                position += up * actor.GroundOffset;
            }

            LivingState state = actor.State with
            {
                Active = false,
                Holder = -1,
                Position = SandboxWorld.Point(position)
            };
            Apply(state);
            _world.Send(_world.Message(SandboxKind.Living) with
            {
                Living = state
            });
            _world.Ragdolls.Remove(body);
            _world.Send(_world.Message(SandboxKind.RemoveRagdoll) with
            {
                Id = body
            });
        }

        internal void Cancel(int body) => Recover(body);

        private static bool Alive(Actor actor)
        {
            if (!actor.Source)
            {
                return false;
            }

            PlayerNetworkController player = actor.Source.GetComponent<PlayerNetworkController>();
            if (player)
            {
                return player.IsClientInitialized && player.Damage && player.Damage.Health > 0f;
            }

            Bot npc = actor.Source.GetComponent<Bot>();
            BossBot boss = actor.Source.GetComponent<BossBot>();
            return npc && npc.IsClientInitialized && npc.Health > 0f || boss && boss.IsClientInitialized && boss.Health > 0f;
        }

        private bool Landed(Actor actor, out Point3 position)
        {
            position = null;
            float now = Time.unscaledTime;
            if (actor.State.Holder >= 0 || !actor.Bound || now < actor.CheckAt) return false;

            actor.CheckAt = now + 0.05f;
            GameObject root = _world.Ragdolls.Root(actor.State.Body);
            Rigidbody pelvis = _world.Ragdolls.PhysicsBody(actor.State.Body, 0);
            if (!root || !pelvis) return false;

            Vector3 up = Physics.gravity.sqrMagnitude > 0.01f ? -Physics.gravity.normalized : Vector3.up;
            float scale = Mathf.Max(0.25f, root.transform.lossyScale.y);
            float margin = Mathf.Clamp(scale * 0.04f, 0.03f, 0.15f);
            bool grounded = false;
            Vector3 contact = Vector3.zero;

            if (Vector3.Dot(pelvis.velocity, up) < 1f && actor.Supports != null)
            {
                foreach (Collider collider in actor.Supports.Where(value => value && value.enabled && value.gameObject.activeInHierarchy)
                    .OrderBy(value => Vector3.Dot(value.bounds.center, up)).Take(3))
                {
                    Bounds bounds = collider.bounds;
                    float extent = Vector3.Dot(new Vector3(Mathf.Abs(up.x), Mathf.Abs(up.y), Mathf.Abs(up.z)), bounds.extents);
                    foreach (RaycastHit hit in Physics.RaycastAll(bounds.center + up * margin, -up,
                        extent + margin * 2f, ~(1 << 2), QueryTriggerInteraction.Ignore))
                    {
                        if (!hit.collider || hit.transform.IsChildOf(root.transform) ||
                            actor.Source && hit.transform.IsChildOf(actor.Source.transform) || Vector3.Dot(hit.normal, up) < 0.5f)
                        {
                            continue;
                        }

                        grounded = true;
                        contact = hit.point + up * (0.03f * scale);
                        break;
                    }

                    if (grounded) break;
                }
            }

            if (!actor.Recovery.Update(now, grounded)) return false;

            position = SandboxWorld.Point(contact);
            return true;
        }

        internal void Update()
        {
            foreach (Actor actor in _actors.Values.ToArray())
            {
                if (!actor.Bound && Time.unscaledTime >= actor.BindAt)
                {
                    actor.BindAt = Time.unscaledTime + 0.2f;
                    Bind(actor);
                }

                if (!_world.IsHost) continue;

                Rigidbody pelvis = _world.Ragdolls.PhysicsBody(actor.State.Body, 0);
                if (!_world.Environment.ThrowAnyone || !Alive(actor) ||
                    pelvis && (!SandboxWorld.Point(pelvis.position).Valid() || pelvis.position.y < -5000f) ||
                    !actor.Bound && Time.unscaledTime - actor.CreatedAt > 3f ||
                    actor.Bound && !_world.Ragdolls.Root(actor.State.Body))
                {
                    Recover(actor.State.Body);
                }
                else if (Landed(actor, out var position))
                {
                    Recover(actor.State.Body, position);
                }
            }

            LivingState local = Local;
            if (local != null && local.Holder >= 0 && !local.Locked && Input.GetKeyDown(KeyCode.Space) &&
                Cursor.lockState == CursorLockMode.Locked && !SandboxWorld.Typing() && Time.unscaledTime >= _tapAt)
            {
                _tapAt = Time.unscaledTime + 0.1f;
                _world.Submit(_world.Message(SandboxKind.Escape));
            }
        }

        internal void LateUpdate()
        {
            foreach (Actor actor in _actors.Values)
            {
                foreach (var item in actor.Renderers)
                {
                    if (item.Renderer)
                    {
                        item.Renderer.enabled = false;
                    }
                }

                foreach (var item in actor.Colliders)
                {
                    if (item.Collider)
                    {
                        item.Collider.enabled = false;
                    }
                }

                if (actor.State.Player != _world.LocalId || !actor.Head)
                {
                    continue;
                }

                FPScontroller movement = _world.LocalMovement;
                if (movement)
                {
                    if (movement.Controller)
                    {
                        movement.Controller.enabled = false;
                    }

                    movement.transform.position = actor.Head.position;
                }

                Camera camera = Camera.main;
                if (camera)
                {
                    camera.transform.SetPositionAndRotation(actor.Head.position + actor.Head.forward * 0.15f, actor.Head.rotation);
                }
            }
        }

        internal void Snapshot(NetworkConnection connection)
        {
            foreach (Actor actor in _actors.Values)
            {
                _world.Queue(_world.Message(SandboxKind.Living) with
                {
                    Living = actor.State
                }, connection);
            }
        }

        internal void Disconnect(int player)
        {
            foreach (Actor actor in _actors.Values.Where(actor => actor.State.Player == player || actor.State.Holder == player).ToArray())
            {
                Recover(actor.State.Body);
            }
        }

        internal void Clear()
        {
            foreach (Actor actor in _actors.Values)
            {
                Restore(actor, null);
            }

            _actors.Clear();
        }
    }
}
