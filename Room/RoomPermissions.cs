namespace NullServers.Room
{
    internal sealed class RoomPermissions
    {
        private readonly Dictionary<int, string> _accountsByClient = new();

        public IEnumerable<int> Clients => _accountsByClient.Keys;

        public bool Grant(int clientId, string account)
        {
            if (clientId <= 0 || string.IsNullOrWhiteSpace(account)) return false;

            _accountsByClient[clientId] = account;

            return true;
        }

        public bool Allows(int clientId, string account)
        {
            if (account == null) return false;
            if (!_accountsByClient.TryGetValue(clientId, out string grantedAccount)) return false;

            return string.Equals(account, grantedAccount, StringComparison.Ordinal);
        }

        public bool Contains(string account)
        {
            if (account == null) return false;

            return _accountsByClient.ContainsValue(account);
        }

        public void Revoke(int clientId)
        {
            _accountsByClient.Remove(clientId);
        }

        public void Clear()
        {
            _accountsByClient.Clear();
        }
    }
}
