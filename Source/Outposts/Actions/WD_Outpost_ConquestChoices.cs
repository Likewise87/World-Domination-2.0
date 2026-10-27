using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Shared logic for the post-conquest choice menu (Recruit pawns / Give settlement to an ally).</summary>
    public static class WD_Outpost_ConquestChoices
    {
        /// <summary>
        /// Generates conquest-founding-count player-faction recruits using the conquered faction's xenotypes and pawn kinds,
        /// then delivers them into the conquering caravan if present, otherwise as a caravan at the ruins tile.
        /// </summary>
        private static readonly List<Outpost_Recruiting.XenotypePoolEntry> conquestXenotypePoolScratch = new List<Outpost_Recruiting.XenotypePoolEntry>();
        private static readonly List<Outpost_Recruiting.PawnKindPoolEntry> conquestPawnKindPoolScratch = new List<Outpost_Recruiting.PawnKindPoolEntry>();

        public static void DeliverRecruits(int tile, SettlementTier tier, int conqueringCaravanId, Faction conqueredFaction = null, int ruinsId = -1)
        {
            // Defensive: vanilla usually already removed ruins on leave; clear any lingerer before spawning recruits.
            ConquestOpportunityUtility.DestroyConquestRuinsAt(tile, ruinsId);

            int count = WorldDominationMod.settings?.GetConquestFoundingPawnCount(tier) ?? 0;
            if (count <= 0)
            {
                Messages.Message("TSA_WD_Conquest_RecruitNone".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            Outpost_Recruiting.BuildXenotypePoolFromFaction(conqueredFaction, conquestXenotypePoolScratch);
            Outpost_Recruiting.BuildPawnKindPoolFromFaction(conqueredFaction, conquestPawnKindPoolScratch);
            var pawns = new List<Pawn>();
            for (int i = 0; i < count; i++)
            {
                XenotypeDef xenotype = Outpost_Recruiting.RollXenotypeFromPool(conquestXenotypePoolScratch);
                PawnKindDef kind = Outpost_Recruiting.RollPawnKindFromPool(conquestPawnKindPoolScratch);
                Pawn p = Outpost_Recruiting.GenerateRecruitPawn(xenotype, prioritySkill: null, pawnKind: kind);
                if (p != null)
                {
                    PrepareGeneratedRecruitForCaravan(p);
                    pawns.Add(p);
                }
            }
            if (pawns.Count == 0) return;

            // Valid temp caravan first, then forced join onto conquering caravan (never MergeCaravans —
            // MaxBy pawn-count can Destroy a smaller Vehicle Framework VehicleCaravan and wipe vehicles).
            Caravan caravan = CaravanMaker.MakeCaravan(pawns, Faction.OfPlayer, tile, true);
            PlayerPawnTransferUtility.PackTravelPemmican(
                caravan,
                PlayerPawnTransferUtility.GetTravelPemmicanShortfall(caravan, pawns.Count));

            Caravan existing = FindCaravan(conqueringCaravanId);
            int joined = TryJoinRecruitCaravanIntoExisting(caravan, existing);
            if (joined > 0)
            {
                Messages.Message("TSA_WD_Conquest_RecruitJoined".Translate(joined, existing.Label), existing, MessageTypeDefOf.NeutralEvent);
                if (caravan == null || caravan.Destroyed || caravan.PawnsListForReading.Count == 0)
                    return;
            }

            if (caravan == null || caravan.Destroyed || caravan.PawnsListForReading.Count == 0)
                return;

            MapParent home = Find.AnyPlayerHomeMap?.Parent;
            if (home != null)
                caravan.pather.StartPath(PlanetSurfaceWorldActions.PlanetTileForWdTravel(home.Tile, caravan), null, false, false);

            Messages.Message("TSA_WD_Conquest_RecruitArriving".Translate(caravan.PawnsListForReading.Count), caravan, MessageTypeDefOf.NeutralEvent);
        }

        /// <summary>Conquest recruits are scripted rewards generated as player pawns; only clear transient ownership/holder state before caravan use.</summary>
        private static void PrepareGeneratedRecruitForCaravan(Pawn pawn)
        {
            if (pawn == null) return;
            pawn.ownership?.UnclaimAll();
            pawn.holdingOwner?.Remove(pawn);
        }

        /// <summary>
        /// Move recruits (and remaining temp inventory) onto <paramref name="existing"/> via AddPawn / AddPawnOrItem,
        /// then destroy the empty temp caravan. Keeps the conquering caravan instance (safe for VF VehicleCaravan).
        /// Returns how many pawns joined; 0 means join failed (caller may path the temp caravan home).
        /// </summary>
        private static int TryJoinRecruitCaravanIntoExisting(Caravan recruitCaravan, Caravan existing)
        {
            if (recruitCaravan == null || recruitCaravan.Destroyed) return 0;
            if (existing == null || existing.Destroyed) return 0;
            if (!existing.IsPlayerControlled || !recruitCaravan.IsPlayerControlled) return 0;
            if (existing.Faction != Faction.OfPlayer || recruitCaravan.Faction != Faction.OfPlayer) return 0;
            if (existing.Tile != recruitCaravan.Tile) return 0;

            var toMove = new List<Pawn>(recruitCaravan.PawnsListForReading);
            if (toMove.Count == 0) return 0;

            int joined = 0;
            for (int i = 0; i < toMove.Count; i++)
            {
                Pawn p = toMove[i];
                if (p == null || p.Destroyed || p.Dead) continue;
                if (!recruitCaravan.ContainsPawn(p)) continue;

                recruitCaravan.RemovePawn(p);
                existing.AddPawn(p, true);
                if (existing.ContainsPawn(p) || existing.PawnsListForReading.Contains(p))
                    joined++;
                else if (!recruitCaravan.Destroyed && !recruitCaravan.ContainsPawn(p))
                    recruitCaravan.AddPawn(p, true);
            }

            if (joined <= 0 || existing.Destroyed)
                return 0;

            TransferRemainingItems(recruitCaravan, existing);

            if (!recruitCaravan.Destroyed
                && recruitCaravan.PawnsListForReading.Count == 0
                && (recruitCaravan.GetDirectlyHeldThings() == null || recruitCaravan.GetDirectlyHeldThings().Count == 0))
            {
                recruitCaravan.Destroy();
            }

            return existing.Destroyed ? 0 : joined;
        }

        private static void TransferRemainingItems(Caravan from, Caravan to)
        {
            if (from == null || from.Destroyed || to == null || to.Destroyed) return;
            ThingOwner directlyHeld = from.GetDirectlyHeldThings();
            if (directlyHeld == null || directlyHeld.Count == 0) return;

            var items = new List<Thing>(directlyHeld.Count);
            for (int i = 0; i < directlyHeld.Count; i++)
            {
                Thing t = directlyHeld[i];
                if (t != null && !(t is Pawn))
                    items.Add(t);
            }

            for (int i = 0; i < items.Count; i++)
            {
                Thing t = items[i];
                if (t == null || t.Destroyed) continue;
                if (!directlyHeld.Contains(t)) continue;
                directlyHeld.Remove(t);
                to.AddPawnOrItem(t, false);
            }
        }

        private static Caravan FindCaravan(int caravanId)
        {
            if (caravanId < 0) return null;
            var caravans = Find.WorldObjects?.Caravans;
            if (caravans == null) return null;
            for (int i = 0; i < caravans.Count; i++)
            {
                var c = caravans[i];
                if (c != null && !c.Destroyed && c.IsPlayerControlled && c.ID == caravanId) return c;
            }
            return null;
        }

        public struct ConquestGiftFactionOption
        {
            public Faction Faction;
            /// <summary>True when the candidate is allied to the conquered faction (shown greyed out; cannot gift).</summary>
            public bool BlockedAlliedToConquered;
        }

        /// <summary>
        /// Player allies/neutrals that can own a surface settlement.
        /// Includes factions allied to the conquered faction (marked <see cref="ConquestGiftFactionOption.BlockedAlliedToConquered"/>)
        /// so the gift dialog can explain why they cannot receive the ruins.
        /// </summary>
        public static List<ConquestGiftFactionOption> GetGiftFactionOptions(Faction conqueredFaction)
        {
            var result = new List<ConquestGiftFactionOption>();
            Faction player = Faction.OfPlayerSilentFail;
            if (player == null || Find.FactionManager == null) return result;

            var all = Find.FactionManager.AllFactionsVisible;
            foreach (Faction f in all)
            {
                if (f == null || f.IsPlayer || f.defeated || f.Hidden) continue;
                if (f == conqueredFaction) continue;
                if (!CanReceiveSurfaceSettlementGift(f)) continue;
                FactionRelationKind playerRel = WorldActions_Utils.SafeRelationKindWith(f, player);
                if (playerRel != FactionRelationKind.Ally && playerRel != FactionRelationKind.Neutral) continue;
                bool blocked = conqueredFaction != null
                    && WorldActions_Utils.SafeRelationKindWith(f, conqueredFaction) == FactionRelationKind.Ally;
                result.Add(new ConquestGiftFactionOption
                {
                    Faction = f,
                    BlockedAlliedToConquered = blocked
                });
            }

            result.Sort((a, b) => a.BlockedAlliedToConquered.CompareTo(b.BlockedAlliedToConquered));
            return result;
        }

        /// <summary>Player allies and neutrals that may actually receive the gift (not allied to the conquered faction).</summary>
        public static List<Faction> GetEligibleGiftFactions(Faction conqueredFaction)
        {
            var options = GetGiftFactionOptions(conqueredFaction);
            var result = new List<Faction>(options.Count);
            for (int i = 0; i < options.Count; i++)
            {
                if (!options[i].BlockedAlliedToConquered && options[i].Faction != null)
                    result.Add(options[i].Faction);
            }
            return result;
        }

        private static bool CanReceiveSurfaceSettlementGift(Faction faction)
        {
            return WorldActions_Utils.FactionAllowsSurfaceSettlements(faction);
        }

        /// <summary>Replaces the ruins with a fresh settlement of the same tier owned by the chosen ally, and improves goodwill with that ally.</summary>
        public static void GiveSettlementToAlly(int tile, int ruinsId, SettlementTier tier, Faction ally)
        {
            if (ally == null) return;
            if (Find.WorldObjects.AnySettlementAt(tile)) return;

            ConquestOpportunityUtility.DestroyConquestRuinsAt(tile, ruinsId);

            Settlement settlement = (Settlement)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);
            settlement.Tile = tile;
            settlement.SetFaction(ally);
            settlement.Name = SettlementNameGenerator.GenerateSettlementName(settlement);
            Find.WorldObjects.Add(settlement);

            var comp = settlement.GetComponent<CompViralSpread>();
            if (comp != null)
            {
                comp.SetState(tier);
                comp.CheckTierUpdate();
            }
            Find.World?.GetComponent<Text_WorldTierOnSettlements>()?.NotifyTierLabelCacheDirty();

            int goodwillGain = WorldDominationMod.settings?.GetConquestAllyGiftGoodwill(tier) ?? WorldDominationSettings.DefConquestAllyGiftGoodwillT1;
            if (goodwillGain > 0)
                GoodwillChangeNotifier.NotifyConquestGift(ally, settlement, goodwillGain);
            else
                Messages.Message("TSA_WD_Conquest_AllyGiftDone".Translate(ally.Name ?? ally.def.LabelCap, settlement.Label), settlement, MessageTypeDefOf.PositiveEvent);
        }
    }
}
