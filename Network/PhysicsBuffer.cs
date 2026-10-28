using System.Numerics;

namespace NullServers.Network
{
    internal static class PhysicsLimits
    {
        internal const int PeerRate = 32768;
        internal const int PeerBurst = 4096;
        internal const int TotalRate = 262144;
        internal const int TotalBurst = 32768;
    }

    internal readonly record struct BufferedPose(Vector3 Position, Quaternion Rotation);

    internal sealed class PhysicsClock
    {
        private bool _ready;
        private double _offset, _sent, _arrived, _jitter;
        private double _render = double.NegativeInfinity, _renderedAt;
        internal double Delay => Math.Clamp(0.1d + _jitter * 2d, 0.1d, 0.25d);

        internal double Observe(double sent, double arrived)
        {
            if (sent <= 0d)
            {
                sent = arrived;
            }

            if (!_ready)
            {
                _ready = true;
                _offset = arrived - sent;
            }
            else if (sent > _sent)
            {
                _jitter += (Math.Abs((arrived - _arrived) - (sent - _sent)) - _jitter) * 0.1d;
                _offset = Math.Min(_offset, arrived - sent);
            }

            if (sent >= _sent)
            {
                _sent = sent;
                _arrived = arrived;
            }

            return sent;
        }

        internal double RenderTime(double now)
        {
            if (!_ready)
            {
                return now - Delay;
            }

            double desired = now - _offset - Delay;
            _render = double.IsNegativeInfinity(_render) ? desired :
                Math.Clamp(desired, _render, _render + Math.Max(0d, now - _renderedAt) * 1.15d);
            _renderedAt = now;
            return _render;
        }
    }

    internal sealed class PhysicsBuffer
    {
        private readonly record struct Frame(double Time, BufferedPose Pose);
        private readonly List<Frame> _frames = new(8);
        private int _sequence;
        internal int Count => _frames.Count;

        internal bool Add(double time, int sequence, Point3 position, Rotation4 rotation)
        {
            if (!double.IsFinite(time) || sequence <= _sequence || position?.Valid() != true || rotation?.Valid != true)
            {
                return false;
            }

            BufferedPose pose = new(new Vector3(position.X, position.Y, position.Z),
                Quaternion.Normalize(new Quaternion(rotation.X, rotation.Y, rotation.Z, rotation.W)));
            if (_frames.Count > 0)
            {
                Frame previous = _frames[^1];
                if (time <= previous.Time)
                {
                    return false;
                }

                if (time - previous.Time > 1d || Vector3.DistanceSquared(previous.Pose.Position, pose.Position) > 64f)
                {
                    _frames.Clear();
                }
            }

            _sequence = sequence;
            if (_frames.Count == 8)
            {
                _frames.RemoveAt(0);
            }

            _frames.Add(new Frame(time, pose));
            return true;
        }

        internal bool Sample(double time, out BufferedPose pose)
        {
            pose = default;
            if (_frames.Count == 0 || !double.IsFinite(time))
            {
                return false;
            }

            Frame first = _frames[0];
            if (time <= first.Time || _frames.Count == 1)
            {
                pose = first.Pose;
                return true;
            }

            for (int index = 1; index < _frames.Count; index++)
            {
                Frame next = _frames[index];
                if (time > next.Time)
                {
                    continue;
                }

                Frame previous = _frames[index - 1];
                float amount = (float)((time - previous.Time) / (next.Time - previous.Time));
                pose = new BufferedPose(Vector3.Lerp(previous.Pose.Position, next.Pose.Position, amount),
                    Quaternion.Normalize(Quaternion.Slerp(previous.Pose.Rotation, next.Pose.Rotation, amount)));
                return true;
            }

            Frame last = _frames[^1];
            Frame before = _frames[^2];
            Vector3 velocity = (last.Pose.Position - before.Pose.Position) / (float)(last.Time - before.Time);

            if (velocity.LengthSquared() > 2500f)
            {
                velocity = Vector3.Normalize(velocity) * 50f;
            }

            pose = last.Pose with { Position = last.Pose.Position + velocity * (float)Math.Min(0.08d, time - last.Time) };
            return true;
        }

        internal void Clear()
        {
            _frames.Clear();
            _sequence = 0;
        }
    }
}
