using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace NullServers.Additional
{
    internal static class PublicImage
    {
        private const int Limit = 8 * 1024 * 1024;
        private const int MaxRedirects = 3;
        private static readonly HttpClient Client = new(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            UseProxy = false,
            MaxConnectionsPerServer = 2,
            ConnectCallback = Connect
        })
        {
            Timeout = TimeSpan.FromSeconds(20)
        };

        internal static bool Allowed(Uri uri) => uri != null && uri.IsAbsoluteUri &&
            uri.Scheme is "http" or "https" && uri.IsDefaultPort && uri.UserInfo.Length == 0 && !uri.IsLoopback;

        internal static bool IsPublic(IPAddress address)
        {
            if (address.IsIPv4MappedToIPv6)
            {
                address = address.MapToIPv4();
            }

            if (IPAddress.IsLoopback(address))
            {
                return false;
            }

            byte[] bytes = address.GetAddressBytes();
            if (address.AddressFamily == AddressFamily.InterNetworkV6)
            {
                return (bytes[0] & 0xE0) == 0x20 && !(bytes[0] == 0x20 && bytes[1] == 2) &&
                                !(bytes[0] == 0x20 && bytes[1] == 1 && (bytes[2] < 2 || bytes[2] == 0x0D && bytes[3] == 0xB8));
            }

            if (address.AddressFamily != AddressFamily.InterNetwork)
            {
                return false;
            }

            return bytes[0] is not (0 or 10 or 127) && bytes[0] < 224 &&
                            !(bytes[0] == 100 && bytes[1] is >= 64 and <= 127) &&
                            !(bytes[0] == 169 && bytes[1] == 254) &&
                            !(bytes[0] == 172 && bytes[1] is >= 16 and <= 31) &&
                            !(bytes[0] == 192 && bytes[1] == 168) &&
                            !(bytes[0] == 192 && bytes[1] == 0 && bytes[2] is 0 or 2) &&
                            !(bytes[0] == 198 && bytes[1] is 18 or 19) &&
                            !(bytes[0] == 198 && bytes[1] == 51 && bytes[2] == 100) &&
                            !(bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113);
        }

        private static async ValueTask<Stream> Connect(SocketsHttpConnectionContext context, CancellationToken cancellation)
        {
            IPAddress[] addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host).WaitAsync(cancellation).ConfigureAwait(false);
            foreach (IPAddress address in addresses.Where(IsPublic))
            {
                Socket socket = new(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), cancellation).ConfigureAwait(false);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch (SocketException)
                {
                    socket.Dispose();
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            }

            throw new HttpRequestException("The image must be hosted on a reachable public web server.");
        }

        internal static async Task<byte[]> Download(string url, CancellationToken cancellation)
        {
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            cancellation = timeout.Token;

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                throw new HttpRequestException("Use a direct public HTTP or HTTPS image link on its standard port.");
            }

            using HttpResponseMessage response = await Get(uri, cancellation).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException("Image server returned HTTP " + (int)response.StatusCode + ".");
            }

            if (response.Content.Headers.ContentLength > Limit)
            {
                throw new HttpRequestException("Image exceeds 8 MB.");
            }

            using Stream source = await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
            using MemoryStream output = new();
            byte[] buffer = new byte[8192];
            int count;

            while ((count = await source.ReadAsync(buffer.AsMemory(), cancellation).ConfigureAwait(false)) > 0)
            {
                if (output.Length + count > Limit)
                {
                    throw new HttpRequestException("Image exceeds 8 MB.");
                }

                output.Write(buffer, 0, count);
            }

            byte[] image = output.ToArray();
            if (!ImageDimensions.Allowed(image))
            {
                throw new HttpRequestException("Use a PNG or JPEG image up to 2048 × 2048.");
            }

            return image;
        }

        private static async Task<HttpResponseMessage> Get(Uri uri, CancellationToken cancellation)
        {
            for (int hop = 0; ; hop++)
            {
                if (!Allowed(uri))
                {
                    throw new HttpRequestException("Use a direct public HTTP or HTTPS image link on its standard port.");
                }

                HttpResponseMessage response = await Client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
                int status = (int)response.StatusCode;

                if (status is < 300 or >= 400 || response.Headers.Location is not { } location)
                {
                    return response;
                }

                response.Dispose();
                if (hop >= MaxRedirects)
                {
                    throw new HttpRequestException("The image link redirects too many times.");
                }

                Uri next = location.IsAbsoluteUri ? location : new Uri(uri, location);
                if (uri.Scheme == Uri.UriSchemeHttps && next.Scheme != Uri.UriSchemeHttps)
                {
                    throw new HttpRequestException("The image link redirects from HTTPS to HTTP.");
                }

                uri = next;
            }
        }
    }
}
