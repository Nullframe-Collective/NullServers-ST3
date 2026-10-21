using S3_NullServers.Additional;
using UnityEngine;
using UnityEngine.Networking;

namespace S3_NullServers.Customization
{
    internal sealed class SkinTextures : IDisposable
    {
        private sealed class Download
        {
            internal UnityWebRequest Request;
            internal Texture2D Texture;
            internal string Error;
            internal float UsedAt;
            internal bool Finished;
            internal int Bytes;
        }

        private readonly Dictionary<string, Download> _downloads = new(StringComparer.Ordinal);
        private int _textureBytes;

        internal string ErrorFor(IEnumerable<string> urls) => urls.Select(url => _downloads.GetValueOrDefault(url)?.Error).FirstOrDefault(error => error != null);

        internal Texture2D Get(string url)
        {
            if (!Appearance.ValidUrl(url) || string.IsNullOrEmpty(url))
            {
                return null;
            }

            if (!_downloads.TryGetValue(url, out var download))
            {
                _downloads[url] = download = new Download();
            }

            download.UsedAt = Time.unscaledTime;
            return download.Texture;
        }

        internal void Update()
        {
            int active = _downloads.Values.Count(download => download.Request != null);
            foreach (var pair in _downloads.ToArray())
            {
                Download download = pair.Value;
                if (Time.unscaledTime - download.UsedAt > 60f)
                {
                    download.Request?.Abort();
                    download.Request?.Dispose();
                    if (download.Texture)
                    {
                        UnityEngine.Object.Destroy(download.Texture);
                    }

                    _textureBytes -= download.Bytes;
                    _downloads.Remove(pair.Key);
                    continue;
                }

                if (download.Finished)
                {
                    continue;
                }

                if (download.Request == null)
                {
                    if (active >= 4)
                    {
                        continue;
                    }

                    try
                    {
                        download.Request = UnityWebRequest.Get(pair.Key);
                        download.Request.timeout = 20;
                        download.Request.redirectLimit = 3;
                        download.Request.SendWebRequest();
                    }
                    catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NotSupportedException)
                    {
                        download.Request?.Dispose();
                        download.Request = null;
                        download.Error = "Cannot load skin image: " + exception.Message;
                        download.Finished = true;
                        continue;
                    }

                    active++;
                }

                UnityWebRequest request = download.Request;
                if (request.downloadedBytes > 8 * 1024 * 1024)
                {
                    request.Abort();
                    download.Error = "Skin image exceeds 8 MB.";
                }

                if (!request.isDone)
                {
                    continue;
                }

                if (download.Error == null && request.result == UnityWebRequest.Result.Success)
                {
                    var data = request.downloadHandler.data;
                    if (!ImageDimensions.Allowed(data.ToArray()))
                    {
                        download.Error = "Use a PNG or JPEG image up to 2048 × 2048.";
                        request.Dispose();
                        download.Request = null;
                        download.Finished = true;
                        active--;
                        continue;
                    }

                    Texture2D texture = new(2, 2, TextureFormat.RGBA32, false);
                    bool decoded = ImageConversion.LoadImage(texture, data, true);
                    long bytes = decoded ? texture.width * texture.height * 4L : 0;
                    if (decoded && texture.width <= 2048 && texture.height <= 2048 && _textureBytes + bytes <= 128L * 1024 * 1024)
                    {
                        download.Texture = texture;
                        download.Bytes = (int)bytes;
                        _textureBytes += download.Bytes;
                    }
                    else
                    {
                        if (texture)
                        {
                            UnityEngine.Object.Destroy(texture);
                        }

                        download.Error = decoded ? "Image size or the 128 MB texture cache limit was exceeded." : "The skin URL did not return a supported image.";
                    }
                }
                else
                {
                    download.Error ??= "Cannot load skin image: " + request.error;
                }

                request.Dispose();
                download.Request = null;
                download.Finished = true;
                active--;
            }
        }

        public void Dispose()
        {
            foreach (Download download in _downloads.Values)
            {
                download.Request?.Abort();
                download.Request?.Dispose();
                if (download.Texture)
                {
                    UnityEngine.Object.Destroy(download.Texture);
                }
            }

            _downloads.Clear();
            _textureBytes = 0;
        }
    }
}
