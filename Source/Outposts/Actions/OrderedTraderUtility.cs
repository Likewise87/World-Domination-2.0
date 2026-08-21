using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    public static class OrderedTraderUtility
    {
        public const int GoodwillFloor = 10;

        public static int GetOrderCost()
        {
            var seth = WorldDominationMod.settings;
            return seth != null ? Mathf.Max(0, seth.orderedTraderGoodwillCost) : WorldDominationSettings.DefOrderedTraderGoodwillCost;
        }

        public static bool CanShowOrderTraderGizmo(Settlement settlement, out string disabledReason)
        {
            disabledReason = null;
            if (settlement == null || settlement.Destroyed || settlement.Faction == null || settlement.Faction.IsPlayer)
                return false;
            if (WorldActions_Utils.IsExcludedFaction(settlement.Faction))
                return false;
            if (!PlanetSurfaceWorldActions.IsPlanetSurfaceWorldObjectForWorldActions(settlement))
                return false;

            Faction player = Faction.OfPlayerSilentFail;
            FactionRelationKind kind = WorldActions_Utils.SafeRelationKindWith(settlement.Faction, player);
            if (kind != FactionRelationKind.Ally && kind != FactionRelationKind.Neutral)
                return false;

            if (settlement.Faction.def?.caravanTraderKinds == null || settlement.Faction.def.caravanTraderKinds.Count == 0)
                return false;

            var comp = settlement.GetComponent<CompViralSpread>();
            if (comp == null) return false;

            Settlement dest = FindNearestPlayerColonyWithMap(settlement.Tile);
            if (dest == null)
            {
                disabledReason = "TSA_WD_OrderedTrader_NoColony".Translate();
                return true;
            }

            if (comp.IsTraderOnCooldown)
            {
                float daysLeft = (comp.traderCooldownTick - Find.TickManager.TicksGame) / 60000f;
                disabledReason = "TSA_WD_OnCooldown".Translate(daysLeft.ToString("F1"));
                return true;
            }

            var destComp = dest.GetComponent<CompViralSpread>();
            if (destComp != null && destComp.IsPlayerColonyWdTraderTargetOnCooldown)
            {
                float daysLeft = (destComp.playerColonyWdTraderCooldownTick - Find.TickManager.TicksGame) / 60000f;
                disabledReason = "TSA_WD_OrderedTrader_ColonyOnCooldown".Translate(daysLeft.ToString("F1"));
                return true;
            }

            int cost = GetOrderCost();
            if (cost > 0 && !GoodwillChangeNotifier.CanPayOrderedRoadCost(settlement.Faction, cost, GoodwillFloor))
                disabledReason = "TSA_WD_OrderedTrader_DisabledGoodwill".Translate(
                    cost,
                    GoodwillChangeNotifier.GetPlayerGoodwill(settlement.Faction),
                    GoodwillFloor);

            return true;
        }

        public static Settlement FindNearestPlayerColonyWithMap(int fromTile)
        {
            Settlement best = null;
            float bestDist = float.MaxValue;
            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            List<Settlement> settlements = Find.WorldObjects.Settlements;
            for (int i = 0; i < settlements.Count; i++)
            {
                Settlement s = settlements[i];
                if (s == null || s.Destroyed || !s.Spawned) continue;
                if (s.Faction == null || !s.Faction.IsPlayer) continue;
                if (!s.HasMap) continue;
                if (!WorldActions_TraderCaravan.IsValidTraderDestination(s)) continue;

                float dist = manager != null
                    ? WorldActions_Utils.GetDistance(fromTile, s.Tile, manager)
                    : Find.WorldGrid.ApproxDistanceInTiles(fromTile, s.Tile);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = s;
                }
            }
            return best;
        }

        public static List<TraderKindDef> GetTraderKinds(Faction faction)
        {
            var result = new List<TraderKindDef>();
            if (faction?.def?.caravanTraderKinds == null) return result;
            for (int i = 0; i < faction.def.caravanTraderKinds.Count; i++)
            {
                TraderKindDef kind = faction.def.caravanTraderKinds[i];
                if (kind != null) result.Add(kind);
            }
            return result;
        }

        /// <summary>Spawns a WD trader traveler; does not deduct settlement strength. Applies sender + colony trader CDs.</summary>
        public static bool LaunchPlayerOrderedTrader(Settlement sender, TraderKindDef kind, Settlement dest)
        {
            if (sender == null || kind == null || dest == null) return false;
            var senderComp = sender.GetComponent<CompViralSpread>();
            if (senderComp == null) return false;

            var def = DefDatabase<WorldObjectDef>.GetNamedSilentFail("TSA_WD_Traveler_Trader");
            if (def == null) return false;

            var seth = WorldDominationMod.settings;
            float payload = Mathf.Max(1f, seth?.traderCaravanCostStrength ?? 100f);

            senderComp.traderCooldownTick = Find.TickManager.TicksGame
                + CompViralSpread.CooldownTicksFromDays(seth?.cooldownTraderDays ?? WorldDominationSettings.DefCdTraderDays);

            if ((seth?.cooldownPlayerColonyTraderDays ?? 0f) > 0f)
            {
                var destComp = dest.GetComponent<CompViralSpread>();
                if (destComp != null)
                {
                    destComp.playerColonyWdTraderCooldownTick = Find.TickManager.TicksGame
                        + CompViralSpread.CooldownTicksFromDays(seth.cooldownPlayerColonyTraderDays);
                }
            }

            var traveler = (WorldObject_Traveler)WorldObjectMaker.MakeWorldObject(def);
            traveler.Tile = sender.Tile;
            traveler.SetFaction(sender.Faction);
            traveler.mission = TravelerMission.Trader;
            traveler.originObject = sender;
            traveler.targetObject = dest;
            traveler.travelerStrength = payload;
            traveler.initialStrength = payload;
            traveler.playerOrderedTrader = true;
            traveler.orderedTraderKind = kind;

            Find.WorldObjects.Add(traveler);
            traveler.pather.StartPath(PlanetSurfaceWorldActions.PlanetTileForWdTravel(dest.Tile, sender));

            var manager = Find.World.GetComponent<WorldComponent_SpreadManager>();
            manager?.AddLog(new SpreadLogEntry(
                "TSA_WD_Log_OrderedTraderLaunched".Translate(sender.LabelCap, dest.LabelCap, kind.LabelCap),
                sender,
                dest));
            return true;
        }
    }
}
