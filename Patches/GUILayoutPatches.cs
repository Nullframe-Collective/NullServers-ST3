using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using NullServers.Players;
using UnityEngine;

namespace NullServers.Patches
{
    [HarmonyPatch(typeof(GUILayout), nameof(GUILayout.Label), new[] { typeof(string), typeof(GUIStyle), typeof(Il2CppReferenceArray<GUILayoutOption>) })]
    internal static class GUILayoutLabelPatch
    {
        public static void Prefix(ref string __0, GUIStyle __1, out (bool Active, bool RichText, Color Color) __state) => PlayerProfiles.BeforeChatLabel(ref __0, __1, out __state);

        public static void Finalizer(GUIStyle __1, (bool Active, bool RichText, Color Color) __state) => PlayerProfiles.AfterChatLabel(__1, __state);
    }
}
