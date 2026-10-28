namespace NullServers.Network
{
    internal enum CarryTarget
    {
        Prop,
        Ragdoll,
        Player,
        NPC
    }

    internal sealed record CarryState(
        int Player,
        bool Equipped,
        int Holding = 0,
        Point3 Position = null,
        Rotation4 Rotation = null,
        bool Throw = false,
        bool Freeze = false,
        int Sequence = 0,
        Point3 Grip = null,
        CarryTarget Target = CarryTarget.Prop,
        int Bone = 0,
        bool Rotate = false,
        bool Building = false)
    {
        internal bool Valid => Player is >= 0 and <= 4095 && Holding is >= 0 and < 2000000 &&
            Enum.IsDefined(typeof(CarryTarget), Target) && Bone is >= 0 and < 64 && Sequence >= 0 &&
            (Position == null || Position.Valid()) && (Grip == null || Grip.Valid(1000f)) &&
            (Rotation == null || Rotation.Valid);
    }

    internal sealed record LivingState(
        int Body,
        int Player = -1,
        int NPC = -1,
        bool Active = true,
        int Holder = -1,
        bool Locked = false,
        int Escapes = 0,
        Point3 Position = null)
    {
        internal bool Valid => Body is >= 1000000 and < 2000000 && Player is >= -1 and <= 4095 &&
            NPC is >= -1 and < 65536 && (Player >= 0 ^ NPC >= 0) && Holder is >= -1 and <= 4095 &&
            Escapes is >= 0 and <= 15 && (Position == null || Position.Valid());
    }

    internal static class CarryRules
    {
        internal static bool Allowed(bool console, bool administrator, bool throwAnyone, CarryTarget target) =>
            administrator || console || throwAnyone && target != CarryTarget.Prop;

        internal const int EscapeTaps = 15;
        internal const float Reach = 3.5f;
        internal const float RecoverDelay = 1f;
        internal static bool CanEscape(bool held, bool locked, int taps) => held && !locked && taps >= EscapeTaps;
        internal static bool CanTakeLiving(bool enabled, int holder, int victim) => enabled && holder >= 0 && holder != victim;
    }

    internal sealed class LandingRecovery
    {
        private double _groundedAt = double.NaN;

        internal bool Update(double now, bool grounded)
        {
            if (!double.IsFinite(now) || !grounded)
            {
                Reset();
                return false;
            }

            if (double.IsNaN(_groundedAt) || now < _groundedAt)
            {
                _groundedAt = now;
            }

            return now - _groundedAt >= CarryRules.RecoverDelay;
        }

        internal void Reset() => _groundedAt = double.NaN;
    }

    internal sealed class EscapeCounter
    {
        private double _last = double.NegativeInfinity;
        internal int Count { get; private set; }
        internal int Press(double now, bool locked)
        {
            if (locked || !double.IsFinite(now) || now - _last < 0.1d)
            {
                return Count;
            }

            if (now - _last > 2d)
            {
                Count = 0;
            }

            _last = now;
            Count = Math.Min(CarryRules.EscapeTaps, Count + 1);
            return Count;
        }
    }
}
