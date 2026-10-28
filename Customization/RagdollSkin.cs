using Il2Cpp;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using NullServers.Network;
using NullServers.Additional;
using UnityEngine;

namespace NullServers.Customization
{
    internal sealed partial record RagdollSkin
    {
        internal static RagdollSkin Capture(int player, PlayerDamage damage = null)
        {
            SyncCustomization sync = UnityEngine.Object.FindObjectsOfType<SyncCustomization>(true)
                .Where(value => value.IsClientInitialized && value.OwnerId == player &&
                !value.FromLegacyReplay && value.HatId >= 0)
                .OrderByDescending(value => damage && value.NetworkObject == damage.NetworkObject)
                .ThenByDescending(value => value.gameObject.activeInHierarchy).FirstOrDefault();
            if (!sync || sync.HatId < 0) return null;

            Renderer[] renderers =
            [
                sync.Head,
                sync.SkinHead,
                sync.Body,
                sync.LeftArm,
                sync.RightArm,
                sync.LeftLeg,
                sync.RightLeg
            ];
            int[] parts = [-1, 0, 1, 2, 2, 3, 3];
            RagdollSurface[] surfaces = renderers.Select((renderer, index) =>
                renderer ? new RagdollSurface(renderer.name, MeshName(renderer), parts[index]) : null)
                .Where(value => value != null).ToArray();
            string hat = sync.HatId < sync.Hats.Count ? sync.Hats[sync.HatId].name : "";

            Appearance appearance = LiveAppearance.Read(sync);
            string[] extra = (appearance.ExtraHats ?? Array.Empty<int>()).Where(id => id > 0 && id < sync.Hats.Count)
                .Select(id => sync.Hats[id]?.name ?? "").ToArray();

            FrameworkAddon addon = Frameworks.Customization;
            string[] custom = addon?.Enabled == true && addon.Read<int>(addon.Catalog, "SelectionApi") == 1 ?
                appearance.Hats.Select(id => addon.Call<string>(addon.Catalog, "HatKey", sync, id))
                    .Where(key => !string.IsNullOrEmpty(key) && !key.StartsWith("base:", StringComparison.Ordinal)).ToArray() : null;

            return new RagdollSkin(appearance, surfaces, hat, extra, custom);
        }

        internal static string MeshName(Renderer renderer)
        {
            SkinnedMeshRenderer skin = renderer.TryCast<SkinnedMeshRenderer>();
            MeshFilter filter = renderer.GetComponent(Il2CppType.Of<MeshFilter>())?.Cast<MeshFilter>();
            return skin && skin.sharedMesh ? skin.sharedMesh.name : filter && filter.sharedMesh ? filter.sharedMesh.name : "";
        }
    }

    internal sealed class RagdollVisual : IDisposable
    {
        private sealed record Surface(Renderer Renderer, Material[] Originals, Material[] Materials, int Part);
        private readonly RagdollSkin _skin;
        private readonly SkinTextures _textures;
        private readonly List<Surface> _surfaces = new();
        private readonly List<GameObject> _createdHats = new();
        private readonly List<Transform> _hats = new();
        private readonly HashSet<string> _selected;
        private readonly bool _customHead;

