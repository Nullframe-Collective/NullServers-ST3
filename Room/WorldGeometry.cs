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
        private readonly Dictionary<IntPtr, int> _nameIndexes = new();
        private readonly Dictionary<int, int> _rootCounts = new();
        private readonly Dictionary<int, Transform> _roots = new();
        private readonly Dictionary<IntPtr, Transform> _parents = new();
        private readonly HashSet<IntPtr> _captured = new();
        private float _scanAt, _fullUntil;
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

        // A part is found by its name path, so the host and every client get the same key. Positions in the hierarchy
        // are not used: network objects (players, NPCs, the room) join the scene at different times on each machine.
        // Siblings with the same name are told apart by their order among themselves.
        // The key is "<scene>/<hash>/<hash>": numbers only, as MapPart.Valid expects.
        private string Key(Transform node)
        {
            Scene scene = node.gameObject.scene;
            List<string> names = new();

            for (Transform current = node; current; current = current.parent)
            {
                names.Add(current.name + "#" + NameIndex(current, scene));
            }

            names.Reverse();
            string path = string.Join("/", names);
            return Hash(scene.path.Length > 0 ? scene.path : scene.name, 2166136261) + "/" + Hash(path, 2166136261) + "/" + Hash(path, 0x9747B28C);
        }

        // How many earlier siblings (or scene roots) share this object's name. Filled once per parent and scan.
        private int NameIndex(Transform node, Scene scene)
        {
            if (_nameIndexes.TryGetValue(node.Pointer, out int index)) return index;

            Transform[] siblings = node.parent
                ? Enumerable.Range(0, node.parent.childCount).Select(node.parent.GetChild).ToArray()
                : scene.GetRootGameObjects().Select(root => root.transform).ToArray();
            Dictionary<string, int> counts = new(StringComparer.Ordinal);

            foreach (Transform sibling in siblings)
            {
                counts.TryGetValue(sibling.name, out int count);
                counts[sibling.name] = count + 1;
                _nameIndexes[sibling.Pointer] = count;
            }

            return _nameIndexes.GetValueOrDefault(node.Pointer);
        }

        private static int Hash(string text, uint seed)
        {
            uint hash = seed;
            foreach (char value in text)
            {
                hash = unchecked((hash ^ value) * 16777619);
            }

            return (int)(hash & int.MaxValue);
        }

        private static Transform GeometryRoot(Transform node)
        {
            Collider collider = node.GetComponent<Collider>();
            Transform parent = node.parent;

            if (!collider || collider.isTrigger || node.GetComponent<Renderer>() || !parent || !WorldGeometryFilter.Allowed(parent))
            {
                return node;
            }

            Bounds physical = collider.bounds;
            for (int depth = 0; depth < 3 && parent && WorldGeometryFilter.Allowed(parent); depth++, parent = parent.parent)
            {
                Renderer[] renderers = parent.GetComponentsInChildren<Renderer>(true).Where(renderer => renderer.enabled).ToArray();
                if (renderers.Length > 8) break;
                if (renderers.Length == 0) continue;

                Bounds visual = renderers[0].bounds;
                foreach (Renderer renderer in renderers)
                {
                    visual.Encapsulate(renderer.bounds);
                }

                visual.Expand(visual.size * 0.1f);
                if (visual.Contains(physical.center))
                {
                    return parent;
                }
            }

            return node;
        }

        // Remembers the original state of every static map part, so the room can change it and later restore it.
        // A normal scan only looks at new scene roots; a full scan looks at all of them again.
        private void Capture(Scene scene, bool full)
        {
            List<GameObject> scanned = ScanRoots(scene, full);
            if (scanned.Count == 0) return;

            _nameIndexes.Clear();

            Transform[] nodes = FindGeometry(scanned);
            HashSet<IntPtr> pointers = nodes.Select(node => node.Pointer).ToHashSet();
            bool added = false;

            foreach (Transform node in nodes)
            {
                if (_captured.Contains(node.Pointer) || InsideOtherPart(node, pointers)) continue;

                string key = Key(node);
                _parts[key] = CapturePart(node, key);
                _captured.Add(node.Pointer);
                added = true;
            }

            if (added)
            {
                _applied = null;
            }

            Status = _parts.Count + " static map parts available.";
        }

        private List<GameObject> ScanRoots(Scene scene, bool full)
        {
            if (!_sceneRoots.TryGetValue(scene.handle, out var original))
            {
                _sceneRoots[scene.handle] = original = new List<GameObject>();
            }

            List<GameObject> scanned = new();
            foreach (GameObject root in scene.GetRootGameObjects().ToArray())
            {
                if (root.name.StartsWith("NullFrame", StringComparison.Ordinal)) continue;

                if (!_rootIndexes.ContainsKey(root.Pointer))
                {
                    _rootIndexes[root.Pointer] = original.Count;
                    original.Add(root);
                    scanned.Add(root);
                }
                else if (full)
                {
                    scanned.Add(root);
                }
            }

            return scanned;
        }

        // Map geometry is everything that is visible, solid, a terrain or a spawn point.
        private static Transform[] FindGeometry(List<GameObject> roots)
        {
            Transform[] all = roots.SelectMany(root => root.GetComponentsInChildren<Transform>(true)).ToArray();
            return all.Where(node => node.GetComponent<Renderer>() && !node.GetComponent<ParticleSystemRenderer>() || node.GetComponent<Terrain>() || node.GetComponent<Collider>() ||
                node.name.Contains("spawn", StringComparison.OrdinalIgnoreCase) || node.gameObject.tag.Contains("spawn", StringComparison.OrdinalIgnoreCase))
                .Where(WorldGeometryFilter.Allowed).Select(GeometryRoot).GroupBy(node => node.Pointer).Select(group => group.First()).ToArray();
        }

        private static bool InsideOtherPart(Transform node, HashSet<IntPtr> pointers)
        {
            for (Transform parent = node.parent; parent; parent = parent.parent)
            {
                if (pointers.Contains(parent.Pointer)) return true;
            }

            return false;
        }

        private Part CapturePart(Transform node, string key)
        {
            Renderer[] renderers = node.GetComponentsInChildren<Renderer>(true).ToArray();
            Collider[] colliders = node.GetComponentsInChildren<Collider>(true).ToArray();
            return new Part
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
                ResetForScene(scene, active);
            }

            if (active && Time.unscaledTime >= _scanAt)
            {
                _scanAt = Time.unscaledTime + 1f;
                ScanLoadedScenes();
            }

            if (!active || settings == _applied) return;
            if (settings?.Valid == false) return;

            Apply(settings);
        }

        private void ResetForScene(Scene scene, bool active)
        {
            Restore();
            _parts.Clear();
            _captured.Clear();
            _parents.Clear();
            _roots.Clear();
            _rootCounts.Clear();
            _sceneRoots.Clear();
            _rootIndexes.Clear();
            _nameIndexes.Clear();
            _scanAt = 0f;
            _applied = null;
            _scene = scene.handle;
            _active = active;
            _gravity = Physics.gravity;

            if (active)
            {
                _scanAt = Time.unscaledTime;
                _fullUntil = Time.unscaledTime + 10f;
            }
        }

        // Captures map objects again only in scenes whose list of root objects changed.
        private void ScanLoadedScenes()
        {
            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                Scene loaded = SceneManager.GetSceneAt(index);
                if (!loaded.isLoaded || loaded.name is "MainMenu" or "Updater") continue;

                int count = loaded.rootCount;
                if (_rootCounts.TryGetValue(loaded.handle, out int previous) && previous == count) continue;

                _rootCounts[loaded.handle] = count;
                Capture(loaded, Time.unscaledTime < _fullUntil);
            }
        }

        private void Apply(WorldSettings settings)
        {
            WorldSettings world = settings ?? WorldSettings.Default;
            Vector3 angles = SandboxWorld.Vector(world.Rotation);
            Vector3 scale = SandboxWorld.Vector(world.Scale);
            bool unchanged = scale == Vector3.one && angles.sqrMagnitude < 0.0001f && (world.Parts?.Length ?? 0) == 0;

            if (unchanged)
            {
                Restore();
                Physics.gravity = settings == null ? _gravity : SandboxWorld.Vector(world.Gravity);
                _applied = settings;
                return;
            }

            MovePartsIntoFrames();

            Quaternion rotation = Quaternion.Euler(angles);
            foreach (Transform root in _roots.Values)
            {
                if (!root) continue;

                root.localRotation = rotation;
                root.localScale = scale;
            }

            foreach (var pair in _parts)
            {
                if (!pair.Value.Transform) continue;

                MapPart change = world.Parts?.FirstOrDefault(value => value.Path == pair.Key);
                ApplyPart(pair.Value, change);
            }

            Physics.gravity = settings == null ? _gravity : SandboxWorld.Vector(world.Gravity);
            Physics.SyncTransforms();
            _applied = settings;
        }

        private void MovePartsIntoFrames()
        {
            foreach (Part part in _parts.Values)
            {
                if (!part.Transform || part.Framed) continue;

                part.Transform.SetParent(Frame(part.Parent, part.Transform.gameObject.scene), false);
                part.Framed = true;
            }
        }

        private static void ApplyPart(Part part, MapPart change)
        {
            part.Transform.localPosition = SandboxWorld.Vector(change?.Position ?? part.Original.Position);
            part.Transform.localRotation = SandboxWorld.Quaternion(change?.Rotation ?? part.Original.Rotation);
            part.Transform.localScale = change == null ? part.Scale : SandboxWorld.Vector(change.Scale);
            part.Batch.Apply();

            bool visible = change?.Visible ?? true;
            bool solid = change?.Solid ?? true;

            foreach (TerrainPart terrain in part.Terrains)
            {
                ApplyTerrain(terrain, visible);
            }

            for (int index = 0; index < part.Renderers.Length; index++)
            {
                if (part.Renderers[index])
                {
                    part.Renderers[index].enabled = part.Visible[index] && visible;
                }
            }

            for (int index = 0; index < part.Colliders.Length; index++)
            {
                if (part.Colliders[index])
                {
                    part.Colliders[index].enabled = part.Solid[index] && solid;
                }
            }

            ApplyColor(part, change);
        }

        // A scaled terrain needs its own copy of the terrain data, because the original asset is shared.
        private static void ApplyTerrain(TerrainPart terrain, bool visible)
        {
            if (!terrain.Terrain) return;

            Vector3 current = terrain.Terrain.transform.lossyScale;
            Vector3 ratio = new(current.x / terrain.Scale.x, current.y / terrain.Scale.y, current.z / terrain.Scale.z);
            Vector3 size = Vector3.Scale(terrain.Size, ratio);
            terrain.Terrain.enabled = terrain.Visible && visible;

            if (!terrain.Copy && (size - terrain.Size).sqrMagnitude < 0.0001f) return;

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
            terrain.Terrain.enabled = terrain.Visible && visible;
            terrain.Terrain.Flush();
        }

        private static void ApplyColor(Part part, MapPart change)
        {
            foreach (Material material in part.Copies)
            {
                if (material)
                {
                    Object.Destroy(material);
                }
            }

            part.Copies.Clear();
            bool tinted = change != null && change.Color != "#FFFFFF";
            ColorUtility.TryParseHtmlString(change?.Color ?? "#FFFFFF", out Color color);

            for (int index = 0; index < part.Renderers.Length; index++)
            {
                if (!part.Renderers[index]) continue;

                if (!tinted)
                {
                    part.Renderers[index].sharedMaterials = part.Materials[index];
                    continue;
                }

                Material[] materials = new Material[part.Materials[index].Length];
                for (int slot = 0; slot < materials.Length; slot++)
                {
                    Material original = part.Materials[index][slot];
                    if (!original) continue;

                    Material copy = new(original);
                    if (copy.HasProperty("_Color"))
                    {
                        copy.color = color;
                    }

                    if (copy.HasProperty("_BaseColor"))
                    {
                        copy.SetColor("_BaseColor", color);
                    }

                    part.Copies.Add(copy);
                    materials[slot] = copy;
                }

                part.Renderers[index].sharedMaterials = materials;
            }
        }

        // Puts the whole map back the way it was captured and removes the frames we created.
        private void Restore()
        {
            if (_active)
            {
                Physics.gravity = _gravity;
            }

            foreach (Part part in _parts.Values)
            {
                RestorePart(part);
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

        private static void RestorePart(Part part)
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
                RestoreTerrain(terrain);
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

        private static void RestoreTerrain(TerrainPart terrain)
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
    }
}
