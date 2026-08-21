using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI.Group;

namespace TSA_WorldDomination
{
    public static class Trader_OnPlayerColony
    {
        public static bool IsWorldDominationTrader;

        public static bool TrySpawnTraderOnPlayerColony(Settlement targetColony, Faction traderFaction, TraderKindDef traderKind = null)
        {
            if (targetColony == null || !targetColony.HasMap || traderFaction == null) return false;

            IncidentParms parms = new IncidentParms
            {
                target = targetColony.Map,
                faction = traderFaction,
                forced = true,
                traderKind = traderKind
            };

            try
            {
                IsWorldDominationTrader = true;
                return IncidentDefOf.TraderCaravanArrival.Worker.TryExecute(parms);
            }
            finally
            {
                IsWorldDominationTrader = false;
            }
        }

        /// <summary>
        /// True when the colony map already has a trader caravan lord from a faction hostile to
        /// <paramref name="incomingFaction"/>. Used to skip a second spawn while still applying arrival rewards.
        /// </summary>
        public static bool TryFindHostileTraderAlreadyOnMap(Map map, Faction incomingFaction, out Faction blockingFaction)
        {
            blockingFaction = null;
            if (map?.lordManager?.lords == null || incomingFaction == null)
                return false;

            var lords = map.lordManager.lords;
            for (int i = 0; i < lords.Count; i++)
            {
                Lord lord = lords[i];
                if (lord == null || lord.faction == null || lord.faction == incomingFaction)
                    continue;
                if (!IsTraderCaravanLordJob(lord.LordJob))
                    continue;
                if (!WorldActions_Utils.SafeHostileTo(incomingFaction, lord.faction))
                    continue;
                blockingFaction = lord.faction;
                return true;
            }

            return false;
        }

        private static bool IsTraderCaravanLordJob(LordJob job)
        {
            return job is LordJob_TradeWithColony
                || job is LordJob_DefendAttackedTraderCaravan;
        }
    }
}
