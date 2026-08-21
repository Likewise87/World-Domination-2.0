using System.Collections.Generic;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Right-side alert when a player WD outpost has unused expert slots
    /// (assigned count below capacity for roles available on that outpost type).
    /// Toggled by <see cref="WorldDominationSettings.notifyOutpostUnusedExperts"/> (default on).
    /// </summary>
    public class Alert_WDOutpostUnusedExperts : Alert
    {
        private readonly List<(WorldObject Obj, int Assigned, int Max)> culpritsScratch =
            new List<(WorldObject, int, int)>(8);
        private readonly StringBuilder explanationScratch = new StringBuilder();
        private string labelCached;

        public Alert_WDOutpostUnusedExperts()
        {
            defaultPriority = AlertPriority.Medium;
        }

        public override string GetLabel() => labelCached ??= "TSA_WD_Alert_OutpostUnusedExperts".Translate();

        public override TaggedString GetExplanation()
        {
            CollectCulprits(culpritsScratch);
            if (culpritsScratch.Count == 0) return "";

            explanationScratch.Clear();
            explanationScratch.AppendLine("TSA_WD_Alert_OutpostUnusedExpertsDescHeader".Translate());
            for (int i = 0; i < culpritsScratch.Count; i++)
            {
                var row = culpritsScratch[i];
                explanationScratch.AppendLine("TSA_WD_Alert_OutpostUnusedExpertsDescLine".Translate(
                    row.Obj.LabelCap,
                    row.Assigned,
                    row.Max));
            }
            return explanationScratch.ToString().TrimEnd();
        }

        public override AlertReport GetReport()
        {
            if (Current.ProgramState != ProgramState.Playing) return false;
            if (WorldDominationMod.settings == null || !WorldDominationMod.settings.notifyOutpostUnusedExperts)
                return false;

            CollectCulprits(culpritsScratch);
            if (culpritsScratch.Count == 0) return false;

            WorldObject first = culpritsScratch[0].Obj;
            return first != null && !first.Destroyed ? AlertReport.CulpritIs(first) : AlertReport.Active;
        }

        private static void CollectCulprits(List<(WorldObject Obj, int Assigned, int Max)> into)
        {
            into.Clear();
            IReadOnlyList<WorldObject_WD_Outpost> outposts = WdPlayerOutpostCache.PlayerOutposts;
            for (int i = 0; i < outposts.Count; i++)
            {
                WorldObject_WD_Outpost outpost = outposts[i];
                if (outpost == null || outpost.Destroyed) continue;

                int max = OutpostExpertUtility.GetMaxExpertSlots(outpost);
                if (max <= 0) continue;

                int assigned = OutpostExpertUtility.GetAssignedExpertCount(outpost);
                if (assigned < max)
                    into.Add((outpost, assigned, max));
            }
        }
    }
}
