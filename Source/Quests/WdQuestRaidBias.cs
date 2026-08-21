using System.Collections.Generic;
using RimWorld;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// One WD quest raid-bias entry: when <see cref="attacker"/> raids, prefer
    /// <see cref="priorityTarget"/>'s settlements/outposts in candidate order.
    /// Does not force hostility or invent in-range targets (silent no-op if none qualify).
    /// </summary>
    public class QuestRaidBiasEntry : IExposable
    {
        public int attackerLoadId = -1;
        public int priorityTargetLoadId = -1;
        public int expiryTick = -1;

        public void ExposeData()
        {
            Scribe_Values.Look(ref attackerLoadId, "attackerLoadId", -1);
            Scribe_Values.Look(ref priorityTargetLoadId, "priorityTargetLoadId", -1);
            Scribe_Values.Look(ref expiryTick, "expiryTick", -1);
        }

        public bool IsExpired(int currentTick) => expiryTick < 0 || currentTick >= expiryTick;

        public float DaysRemaining(int currentTick)
        {
            int rem = expiryTick - currentTick;
            return rem <= 0 ? 0f : rem / 60000f;
        }
    }

    /// <summary>
    /// Quest raid bias for future WD quests.
    /// Primary API: <see cref="Apply"/>. Fail/won aliases set common call sites.
    /// Pay-or-raid fail: Apply(asker, player). Conquer-for-B win: Apply(B, A).
    /// </summary>
    public static class WdQuestRaidBias
    {
        public const int DefaultDurationDays = 15;

        public static void Apply(Faction attacker, Faction priorityTarget, int durationDays = DefaultDurationDays)
        {
            if (attacker == null || priorityTarget == null || attacker == priorityTarget)
                return;
            if (attacker.defeated || priorityTarget.defeated)
                return;
            if (durationDays <= 0)
                return;

            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            if (manager == null)
                return;

            bool playerTargeted = priorityTarget.IsPlayer;
            manager.SetQuestRaidBias(attacker, priorityTarget, durationDays);

            if (playerTargeted)
                SendPlayerTargetedLetter(attacker, durationDays);
        }

        /// <summary>Fail stick: attacker prioritizes the player (or an explicit target).</summary>
        public static void ApplyOnQuestFailed(Faction attacker, Faction priorityTarget = null, int durationDays = DefaultDurationDays)
        {
            Apply(attacker, priorityTarget ?? Faction.OfPlayerSilentFail, durationDays);
        }

        /// <summary>Win stick: e.g. asker B prioritizes conquered faction A.</summary>
        public static void ApplyOnQuestWon(Faction attacker, Faction priorityTarget, int durationDays = DefaultDurationDays)
        {
            Apply(attacker, priorityTarget, durationDays);
        }

        public static bool IsActive(Faction attacker, Faction priorityTarget = null)
        {
            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            return manager != null && manager.IsQuestRaidBiasActive(attacker, priorityTarget);
        }

        public static void Clear(Faction attacker, Faction priorityTarget = null)
        {
            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            manager?.ClearQuestRaidBias(attacker, priorityTarget);
        }

        public static List<QuestRaidBiasEntry> GetActiveEntries()
        {
            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            return manager?.GetActiveQuestRaidBiasEntries() ?? new List<QuestRaidBiasEntry>();
        }

        public static HashSet<int> GetPriorityTargetLoadIds(Faction attacker)
        {
            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            return manager?.GetQuestRaidBiasPriorityTargetLoadIds(attacker) ?? new HashSet<int>();
        }

        private static void SendPlayerTargetedLetter(Faction attacker, int durationDays)
        {
            string name = attacker?.Name ?? "Faction";
            Find.LetterStack.ReceiveLetter(
                "TSA_WD_QuestRaidBias_LetterLabel".Translate(name),
                "TSA_WD_QuestRaidBias_LetterText".Translate(name, durationDays),
                LetterDefOf.ThreatSmall);
        }
    }
}
