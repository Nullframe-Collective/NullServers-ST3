using HarmonyLib;
using Il2Cpp;
using Il2CppFishNet.Connection;
using Il2CppInterop.Runtime;
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
            internal Vector3[] Positions;
            internal Quaternion[] Rotations;
            internal int[] Sequences;
            internal (bool Kinematic, bool Gravity, bool Collisions)[] Original;
            internal bool Copy;
            internal float NextSend;
            internal float CreatedAt;
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

        internal RagdollSync(HarmonyLib.Harmony harmony, SandboxWorld world)
        {
            _instance = this;
            _world = world;

            harmony.Patch(
                AccessTools.Method(typeof(RagdollController), nameof(RagdollController.Awake)),
                postfix: new HarmonyMethod(typeof(RagdollSync), nameof(Created)));

            harmony.Patch(
                AccessTools.Method(typeof(PlayerDamage), nameof(PlayerDamage.KDDDGBFEFOC)),
                prefix: new HarmonyMethod(typeof(RagdollSync), nameof(BeforeDeath)),
                finalizer: new HarmonyMethod(typeof(RagdollSync), nameof(AfterDeath)));
        }

        private static void BeforeDeath(PlayerDamage __instance, out int __state)
        {
            __state = _instance._sourcePlayer;
            _instance._sourcePlayer = SandboxWorld.DamageOwner(__instance);
        }

        private static Exception AfterDeath(PlayerDamage __instance, Exception __exception, int __state)
        {
            try
            {
                if (__exception == null && _instance._world.Active && __instance && (float)__instance.PIIDNIGPCDK <= 0f)
                {
                    _instance.Scan();
                }
            }
            finally
            {
                _instance._sourcePlayer = __state;
            }

            return __exception;
        }

        private static void Created(RagdollController __instance)
        {
            if (_instance._world.Active)
            {
                _instance.Register(__instance.gameObject);
            }
        }

        internal void Register(GameObject root)
        {
            if (!root || !root.scene.IsValid() || _local.ContainsKey(root.Pointer)) return;

            Rigidbody[] physics = root.GetComponentsInChildren<Rigidbody>(true).ToArray();
            if (physics.Length is < 2 or > 64) return;

            _local[root.Pointer] = root;
            _players[root.Pointer] = _sourcePlayer;

            if (!_world.IsHost)
            {
                foreach 
                (
                    Body copy in _bodies.Values.Where(body => body.Copy &&
                    body.Root &&
                    body.Definition.Name == root.name.Replace("(Clone)", "").Trim() &&
                    (body.Definition.Player < 0 || body.Definition.Player == _sourcePlayer) &&
                    Vector3.Distance(root.transform.position, SandboxWorld.Vector(body.Definition.Position)) < 8f)
                )
                {
                    _bound.Remove(copy.Root.Pointer);
                    Object.Destroy(copy.Root);
                    copy.Root = null;
                    copy.Bones = null;
                    copy.Copy = false;
                }

                return;
            }

            string[] paths = physics.Select(body => Path(root.transform, body.transform)).ToArray();

            RagdollDefinition definition = new
            (
                _nextId++,
                root.name.Replace("(Clone)", "").Trim(),
                SandboxWorld.Point(root.transform.position),
                paths,
                _sourcePlayer
            );

            Body state = new()
            {
                Definition = definition,
                Root = root,
                Physics = physics,
                Bones = physics.Select(body => body.transform).ToArray()
            };

            _bodies[definition.Id] = state;
            _world.Send(_world.Message(SandboxKind.Ragdoll) with
            {
                Ragdoll = definition
            });
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
                Positions = new Vector3[definition.Bones.Length],
                Rotations = new Quaternion[definition.Bones.Length],
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
                    PhysicsPose[] poses = body.Bones.Skip(offset).Take(PhysicsPacket.MaxPoses).Select
                    (
                        (bone, index) =>
                        new PhysicsPose
                        (
                            body.Definition.Id,
                            (byte)(offset + index),
                            SandboxWorld.Point(bone.position),
                            SandboxWorld.Rotation(bone.rotation)
                        )
                    ).ToArray();

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

        private bool Bind(Body body)
        {
            RagdollDefinition definition = body.Definition;
            GameObject root = 
                _local.Values.Where(value => value &&
                !_bound.Contains(value.Pointer) &&
                value.name.Replace("(Clone)", "").Trim() == definition.Name &&
                (definition.Player < 0 || _players.GetValueOrDefault(value.Pointer, -1) == definition.Player) &&
                Vector3.Distance(value.transform.position, SandboxWorld.Vector(definition.Position)) < 8f)
                .OrderBy(value => Vector3.Distance(value.transform.position, SandboxWorld.Vector(definition.Position))).FirstOrDefault();

            if (!root)
            {
                if (Time.unscaledTime - body.CreatedAt < 1f) return false;

                GameObject prefab = 
                    Resources.FindObjectsOfTypeAll<GameObject>().FirstOrDefault(value => !value.scene.IsValid() &&
                    value.name == definition.Name &&
                    value.GetComponentInChildren(Il2CppType.Of<RagdollController>(), true));

                if (!prefab) return false;

                root = SandboxAssets.CloneVisual(prefab, "NullFrameSyncedRagdoll");
                root.transform.position = SandboxWorld.Vector(definition.Position);
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
            body.Physics = root.GetComponentsInChildren<Rigidbody>(true).ToArray();
            body.Original = body.Physics.Select(value => (value.isKinematic, value.useGravity, value.detectCollisions)).ToArray();

            foreach (Rigidbody value in body.Physics)
            {
                value.isKinematic = true;
                value.useGravity = false;
                value.detectCollisions = false;
            }

            for (int index = 0; index < bones.Length; index++)
            {
                if (body.Sequences[index] == 0)
                {
                    body.Positions[index] = bones[index].position;
                    body.Rotations[index] = bones[index].rotation;
                }
            }

            _bound.Add(root.Pointer);

            return true;
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

                foreach (IntPtr pointer in _local.Where(pair => !pair.Value).Select(pair => pair.Key).ToArray())
                {
                    _local.Remove(pointer);
                    _players.Remove(pointer);
                }
            }

            if (!_world.IsHost)
            {
                foreach (Body body in _bodies.Values.Where(body => body.Bones == null))
                {
                    Bind(body);
                }

                return;
            }

            if (Time.unscaledTime < _poseAt) return;

            _poseAt = Time.unscaledTime + 0.1f;

            foreach (var pair in _bodies.ToArray())
            {
                Body body = pair.Value;
                if (!body.Root)
                {
                    _bodies.Remove(pair.Key);
                    _world.Physics.Forget(pair.Key);
                    _world.Send(_world.Message(SandboxKind.RemoveRagdoll) with
                    {
                        Id = pair.Key
                    });

                    continue;
                }

                if (Time.unscaledTime < body.NextSend) continue;

                bool sleeping = body.Physics.All(value => !value || value.IsSleeping());
                body.NextSend = Time.unscaledTime + (sleeping ? 0.5f : 0.1f);

                for (byte index = 0; index < body.Bones.Length; index++)
                {
                    _world.Physics.Capture(pair.Key, index, body.Bones[index].position, body.Bones[index].rotation);
                }
            }
        }

        internal void Pose(PhysicsPose pose, int sequence)
        {
            if 
            (
                !_bodies.TryGetValue(pose.Id, out var body) ||
                pose.Bone >= body.Definition.Bones.Length ||
                sequence <= body.Sequences[pose.Bone]
            )
            {
                return;
            }

            body.Sequences[pose.Bone] = sequence;
            body.Positions[pose.Bone] = SandboxWorld.Vector(pose.Position);
            body.Rotations[pose.Bone] = SandboxWorld.Quaternion(pose.Rotation).normalized;
        }

        internal void Pose(SandboxMessage message)
        {
            if 
            (
                !_bodies.TryGetValue(message.Id, out var body) ||
                !SandboxPose.TryDecode(message.Data, out var offset, out var values)
            )
            {
                return;
            }

            if (body.Bones == null && !Bind(body) || 
                offset + values.Length / 7 > body.Bones.Length)
            {
                return;
            }

            for (int index = 0; index < values.Length / 7; index++)
            {
                int bone = offset + index;
                if (message.Sequence <= body.Sequences[bone]) continue;

                body.Sequences[bone] = message.Sequence;
                body.Positions[bone] = new Vector3(values[index * 7], values[index * 7 + 1], values[index * 7 + 2]);
                body.Rotations[bone] = new Quaternion
                (
                    values[index * 7 + 3],
                    values[index * 7 + 4],
                    values[index * 7 + 5],
                    values[index * 7 + 6]
                );
            }
        }

        internal void LateUpdate()
        {
            if (!_world.Active || _world.IsHost) return;

            foreach (Body body in _bodies.Values)
            {
                if (body.Root && body.Bones != null)
                {
                    for (int index = 0; index < body.Bones.Length; index++)
                    {
                        if (body.Bones[index] && body.Sequences[index] > 0)
                        {
                            body.Bones[index].SetPositionAndRotation(body.Positions[index], body.Rotations[index]);
                        }
                    }
                }
            }
        }

        internal void Remove(int id)
        {
            _world.Physics.Forget(id);
            if (!_bodies.Remove(id, out var body)) return;

            if (!body.Root) return;

            _bound.Remove(body.Root.Pointer);
            Object.Destroy(body.Root);
        }

        internal void Clear()
        {
            foreach (Body body in _bodies.Values)
            {
                if (!body.Root) continue;

                if (body.Copy)
                {
                    Object.Destroy(body.Root);
                }
                else if (body.Original != null)
                {
                    for (int index = 0; index < body.Physics.Length; index++)
                    {
                        if (!body.Physics[index]) continue;

                        body.Physics[index].isKinematic = body.Original[index].Kinematic;
                        body.Physics[index].useGravity = body.Original[index].Gravity;
                        body.Physics[index].detectCollisions = body.Original[index].Collisions;
                    }
                }
            }

            _bodies.Clear();
            _local.Clear();
            _players.Clear();
            _bound.Clear();
            _nextScan = _poseAt = 0f;
        }
    }
}
