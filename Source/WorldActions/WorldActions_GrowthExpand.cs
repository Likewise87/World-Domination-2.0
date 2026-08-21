using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;
using UnityEngine;

namespace TSA_WorldDomination
{
    public static class WorldActions_GrowthExpand
    {
        public const float DevelopNearCapFraction = 0.95f;

        /// <summary>Silent daily offensive gain for NPC settlements (no action log).</summary>
        public static void ApplyPassiveOffensiveGrowth(CompViralSpread comp)
        {
            if (comp == null || !comp.IsSettlement || comp.IsOutpost) return;
            WorldObject parent = comp.parent;
            if (parent?.Faction == null || parent.Faction.IsPlayer) return;

            var seth = WorldDominationMod.settings;
            if (seth == null) return;

            FloatRange band = CompViralSpread.GetStrengthRange(comp.tier);
            float tierMax = band.max;
            float oldStr = comp.strength;
            if (oldStr >= tierMax - 0.01f) return;

            float buffMult = 1f;
            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            if (manager != null
                && parent.Faction == manager.currentWeakestUnderdog
                && Find.TickManager.TicksGame < manager.underdogBuffExpiryTick)
            {
                buffMult = seth.underdogGrowthGainMult;
            }

            float gain = seth.GetPassiveGrowthAmount(comp.tier) * buffMult;
            if (manager != null && WdEscalation.IsMidOrLate(manager))
                gain *= manager.GetLateGameGrowthMultiplier();

            if (gain <= 0f) return;
            comp.strength = Mathf.Min(oldStr + gain, tierMax);
        }

        public static bool IsDevelopEligible(CompViralSpread comp)
        {
            if (comp == null || !comp.IsSettlement || comp.IsOutpost) return false;
            float tierMax = CompViralSpread.GetStrengthRange(comp.tier).max;
            return comp.strength >= tierMax * DevelopNearCapFraction;
        }

        /// <summary>
        /// At-cap Develop: tier upgrade if neighbors allow, else expansion (no fail roll; expand CD only).
        /// </summary>
        public static bool AttemptDevelop(WorldObject s, CompViralSpread comp, WorldComponent_SpreadManager manager)
        {
            if (s == null || comp == null || manager == null) return false;
            if (!IsDevelopEligible(comp)) return false;

            var seth = WorldDominationMod.settings;
            WDVerbose.Msg($"AttemptDevelop: {s.LabelCap} tier={comp.tier} str={comp.strength:F0}");

            if (TryUpgrade(s, comp, manager, seth))
                return true;

            if (!comp.IsExpansionOnCooldown)
                return ExecuteExpansion(s as Settlement, comp, manager);

            WDVerbose.Msg($"AttemptDevelop: {s.LabelCap} expand on cooldown");
            manager.AddLog(new SpreadLogEntry(
                "TSA_WD_Log_Grow_NoChange_ExpandCooldown".Translate(s.LabelCap), s));
            return false;
        }

        /// <summary>Legacy name kept for any remaining callers; redirects to Develop.</summary>
        public static bool AttemptGrow(WorldObject s, CompViralSpread comp, WorldComponent_SpreadManager manager)
            => AttemptDevelop(s, comp, manager);

