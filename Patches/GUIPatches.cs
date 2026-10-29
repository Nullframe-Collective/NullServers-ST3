using HarmonyLib;
using NullServers.Players;
using NullServers.Room;
using NullServers.Server;
using NullServers.UI;
using System.Reflection;
using UnityEngine;

namespace NullServers.Patches
{
    [HarmonyPatch(typeof(GUI), nameof(GUI.Label), new[] { typeof(Rect), typeof(string), typeof(GUIStyle) })]
    internal static class GUILabelForPlayerProfilesPatch
    {
        [HarmonyPriority(Priority.HigherThanNormal)]
        public static void Prefix(ref string __1, GUIStyle __2, out (bool Active, bool RichText, Color Color) __state) => PlayerProfiles.BeforeNameLabel(ref __1, __2, out __state);

        [HarmonyPriority(Priority.HigherThanNormal)]
        public static void Finalizer(GUIStyle __2, (bool Active, bool RichText, Color Color) __state) => PlayerProfiles.AfterNameLabel(__2, __state);
    }

    [HarmonyPatch(typeof(GUI), nameof(GUI.Box), new[] { typeof(Rect), typeof(string) })]
    internal static class GUIBoxForProfileMenuPatch
    {
        [HarmonyPriority(Priority.HigherThanNormal)]
        public static void Prefix(ref Rect __0) => ProfileMenu.BeforeBrowserBox(ref __0);
    }

    [HarmonyPatch(typeof(GUI), nameof(GUI.TextField), new[] { typeof(Rect), typeof(string), typeof(int) })]
    internal static class GUITextFieldForProfileMenuPatch
    {
        public static void Prefix(Rect __0) => ProfileMenu.BeforeRoomField(__0);
    }

    [HarmonyPatch(typeof(GUI), nameof(GUI.Label), new[] { typeof(Rect), typeof(string), typeof(GUIStyle) })]
    internal static class GUILabelForServerControlPatch
    {
        [HarmonyPriority(Priority.Normal)]
        public static void Prefix(ref string __1, GUIStyle __2, out bool __state) => ServerControl.BeforeRoomCaption(ref __1, __2, out __state);

        [HarmonyPriority(Priority.Normal)]
        public static void Finalizer(GUIStyle __2, bool __state) => ServerControl.AfterRoomCaption(__2, __state);
    }

    [HarmonyPatch(typeof(GUI), nameof(GUI.Box), new[] { typeof(Rect), typeof(string) })]
    internal static class GUIBoxForRoomCustomizationPatch
    {
        [HarmonyPriority(Priority.Normal)]
        public static void Prefix(Rect __0) => RoomCustomization.BeforeSettingsPanel(__0);
    }

    [HarmonyPatch(typeof(GUI), nameof(GUI.TextField), new[] { typeof(Rect), typeof(string), typeof(int), typeof(GUIStyle) })]
    internal static class GUITextFieldForRoomCustomizationPatch
    {
        public static void Prefix(Rect __0, ref string __1, ref int __2, GUIStyle __3, out (bool Active, bool RichText) __state) => RoomCustomization.BeforeHostName(__0, ref __1, ref __2, __3, out __state);

        public static void Postfix(ref string __result, (bool Active, bool RichText) __state) => RoomCustomization.AfterHostName(ref __result, __state);

        public static void Finalizer(GUIStyle __3, (bool Active, bool RichText) __state) => RoomCustomization.EndHostName(__3, __state);
    }

    [HarmonyPatch(typeof(GUI), nameof(GUI.BeginScrollView), new[] { typeof(Rect), typeof(Vector2), typeof(Rect) })]
    internal static class GUIBeginScrollViewPatch
    {
        public static void Prefix(ref Rect __0, ref Rect __2) => RoomCustomization.BeforeModeScrollView(ref __0, ref __2);
    }

    [HarmonyPatch(typeof(GUI), nameof(GUI.EndScrollView), new Type[0])]
    internal static class GUIEndScrollViewPatch
    {
        public static void Postfix() => RoomCustomization.AfterModeScrollView();
    }

    [HarmonyPatch(typeof(GUI), nameof(GUI.Label), new[] { typeof(Rect), typeof(string), typeof(GUIStyle) })]
    internal static class GUILabelForRoomCustomizationPatch
    {
        [HarmonyPriority(Priority.LowerThanNormal)]
        public static void Prefix(ref Rect __0, ref string __1, out bool __state) => RoomCustomization.BeforeLabel(ref __0, ref __1, out __state);

        [HarmonyPriority(Priority.LowerThanNormal)]
        public static void Finalizer(bool __state) => RoomCustomization.AfterSettingsControl(__state);
    }

    [HarmonyPatch]
    internal static class GUIControlsForRoomCustomizationPatch
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(GUI), nameof(GUI.Button), new[] { typeof(Rect), typeof(GUIContent), typeof(GUIStyle) });
            yield return AccessTools.Method(typeof(GUI), nameof(GUI.Toggle), new[] { typeof(Rect), typeof(bool), typeof(string) });
            yield return AccessTools.Method(typeof(GUI), nameof(GUI.HorizontalSlider), new[] { typeof(Rect), typeof(float), typeof(float), typeof(float) });
        }

        public static void Prefix(ref Rect __0, out bool __state, MethodBase __originalMethod) =>
            RoomCustomization.BeforeSettingsControl(ref __0, out __state, __originalMethod);

        public static void Finalizer(bool __state) => RoomCustomization.AfterSettingsControl(__state);
    }
}
