using System.Collections.Generic;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Right-side alert when a player WD outpost requires power and is unpowered.
    /// Toggled by <see cref="WorldDominationSettings.notifyOutpostUnpowered"/> (default on).
    /// </summary>
    public class Alert_WDOutpostUnpowered : Alert
    {
        private readonly List<WorldObject> culpritsScratch = new List<WorldObject>(8);
        private readonly StringBuilder explanationScratch = new StringBuilder();
        private string labelCached;

        public Alert_WDOutpostUnpowered()
        {
            defaultPriority = AlertPriority.Medium;
        }

        public override string GetLabel() => labelCached ??= "TSA_WD_Alert_OutpostUnpowered".Translate();

        public override TaggedString GetExplanation()
        {
            CollectCulprits(culpritsScratch);
            if (culpritsScratch.Count == 0) return "";

            explanationScratch.Clear();
            explanationScratch.AppendLine("TSA_WD_Alert_OutpostUnpoweredDescHeader".Translate());
            for (int i = 0; i < culpritsScratch.Count; i++)
            {
                WorldObject wo = culpritsScratch[i];
                explanationScratch.AppendLine("TSA_WD_Alert_OutpostUnpoweredDescLine".Translate(wo.LabelCap));
            }
            return explanationScratch.ToString().TrimEnd();
        }

        public override AlertReport GetReport()
        {
            if (Current.ProgramState != ProgramState.Playing) return false;
            if (!OutpostPowerGrid.Enabled) return false;
            if (WorldDominationMod.settings == null || !WorldDominationMod.settings.notifyOutpostUnpowered)
                return false;

            CollectCulprits(culpritsScratch);
            if (culpritsScratch.Count == 0) return false;

            WorldObject first = culpritsScratch[0];
            return first != null && !first.Destroyed ? AlertReport.CulpritIs(first) : AlertReport.Active;
        }

        private static void CollectCulprits(List<WorldObject> into)
        {
            into.Clear();
            IReadOnlyList<WorldObject_WD_Outpost> outposts = WdPlayerOutpostCache.PlayerOutposts;
            for (int i = 0; i < outposts.Count; i++)
            {
                WorldObject_WD_Outpost outpost = outposts[i];
                if (outpost == null || outpost.Destroyed) continue;
                if (!OutpostPowerGrid.RequiresPower(outpost)) continue;
                if (OutpostPowerGrid.IsPowered(outpost)) continue;
                into.Add(outpost);
            }
        }
    }
}
