using System.Collections.Generic;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Right-side alert when a player WD outpost has construction progress ready but lacks materials to dispatch the crew.
    /// Toggled by <see cref="WorldDominationSettings.notifyConstructionInsufficientMaterials"/> (default on).
    /// </summary>
    public class Alert_WDConstructionInsufficientMaterials : Alert
    {
        private readonly List<WorldObject> culpritsScratch = new List<WorldObject>(8);
        private readonly StringBuilder explanationScratch = new StringBuilder();
        private string labelCached;

        public Alert_WDConstructionInsufficientMaterials()
        {
            defaultPriority = AlertPriority.Medium;
        }

        public override string GetLabel() => labelCached ??= "TSA_WD_Alert_ConstructionInsufficientMaterials".Translate();

        public override TaggedString GetExplanation()
        {
            CollectCulprits(culpritsScratch);
            if (culpritsScratch.Count == 0) return "";

            explanationScratch.Clear();
            explanationScratch.AppendLine("TSA_WD_Alert_ConstructionInsufficientMaterialsDescHeader".Translate());
            for (int i = 0; i < culpritsScratch.Count; i++)
            {
                WorldObject wo = culpritsScratch[i];
                var comp = wo.GetComponent<CompViralSpread>();
                string msg = comp?.GetInsufficientMaterialsConstructionMessage() ?? "";
                explanationScratch.AppendLine("TSA_WD_Alert_ConstructionInsufficientMaterialsDescLine".Translate(
                    wo.LabelCap,
                    msg));
            }
            return explanationScratch.ToString().TrimEnd();
        }

        public override AlertReport GetReport()
        {
            if (Current.ProgramState != ProgramState.Playing) return false;
            if (WorldDominationMod.settings == null || !WorldDominationMod.settings.notifyConstructionInsufficientMaterials)
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
                var comp = outpost.GetComponent<CompViralSpread>();
                if (comp != null && comp.IsConstructionWaitingOnMaterials(out _))
                    into.Add(outpost);
            }
        }
    }
}
