using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Shared settlement spawn / tier / strength edits for World Setup and debug tools.</summary>
    public static class WD_SettlementEditUtility
    {
        public static bool TryGetValidSettlementTile(GlobalTargetInfo target, out int tile, bool enforceMinDistance, out string failReason)
        {
            tile = -1;
            failReason = null;
            if (!target.IsValid)
            {
                failReason = "TSA_WD_WorldSetup_InvalidTile".Translate();
                return false;
            }

            tile = target.Tile;
            if (tile < 0 || !PlanetSurfaceWorldActions.IsPlanetSurfaceTileForWorldActions(tile))
            {
                failReason = "TSA_WD_WorldSetup_InvalidTile".Translate();
                return false;
            }

            if (Find.WorldObjects != null && Find.WorldObjects.AnyWorldObjectAt(tile))
            {
                failReason = "TSA_WD_WorldSetup_TileOccupied".Translate(tile);
                return false;
            }

            if (!TileFinder.IsValidTileForNewSettlement(tile))
            {
                failReason = "TSA_WD_WorldSetup_TileInvalidSettlement".Translate(tile);
                return false;
            }

            if (enforceMinDistance && !Outpost_EstablishmentRequirements.MeetsMinDistanceOnly(tile, out string minReason))
            {
                failReason = minReason ?? "TSA_WD_WorldSetup_TooClose".Translate();
                return false;
            }

            return true;
        }

        public static bool IsValidSettlementTile(int tile, bool enforceMinDistance)
        {
            if (tile < 0) return false;
            if (!PlanetSurfaceWorldActions.IsPlanetSurfaceTileForWorldActions(tile)) return false;
            if (Find.WorldObjects != null && Find.WorldObjects.AnyWorldObjectAt(tile)) return false;
            if (!TileFinder.IsValidTileForNewSettlement(tile)) return false;
            if (enforceMinDistance && !Outpost_EstablishmentRequirements.MeetsMinDistanceOnly(tile, out _))
                return false;
            return true;
        }

        public static bool TrySpawnWdSettlementAt(
            int tile,
            SettlementTier tier,
            Faction faction,
            out string failReason,
            bool enforceMinDistance = true,
            bool deferSideEffects = false)
        {
            failReason = null;
            if (faction == null)
            {
                failReason = "TSA_WD_WorldSetup_FactionMissing".Translate();
                return false;
            }

            if (Find.WorldObjects == null)
            {
                failReason = "TSA_WD_WorldSetup_WorldMissing".Translate();
                return false;
            }

            if (!IsValidSettlementTile(tile, enforceMinDistance))
            {
                if (enforceMinDistance && !Outpost_EstablishmentRequirements.MeetsMinDistanceOnly(tile, out string minReason))
                    failReason = minReason ?? "TSA_WD_WorldSetup_TooClose".Translate();
                else
                    failReason = "TSA_WD_WorldSetup_TileInvalidSettlement".Translate(tile);
                return false;
            }

            Settlement newS = (Settlement)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);
            newS.Tile = tile;
            newS.SetFaction(faction);
            newS.Name = SettlementNameGenerator.GenerateSettlementName(newS);
            Find.WorldObjects.Add(newS);

            var comp = newS.GetComponent<CompViralSpread>();
            if (comp != null)
            {
                comp.SetState(tier);
                FloatRange range = CompViralSpread.GetStrengthRange(tier);
                comp.strength = range.min;
                comp.CheckTierUpdate(false);
            }

            if (!deferSideEffects)
            {
                Outpost_EstablishmentRequirements.InvalidateNearbyCountCache();
                Find.World.GetComponent<WorldComponent_SpreadManager>()?.Notify_WeightsChanged();
            }

            return true;
        }

        public static bool TryAdjustTierAtTile(int tile, int delta, out string message)
        {
            message = null;
            WorldObject obj = FindSettlementOrOutpostAt(tile);
            if (obj == null) return false;
            var comp = obj.GetComponent<CompViralSpread>();
            if (comp == null) return false;

            SettlementTier next = comp.tier;
            if (delta > 0)
            {
                if (comp.tier == SettlementTier.T1) next = SettlementTier.T2;
                else if (comp.tier == SettlementTier.T2) next = SettlementTier.T3;
                else if (comp.tier == SettlementTier.T3) next = SettlementTier.T4;
            }
            else if (delta < 0)
            {
                if (comp.tier == SettlementTier.T4) next = SettlementTier.T3;
                else if (comp.tier == SettlementTier.T3) next = SettlementTier.T2;
                else if (comp.tier == SettlementTier.T2) next = SettlementTier.T1;
            }

            if (next == comp.tier) return false;
            comp.SetState(next);
            comp.strength = CompViralSpread.GetStrengthRange(comp.tier).min;
            Find.World.GetComponent<WorldComponent_SpreadManager>()?.Notify_WeightsChanged();
            message = "TSA_WD_WorldSetup_TierChanged".Translate(obj.LabelCap, comp.tier.ToString());
            return true;
        }

        public static bool TryAdjustStrengthAtTile(int tile, float delta, out string message)
        {
            message = null;
            WorldObject obj = FindSettlementOrOutpostAt(tile);
            if (obj == null) return false;
            var comp = obj.GetComponent<CompViralSpread>();
            if (comp == null) return false;
            comp.AdjustStrengthWithinTier(delta);
            Find.World.GetComponent<WorldComponent_SpreadManager>()?.Notify_WeightsChanged();
            message = "TSA_WD_WorldSetup_StrengthChanged".Translate(obj.LabelCap, comp.strength.ToString("F0"), comp.tier.ToString());
            return true;
        }

        public static bool TryRemoveNpcSettlementAtTile(int tile, out string message)
        {
            message = null;
            if (tile < 0 || Find.WorldObjects == null) return false;

            Settlement settlement = null;
            foreach (WorldObject obj in Find.WorldObjects.ObjectsAt(tile))
            {
                if (obj is Settlement s && !s.Destroyed)
                {
                    settlement = s;
                    break;
                }
            }

            if (settlement == null) return false;
            if (settlement.Faction != null && settlement.Faction.IsPlayer)
            {
                message = "TSA_WD_WorldSetup_RemoveSettlementPlayer".Translate();
                return false;
            }
            if (!WD_SettlementLayoutUtility.IsRecreateTargetSettlement(settlement))
                return false;

            string name = settlement.LabelCap;
            settlement.Destroy();
            Outpost_EstablishmentRequirements.InvalidateNearbyCountCache();
            Find.World.GetComponent<WorldComponent_SpreadManager>()?.Notify_WeightsChanged();
            message = "TSA_WD_WorldSetup_RemoveSettlementDone".Translate(name);
            return true;
        }

        public static WorldObject FindSettlementOrOutpostAt(int tile)
        {
            if (tile < 0 || Find.WorldObjects == null) return null;
            return Find.WorldObjects.ObjectsAt(tile).FirstOrDefault(x => x is Settlement || x is WorldObject_WD_Outpost);
        }
    }
}
