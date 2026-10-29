using System.Runtime.InteropServices;
using NullServers.Additional;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NullServers.Room
{
    internal sealed class SkyboxImage : IDisposable
    {
        private CancellationTokenSource _cancellation;
        private Task<byte[]> _download;
        private string _url;
        private Texture2D _texture;
        private readonly List<Texture2D> _faces = new();
        private Material _material;
        private Texture2D _source;
        internal string Error { get; private set; }

        // Returns the sky material for the image at the URL, or null while it downloads or when it cannot be used.
        internal Material Update(string url, float exposure)
        {
            if (_url != url)
            {
                Start(url);
            }

            if (string.IsNullOrEmpty(url)) return null;

            if (_download?.IsCompleted == true)
            {
                FinishDownload();
            }

            Texture2D texture = _texture;
            if (!texture) return null;

            if (_source != texture && !PrepareMaterial(texture)) return null;

            if (_material && _material.HasProperty("_Exposure"))
            {
                _material.SetFloat("_Exposure", exposure);
            }

            return _material;
        }

        private void Start(string url)
        {
            Dispose();
            _url = url;
            Error = null;

            if (string.IsNullOrEmpty(url)) return;

            _cancellation = new CancellationTokenSource();
            _download = PublicImage.Download(url, _cancellation.Token);
            _download.ContinueWith(task => { _ = task.Exception; }, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
        }

        private void FinishDownload()
        {
            try
            {
                byte[] bytes = _download.GetAwaiter().GetResult();
                _texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);

                if (!ImageConversion.LoadImage(_texture, bytes, false))
                {
                    Object.Destroy(_texture);
                    _texture = null;
                    Error = "The sky URL did not return a supported image.";
                }
            }
            catch (Exception exception)
            {
                if (_texture)
                {
                    Object.Destroy(_texture);
                }

                _texture = null;
                Error = Describe("Cannot load sky image", exception);
            }

            _download = null;
        }

        // The panoramic shader takes the image as it is. Without it the image is cut into six cube faces.
        private bool PrepareMaterial(Texture2D texture)
        {
            ReleaseMaterial();
            _source = texture;

            try
            {
                Shader panoramic = Shader.Find("Skybox/Panoramic");
                if (panoramic)
                {
                    _material = new Material(panoramic);
                    _material.SetTexture("_MainTex", texture);
                    return true;
                }

                Shader shader = Shader.Find("Skybox/6 Sided");
                if (!shader)
                {
                    Error = "This game build has no supported image skybox shader.";
                    return false;
                }

                _material = new Material(shader);
                BuildCubeFaces(texture);
                return true;
            }
            catch (Exception exception)
            {
                ReleaseMaterial();
                _source = texture;
                Error = Describe("Cannot prepare sky image", exception);
                return false;
            }
        }

        private void BuildCubeFaces(Texture2D panorama)
        {
            const int size = 256;
            string[] slots = ["_RightTex", "_LeftTex", "_UpTex", "_DownTex", "_FrontTex", "_BackTex"];
            Color32[] source = panorama.GetPixels32().ToArray();

            for (int face = 0; face < slots.Length; face++)
            {
                if (!_material.HasProperty(slots[face])) continue;

                Color32[] colors = new Color32[size * size];
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        Vector3 direction = CubeDirection(face, (x + 0.5f) / size * 2f - 1f, (y + 0.5f) / size * 2f - 1f);
                        colors[y * size + x] = SamplePanorama(source, panorama.width, panorama.height, direction);
                    }
                }

                Texture2D image = new(size, size, TextureFormat.RGBA32, false);
                _faces.Add(image);
                Upload(image, colors);
                image.Apply(false, true);
                _material.SetTexture(slots[face], image);
            }
        }

        private static Vector3 CubeDirection(int face, float u, float v)
        {
            Vector3 direction = face switch
            {
                0 => new(1f, v, -u),
                1 => new(-1f, v, u),
                2 => new(u, 1f, -v),
                3 => new(u, -1f, v),
                4 => new(u, v, 1f),
                _ => new(-u, v, -1f)
            };

            return direction.normalized;
        }

        private static Color32 SamplePanorama(Color32[] pixels, int width, int height, Vector3 direction)
        {
            float longitude = Mathf.Atan2(direction.x, direction.z) / (2f * Mathf.PI) + 0.5f;
            float latitude = Mathf.Asin(direction.y) / Mathf.PI + 0.5f;
            int x = Mathf.Clamp((int)(longitude * width), 0, width - 1);
            int y = Mathf.Clamp((int)(latitude * height), 0, height - 1);

            return pixels[y * width + x];
        }

        private static string Describe(string context, Exception exception)
        {
            string reason = exception.GetBaseException().Message.Replace("<", "").Replace(">", "");
            return context + ": " + reason.Substring(0, Math.Min(240, reason.Length));
        }

        private static void Upload(Texture2D image, Color32[] colors)
        {
            byte[] bytes = new byte[colors.Length * 4];
            for (int index = 0; index < colors.Length; index++)
            {
                bytes[index * 4] = colors[index].r;
                bytes[index * 4 + 1] = colors[index].g;
                bytes[index * 4 + 2] = colors[index].b;
                bytes[index * 4 + 3] = colors[index].a;
            }

            GCHandle handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                image.LoadRawTextureData(handle.AddrOfPinnedObject(), bytes.Length);
            }
            finally
            {
                handle.Free();
            }
        }

        private void ReleaseMaterial()
        {
            if (_material)
            {
                Object.Destroy(_material);
            }

            foreach (Texture2D face in _faces)
            {
                if (face)
                {
                    Object.Destroy(face);
                }
            }

            _faces.Clear();
            _material = null;
            _source = null;
        }

        public void Dispose()
        {
            ReleaseMaterial();
            _cancellation?.Cancel();
            _cancellation?.Dispose();
            _cancellation = null;
            _download = null;

            if (_texture)
            {
                Object.Destroy(_texture);
            }

            _texture = null;
            _url = null;
        }
    }
}
