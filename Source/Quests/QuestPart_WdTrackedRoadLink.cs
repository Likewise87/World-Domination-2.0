using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Tracks the colony road-link asker settlement for look targets and outcome checks.
    /// </summary>
    public class QuestPart_WdTrackedRoadLink : QuestPart
    {
        public Settlement? settlement;
        public Faction? askerFaction;
        public string? settlementLabelFallback;

        public override IEnumerable<GlobalTargetInfo> QuestLookTargets
        {
            get
            {
                if (settlement != null && !settlement.Destroyed)
                    yield return settlement;
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref settlement, "settlement");
            Scribe_References.Look(ref askerFaction, "askerFaction");
            Scribe_Values.Look(ref settlementLabelFallback, "settlementLabelFallback");
        }
    }
}
