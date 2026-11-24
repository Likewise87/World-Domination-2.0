using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// FoW-aware settlement visibility for WD quest issue pickers.
    /// When settlement intel tracking is off, all settlements are eligible (legacy behavior).
    /// </summary>
    public static class WdQuestDiscovery
    {
        /// <summary>
        /// True when the player may use this settlement as a quest asker/target.
        /// With intel tracking on: must be discovered (or always-known).
        /// </summary>
        public static bool PlayerSeesSettlement(WorldObject wo)
        {
            if (wo == null || wo.Destroyed) return false;
            return WdPlayerIntel.PlayerKnows(wo);
        }

        /// <summary>True if this faction has at least one WD surface settlement the player can see for quests.</summary>
        public static bool PlayerSeesAnySettlementOf(Faction faction)
        {
            if (faction == null || faction.IsPlayer) return false;
            List<Settlement> settlements = Find.WorldObjects?.Settlements;
            if (settlements == null) return false;
            for (int i = 0; i < settlements.Count; i++)
            {
                Settlement s = settlements[i];
                if (s == null || s.Destroyed || s.Faction != faction) continue;
                if (!WorldActions_Utils.IsWdSurfaceWorldObject(s)) continue;
                if (s.GetComponent<CompViralSpread>() == null) continue;
                if (!PlayerSeesSettlement(s)) continue;
                return true;
            }
            return false;
        }
    }
}
