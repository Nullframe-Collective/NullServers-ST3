using Il2Cpp;
using Il2CppFishNet.Connection;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using NullServers.Network;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace NullServers.Sandbox
{
    internal sealed class PhysGun
    {
        private static readonly Vector3 GripPoint = new(-0.047f, -0.115f, -0.355f);
        private static readonly Vector3 MuzzlePoint = new(-0.055f, -0.025f, 0.435f);

        private sealed class Tool
        {
            internal GunState State;
            internal GameObject Model, WorldModel, Beam, ViewBeam;
            internal Transform[] Segments, ViewSegments;
            internal readonly List<(Renderer Renderer, bool Enabled)> Hidden = new();
            internal Rigidbody Body;
            internal Transform AimTransform;
            internal Transform Hand, Finger;
            internal ThirdPersonControl ViewControl;
            internal WeaponScript Weapon;
            internal Vector3 Target;
            internal Quaternion Rotation;
            internal float AimAt;
            internal int AimSequence;
        }

        private readonly SandboxWorld _world;
        private readonly Camera.CameraCallback _beforeRender;
        private readonly Dictionary<int, Tool> _tools = new();
        private Mesh _mesh;
        private Material _metal, _glow;
        private bool _equipped;
        private bool _releasePending;
        private int _aimSequence;
        private int _clickTarget;
        private float _nextAim, _scanAt, _distance = 5f, _lastClick = -1, _lastAllowed;
        private Quaternion _rotation;

        internal bool Equipped => _equipped;
        internal int Holding => _tools.GetValueOrDefault(_world.LocalId)?.State.Holding ?? 0;
        internal IEnumerable<int> HeldObjects => _tools.Values.Where(tool => tool.State.Holding > 0).Select(tool => tool.State.Holding);
        internal bool Rotating => Equipped && Holding > 0 && Input.GetKey(KeyCode.E);

        internal PhysGun(SandboxWorld world)
        {
            _world = world;
            _beforeRender = (Camera.CameraCallback)BeforeRender;
            Camera.onPreRender = Il2CppSystem.Delegate.Combine(Camera.onPreRender, _beforeRender).Cast<Camera.CameraCallback>();
        }

        private void BeforeRender(Camera camera)
        {
            if (camera && camera.cullingMask != 0)
            {
                LateUpdate();
            }
        }

        internal void Dispose()
        {
            Camera.onPreRender = Il2CppSystem.Delegate.Remove(Camera.onPreRender, _beforeRender)?.Cast<Camera.CameraCallback>();
            Clear();
        }

        internal void Equip(bool equipped)
        {
            if (!_world.Active || equipped && !_world.ConsoleEnabled && !_world.IsAdmin)
            {
                return;
            }

            _equipped = equipped;
            _releasePending = false;
            if (!equipped)
            {
                _world.Building.Stop();
            }

            _world.Submit(_world.Message(SandboxKind.GunState) with
            {
                Gun = new GunState(_world.LocalId, equipped)
            });
            _world.SetStatus(equipped ? "Phys Gun equipped. F6 returns to your weapon." : "Weapon equipped.");
        }

        internal string Request(int sender, SandboxMessage message)
        {
            if (message.Gun == null || !message.Gun.Valid)
            {
                return "Invalid Phys Gun request.";
            }

            if 
            (
                !_world.ConsoleEnabled &&
                !_world.Administrator(sender) &&
                message.Kind != SandboxKind.GunRelease &&
                !(message.Kind == SandboxKind.GunState && !message.Gun.Equipped)
            )
            {
                return "The host must enable the sandbox console first.";
            }

            if (message.Kind == SandboxKind.GunState)
            {
                Release(sender, false, false);
                Apply(new GunState(sender, message.Gun.Equipped));

                _world.Send(_world.Message(SandboxKind.GunState) with
                {
                    Gun = _tools[sender].State
                });

                return null;
            }

            if (!_tools.TryGetValue(sender, out var tool) || !tool.State.Equipped)
            {
                return "Equip the Phys Gun first.";
            }

            if (message.Kind == SandboxKind.GunRelease)
            {
                Release(sender, message.Gun.Throw, message.Gun.Freeze);

                return null;
            }

            BuildingObject state = _world.Building.State(message.Id);
            GameObject root = _world.Building.Root(message.Id);
            PlayerNetworkController avatar = SandboxWorld.Avatar(sender);

            if 
            (
                state == null ||
                !root ||
                !avatar ||
                Vector3.Distance(root.transform.position, avatar.transform.position) > 40f
            )
            {
                return "Aim at a nearby building object.";
            }

            if (!SandboxMessage.CanEdit(state.Owner, sender, _world.Administrator(sender)))
            {
                return "You can move your own props; administrators can move all props.";
            }

            if (_tools.Values.Any(other => other != tool && other.State.Holding == state.Id))
            {
                return "Another player is holding this object.";
            }

            Vector3 grabPoint = message.Gun.Position != null ? SandboxWorld.Vector(message.Gun.Position) : root.transform.position;
            Bounds bounds = SandboxAssets.BoundsOf(root);
            bounds.Expand(0.5f);

            if (!bounds.Contains(grabPoint))
            {
                return "Aim at the surface of this object.";
            }

            Release(sender, false, false);
            if (!state.Dynamic)
            {
                string error = _world.Building.Request(sender, _world.Message(SandboxKind.Edit) with
                {
                    Object = state with
                    {
                        Dynamic = true
                    }
                });

                if (error != null) return error;
            }

            tool.Body = root.GetComponent(Il2CppType.Of<Rigidbody>())?.Cast<Rigidbody>();
            if (!tool.Body)
            {
                return "This object has no Rigidbody.";
            }

            if (sender == _world.LocalId)
            {
                _world.Building.Stop();
                _lastAllowed = Time.unscaledTime;
            }

            tool.Target = grabPoint;
            tool.Rotation = root.transform.rotation;
            tool.AimAt = Time.unscaledTime;
            tool.Body.WakeUp();
            tool.State = new GunState
            (
                sender,
                true,
                state.Id,
                SandboxWorld.Point(tool.Target),
                SandboxWorld.Rotation(tool.Rotation),
                Grip: SandboxWorld.Point(root.transform.InverseTransformPoint(grabPoint))
            );

            if (sender == _world.LocalId)
            {
                _releasePending = false;
            }

            _world.Send(_world.Message(SandboxKind.GunState) with
            {
                Gun = tool.State
            });

            return null;
        }

        internal void Aim(int sender, GunState state)
        {
            if 
            (
                !_world.IsHost ||
                state == null ||
                state.Position == null ||
                state.Rotation == null ||
                !state.Valid ||
                !_tools.TryGetValue(sender, out var tool) ||
                !tool.State.Equipped ||
                tool.State.Holding == 0 ||
                state.Holding != tool.State.Holding ||
                state.Sequence <= tool.AimSequence ||
                sender != _world.LocalId &&
                Time.unscaledTime - tool.AimAt < 0.06f
            )
            {
                return;
            }

            PlayerNetworkController avatar = SandboxWorld.Avatar(sender);
            if (!avatar || Vector3.Distance(avatar.transform.position, SandboxWorld.Vector(state.Position)) > 45f)
            {
                return;
            }

            tool.AimAt = Time.unscaledTime;
            tool.AimSequence = state.Sequence;
            tool.Target = SandboxWorld.Vector(state.Position);
            tool.Rotation = SandboxWorld.Quaternion(state.Rotation).normalized;
            tool.State = tool.State with
            {
                Position = state.Position,
                Rotation = state.Rotation
            };
        }

        internal void Apply(GunState state)
        {
            if (!_tools.TryGetValue(state.Player, out var tool))
            {
                _tools[state.Player] = tool = new Tool();
            }

            tool.State = state;
            if (state.Player == _world.LocalId)
            {
                _equipped = state.Equipped;
                if (state.Holding == 0)
                {
                    _releasePending = false;
                }
            }

            if (!state.Equipped)
            {
                DestroyVisual(tool);
            }
        }

        private void Release(int player, bool throwing, bool freeze)
        {
            if (!_tools.TryGetValue(player, out var tool) || tool.State.Holding == 0)
            {
                return;
            }

            int id = tool.State.Holding;
            Rigidbody body = tool.Body;
            tool.Body = null;
            tool.State = tool.State with
            {
                Holding = 0,
                Position = null,
                Rotation = null,
                Grip = null
            };

            if (player == _world.LocalId)
            {
                _releasePending = false;
            }

            if (body)
            {
                BuildingObject state = _world.Building.State(id);
                if (throwing)
                {
                    PlayerNetworkController avatar = SandboxWorld.Avatar(player);
                    Vector3 direction = avatar ? (tool.Target - avatar.transform.position).normalized : Vector3.up;
                    body.velocity = direction * 25f;
                    body.WakeUp();
                }

                if (state != null)
                {
                    state = state with
                    {
                        Position = SandboxWorld.Point(body.transform.position),
                        Rotation = SandboxWorld.Rotation(body.transform.rotation),
                        Dynamic = !freeze
                    };

                    _world.Building.Request(player, _world.Message(SandboxKind.Edit) with
                    {
                        Object = state
                    });
                }
            }

            _world.Send(_world.Message(SandboxKind.GunState) with
            {
                Gun = tool.State
            });
        }

        internal void Removed(int id)
        {
            foreach (var pair in _tools.Where(pair => pair.Value.State.Holding == id).ToArray())
            {
                pair.Value.Body = null;
                pair.Value.State = pair.Value.State with
                {
                    Holding = 0,
                    Position = null,
                    Rotation = null,
                    Grip = null
                };

                if (_world.IsHost)
                {
                    _world.Send(_world.Message(SandboxKind.GunState) with
                    {
                        Gun = pair.Value.State
                    });
                }
            }
        }

        internal void Snapshot(NetworkConnection connection)
        {
            foreach (Tool tool in _tools.Values.Where(tool => tool.State.Equipped))
            {
                _world.Queue(_world.Message(SandboxKind.GunState) with
                {
                    Gun = tool.State
                }, connection);
            }
        }

        internal void HandleInput()
        {
            Step
            (
                new PhysGunInput
                (
                    InputAllowed(),
                    Input.GetMouseButtonDown(0),
                    Input.GetMouseButton(0),
                    Input.GetMouseButtonUp(0),
                    Input.GetMouseButtonDown(1),
                    Input.GetKeyDown(KeyCode.R),
                    Input.GetKeyDown(KeyCode.Escape),
                    Input.GetKeyDown(KeyCode.F6),
                    Input.GetKeyDown(KeyCode.Alpha1) ||
                    Input.GetKeyDown(KeyCode.Alpha2) ||
                    Input.GetKeyDown(KeyCode.Alpha3),
                    Input.GetKey(KeyCode.E),
                    Input.GetKey(KeyCode.LeftShift),
                    Input.GetAxis("Mouse ScrollWheel"),
                    Input.GetAxis("Mouse X"),
                    Input.GetAxis("Mouse Y")
                ),
                Camera.main
            );
        }

        internal void Step(PhysGunInput input, Camera camera)
        {
            if (!_world.Active) return;

            float now = Time.unscaledTime;

            if (!input.Allowed)
            {
                if (Holding > 0 && now - _lastAllowed > 0.25f)
                {
                    RequestRelease();
                }

                return;
            }

            _lastAllowed = now;
            if (input.Toggle)
            {
                Equip(!Equipped);

                return;
            }

            if (!Equipped || !camera) return;

            if (input.SwitchWeapon || input.Wheel != 0f && Holding == 0 && !_world.Building.Placing)
            {
                Equip(false);

                return;
            }

            if (Holding > 0)
            {
                _world.Building.Highlight(Holding);
                if (input.Down && Holding == _clickTarget && now - _lastClick < 0.3f)
                {
                    int selectedId = Holding;
                    RequestRelease();
                    _world.Building.SelectId(selectedId);
                    _clickTarget = 0;

                    return;
                }

                if (_releasePending) return;

                if (input.Up || !input.Held && !input.Down || input.Secondary || input.Throw || input.Cancel)
                {
                    RequestRelease(input.Throw, input.Secondary);

                    return;
                }

                _distance = Mathf.Clamp(_distance + input.Wheel * 6f, 2f, 35f);
                if (input.Rotate)
                {
                    _rotation = Quaternion.AngleAxis
                    (
                        input.MouseX * 4f,
                        input.Roll ? camera.transform.forward : camera.transform.up
                    ) * Quaternion.AngleAxis(-input.MouseY * 4f, camera.transform.right) * _rotation;
                }

                if (now >= _nextAim)
                {
                    _nextAim = now + 0.1f;
                    _world.SubmitAim(_world.Message(SandboxKind.GunAim) with
                    {
                        Gun = new GunState
                        (
                            _world.LocalId,
                            true,
                            Holding,
                            SandboxWorld.Point(camera.transform.position + camera.transform.forward * _distance),
                            SandboxWorld.Rotation(_rotation),
                            Sequence: ++_aimSequence
                        )
                    });
                }

                return;
            }

            if (_world.Building.Placing)
            {
                _world.Building.HandleInput();

                return;
            }

            _world.Building.Pick(camera, out var id, out var point);
            _world.Building.Highlight(id);

            if (Input.GetKeyDown(KeyCode.Z) && id > 0)
            {
                _world.Submit(_world.Message(SandboxKind.Delete) with
                {
                    Id = id
                });

                return;
            }

            if (!input.Down || id == 0) return;

            if (id == _clickTarget && now - _lastClick < 0.3f)
            {
                _world.Building.SelectId(id);
                _clickTarget = 0;

                return;
            }

            _clickTarget = id;
            _lastClick = now;
            _distance = Mathf.Clamp(Vector3.Distance(camera.transform.position, point), 2f, 35f);
            _rotation = _world.Building.Root(id).transform.rotation;

            _world.Submit(_world.Message(SandboxKind.GunGrab) with
            {
                Id = id,
                Gun = new GunState(_world.LocalId, true, Position: SandboxWorld.Point(point))
            });
        }

        private void RequestRelease(bool throwing = false, bool freeze = false)
        {
            if (_releasePending || Holding == 0) return;

            _releasePending = true;
            _world.Submit(_world.Message(SandboxKind.GunRelease) with
            {
                Gun = new GunState(_world.LocalId, true, Throw: throwing, Freeze: freeze)
            });
        }

        private bool InputAllowed()
        {
            if (Cursor.lockState != CursorLockMode.Locked) return false;

            MultiplayerChat chat = MultiplayerChat.CFNPNLGIHMH;
            return !chat || !chat.BOCAABIECMF || !chat.BOCAABIECMF.isFocused;
        }

        internal void FixedUpdate()
        {
            if (!_world.Active || !_world.IsHost) return;

            foreach (var pair in _tools)
            {
                Tool tool = pair.Value;
                if (tool.State.Holding == 0) continue;

                if 
                (
                    !tool.Body ||
                    Time.unscaledTime - tool.AimAt > 2.5f ||
                    !_world.ConsoleEnabled &&
                    !_world.Administrator(pair.Key)
                )
                {
                    Release(pair.Key, false, false);

                    continue;
                }

                Rigidbody body = tool.Body;
                Vector3 grip = tool.State.Grip != null ? body.transform.TransformPoint(SandboxWorld.Vector(tool.State.Grip)) : body.worldCenterOfMass;
                Vector3 acceleration = (tool.Target - grip) * 100f - body.velocity * 20f - Physics.gravity;

                body.AddForce(Vector3.ClampMagnitude(acceleration, 300f), ForceMode.Acceleration);

                Quaternion delta = tool.Rotation * Quaternion.Inverse(body.rotation);
                delta.ToAngleAxis(out var angle, out var axis);

                if (angle > 180f)
                {
                    angle -= 360f;
                }

                body.angularVelocity = 
                    Mathf.Abs(angle) > 0.01f &&
                    float.IsFinite(axis.x) &&
                    float.IsFinite(axis.y) &&
                    float.IsFinite(axis.z) ?
                    Vector3.ClampMagnitude(axis * (angle * Mathf.Deg2Rad * 12f), 12f) : Vector3.zero;

                body.velocity = Vector3.ClampMagnitude(body.velocity, 25f);
                body.WakeUp();
            }
        }

        private void LoadModel()
        {
            if (_mesh) return;

            using var stream = typeof(PhysGun).Assembly.GetManifestResourceStream("NullServers.PhysGun.mesh");
            using var reader = new BinaryReader(stream);
            Vector3[] vertices = new Vector3[reader.ReadInt32()];
            int[] indices = new int[reader.ReadInt32()];
            Vector3[] normals = new Vector3[vertices.Length];
            Quaternion orientation = Quaternion.Euler(0f, 15f, 0f);

            for (int index = 0; index < vertices.Length; index++)
            {
                vertices[index] = orientation * new Vector3
                (
                    -reader.ReadSingle(),
                    reader.ReadSingle(),
                    -reader.ReadSingle()
                );

                normals[index] = orientation * new Vector3
                (
                    -reader.ReadSingle(),
                    reader.ReadSingle(),
                    -reader.ReadSingle()
                );
            }

            for (int index = 0; index < indices.Length; index++)
            {
                indices[index] = reader.ReadInt32();
            }

            _mesh = new Mesh
            {
                name = "PhysGunMesh",
                vertices = new Il2CppStructArray<Vector3>(vertices),
                normals = new Il2CppStructArray<Vector3>(normals),
                triangles = new Il2CppStructArray<int>(indices)
            };
            _mesh.RecalculateBounds();
            _metal = new Material(Shader.Find("Standard"));
            _metal.color = new Color(0.25f, 0.32f, 0.38f);
            _metal.SetFloat("_Metallic", 0.7f);
            _metal.SetFloat("_Glossiness", 0.65f);
            _glow = new Material(Shader.Find("Sprites/Default"));
            _glow.color = new Color(0.1f, 0.8f, 1f, 0.8f);
        }

        private GameObject Model(Transform parent, bool firstPerson)
        {
            LoadModel();

            GameObject root = new(firstPerson ? "PhysGunFirstPerson" : "PhysGunWorld");
            root.layer = firstPerson ? 2 : parent.gameObject.layer;
            root.transform.SetParent(parent, false);
            root.transform.localPosition = firstPerson ? new Vector3(0.28f, -0.23f, 0.68f) : Vector3.zero;
            root.transform.localRotation = firstPerson ? Quaternion.Euler(-4, -7, 0f) : Quaternion.identity;
            root.transform.localScale = Vector3.one;
            root.AddComponent(Il2CppType.Of<MeshFilter>()).Cast<MeshFilter>().sharedMesh = _mesh;

            MeshRenderer renderer = root.AddComponent(Il2CppType.Of<MeshRenderer>()).Cast<MeshRenderer>();
            renderer.sharedMaterial = _metal;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            GameObject core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            core.name = "PhysGunCore";
            core.layer = root.layer;

            Collider collider = core.GetComponent<Collider>();
            collider.enabled = false;

            Object.Destroy(collider);

            core.transform.SetParent(root.transform, false);
            core.transform.localPosition = new Vector3(-0.055f, -0.025f, 0.05f);
            core.transform.localScale = Vector3.one * 0.12f;
            core.GetComponent<Renderer>().sharedMaterial = _glow;

            if (!firstPerson)
            {
                PlayerNetworkController avatar = parent.GetComponentInParent(Il2CppType.Of<PlayerNetworkController>())?.Cast<PlayerNetworkController>();
                WorldScale(root.transform, avatar ? avatar.transform.lossyScale : Vector3.one);
                AlignWorld(root, AimTransform(parent), parent, parent.Find("Bip001 R Finger0"));
            }

            return root;
        }

        private static Transform AimTransform(Transform hand)
        {
            PlayerNetworkController avatar = hand.GetComponentInParent(Il2CppType.Of<PlayerNetworkController>())?.Cast<PlayerNetworkController>();
            return avatar ? avatar.GetComponentsInChildren<Transform>(true).FirstOrDefault(node => node.name == "FirePoint(ThirdPerson)") : null;
        }

        private static void AlignWorld(GameObject model, Transform aim, Transform hand, Transform finger)
        {
            if (!hand || !aim) return;

            Vector3 scale = model.transform.lossyScale;
            Vector3 palm = finger ? Vector3.Lerp(hand.position, finger.position, 0.5f) : hand.position;
            model.transform.SetPositionAndRotation(palm - aim.rotation * Vector3.Scale(GripPoint, scale), aim.rotation);
        }

        private static void WorldScale(Transform model, Vector3 scale)
        {
            Vector3 parent = model.parent.lossyScale;
            model.localScale = new Vector3
            (
                scale.x / Mathf.Max(0.00001f, Mathf.Abs(parent.x)),
                scale.y / Mathf.Max(0.00001f, Mathf.Abs(parent.y)),
                scale.z / Mathf.Max(0.00001f, Mathf.Abs(parent.z))
            );
        }

        private static Transform Hand(PlayerNetworkController avatar)
        {
            SyncCustomization sync = avatar.GetComponentsInChildren<SyncCustomization>(true).FirstOrDefault(value => value.IsClientInitialized &&
                !value.IGDAIDNEJBO);
            SkinnedMeshRenderer arm = sync && sync.FFDDAKMAHKI ? sync.FFDDAKMAHKI.TryCast<SkinnedMeshRenderer>() : null;

            return arm ? arm.bones.FirstOrDefault(bone => bone && bone.name.EndsWith("Hand", StringComparison.OrdinalIgnoreCase)) : null;
        }

        private static Transform BodyRoot(Transform hand)
        {
            // The hand is disabled to hide native items; its active forearm still follows the rig during rendering.
            for (Transform node = hand.parent; node; node = node.parent)
            {
                if (node.gameObject.activeSelf) return node;
            }

            return hand.root;
        }

        internal void Update()
        {
            HandleInput();

            if (Equipped && !_world.ConsoleEnabled && !_world.IsAdmin)
            {
                Equip(false);
            }

            if (Time.unscaledTime < _scanAt) return;

            _scanAt = Time.unscaledTime + 0.25f;
            foreach (var pair in _tools)
            {
                Tool tool = pair.Value;
                if (!tool.State.Equipped) continue;

                PlayerNetworkController avatar = SandboxWorld.Avatar(pair.Key);
                Transform hand = avatar ? Hand(avatar) : null;

                bool local = pair.Key == _world.LocalId;
                WeaponManager manager = local && avatar ? avatar.GetComponentInChildren(Il2CppType.Of<WeaponManager>(), true)?.Cast<WeaponManager>() : null;
                WeaponScript weapon = manager ? manager.FGGENNCGLJI : null;

                if (tool.WorldModel && tool.Hand != hand || tool.Model && tool.Weapon != weapon)
                {
                    DestroyVisual(tool);
                }

                if (hand && !tool.WorldModel)
                {
                    tool.WorldModel = Model(hand, false);
                    tool.Hand = hand;
                    tool.Finger = hand.Find("Bip001 R Finger0");
                    tool.WorldModel.transform.SetParent(BodyRoot(hand), true);
                    tool.AimTransform = AimTransform(hand);

                    foreach (Renderer renderer in hand.GetComponentsInChildren<Renderer>(true))
                    {
                        Hide(tool, renderer);
                    }

                    foreach (Transform node in avatar.GetComponentsInChildren<Transform>(true))
                    {
                        if (node.name is "ThirdPersonWeapons" or "torchitem" or "Playertorch" or "NV_Camera")
                        {
                            foreach (Renderer renderer in node.GetComponentsInChildren<Renderer>(true))
                            {
                                Hide(tool, renderer);
                            }
                        }
                    }
                }

                if (tool.WorldModel && avatar)
                {
                    WorldScale(tool.WorldModel.transform, avatar.transform.lossyScale);
                }

                if (local && weapon && !tool.Model)
                {
                    Camera camera = manager.GetComponentInParent<Camera>();
                    if (!camera) continue;

                    tool.Weapon = weapon;
                    tool.ViewControl = avatar.GetComponentInChildren(Il2CppType.Of<ThirdPersonControl>(), true)?.Cast<ThirdPersonControl>();
                    weapon.transform.localPosition = weapon.KKCIIEDNLEO;
                    tool.Model = Model(camera.transform, true);
                    tool.Model.transform.SetParent(weapon.transform, true);

                    var renderers = weapon.GetComponentsInChildren<Renderer>(true);
                    int layer = renderers.FirstOrDefault(renderer => !renderer.transform.IsChildOf(tool.Model.transform))?.gameObject.layer ?? 9;

                    foreach (Transform node in tool.Model.GetComponentsInChildren<Transform>())
                    {
                        node.gameObject.layer = layer;
                    }

                    foreach (Renderer renderer in renderers)
                    {
                        if 
                        (
                            !renderer.name.Contains("arm", StringComparison.OrdinalIgnoreCase) &&
                            !renderer.name.Contains("hand", StringComparison.OrdinalIgnoreCase) &&
                            renderer.name != "Body-mesh"
                        )
                        {
                            Hide(tool, renderer);
                        }
                    }
                }

                foreach (var item in tool.Hidden)
                {
                    if (!item.Renderer) continue;

                    item.Renderer.enabled = false;
                }
            }
        }

        private static void Hide(Tool tool, Renderer renderer)
        {
            if
            (
                !renderer ||
                tool.Model &&
                renderer.transform.IsChildOf(tool.Model.transform) ||
                tool.WorldModel &&
                renderer.transform.IsChildOf(tool.WorldModel.transform) ||
                tool.Hidden.Any(item => item.Renderer == renderer)
            )
            {
                return;
            }

            tool.Hidden.Add((renderer, renderer.enabled));
            renderer.enabled = false;
        }

        internal void LateUpdate()
        {
            if (!_world.Active) return;

            foreach (Tool tool in _tools.Values)
            {
                if (!tool.State.Equipped) continue;

                if (tool.WorldModel)
                {
                    AlignWorld(tool.WorldModel, tool.AimTransform, tool.Hand, tool.Finger);
                }

                bool thirdPerson = tool.ViewControl && tool.ViewControl.HCAOIMFNLMB;
                if (tool.Model)
                {
                    tool.Model.SetActive(!thirdPerson);
                }

                foreach (var item in tool.Hidden)
                {
                    if (!item.Renderer) continue;

                    item.Renderer.enabled = false;
                }

                GameObject root = _world.Building.Root(tool.State.Holding);
                Vector3 to = root ? root.transform.TransformPoint(tool.State.Grip != null ? SandboxWorld.Vector(tool.State.Grip) : Vector3.zero) : Vector3.zero;

                bool worldVisible = 
                    root &&
                    tool.WorldModel &&
                    tool.WorldModel.activeInHierarchy &&
                    (!Camera.main ||
                    Vector3.Distance(Camera.main.transform.position, tool.WorldModel.transform.position) < 100f);

                Beam(ref tool.Beam, ref tool.Segments, "NullFramePhysGunBeam", tool.WorldModel, to, worldVisible);

                bool viewVisible = root && tool.Model && tool.Model.activeInHierarchy && !thirdPerson;
                Beam(ref tool.ViewBeam, ref tool.ViewSegments, "NullFramePhysGunViewBeam", tool.Model, to, viewVisible);
            }
        }

        private void Beam(ref GameObject beam, ref Transform[] segments, string name, GameObject model, Vector3 to, bool visible)
        {
            if (!visible)
            {
                if (beam)
                {
                    beam.SetActive(false);
                }

                return;
            }

            if (!beam)
            {
                beam = new GameObject(name);
                segments = new Transform[16];

                for (int index = 0; index < segments.Length; index++)
                {
                    GameObject segment = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    Collider collider = segment.GetComponent<Collider>();
                    collider.enabled = false;

                    Object.Destroy(collider);

                    segment.transform.SetParent(beam.transform, false);
                    segments[index] = segment.transform;

                    Renderer renderer = segment.GetComponent<Renderer>();
                    renderer.sharedMaterial = _glow;
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                }
            }

            beam.SetActive(true);

            Vector3 from = model.transform.TransformPoint(MuzzlePoint);
            Vector3 direction = (to - from).normalized;
            Vector3 side = Vector3.Cross(direction, Vector3.up).normalized;
            Vector3 up = Vector3.Cross(side, direction).normalized;
            Vector3 last = from;

            for (int index = 0; index < segments.Length; index++)
            {
                float amount = (index + 1f) / segments.Length;
                float phase = amount * 15f - Time.unscaledTime * 9f;

                Vector3 point = Vector3.Lerp(from, to, amount) + (side * Mathf.Sin(phase) + up * Mathf.Cos(phase)) * (Mathf.Sin(amount * Mathf.PI) * 0.018f);
                Transform segment = segments[index];
                segment.gameObject.layer = model.layer;
                segment.SetPositionAndRotation
                (
                    (last + point) * 0.5f,
                    Quaternion.FromToRotation(Vector3.forward, point - last)
                );
                segment.localScale = new Vector3(0.014f, 0.014f, Vector3.Distance(last, point) + 0.003f);

                last = point;
            }
        }

        private static void DestroyVisual(Tool tool)
        {
            foreach (GameObject node in new[]
            {
                tool.Model,
                tool.WorldModel,
                tool.Beam,
                tool.ViewBeam
            })
            {
                if (node)
                {
                    Object.Destroy(node);
                }
            }

            tool.Model = tool.WorldModel = tool.Beam = tool.ViewBeam = null;
            tool.Weapon = null;
            tool.ViewControl = null;
            tool.AimTransform = tool.Hand = tool.Finger = null;

            foreach (var item in tool.Hidden)
            {
                if (item.Renderer)
                {
                    item.Renderer.enabled = item.Enabled;
                }
            }

            tool.Hidden.Clear();
        }

        internal void Disconnect(int id)
        {
            Release(id, false, false);

            if (_tools.Remove(id, out var tool))
            {
                DestroyVisual(tool);
            }
        }

        internal void Clear()
        {
            foreach (Tool tool in _tools.Values)
            {
                DestroyVisual(tool);
            }

            _tools.Clear();
            _equipped = _releasePending = false;
            _nextAim = _scanAt = 0f;
            _aimSequence = 0;
        }
    }
}
