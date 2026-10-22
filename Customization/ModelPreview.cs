using Il2Cpp;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.Rendering;

namespace NullServers.Customization
{
    internal sealed class ModelPreview : IDisposable
    {
        private readonly GameObject _root;
        private readonly GameObject _cameraObject;
        private readonly GameObject _lightObject;
        private readonly Transform _pivot;
        private readonly Camera _camera;
        private readonly AppearanceVisual _visual;
        private readonly Dictionary<IntPtr, Transform> _nodes = new();
        private readonly List<Renderer> _renderers = new();
        private ParticleSystem[] _particles = Array.Empty<ParticleSystem>();
        private readonly float _height;
        private Quaternion _rotation = Quaternion.Euler(0f, 180f, 0f);
        private int _effectHat = -1;

        internal RenderTexture Texture { get; }
        internal bool AutoRotate { get; set; } = true;
        internal float Zoom { get; set; } = 1f;

        internal ModelPreview(SyncCustomization source, LiveAppearance appearance)
        {
            _root = new GameObject("ModelPreview")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            _root.SetActive(false);

            try
            {
                _pivot = _root.transform;
                _pivot.position = new Vector3(0f, -10000, 0f);

                List<Renderer> renderers =
                [
                    source.KAMAAAJBLLG,
                    source.EKMFOOLPMCC,
                    source.NGBLPBOAANH,
                    source.DCBPBGEFBHD,
                    source.FFDDAKMAHKI,
                    source.JCGAKKLDCKJ,
                    source.IIGKOHECBEL
                ];
                if (source.LEEICKEPPEB)
                {
                    renderers.AddRange(source.LEEICKEPPEB.GetComponentsInChildren(Il2CppType.Of<Renderer>(), true).Select(component => component.Cast<Renderer>()));
                }

                renderers = renderers.Where(renderer => renderer).DistinctBy(renderer => renderer.Pointer).ToList();
                if (renderers.Count == 0)
                {
                    throw new InvalidOperationException("The player model has no renderable meshes.");
                }

                Transform sourceRoot = source.transform;
                foreach (Transform node in renderers.Select(renderer => renderer.transform).Concat(new[] { source.ELPJDJFCCNJ, source.LEEICKEPPEB }).Where(node => node))
                {
                    while (sourceRoot && node != sourceRoot && !node.IsChildOf(sourceRoot))
                    {
                        sourceRoot = sourceRoot.parent;
                    }
                }

                if (!sourceRoot)
                {
                    throw new InvalidOperationException("The player model hierarchy is incomplete.");
                }

                CopyNodes(sourceRoot, _pivot, source.LEEICKEPPEB);

                Transform model = _nodes[sourceRoot.Pointer];
                model.localPosition = Vector3.zero;
                model.localRotation = Quaternion.identity;

                foreach (Transform branch in new[] { source.ELPJDJFCCNJ, source.LEEICKEPPEB })
                {
                    if (!branch) continue;

                    for (Transform node = _nodes[branch.Pointer]; node && node != _pivot; node = node.parent)
                    {
                        node.gameObject.SetActive(true);
                    }
                }

                Dictionary<IntPtr, Material[]> defaults = new();
                bool customHead = source.EKMFOOLPMCC && source.EKMFOOLPMCC.gameObject.activeSelf;

                foreach (Renderer original in renderers)
                {
                    Transform node = _nodes[original.transform.Pointer];

                    bool bodyPart = 
                        original == source.KAMAAAJBLLG ||
                        original == source.NGBLPBOAANH ||
                        original == source.DCBPBGEFBHD ||
                        original == source.FFDDAKMAHKI ||
                        original == source.JCGAKKLDCKJ ||
                        original == source.IIGKOHECBEL;

                    if (bodyPart || original == source.EKMFOOLPMCC && customHead)
                    {
                        for (Transform ancestor = node; ancestor && ancestor != _pivot; ancestor = ancestor.parent)
                        {
                            ancestor.gameObject.SetActive(true);
                        }
                    }

                    Renderer renderer;
                    SkinnedMeshRenderer skin = original.TryCast<SkinnedMeshRenderer>();

                    bool hat = 
                        source.LEEICKEPPEB &&
                        (original.transform == source.LEEICKEPPEB ||
                        original.transform.IsChildOf(source.LEEICKEPPEB));

                    if (hat)
                    {
                        renderer = node.GetComponent(Il2CppType.Of<Renderer>())?.Cast<Renderer>();
                    }
                    else if (skin)
                    {
                        SkinnedMeshRenderer copy = node.gameObject.AddComponent(Il2CppType.Of<SkinnedMeshRenderer>()).Cast<SkinnedMeshRenderer>();
                        copy.sharedMesh = skin.sharedMesh;
                        copy.bones = new Il2CppReferenceArray<Transform>(skin.bones.Select(bone => bone ? _nodes.GetValueOrDefault(bone.Pointer) : null).ToArray());
                        copy.rootBone = skin.rootBone ? _nodes.GetValueOrDefault(skin.rootBone.Pointer) : null;
                        copy.localBounds = skin.localBounds;
                        copy.updateWhenOffscreen = true;
                        renderer = copy;
                    }
                    else
                    {
                        MeshFilter filter = original.GetComponent(Il2CppType.Of<MeshFilter>())?.Cast<MeshFilter>();
                        if (!filter) continue;

                        node.gameObject.AddComponent(Il2CppType.Of<MeshFilter>()).Cast<MeshFilter>().sharedMesh = filter.sharedMesh;
                        renderer = node.gameObject.AddComponent(Il2CppType.Of<MeshRenderer>()).Cast<MeshRenderer>();
                    }

                    if (!renderer) continue;

                    renderer.sharedMaterials = original.sharedMaterials;
                    defaults[renderer.Pointer] = appearance.OriginalMaterials(source, original);
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    renderer.forceRenderingOff = false;
                    renderer.enabled = 
                        original.enabled ||
                        bodyPart ||
                        original == source.EKMFOOLPMCC &&
                        customHead ||
                        source.LEEICKEPPEB &&
                        original.transform.IsChildOf(source.LEEICKEPPEB);

                    if (original == source.KAMAAAJBLLG)
                    {
                        renderer.enabled = !customHead;
                    }

                    _renderers.Add(renderer);
                }

                // Reconstruct the bind pose without an Animator or any components from the live network player.
                Dictionary<IntPtr, (Transform Bone, Matrix4x4 Matrix)> poses = new();

                foreach (Renderer renderer in _renderers)
                {
                    SkinnedMeshRenderer skin = renderer.TryCast<SkinnedMeshRenderer>();
                    if (!skin || !skin.sharedMesh) continue;

                    var bindposes = skin.sharedMesh.bindposes;
                    var bones = skin.bones;
                    for (int index = 0; index < Math.Min(bindposes.Length, bones.Length); index++)
                    {
                        if (!bones[index]) continue;

                        poses.TryAdd
                        (
                            bones[index].Pointer,
                            (bones[index], skin.localToWorldMatrix * bindposes[index].inverse)
                        );
                    }
                }

                foreach (Transform node in _nodes.Values)
                {
                    if (poses.TryGetValue(node.Pointer, out var pose))
                    {
                        node.position = pose.Matrix.MultiplyPoint3x4(Vector3.zero);
                        node.rotation = pose.Matrix.rotation;
                    }
                }

                PoseArm(source.DCBPBGEFBHD, model.TransformDirection(Vector3.left));
                PoseArm(source.FFDDAKMAHKI, model.TransformDirection(Vector3.right));

                _visual = new AppearanceVisual(source, node => _nodes.GetValueOrDefault(node.Pointer), defaults);
                _visual.RetainCurrent(LiveAppearance.Read(source), appearance.Textures);
                _root.SetActive(true);

                Bounds bounds = new(_pivot.position, Vector3.one);
                bool first = true;

                foreach (Renderer renderer in _renderers.Where(renderer => renderer.enabled && renderer.gameObject.activeInHierarchy))
                {
                    if (renderer.TryCast<ParticleSystemRenderer>()) continue;

                    if (first)
                    {
                        bounds = renderer.bounds;
                        first = false;
                    }
                    else
                    {
                        bounds.Encapsulate(renderer.bounds);
                    }
                }

                _height = Mathf.Max(0.5f, bounds.size.y);
                model.position -= bounds.center - _pivot.position;

                UpdateEffects(LiveAppearance.Read(source).Hat);

                Texture = new RenderTexture(768, 1024, 24)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    antiAliasing = 2
                };
                Texture.Create();

                _cameraObject = new GameObject("PreviewCamera")
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                _camera = _cameraObject.AddComponent(Il2CppType.Of<Camera>()).Cast<Camera>();
                _camera.enabled = false;
                _camera.cullingMask = 1 << 30;
                _camera.clearFlags = CameraClearFlags.SolidColor;
                _camera.backgroundColor = new Color(0.035f, 0.045f, 0.06f);
                _camera.nearClipPlane = 0.01f;
                _camera.farClipPlane = _height * 20f;
                _camera.fieldOfView = 32f;
                _camera.targetTexture = Texture;

                _lightObject = new GameObject("PreviewLight")
                {
                    hideFlags = HideFlags.HideAndDontSave
                };

                Light light = _lightObject.AddComponent(Il2CppType.Of<Light>()).Cast<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.5f;
                light.cullingMask = 1 << 30;

                _lightObject.transform.rotation = Quaternion.Euler(35f, -25, 0f);

                Render();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        private void CopyNodes(Transform source, Transform parent, Transform hats)
        {
            if (source == hats)
            {
                // Clone effect modules while inactive, before their destruction/gameplay scripts can run.
                GameObject clone = UnityEngine.Object.Instantiate(source.gameObject, parent, false).Cast<GameObject>();
                foreach (Component component in clone.GetComponentsInChildren(Il2CppType.Of<Component>(), true))
                {
                    if 
                    (
                        component &&
                        !component.TryCast<Transform>() &&
                        !component.TryCast<Renderer>() &&
                        !component.TryCast<MeshFilter>() &&
                        !component.TryCast<ParticleSystem>() &&
                        !component.TryCast<Light>() &&
                        !component.TryCast<Animation>() &&
                        !component.TryCast<Animator>()
                    )
                    {
                        UnityEngine.Object.DestroyImmediate(component);
                    }
                }

                MapHats(source, clone.transform);

                _particles = clone.GetComponentsInChildren(Il2CppType.Of<ParticleSystem>(), true).Select(value => value.Cast<ParticleSystem>()).ToArray();
                foreach (ParticleSystem particles in _particles)
                {
                    ParticleSystem.MainModule main = particles.main;
                    main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
                    ParticleSystem.EmissionModule emission = particles.emission;
                    emission.enabled = true;
                }

                foreach (Component component in clone.GetComponentsInChildren(Il2CppType.Of<Light>(), true))
                {
                    component.Cast<Light>().cullingMask = 1 << 30;
                }

                foreach (Component component in clone.GetComponentsInChildren(Il2CppType.Of<Animator>(), true))
                {
                    Animator animator = component.Cast<Animator>();
                    animator.updateMode = AnimatorUpdateMode.UnscaledTime;
                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                }

                return;
            }

            GameObject copy = new(source.name)
            {
                layer = 30,
                hideFlags = HideFlags.HideAndDontSave
            };

            Transform node = copy.transform;
            node.SetParent(parent, false);
            node.localPosition = source.localPosition;
            node.localRotation = source.localRotation;
            node.localScale = source.localScale;

            _nodes[source.Pointer] = node;

            for (int index = 0; index < source.childCount; index++)
            {
                CopyNodes(source.GetChild(index), node, hats);
            }

            copy.SetActive(source.gameObject.activeSelf);
        }

        private void MapHats(Transform source, Transform copy)
        {
            _nodes[source.Pointer] = copy;

            copy.gameObject.layer = 30;
            copy.gameObject.hideFlags = HideFlags.HideAndDontSave;

            for (int index = 0; index < source.childCount; index++)
            {
                MapHats(source.GetChild(index), copy.GetChild(index));
            }
        }

        private void PoseArm(SkinnedMeshRenderer arm, Vector3 direction)
        {
            if (!arm) return;

            Transform[] bones = arm.bones.Where(bone => bone).Select(bone => _nodes.GetValueOrDefault(bone.Pointer)).Where(bone => bone).ToArray();
            Transform shoulder = bones.FirstOrDefault(bone => bone.name.EndsWith("UpperArm", StringComparison.Ordinal));
            Transform elbow = bones.FirstOrDefault(bone => bone.name.EndsWith("Forearm", StringComparison.Ordinal));
            Transform hand = bones.FirstOrDefault(bone => bone.name.EndsWith("Hand", StringComparison.Ordinal));

            if (!shoulder || !elbow || !hand) return;

            shoulder.rotation = Quaternion.FromToRotation(elbow.position - shoulder.position, direction) * shoulder.rotation;
            elbow.rotation = Quaternion.FromToRotation(hand.position - elbow.position, direction) * elbow.rotation;
        }

        internal void Rotate(Vector2 delta)
        {
            AutoRotate = false;
            _rotation = Quaternion.AngleAxis
            (
                -delta.y * 0.6f, Vector3.right) *
                Quaternion.AngleAxis(delta.x * 0.6f, Vector3.up
            ) * _rotation;
        }

        internal void Update(Appearance appearance, SkinTextures textures)
        {
            _visual.Apply(appearance, textures);

            if (AutoRotate)
            {
                _rotation = Quaternion.AngleAxis(Time.unscaledDeltaTime * 22f, Vector3.up) * _rotation;
            }

            UpdateEffects(appearance.Hat);
            Render();
        }

        private void UpdateEffects(int hat)
        {
            if (_effectHat == hat) return;

            foreach (ParticleSystem particles in _particles)
            {
                particles.Clear(false);
                if (!particles.gameObject.activeInHierarchy) continue;

                particles.Play(false);
            }

            _effectHat = hat;
        }

        private void Render()
        {
            _pivot.rotation = _rotation;

            Vector3 center = _pivot.position;
            _camera.transform.position = center + new Vector3
            (
                0f,
                _height * 0.04f,
                -_height * 2.6f / Mathf.Clamp(Zoom, 0.7f, 2.5f)
            );
            _camera.transform.LookAt(center);
            _camera.Render();
        }

        public void Dispose()
        {
            _visual?.Dispose();
            if (_cameraObject)
            {
                UnityEngine.Object.Destroy(_cameraObject);
            }

            if (_lightObject)
            {
                UnityEngine.Object.Destroy(_lightObject);
            }

            if (_root)
            {
                UnityEngine.Object.Destroy(_root);
            }

            if (Texture)
            {
                Texture.Release();
                UnityEngine.Object.Destroy(Texture);
            }
        }
    }
}
