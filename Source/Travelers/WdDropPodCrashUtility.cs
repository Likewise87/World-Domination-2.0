using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Empty-tile crash site for AA-hit player drop pods: map gen, wounded landing, on-map corpses,
    /// reinforcement proxy handoff, and reform-caravan ambush priming.
    /// </summary>
    public static class WdDropPodCrashUtility
    {
        public const int TileSearchRadius = 8;
        public const float KillGivenHitChance = 0.25f;

        private static readonly HashSet<int> ScratchVisited = new HashSet<int>();
        private static readonly Queue<int> ScratchQueue = new Queue<int>();
        private static readonly List<PlanetTile> ScratchNeighbors = new List<PlanetTile>();

        public static Settlement FindReinforcementProxy(WorldObject aaOrigin, int nearTileId)
        {
            if (aaOrigin is Settlement s
                && s.Faction != null
                && !s.Faction.IsPlayer
                && WorldActions_Utils.SafeHostileTo(s.Faction, Faction.OfPlayer)
                && s.GetComponent<CompViralSpread>() != null)
            {
                return s;
            }

            Settlement best = null;
            int bestDist = int.MaxValue;
            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            List<Settlement> all = Find.WorldObjects.Settlements;
            for (int i = 0; i < all.Count; i++)
            {
                Settlement cand = all[i];
                if (cand == null || cand.Destroyed || cand.Faction == null || cand.Faction.IsPlayer) continue;
                if (!WorldActions_Utils.SafeHostileTo(cand.Faction, Faction.OfPlayer)) continue;
                if (cand.GetComponent<CompViralSpread>() == null) continue;
                int dist = WorldActions_Utils.GetDistance(nearTileId, cand.Tile.tileId, manager);
                if (dist < 0 || dist >= bestDist) continue;
                bestDist = dist;
                best = cand;
            }
            return best;
        }

        public static bool TryFindCrashTile(int preferTileId, out int crashTileId)
        {
            crashTileId = -1;
            if (preferTileId < 0 || !Find.WorldGrid.InBounds(preferTileId)) return false;
            if (IsTileFreeForCrashSite(preferTileId))
            {
                crashTileId = preferTileId;
                return true;
            }

            ScratchVisited.Clear();
            ScratchQueue.Clear();
            ScratchVisited.Add(preferTileId);
            ScratchQueue.Enqueue(preferTileId);
            int explored = 0;
            int maxExplore = 1 + TileSearchRadius * TileSearchRadius * 6;

            while (ScratchQueue.Count > 0 && explored < maxExplore)
            {
                int tile = ScratchQueue.Dequeue();
                explored++;
                var mgr = Find.World?.GetComponent<WorldComponent_SpreadManager>();
                int dist = mgr != null ? WorldActions_Utils.GetDistance(preferTileId, tile, mgr) : Mathf.RoundToInt(Find.WorldGrid.ApproxDistanceInTiles(preferTileId, tile));
                if (dist < 0 || dist > TileSearchRadius) continue;

                ScratchNeighbors.Clear();
                Find.WorldGrid.GetTileNeighbors(tile, ScratchNeighbors);
                for (int i = 0; i < ScratchNeighbors.Count; i++)
                {
                    int n = ScratchNeighbors[i].tileId;
                    if (!ScratchVisited.Add(n)) continue;
                    if (!Find.WorldGrid.InBounds(n)) continue;
                    int nd = mgr != null ? WorldActions_Utils.GetDistance(preferTileId, n, mgr) : Mathf.RoundToInt(Find.WorldGrid.ApproxDistanceInTiles(preferTileId, n));
                    if (nd < 0 || nd > TileSearchRadius) continue;
                    if (IsTileFreeForCrashSite(n))
                    {
                        crashTileId = n;
                        return true;
                    }
                    ScratchQueue.Enqueue(n);
                }
            }
            return false;
        }

        public static bool IsTileFreeForCrashSite(int tileId)
        {
            if (tileId < 0 || !Find.WorldGrid.InBounds(tileId)) return false;
            if (Find.World.Impassable(tileId)) return false;

            PlanetTile pt = PlanetSurfaceWorldActions.PlanetTileForWdTravel(tileId, null);
            foreach (WorldObject wo in Find.WorldObjects.ObjectsAt(pt))
            {
                if (wo == null || wo.Destroyed) continue;
                if (wo is MapParent) return false;
                if (wo is Settlement) return false;
                if (wo is WorldObject_WD_Outpost) return false;
                if (wo is Caravan) return false;
                if (wo is WorldObject_AT_Turret) return false;
                if (wo is WorldObject_Traveler) return false;
            }
            return Find.WorldObjects.MapParentAt(pt) == null;
        }

        /// <summary>
        /// Create crash site, land wounded survivors and corpses. Returns site (with map) or null if tile/map failed
        /// (caller should virtual-kill everyone in both lists).
        /// </summary>
        public static WorldObject_WD_DropPodCrashSite TryCreateAndLand(
            int preferTileId,
            Settlement proxy,
            List<Pawn> crashed,
            List<Pawn> killed)
        {
            if ((crashed == null || crashed.Count == 0) && (killed == null || killed.Count == 0))
                return null;
            if (!TryFindCrashTile(preferTileId, out int crashTile))
                return null;

            var def = DefDatabase<WorldObjectDef>.GetNamedSilentFail("TSA_WD_DropPodCrashSite");
            if (def == null)
            {
                Log.Error("[TSA World Domination] Missing WorldObjectDef TSA_WD_DropPodCrashSite.");
                return null;
            }

            var site = (WorldObject_WD_DropPodCrashSite)WorldObjectMaker.MakeWorldObject(def);
            site.Tile = PlanetSurfaceWorldActions.PlanetTileForWdTravel(crashTile, null);
            site.reinforcementProxy = proxy;
            if (proxy?.Faction != null)
                site.SetFaction(proxy.Faction);
            Find.WorldObjects.Add(site);

            int pawnCount = (crashed?.Count ?? 0) + (killed?.Count ?? 0);
            int side = Mathf.Clamp(75 + pawnCount * 4, 75, 200);
            IntVec3 mapSize = new IntVec3(side, 1, side);

            Map map;
            try
            {
                map = GetOrGenerateMapUtility.GetOrGenerateMap(site.Tile, mapSize, def);
            }
            catch (System.Exception e)
            {
                Log.Error($"[TSA World Domination] Drop pod crash map gen failed: {e}");
                if (!site.Destroyed) site.Destroy();
                return null;
            }

            if (map == null)
            {
                if (!site.Destroyed) site.Destroy();
                return null;
            }

            LandCrashed(map, crashed);
            LandKilledAsCorpses(map, killed);

            var timer = map.GetComponent<MapComponent_ReinforcementTimer>();
            timer?.InitializeReinforcementsForCrashSite(proxy);

            CameraJumper.TryJump(site);
            return site;
        }

        private static void LandCrashed(Map map, List<Pawn> crashed)
        {
            if (crashed == null || crashed.Count == 0 || map == null) return;
            IntVec3 cell = FindDropCell(map);
            var things = new List<Thing>(crashed.Count);
            for (int i = 0; i < crashed.Count; i++)
            {
                Pawn p = crashed[i];
                if (p == null || p.Destroyed || p.Dead) continue;
                things.Add(p);
            }
            if (things.Count == 0) return;
            DropPodUtility.DropThingsNear(cell, map, things);
            for (int i = 0; i < crashed.Count; i++)
            {
                Pawn p = crashed[i];
                if (p == null || p.Dead || p.Destroyed) continue;
                WD_OutpostDefenseSkirmishUtility.ApplySkirmishInjuries(p);
            }
        }

        private static void LandKilledAsCorpses(Map map, List<Pawn> killed)
        {
            if (killed == null || killed.Count == 0 || map == null) return;
            IntVec3 cell = FindDropCell(map);
            for (int i = 0; i < killed.Count; i++)
            {
                Pawn p = killed[i];
                if (p == null || p.Destroyed) continue;
                if (p.Dead)
                {
                    if (!p.Spawned && p.Corpse != null && !p.Corpse.Spawned)
                        GenSpawn.Spawn(p.Corpse, CellFinder.RandomClosewalkCellNear(cell, map, 6), map);
                    continue;
                }
                GenSpawn.Spawn(p, CellFinder.RandomClosewalkCellNear(cell, map, 6), map);
                p.Kill(null);
            }
        }

        private static IntVec3 FindDropCell(Map map)
        {
            IntVec3 cell;
            if (CellFinderLoose.TryGetRandomCellWith(
                    c => c.InBounds(map) && c.Standable(map) && !c.Fogged(map), map, 1000, out cell))
                return cell;
            return DropCellFinder.TradeDropSpot(map);
        }

        public static void VirtualKillAll(List<Pawn> pawns, List<string> killedNames)
        {
            if (pawns == null) return;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                if (p == null || p.Destroyed || p.Dead) continue;
                string name = p.LabelShortCap;
                p.Kill(null);
                if (p.Dead && killedNames != null)
                    killedNames.Add(name);
            }
        }

        public static void NotifyCrashEvacCaravanIfFromCrashMap(Caravan caravan)
        {
            if (caravan == null || caravan.Destroyed || caravan.Faction?.IsPlayer != true) return;
            Map map = Find.CurrentMap;
            if (map?.Parent is WorldObject_WD_DropPodCrashSite)
            {
                WorldComponent_DropPodCrashEvac.Get()?.Prime(caravan);
                SettlementAmbushUtility.TryCheckAmbushForCaravan(caravan, caravan.Tile.tileId);
            }
        }
    }

    [HarmonyPatch(typeof(CaravanMaker), nameof(CaravanMaker.MakeCaravan))]
    public static class Patch_CaravanMaker_DropPodCrashEvacPrime
    {
        public static void Postfix(Caravan __result)
        {
            WdDropPodCrashUtility.NotifyCrashEvacCaravanIfFromCrashMap(__result);
        }
    }
}
