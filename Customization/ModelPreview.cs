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
        private readonly Light _light;
        private readonly AppearanceVisual _visual;
        private readonly Dictionary<IntPtr, Transform> _nodes = new();
        private readonly List<Renderer> _renderers = new();
        private readonly List<Material> _hatMaterials = new();
        private ParticleSystem[] _particles = Array.Empty<ParticleSystem>();
        private readonly float _height;
        private Quaternion _rotation = Quaternion.Euler(0f, 180f, 0f);
        private int[] _effectHats = Array.Empty<int>();
        private readonly HashSet<IntPtr> _hatRoots = new();

        internal RenderTexture Texture { get; }
        internal bool AutoRotate { get; set; } = true;
        internal float Zoom { get; set; } = 1f;
        internal float Lighting { get; set; } = 0.8f;

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
                    source.Head,
                    source.SkinHead,
                    source.Body,
                    source.LeftArm,
                    source.RightArm,
                    source.LeftLeg,
                    source.RightLeg
                ];
                if (source.HatRoot)
                {
                    renderers.AddRange(source.HatRoot.GetComponentsInChildren(Il2CppType.Of<Renderer>(), true).Select(component => component.Cast<Renderer>()));
                }

                foreach (Transform hat in source.Hats)
                {
                    if (!hat) continue;

                    if (hat.parent)
                    {
                        _hatRoots.Add(hat.parent.Pointer);
                    }

                    renderers.AddRange(hat.GetComponentsInChildren<Renderer>(true));
                }

                renderers = renderers.Where(renderer => renderer).DistinctBy(renderer => renderer.Pointer).ToList();
                if (renderers.Count == 0)
                {
                    throw new InvalidOperationException("The player model has no renderable meshes.");
                }

                Transform sourceRoot = source.transform;
                foreach (Transform node in renderers.Select(renderer => renderer.transform).Concat(new[] { source.AccentRoot, source.HatRoot }).Where(node => node))
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

                CopyNodes(sourceRoot, _pivot, source.HatRoot);

                Transform model = _nodes[sourceRoot.Pointer];
                model.localPosition = Vector3.zero;
                model.localRotation = Quaternion.identity;

                foreach (Transform branch in new[] { source.AccentRoot, source.HatRoot })
                {
                    if (!branch) continue;

                    for (Transform node = _nodes[branch.Pointer]; node && node != _pivot; node = node.parent)
                    {
                        node.gameObject.SetActive(true);
                    }
                }

                Dictionary<IntPtr, Material[]> defaults = new();
                bool customHead = source.SkinHead && source.SkinHead.gameObject.activeSelf;

                foreach (Renderer original in renderers)
                {
                    Transform node = _nodes[original.transform.Pointer];

                    bool bodyPart = original == source.Head ||
                        original == source.Body ||
                        original == source.LeftArm ||
                        original == source.RightArm ||
                        original == source.LeftLeg ||
                        original == source.RightLeg;

                    if (bodyPart || original == source.SkinHead && customHead)
                    {
                        for (Transform ancestor = node; ancestor && ancestor != _pivot; ancestor = ancestor.parent)
                        {
                            ancestor.gameObject.SetActive(true);
                        }
                    }

                    Renderer renderer;
                    SkinnedMeshRenderer skin = original.TryCast<SkinnedMeshRenderer>();

                    bool hat = source.Hats.ToArray().Any(node => node &&
                        (original.transform == node || original.transform.IsChildOf(node)));

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
                    if (hat)
                    {
                        Material[] materials = renderer.sharedMaterials.Select(material => material ? new Material(material) : null).ToArray();
                        renderer.sharedMaterials = new Il2CppReferenceArray<Material>(materials);
                        _hatMaterials.AddRange(materials.Where(material => material));
                    }

                    defaults[renderer.Pointer] = appearance.OriginalMaterials(source, original);
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    renderer.forceRenderingOff = false;
                    renderer.enabled = original.enabled ||
                        bodyPart ||
                        original == source.SkinHead &&
                        customHead ||
                        hat;

                    if (original == source.Head)
                    {
                        renderer.enabled = !customHead;
                    }

                    _renderers.Add(renderer);
                }

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

                        poses.TryAdd(bones[index].Pointer, (bones[index], skin.localToWorldMatrix * bindposes[index].inverse));
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

                RestoreHead(source);
                PoseArm(source.LeftArm, model.TransformDirection(Vector3.left));
                PoseArm(source.RightArm, model.TransformDirection(Vector3.right));

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

                UpdateEffects(LiveAppearance.Read(source).Hats);

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

                _light = _lightObject.AddComponent(Il2CppType.Of<Light>()).Cast<Light>();
                _light.type = LightType.Directional;
                _light.cullingMask = 1 << 30;

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
            if (source == hats || _hatRoots.Contains(source.Pointer))
            {
                GameObject clone = UnityEngine.Object.Instantiate(source.gameObject, parent, false).Cast<GameObject>();
                foreach (Component component in clone.GetComponentsInChildren(Il2CppType.Of<Component>(), true))
                {
                    if (component &&
                        !component.TryCast<Transform>() &&
                        !component.TryCast<Renderer>() &&
                        !component.TryCast<MeshFilter>() &&
                        !component.TryCast<ParticleSystem>() &&
                        !component.TryCast<Light>() &&
                        !component.TryCast<Animation>() &&
                        !component.TryCast<Animator>())
                    {
                        UnityEngine.Object.DestroyImmediate(component);
                    }
                }

                MapHats(source, clone.transform);

                _particles = _particles.Concat(clone.GetComponentsInChildren(Il2CppType.Of<ParticleSystem>(), true)
                    .Select(value => value.Cast<ParticleSystem>())).ToArray();
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

        private void RestoreHead(SyncCustomization source)
        {
            SyncCustomization baseline = Resources.FindObjectsOfTypeAll<SyncCustomization>()
                .FirstOrDefault(value => value && !value.gameObject.scene.IsValid() &&
                value.Body && value.Body.sharedMesh == source.Body.sharedMesh);
            if (!baseline) return;

            Transform[] originals = baseline.transform.root.GetComponentsInChildren<Transform>(true).ToArray();
            foreach (Transform node in source.transform.root.GetComponentsInChildren<Transform>(true))
            {
                if (!_nodes.TryGetValue(node.Pointer, out var copy) ||
                    !(node.name.EndsWith(" Head", StringComparison.Ordinal) ||
                    node.name.EndsWith(" Neck", StringComparison.Ordinal)))
                {
                    continue;
                }

                Transform rest = originals.FirstOrDefault(value => value.name == node.name);
                if (!rest) continue;

                copy.localPosition = rest.localPosition;
                copy.localRotation = rest.localRotation;
            }
        }

        internal void Rotate(Vector2 delta)
        {
            AutoRotate = false;
            _rotation = Quaternion.AngleAxis(
                -delta.y * 0.6f, Vector3.right) *
                Quaternion.AngleAxis(delta.x * 0.6f, Vector3.up) * _rotation;
        }

        internal void Update(Appearance appearance, SkinTextures textures)
        {
            _visual.Apply(appearance, textures);

            if (AutoRotate)
            {
                _rotation = Quaternion.AngleAxis(Time.unscaledDeltaTime * 22f, Vector3.up) * _rotation;
            }

            UpdateEffects(appearance.Hats);
            Render();
        }

        private void UpdateEffects(int[] hats)
        {
            if (_effectHats.SequenceEqual(hats)) return;

            foreach (ParticleSystem particles in _particles)
            {
                particles.Clear(false);
                if (!particles.gameObject.activeInHierarchy) continue;

                particles.Play(false);
            }

            _effectHats = hats;
        }

        private void Render()
        {
            _pivot.rotation = _rotation;

            Vector3 center = _pivot.position;
            _camera.transform.position = center + new Vector3(
                0f,
                _height * 0.04f,
                -_height * 2.6f / Mathf.Clamp(Zoom, 0.7f, 2.5f));
            _camera.transform.LookAt(center);
            _light.intensity = Mathf.Clamp(Lighting, 0.15f, 2f);

            foreach (Renderer renderer in _renderers)
            {
                if (!renderer) continue;

                foreach (Material material in renderer.sharedMaterials)
                {
                    if (!material) continue;

                    if (material.HasProperty("_Glossiness"))
                    {
                        material.SetFloat("_Glossiness", 0.12f);
                    }

                    if (material.HasProperty("_Metallic"))
                    {
                        material.SetFloat("_Metallic", 0f);
                    }
                }
            }

            AmbientMode ambientMode = RenderSettings.ambientMode;
            Color ambient = RenderSettings.ambientLight;
            float reflection = RenderSettings.reflectionIntensity;

            try
            {
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = Color.white * (Lighting * 0.45f);
                RenderSettings.reflectionIntensity = 0f;
                _camera.Render();
            }
            finally
            {
                RenderSettings.ambientMode = ambientMode;
                RenderSettings.ambientLight = ambient;
                RenderSettings.reflectionIntensity = reflection;
            }
        }

        public void Dispose()
        {
            _visual?.Dispose();
            foreach (Material material in _hatMaterials)
            {
                if (material)
                {
                    UnityEngine.Object.Destroy(material);
                }
            }

            _hatMaterials.Clear();
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
