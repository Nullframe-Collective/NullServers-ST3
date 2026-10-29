using HarmonyLib;
using NullServers.Server;

namespace NullServers.Patches
{
    [HarmonyPatch(typeof(UnityEngine.UI.Text), nameof(UnityEngine.UI.Text.text), MethodType.Setter)]
    internal static class TextSetTextPatch
    {
        public static void Prefix(UnityEngine.UI.Text __instance, ref string __0) => ServerControl.BeforeCaptionText(__instance, ref __0);
    }
}
