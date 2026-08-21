using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    public static class WorldActions_OutpostIncidents
    {
        private const float DeadOffenseEpsilon = 0.01f;

        /// <summary>Once per day after escalation metrics update; may attrition one player outpost while Mid or Late is active.</summary>
        public static void TryDailyOutpostIncident(WorldComponent_SpreadManager manager)
        {
            var seth = WorldDominationMod.settings;
            if (seth == null || manager == null) return;

            WdEscalationStage stage = manager.cachedEscalationStage;
            if (!WdEscalation.OutpostIncidentsEnabled(seth, stage)) return;

            float chance = WdEscalation.GetOutpostIncidentDailyChance(seth, stage);
            if (chance <= 0f || Rand.Value > chance) return;

            var outposts = WorldStatsUtils.CollectPlayerOutposts();
            if (outposts == null || outposts.Count == 0) return;

            WorldObject_WD_Outpost target = null;
            CompViralSpread targetComp = null;
            int tries = 0;
            while (tries < 12)
            {
                tries++;
                var candidate = outposts.RandomElement();
                if (candidate == null || candidate.Destroyed) continue;
                var comp = candidate.GetComponent<CompViralSpread>();
                if (comp == null || comp.IsIncidentOnCooldown) continue;
                target = candidate;
                targetComp = comp;
                break;
            }

            if (target == null || targetComp == null) return;

            float loss = WdEscalation.GetOutpostIncidentSeverity(seth, stage);
            float oldDef = targetComp.defensiveStrength;
            float oldOff = targetComp.offensiveStrength;
            targetComp.defensiveStrength = Mathf.Max(0f, oldDef - loss);
            targetComp.offensiveStrength = Mathf.Max(0f, oldOff - loss);
            targetComp.incidentCooldownTick = Find.TickManager.TicksGame
                + CompViralSpread.CooldownTicksFromDays(Mathf.Max(0.1f, seth.cooldownIncidentDays));
            targetComp.CheckTierUpdate(false);

            // Always log (letter stays behind notifyOutpostIncident).
            Log.Message(
                $"[WD] Outpost incident target={target.LabelCap} tile={target.Tile.tileId} stage={stage} loss={loss:F0} " +
                $"off {oldOff:F1}->{targetComp.offensiveStrength:F1} def {oldDef:F1}->{targetComp.defensiveStrength:F1} " +
                $"occupants={target.Occupants?.Count ?? 0}");

            if (targetComp.offensiveStrength <= DeadOffenseEpsilon)
            {
                DestroyOutpostFromIncident(target, targetComp, manager, seth, loss, oldDef, oldOff);
                return;
            }

            manager.AddLog(new SpreadLogEntry(
                "TSA_WD_Log_OutpostIncident".Translate(
                    target.LabelCap,
                    loss.ToString("F0"),
                    oldDef.ToString("F0"),
                    targetComp.defensiveStrength.ToString("F0")),
                target));

            if (seth.notifyOutpostIncident)
            {
                Find.LetterStack.ReceiveLetter(
                    "TSA_WD_Letter_OutpostIncident_Label".Translate(target.LabelCap),
                    "TSA_WD_Letter_OutpostIncident_Text".Translate(target.LabelCap, loss.ToString("F0")),
                    LetterDefOf.NegativeEvent,
                    target);
            }
        }

        /// <summary>Incident reduced outpost offense to ~0: destroy and leave timed WD ruins (same as raid raze).</summary>
        private static void DestroyOutpostFromIncident(
            WorldObject_WD_Outpost target,
            CompViralSpread targetComp,
            WorldComponent_SpreadManager manager,
            WorldDominationSettings seth,
            float loss,
            float oldDef,
            float oldOff)
        {
            if (target == null || target.Destroyed) return;

            int tile = target.Tile;
            string label = target.LabelCap;
            Faction faction = target.Faction;

            manager?.AddLog(new SpreadLogEntry(
                "TSA_WD_Log_OutpostIncident_Destroyed".Translate(
                    label,
                    loss.ToString("F0"),
                    oldOff.ToString("F0"),
                    oldDef.ToString("F0")),
                target));

            if (seth != null && seth.notifyOutpostIncident)
            {
                Find.LetterStack.ReceiveLetter(
                    "TSA_WD_Letter_OutpostIncidentDestroyed_Label".Translate(label),
                    "TSA_WD_Letter_OutpostIncidentDestroyed_Text".Translate(label, loss.ToString("F0")),
                    LetterDefOf.NegativeEvent,
                    new GlobalTargetInfo(tile));
            }

            Log.Message(
                $"[WD] Outpost incident destroyed outpost={label} tile={tile} " +
                $"off {oldOff:F1}->{targetComp?.offensiveStrength:F1} def {oldDef:F1}->{targetComp?.defensiveStrength:F1}");

            target.Destroy();
            WorldObject_WdSettlementRuin.Spawn(tile, label, faction);
        }
    }
}
