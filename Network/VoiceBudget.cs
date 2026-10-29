namespace NullServers.Network
{
    internal sealed class VoiceBudget
    {
        private float _tokens = 10f;
        private float _at;
        private float _lastFrame = -10f;
        private uint _sequence;

        internal bool Accept(uint sequence, float now)
        {
            _tokens = Math.Min(10f, _tokens + Math.Max(0f, now - _at) * 55f);
            _at = now;
            if (_tokens < 1f || now - _lastFrame < 2f && unchecked((int)(sequence - _sequence)) <= 0) return false;

            _tokens -= 1f;
            _sequence = sequence;
            _lastFrame = now;
            return true;
        }
    }
}