        private static bool TryUpgrade(
            WorldObject s,
            CompViralSpread comp,
            WorldComponent_SpreadManager manager,
            WorldDominationSettings seth)
        {
            if (comp.tier == SettlementTier.T4) return false;

            int sameTierNeighbors = 0;
            int t2Count = 0;
            int t3Count = 0;
            int t4Count = 0;

            if (manager.TryGetFactionSettlements(s.Faction, out var facSettlements) && facSettlements != null)
            {
                for (int ni = 0; ni < facSettlements.Count; ni++)
                {
                    var n = facSettlements[ni];
                    if (n == s || !DailyWorldSnapshot.IsSettlementStillValid(n)) continue;
                    if (Find.WorldGrid.ApproxDistanceInTiles(s.Tile, n.Tile) > seth.expandMaxRadius) continue;
                    var nComp = n.GetComponent<CompViralSpread>();
                    if (nComp == null) continue;
                    if (nComp.tier == comp.tier) sameTierNeighbors++;
                    if (nComp.tier == SettlementTier.T2) t2Count++;
                    if (nComp.tier == SettlementTier.T3) t3Count++;
                    if (nComp.tier == SettlementTier.T4) t4Count++;
                }
            }
            else
            {
                var allNeighborSettlements = Find.WorldObjects.Settlements;
                for (int ni = 0; ni < allNeighborSettlements.Count; ni++)
                {
                    var n = allNeighborSettlements[ni];
                    if (n.Faction != s.Faction || n == s || Find.WorldGrid.ApproxDistanceInTiles(s.Tile, n.Tile) > seth.expandMaxRadius) continue;
                    var nComp = n.GetComponent<CompViralSpread>();
                    if (nComp == null) continue;
                    if (nComp.tier == comp.tier) sameTierNeighbors++;
                    if (nComp.tier == SettlementTier.T2) t2Count++;
                    if (nComp.tier == SettlementTier.T3) t3Count++;
                    if (nComp.tier == SettlementTier.T4) t4Count++;
                }
            }

            if (sameTierNeighbors < seth.GetSameTierNeighborsRequiredForUpgrade(comp.tier))
                return false;

            bool canUpgrade = (comp.tier == SettlementTier.T1) ? (t2Count < seth.localMaxT2) :
                              (comp.tier == SettlementTier.T2) ? (t3Count < seth.localMaxT3) :
                              (comp.tier == SettlementTier.T3) ? (t4Count < seth.localMaxT4) :
                              false;
            if (!canUpgrade) return false;

            SettlementTier oldTier = comp.tier;
            SettlementTier nextTier = (oldTier == SettlementTier.T1) ? SettlementTier.T2 :
                                     (oldTier == SettlementTier.T2) ? SettlementTier.T3 : SettlementTier.T4;

            comp.SetState(nextTier);
            manager.AddLog(new SpreadLogEntry("TSA_WD_Log_Upgrade".Translate(oldTier.ToString(), comp.tier.ToString(), sameTierNeighbors), s));
            WorldActions_Utils.RefreshMap();
            return true;
        }

        private static bool ExecuteExpansion(Settlement parent, CompViralSpread parentComp, WorldComponent_SpreadManager manager)
        {
            if (parent == null || parentComp == null) return false;
            var seth = WorldDominationMod.settings;

            if (!PlanetSurfaceWorldActions.IsPlanetSurfaceWorldObjectForWorldActions(parent))
            {
                WDVerbose.Msg($"ExecuteExpansion: skip non-surface parent {parent.LabelCap}");
                return false;
            }

            int totalGlobal = 0;
            var globalSettlements = Find.WorldObjects.Settlements;
            for (int i = 0; i < globalSettlements.Count; i++)
            {
                var gs = globalSettlements[i];
                if (gs.Faction != null && !gs.Faction.IsPlayer && !gs.Faction.def.hidden
                    && PlanetSurfaceWorldActions.IsPlanetSurfaceWorldObjectForWorldActions(gs))
                    totalGlobal++;
            }
            if (totalGlobal >= seth.maxSettlements)
            {
                manager.AddLog(new SpreadLogEntry("TSA_WD_Log_Expand_SkippedGlobalCap".Translate(parent.LabelCap, totalGlobal, seth.maxSettlements), parent));
                return false;
            }

            bool biasTowardPlayer = manager != null && WdEscalation.IsMidOrLate(manager);
            int expandTiles = manager != null
                ? WdEscalation.GetExpandTowardPlayerMaxTiles(WorldDominationMod.settings, manager.cachedEscalationStage)
                : 0;
            int minRadius = Mathf.Max(1, seth.expandMinRadius);
            int maxRadius = biasTowardPlayer
                ? Mathf.Min(seth.expandMaxRadius, Mathf.Max(minRadius, expandTiles))
                : seth.expandMaxRadius;
            if (maxRadius < minRadius) maxRadius = minRadius;

            int chosenTile = -1;
            bool hasTile = biasTowardPlayer
                ? TryFindExpandTileTowardPlayer(parent, minRadius, maxRadius, seth, manager, out chosenTile)
                : TryFindExpandTileRandomEarly(parent, minRadius, maxRadius, seth, manager, out chosenTile);

            // Mid/late corridor miss → same cheap random search as early game.
            if (!hasTile && biasTowardPlayer)
                hasTile = TryFindExpandTileRandomEarly(parent, minRadius, maxRadius, seth, manager, out chosenTile);

            if (!hasTile)
            {
                manager.AddLog(new SpreadLogEntry("TSA_WD_Log_Expand_SkippedNoValidTile".Translate(parent.LabelCap), parent));
                return false;
            }

            parentComp.expansionCooldownTick = Find.TickManager.TicksGame + Mathf.RoundToInt(seth.cooldownExpandDays * 60000f);

            float cost = parentComp.strength * 0.25f;
            parentComp.strength -= cost;
            parentComp.CheckTierUpdate(false);

            // Log before pathing so a StartPath failure cannot swallow the dispatch entry.
            manager.AddLog(new SpreadLogEntry("TSA_WD_Log_ExpeditionLaunched".Translate(parent.LabelCap), parent, chosenTile));

            WorldObject_Traveler traveler = (WorldObject_Traveler)WorldObjectMaker.MakeWorldObject(DefDatabase<WorldObjectDef>.GetNamed("TSA_WD_Traveler_Expansion"));
            traveler.Tile = parent.Tile;
            traveler.SetFaction(parent.Faction);
            traveler.mission = TravelerMission.Expansion;
            traveler.travelerStrength = cost;
            traveler.initialStrength = cost;
            traveler.originObject = parent;

            Find.WorldObjects.Add(traveler);
            traveler.pather.StartPath(PlanetSurfaceWorldActions.PlanetTileForWdTravel(chosenTile, parent));
            return true;
        }

