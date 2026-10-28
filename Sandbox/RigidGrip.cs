using UnityEngine;

namespace NullServers.Sandbox
{
    internal sealed class RigidGrip : IDisposable
    {
        private readonly Rigidbody _body;
        private readonly bool _kinematic;
        private readonly RigidbodyInterpolation _interpolation;
        private readonly List<(Collider First, Collider Second)> _collisions = new();

        internal RigidGrip(Rigidbody body, GameObject root, IEnumerable<Collider> holder)
        {
            _body = body;
            _kinematic = body.isKinematic;
            _interpolation = body.interpolation;

            if (!body.isKinematic)
            {
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }

            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.None;

            foreach (Collider first in root.GetComponentsInChildren<Collider>(true))
            {
                foreach (Collider second in holder)
                {
                    if (!first || !second || first == second || Physics.GetIgnoreCollision(first, second))
                    {
                        continue;
                    }

                    Physics.IgnoreCollision(first, second, true);
                    _collisions.Add((first, second));
                }
            }
        }

        internal static Vector3 Position(Rigidbody body, Vector3 localPoint, Vector3 target, Quaternion rotation)
        {
            Vector3 offset = body.transform.TransformVector(localPoint);
            return target - rotation * Quaternion.Inverse(body.rotation) * offset;
        }

        internal void Move(Vector3 localPoint, Vector3 target, Quaternion rotation)
        {
            if (!_body)
            {
                return;
            }

            Vector3 position = Position(_body, localPoint, target, rotation);
            _body.position = position;
            _body.rotation = rotation;
        }

        public void Dispose()
        {
            if (_body)
            {
                _body.isKinematic = _kinematic;
                _body.interpolation = _interpolation;

                if (!_body.isKinematic)
                {
                    _body.WakeUp();
                }
            }

            foreach (var pair in _collisions)
            {
                if (pair.First && pair.Second)
                {
                    Physics.IgnoreCollision(pair.First, pair.Second, false);
                }
            }

            _collisions.Clear();
        }
    }
}
