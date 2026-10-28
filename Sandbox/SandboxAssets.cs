using Il2Cpp;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using NullServers.Customization;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace NullServers.Sandbox
{
    internal sealed class SandboxAssets : IDisposable
    {
        internal sealed record Entry(string Id, string Name, PrimitiveType? Primitive, GameObject Source, Sprite Icon = null);

        private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Texture2D> _icons = new();
        private readonly Queue<Entry> _pendingIcons = new();
        private readonly HashSet<string> _queuedIcons = new();
        private readonly Dictionary<string, float> _retryIcons = new();
        private readonly HashSet<IntPtr> _capturedVolumes = new();
        private float _nextCapture;
        private int _scene = -1;
        private readonly BuildingProps _props = new();

        internal SkinTextures Textures { get; } = new();

        internal IReadOnlyCollection<Entry> Entries
        {
            get
            {
                ScanIfStale();
                return _entries.Values;
            }
        }

        internal SandboxAssets()
        {
            foreach (PrimitiveType primitive in new[]
            {
                PrimitiveType.Cube,
                PrimitiveType.Sphere,
                PrimitiveType.Capsule,
                PrimitiveType.Cylinder,
                PrimitiveType.Plane,
                PrimitiveType.Quad
            })
            {
                Add(new Entry("unity:" + primitive, primitive.ToString(), primitive, null));
            }

            foreach (string name in _props.Names)
            {
                Add(new Entry("kit:" + name, name, null, null));
            }
        }

        private void Add(Entry entry) => _entries.TryAdd(entry.Id, entry);

        internal void Capture(IEnumerable<Volume> volumes)
        {
            int scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;
            if (_scene != scene)
            {
                _scene = scene;
                _capturedVolumes.Clear();
                _nextCapture = 0f;
            }

            foreach (Entry entry in _entries.Values.Where(entry => !entry.Primitive.HasValue &&
                !entry.Source &&
                !entry.Id.StartsWith("kit:", StringComparison.Ordinal)).ToArray())
            {
                _entries.Remove(entry.Id);
                if (_icons.Remove(entry.Id, out var icon) && icon)
                {
                    Object.Destroy(icon);
                }
            }

            foreach (Volume volume in volumes)
            {
                if (!volume || !_capturedVolumes.Add(volume.Pointer) || volume.Categories == null)
                {
                    continue;
                }

                foreach (Volume.catagory category in volume.Categories)
                {
                    if (category == null || category.isWeapon || category.isPlayAs || category.isMusic || category.options == null)
                    {
                        continue;
                    }

                    foreach (Volume.option option in category.options)
                    {
                        string path = option == null ? null : (string)option.resourcePath;
                        if (string.IsNullOrEmpty(path) || path.Length > 170) continue;

                        GameObject source = Resources.Load(path, Il2CppType.Of<GameObject>())?.Cast<GameObject>();
                        if (SafeProp(source))
                        {
                            Add(new Entry("resource:" + path, (string)option.optionName, null, source, option.image));
                        }
                    }
                }
            }
        }

        private void ScanIfStale()
        {
            if (Time.unscaledTime < _nextCapture) return;

            _nextCapture = Time.unscaledTime + 15f;
            foreach (GameObject source in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (!source.scene.IsValid() && !source.transform.parent && source.name.Length <= 170 && SafeProp(source))
                {
                    Add(new Entry("prop:" + source.name, source.name, null, source));
                }
            }

            RoomMultiplayerMenu room = RoomMultiplayerMenu.Current;
            if (!room) return;

            foreach (MeshFilter filter in Object.FindObjectsOfType<MeshFilter>())
            {
                if (!filter.sharedMesh ||
                    filter.gameObject.scene != room.gameObject.scene ||
                    filter.sharedMesh.vertexCount > 100000 ||
                    _entries.Count >= 512 ||
                    filter.GetComponentsInChildren<MeshFilter>().Length != 1)
                {
                    continue;
                }

                bool excluded = false;
                for (Transform node = filter.transform; node; node = node.parent)
                {
                    if (node.GetComponent(Il2CppType.Of<PlayerNetworkController>()) ||
                        node.GetComponent(Il2CppType.Of<PlayerMonster>()) ||
                        node.GetComponent(Il2CppType.Of<Bot>()) ||
                        node.GetComponent(Il2CppType.Of<Volume>()) ||
                        node.GetComponent(Il2CppType.Of<RagdollController>()) ||
                        node.GetComponent(Il2CppType.Of<FPScontroller>()))
                    {
                        excluded = true;
                        break;
                    }
                }

                Vector3 size = Vector3.Scale(filter.sharedMesh.bounds.size, filter.transform.lossyScale);
                if (excluded || Mathf.Max(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z)) > 30f)
                {
                    continue;
                }

                Bounds bounds = filter.sharedMesh.bounds;
                Renderer renderer = filter.GetComponent(Il2CppType.Of<Renderer>())?.Cast<Renderer>();

                string signature = filter.sharedMesh.name + "|" +
                    filter.sharedMesh.vertexCount + "|" +
                    string.Join(
                        "|",
                        new[]
                        {
                            bounds.center.x,
                            bounds.center.y,
                            bounds.center.z,
                            bounds.size.x,
                            bounds.size.y,
                            bounds.size.z
                        }
                        .Select(value => value.ToString("R", CultureInfo.InvariantCulture))) +
                        "|" +
                        (renderer ?
                        string.Join("|", renderer.sharedMaterials.Select(material => material ? material.name : "")) : "");

                string id = string.Concat("scene:", room.gameObject.scene.name, ":", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(signature))).AsSpan(0, 24));

                if (id.Length <= 192)
                {
                    Add(new Entry(id, filter.name, null, filter.gameObject));
                }
            }
        }

        private static bool SafeProp(GameObject source)
        {
            if (!source ||
                source.name.StartsWith("NullFrame", StringComparison.Ordinal) ||
                source.GetComponentsInChildren<Transform>(true).Length > 128)
            {
                return false;
            }

            foreach (Il2CppSystem.Type type in new[]
            {
                Il2CppType.Of<PlayerNetworkController>(),
                Il2CppType.Of<PlayerMonster>(),
                Il2CppType.Of<Bot>(),
                Il2CppType.Of<BossBot>(),
                Il2CppType.Of<Volume>(),
                Il2CppType.Of<RoomMultiplayerMenu>(),
                Il2CppType.Of<WeaponManager>(),
                Il2CppType.Of<RagdollController>()
            })
            {
                if (source.GetComponentInChildren(type, true)) return false;
            }

            var meshes = source.GetComponentsInChildren<MeshFilter>(true);
            return meshes.Length is > 0 and <= 32 &&
                meshes.All(mesh => !mesh.sharedMesh || mesh.sharedMesh.vertexCount < 100000);
        }

        internal Entry Find(string id)
        {
            if (id == null) return null;
            if (_entries.TryGetValue(id, out var entry)) return entry;

            ScanIfStale();
            return _entries.GetValueOrDefault(id);
        }

        internal GameObject Create(string id, bool collision)
        {
            Entry entry = Find(id);
            if (entry == null) return null;

            if (id.StartsWith("kit:", StringComparison.Ordinal))
            {
                return _props.Create(id.Substring(4), collision);
            }

            GameObject root;
            if (entry.Primitive.HasValue)
            {
                root = GameObject.CreatePrimitive(entry.Primitive.Value);
                root.name = "NullFrameBuild";

                foreach (Collider collider in root.GetComponents<Collider>())
                {
                    collider.enabled = collision;
                }

                if (entry.Primitive.Value is PrimitiveType.Plane or PrimitiveType.Quad)
                {
                    foreach (Collider collider in root.GetComponents<Collider>())
                    {
                        collider.enabled = false;
                        Object.Destroy(collider);
                    }

                    if (collision)
                    {
                        BoxCollider box = root.AddComponent(Il2CppType.Of<BoxCollider>()).Cast<BoxCollider>();
                        box.size = entry.Primitive.Value == PrimitiveType.Plane ? new Vector3(10f, 0.02f, 10f) : new Vector3(1f, 1f, 0.02f);
                    }
                }
            }
            else
            {
                if (!entry.Source) return null;

                root = CloneVisual(entry.Source, "NullFrameBuild");
                if (id.StartsWith("scene:", StringComparison.Ordinal))
                {
                    Mesh mesh = entry.Source.GetComponent(Il2CppType.Of<MeshFilter>()).Cast<MeshFilter>().sharedMesh;
                    root.transform.localScale = Vector3.one / Mathf.Max(
                        0.01f,
                        mesh.bounds.size.x,
                        mesh.bounds.size.y,
                        mesh.bounds.size.z);
                }

                if (collision)
                {
                    Bounds bounds = BoundsOf(root);
                    BoxCollider collider = root.AddComponent(Il2CppType.Of<BoxCollider>()).Cast<BoxCollider>();
                    collider.center = root.transform.InverseTransformPoint(bounds.center);
                    Vector3 local = root.transform.InverseTransformVector(bounds.size);
                    collider.size = new Vector3(Mathf.Abs(local.x), Mathf.Abs(local.y), Mathf.Abs(local.z));
                }
            }

            return root;
        }

        internal Texture Icon(Entry entry)
        {
            if (entry.Icon)
            {
                return entry.Icon.texture;
            }

            Texture2D texture = _icons.GetValueOrDefault(entry.Id);
            if (!texture)
            {
                QueueIcon(entry);
            }

            return texture;
        }

        private void QueueIcon(Entry entry)
        {
            if (Time.unscaledTime < _retryIcons.GetValueOrDefault(entry.Id) || !_queuedIcons.Add(entry.Id)) return;

            _pendingIcons.Enqueue(entry);
        }

        internal void Update()
        {
            Textures.Update();
            if (_pendingIcons.TryDequeue(out var entry))
            {
                _queuedIcons.Remove(entry.Id);
                try
                {
                    RenderIcon(entry);
                }
                catch (Exception exception) when (exception is InvalidOperationException or Il2CppInterop.Runtime.Il2CppException)
                {
                    _retryIcons[entry.Id] = Time.unscaledTime + 2f;
                }
            }
        }

        private void RenderIcon(Entry entry)
        {
            GameObject root = Create(entry.Id, false);
            if (!root) return;

            GameObject cameraObject = new("NullFramePropCamera");
            GameObject lightObject = new("NullFramePropLight");
            RenderTexture texture = new(128, 128, 16, RenderTextureFormat.ARGB32);
            RenderTexture previous = RenderTexture.active;

            try
            {
                root.transform.position = new Vector3(0f, -18000, 0f);
                foreach (Transform node in root.GetComponentsInChildren<Transform>(true))
                {
                    node.gameObject.layer = 30;
                }

                Bounds bounds = BoundsOf(root);
                float radius = Mathf.Max(0.1f, bounds.extents.magnitude);
                Camera camera = cameraObject.AddComponent(Il2CppType.Of<Camera>()).Cast<Camera>();
                camera.enabled = false;
                camera.cullingMask = 1 << 30;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.78f, 0.81f, 0.85f, 1f);
                camera.nearClipPlane = 0.01f;
                camera.farClipPlane = radius * 10f + 10f;
                camera.orthographic = true;
                camera.orthographicSize = radius * 1.1f;
                camera.transform.position = bounds.center + new Vector3(1f, 0.7f, -1).normalized * radius * 3f;
                camera.transform.LookAt(bounds.center);
                Light light = lightObject.AddComponent(Il2CppType.Of<Light>()).Cast<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.3f;
                light.cullingMask = 1 << 30;
                light.transform.rotation = Quaternion.Euler(35f, -35, 0f);
                texture.Create();
                camera.targetTexture = texture;
                camera.Render();
                RenderTexture.active = texture;
                Texture2D icon = new(128, 128, TextureFormat.RGBA32, false);
                icon.hideFlags = HideFlags.HideAndDontSave;
                icon.ReadPixels(new Rect(0f, 0f, 128f, 128f), 0, 0);
                icon.Apply();

                if (_icons.Remove(entry.Id, out var old) && old)
                {
                    Object.Destroy(old);
                }

                _icons[entry.Id] = icon;
                camera.targetTexture = null;
            }
            finally
            {
                RenderTexture.active = previous;
                texture.Release();
                Object.Destroy(texture);
                Object.Destroy(root);
                Object.Destroy(cameraObject);
                Object.Destroy(lightObject);
            }
        }

        internal static Transform CreateSegment(Transform parent, Material material)
        {
            GameObject segment = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Collider collider = segment.GetComponent<Collider>();
            collider.enabled = false;
            Object.Destroy(collider);
            segment.transform.SetParent(parent, false);

            Renderer renderer = segment.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            return segment.transform;
        }

        internal static Bounds BoundsOf(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>();
            Bounds bounds = new(root.transform.position, Vector3.one * 0.1f);

            if (renderers.Length == 0) return bounds;

            bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }

            return bounds;
        }

        internal static GameObject CloneVisual(GameObject source, string name)
        {
            GameObject holder = new("PropStaging");
            holder.SetActive(false);
            GameObject root = null;

            try
            {
                root = Object.Instantiate(source, holder.transform, false).Cast<GameObject>();
                root.SetActive(false);
                root.name = name;

                foreach (Component component in root.GetComponentsInChildren<Component>(true))
                {
                    if (!component ||
                        component.TryCast<Transform>() ||
                        component.TryCast<Renderer>() ||
                        component.TryCast<MeshFilter>() ||
                        component.TryCast<ParticleSystem>() ||
                        component.TryCast<Light>() ||
                        component.TryCast<Animation>() ||
                        component.TryCast<Animator>())
                    {
                        continue;
                    }

                    Object.DestroyImmediate(component);
                }

                root.transform.SetParent(null, false);
                root.transform.localPosition = Vector3.zero;
                root.transform.localRotation = Quaternion.identity;
                root.SetActive(true);
            }
            catch
            {
                if (root)
                {
                    Object.Destroy(root);
                }

                throw;
            }
            finally
            {
                Object.Destroy(holder);
            }

            return root;
        }

        public void Dispose()
        {
            Textures.Dispose();
            foreach (Texture2D icon in _icons.Values)
            {
                if (icon)
                {
                    Object.Destroy(icon);
                }
            }

            _icons.Clear();
        }
    }
}
