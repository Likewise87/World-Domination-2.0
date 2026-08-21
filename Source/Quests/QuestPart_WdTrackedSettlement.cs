using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Tracks the common-enemy quest target and whether a valid player strike was committed.
    /// </summary>
    public class QuestPart_WdTrackedSettlement : QuestPart
    {
        public Settlement? settlement;
        public Faction? originalEnemyFaction;
        public SettlementTier targetTier = SettlementTier.T1;
        public bool playerAttributed;
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
            Scribe_References.Look(ref originalEnemyFaction, "originalEnemyFaction");
            Scribe_Values.Look(ref targetTier, "targetTier", SettlementTier.T1);
            Scribe_Values.Look(ref playerAttributed, "playerAttributed", false);
            Scribe_Values.Look(ref settlementLabelFallback, "settlementLabelFallback");
        }
    }
}
