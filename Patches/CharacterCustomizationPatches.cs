using HarmonyLib;
using Il2Cpp;
using NullServers.Customization;

namespace NullServers.Patches
{
    [HarmonyPatch(typeof(CharacterCustomization), nameof(CharacterCustomization.Awake))]
    internal static class CharacterCustomizationAwakePatch
    {
        public static void Postfix(CharacterCustomization __instance) => LiveAppearance.CaptureHats(__instance);
    }
}