        internal RagdollVisual(GameObject root, RagdollSkin skin, SkinTextures textures)
        {
            _skin = skin;
            _textures = textures;

            foreach (Renderer renderer in root.GetComponentsInChildren(Il2CppType.Of<Renderer>(), true).Select(value => value.Cast<Renderer>()))
            {
                if (renderer.TryCast<ParticleSystemRenderer>()) continue;

                RagdollSurface description = skin.Surfaces.FirstOrDefault(value => value.Name == renderer.name) ??
                    skin.Surfaces.FirstOrDefault(value => value.Mesh.Length > 0 && value.Mesh == RagdollSkin.MeshName(renderer));
                if (description == null) continue;

                Material[] originals = renderer.sharedMaterials.ToArray();
                Material[] copies = originals.Select(material => material ? new Material(material) : null).ToArray();
                renderer.sharedMaterials = new Il2CppReferenceArray<Material>(copies);
                _surfaces.Add(new Surface(renderer, originals, copies, description.Part));
            }

            _customHead = _surfaces.Any(surface => surface.Part == 0);
            _selected = new(new[] { skin.Hat }.Concat(skin.ExtraHats ?? Array.Empty<string>()), StringComparer.Ordinal);

            Transform[] roots = root.GetComponentsInChildren<Transform>(true).Where(node =>
                node.name.Equals("HATS", StringComparison.OrdinalIgnoreCase)).ToArray();

            foreach (Transform hats in roots)
            {
                for (int index = 0; index < hats.childCount; index++)
                {
                    _hats.Add(hats.GetChild(index));
                }
            }

            FrameworkAddon addon = Frameworks.Customization;

            if (roots.Length > 0 && addon?.Enabled == true && addon.Read<int>(addon.Catalog, "SelectionApi") == 1)
            {
                foreach (string key in skin.CustomHats ?? Array.Empty<string>())
                {
                    GameObject hat = addon.Call<GameObject>(addon.Catalog, "CreateHat", roots[0], key);

                    if (!hat) continue;

                    _createdHats.Add(hat);
                    _hats.Add(hat.transform);
                }
            }

            Update();
        }

        internal void Update()
        {
            Appearance appearance = _skin.Appearance;
            Texture2D[] images = _textures.Get(appearance);
            ColorUtility.TryParseHtmlString(appearance.FurColor, out Color fur);

            foreach (Transform hat in _hats)
            {
                if (!hat) continue;

                bool selected = _selected.Contains(hat.name);
                if (hat.gameObject.activeSelf != selected)
                {
                    hat.gameObject.SetActive(selected);
                }
            }

            foreach (Surface surface in _surfaces)
            {
                if (!surface.Renderer) continue;

                Material[] current = surface.Renderer.sharedMaterials.ToArray();
                if (current.Length != surface.Materials.Length || current.Where((material, index) =>
                    !material || !surface.Materials[index] || material.Pointer != surface.Materials[index].Pointer).Any())
                {
                    surface.Renderer.sharedMaterials = new Il2CppReferenceArray<Material>(surface.Materials);
                }

                int part = Math.Max(0, surface.Part);
                Texture2D image = images[part];

                if (surface.Part == -1 && _customHead)
                {
                    surface.Renderer.enabled = !images[0];
                    image = null;
                }
                else if (surface.Part == 0)
                {
                    surface.Renderer.gameObject.SetActive(image);
                    surface.Renderer.enabled = image;
                }
                else
                {
                    surface.Renderer.enabled = true;
                    surface.Renderer.forceRenderingOff = false;
                }

                if (surface.Materials.Length > 0)
                {
                    Material material = surface.Materials[0];
                    if (!material) continue;

                    if (surface.Originals[0])
                    {
                        material.CopyPropertiesFromMaterial(surface.Originals[0]);
                    }

                    material.color = image ? Color.white : fur;
                    if (!image) continue;

                    if (surface.Part > 0)
                    {
                        Shader shader = Shader.Find("Diffuse");
                        if (shader)
                        {
                            material.shader = shader;
                        }
                    }

                    material.mainTexture = image;
                    if (material.HasProperty("_TopMainTex"))
                    {
                        material.SetTexture("_TopMainTex", image);
                    }
                }
            }
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
                    if (material)
                    {
                        UnityEngine.Object.Destroy(material);
                    }
                }
            }

            _surfaces.Clear();

            foreach (GameObject hat in _createdHats)
            {
                if (hat)
                {
                    UnityEngine.Object.Destroy(hat);
                }
            }

            _createdHats.Clear();
            _hats.Clear();
        }
    }
}
