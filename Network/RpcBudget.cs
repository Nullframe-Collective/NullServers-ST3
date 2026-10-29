namespace NullServers.Network
{
    internal enum RpcCategory
    {
        General,
        Chat,
        Appearance,
        Spawn,
        Notification,
        Broadcast,
        Voice
    }

    internal sealed class RpcBudget
    {
        private readonly TrafficBudget _calls;
        private readonly TrafficBudget _bytes;
        private readonly TrafficBudget _actors;
        private readonly TrafficBudget _consoleActors;
        private readonly TrafficBudget _objects;
        private readonly Dictionary<(IntPtr Type, uint Method), TrafficBudget> _methods = new();
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
            _calls = new TrafficBudget(600, 1200, now);
            _bytes = new TrafficBudget(256 * 1024, 512 * 1024, now);
            _actors = new TrafficBudget(1, 3, now);
            _consoleActors = new TrafficBudget(12, 32, now);
            _objects = new TrafficBudget(40, 80, now);
            _malformedAt = now;
        }

        internal bool AllowPacket(double now, int bytes) => bytes >= 0 && _calls.Spend(1, now) && _bytes.Spend(bytes, now);

        internal bool AllowObject(double now, bool actor, bool console = false)
        {
            return (!actor || (console ? _consoleActors : _actors).Spend(1, now)) && _objects.Spend(1, now);
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
                    RpcCategory.Voice => (60, 120),
                    _ => (200, 400)
                };

                _methods[key] = bucket = new TrafficBudget(rate, burst, now);
            }

            return bucket.Spend(1, now);
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

            if (++_burstDrops >= 64) return true;

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
