using NullServers.Additional;
using UnityEngine;

namespace NullServers.Customization
{
    internal sealed class SkinTextures : IDisposable
    {
        private const int MaxDownloads = 4;
        private const long CacheLimit = 128L * 1024 * 1024;

        private sealed class Download : IDisposable
        {
            internal CancellationTokenSource Cancellation;
            internal Task<byte[]> Task;
            internal Texture2D Texture;
            internal string Error;
            internal float UsedAt;
            internal bool Finished;
            internal int Bytes;

            internal void Finish()
            {
                Cancellation?.Dispose();
                Cancellation = null;
                Task = null;
                Finished = true;
            }

            public void Dispose()
            {
                Cancellation?.Cancel();
                Finish();

                if (Texture)
                {
                    UnityEngine.Object.Destroy(Texture);
                }
            }
        }

        private readonly Dictionary<string, Download> _downloads = new(StringComparer.Ordinal);
        private long _textureBytes;
        private readonly bool _readable;

        internal SkinTextures(bool readable = false)
        {
            _readable = readable;
        }

        internal string ErrorFor(IEnumerable<string> urls) => urls.Select(url => _downloads.GetValueOrDefault(url)?.Error).FirstOrDefault(error => error != null);

        internal Texture2D Get(string url)
        {
            if (string.IsNullOrEmpty(url) || !Appearance.ValidUrl(url)) return null;

            if (!_downloads.TryGetValue(url, out var download))
            {
                _downloads[url] = download = new Download();
            }

            download.UsedAt = Time.unscaledTime;
            return download.Texture;
        }

        internal Texture2D[] Get(Appearance appearance) => appearance.Urls.Select(url => appearance.UseSkin ? Get(url) : null).ToArray();

        internal void Update()
        {
            int active = _downloads.Values.Count(download => download.Task != null);

            foreach (var pair in _downloads.ToArray())
            {
                Download download = pair.Value;
                if (Time.unscaledTime - download.UsedAt > 60f)
                {
                    download.Dispose();
                    _textureBytes -= download.Bytes;
                    _downloads.Remove(pair.Key);
                    continue;
                }

                if (download.Finished) continue;

                if (download.Task == null)
                {
                    if (active >= MaxDownloads) continue;

                    download.Cancellation = new CancellationTokenSource();
                    download.Task = PublicImage.Download(pair.Key, download.Cancellation.Token);
                    active++;
                }

                if (!download.Task.IsCompleted) continue;

                try
                {
                    Decode(download, download.Task.GetAwaiter().GetResult());
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    download.Error = "Cannot load skin image: " + exception.GetBaseException().Message;
                }

                download.Finish();
                active--;
            }
        }

        private void Decode(Download download, byte[] data)
        {
            Texture2D texture = new(2, 2, TextureFormat.RGBA32, false);
            bool decoded = ImageConversion.LoadImage(texture, data, !_readable);
            long bytes = decoded ? texture.width * texture.height * 4L : 0;

            if (decoded && texture.width <= 2048 && texture.height <= 2048 && _textureBytes + bytes <= CacheLimit)
            {
                download.Texture = texture;
                download.Bytes = (int)bytes;
                _textureBytes += bytes;
                return;
            }

            UnityEngine.Object.Destroy(texture);
            download.Error = decoded ? "Image size or the 128 MB texture cache limit was exceeded." : "The skin URL did not return a supported image.";
        }

        public void Dispose()
        {
            foreach (Download download in _downloads.Values)
            {
                download.Dispose();
            }

            _downloads.Clear();
            _textureBytes = 0;
        }
    }
}
