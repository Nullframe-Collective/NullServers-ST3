using Il2Cpp;
using Il2CppInterop.Runtime;
using S3_NullServers.Network;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace S3_NullServers.Sandbox
{
    internal sealed class SandboxBuilding
    {
        private sealed class Placed
        {
            internal BuildingObject State;
            internal GameObject Root;
            internal (Material Material, Texture Texture)[] Materials;
            internal Vector3 Position;
            internal Quaternion Rotation;
            internal int Sequence;
            internal Vector3 BaseScale;
        }

        private readonly SandboxWorld _world;
        private readonly Dictionary<int, Placed> _objects = new();
        private readonly Dictionary<int, BuildingObject> _pending = new();
        private readonly List<string> _recent = new();
        private readonly List<(Renderer Renderer, bool Enabled)> _hiddenSelected = new();
        private GameObject _ghost;
        private Material _ghostMaterial;
        private BuildingObject _draft;
        private bool _hasSurface;
        private bool _editing;
        private Vector3 _ghostBaseScale;
        private int _nextId = 1;
        private float _nextPhysics;
        private readonly List<int> _undo = new();
        private int _undoPending;
        private float _undoRequestedAt;
        private int _inputFrame = -1;
        private bool _selecting;
        private GameObject _outline;
        private Transform[] _outlineEdges;
        private Material _outlineMaterial;
        private Transform _localAvatar;
        private Transform _localController;
        private float _actorCheck;
        private float _clickAfter;

        internal bool Placing => _ghost || _selecting;

        internal bool SuppressWeapons => _world.Gun.Equipped;

        internal bool Selecting => _selecting;

        internal bool Editing => _editing;

        internal int SelectedId => _editing ? _draft?.Id ?? 0 : 0;

        internal BuildingObject Draft => _draft;

        internal IReadOnlyList<string> Recent => _recent;

        internal IEnumerable<BuildingObject> States => _objects.Values.Select(item => item.State);

        internal GameObject Root(int id) => _objects.GetValueOrDefault(id)?.Root;

        internal BuildingObject State(int id) => _objects.GetValueOrDefault(id)?.State;

        internal int TargetId => Target()?.State.Id ?? 0;

        internal bool SelectId(int id) => SelectItem(_objects.GetValueOrDefault(id));

        internal void Highlight(int id)
        {
            GameObject root = Root(id);
            if (root)
            {
                Frame(SandboxAssets.BoundsOf(root), new Color(0.15f, 0.8f, 1f));
            }
            else if (_outline && !Editing && !Selecting)
            {
                _outline.SetActive(false);
            }
        }

        internal SandboxBuilding(SandboxWorld world)
        {
            _world = world;
        }

        internal string Request(int sender, SandboxMessage message)
        {
            bool admin = _world.Administrator(sender);
            if (!_world.ConsoleEnabled && !admin)
            {
                return "The host must enable the sandbox console first.";
            }

            switch (message.Kind)
            {
                case SandboxKind.Create:
                case SandboxKind.Edit:
                    BuildingObject state = message.Object;
                    if (state == null || !state.Valid || _world.Assets.Find(state.Asset) == null)
                    {
                        return "This object is not available on the host.";
                    }

                    if (state.Dynamic &&
                        _objects.Values.Count(item => item.State.Dynamic && item.State.Id != state.Id) >= 64)
                    {
                        return "This room already has 64 building objects with active physics.";
                    }

                    if (message.Kind == SandboxKind.Create)
                    {
                        if (_objects.Count >= SandboxMessage.MaxObjects ||
                            _objects.Values.Count(item => item.State.Owner == sender) >= SandboxMessage.MaxObjectsPerPlayer)
                        {
                            return $"Building limit reached ({SandboxMessage.MaxObjectsPerPlayer} per player, {SandboxMessage.MaxObjects} per room).";
                        }

                        PlayerNetworkController avatar = SandboxWorld.Avatar(sender);
                        if (!avatar ||
                            Vector3.Distance(avatar.transform.position, SandboxWorld.Vector(state.Position)) > 120f)
                        {
                            return "Place objects within 120 metres of your character.";
                        }

                        while (_objects.ContainsKey(_nextId))
                        {
                            _nextId = _nextId % 999999 + 1;
                        }

                        state = state with
                        {
                            Id = _nextId,
                            Owner = sender,
                            Revision = 1
                        };
                        _nextId = _nextId % 999999 + 1;
                    }
                    else
                    {
                        if (!_objects.TryGetValue(state.Id, out var item) ||
                            !SandboxMessage.CanEdit(item.State.Owner, sender, admin))
                        {
                            return "You can edit your own objects. Administrators can edit all objects.";
                        }

                        if (state.Revision != item.State.Revision)
                        {
                            return "The object changed. Select it again before editing.";
                        }

                        if (state.Asset != item.State.Asset)
                        {
                            return "Select a new prop to create a different object.";
                        }

                        state = state with
                        {
                            Owner = item.State.Owner,
                            Revision = item.State.Revision + 1
                        };
                    }
                    Apply(state);
                    _world.Send(_world.Message(SandboxKind.Object) with
                    {
                        Object = state
                    });
                    return null;
                case SandboxKind.Delete:
                    if (!_objects.TryGetValue(message.Id, out var deleted) ||
                        !SandboxMessage.CanEdit(deleted.State.Owner, sender, admin))
                    {
                        return "You cannot delete this object.";
                    }

                    Remove(message.Id);
                    _world.Send(_world.Message(SandboxKind.Delete) with
                    {
                        Id = message.Id
                    });
                    return null;
                case SandboxKind.Clear:
                    if (!admin)
                    {
                        return "Only administrators can clear the building objects.";
                    }

                    Clear();
                    _world.Send(_world.Message(SandboxKind.Clear));
                    return null;
                default:
                    return "Unknown sandbox request.";
            }
        }

        internal void Apply(BuildingObject state)
        {
            if (_objects.TryGetValue(state.Id, out var item) && item.State.Revision >= state.Revision)
            {
                return;
            }

            bool created = item == null;
            if (created)
            {
                if (_objects.Count >= SandboxMessage.MaxObjects)
                {
                    return;
                }

                GameObject root = _world.Assets.Create(state.Asset, true);
                if (!root)
                {
                    if (_pending.Count < SandboxMessage.MaxObjects)
                    {
                        _pending[state.Id] = state;
                    }

                    _world.SetStatus("Waiting for prop: " + state.Asset);
                    return;
                }

                _pending.Remove(state.Id);
                _objects[state.Id] = item = new Placed
                {
                    Root = root,
                    BaseScale = root.transform.localScale
                };
                root.name = "NullFrameBuild_" + state.Id;
                List<(Material, Texture)> materials = new();
                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>())
                {
                    Material[] copies = renderer.sharedMaterials.Where(material => material).Select(material => new Material(material)).ToArray();
                    foreach (Material material in copies)
                    {
                        materials.Add((material, material.mainTexture));
                    }

                    renderer.sharedMaterials = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Material>(copies);
                }

                item.Materials = materials.ToArray();
            }

            item.State = state;
            item.Root.transform.SetPositionAndRotation(
                SandboxWorld.Vector(state.Position),
                SandboxWorld.Quaternion(state.Rotation));
            item.Root.transform.localScale = Vector3.Scale(item.BaseScale, SandboxWorld.Vector(state.Scale));
            item.Position = item.Root.transform.position;
            item.Rotation = item.Root.transform.rotation;
            ColorUtility.TryParseHtmlString(state.Color, out var color);
            foreach (var pair in item.Materials)
            {
                pair.Material.color = color;
                pair.Material.mainTexture = pair.Texture;
            }

            Rigidbody body = item.Root.GetComponent(Il2CppType.Of<Rigidbody>())?.Cast<Rigidbody>();
            if (state.Dynamic && !body)
            {
                body = item.Root.AddComponent(Il2CppType.Of<Rigidbody>()).Cast<Rigidbody>();
            }

            if (body)
            {
                body.isKinematic = !_world.IsHost || !state.Dynamic;
                body.useGravity = state.Dynamic;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.mass = Mathf.Clamp(
                    item.Root.transform.localScale.x * item.Root.transform.localScale.y * item.Root.transform.localScale.z,
                    0.1f,
                    100f);
            }

            if (created && state.Owner == _world.LocalId)
            {
                _undo.Add(state.Id);
            }
        }

        internal void Remove(int id)
        {
            _world.Physics.Forget(id);
            _world.Gun.Removed(id);
            _undo.Remove(id);
            if (_undoPending == id)
            {
                _undoPending = 0;
            }

            if (SelectedId == id)
            {
                Stop();
            }

            _pending.Remove(id);
            if (!_objects.Remove(id, out var item))
            {
                return;
            }

            if (item.Root)
            {
                Object.Destroy(item.Root);
            }

            foreach (var pair in item.Materials)
            {
                if (pair.Material)
                {
                    Object.Destroy(pair.Material);
                }
            }
        }

        internal void Clear()
        {
            Stop();
            _undo.Clear();
            _undoPending = 0;
            if (_outline)
            {
                Object.Destroy(_outline);
            }

            if (_outlineMaterial)
            {
                Object.Destroy(_outlineMaterial);
            }

            _outline = null;
            _outlineMaterial = null;
            _localAvatar = _localController = null;
            _actorCheck = 0f;
            foreach (int id in _objects.Keys.ToArray())
            {
                Remove(id);
            }

            _pending.Clear();
            _nextId = 1;
        }

        internal void Start(string asset)
        {
            _world.Gun.Equip(true);
            Stop();
            _draft = new BuildingObject(
                0,
                Math.Max(0, _world.LocalId),
                asset,
                new Point3(0f, 0f, 0f),
                new Rotation4(0f, 0f, 0f, 1f),
                new Point3(1f, 1f, 1f),
                "#FFFFFF",
                "",
                false);
            _editing = false;
            CreateGhost();
            _recent.Remove(asset);
            _recent.Insert(0, asset);
            if (_recent.Count > 12)
            {
                _recent.RemoveAt(12);
            }
        }

        internal bool Select()
        {
            return SelectItem(Target());
        }

        private Placed Target()
        {
            Camera camera = Camera.main;
            return Pick(camera, out var id, out _) ? _objects.GetValueOrDefault(id) : null;
        }

        internal bool Pick(Camera camera, out int id, out Vector3 point)
        {
            id = 0;
            point = default;
            if (!camera || !Surface(new Ray(camera.transform.position, camera.transform.forward), 40f, out var hit))
            {
                return false;
            }

            Placed item = _objects.Values.FirstOrDefault(value => value.Root &&
                (hit.transform == value.Root.transform ||
                hit.transform.IsChildOf(value.Root.transform)));
            if (item == null)
            {
                return false;
            }

            id = item.State.Id;
            point = hit.point;
            return true;
        }

        private bool SelectItem(Placed item)
        {
            if (item == null)
            {
                return false;
            }

            if (!SandboxMessage.CanEdit(item.State.Owner, _world.LocalId, _world.IsAdmin))
            {
                _world.SetStatus("This object belongs to another player.");
                return false;
            }

            Stop();
            _draft = item.State with
            {
                Position = SandboxWorld.Point(item.Root.transform.position),
                Rotation = SandboxWorld.Rotation(item.Root.transform.rotation)
            };
            _editing = true;
            CreateGhost();
            foreach (Renderer renderer in item.Root.GetComponentsInChildren<Renderer>())
            {
                _hiddenSelected.Add((renderer, renderer.enabled));
                renderer.enabled = false;
            }

            _world.SetStatus("Selected " + (_world.Assets.Find(item.State.Asset)?.Name ?? "object") + " #" + item.State.Id + ". Z: delete. Building > Edit selected: settings.");
            return true;
        }

        internal void BeginSelection()
        {
            _world.Gun.Equip(true);
            Stop();
            _selecting = true;
            _clickAfter = Time.unscaledTime + 0.2f;

            _world.SetStatus("Aim at a placed object and left-click. A blue frame marks the selected object.");
        }

        private void CreateGhost()
        {
            _ghost = _world.Assets.Create(_draft.Asset, false);
            if (!_ghost)
            {
                return;
            }

            _ghost.name = "NullFrameBuildGhost";
            _ghostBaseScale = _ghost.transform.localScale;
            _ghostMaterial = new Material(Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Transparent"));
            _ghostMaterial.color = new Color(0.2f, 0.85f, 1f, 0.35f);
            foreach (Transform node in _ghost.GetComponentsInChildren<Transform>(true))
            {
                node.gameObject.layer = 2;
            }

            foreach (Renderer renderer in _ghost.GetComponentsInChildren<Renderer>())
            {
                renderer.sharedMaterials = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Material>(
                    Enumerable.Repeat(_ghostMaterial, Math.Max(1, renderer.sharedMaterials.Length)).ToArray());
                renderer.shadowCastingMode = ShadowCastingMode.Off;
            }

            _clickAfter = Time.unscaledTime + 0.2f;

        }

        internal void Change(Point3 scale, Rotation4 rotation, string color, string texture, bool dynamic)
        {
            if (_draft == null)
            {
                return;
            }

            BuildingObject draft = _draft with
            {
                Scale = scale,
                Rotation = rotation,
                Color = color,
                Texture = texture,
                Dynamic = dynamic
            };
            if (draft.Valid)
            {
                _draft = draft;
            }
        }

        internal void MoveSelected(Point3 position)
        {
            if (_editing && _draft != null && position.Valid())
            {
                _draft = _draft with
                {
                    Position = position
                };
            }
        }

        internal void Confirm()
        {
            if (!_ghost || !_hasSurface && !_editing)
            {
                return;
            }

            _world.Submit(_world.Message(_editing ? SandboxKind.Edit : SandboxKind.Create) with
            {
                Object = _draft
            });
            if (_editing)
            {
                Stop();
            }
        }

        internal void DeleteSelected()
        {
            if (_editing && _draft != null)
            {
                _world.Submit(_world.Message(SandboxKind.Delete) with
                {
                    Id = _draft.Id
                });
            }

            Stop();
        }

        internal void Undo()
        {
            if (_undoPending != 0 && Time.unscaledTime - _undoRequestedAt < 3f)
            {
                _world.SetStatus("Waiting for the host to undo the last placement...");
                return;
            }

            _undoPending = 0;
            while (_undo.Count > 0)
            {
                int id = _undo[^1];
                if (!_objects.TryGetValue(id, out var item) || item.State.Owner != _world.LocalId)
                {
                    _undo.RemoveAt(_undo.Count - 1);
                    continue;
                }

                _undoPending = id;
                _undoRequestedAt = Time.unscaledTime;
                _world.Submit(_world.Message(SandboxKind.Delete) with
                {
                    Id = id
                });
                return;
            }

            _world.SetStatus("No more placements to undo.");
        }

        internal void RequestFailed() => _undoPending = 0;

        private bool InputAllowed()
        {
            if (!_world.Gun.Equipped ||
                !_world.Active ||
                Cursor.lockState != CursorLockMode.Locked ||
                !_world.ConsoleEnabled &&
                !_world.IsAdmin)
            {
                return false;
            }

            MultiplayerChat chat = MultiplayerChat.CFNPNLGIHMH;
            if (chat && chat.BOCAABIECMF && chat.BOCAABIECMF.isFocused)
            {
                return false;
            }

            GameObject selected = UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject;
            UnityEngine.UI.InputField input = selected ? selected.GetComponent(Il2CppType.Of<UnityEngine.UI.InputField>())?.Cast<UnityEngine.UI.InputField>() : null;
            return !input || !input.isFocused;
        }

        internal void HandleInput()
        {
            if (!_world.Gun.Equipped || _inputFrame == Time.frameCount)
            {
                return;
            }

            _inputFrame = Time.frameCount;
            if (!InputAllowed())
            {
                return;
            }

            float now = Time.unscaledTime;
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Stop();
                return;
            }

            if (Input.GetKeyDown(KeyCode.Z) && Editing)
            {
                DeleteSelected();
                return;
            }

            if (Placing && Input.GetMouseButtonDown(1))
            {
                Stop();
                return;
            }

            if (now < _clickAfter || !Input.GetMouseButtonDown(0))
            {
                return;
            }

            if (_selecting)
            {
                Select();
                return;
            }

            if (_ghost)
            {
                Confirm();
            }
        }

        internal void Stop()
        {
            if (_ghost)
            {
                Object.Destroy(_ghost);
            }

            if (_ghostMaterial)
            {
                Object.Destroy(_ghostMaterial);
            }

            _ghost = null;
            _ghostMaterial = null;
            foreach (var item in _hiddenSelected)
            {
                if (item.Renderer)
                {
                    item.Renderer.enabled = item.Enabled;
                }
            }

            _hiddenSelected.Clear();
            if (_outline)
            {
                _outline.SetActive(false);
            }

            _draft = null;
            _editing = false;
            _hasSurface = false;
            _selecting = false;
            _clickAfter = Time.unscaledTime + 0.15f;
        }

        private bool Surface(Ray ray, float distance, out RaycastHit hit)
        {
            foreach (RaycastHit candidate in Physics.RaycastAll(ray, distance, ~(1 << 2), QueryTriggerInteraction.Ignore).OrderBy(value => value.distance))
            {
                Transform node = candidate.transform;
                if (_localAvatar &&
                    (node == _localAvatar || node.IsChildOf(_localAvatar)) ||
                    _localController &&
                    (node == _localController || node.IsChildOf(_localController)))
                {
                    continue;
                }

                hit = candidate;
                return true;
            }

            hit = default;
            return false;
        }

        private void Frame(Bounds bounds, Color color)
        {
            if (!_outline)
            {
                _outline = new GameObject("NullFrameBuildSelection");
                _outline.layer = 2;
                _outlineMaterial = new Material(Shader.Find("Sprites/Default"));
                _outlineMaterial.SetInt("_ZTest", (int)CompareFunction.Always);
                _outlineEdges = new Transform[12];
                for (int index = 0; index < _outlineEdges.Length; index++)
                {
                    GameObject edge = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    edge.name = "SelectionEdge";
                    edge.layer = 2;
                    edge.transform.SetParent(_outline.transform, false);
                    Collider collider = edge.GetComponent<Collider>();
                    collider.enabled = false;
                    Object.Destroy(collider);
                    Renderer renderer = edge.GetComponent<Renderer>();
                    renderer.sharedMaterial = _outlineMaterial;
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    _outlineEdges[index] = edge.transform;
                }
            }

            _outline.SetActive(true);
            _outlineMaterial.color = color;
            float width = Mathf.Clamp(bounds.size.magnitude / 400f, 0.015f, 0.06f);
            bounds.Expand(0.025f);
            Vector3 a = bounds.min;
            Vector3 b = bounds.max;
            Vector3[] corners = new[]
            {
                new Vector3(a.x, a.y, a.z),
                new Vector3(b.x, a.y, a.z),
                new Vector3(b.x, a.y, b.z),
                new Vector3(a.x, a.y, b.z),
                new Vector3(a.x, b.y, a.z),
                new Vector3(b.x, b.y, a.z),
                new Vector3(b.x, b.y, b.z),
                new Vector3(a.x, b.y, b.z)
            };
            int[] order = new[]
            {
                0,
                1,
                1,
                2,
                2,
                3,
                3,
                0,
                4,
                5,
                5,
                6,
                6,
                7,
                7,
                4,
                0,
                4,
                1,
                5,
                2,
                6,
                3,
                7
            };
            for (int index = 0; index < _outlineEdges.Length; index++)
            {
                Vector3 from = corners[order[index * 2]];
                Vector3 to = corners[order[index * 2 + 1]];
                _outlineEdges[index].SetPositionAndRotation(
                    (from + to) * 0.5f,
                    Quaternion.FromToRotation(Vector3.up, to - from));
                _outlineEdges[index].localScale = new Vector3(width, (to - from).magnitude, width);
            }
        }

        internal void Update()
        {
            if (Time.unscaledTime >= _actorCheck)
            {
                _actorCheck = Time.unscaledTime + 0.5f;
                _localAvatar = SandboxWorld.Avatar(_world.LocalId)?.transform;
                _localController = Object.FindObjectsOfType<FPScontroller>().FirstOrDefault()?.transform;
            }

            foreach (BuildingObject state in _pending.Values.ToArray())
            {
                if (_world.Assets.Find(state.Asset) != null)
                {
                    Apply(state);
                }
            }

            foreach (Placed item in _objects.Values)
            {
                if (!item.Root)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(item.State.Texture))
                {
                    Texture2D texture = _world.Assets.Textures.Get(item.State.Texture);
                    if (texture)
                    {
                        foreach (var pair in item.Materials)
                        {
                            pair.Material.mainTexture = texture;
                        }
                    }
                }

                if (!_world.IsHost && item.State.Dynamic)
                {
                    float amount = 1f - Mathf.Exp(-25 * Time.unscaledDeltaTime);
                    item.Root.transform.SetPositionAndRotation(
                        Vector3.Lerp(
                            item.Root.transform.position,
                            item.Position,
                            amount),
                        Quaternion.Slerp(item.Root.transform.rotation, item.Rotation, amount));
                }
            }

            if (_world.IsHost && Time.unscaledTime >= _nextPhysics)
            {
                _nextPhysics = Time.unscaledTime + 0.1f;
                foreach (Placed item in _objects.Values.Where(value => value.State.Dynamic && value.Root).ToArray())
                {
                    Transform transform = item.Root.transform;
                    Vector3 position = transform.position;
                    Quaternion rotation = transform.rotation;
                    if (!SandboxWorld.Point(position).Valid() || position.y < -5000)
                    {
                        Remove(item.State.Id);
                        _world.Send(_world.Message(SandboxKind.Delete) with
                        {
                            Id = item.State.Id
                        });
                        continue;
                    }

                    item.State = item.State with
                    {
                        Position = SandboxWorld.Point(position),
                        Rotation = SandboxWorld.Rotation(rotation)
                    };
                    _world.Physics.Capture(item.State.Id, 255, position, rotation);
                }
            }

            if (_selecting)
            {
                Camera view = Camera.main;
                if (_outline)
                {
                    _outline.SetActive(false);
                }

                if (view && Surface(new Ray(view.transform.position, view.transform.forward), 120f, out var hovered))
                {
                    Placed item = _objects.Values.FirstOrDefault(value => value.Root &&
                        (hovered.transform == value.Root.transform ||
                        hovered.transform.IsChildOf(value.Root.transform)));
                    if (item != null)
                    {
                        Frame(
                            SandboxAssets.BoundsOf(item.Root),
                            SandboxMessage.CanEdit(item.State.Owner, _world.LocalId, _world.IsAdmin) ?
                        new Color(0.2f, 0.85f, 1f) : new Color(1f, 0.35f, 0.25f));
                    }
                }

                HandleInput();
                return;
            }

            if (!_ghost || _draft == null)
            {
                HandleInput();
                return;
            }

            if (!_world.ConsoleEnabled && !_world.IsAdmin)
            {
                Stop();
                return;
            }

            Camera camera = Camera.main;
            if (!camera)
            {
                return;
            }

            bool inWorld = Cursor.lockState == CursorLockMode.Locked;
            _hasSurface = _editing;
            if (!_editing && inWorld)
            {
                PositionPreview(camera);
            }

            _ghost.transform.SetPositionAndRotation(
                SandboxWorld.Vector(_draft.Position),
                SandboxWorld.Quaternion(_draft.Rotation));
            _ghost.transform.localScale = Vector3.Scale(_ghostBaseScale, SandboxWorld.Vector(_draft.Scale));
            Bounds previewBounds = SandboxAssets.BoundsOf(_ghost);
            if (!_editing && previewBounds.Contains(camera.transform.position))
            {
                _hasSurface = false;
            }

            foreach (Renderer renderer in _ghost.GetComponentsInChildren<Renderer>())
            {
                renderer.enabled = _hasSurface || _editing;
            }

            if (_editing)
            {
                Frame(previewBounds, new Color(0.15f, 0.75f, 1f));
            }

            ColorUtility.TryParseHtmlString(_draft.Color, out var tint);
            _ghostMaterial.color = _hasSurface ? new Color(tint.r, tint.g, tint.b, 0.35f) : new Color(1f, 0.2f, 0.2f, 0.35f);
            if (!string.IsNullOrEmpty(_draft.Texture))
            {
                _ghostMaterial.mainTexture = _world.Assets.Textures.Get(_draft.Texture);
            }

            if (!InputAllowed())
            {
                HandleInput();
                return;
            }

            if (Input.GetKeyDown(KeyCode.Q) || Input.GetKeyDown(KeyCode.E))
            {
                Vector3 axis = Input.GetKey(KeyCode.LeftAlt) ? Vector3.forward : Input.GetKey(KeyCode.LeftShift) ? Vector3.right : Vector3.up;
                _draft = _draft with
                {
                    Rotation = SandboxWorld.Rotation(SandboxWorld.Quaternion(_draft.Rotation) *
                    Quaternion.AngleAxis(Input.GetKeyDown(KeyCode.Q) ? -15 : 15, axis))
                };
            }

            HandleInput();
        }

        private void PositionPreview(Camera camera)
        {
            _hasSurface = false;
            if (!Surface(new Ray(camera.transform.position, camera.transform.forward), 100f, out var hit))
            {
                return;
            }

            _ghost.transform.localScale = Vector3.Scale(_ghostBaseScale, SandboxWorld.Vector(_draft.Scale));
            _ghost.transform.rotation = SandboxWorld.Quaternion(_draft.Rotation);
            _ghost.transform.position = Vector3.zero;
            Bounds bounds = SandboxAssets.BoundsOf(_ghost);
            Vector3 normal = hit.normal;
            Vector3 point = hit.point;
            if (normal.y > 0.55f)
            {
                Vector3 origin = _localController ? _localController.position : camera.transform.position;
                Vector3 forward = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up).normalized;
                if (forward.sqrMagnitude < 0.01f)
                {
                    forward = Vector3.ProjectOnPlane(camera.transform.up, Vector3.up).normalized;
                }

                float clearance = Mathf.Max(
                    2f,
                    Mathf.Abs(forward.x) * bounds.extents.x + Mathf.Abs(forward.z) * bounds.extents.z + 0.8f);
                if (Vector3.ProjectOnPlane(point - origin, Vector3.up).magnitude < clearance)
                {
                    Vector3 ahead = origin + forward * clearance;
                    ahead.y = point.y + 2f;
                    if (!Surface(new Ray(ahead, Vector3.down), 5f, out var floor) || floor.normal.y <= 0.55f)
                    {
                        return;
                    }

                    point = floor.point;
                    normal = floor.normal;
                }
            }

            float support = Mathf.Abs(normal.x) * bounds.extents.x + Mathf.Abs(normal.y) * bounds.extents.y + Mathf.Abs(normal.z) * bounds.extents.z;
            Vector3 position = point + normal * (support + 0.005f) - bounds.center;
            _draft = _draft with
            {
                Position = SandboxWorld.Point(position)
            };
            _ghost.transform.position = position;
            _hasSurface = !SandboxAssets.BoundsOf(_ghost).Contains(camera.transform.position);
        }

        internal void Pose(PhysicsPose pose, int sequence)
        {
            if (!_objects.TryGetValue(pose.Id, out var item) || !item.State.Dynamic || sequence <= item.Sequence)
            {
                return;
            }

            item.Sequence = sequence;
            item.Position = SandboxWorld.Vector(pose.Position);
            item.Rotation = SandboxWorld.Quaternion(pose.Rotation).normalized;
        }

        internal void Pose(SandboxMessage message)
        {
            if (!_objects.TryGetValue(message.Id, out var item) ||
                !item.State.Dynamic ||
                message.Sequence <= item.Sequence ||
                !SandboxPose.TryDecode(message.Data, out var offset, out var values) ||
                offset != 0 ||
                values.Length != 7)
            {
                return;
            }

            item.Sequence = message.Sequence;
            item.Position = new Vector3(values[0], values[1], values[2]);
            item.Rotation = new Quaternion(values[3], values[4], values[5], values[6]);
        }
    }
}
