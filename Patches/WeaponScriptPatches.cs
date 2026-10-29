using HarmonyLib;
using Il2Cpp;
using NullServers.UI;
using UnityEngine;

namespace NullServers.Patches
{
    [HarmonyPatch(typeof(WeaponScript), "Update")]
    internal static class WeaponScriptUpdatePatch
    {
        public static bool Prefix(Component __instance) => SandboxMenu.BeforeWeapon(__instance);
    }
}
