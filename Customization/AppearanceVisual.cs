using Il2Cpp;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace NullServers.Customization
{
    internal sealed class AppearanceVisual : IDisposable
    {
        private sealed record Surface(Renderer Renderer, Material[] Originals, Material[] Materials);

        private readonly List<Surface> _surfaces = new();
        private readonly HashSet<IntPtr> _fur = new();
        private readonly Transform[] _hats;
        private readonly Renderer _head;
        private readonly Renderer _skinHead;
        private readonly bool _headForceOff;
        private readonly bool _headEnabled;
        private readonly bool _preview;
        private readonly Renderer[][] _parts;
        private Appearance _applied;
        private Texture2D[] _images;

        internal AppearanceVisual
        (
            SyncCustomization source,
            Func<Transform, Transform> map = null,
            IReadOnlyDictionary<IntPtr, Material[]> defaults = null
        )
        {
            _preview = map != null;

            Transform Node(Transform value) => value && map != null ? map(value) : value;
            Renderer SurfaceFor(Renderer value) => value && map != null ? Node(value.transform)?.GetComponent(Il2CppType.Of<Renderer>())?.Cast<Renderer>() : value;

            _head = SurfaceFor(source.KAMAAAJBLLG);
            _headForceOff = _head && _head.forceRenderingOff;
            _headEnabled = _preview || _head && _head.enabled;
            _skinHead = SurfaceFor(source.EKMFOOLPMCC);
            _parts =
            [
                [_skinHead],
                [SurfaceFor(source.NGBLPBOAANH)],
                [SurfaceFor(source.DCBPBGEFBHD), SurfaceFor(source.FFDDAKMAHKI)],
                [SurfaceFor(source.JCGAKKLDCKJ), SurfaceFor(source.IIGKOHECBEL)]
            ];

            _hats = Enumerable.Range(0, source.GKCNMADGAAF.Count).Select(index => Node(source.GKCNMADGAAF[index])).ToArray();

            foreach (Renderer renderer in _parts.Skip(1).SelectMany(part => part).Where(renderer => renderer))
            {
                _fur.Add(renderer.Pointer);
            }

            if (_head)
            {
                _fur.Add(_head.Pointer);
            }

            var renderers = _parts.SelectMany(part => part).Append(_head).Where(renderer => renderer).DistinctBy(renderer => renderer.Pointer);
            foreach (Renderer renderer in renderers)
            {
                Material[] current = renderer.sharedMaterials.ToArray();
                if (current.Length == 0) continue;

                Material[] originals = defaults != null && defaults.TryGetValue(renderer.Pointer, out var baseline) ? baseline : current;
                Material[] materials = current.Select(material => material ? new Material(material) : null).ToArray();

                renderer.sharedMaterials = new Il2CppReferenceArray<Material>(materials);

                // Unity can instantiate shared materials on the next materials getter, so retain the renderer-owned instances.
                Material[] owned = renderer.materials.ToArray();
                foreach (Material material in materials)
                {
                    if (material && !owned.Any(value => value && value.Pointer == material.Pointer))
                    {
                        UnityEngine.Object.Destroy(material);
                    }
                }

                _surfaces.Add(new Surface(renderer, originals, owned));
            }
        }

        internal void Apply(Appearance appearance, SkinTextures textures)
        {
            string[] urls = appearance.Urls;
            Texture2D[] images = urls.Select(url => appearance.UseSkin ? textures.Get(url) : null).ToArray();

            if (_applied?.SameLook(appearance) == true && _images != null && images.SequenceEqual(_images))
            {
                return;
            }

            _applied = appearance;
            _images = images;
            ColorUtility.TryParseHtmlString(appearance.FurColor, out Color fur);

            for (int index = 0; index < _hats.Length; index++)
            {
                if (_hats[index])
                {
                    _hats[index].gameObject.SetActive(index == appearance.Hat);
                }
            }

            foreach (Surface surface in _surfaces)
            {
                if (!surface.Renderer || !surface.Materials[0]) continue;

                Material material = surface.Materials[0];
                if (surface.Originals[0])
                {
                    material.shader = surface.Originals[0].shader;
                    material.CopyPropertiesFromMaterial(surface.Originals[0]);
                }

                if (_fur.Contains(surface.Renderer.Pointer))
                {
                    material.color = fur;
                }
            }

            if (_head)
            {
                _head.enabled = !images[0] && _headEnabled;
                _head.forceRenderingOff = images[0] || _headForceOff;
            }

            if (_skinHead)
            {
                _skinHead.gameObject.SetActive(images[0]);
                if (_preview)
                {
                    _skinHead.enabled = images[0];
                }
            }

            for (int part = 0; part < images.Length; part++)
            {
                if (!images[part]) continue;

                foreach (Renderer renderer in _parts[part])
                {
                    if (!renderer) continue;

                    Material material = _surfaces.FirstOrDefault(surface => surface.Renderer == renderer)?.Materials[0];
                    if (!material) continue;

                    if (part != 0)
                    {
                        Shader shader = Shader.Find("Diffuse");
                        if (shader)
                        {
                            material.shader = shader;
                        }
                    }

                    material.mainTexture = images[part];
                    if (part != 0)
                    {
                        material.color = Color.white;
                    }

                    if (material.HasProperty("_TopMainTex"))
                    {
                        material.SetTexture("_TopMainTex", images[part]);
                    }
                }
            }
        }

        internal void RetainCurrent(Appearance appearance, SkinTextures textures)
        {
            for (int index = 0; index < _hats.Length; index++)
            {
                if (!_hats[index]) continue;

                _hats[index].gameObject.SetActive(index == appearance.Hat);
            }

            _applied = appearance;
            _images = appearance.Urls.Select(url => appearance.UseSkin ? textures.Get(url) : null).ToArray();
        }

        internal Material[] OriginalMaterials(Renderer renderer)
        {
            return _surfaces.FirstOrDefault(surface => surface.Renderer == renderer)?.Originals ??
            renderer.sharedMaterials.ToArray();
        }

        public void Dispose()
        {
            foreach (Surface surface in _surfaces)
            {
                if (surface.Renderer)
                {
                    surface.Renderer.sharedMaterials = new Il2CppReferenceArray<Material>(surface.Originals);
                }

                foreach (Material material in surface.Materials)
                {
                    if (!material) continue;

                    UnityEngine.Object.Destroy(material);
                }
            }

            _surfaces.Clear();
        }
    }
}
