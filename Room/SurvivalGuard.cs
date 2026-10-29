using Il2Cpp;
using UnityEngine;

namespace NullServers.Room
{
    public sealed class SurvivalRound
    {
        public float StartedAt;
        public float NextSurvivorCheck;
        public bool HadSurvivors;
        public bool RoundEndSkipped;
    }

    public static class SurvivalGuard
    {
        private const float MaxWaitForSurvivors = 120f;
        private static readonly Dictionary<IntPtr, SurvivalRound> Rounds = new();

        public static void Reset()
        {
            Rounds.Clear();
        }

        public static SurvivalRound GetRound(SurvivalMechanics survival)
        {
            if (!Rounds.TryGetValue(survival.Pointer, out SurvivalRound round))
            {
                round = new SurvivalRound { StartedAt = Time.unscaledTime };
                Rounds[survival.Pointer] = round;
            }

            return round;
        }

        public static bool CanEndRound(SurvivalMechanics survival)
        {
            if (!Rounds.TryGetValue(survival.Pointer, out SurvivalRound round)) return true;

            return round.HadSurvivors || Time.unscaledTime - round.StartedAt > MaxWaitForSurvivors;
        }
    }
}
