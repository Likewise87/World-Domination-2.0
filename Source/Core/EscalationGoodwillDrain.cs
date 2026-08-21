using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Periodic Mid/Late goodwill drain while escalation is active.</summary>
    public static class EscalationGoodwillDrain
    {
        private static int lastPulseTick = -1;

        public static void ResetPulseClock() => lastPulseTick = -1;

        /// <summary>Call from the daily escalation path when metrics are fresh.</summary>
        public static void TryPulse(WorldComponent_SpreadManager manager)
        {
            if (manager == null) return;
            var seth = WorldDominationMod.settings;
            if (seth == null || !seth.enableLateGameScaling || !seth.enableGoodwillDrain) return;

            WdEscalationStage stage = manager.cachedEscalationStage;
            int amount = WdEscalation.GetGoodwillDrainAmount(seth, stage);
            if (amount <= 0) return;

            int intervalDays = Mathf.Max(1, seth.goodwillDrainIntervalDays);
            int intervalTicks = intervalDays * GenDate.TicksPerDay;
            int now = Find.TickManager?.TicksGame ?? 0;
            if (lastPulseTick >= 0 && now - lastPulseTick < intervalTicks)
                return;
            lastPulseTick = now;

            Faction player = Faction.OfPlayerSilentFail;
            if (player == null) return;

            int drainedFactions = 0;
            var kindFlips = new List<(Faction faction, FactionRelationKind from, FactionRelationKind to)>();

            List<Faction> factions = Find.FactionManager?.AllFactionsListForReading;
            if (factions == null) return;

            for (int i = 0; i < factions.Count; i++)
            {
                Faction faction = factions[i];
                if (faction == null || faction.defeated || WorldActions_Utils.IsExcludedFaction(faction)) continue;
                if (WorldActions_Utils.IsPermanentEnemyOfPlayer(faction)) continue;

                FactionRelationKind before = WorldActions_Utils.SafeRelationKindWith(faction, player);
                if (!TryAffectSilent(faction, player, -amount))
                    continue;

                drainedFactions++;
                FactionRelationKind after = WorldActions_Utils.SafeRelationKindWith(faction, player);
                if (before != after)
                    kindFlips.Add((faction, before, after));
            }

            if (drainedFactions <= 0) return;

            Messages.Message(
                "TSA_WD_GoodwillMsg_EscalationDrainBatch".Translate(WdEscalation.StageLabel(stage), amount, drainedFactions),
                MessageTypeDefOf.NeutralEvent);

            for (int i = 0; i < kindFlips.Count; i++)
            {
                var flip = kindFlips[i];
                Messages.Message(
                    "TSA_WD_GoodwillMsg_EscalationRelationFlip".Translate(
                        flip.faction.Name,
                        RelationLabel(flip.from),
                        RelationLabel(flip.to)),
                    MessageTypeDefOf.NegativeEvent);
            }
        }

        private static string RelationLabel(FactionRelationKind kind) => kind switch
        {
            FactionRelationKind.Ally => "TSA_WD_Relation_Ally".Translate().ToString(),
            FactionRelationKind.Hostile => "TSA_WD_Relation_Hostile".Translate().ToString(),
            _ => "TSA_WD_Relation_Neutral".Translate().ToString()
        };

        private static bool TryAffectSilent(Faction faction, Faction player, int change)
        {
            if (faction == null || player == null || change == 0) return false;
            // Suppress vanilla goodwill/hostility letters; WD posts its own batch + kind-flip messages.
            return faction.TryAffectGoodwillWith(player, change, canSendMessage: false, canSendHostilityLetter: false);
        }
    }
}
