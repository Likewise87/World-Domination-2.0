using System.Collections.Generic;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Persistent right-side alert when at least one player WD outpost has critical virtual food
    /// (negative stock, or ≤3 in-game days of food left while net is draining).
    /// Label stays generic; per-outpost detail is only in the mouseover explanation.
    /// Click jumps to the most severe outpost. Toggled by notifyCriticalFood.
    /// Inherits <see cref="Alert_Critical"/> for the same pulsing red fill as Major Break Risk.
    /// </summary>
    public class Alert_WDCriticalFood : Alert_Critical
    {
        private readonly List<(WorldObject Obj, CompOutpostLogistics Logi, float DaysUntilStarvation)> criticalScratch =
            new List<(WorldObject, CompOutpostLogistics, float)>(8);

        private readonly StringBuilder explanationScratch = new StringBuilder();

        private static WorldComponent_LogisticsManager Manager =>
            Current.ProgramState == ProgramState.Playing ? Find.World?.GetComponent<WorldComponent_LogisticsManager>() : null;

        public override string GetLabel() => "TSA_WD_Alert_CriticalFood".Translate();

        public override TaggedString GetExplanation()
        {
            var m = Manager;
            if (m == null) return "";
            m.CollectCriticalFoodOutposts(criticalScratch);
            if (criticalScratch.Count == 0) return "";

            explanationScratch.Clear();
            explanationScratch.AppendLine("TSA_WD_Alert_CriticalFoodDescHeader".Translate());
            for (int i = 0; i < criticalScratch.Count; i++)
            {
                var row = criticalScratch[i];
                explanationScratch.AppendLine("TSA_WD_Alert_CriticalFoodDescLine".Translate(
                    row.Obj.LabelCap,
                    row.Logi.currentFood.ToString("F1"),
                    row.DaysUntilStarvation.ToString("F1")));
            }
            return explanationScratch.ToString().TrimEnd();
        }

        public override AlertReport GetReport()
        {
            if (Current.ProgramState != ProgramState.Playing) return false;
            if (WorldDominationMod.settings == null || !WorldDominationMod.settings.notifyCriticalFood) return false;
            if (!WorldDominationMod.settings.foodLogisticsActive) return false;

            var m = Manager;
            if (m == null) return false;
            m.CollectCriticalFoodOutposts(criticalScratch);
            if (criticalScratch.Count == 0) return false;

            WorldObject worst = criticalScratch[0].Obj;
            return worst != null ? AlertReport.CulpritIs(worst) : AlertReport.Active;
        }
    }
}