        /// <summary>Random annulus seed, then local flood until first valid tile (early / fallback).</summary>
        private const int EarlyRandomSeedAttempts = 5;
        private const int LocalSearchRadiusFromSeed = 6;

        private static bool TryFindExpandTileRandomEarly(
            Settlement parent,
            int minRadius,
            int maxRadius,
            WorldDominationSettings seth,
            WorldComponent_SpreadManager manager,
            out int chosenTile)
        {
            chosenTile = -1;
            if (parent == null) return false;

            for (int attempt = 0; attempt < EarlyRandomSeedAttempts; attempt++)
            {
                if (!TryPickRandomAnnulusSeed(parent.Tile, minRadius, maxRadius, out int seed))
                    continue;
                if (TryFindFirstValidFromSeed(seed, parent, minRadius, maxRadius, seth, manager, out chosenTile))
                    return true;
            }

            // Last resort: BFS from parent, stop at first valid in range.
            return TryFindFirstValidFromSeed(parent.Tile.tileId, parent, minRadius, maxRadius, seth, manager, out chosenTile, searchRadius: maxRadius);
        }

        /// <summary>Line to nearest player anchors; search from player-facing edge; first valid wins. Tries next anchors on miss.</summary>
        private static bool TryFindExpandTileTowardPlayer(
            Settlement parent,
            int minRadius,
            int maxRadius,
            WorldDominationSettings seth,
            WorldComponent_SpreadManager manager,
            out int chosenTile)
        {
            chosenTile = -1;
            if (parent == null) return false;

            CollectPlayerExpandAnchors(parent.Tile.tileId, expandAnchorScratch);
            if (expandAnchorScratch.Count == 0) return false;

            for (int a = 0; a < expandAnchorScratch.Count; a++)
            {
                int anchor = expandAnchorScratch[a];
                if (!TryBuildGreedyLineToward(parent.Tile.tileId, anchor, maxRadius, expandLineScratch))
                    continue;

                // Prefer the player-facing edge of the allowed ring on this line.
                int seed = -1;
                int bestDist = -1;
                for (int i = 0; i < expandLineScratch.Count; i++)
                {
                    int tile = expandLineScratch[i];
                    int d = Mathf.RoundToInt(Find.WorldGrid.ApproxDistanceInTiles(parent.Tile.tileId, tile));
                    if (d < minRadius || d > maxRadius) continue;
                    if (d >= bestDist)
                    {
                        bestDist = d;
                        seed = tile;
                    }
                }

                if (seed < 0) continue;

                // Start at the player-facing edge: seed, then local flood, then walk the line back toward parent.
                if (IsValidExpandTile(seed, parent, minRadius, maxRadius, seth, manager))
                {
                    chosenTile = seed;
                    return true;
                }

                if (TryFindFirstValidFromSeed(seed, parent, minRadius, maxRadius, seth, manager, out chosenTile))
                    return true;

                for (int i = expandLineScratch.Count - 1; i >= 0; i--)
                {
                    int tile = expandLineScratch[i];
                    if (tile == seed) continue;
                    if (IsValidExpandTile(tile, parent, minRadius, maxRadius, seth, manager))
                    {
                        chosenTile = tile;
                        return true;
                    }
                }
            }

            return false;
        }

