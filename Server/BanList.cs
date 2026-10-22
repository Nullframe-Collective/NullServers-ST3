using System.Text.Json;

namespace NullServers.Server
{
    internal sealed class BanEntry
    {
        public string Account { get; set; }
        public string Nickname { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
    }

    internal sealed class BanList
    {
        private readonly string _path;
        private readonly string _label;
        private readonly List<BanEntry> _entries = new();

        internal IReadOnlyList<BanEntry> Entries => _entries;

        internal string LoadError { get; private set; }

        internal BanList(string path, string label = "ban list")
        {
            _path = path;
            _label = label;

            try
            {
                if (!File.Exists(path)) return;

                var entries = JsonSerializer.Deserialize<List<BanEntry>>(File.ReadAllText(path));
                if (entries == null || entries.Any(entry => entry == null || string.IsNullOrWhiteSpace(entry.Account)))
                {
                    throw new JsonException($"Invalid {label}.");
                }

                _entries.AddRange(entries.DistinctBy(entry => entry.Account, StringComparer.OrdinalIgnoreCase));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                LoadError = $"Cannot read the {label}: " + exception.Message;
            }
        }

        internal bool Contains(string account)
        {
            return _entries.Any(entry =>
            string.Equals(entry.Account, account, StringComparison.OrdinalIgnoreCase));
        }

        internal bool Add(string account, string nickname, out string error)
        {
            if (string.IsNullOrWhiteSpace(account))
            {
                error = "The authenticated account is unavailable.";

                return false;
            }

            if (Contains(account))
            {
                error = null;

                return true;
            }

            BanEntry entry = new()

            {
                Account = account,
                Nickname = nickname,
                CreatedAt = DateTimeOffset.UtcNow
            };
            var next = _entries.Append(entry).ToList();

            return Save(next, out error);
        }

        internal bool Remove(string account, out string error)
        {
            return Save
            (
                _entries.Where(entry =>
                !string.Equals(entry.Account, account, StringComparison.OrdinalIgnoreCase)).ToList(),
                out error
            );
        }

        private bool Save(List<BanEntry> entries, out string error)
        {
            error = LoadError;
            if (error != null) return false;

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path));
                File.WriteAllText(_path + ".tmp", JsonSerializer.Serialize(entries));
                File.Move(_path + ".tmp", _path, overwrite: true);

                _entries.Clear();
                _entries.AddRange(entries);

                return true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                error = $"Cannot save the {_label}: " + exception.Message;

                return false;
            }
        }
    }
}
