using Il2Cpp;
using Il2CppFishNet.Object;
using NullServers.Network;
using NullServers.Sandbox;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace NullServers.Room
{
    internal sealed class WorldGeometry
    {
        private sealed class Part
        {
            internal Transform Transform, Parent;
            internal int Sibling;
            internal bool Framed;
            internal TerrainPart[] Terrains;
            internal WorldBatch Batch;
            internal MapPart Original;
            internal Vector3 Scale;
            internal Renderer[] Renderers;
            internal Collider[] Colliders;
            internal bool[] Visible, Solid;
            internal Material[][] Materials;
            internal readonly List<Material> Copies = new();
        }

        private sealed class TerrainPart
        {
            internal Terrain Terrain;
            internal TerrainCollider Collider;
            internal TerrainData Original, Copy;
            internal Vector3 Scale, Size;
            internal bool Visible;
        }

        private readonly Dictionary<int, List<GameObject>> _sceneRoots = new();
        private readonly Dictionary<IntPtr, int> _rootIndexes = new();
        private readonly Dictionary<int, int> _rootCounts = new();
        private readonly Dictionary<int, Transform> _roots = new();
        private readonly Dictionary<IntPtr, Transform> _parents = new();
        private readonly HashSet<IntPtr> _captured = new();
        private float _scanAt;
        private readonly Dictionary<string, Part> _parts = new(StringComparer.Ordinal);
        private WorldSettings _applied;
        private Vector3 _gravity;
        private int _scene = -1;
        private bool _active;
        internal string Status { get; private set; }

        internal bool Validate(WorldSettings settings, out string error)
        {
            error = null;
            if (settings?.Parts != null && settings.Parts.Any(part => !_parts.ContainsKey(part.Path)))
            {
                error = "Select an available static map part.";
                return false;
            }

            if (_parts.Values.Any(part => part.Terrains.Length > 0) && settings != null &&
                (SandboxWorld.Vector(settings.Rotation).sqrMagnitude > 0.0001f || settings.Parts?.Any(change =>
                _parts[change.Path].Terrains.Length > 0 && Quaternion.Angle(SandboxWorld.Quaternion(change.Rotation),
                SandboxWorld.Quaternion(_parts[change.Path].Original.Rotation)) > 0.01f) == true))
            {
                error = "Unity Terrain supports resizing; rotating a heightfield requires converting the terrain to a mesh.";
                return false;
            }

            return true;
        }

        private Transform Frame(Transform original, Scene scene)
        {
            if (!_roots.TryGetValue(scene.handle, out var root) || !root)
            {
                GameObject holder = new("NullFrameWorldGeometry");
                SceneManager.MoveGameObjectToScene(holder, scene);
                _roots[scene.handle] = root = holder.transform;
            }

            if (!original)
            {
                return root;
            }

            if (_parents.TryGetValue(original.Pointer, out var copy) && copy)
            {
                return copy;
            }

            GameObject proxy = new("NullFrameWorldFrame");
            copy = proxy.transform;
            copy.SetParent(Frame(original.parent, scene), false);
            copy.localPosition = original.localPosition;
            copy.localRotation = original.localRotation;
            copy.localScale = original.localScale;
            _parents[original.Pointer] = copy;
            return copy;
        }

        private string Key(Transform node)
        {
            List<int> path = new();
            Scene scene = node.gameObject.scene;

            while (node.parent)
            {
                path.Add(node.GetSiblingIndex());
                node = node.parent;
            }

            path.Add(_rootIndexes[node.gameObject.Pointer]);
            uint hash = 2166136261;

            foreach (char value in scene.path.Length > 0 ? scene.path : scene.name)
            {
                hash = unchecked((hash ^ value) * 16777619);
            }

            path.Add((int)(hash & int.MaxValue));
            path.Reverse();
            return string.Join("/", path);
        }

        private static bool Allowed(Transform node)
        {
            if (node.GetComponentInParent<NetworkObject>() || node.GetComponentInParent<Canvas>() || node.GetComponentInParent<Camera>() ||
                node.GetComponentInParent<FPScontroller>() || node.GetComponentInParent<RagdollController>() ||
                node.GetComponentsInChildren<NetworkObject>(true).Length > 0 || node.GetComponentsInChildren<Camera>(true).Length > 0 ||
                node.GetComponentsInChildren<Canvas>(true).Length > 0)
            {
                return false;
            }

            for (Transform parent = node; parent; parent = parent.parent)
            {
                if (parent.name.StartsWith("NullFrame", StringComparison.Ordinal) || parent.name is "BuildGhost" or "BuildSelection")
                {
                    return false;
                }
            }

            return !node.GetComponentInParent<Rigidbody>() && node.GetComponentsInChildren<Rigidbody>(true).Length == 0;
        }

        private static Transform GeometryRoot(Transform node)
        {
            Collider collider = node.GetComponent<Collider>();
            Transform parent = node.parent;

            if (!collider || collider.isTrigger || node.GetComponent<Renderer>() || !parent || !Allowed(parent))
            {
                return node;
            }

            Renderer[] renderers = parent.GetComponentsInChildren<Renderer>(true).ToArray();
            if (renderers.Length != 1 || !renderers[0].enabled)
            {
                return node;
            }

            Bounds visual = renderers[0].bounds, physical = collider.bounds;
            float tolerance = Mathf.Max(0.01f, visual.size.magnitude * 0.001f);
            return (visual.center - physical.center).magnitude <= tolerance && (visual.size - physical.size).magnitude <= tolerance ? parent : node;
        }

        private void Capture(Scene scene)
        {
            GameObject[] roots = scene.GetRootGameObjects().ToArray();
            if (!_sceneRoots.TryGetValue(scene.handle, out var original))
            {
                _sceneRoots[scene.handle] = original = new List<GameObject>();
            }

            foreach (GameObject root in roots)
            {
                if (!root.name.StartsWith("NullFrame", StringComparison.Ordinal) && !_rootIndexes.ContainsKey(root.Pointer))
                {
                    _rootIndexes[root.Pointer] = original.Count;
                    original.Add(root);
                }
            }

            Transform[] all = roots.Where(root => !root.name.StartsWith("NullFrame", StringComparison.Ordinal))
                .SelectMany(root => root.GetComponentsInChildren<Transform>(true)).ToArray();
            var nodes = all.Where(node => node.GetComponent<Renderer>() && !node.GetComponent<ParticleSystemRenderer>() || node.GetComponent<Terrain>() || node.GetComponent<Collider>() ||
                node.name.Contains("spawn", StringComparison.OrdinalIgnoreCase) || node.gameObject.tag.Contains("spawn", StringComparison.OrdinalIgnoreCase))
                .Where(Allowed).Select(GeometryRoot).GroupBy(node => node.Pointer).Select(group => group.First()).ToArray();
            var pointers = nodes.Select(node => node.Pointer).ToHashSet();
            List<Part> added = new();

            foreach (Transform node in nodes)
            {
                if (_captured.Contains(node.Pointer))
                {
                    continue;
                }

                bool child = false;
                for (Transform parent = node.parent; parent; parent = parent.parent)
                {
                    if (pointers.Contains(parent.Pointer))
                    {
                        child = true;
                        break;
                    }
                }

                if (child)
                {
                    continue;
                }

                string key = Key(node);
                Renderer[] renderers = node.GetComponentsInChildren<Renderer>(true).ToArray();
                Collider[] colliders = node.GetComponentsInChildren<Collider>(true).ToArray();
                _parts[key] = new Part
                {
                    Transform = node,
                    Parent = node.parent,
                    Sibling = node.parent ? node.GetSiblingIndex() : _rootIndexes[node.gameObject.Pointer],
                    Terrains = node.GetComponentsInChildren<Terrain>(true).Select(terrain => new TerrainPart
                    {
                        Terrain = terrain,
                        Collider = terrain.GetComponent<TerrainCollider>(),
                        Original = terrain.terrainData,
                        Scale = terrain.transform.lossyScale,
                        Size = terrain.terrainData.size,
                        Visible = terrain.enabled
                    }).ToArray(),
                    Scale = node.localScale,
                    Original = new MapPart(key, SandboxWorld.Point(node.localPosition), SandboxWorld.Rotation(node.localRotation), SandboxWorld.Point(node.localScale)),
                    Renderers = renderers,
                    Batch = new WorldBatch(node, renderers),
                    Colliders = colliders,
                    Visible = renderers.Select(value => value.enabled).ToArray(),
                    Solid = colliders.Select(value => value.enabled).ToArray(),
                    Materials = renderers.Select(value => value.sharedMaterials.ToArray()).ToArray()
                };
                _captured.Add(node.Pointer);
                added.Add(_parts[key]);
            }

            if (added.Count > 0)
            {
                _applied = null;
            }

            Status = _parts.Count + " static map parts available.";
        }

        internal MapPart Aimed(Camera camera)
        {
            if (!camera || !Physics.Raycast(new Ray(camera.transform.position, camera.transform.forward), out RaycastHit hit, 120f, ~(1 << 2), QueryTriggerInteraction.Ignore))
            {
                return null;
            }

            var pair = _parts.FirstOrDefault(pair => pair.Value.Transform && (hit.transform == pair.Value.Transform || hit.transform.IsChildOf(pair.Value.Transform)));
            return pair.Value == null ? null : _applied?.Parts?.FirstOrDefault(part => part.Path == pair.Key) ?? pair.Value.Original;
        }

        internal string Name(MapPart part) => part != null && _parts.TryGetValue(part.Path, out var value) && value.Transform ? value.Transform.name : "No static map part selected";

        internal void Update(bool active, WorldSettings settings)
        {
            Scene scene = SceneManager.GetActiveScene();
            if (_scene != scene.handle || active != _active)
            {
                Restore();
                _parts.Clear();
                _captured.Clear();
                _parents.Clear();
                _roots.Clear();
                _rootCounts.Clear();
                _sceneRoots.Clear();
                _rootIndexes.Clear();
                _scanAt = 0f;
                _applied = null;
                _scene = scene.handle;
                _active = active;
                _gravity = Physics.gravity;

                if (active)
                {
                    _scanAt = Time.unscaledTime;
                }
            }

            if (active && Time.unscaledTime >= _scanAt)
            {
                _scanAt = Time.unscaledTime + 1f;
                for (int index = 0; index < SceneManager.sceneCount; index++)
                {
                    Scene loaded = SceneManager.GetSceneAt(index);
                    if (!loaded.isLoaded || loaded.name is "MainMenu" or "Updater")
                    {
                        continue;
                    }

                    int count = loaded.GetRootGameObjects().Length;
                    if (!_rootCounts.TryGetValue(loaded.handle, out var previous) || previous != count)
                    {
                        Capture(loaded);
                        _rootCounts[loaded.handle] = loaded.GetRootGameObjects().Length;
                    }
                }
            }

            if (!active || settings == _applied)
            {
                return;
            }

            if (settings?.Valid == false)
            {
                return;
            }

            WorldSettings world = settings ?? WorldSettings.Default;
            Quaternion rotation = Quaternion.Euler(SandboxWorld.Vector(world.Rotation));
            Vector3 scale = SandboxWorld.Vector(world.Scale);

            if (scale == Vector3.one && SandboxWorld.Vector(world.Rotation).sqrMagnitude < 0.0001f && (world.Parts?.Length ?? 0) == 0)
            {
                Restore();
                Physics.gravity = settings == null ? _gravity : SandboxWorld.Vector(world.Gravity);
                _applied = settings;
                return;
            }

            foreach (Part part in _parts.Values)
            {
                if (!part.Transform || part.Framed)
                {
                    continue;
                }

                part.Transform.SetParent(Frame(part.Parent, part.Transform.gameObject.scene), false);
                part.Framed = true;
            }

            foreach (Transform root in _roots.Values)
            {
                if (!root)
                {
                    continue;
                }

                root.localRotation = rotation;
                root.localScale = scale;
            }

            foreach (var pair in _parts)
            {
                Part part = pair.Value;
                if (!part.Transform)
                {
                    continue;
                }

                MapPart change = world.Parts?.FirstOrDefault(value => value.Path == pair.Key);
                part.Transform.localPosition = change == null ? SandboxWorld.Vector(part.Original.Position) : SandboxWorld.Vector(change.Position);
                part.Transform.localRotation = change == null ? SandboxWorld.Quaternion(part.Original.Rotation) : SandboxWorld.Quaternion(change.Rotation);
                part.Transform.localScale = change == null ? part.Scale : SandboxWorld.Vector(change.Scale);
                part.Batch.Apply();

                foreach (TerrainPart terrain in part.Terrains)
                {
                    if (!terrain.Terrain)
                    {
                        continue;
                    }

                    Vector3 current = terrain.Terrain.transform.lossyScale;
                    Vector3 size = Vector3.Scale(terrain.Size, new Vector3(current.x / terrain.Scale.x, current.y / terrain.Scale.y, current.z / terrain.Scale.z));
                    terrain.Terrain.enabled = terrain.Visible && (change?.Visible ?? true);

                    if (!terrain.Copy && (size - terrain.Size).sqrMagnitude < 0.0001f)
                    {
                        continue;
                    }

                    if (!terrain.Copy)
                    {
                        terrain.Copy = Object.Instantiate(terrain.Original);
                        terrain.Terrain.terrainData = terrain.Copy;

                        if (terrain.Collider)
                        {
                            terrain.Collider.terrainData = terrain.Copy;
                        }
                    }

                    terrain.Copy.size = size;
                    terrain.Terrain.enabled = terrain.Visible && (change?.Visible ?? true);
                    terrain.Terrain.Flush();
                }

                for (int index = 0; index < part.Renderers.Length; index++)
                {
                    if (part.Renderers[index])
                    {
                        part.Renderers[index].enabled = part.Visible[index] && (change?.Visible ?? true);
                    }
                }

                for (int index = 0; index < part.Colliders.Length; index++)
                {
                    if (part.Colliders[index])
                    {
                        part.Colliders[index].enabled = part.Solid[index] && (change?.Solid ?? true);
                    }
                }

                foreach (Material material in part.Copies)
                {
                    if (material)
                    {
                        Object.Destroy(material);
                    }
                }

                part.Copies.Clear();
                for (int index = 0; index < part.Renderers.Length; index++)
                {
                    if (!part.Renderers[index])
                    {
                        continue;
                    }

                    if (change == null || change.Color == "#FFFFFF")
                    {
                        part.Renderers[index].sharedMaterials = part.Materials[index];
                    }
                    else
                    {
                        ColorUtility.TryParseHtmlString(change.Color, out Color color);
                        var materials = part.Materials[index].Select(original => original ? new Material(original) : null).ToArray();

                        foreach (Material material in materials)
                        {
                            if (!material)
                            {
                                continue;
                            }

                            if (material.HasProperty("_Color"))
                            {
                                material.color = color;
                            }

                            if (material.HasProperty("_BaseColor"))
                            {
                                material.SetColor("_BaseColor", color);
                            }

                            part.Copies.Add(material);
                        }

                        part.Renderers[index].sharedMaterials = materials;
                    }
                }
            }

            Physics.gravity = settings == null ? _gravity : SandboxWorld.Vector(world.Gravity);
            Physics.SyncTransforms();
            _applied = settings;
        }

        private void Restore()
        {
            if (_active)
            {
                Physics.gravity = _gravity;
            }

            foreach (Part part in _parts.Values)
            {
                part.Batch.Restore();
                if (part.Transform)
                {
                    if (part.Framed)
                    {
                        part.Transform.SetParent(part.Parent, false);
                        part.Transform.SetSiblingIndex(part.Sibling);
                        part.Framed = false;
                    }

                    part.Transform.localPosition = SandboxWorld.Vector(part.Original.Position);
                    part.Transform.localRotation = SandboxWorld.Quaternion(part.Original.Rotation);
                    part.Transform.localScale = part.Scale;
                }

                foreach (TerrainPart terrain in part.Terrains)
                {
                    if (terrain.Terrain)
                    {
                        terrain.Terrain.terrainData = terrain.Original;
                        terrain.Terrain.enabled = terrain.Visible;
                    }

                    if (terrain.Collider)
                    {
                        terrain.Collider.terrainData = terrain.Original;
                    }

                    if (terrain.Copy)
                    {
                        Object.Destroy(terrain.Copy);
                    }

                    terrain.Copy = null;
                }

                for (int index = 0; index < part.Renderers.Length; index++)
                {
                    if (part.Renderers[index])
                    {
                        part.Renderers[index].enabled = part.Visible[index];
                        part.Renderers[index].sharedMaterials = part.Materials[index];
                    }
                }

                for (int index = 0; index < part.Colliders.Length; index++)
                {
                    if (part.Colliders[index])
                    {
                        part.Colliders[index].enabled = part.Solid[index];
                    }
                }

                foreach (Material material in part.Copies)
                {
                    if (material)
                    {
                        Object.Destroy(material);
                    }
                }

                part.Copies.Clear();
            }

            foreach (Transform root in _roots.Values)
            {
                if (root)
                {
                    Object.Destroy(root.gameObject);
                }
            }

            _roots.Clear();
            _parents.Clear();
        }
    }
}
