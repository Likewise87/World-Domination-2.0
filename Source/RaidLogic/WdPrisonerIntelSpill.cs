using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// On prisoner→colonist convert, chance to permanently reveal the closest unknown settlement
    /// of the pawn's former faction (same Discover + 6-tile disk as scout asker).
    /// Pick candidate first, then roll, so a "hit" never silently no-ops when the map is fully known.
    /// </summary>
    public static class WdPrisonerIntelSpill
    {
        public static void TrySpillOnRecruit(Pawn pawn, Faction formerFaction, PlanetTile origin)
        {
            if (pawn == null || pawn.Destroyed) return;
            if (!WdPlayerIntel.TrackingActive) return;
            if (formerFaction == null || formerFaction.IsPlayer || formerFaction.defeated) return;
            if (!origin.Valid) return;

            var seth = WorldDominationMod.settings;
            if (seth == null || !seth.intelPrisonerSpillEnabled) return;
            if (seth.intelPrisonerSpillChance <= 0f) return;

            if (!TryPickClosestUnknownSettlement(formerFaction, origin.tileId, out Settlement settlement))
                return;

            if (!Rand.Chance(seth.intelPrisonerSpillChance))
                return;

            WdPlayerIntel.RevealSettlementToPlayer(settlement);

            if (!seth.notifyPrisonerIntelSpill || Find.LetterStack == null)
                return;

            LookTargets look = WdPlayerIntel.LookTargetsOrNull(settlement);
            Find.LetterStack.ReceiveLetter(
                "TSA_WD_Letter_PrisonerIntelSpill_Label".Translate(),
                "TSA_WD_Letter_PrisonerIntelSpill_Text".Translate(
                    pawn.LabelShortCap,
                    settlement.LabelCap,
                    formerFaction.Name),
                LetterDefOf.PositiveEvent,
                look);
        }

        /// <summary>Closest same-faction surface settlement the player has not discovered.</summary>
        public static bool TryPickClosestUnknownSettlement(Faction faction, int originTileId, out Settlement best)
        {
            best = null;
            if (faction == null || originTileId < 0) return false;

            WorldComponent_SettlementIntel intel = WorldComponent_SettlementIntel.Get();
            Faction player = WdPlayerIntel.Player;
            if (intel == null || player == null) return false;

            WorldGrid grid = Find.WorldGrid;
            if (grid == null) return false;

            List<Settlement> settlements = Find.WorldObjects?.Settlements;
            if (settlements == null || settlements.Count == 0) return false;

            float bestDist = float.MaxValue;
            for (int i = 0; i < settlements.Count; i++)
            {
                Settlement s = settlements[i];
                if (s == null || s.Destroyed) continue;
                if (s.Faction != faction) continue;
                if (!PlanetSurfaceWorldActions.IsPlanetSurfaceWorldObjectForWorldActions(s)) continue;
                if (intel.IsDiscovered(player, s)) continue;

                float dist = grid.ApproxDistanceInTiles(originTileId, s.Tile.tileId);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = s;
                }
            }

            return best != null;
        }
    }
}
