namespace S3_NullServers.Room
{
    internal sealed class RoomPermissions
    {
        private readonly Dictionary<int, string> _accounts = new();

        internal IEnumerable<int> Clients => _accounts.Keys;

        internal bool Grant(int clientId, string account)
        {
            if (clientId <= 0 || string.IsNullOrWhiteSpace(account))
            {
                return false;
            }

            _accounts[clientId] = account;
            return true;
        }

        internal bool Allows(int clientId, string account)
        {
            return account != null &&
                _accounts.TryGetValue(clientId, out var expected) &&
                string.Equals(account, expected, StringComparison.Ordinal);
        }

        internal void Revoke(int clientId) => _accounts.Remove(clientId);

        internal void Clear() => _accounts.Clear();
    }
}
