using UnityEngine;
using Object = UnityEngine.Object;

namespace NullServers.Sandbox
{
    internal sealed class PropNavigation : IDisposable
    {
        private sealed record Entry(Collider Collider, NavigationObstacle Obstacle, GameObject Frame);
        private readonly List<Entry> _entries = new();
        private bool _held;

        internal PropNavigation(GameObject root)
        {
            foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
            {
                if (!collider || collider.isTrigger) continue;

                GameObject frame = new("PropNavigationObstacle");
                frame.transform.SetParent(collider.transform, false);
                NavigationObstacle obstacle = new(frame);
                _entries.Add(new Entry(collider, obstacle, frame));
            }

            Refresh();
        }

        internal void Refresh()
        {
            foreach (Entry entry in _entries)
            {
                if (!entry.Obstacle.Alive) continue;

                entry.Obstacle.Enabled = false;
                if (!entry.Collider || !entry.Collider.enabled || entry.Collider.isTrigger) continue;

                if (Configure(entry.Collider, entry.Obstacle))
                {
                    entry.Obstacle.Enabled = true;
                }
            }
        }

        private static bool Configure(Collider collider, NavigationObstacle obstacle)
        {
            if (collider.TryCast<BoxCollider>() is { } box)
            {
                obstacle.Box(box.center, box.size);
                return true;
            }

            if (collider.TryCast<SphereCollider>() is { } sphere)
            {
                obstacle.Capsule(sphere.center, sphere.radius, sphere.radius * 2f);
                return true;
            }

            if (collider.TryCast<CapsuleCollider>() is { } capsule)
            {
                if (capsule.direction == 1)
                {
                    obstacle.Capsule(capsule.center, capsule.radius, Mathf.Max(capsule.height, capsule.radius * 2f));
                }
                else
                {
                    Vector3 size = Vector3.one * (capsule.radius * 2f);
                    size[capsule.direction] = Mathf.Max(capsule.height, capsule.radius * 2f);
                    obstacle.Box(capsule.center, size);
                }

                return true;
            }

            if (collider.TryCast<MeshCollider>() is { } mesh && mesh.sharedMesh)
            {
                obstacle.Box(mesh.sharedMesh.bounds.center, mesh.sharedMesh.bounds.size);
                return true;
            }

            return false;
        }

        internal void SetHeld(bool held)
        {
            if (_held == held) return;

            _held = held;
            foreach (Entry entry in _entries)
            {
                if (entry.Obstacle.Alive)
                {
                    entry.Obstacle.Carving = !held;
                }
            }
        }

        public void Dispose()
        {
            foreach (Entry entry in _entries)
            {
                if (entry.Obstacle.Alive)
                {
                    entry.Obstacle.Enabled = false;
                }

                if (entry.Frame)
                {
                    Object.Destroy(entry.Frame);
                }
            }

            _entries.Clear();
        }
    }
}
