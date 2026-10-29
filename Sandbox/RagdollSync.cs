using Il2Cpp;
using Il2CppFishNet.Connection;
using Il2CppFishNet.Object;
using NullServers.Customization;
using NullServers.Network;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NullServers.Sandbox
{
    internal sealed class RagdollSync
    {
        private sealed class Body
        {
            internal RagdollDefinition Definition;
            internal GameObject Root;
            internal Rigidbody[] Physics;
            internal Transform[] Bones;
            internal PhysicsBuffer[] Motion;
            internal int[] Sequences;
            internal bool Copy;
            internal float NextSend;
            internal float CreatedAt;
            internal float DestroyAt;
            internal float BindAt;
            internal RagdollVisual Visual;
        }

        private static RagdollSync _instance;
        private readonly SandboxWorld _world;
        private readonly Dictionary<int, Body> _bodies = new();
        private readonly Dictionary<IntPtr, GameObject> _local = new();
        private readonly Dictionary<IntPtr, int> _players = new();
        private readonly HashSet<IntPtr> _bound = new();
        private int _nextId = 1000000;
        private float _nextScan;
        private float _poseAt;
        private int _sourcePlayer = -1;
        private RagdollSkin _sourceSkin;
        private bool _creatingLiving;

        internal RagdollSync(SandboxWorld world)
        {
            _instance = this;
            _world = world;
        }

        internal static void BeforeDeath(PlayerDamage __instance, out (int Player, RagdollSkin Skin) __state)
        {
            __state = (_instance._sourcePlayer, _instance._sourceSkin);
            _instance._sourcePlayer = SandboxWorld.DamageOwner(__instance);
            _instance._sourceSkin = _instance._world.Active ? RagdollSkin.Capture(_instance._sourcePlayer, __instance) : null;
        }

        internal static Exception AfterDeath(PlayerDamage __instance, Exception __exception, (int Player, RagdollSkin Skin) __state)
        {
            try
            {
                if (__exception == null && _instance._world.Active && __instance && __instance.Health <= 0f)
                {
                    _instance.Scan();
                }
            }
            finally
            {
                _instance._sourcePlayer = __state.Player;
                _instance._sourceSkin = __state.Skin;
            }

            return __exception;
        }

        internal static void Created(RagdollController __instance)
        {
            if (_instance._world.Active)
            {
                _instance.Register(__instance.gameObject);
            }
        }

        internal static bool BeforeFreeze() => !_instance._world.Active;

        internal static bool BeforeEnable(RagdollMode __instance)
        {
            RagdollSync sync = _instance;
            if (!sync._world.Active) return true;

            MecanimControl model = __instance.Mecanim;
            if (!model || __instance.VisualRenderers == null || __instance.PhysicalRenderers == null) return true;

            PlayerDamage damage = __instance.GetComponentInParent<PlayerDamage>();
            int player = sync._sourcePlayer >= 0 ? sync._sourcePlayer : damage ? SandboxWorld.DamageOwner(damage) : -1;
            RagdollSkin skin = sync._sourceSkin ?? (player >= 0 ? RagdollSkin.Capture(player, damage) : null);
            foreach (Behaviour behaviour in model.GetComponentsInChildren<Behaviour>(true))
                if (behaviour.TryCast<Animator>() || behaviour.TryCast<Animation>() || behaviour.TryCast<MecanimControl>() ||
                    behaviour.TryCast<HeadLookController>() || behaviour.TryCast<CharacterAnimation>()) behaviour.enabled = false;
            model.gameObject.SetActive(true);
            model.transform.SetParent(__instance.transform, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;

            int count = Math.Min(__instance.VisualRenderers.Length, __instance.PhysicalRenderers.Length);
            for (int index = 0; index < count; index++)
            {
                SkinnedMeshRenderer visual = __instance.VisualRenderers[index];
                SkinnedMeshRenderer physical = __instance.PhysicalRenderers[index];
                if (!visual || !physical) continue;

                Transform[] from = visual.bones.ToArray(), to = physical.bones.ToArray();
                for (int bone = 0; bone < Math.Min(from.Length, to.Length); bone++)
                {
                    if (!from[bone] || !to[bone] || from[bone] == to[bone]) continue;

                    from[bone].SetParent(to[bone], false);
                    from[bone].localPosition = Vector3.zero;
                    from[bone].localRotation = Quaternion.identity;
                    from[bone].localScale = Vector3.one;
                }
            }

            __instance.transform.SetParent(null, true);
            int previousPlayer = sync._sourcePlayer;
            RagdollSkin previousSkin = sync._sourceSkin;

            try
            {
                sync._sourcePlayer = player;
                sync._sourceSkin = skin;
                sync.Register(__instance.gameObject);
                Body body = sync._bodies.Values.FirstOrDefault(body => body.Root == __instance.gameObject);

                if (body != null)
                {
                    body.DestroyAt = Time.unscaledTime + 10f;
                }
            }
            finally
            {
                sync._sourcePlayer = previousPlayer;
                sync._sourceSkin = previousSkin;
            }

            return false;
        }

        internal static bool BeforeCorpseTimer(WaitForDestroy __instance)
        {
            RagdollSync sync = _instance;
            GameObject root = __instance.gameObject;
            bool corpse = root.name.Contains("ragdoll", StringComparison.OrdinalIgnoreCase) || root.GetComponentsInChildren<CharacterJoint>(true).Length > 0;
            if (!sync._world.Active || !corpse || root.GetComponentsInChildren<Rigidbody>(true).Length == 0) return true;

            sync.OwnLifetime(root, __instance.Delay);
            return false;
        }

        internal static bool BeforeDeadBot(DeadBot __instance)
        {
            RagdollSync sync = _instance;
            if (!sync._world.Active) return true;

            if (__instance.Mecanim && __instance.DeathAnimations is { Length: > 0 } animations)
            {
                __instance.Mecanim.Play(animations[UnityEngine.Random.Range(0, animations.Length)], 0.1f);
            }

            EnsurePhysics(__instance.gameObject);
            sync.OwnLifetime(__instance.gameObject, __instance.DestroyDelay);
            return false;
        }

        private void OwnLifetime(GameObject root, float seconds)
        {
            Register(root);
            Body body = _bodies.Values.FirstOrDefault(body => body.Root == root);

            if (body != null)
            {
                body.DestroyAt = Time.unscaledTime + (float.IsFinite(seconds) ? Mathf.Clamp(seconds, 0.01f, 86400f) : 10f);
            }
        }

        private static void EnsurePhysics(GameObject root)
        {
            if (root.GetComponentsInChildren<Rigidbody>(true).Length > 0) return;

            Bounds bounds = SandboxAssets.BoundsOf(root);
            Vector3 scale = root.transform.lossyScale;

            if (!root.GetComponentsInChildren<Collider>(true).Any(collider => collider.enabled && !collider.isTrigger))
            {
                BoxCollider collider = root.AddComponent<BoxCollider>();
                collider.center = root.transform.InverseTransformPoint(bounds.center);
                collider.size = new Vector3(bounds.size.x / Mathf.Max(0.001f, Mathf.Abs(scale.x)),
                    bounds.size.y / Mathf.Max(0.001f, Mathf.Abs(scale.y)), bounds.size.z / Mathf.Max(0.001f, Mathf.Abs(scale.z)));
            }

            Rigidbody body = root.AddComponent<Rigidbody>();
            body.mass = Mathf.Clamp(bounds.size.x * bounds.size.y * bounds.size.z, 1f, 100f);
        }

        internal GameObject Root(int id) => _bodies.GetValueOrDefault(id)?.Root;
        internal Rigidbody PhysicsBody(int id, int bone) => _bodies.TryGetValue(id, out var body) && body.Physics != null && bone >= 0 && bone < body.Physics.Length ? body.Physics[bone] : null;

        internal bool Pick(RaycastHit hit, out int id, out int bone)
        {
            id = bone = 0;
            foreach (var pair in _bodies)
            {
                Body body = pair.Value;
                if (!body.Root || !hit.transform.IsChildOf(body.Root.transform) && hit.transform != body.Root.transform) continue;

                id = pair.Key;
                if (body.Physics != null)
                {
                    int index = Array.FindIndex(body.Physics, value => value && value == hit.rigidbody);
                    bone = Math.Max(0, index);
                }

                return true;
            }

            return false;
        }

        internal void Throw(int id, Vector3 velocity)
        {
            if (!_bodies.TryGetValue(id, out var body) || body.Physics == null) return;

            foreach (Rigidbody part in body.Physics)
            {
                if (!part) continue;

                part.isKinematic = false;
                part.velocity = velocity;
                part.angularVelocity = Vector3.zero;
                part.WakeUp();
            }
        }

        internal static GameObject CloneBody(GameObject prefab)
        {
            GameObject holder = new("NullFrameBodyStaging");
            holder.SetActive(false);
            GameObject root = Object.Instantiate(prefab, holder.transform, false);
            root.SetActive(false);

            foreach (Component component in root.GetComponentsInChildren<Component>(true))
            {
                if (!component || component.TryCast<Transform>() || component.TryCast<Renderer>() || component.TryCast<MeshFilter>() ||
                    component.TryCast<Rigidbody>() || component.TryCast<Collider>() || component.TryCast<Joint>()) continue;
                Object.DestroyImmediate(component);
            }

            root.name = prefab.name;
            root.transform.SetParent(null, false);
            Object.Destroy(holder);
            return root;
        }

        private static GameObject PrepareLivingBody(GameObject prefab, GameObject source, int player)
        {
            GameObject root = CloneBody(prefab);
            if (player < 0) return root;

            PlayerNetworkController avatar = source.GetComponent<PlayerNetworkController>();
            Transform model = avatar ? avatar.Model : null;
            if (!model) return root;

            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                renderer.enabled = false;
            }

            Dictionary<string, Transform> physical = root.GetComponentsInChildren<Transform>(true).GroupBy(node => node.name)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            GameObject visual = SandboxAssets.CloneVisual(model.gameObject, "NullFrameBodyModel");
            visual.transform.SetParent(root.transform, false);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = model.localScale;

            foreach (Behaviour behaviour in visual.GetComponentsInChildren<Behaviour>(true))
            {
                behaviour.enabled = false;
            }

            Transform[] bones = visual.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .SelectMany(renderer => renderer.bones).Where(node => node).DistinctBy(node => node.Pointer).ToArray();
            foreach (Transform bone in bones)
            {
                if (!physical.TryGetValue(bone.name, out var target)) continue;

                bone.SetParent(target, false);
                bone.localPosition = Vector3.zero;
                bone.localRotation = Quaternion.identity;
                bone.localScale = Vector3.one;
            }

            return root;
        }

        internal bool CreateLiving(GameObject prefab, GameObject source, int player, out int id)
        {
            id = 0;
            if (_bodies.Count >= 512) return false;

            GameObject root = PrepareLivingBody(prefab, source, player);
            root.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
            root.transform.localScale = source.transform.lossyScale;
            Dictionary<string, Transform> sourceBones = source.GetComponentsInChildren<Transform>(true).GroupBy(value => value.name).ToDictionary(group => group.Key, group => group.First());

            foreach (Rigidbody part in root.GetComponentsInChildren<Rigidbody>(true))
            {
                if (sourceBones.TryGetValue(part.name, out var original))
                {
                    part.transform.SetPositionAndRotation(original.position, original.rotation);
                }

                part.isKinematic = false;
            }

            int previous = _sourcePlayer;
            RagdollSkin skin = _sourceSkin;

            try
            {
                _creatingLiving = true;
                _sourcePlayer = player;
                _sourceSkin = player >= 0 ? RagdollSkin.Capture(player, SandboxWorld.Avatar(player)?.Damage) : null;
                root.SetActive(true);
                EnsurePhysics(root);
                Register(root, player < 0 ? source.GetComponent<NetworkObject>()?.ObjectId ?? -1 : -1);
                Body body = _bodies.Values.FirstOrDefault(body => body.Root == root);
                id = body?.Definition.Id ?? 0;
            }
            finally
            {
                _creatingLiving = false;
                _sourcePlayer = previous;
                _sourceSkin = skin;
            }

            if (id == 0)
            {
                Object.Destroy(root);
            }

            return id > 0;
        }

        // Every ragdoll that appears in the scene is registered here. The host publishes it,
        // clients drop their own copy of a ragdoll once the game spawns the real one.
        internal void Register(GameObject root, int npc = -1)
        {
            if (!root || !root.scene.IsValid() || _local.ContainsKey(root.Pointer)) return;

            Rigidbody[] physics = root.GetComponentsInChildren<Rigidbody>(true).ToArray();
            if (physics.Length is < 1 or > 64) return;

            if (_world.IsHost && _bodies.Count >= 512 && !RemoveOldestCorpse()) return;

            _local[root.Pointer] = root;
            _players[root.Pointer] = _sourcePlayer;

            if (_world.IsHost)
            {
                Publish(root, physics, npc);
            }
            else
            {
                DropCopiesOf(root);
            }
        }

        private static string BodyName(GameObject root) => root.name.Replace("(Clone)", "").Trim();

        // A room keeps up to 512 ragdolls. Living ragdolls and held corpses are never removed.
        private bool RemoveOldestCorpse()
        {
            var oldest = _bodies.Where(pair => !pair.Value.Definition.Living && !_world.Hands.HeldObjects.Contains(pair.Key))
                .OrderBy(pair => pair.Value.CreatedAt).FirstOrDefault();
            if (oldest.Value == null) return false;

            Remove(oldest.Key);
            _world.Send(_world.Message(SandboxKind.RemoveRagdoll) with { Id = oldest.Key });
            _world.SetStatus("Ragdoll limit reached: the oldest unheld corpse was removed (512 per room).");
            return true;
        }

        private void DropCopiesOf(GameObject root)
        {
            string name = BodyName(root);
            foreach (Body copy in _bodies.Values.Where(body => body.Copy &&
                body.Root &&
                body.Definition.Name == name &&
                (body.Definition.Player < 0 || body.Definition.Player == _sourcePlayer) &&
                Vector3.Distance(root.transform.position, SandboxWorld.Vector(body.Definition.Position)) < 8f))
            {
                _bound.Remove(copy.Root.Pointer);
                copy.Visual?.Dispose();
                copy.Visual = null;
                Object.Destroy(copy.Root);
                copy.Root = null;
                copy.Bones = null;
                copy.Copy = false;
            }
        }

        private void Publish(GameObject root, Rigidbody[] physics, int npc)
        {
            string[] paths = physics.Select(body => Path(root.transform, body.transform)).ToArray();

            RagdollDefinition definition = new(
                _nextId++,
                BodyName(root),
                SandboxWorld.Point(root.transform.position),
                paths,
                _sourcePlayer,
                _sourceSkin,
                _creatingLiving,
                SandboxWorld.Point(root.transform.localScale),
                npc);

            Body state = new()
            {
                Definition = definition,
                Root = root,
                Physics = physics,
                CreatedAt = Time.unscaledTime,
                Bones = physics.Select(body => body.transform).ToArray()
            };

            if (definition.Skin != null)
            {
                state.Visual = new RagdollVisual(root, definition.Skin, _world.Assets.Textures);
            }

            Stabilize(root, physics);
            _bodies[definition.Id] = state;
            _world.Send(_world.Message(SandboxKind.Ragdoll) with { Ragdoll = definition });
        }

        private static void Stabilize(GameObject root, Rigidbody[] physics)
        {
            foreach (Rigidbody part in physics)
            {
                part.solverIterations = Mathf.Max(part.solverIterations, 12);
                part.solverVelocityIterations = Mathf.Max(part.solverVelocityIterations, 4);
                part.maxDepenetrationVelocity = 3f;
                part.maxAngularVelocity = 12f;
            }

            Collider[] colliders = root.GetComponentsInChildren<Collider>(true).Where(value => !value.isTrigger).ToArray();
            for (int first = 0; first < colliders.Length; first++)
            {
                for (int second = first + 1; second < colliders.Length; second++)
                {
                    Physics.IgnoreCollision(colliders[first], colliders[second], true);
                }
            }

            foreach (CharacterJoint joint in root.GetComponentsInChildren<CharacterJoint>(true))
            {
                joint.enableCollision = false;
                joint.enableProjection = true;
                joint.projectionDistance = 0.1f * Mathf.Max(0.25f, root.transform.lossyScale.y);
                joint.projectionAngle = 15f;
            }
        }

        internal void DisplayGrip(int id, int bone, Vector3 localPoint, Vector3 target, Quaternion rotation)
        {
            if (!_bodies.TryGetValue(id, out var body) || !body.Root || body.Physics == null || bone < 0 || bone >= body.Physics.Length)
            {
                return;
            }

            Rigidbody anchor = body.Physics[bone];
            if (!anchor)
            {
                return;
            }

            Vector3 position = RigidGrip.Position(anchor, localPoint, target, rotation);
            Quaternion delta = rotation * Quaternion.Inverse(anchor.rotation);
            Vector3 origin = anchor.position;
            var poses = body.Bones.Where(value => value).Select(value =>
                (Bone: value, Position: position + delta * (value.position - origin), Rotation: delta * value.rotation)).ToArray();
            foreach (var pose in poses)
            {
                pose.Bone.SetPositionAndRotation(pose.Position, pose.Rotation);
            }
        }

        internal void Define(RagdollDefinition definition)
        {
            if (_bodies.ContainsKey(definition.Id) || _bodies.Count >= 512)
            {
                return;
            }

            _bodies[definition.Id] = new Body

            {
                Definition = definition,
                CreatedAt = Time.unscaledTime,
                Motion = Enumerable.Range(0, definition.Bones.Length).Select(_ => new PhysicsBuffer()).ToArray(),
                Sequences = new int[definition.Bones.Length]
            };
        }

        internal bool Contains(int id) => _bodies.ContainsKey(id);

        internal void Snapshot(NetworkConnection connection)
        {
            foreach (Body body in _bodies.Values.Where(body => body.Root))
            {
                _world.Queue(_world.Message(SandboxKind.Ragdoll) with
                {
                    Ragdoll = body.Definition
                }, connection);

                for (int offset = 0; offset < body.Bones.Length; offset += PhysicsPacket.MaxPoses)
                {
                    PhysicsPose[] poses = body.Bones.Skip(offset).Take(PhysicsPacket.MaxPoses).Select(
                        (bone, index) =>
                        new PhysicsPose(
                            body.Definition.Id,
                            (byte)(offset + index),
                            SandboxWorld.Point(bone.position),
                            SandboxWorld.Rotation(bone.rotation))).ToArray();

                    _world.Queue(_world.Message(SandboxKind.PoseBatch) with
                    {
                        Sequence = 1,
                        Data = PhysicsPacket.Encode(poses)
                    }, connection);
                }
            }
        }

        private static string Path(Transform root, Transform node)
        {
            List<int> indices = new();
            while (node && node != root)
            {
                indices.Add(node.GetSiblingIndex());
                node = node.parent;
            }

            indices.Reverse();

            return string.Join("/", indices);
        }

        private static Transform Resolve(Transform root, string path)
        {
            if (path.Length == 0) return root;

            foreach (string part in path.Split('/'))
            {
                if (!int.TryParse(part, out var index) || index < 0 || index >= root.childCount)
                {
                    return null;
                }

                root = root.GetChild(index);
            }

            return root;
        }

        // Connects a host ragdoll to the matching local one. If the game did not spawn one here,
        // the client makes its own copy after a second. The bones then follow the host's poses.
        private bool Bind(Body body)
        {
            RagdollDefinition definition = body.Definition;
            GameObject root = FindLocal(definition);
            if (!root)
            {
                if (Time.unscaledTime - body.CreatedAt < 1f) return false;

                root = CreateCopy(definition);
                if (!root) return false;

                body.Copy = true;
            }

            Transform[] bones = definition.Bones.Select(path => Resolve(root.transform, path)).ToArray();
            if (bones.Any(bone => !bone))
            {
                if (body.Copy)
                {
                    Object.Destroy(root);
                }

                return false;
            }

            body.Root = root;
            body.Bones = bones;
            body.Physics = bones.Select(value => value.GetComponent<Rigidbody>()).ToArray();

            foreach (Rigidbody value in body.Physics)
            {
                if (!value) continue;

                value.isKinematic = true;
                value.useGravity = false;
                value.detectCollisions = true;
            }

            _bound.Add(root.Pointer);
            if (definition.Skin != null)
            {
                body.Visual = new RagdollVisual(root, definition.Skin, _world.Assets.Textures);
            }

            return true;
        }

        // The closest unbound local ragdoll with the same name and player, within 8 metres.
        private GameObject FindLocal(RagdollDefinition definition)
        {
            Vector3 position = SandboxWorld.Vector(definition.Position);
            return _local.Values.Where(value => value &&
                !_bound.Contains(value.Pointer) &&
                BodyName(value) == definition.Name &&
                (definition.Player < 0 || _players.GetValueOrDefault(value.Pointer, -1) == definition.Player) &&
                Vector3.Distance(value.transform.position, position) < 8f)
                .OrderBy(value => Vector3.Distance(value.transform.position, position)).FirstOrDefault();
        }

        private GameObject CreateCopy(RagdollDefinition definition)
        {
            PlayerNetworkController player = definition.Living && definition.Player >= 0 ? SandboxWorld.Avatar(definition.Player) : null;
            GameObject prefab = FindPrefab(definition, player);
            if (!prefab) return null;

            GameObject root = player ? PrepareLivingBody(prefab, player.gameObject, definition.Player) : CloneBody(prefab);
            root.transform.position = SandboxWorld.Vector(definition.Position);

            if (definition.Scale != null)
            {
                root.transform.localScale = SandboxWorld.Vector(definition.Scale);
            }

            root.SetActive(true);
            EnsurePhysics(root);
            _local[root.Pointer] = root;
            _players[root.Pointer] = definition.Player;
            return root;
        }

        // Living NPC ragdolls use the NPC's prefab, living player ragdolls the player's prefab,
        // everything else the loaded prefab with the same name and bone count.
        private static GameObject FindPrefab(RagdollDefinition definition, PlayerNetworkController player)
        {
            if (definition.Living && definition.NPC >= 0)
            {
                GameObject source = SandboxWorld.Spawned(definition.NPC);
                Bot npc = source ? source.GetComponent<Bot>() : null;
                if (npc && npc.RagdollPrefab) return npc.RagdollPrefab.gameObject;

                BossBot boss = source ? source.GetComponent<BossBot>() : null;
                if (boss && boss.RagdollPrefab) return boss.RagdollPrefab.gameObject;
            }

            if (player && player.Damage && player.Damage.RagdollPrefab) return player.Damage.RagdollPrefab.gameObject;

            return Resources.FindObjectsOfTypeAll<GameObject>().FirstOrDefault(value => !value.scene.IsValid() &&
                value.name == definition.Name &&
                value.GetComponentsInChildren<Rigidbody>(true).Length == definition.Bones.Length);
        }

        private void Scan()
        {
            foreach (RagdollController controller in Object.FindObjectsOfType<RagdollController>())
            {
                Register(controller.gameObject);
            }

            foreach (CharacterJoint joint in Object.FindObjectsOfType<CharacterJoint>())
            {
                if (joint.transform.root.name.Contains("rag", StringComparison.OrdinalIgnoreCase))
                {
                    Register(joint.transform.root.gameObject);
                }
            }
        }

        internal void Update()
        {
            if (!_world.Active) return;

            if (Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + 1f;
                Scan();
                ForgetDestroyed();
            }

            if (!_world.IsHost)
            {
                BindCopies();
                return;
            }

            if (Time.unscaledTime < _poseAt) return;

            _poseAt = Time.unscaledTime + 0.05f;
            foreach (var pair in _bodies.ToArray())
            {
                SendPose(pair.Key, pair.Value);
            }
        }

        private void ForgetDestroyed()
        {
            foreach (IntPtr pointer in _local.Where(pair => !pair.Value).Select(pair => pair.Key).ToArray())
            {
                _local.Remove(pointer);
                _players.Remove(pointer);
            }
        }

        // A client retries binding unbound ragdolls once per second.
        private void BindCopies()
        {
            foreach (Body body in _bodies.Values)
            {
                if (!body.Root && body.Bones != null)
                {
                    body.Visual?.Dispose();
                    body.Visual = null;
                    body.Bones = null;
                    body.Copy = false;
                }

                if (body.Bones == null && Time.unscaledTime >= body.BindAt)
                {
                    body.BindAt = Time.unscaledTime + 1f;
                    Bind(body);
                }
            }
        }

        // The host removes expired and destroyed ragdolls and sends the poses of the rest:
        // 20 times per second while they move, twice per second while they sleep.
        private void SendPose(int id, Body body)
        {
            if (body.Root && !body.Definition.Living && !_world.Environment.KeepRagdolls && body.DestroyAt > 0f &&
                Time.unscaledTime >= body.DestroyAt && !_world.Hands.HeldObjects.Contains(id))
            {
                Remove(id);
                _world.Send(_world.Message(SandboxKind.RemoveRagdoll) with { Id = id });
                return;
            }

            if (!body.Root)
            {
                _bodies.Remove(id);
                body.Visual?.Dispose();
                _world.Physics.Forget(id);
                _world.Send(_world.Message(SandboxKind.RemoveRagdoll) with { Id = id });
                return;
            }

            if (Time.unscaledTime < body.NextSend) return;

            bool sleeping = body.Physics.All(value => !value || value.IsSleeping());
            body.NextSend = Time.unscaledTime + (sleeping ? 0.5f : 0.05f);

            for (byte index = 0; index < body.Bones.Length; index++)
            {
                _world.Physics.Capture(id, index, body.Bones[index].position, body.Bones[index].rotation);
            }
        }

        internal void Pose(PhysicsPose pose, int sequence, double stamp)
        {
            if (!_bodies.TryGetValue(pose.Id, out var body) ||
                pose.Bone >= body.Definition.Bones.Length ||
                sequence <= body.Sequences[pose.Bone])
            {
                return;
            }

            if (body.Motion[pose.Bone].Add(stamp, sequence, pose.Position, pose.Rotation))
            {
                body.Sequences[pose.Bone] = sequence;
            }
        }

        internal void Pose(SandboxMessage message)
        {
            if (!_bodies.TryGetValue(message.Id, out var body) ||
                !SandboxPose.TryDecode(message.Data, out var offset, out var values))
            {
                return;
            }

            if (body.Bones == null && !Bind(body) ||
                offset + values.Length / 7 > body.Bones.Length)
            {
                return;
            }

            double stamp = _world.Physics.Timestamp(message);
            for (int index = 0; index < values.Length / 7; index++)
            {
                int bone = offset + index;
                if (message.Sequence <= body.Sequences[bone]) continue;

                Pose(new PhysicsPose(message.Id, (byte)bone,
                    new Point3(values[index * 7], values[index * 7 + 1], values[index * 7 + 2]),
                    new Rotation4(values[index * 7 + 3], values[index * 7 + 4], values[index * 7 + 5], values[index * 7 + 6])),
                    message.Sequence, stamp);
            }
        }

        internal void LateUpdate()
        {
            if (!_world.Active) return;

            foreach (Body body in _bodies.Values)
            {
                body.Visual?.Update();
            }

            if (_world.IsHost) return;

            foreach (Body body in _bodies.Values)
            {
                if (body.Root && body.Bones != null)
                {
                    for (int index = 0; index < body.Bones.Length; index++)
                    {
                        if (body.Bones[index] && body.Motion[index].Sample(_world.Physics.RenderTime, out var pose))
                        {
                            body.Bones[index].SetPositionAndRotation(
                                new Vector3(pose.Position.X, pose.Position.Y, pose.Position.Z),
                                new Quaternion(pose.Rotation.X, pose.Rotation.Y, pose.Rotation.Z, pose.Rotation.W));
                        }
                    }
                }
            }
        }

        internal void Remove(int id)
        {
            _world.Physics.Forget(id);
            if (!_bodies.Remove(id, out var body)) return;

            body.Visual?.Dispose();

            if (!body.Root) return;

            _bound.Remove(body.Root.Pointer);
            Object.Destroy(body.Root);
        }

        internal void Clear()
        {
            foreach (Body body in _bodies.Values)
            {
                body.Visual?.Dispose();
                if (!body.Root) continue;

                Object.Destroy(body.Root);
            }

            _bodies.Clear();
            _local.Clear();
            _players.Clear();
            _bound.Clear();
            _nextScan = _poseAt = 0f;
        }
    }
}