        private static readonly List<int> expandAnchorScratch = new List<int>(32);
        private static readonly List<int> expandLineScratch = new List<int>(64);
        private static readonly List<int> expandRingScratch = new List<int>(128);
        private static readonly List<PlanetTile> expandNeighborScratch = new List<PlanetTile>(8);

        private static void CollectPlayerExpandAnchors(int fromTileId, List<int> into)
        {
            into.Clear();
            Settlement colony = InfluenceUtils.GetPlayerColony();
            if (colony != null && colony.Tile >= 0)
                into.Add(colony.Tile.tileId);

            Faction playerFaction = Faction.OfPlayerSilentFail;
            if (playerFaction != null && Find.WorldObjects != null)
            {
                var all = Find.WorldObjects.AllWorldObjects;
                for (int i = 0; i < all.Count; i++)
                {
                    if (all[i] is WorldObject_WD_Outpost o && o.Faction == playerFaction
                        && WorldActions_Utils.IsWdSurfaceWorldObject(o) && o.Tile >= 0)
                        into.Add(o.Tile.tileId);
                }
            }

            if (into.Count <= 1) return;

            WorldGrid grid = Find.WorldGrid;
            into.Sort((a, b) =>
                grid.ApproxDistanceInTiles(fromTileId, a).CompareTo(grid.ApproxDistanceInTiles(fromTileId, b)));
        }

        /// <summary>Greedy hex walk toward <paramref name="anchorTileId"/>, capped near <paramref name="maxRadius"/> from start.</summary>
        private static bool TryBuildGreedyLineToward(int startTileId, int anchorTileId, int maxRadius, List<int> lineOut)
        {
            lineOut.Clear();
            WorldGrid grid = Find.WorldGrid;
            PlanetLayer layer = PlanetSurfaceWorldActions.WdSurfaceLayer ?? grid?.Surface;
            if (grid == null || layer == null || startTileId < 0 || anchorTileId < 0) return false;
            if (!grid.InBounds(startTileId) || !grid.InBounds(anchorTileId)) return false;

            int cur = startTileId;
            int maxSteps = Mathf.Max(8, maxRadius * 3);
            for (int step = 0; step < maxSteps; step++)
            {
                float curDistToAnchor = grid.ApproxDistanceInTiles(cur, anchorTileId);
                if (curDistToAnchor <= 0.5f) break;

                expandNeighborScratch.Clear();
                grid.GetTileNeighbors(new PlanetTile(cur, layer), expandNeighborScratch);

                int best = -1;
                float bestDist = curDistToAnchor;
                for (int i = 0; i < expandNeighborScratch.Count; i++)
                {
                    PlanetTile n = expandNeighborScratch[i];
                    if (!n.Valid || !PlanetSurfaceWorldActions.IsPlanetSurfaceTileForWorldActions(n)) continue;
                    float d = grid.ApproxDistanceInTiles(n.tileId, anchorTileId);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        best = n.tileId;
                    }
                }

                if (best < 0 || best == cur) break;
                cur = best;
                lineOut.Add(cur);

                int fromStart = Mathf.RoundToInt(grid.ApproxDistanceInTiles(startTileId, cur));
                if (fromStart >= maxRadius) break;
            }

            return lineOut.Count > 0;
        }

        private static bool TryPickRandomAnnulusSeed(PlanetTile parentTile, int minRadius, int maxRadius, out int seedTileId)
        {
            seedTileId = -1;
            WorldGrid grid = Find.WorldGrid;
            PlanetLayer layer = parentTile.Layer ?? PlanetSurfaceWorldActions.WdSurfaceLayer ?? grid?.Surface;
            if (grid == null || layer == null || !parentTile.Valid) return false;

            int targetDist = Rand.RangeInclusive(minRadius, maxRadius);
            expandRingScratch.Clear();
            layer.Filler.FloodFill(
                parentTile,
                pt => PlanetSurfaceWorldActions.IsPlanetSurfaceTileForWorldActions(pt),
                (PlanetTile pt, int dist) =>
                {
                    if (dist > targetDist) return true;
                    if (dist == targetDist)
                        expandRingScratch.Add(pt.tileId);
                    return false;
                });

            if (expandRingScratch.Count == 0 && targetDist != minRadius)
            {
                targetDist = minRadius;
                expandRingScratch.Clear();
                layer.Filler.FloodFill(
                    parentTile,
                    pt => PlanetSurfaceWorldActions.IsPlanetSurfaceTileForWorldActions(pt),
                    (PlanetTile pt, int dist) =>
                    {
                        if (dist > targetDist) return true;
                        if (dist == targetDist)
                            expandRingScratch.Add(pt.tileId);
                        return false;
                    });
            }

            return expandRingScratch.TryRandomElement(out seedTileId);
        }

