using NullServers.Additional;
using System.Text;

namespace NullServers.Server
{
    internal sealed class GlobalBlacklist : IDisposable
    {
        internal const string BannedMessage = "You have been banned from NullFrame Collective servers.";
        internal const string UnavailableMessage = "The NullFrame blacklist could not be checked. Please try again in a moment.";
        private readonly Func<CancellationToken, Task<string>> _download;
        private readonly CancellationTokenSource _cancellation = new();
        private Task<HashSet<string>> _pending;
        private HashSet<string> _accounts;
        private DateTimeOffset _nextRefresh;

        internal bool Ready => _accounts != null;
        internal bool Updating => _pending != null;
        internal string Error { get; private set; }

        internal GlobalBlacklist(Func<CancellationToken, Task<string>> download = null)
        {
            _download = download ?? (cancellation => CommunityText.Download(CommunityText.BlacklistUrl, cancellation));
        }

        internal static HashSet<string> Parse(string text)
        {
            if (text == null || Encoding.UTF8.GetByteCount(text) > 262144)
            {
                throw new FormatException("Invalid community blacklist size.");
            }

            HashSet<string> accounts = new(StringComparer.OrdinalIgnoreCase);
            foreach (string line in CommunityDocument.Normalize(text).Split('\n'))
            {
                string account = line.Trim();
                if (account.Length == 0 || account.StartsWith('#') || account.StartsWith("//", StringComparison.Ordinal))
                {
                    continue;
                }

                if (account.Length > 256 || account.Any(char.IsControl) || account[0] is '<' or '{' || account.StartsWith("[\"", StringComparison.Ordinal))
                {
                    throw new FormatException("The community blacklist must contain one account per line.");
                }

                accounts.Add(account);
                if (accounts.Count > 10000)
                {
                    throw new FormatException("The community blacklist contains too many accounts.");
                }
            }

            return accounts;
        }

        internal bool Contains(string account) => !string.IsNullOrWhiteSpace(account) && _accounts?.Contains(account.Trim()) == true;

        internal void Refresh()
        {
            if (_pending == null && !_cancellation.IsCancellationRequested)
            {
                _pending = Fetch();
            }
        }

        private async Task<HashSet<string>> Fetch() => Parse(await _download(_cancellation.Token).ConfigureAwait(false));

        internal void Update()
        {
            if (_pending?.IsCompleted == true)
            {
                try
                {
                    _accounts = _pending.GetAwaiter().GetResult();
                    Error = null;
                    _nextRefresh = DateTimeOffset.UtcNow.AddMinutes(5);
                }
                catch (Exception exception) when (exception is HttpRequestException or IOException or OperationCanceledException or FormatException or DecoderFallbackException)
                {
                    Error = exception.Message;
                    _nextRefresh = DateTimeOffset.UtcNow.AddSeconds(30);
                }

                _pending = null;
            }

            if (_pending == null && DateTimeOffset.UtcNow >= _nextRefresh)
            {
                Refresh();
            }
        }

        public void Dispose()
        {
            _cancellation.Cancel();
            _cancellation.Dispose();
        }
    }
}
