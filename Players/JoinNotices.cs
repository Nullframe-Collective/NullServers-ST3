using Il2Cpp;
using Il2CppFishNet.Transporting.Relay;
using NullServers.Network;
using System.Text.RegularExpressions;

namespace NullServers.Players
{
    public sealed class HeldJoinNotice
    {
        public WhoKilledWho Feed;
        public string PlayerName;
        public string Weapon;
        public string Victim;
        public string RoomId;
    }

    internal sealed class JoinNotices
    {
        public const string ConnectedText = "connected";
        public const string LeftText = "left";
        private static readonly Regex RichTextTags = new("<[^>]*>", RegexOptions.CultureInvariant);

        public static JoinNotices Instance { get; private set; }

        private readonly RelaySession _session;
        private readonly FrameworkCompatibility _compatibility;
        private readonly HashSet<string> _announcedPlayers = new(StringComparer.OrdinalIgnoreCase);
        private HeldJoinNotice _heldNotice;
        private string _roomId;
        private bool _isResending;

        public JoinNotices(RelaySession session, FrameworkCompatibility compatibility)
        {
            Instance = this;
            _session = session;
            _compatibility = compatibility;
        }

        public string CurrentRoomId
        {
            get
            {
                if (!_session.IsNullFrame || _session.IsSwitching) return null;

                RelayTransport relay = _session.Relay;
                if (relay == null || relay.OfflineMode) return null;

                return relay.CurrentRoom?.Id;
            }
        }

        public void Update()
        {
            string roomId = CurrentRoomId;
            if (roomId != _roomId)
            {
                _roomId = roomId;
                _announcedPlayers.Clear();
                _heldNotice = null;
            }

            if (_heldNotice == null) return;

            if (_heldNotice.Feed == null || _heldNotice.RoomId != roomId)
            {
                _heldNotice = null;
                return;
            }

            if (!_compatibility.Ready) return;

            HeldJoinNotice notice = _heldNotice;
            _heldNotice = null;
            _isResending = true;

            try
            {
                notice.Feed.FKAKKHPGFJP(notice.PlayerName, notice.Weapon, ConnectedText, notice.Victim);
            }
            finally
            {
                _isResending = false;
            }
        }

        // A client keeps its own "connected" notice until the host has confirmed its content.
        public bool ShouldSendToServer(WhoKilledWho feed, string playerName, string weapon, string action, string victim)
        {
            string roomId = CurrentRoomId;
            if (_isResending || action != ConnectedText || roomId == null || _compatibility.Ready) return true;

            _heldNotice = new HeldJoinNotice
            {
                Feed = feed,
                PlayerName = playerName,
                Weapon = weapon,
                Victim = victim,
                RoomId = roomId
            };

            return false;
        }

        // The host hides "left" for a player who disconnected before they were ready.
        public bool ShouldBroadcast(string playerName, string action)
        {
            if (CurrentRoomId == null || action is not (ConnectedText or LeftText)) return true;

            string name = RichTextTags.Replace(playerName ?? "", "").Trim();
            if (action == ConnectedText)
            {
                _announcedPlayers.Add(name);
                return true;
            }

            if (_announcedPlayers.Remove(name)) return true;

            return !_compatibility.QuietDeparture();
        }
    }
}