        private static bool TryFindFirstValidFromSeed(
            int seedTileId,
            Settlement parent,
            int minRadius,
            int maxRadius,
            WorldDominationSettings seth,
            WorldComponent_SpreadManager manager,
            out int chosenTile,
            int searchRadius = LocalSearchRadiusFromSeed)
        {
            chosenTile = -1;
            if (parent == null || seedTileId < 0) return false;

            if (IsValidExpandTile(seedTileId, parent, minRadius, maxRadius, seth, manager))
            {
                chosenTile = seedTileId;
                return true;
            }

            WorldGrid grid = Find.WorldGrid;
            PlanetLayer layer = PlanetSurfaceWorldActions.WdSurfaceLayer ?? grid?.Surface;
            if (grid == null || layer == null || !grid.InBounds(seedTileId)) return false;

            int found = -1;
            layer.Filler.FloodFill(
                new PlanetTile(seedTileId, layer),
                pt => PlanetSurfaceWorldActions.IsPlanetSurfaceTileForWorldActions(pt),
                (PlanetTile pt, int dist) =>
                {
                    if (dist > searchRadius) return true;
                    if (IsValidExpandTile(pt.tileId, parent, minRadius, maxRadius, seth, manager))
                    {
                        found = pt.tileId;
                        return true;
                    }
                    return false;
                });

            if (found < 0) return false;
            chosenTile = found;
            return true;
        }

        private static bool IsValidExpandTile(
            int tileId,
            Settlement parent,
            int minRadius,
            int maxRadius,
            WorldDominationSettings seth,
            WorldComponent_SpreadManager manager)
        {
            if (parent == null || tileId < 0 || Find.WorldGrid == null) return false;
            if (!Find.WorldGrid.InBounds(tileId)) return false;

            int d = Mathf.RoundToInt(Find.WorldGrid.ApproxDistanceInTiles(parent.Tile.tileId, tileId));
            if (d < minRadius || d > maxRadius) return false;
            if (!TileFinder.IsValidTileForNewSettlement(tileId)) return false;
            if (Find.WorldObjects.AnyWorldObjectAt(tileId)) return false;
            if (Outpost_EstablishmentRequirements.IsTileBlockedByMinDistanceCached(tileId)) return false;
            if (IsTargetSaturated(tileId, parent.Faction, seth, manager)) return false;
            return true;
        }

        public static bool IsTargetSaturated(int tileId, Faction faction, WorldDominationSettings seth, WorldComponent_SpreadManager manager = null)
        {
            int nearbyT1 = 0;
            float checkRadius = seth.expandMaxRadius;

            if (manager != null && manager.TryGetFactionSettlements(faction, out var facList) && facList != null)
            {
                for (int i = 0; i < facList.Count; i++)
                {
                    var s = facList[i];
                    if (!DailyWorldSnapshot.IsSettlementStillValid(s)) continue;
                    if (Find.WorldGrid.ApproxDistanceInTiles(tileId, s.Tile) > checkRadius) continue;
                    var comp = s.GetComponent<CompViralSpread>();
                    if (comp != null && comp.tier == SettlementTier.T1)
                    {
                        nearbyT1++;
                        if (nearbyT1 >= seth.localMaxT1) return true;
                    }
                }
                return false;
            }

            var settlements = Find.WorldObjects.Settlements;
            for (int i = 0; i < settlements.Count; i++)
            {
                var s = settlements[i];
                if (s.Faction != faction) continue;
                if (Find.WorldGrid.ApproxDistanceInTiles(tileId, s.Tile) <= checkRadius)
                {
                    var comp = s.GetComponent<CompViralSpread>();
                    if (comp != null && comp.tier == SettlementTier.T1)
                    {
                        nearbyT1++;
                        if (nearbyT1 >= seth.localMaxT1) return true;
                    }
                }
            }
            return false;
        }
    }
}
