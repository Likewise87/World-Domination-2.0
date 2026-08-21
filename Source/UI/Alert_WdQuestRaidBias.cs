using System.Collections.Generic;
using System.Text;
using RimWorld;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Right-side alert while quest raid bias is active (player-as-target preferred in the label).</summary>
    public class Alert_WdQuestRaidBias : Alert
    {
        private readonly StringBuilder explanationScratch = new StringBuilder();
        private readonly List<QuestRaidBiasEntry> entriesScratch = new List<QuestRaidBiasEntry>();

        public Alert_WdQuestRaidBias()
        {
            defaultPriority = AlertPriority.Medium;
        }

        public override string GetLabel()
        {
            CollectActive(entriesScratch);
            if (entriesScratch.Count == 0) return "";

            Faction player = Faction.OfPlayerSilentFail;
            int now = Find.TickManager.TicksGame;
            for (int i = 0; i < entriesScratch.Count; i++)
            {
                QuestRaidBiasEntry e = entriesScratch[i];
                if (player != null && e.priorityTargetLoadId == player.loadID)
                {
                    Faction attacker = FindFaction(e.attackerLoadId);
                    string name = attacker?.Name ?? "?";
                    int days = UnityEngine.Mathf.CeilToInt(e.DaysRemaining(now));
                    return "TSA_WD_Alert_QuestRaidBias_PlayerTarget".Translate(name, days);
                }
            }

            QuestRaidBiasEntry first = entriesScratch[0];
            Faction a = FindFaction(first.attackerLoadId);
            Faction t = FindFaction(first.priorityTargetLoadId);
            int d = UnityEngine.Mathf.CeilToInt(first.DaysRemaining(now));
            return "TSA_WD_Alert_QuestRaidBias_Other".Translate(a?.Name ?? "?", t?.Name ?? "?", d);
        }

        public override TaggedString GetExplanation()
        {
            CollectActive(entriesScratch);
            if (entriesScratch.Count == 0) return "";

            int now = Find.TickManager.TicksGame;
            explanationScratch.Clear();
            explanationScratch.AppendLine("TSA_WD_Alert_QuestRaidBias_DescHeader".Translate());
            for (int i = 0; i < entriesScratch.Count; i++)
            {
                QuestRaidBiasEntry e = entriesScratch[i];
                Faction a = FindFaction(e.attackerLoadId);
                Faction t = FindFaction(e.priorityTargetLoadId);
                int days = UnityEngine.Mathf.CeilToInt(e.DaysRemaining(now));
                explanationScratch.AppendLine("TSA_WD_Alert_QuestRaidBias_DescLine".Translate(
                    a?.Name ?? "?",
                    t?.Name ?? "?",
                    days));
            }
            return explanationScratch.ToString().TrimEnd();
        }

        public override AlertReport GetReport()
        {
            if (Current.ProgramState != ProgramState.Playing) return false;
            CollectActive(entriesScratch);
            if (entriesScratch.Count == 0) return false;

            Faction player = Faction.OfPlayerSilentFail;
            for (int i = 0; i < entriesScratch.Count; i++)
            {
                if (player != null && entriesScratch[i].priorityTargetLoadId == player.loadID)
                    return AlertReport.Active;
            }
            return AlertReport.Active;
        }

        private static void CollectActive(List<QuestRaidBiasEntry> into)
        {
            into.Clear();
            List<QuestRaidBiasEntry> live = WdQuestRaidBias.GetActiveEntries();
            for (int i = 0; i < live.Count; i++)
                into.Add(live[i]);
        }

        private static Faction FindFaction(int loadId)
        {
            if (loadId < 0) return null;
            var all = Find.FactionManager?.AllFactionsListForReading;
            if (all == null) return null;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] != null && all[i].loadID == loadId)
                    return all[i];
            }
            return null;
        }
    }
}
