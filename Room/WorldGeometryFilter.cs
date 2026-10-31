using Il2Cpp;
using Il2CppFishNet.Object;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace NullServers.Room
{
    internal static class WorldGeometryFilter
    {
        internal static bool Allowed(Transform node)
        {
            if (!node) return false;

            for (Transform parent = node; parent; parent = parent.parent)
            {
                if (Temporary(parent)) return false;
            }

            if (Parent<NetworkObject>(node) || Parent<Canvas>(node) || Parent<Camera>(node) ||
                Parent<FPScontroller>(node) || Parent<RagdollController>(node) || Parent<CharacterCustomization>(node) ||
                Parent<SyncCustomization>(node) || Parent<Animator>(node) || Parent<Animation>(node) ||
                Parent<Rigidbody>(node) || Parent<CharacterController>(node) || Parent<SkinnedMeshRenderer>(node)) return false;

            if (node.GetComponentsInChildren<Transform>(true).Any(Temporary)) return false;

            return !Children<NetworkObject>(node) && !Children<Camera>(node) && !Children<Canvas>(node) &&
                !Children<Rigidbody>(node) && !Children<CharacterController>(node) && !Children<SkinnedMeshRenderer>(node) &&
                !Children<CharacterCustomization>(node) && !Children<SyncCustomization>(node) &&
                !Children<FPScontroller>(node) && !Children<RagdollController>(node) &&
                !Children<Animator>(node) && !Children<Animation>(node);
        }

        private static bool Temporary(Transform node) =>
            (node.gameObject.hideFlags & HideFlags.DontSave) != 0 ||
            node.name.StartsWith("NullFrame", StringComparison.Ordinal) || node.name is "BuildGhost" or "BuildSelection";

        private static bool Parent<T>(Transform node) where T : Component =>
            node.GetComponentInParent(Il2CppType.Of<T>(), true);

        private static bool Children<T>(Transform node) where T : Component =>
            node.GetComponentsInChildren(Il2CppType.Of<T>(), true).Length > 0;
    }
}
