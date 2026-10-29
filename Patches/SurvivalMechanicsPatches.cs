using HarmonyLib;
using Il2Cpp;
using NullServers.Room;
using UnityEngine;

namespace NullServers.Patches
{
    [HarmonyPatch(typeof(SurvivalMechanics), nameof(SurvivalMechanics.FixedUpdate))]
    internal static class SurvivalMechanicsFixedUpdatePatch
    {
        public static void Prefix(SurvivalMechanics __instance)
        {
            SurvivalRound round = SurvivalGuard.GetRound(__instance);
            round.RoundEndSkipped = false;

            if (round.HadSurvivors || Time.unscaledTime < round.NextSurvivorCheck) return;

            round.NextSurvivorCheck = Time.unscaledTime + 0.5f;
            round.HadSurvivors = GameObject.FindGameObjectsWithTag("team1").Length > 0;
        }

        public static void Postfix(SurvivalMechanics __instance)
        {
            SurvivalRound round = SurvivalGuard.GetRound(__instance);
            if (!round.RoundEndSkipped) return;

            // The game marks the round end as checked even when we skipped it, so the flag is cleared to let the real end happen later.
            round.RoundEndSkipped = false;
            __instance.JKKJOBOKEMM = false;
        }
    }

    [HarmonyPatch(typeof(SurvivalMechanics), nameof(SurvivalMechanics.KMDHGEDCCAG))]
    internal static class SurvivalMechanicsRoundLostPatch
    {
        public static bool Prefix(SurvivalMechanics __instance)
        {
            if (SurvivalGuard.CanEndRound(__instance)) return true;

            SurvivalGuard.GetRound(__instance).RoundEndSkipped = true;

            return false;
        }
    }
}
