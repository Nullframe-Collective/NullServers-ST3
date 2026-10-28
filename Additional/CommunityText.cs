using System.Net;
using System.Net.Http;
using System.Text;

namespace NullServers.Additional
{
    internal static class CommunityText
    {
        internal const string BlacklistUrl = "https://raw.githubusercontent.com/Nullframe-Collective/Nullframe-Blacklist/refs/heads/main/Blacklist.txt";
        internal const string UpdatesUrl = "https://pastebin.com/raw/Ph2u8AG8";
        private static readonly HttpClient Client = new(new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            UseProxy = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        })
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        internal static async Task<string> Download(string url, CancellationToken cancellation)
        {
            if (url != BlacklistUrl && url != UpdatesUrl)
            {
                throw new ArgumentException("Unknown community document.", nameof(url));
            }

            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            using HttpRequestMessage request = new(HttpMethod.Get, url);
            request.Headers.CacheControl = new() { NoCache = true };
            using HttpResponseMessage response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            return await Read(response, url == BlacklistUrl ? 262144 : 65536, timeout.Token).ConfigureAwait(false);
        }

        internal static async Task<string> Read(HttpResponseMessage response, int limit, CancellationToken cancellation)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException("Community document returned HTTP " + (int)response.StatusCode + ".");
            }

            if (!string.Equals(response.Content.Headers.ContentType?.MediaType, "text/plain", StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("The community document must be plain text.");
            }

            if (response.Content.Headers.ContentLength > limit)
            {
                throw new IOException("The community document is too large.");
            }

            using Stream source = await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
            using MemoryStream output = new();
            byte[] buffer = new byte[4096];
            int count;

            while ((count = await source.ReadAsync(buffer.AsMemory(), cancellation).ConfigureAwait(false)) > 0)
            {
                if (output.Length + count > limit)
                {
                    throw new IOException("The community document is too large.");
                }

                output.Write(buffer, 0, count);
            }

            string text = new UTF8Encoding(false, true).GetString(output.ToArray());
            if (text.Any(character => char.IsControl(character) && character is not '\n' and not '\r' and not '\t'))
            {
                throw new IOException("The community document contains invalid text.");
            }

            return CommunityDocument.Normalize(text);
        }
    }
}
