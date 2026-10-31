using Il2CppInterop.Runtime;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Bindings;
using Object = UnityEngine.Object;

namespace NullServers.Sandbox
{
    internal sealed class NavigationObstacle
    {
        [StructLayout(LayoutKind.Sequential)]
        private readonly struct NameSpan(IntPtr begin, int length)
        {
            private readonly IntPtr _begin = begin;
            private readonly int _length = length;
        }

        private delegate IntPtr AddComponentDelegate(IntPtr self, ref NameSpan name);

        private static readonly AddComponentDelegate AddComponent = IL2CPP.ResolveICall<AddComponentDelegate>(
            "UnityEngine.GameObject::AddComponentInternal_Injected");
        private readonly Behaviour _component;

        private IntPtr Native => Object.MarshalledUnityObject.MarshalNotNull(_component);

        internal bool Alive => _component;

        internal bool Enabled
        {
            set => _component.enabled = value;
        }

        internal bool Carving
        {
            set => NavMeshObstacle.set_carving_Injected(Native, value);
        }

        internal NavigationObstacle(GameObject frame)
        {
            IntPtr text = Marshal.StringToHGlobalUni("NavMeshObstacle");
            try
            {
                NameSpan name = new(text, "NavMeshObstacle".Length);
                IntPtr handle = AddComponent(Object.MarshalledUnityObject.MarshalNotNull(frame), ref name);
                Component component = Unmarshal.UnmarshalUnityObject<Component>(handle);
                if (!component) throw new InvalidOperationException("Unity could not create a navigation obstacle.");

                _component = component.Cast<Behaviour>();
            }
            finally
            {
                Marshal.FreeHGlobal(text);
            }

            Enabled = false;
            NavMeshObstacle.set_carveOnlyStationary_Injected(Native, true);
            NavMeshObstacle.set_carvingTimeToStationary_Injected(Native, 0.5f);
            NavMeshObstacle.set_carvingMoveThreshold_Injected(Native, 0.1f);
            Carving = true;
        }

        internal void Box(Vector3 center, Vector3 size)
        {
            IntPtr native = Native;
            NavMeshObstacle.set_shape_Injected(native, NavMeshObstacleShape.Box);
            NavMeshObstacle.set_center_Injected(native, ref center);
            NavMeshObstacle.set_size_Injected(native, ref size);
        }

        internal void Capsule(Vector3 center, float radius, float height)
        {
            IntPtr native = Native;
            NavMeshObstacle.set_shape_Injected(native, NavMeshObstacleShape.Capsule);
            NavMeshObstacle.set_center_Injected(native, ref center);
            NavMeshObstacle.set_radius_Injected(native, radius);
            NavMeshObstacle.set_height_Injected(native, height);
        }
    }
}
