using UnityEngine;
using Object = UnityEngine.Object;

namespace NullServers.Room
{
    internal sealed class WorldBatch
    {
        private sealed record Pose(Vector3 Position, Quaternion Rotation, Vector3 Scale);
        private sealed record Surface(Renderer Renderer, Transform Root, Pose[] Hierarchy, Matrix4x4 Inverse, Bounds Bounds, bool Custom);
        private readonly Transform _part;
        private readonly Pose[] _hierarchy;
        private readonly Surface[] _surfaces;
        private readonly Dictionary<IntPtr, Transform> _frames = new();
        private Transform _inverse;

        internal WorldBatch(Transform part, Renderer[] renderers)
        {
            _part = part;
            _surfaces = renderers.Where(renderer => renderer.isPartOfStaticBatch &&
                (renderer.staticBatchIndex > 0 || renderer.GetComponent<MeshFilter>()?.sharedMesh?.name.StartsWith("Combined Mesh", StringComparison.Ordinal) == true))
                .Select(renderer => new Surface(renderer, renderer.staticBatchRootTransform, Hierarchy(renderer.staticBatchRootTransform),
                    renderer.staticBatchRootTransform ? renderer.staticBatchRootTransform.worldToLocalMatrix : Matrix4x4.identity,
                    renderer.bounds, renderer.Internal_HasCustomBounds())).ToArray();
            _hierarchy = _surfaces.Length > 0 ? Hierarchy(part) : Array.Empty<Pose>();
        }

        private static Pose[] Hierarchy(Transform node)
        {
            List<Pose> poses = new();
            while (node)
            {
                poses.Add(new Pose(node.localPosition, node.localRotation, node.localScale));
                node = node.parent;
            }

            return poses.ToArray();
        }

        private static Transform Frame(Transform parent, Pose pose)
        {
            Transform frame = new GameObject("NullFrameWorldBatch").transform;
            frame.SetParent(parent, false);
            frame.localPosition = pose.Position;
            frame.localRotation = pose.Rotation;
            frame.localScale = pose.Scale;
            return frame;
        }

        internal void Apply()
        {
            if (_surfaces.Length == 0 || !_part)
            {
                return;
            }

            if (!_inverse)
            {
                _inverse = Frame(_part, new Pose(Vector3.zero, Quaternion.identity, Vector3.one));
                Transform parent = _inverse;

                foreach (Pose pose in _hierarchy)
                {
                    parent = Frame(parent, new Pose(Vector3.zero, Quaternion.identity, new Vector3(Inverse(pose.Scale.x), Inverse(pose.Scale.y), Inverse(pose.Scale.z))));
                    parent = Frame(parent, new Pose(Vector3.zero, Quaternion.Inverse(pose.Rotation), Vector3.one));
                    parent = Frame(parent, new Pose(-pose.Position, Quaternion.identity, Vector3.one));
                }

                _frames[IntPtr.Zero] = parent;
            }

            foreach (Surface surface in _surfaces)
            {
                if (!surface.Renderer)
                {
                    continue;
                }

                IntPtr key = surface.Root ? surface.Root.Pointer : IntPtr.Zero;
                if (!_frames.TryGetValue(key, out var frame))
                {
                    frame = _frames[IntPtr.Zero];
                    foreach (Pose pose in surface.Hierarchy.AsEnumerable().Reverse())
                    {
                        frame = Frame(frame, pose);
                    }

                    _frames[key] = frame;
                }

                surface.Renderer.staticBatchRootTransform = frame;
                Matrix4x4 delta = frame.localToWorldMatrix * surface.Inverse;
                Vector3 size = surface.Bounds.extents;
                Vector3 x = delta.MultiplyVector(new Vector3(size.x, 0f, 0f));
                Vector3 y = delta.MultiplyVector(new Vector3(0f, size.y, 0f));
                Vector3 z = delta.MultiplyVector(new Vector3(0f, 0f, size.z));
                surface.Renderer.bounds = new Bounds(delta.MultiplyPoint3x4(surface.Bounds.center), new Vector3(
                    Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x),
                    Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y),
                    Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z)) * 2f);
            }
        }

        private static float Inverse(float scale) => Mathf.Abs(scale) > 0.000001f ? 1f / scale : 0f;

        internal void Restore()
        {
            foreach (Surface surface in _surfaces)
            {
                if (!surface.Renderer)
                {
                    continue;
                }

                surface.Renderer.staticBatchRootTransform = surface.Root;
                if (surface.Custom)
                {
                    surface.Renderer.bounds = surface.Bounds;
                }
                else
                {
                    surface.Renderer.ResetBounds();
                }
            }

            if (_inverse)
            {
                Object.Destroy(_inverse.gameObject);
            }

            _inverse = null;
            _frames.Clear();
        }
    }
}
