namespace S3_NullServers.Network
{
    internal enum RpcCategory
    {
        General,
        Chat,
        Appearance,
        Spawn,
        Notification,
        Broadcast
    }

    internal sealed class RpcBudget
    {
        private sealed class Bucket
        {
            private readonly double _rate;
            private readonly double _capacity;
            private double _tokens;
            private double _updated;

            internal Bucket(double rate, double capacity, double now)
            {
                _rate = rate;
                _capacity = _tokens = capacity;
                _updated = now;
            }

            internal bool Spend(double now, int amount)
            {
                _tokens = Math.Min(_capacity, _tokens + Math.Max(0, now - _updated) * _rate);
                _updated = Math.Max(now, _updated);
                if (amount > _tokens)
                {
                    return false;
                }

                _tokens -= amount;
                return true;
            }
        }

        private readonly Bucket _calls;
        private readonly Bucket _bytes;
        private readonly Bucket _actors;
        private readonly Bucket _consoleActors;
        private readonly Bucket _objects;
        private readonly Dictionary<(IntPtr Type, uint Method), Bucket> _methods = new();
        private double _malformedAt;
        private int _malformed;
        private double _firstDrop;
        private long _dropSecond = -1;
        private int _drops;
        private int _floodSeconds;
        private double _burstAt;
        private int _burstDrops;

        internal RpcBudget(double now)
        {
            _calls = new Bucket(600, 1200, now);
            _bytes = new Bucket(256 * 1024, 512 * 1024, now);
            _actors = new Bucket(1, 3, now);
            _consoleActors = new Bucket(12, 32, now);
            _objects = new Bucket(40, 80, now);
            _malformedAt = now;
        }

        internal bool AllowPacket(double now, int bytes) => bytes >= 0 && _calls.Spend(now, 1) && _bytes.Spend(now, bytes);

        internal bool AllowObject(double now, bool actor, bool console = false)
        {
            return (!actor || (console ? _consoleActors : _actors).Spend(now, 1)) && _objects.Spend(now, 1);
        }

        internal bool AllowMethod(double now, IntPtr type, uint method, RpcCategory category)
        {
            var key = (type, method);
            if (!_methods.TryGetValue(key, out var bucket))
            {
                var (rate, burst) = category switch

                {
                    RpcCategory.Chat => (3, 10),
                    RpcCategory.Appearance => (5, 12),
                    RpcCategory.Spawn => (12, 32),
                    RpcCategory.Notification => (8, 20),
                    RpcCategory.Broadcast => (20, 50),
                    _ => (200, 400)
                };
                _methods[key] = bucket = new Bucket(rate, burst, now);
            }

            return bucket.Spend(now, 1);
        }

        internal bool Malformed(double now)
        {
            if (now - _malformedAt > 10)
            {
                _malformedAt = now;
                _malformed = 0;
            }

            return ++_malformed >= 8;
        }

        internal bool Flood(double now)
        {
            if (now - _burstAt > 1)
            {
                _burstAt = now;
                _burstDrops = 0;
            }

            if (++_burstDrops >= 64)
            {
                return true;
            }

            long second = (long)Math.Floor(now);
            if (second != _dropSecond)
            {
                if (second == _dropSecond + 1 && _drops >= 10)
                {
                    _floodSeconds++;
                }
                else
                {
                    _floodSeconds = 0;
                    _firstDrop = now;
                }

                _dropSecond = second;
                _drops = 0;
            }

            return ++_drops >= 10 && _floodSeconds >= 2 && now - _firstDrop >= 2;
        }
    }
}
