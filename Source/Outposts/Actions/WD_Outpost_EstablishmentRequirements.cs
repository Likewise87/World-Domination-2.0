using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Min distance, tile requirements (fertile/animals/etc.), and build cost for establishing a WD outpost.</summary>
    public static class Outpost_EstablishmentRequirements
    {
        /// <summary>Minimum tiles from any settlement, player colony, or other WD outpost. From mod settings.</summary>
        public static int MinDistanceTiles => WorldDominationMod.settings?.outpostMinDistanceTiles ?? 2;

        public static bool EnforceBiome => WorldDominationMod.settings?.outpostReqBiome ?? true;
        public static bool EnforceFertility => WorldDominationMod.settings?.outpostReqFertility ?? true;
        public static bool EnforceAnimalAbundance => WorldDominationMod.settings?.outpostReqAnimalAbundance ?? true;
        public static bool EnforceFishAbundance => WorldDominationMod.settings?.outpostReqFishAbundance ?? true;
        public static bool EnforceMiningTerrain => WorldDominationMod.settings?.outpostReqMiningTerrain ?? true;
        public static bool EnforceResearch => WorldDominationMod.settings?.outpostReqResearch ?? true;
        public static bool EnforceNearbySettlements => WorldDominationMod.settings?.outpostReqNearbySettlements ?? true;
        public static bool EnforceMinPawns => WorldDominationMod.settings?.outpostReqMinPawns ?? true;
        public static bool EnforceMinSkill => WorldDominationMod.settings?.outpostReqMinSkill ?? true;
        public static bool EnforceCost => WorldDominationMod.settings?.outpostReqCost ?? true;

        /// <summary>
        /// True for world objects that reserve min-distance around their tile for establishment.
        /// Settlements (incl. player colonies), WD outposts, and timed WD raze ruins.
        /// Non-surface / space objects never block (orbit tileIds must not pollute the surface cache).
        /// </summary>
        public static bool IsEstablishmentMinDistanceBlocker(WorldObject o)
        {
            if (o == null || o.Destroyed || !o.Tile.Valid) return false;
            if (WorldActions_Utils.IsSpace(o)) return false;
            return o is Settlement || o is WorldObject_WD_Outpost || o is WorldObject_WdSettlementRuin;
        }

        /// <summary>Vanilla leftover after a temporary caravan camp is abandoned (not WD raze / conquest ruins).</summary>
        public static bool IsVanillaAbandonedCamp(WorldObject o)
            => o != null && !o.Destroyed && o.def == WorldObjectDefOf.AbandonedCamp;

        /// <summary>Live caravan camp map (not abandoned-camp ruins).</summary>
        public static bool IsActiveCamp(WorldObject o)
            => o != null && !o.Destroyed && (o is Camp || o.def == WorldObjectDefOf.Camp);

        public static bool TileHasActiveCamp(int tile)
        {
            if (Find.WorldObjects == null || tile < 0) return false;
            foreach (WorldObject o in Find.WorldObjects.ObjectsAt(tile))
            {
                if (IsActiveCamp(o)) return true;
            }
            return false;
        }

        public static bool TileHasVanillaAbandonedCamp(int tile)
        {
            if (Find.WorldObjects == null || tile < 0) return false;
            foreach (WorldObject o in Find.WorldObjects.ObjectsAt(tile))
            {
                if (IsVanillaAbandonedCamp(o)) return true;
            }
            return false;
        }

        /// <summary>Remove vanilla abandoned camp leftovers so a WD outpost can occupy the tile.</summary>
        public static void DestroyVanillaAbandonedCampsAt(int tile)
        {
            if (Find.WorldObjects == null || tile < 0) return;
            var toDestroy = new List<WorldObject>();
            foreach (WorldObject o in Find.WorldObjects.ObjectsAt(tile))
            {
                if (IsVanillaAbandonedCamp(o))
                    toDestroy.Add(o);
            }
            for (int i = 0; i < toDestroy.Count; i++)
            {
                WorldObject o = toDestroy[i];
                if (o != null && !o.Destroyed)
                    o.Destroy();
            }
        }

        /// <summary>True if no settlement or WD outpost is within MinDistanceTiles of the tile. Used for gizmo disable and tooltip.</summary>
        public static bool MeetsMinDistanceOnly(int tile, out string reason)
        {
            return MeetsMinDistance(tile, out reason, null);
        }

        private static int cachedMinDistTile = -1;
        private static bool cachedMinDistHasExclude;
        private static int cachedMinDistTick = -99999;
        private static bool cachedMinDistResult;
        private static string cachedMinDistReason;

        /// <summary>Shared min-distance check. exclude: optional predicate to ignore specific world objects (e.g. same def + player in CanEstablishAt). Cached for 120 ticks.</summary>
        private static bool MeetsMinDistance(int tile, out string reason, System.Func<WorldObject, bool> exclude)
        {
            reason = null;
            if (Find.WorldGrid == null || tile < 0) return true;
            int tick = Find.TickManager?.TicksGame ?? 0;
            bool hasExclude = exclude != null;
            if (tile == cachedMinDistTile && hasExclude == cachedMinDistHasExclude && tick - cachedMinDistTick < 120)
            {
                reason = cachedMinDistReason;
                return cachedMinDistResult;
            }
            int minDist = MinDistanceTiles;
            var allObjs = Find.WorldObjects.AllWorldObjects;
            for (int i = 0; i < allObjs.Count; i++)
            {
                WorldObject o = allObjs[i];
                if (!IsEstablishmentMinDistanceBlocker(o)) continue;
                if (exclude != null && exclude(o)) continue;
                int dist = (int)Find.WorldGrid.ApproxDistanceInTiles(tile, o.Tile);
                if (dist < minDist)
                {
                    reason = "TSA_WD_Establish_TooClose".Translate(minDist, o.Label);
                    cachedMinDistTile = tile;
                    cachedMinDistHasExclude = hasExclude;
                    cachedMinDistTick = tick;
                    cachedMinDistResult = false;
                    cachedMinDistReason = reason;
                    return false;
                }
            }
            cachedMinDistTile = tile;
            cachedMinDistHasExclude = hasExclude;
            cachedMinDistTick = tick;
            cachedMinDistResult = true;
            cachedMinDistReason = null;
            return true;
        }

        private static int cachedNearbyTile = -1;
        private static int cachedNearbyRadius = -1;
        private static int cachedNearbyTick = -99999;
        private static int cachedNearbyResult;

        /// <summary>Bust the nearby-count cache so the next query rescans the world (e.g. after settlements are destroyed).</summary>
        public static void InvalidateNearbyCountCache()
        {
            cachedNearbyTick = -99999;
            InvalidateEstablishmentBlockedCache();
        }

        private static byte[] establishmentBlockedCache;

        /// <summary>True when the tile is closer than <see cref="MinDistanceTiles"/> to any settlement or WD outpost.</summary>
        public static bool IsTileBlockedByMinDistanceCached(int tile)
        {
            if (Find.WorldGrid == null || tile < 0) return false;
            EnsureEstablishmentBlockedCache();
            if (establishmentBlockedCache == null || tile >= establishmentBlockedCache.Length) return false;

            byte cached = establishmentBlockedCache[tile];
            if (cached == 1) return true;
            if (cached == 2) return false;

            bool blocked = !MeetsMinDistanceOnly(tile, out _);
            establishmentBlockedCache[tile] = blocked ? (byte)1 : (byte)2;
            return blocked;
        }

        public static void InvalidateEstablishmentBlockedCache()
        {
            establishmentBlockedCache = null;
            cachedMinDistTile = -1;
            cachedMinDistTick = -99999;
            establishmentBlockedPrewarmTick = -99999;
        }

        /// <summary>Clear and rebuild the blocked-tile cache for an on-demand overlay pass.</summary>
        public static void RebuildEstablishmentBlockedCacheForOverlay()
        {
            InvalidateEstablishmentBlockedCache();
            EnsureEstablishmentBlockedCache();
            PrewarmEstablishmentBlockedAroundSettlements(force: true);
        }

        private static void EnsureEstablishmentBlockedCache()
        {
            int count = Find.WorldGrid?.TilesCount ?? 0;
            if (count <= 0)
            {
                establishmentBlockedCache = null;
                return;
            }

            if (establishmentBlockedCache == null || establishmentBlockedCache.Length != count)
                establishmentBlockedCache = new byte[count];
        }

        private static int establishmentBlockedPrewarmTick = -99999;
        private static readonly List<PlanetTile> blockedPrewarmNeighbors = new List<PlanetTile>(8);
        private static readonly Queue<PlanetTile> blockedPrewarmOpen = new Queue<PlanetTile>(64);
        private static readonly Dictionary<int, int> blockedPrewarmDist = new Dictionary<int, int>(128);

        /// <summary>Mark tiles within min-distance of settlements/outposts as blocked so overlay regen avoids cold MeetsMinDistance scans.</summary>
        public static void PrewarmEstablishmentBlockedAroundSettlements(bool force = false)
        {
            WorldGrid grid = Find.WorldGrid;
            if (grid == null) return;
            int tick = Find.TickManager?.TicksGame ?? 0;
            if (!force && tick - establishmentBlockedPrewarmTick < 600) return;
            establishmentBlockedPrewarmTick = tick;

            EnsureEstablishmentBlockedCache();
            if (establishmentBlockedCache == null) return;

            int minDist = MinDistanceTiles;
            if (minDist <= 0) return;
            int floodRadius = Mathf.Max(0, minDist - 1);

            var allObjs = Find.WorldObjects?.AllWorldObjects;
            if (allObjs == null) return;
            for (int i = 0; i < allObjs.Count; i++)
            {
                WorldObject o = allObjs[i];
                if (!IsEstablishmentMinDistanceBlocker(o)) continue;
                MarkBlockedFlood(grid, o.Tile, floodRadius);
            }
        }

        private static void MarkBlockedFlood(WorldGrid grid, PlanetTile root, int radius)
        {
            // Flood writes tileId into a surface-sized cache; never start from a non-surface root.
            if (!root.Valid || radius < 0) return;
            if (!PlanetSurfaceWorldActions.IsPlanetSurfaceTileForWorldActions(root)) return;
            blockedPrewarmOpen.Clear();
            blockedPrewarmDist.Clear();
            blockedPrewarmOpen.Enqueue(root);
            blockedPrewarmDist[root.tileId] = 0;
            while (blockedPrewarmOpen.Count > 0)
            {
                PlanetTile tile = blockedPrewarmOpen.Dequeue();
                int d = blockedPrewarmDist[tile.tileId];
                if (tile.tileId >= 0 && tile.tileId < establishmentBlockedCache.Length)
                    establishmentBlockedCache[tile.tileId] = 1;
                if (d >= radius) continue;
                blockedPrewarmNeighbors.Clear();
                grid.GetTileNeighbors(tile, blockedPrewarmNeighbors);
                for (int i = 0; i < blockedPrewarmNeighbors.Count; i++)
                {
                    PlanetTile n = blockedPrewarmNeighbors[i];
                    if (!n.Valid || blockedPrewarmDist.ContainsKey(n.tileId)) continue;
                    blockedPrewarmDist[n.tileId] = d + 1;
                    blockedPrewarmOpen.Enqueue(n);
                }
            }
        }

        /// <summary>Count NPC settlements within radius of tile that are non-player and non-hostile to player. Cached per (tile,radius) for 2500 ticks. (WD outposts are player-only and never counted.)</summary>
        public static int CountNearbySettlementsOrOutposts(int tile, int radiusTiles)
        {
            if (Find.WorldGrid == null || tile < 0 || radiusTiles <= 0) return 0;
            int tick = Find.TickManager.TicksGame;
            if (tile == cachedNearbyTile && radiusTiles == cachedNearbyRadius && tick - cachedNearbyTick < 2500)
                return cachedNearbyResult;

            int count = 0;
            Faction playerFaction = Faction.OfPlayer;
            var settlements = Find.WorldObjects.Settlements;
            for (int i = 0; i < settlements.Count; i++)
            {
                Settlement s = settlements[i];
                if (!s.Tile.Valid || WorldActions_Utils.IsSpace(s) || s.Faction == null || s.Faction.IsPlayer || s.Faction == playerFaction
                    || WorldActions_Utils.SafeHostileTo(s.Faction, playerFaction))
                    continue;
                if ((int)Find.WorldGrid.ApproxDistanceInTiles(tile, s.Tile) <= radiusTiles) count++;
            }

            cachedNearbyTile = tile;
            cachedNearbyRadius = radiusTiles;
            cachedNearbyTick = tick;
            cachedNearbyResult = count;
            return count;
        }

        /// <summary>True if tile has at least ext.minNearbySettlementsOrOutposts within ext.minNearbyRadiusTiles (non-hostile, non-player). If def has no requirement, returns true.</summary>
        public static bool MeetsMinNearbySettlements(int tile, WorldObjectDef outpostDef, out string reason)
        {
            reason = null;
            var ext = outpostDef?.GetModExtension<OutpostDefExtension>();
            if (ext == null || ext.minNearbySettlementsOrOutposts <= 0) return true;
            int radius = Mathf.Max(0, ext.minNearbyRadiusTiles);
            int count = CountNearbySettlementsOrOutposts(tile, radius);
            if (count >= ext.minNearbySettlementsOrOutposts) return true;
            string key = "TSA_WD_Establish_MinNearbySettlements";
            reason = key.Translate(outpostDef?.label ?? "Outpost", ext.minNearbySettlementsOrOutposts, radius, count).ToString();
            if (reason == key) reason = "Need at least " + ext.minNearbySettlementsOrOutposts + " neutral or allied settlements within " + radius + " tiles. Found: " + count;
            return false;
        }

        /// <summary>Cost to establish this outpost from def (OutpostDefExtension.establishmentCost); default 50 wood scaled by settings if def has none. Zero multiplier = no cost.</summary>
        public static List<ThingDefCountClass> GetCost(WorldObjectDef outpostDef)
        {
            float mult = WorldDominationMod.settings?.outpostBuildCostMultiplier ?? 1f;
            var ext = outpostDef?.GetModExtension<OutpostDefExtension>();
            if (ext?.establishmentCost != null && ext.establishmentCost.Count > 0)
            {
                var list = new List<ThingDefCountClass>();
                foreach (var c in ext.establishmentCost)
                {
                    if (c?.thingDef == null) continue;
                    int n = Mathf.RoundToInt(c.count * mult);
                    if (n > 0)
                        list.Add(new ThingDefCountClass(c.thingDef, n));
                }
                return list;
            }
            int defaultCount = Mathf.RoundToInt(50f * mult);
            var fallback = new List<ThingDefCountClass>();
            if (defaultCount > 0)
                fallback.Add(new ThingDefCountClass(ThingDefOf.WoodLog, defaultCount));
            return fallback;
        }

        /// <summary>
        /// Caravan must be fully stopped on <paramref name="tile"/> (not mid-move). Same idea as <see cref="WorldObjectComp_AutoAddPawn"/>.
        /// Prevents founding with missing caravan pawns and falling through to conquest-style generated colonists.
        /// </summary>
        public static bool CaravanFullyStoppedOnTileForEstablishment(Caravan caravan, int tile, out string reason)
        {
            reason = null;
            if (caravan == null || caravan.Destroyed)
            {
                reason = "TSA_WD_EstablishOutpost_CaravanInvalid".Translate().ToString();
                return false;
            }
            if (VehicleFrameworkOutpostDissolveCompat.TryEvaluateVehicleCaravanStoppedOnTile(
                    caravan, tile, requireDestinationMatchesTile: true, out bool vfOk, out string vfReason))
            {
                reason = vfReason;
                return vfOk;
            }

            if (caravan.pather == null)
            {
                reason = "TSA_WD_EstablishOutpost_CaravanInvalid".Translate().ToString();
                return false;
            }

            if (caravan.pather.MovingNow)
            {
                reason = "TSA_WD_EstablishOutpost_WaitUntilStopped".Translate().ToString();
                return false;
            }

            PlanetTile current = caravan.Tile;
            if (!current.Valid || current.tileId != tile)
            {
                reason = "TSA_WD_EstablishOutpost_WaitUntilStopped".Translate().ToString();
                return false;
            }

            // Shuttle caravans cannot walk; pather destination often stays stale after landing.
            if (OdysseyShuttleOutpostEstablishmentCompat.CaravanUsesPassengerShuttleForTravel(caravan))
                return true;

            // Breaking camp / arriving on abandoned camp leaves stale pather.Destination (VF aerial parallel).
            if (TileHasVanillaAbandonedCamp(tile))
                return true;

            PlanetTile dest = caravan.pather.Destination;
            if (dest.Valid && dest != current)
            {
                reason = "TSA_WD_EstablishOutpost_WaitUntilStopped".Translate().ToString();
                return false;
            }
            return true;
        }

        /// <summary>
        /// Relaxed stop check for add-to-outpost / auto-add only (not founding). Pod-spawned caravans can have
        /// <c>pather.Destination != tile</c> while not moving; we only require same tile and not mid-move.
        /// </summary>
        public static bool CaravanParkedOnTileForAddToOutpost(Caravan caravan, int tile, out string reason)
        {
            reason = null;
            if (caravan == null || caravan.Destroyed)
            {
                reason = "TSA_WD_EstablishOutpost_CaravanInvalid".Translate().ToString();
                return false;
            }
            if (VehicleFrameworkOutpostDissolveCompat.TryEvaluateVehicleCaravanStoppedOnTile(
                    caravan, tile, requireDestinationMatchesTile: false, out bool vfOk, out string vfReason))
            {
                reason = vfReason;
                return vfOk;
            }

            if (caravan.pather == null)
            {
                reason = "TSA_WD_EstablishOutpost_CaravanInvalid".Translate().ToString();
                return false;
            }

            if (caravan.pather.MovingNow)
            {
                reason = "TSA_WD_EstablishOutpost_WaitUntilStopped".Translate().ToString();
                return false;
            }

            PlanetTile current = caravan.Tile;
            if (!current.Valid || current.tileId != tile)
            {
                reason = "TSA_WD_EstablishOutpost_WaitUntilStopped".Translate().ToString();
                return false;
            }
            return true;
        }

        /// <summary>Returns true if the tile and caravan satisfy requirements for this outpost type. reason is set when false.</summary>
        public static bool CanEstablishAt(int tile, WorldObjectDef outpostDef, Caravan caravan, out string reason)
        {
            reason = null;

            if (Find.WorldGrid == null || tile < 0 || tile >= Find.WorldGrid.TilesCount)
            {
                reason = "Invalid tile.";
                return false;
            }

            if (TileHasActiveCamp(tile))
            {
                reason = "TSA_WD_Establish_ActiveCamp".Translate();
                return false;
            }

            if (caravan != null && !CaravanFullyStoppedOnTileForEstablishment(caravan, tile, out reason))
                return false;

            // Min distance is the General slider, not the nearby-settlements checkbox.
            if (!MeetsMinDistance(tile, out reason, o => o.def == outpostDef && o.Faction == Faction.OfPlayer))
                return false;

            // Min nearby settlements/outposts (non-hostile, non-player) for e.g. Trading/Recruiting
            if (EnforceNearbySettlements && !MeetsMinNearbySettlements(tile, outpostDef, out reason))
                return false;

            // Tile requirements per outpost type
            if (!TileSatisfiesOutpostType(tile, outpostDef, out string tileReason))
            {
                reason = tileReason;
                return false;
            }

            // Biome restriction: outpost def can limit which biomes it can be built on
            if (EnforceBiome && !BiomeAllowedForOutpost(tile, outpostDef, out string biomeReason))
            {
                reason = biomeReason;
                return false;
            }

            // Research: some outpost types require a finished research project (e.g. Production = Fabrication)
            if (EnforceResearch && !ResearchRequirementsMet(outpostDef, out string researchReason))
            {
                reason = researchReason;
                return false;
            }

            // Cost, min pawns and min skill: only when establishing from caravan (conquest path ignores these)
            if (caravan != null)
            {
                if (EnforceMinPawns && !MeetsMinPawns(outpostDef, caravan, out string pawnReason))
                {
                    reason = pawnReason;
                    return false;
                }
                if (EnforceCost && !CaravanHasCost(caravan, outpostDef, out string costReason))
                {
                    reason = costReason;
                    return false;
                }
                if (EnforceMinSkill && !MeetsMinCumulativeSkill(outpostDef, caravan, out string skillReason))
                {
                    reason = skillReason;
                    return false;
                }
            }

            return true;
        }

        /// <summary>Returns the research projects required to establish this outpost type (all must be completed). Empty if none.</summary>
        public static List<ResearchProjectDef> GetRequiredResearchProjects(WorldObjectDef outpostDef)
        {
            var list = new List<ResearchProjectDef>();
            var ext = outpostDef?.GetModExtension<OutpostDefExtension>();
            if (ext?.requiredResearchProjectDefNames == null || ext.requiredResearchProjectDefNames.Count == 0) return list;
            foreach (string name in ext.requiredResearchProjectDefNames)
            {
                if (string.IsNullOrEmpty(name)) continue;
                var project = DefDatabase<ResearchProjectDef>.GetNamedSilentFail(name);
                if (project != null) list.Add(project);
            }
            return list;
        }

        /// <summary>True if no research is required for this outpost type, or all required research is finished. reason set when false.</summary>
        public static bool ResearchRequirementsMet(WorldObjectDef outpostDef, out string reason)
        {
            reason = null;
            var projects = GetRequiredResearchProjects(outpostDef);
            if (projects.Count == 0) return true;
            if (Find.ResearchManager == null) return true;
            foreach (var project in projects)
            {
                if (Find.ResearchManager.GetProgress(project) < project.baseCost)
                {
                    reason = "TSA_WD_Establish_ResearchRequired".Translate(outpostDef?.label ?? "Outpost", project.LabelCap);
                    return false;
                }
            }
            return true;
        }

        /// <summary>True if the tile's biome is allowed for this outpost type. Uses WorldTileInfo.GetBiome (tries tile.biome and tile.PrimaryBiome), then disallowed/allowed list check.</summary>
        public static bool BiomeAllowedForOutpost(int tile, WorldObjectDef outpostDef, out string reason)
        {
            reason = null;
            var ext = outpostDef?.GetModExtension<OutpostDefExtension>();
            if (ext == null) return true;
            var biome = WorldTileInfo.GetBiome(tile);
            string biomeDefName = biome?.defName;
            string biomeLabel = biome?.label;
            reason = ext.CanBuildInBiome(biomeDefName, biomeLabel, outpostDef?.label ?? "Outpost");
            return reason == null;
        }

        /// <summary>Minimum number of pawns required to found this outpost from a caravan. From OutpostDefExtension.minPawnsToFound (default 1).</summary>
        public static int GetMinPawnsToFound(WorldObjectDef outpostDef)
        {
            var ext = outpostDef?.GetModExtension<OutpostDefExtension>();
            if (ext == null) return 1;
            return Mathf.Max(1, ext.minPawnsToFound);
        }

        /// <summary>True if caravan has at least GetMinPawnsToFound humanlike pawns. reason set when false.</summary>
        public static bool MeetsMinPawns(WorldObjectDef outpostDef, Caravan caravan, out string reason)
        {
            reason = null;
            if (caravan?.PawnsListForReading == null) return true;
            int need = GetMinPawnsToFound(outpostDef);
            int have = 0;
            var pawnsList = caravan.PawnsListForReading;
            for (int i = 0; i < pawnsList.Count; i++)
            {
                if (pawnsList[i]?.RaceProps?.Humanlike == true && !pawnsList[i].Dead) have++;
            }
            if (have >= need) return true;
            reason = "TSA_WD_Establish_MinPawns".Translate(outpostDef?.label ?? "Outpost", need, have);
            return false;
        }

        /// <summary>True if caravan meets all required cumulative skills for this outpost (when founding from caravan).</summary>
        public static bool MeetsMinCumulativeSkill(WorldObjectDef outpostDef, Caravan caravan, out string reason)
        {
            reason = null;
            var ext = outpostDef?.GetModExtension<OutpostDefExtension>();
            if (ext?.MinCumulativeSkill == null || ext.MinCumulativeSkill.Count == 0) return true;
            foreach (var set in ext.MinCumulativeSkill)
            {
                if (set == null || !set.HasAnyRequirement()) continue;
                foreach (var kv in set.GetRequirements())
                {
                    if (kv.Key == null || kv.Value <= 0) continue;
                    int have = GetCumulativeCaravanSkillForSkill(caravan, kv.Key);
                    if (have >= kv.Value) continue;
                    reason = "TSA_WD_Establish_MinSkill".Translate(kv.Value, kv.Key.LabelCap, have);
                    return false;
                }
            }
            return true;
        }

        /// <summary>Cumulative skill (sum of level) for one skill across all humanlike caravan pawns.</summary>
        public static int GetCumulativeCaravanSkillForSkill(Caravan caravan, SkillDef skillDef)
        {
            if (caravan?.PawnsListForReading == null || skillDef == null) return 0;
            int sum = 0;
            foreach (var p in caravan.PawnsListForReading)
            {
                if (p?.RaceProps?.Humanlike == true && !p.Dead && p.skills != null)
                    sum += p.skills.GetSkill(skillDef).Level;
            }
            return sum;
        }

        /// <summary>Cumulative skill of caravan pawns for this outpost type (sum of primary skill(s)). Used for UI when def has no MinCumulativeSkill; otherwise summed from MinCumulativeSkill sets.</summary>
        public static int GetCumulativeCaravanSkill(WorldObjectDef def, Caravan caravan)
        {
            if (def == null || caravan?.PawnsListForReading == null) return 0;
            var ext = def.GetModExtension<OutpostDefExtension>();
            if (ext?.MinCumulativeSkill != null && ext.MinCumulativeSkill.Count > 0)
            {
                int total = 0;
                foreach (var set in ext.MinCumulativeSkill)
                {
                    if (set == null) continue;
                    foreach (var kv in set.GetRequirements())
                        if (kv.Key != null) total += GetCumulativeCaravanSkillForSkill(caravan, kv.Key);
                }
                return total;
            }
            string d = def.defName.ToLowerInvariant();
            SkillDef targetSkill;
            if (d.Contains("farming"))
                targetSkill = SkillDefOf.Plants;
            else if (d.Contains("hunting") || d.Contains("fishing"))
                targetSkill = SkillDefOf.Animals;
            else if (d.Contains("recruiting") || d.Contains("trading") || d.Contains("embassy") || d.Contains("town"))
                targetSkill = SkillDefOf.Social;
            else if (d.Contains("mining"))
                targetSkill = SkillDefOf.Mining;
            else if (d.Contains("fabrication") || d.Contains("production") || d.Contains("factory"))
                targetSkill = SkillDefOf.Crafting;
            else
                targetSkill = SkillDefOf.Plants;
            var allPawns = caravan.PawnsListForReading;
            int sum = 0;
            for (int i = 0; i < allPawns.Count; i++)
            {
                var p = allPawns[i];
                if (p?.RaceProps?.Humanlike != true || p.Dead) continue;
                sum += p.skills?.GetSkill(targetSkill).Level ?? 0;
            }
            return sum;
        }

        /// <summary>Farming/Logging: not water, fertility % >= minFertilityPercent. Hunting: not water, animal abundance % >= minAnimalAbundancePercent. Mining: hills+.</summary>
        public static bool TileSatisfiesOutpostType(int tile, WorldObjectDef outpostDef, out string reason)
        {
            reason = null;
            if (Find.WorldGrid == null) return true;
            if (tile < 0 || tile >= Find.WorldGrid.TilesCount) return false;

            Tile tileInfo = Find.WorldGrid[tile];
            string d = (outpostDef?.defName ?? "").ToLowerInvariant();
            var ext = outpostDef?.GetModExtension<OutpostDefExtension>();

            if (d.Contains("farming") || Outpost_Production_Utils.IsRanchOutpost(outpostDef))
            {
                if (EnforceFertility)
                {
                    if (tileInfo.WaterCovered)
                    {
                        reason = "TSA_WD_Establish_NeedFertile".Translate();
                        return false;
                    }
                    int minFert = ext?.minFertilityPercent ?? 30;
                    int fertPct = Mathf.RoundToInt(WorldTileProductivity.GetFarmingFertilityScore(tile) * 100f);
                    if (fertPct < minFert)
                    {
                        reason = "TSA_WD_Establish_MinFertility".Translate(minFert, fertPct);
                        return false;
                    }
                }
            }
            else if (d.Contains("hunting"))
            {
                if (EnforceAnimalAbundance)
                {
                    if (tileInfo.WaterCovered)
                    {
                        reason = "TSA_WD_Establish_NeedAnimals".Translate();
                        return false;
                    }
                    int minAnim = ext?.minAnimalAbundancePercent ?? 30;
                    int animPct = Mathf.RoundToInt(WorldTileProductivity.GetHuntingScore(tile) * 100f);
                    if (animPct < minAnim)
                    {
                        reason = "TSA_WD_Establish_MinAnimalAbundance".Translate(minAnim, animPct);
                        return false;
                    }
                }
            }
            else if (d.Contains("fishing"))
            {
                if (tileInfo.WaterCovered || !tileInfo.IsCoastal)
                {
                    reason = "TSA_WD_Establish_NeedCoast".Translate();
                    return false;
                }
                if (EnforceFishAbundance)
                {
                    if (!Outpost_Fishing.HasAnySaltwaterFish(tile))
                    {
                        reason = "TSA_WD_Establish_NeedFishSpecies".Translate();
                        return false;
                    }
                    int minFish = ext?.minFishAbundancePercent ?? 30;
                    int fishPct = Mathf.RoundToInt(WorldTileProductivity.GetFishingScore(tile) * 100f);
                    if (fishPct < minFish)
                    {
                        reason = "TSA_WD_Establish_MinFishAbundance".Translate(minFish, fishPct);
                        return false;
                    }
                }
            }
            else if (d.Contains("mining"))
            {
                if (EnforceMiningTerrain)
                {
                    if (tileInfo.hilliness < Hilliness.SmallHills)
                    {
                        reason = "TSA_WD_Establish_NeedMining".Translate();
                        return false;
                    }
                }
            }

            return true;
        }

        public static bool CaravanHasCost(Caravan caravan, WorldObjectDef outpostDef, out string reason)
        {
            reason = null;
            var cost = GetCost(outpostDef);
            foreach (var c in cost)
            {
                int have = CountThingOnCaravan(caravan, c.thingDef);
                if (have < c.count)
                {
                    reason = "TSA_WD_Establish_NeedCost".Translate(c.count, c.thingDef.label, have);
                    return false;
                }
            }
            return true;
        }

        /// <summary>Total stack count of <paramref name="thingDef"/> in caravan inventory.</summary>
        public static int CountThingOnCaravan(Caravan caravan, ThingDef thingDef)
        {
            if (caravan == null || thingDef == null) return 0;
            int have = 0;
            var invItems = new List<Thing>(CaravanInventoryUtility.AllInventoryItems(caravan));
            VehicleFrameworkOutpostDissolveCompat.AppendVehicleInventoryItems(caravan, invItems);
            for (int j = 0; j < invItems.Count; j++)
            {
                if (invItems[j].def == thingDef)
                    have += invItems[j].stackCount;
            }
            return have;
        }

        /// <summary>Deduct establishment cost from caravan inventory. Returns true if deducted (or no cost).</summary>
        public static bool TryDeductCost(Caravan caravan, WorldObjectDef outpostDef)
        {
            if (!EnforceCost) return true;
            if (caravan == null) return true;
            var cost = GetCost(outpostDef);
            foreach (var c in cost)
            {
                int toRemove = c.count;
                var allInvItems = new List<Thing>(CaravanInventoryUtility.AllInventoryItems(caravan));
                VehicleFrameworkOutpostDissolveCompat.AppendVehicleInventoryItems(caravan, allInvItems);
                var items = new List<Thing>();
                for (int j = 0; j < allInvItems.Count; j++)
                {
                    if (allInvItems[j].def == c.thingDef)
                        items.Add(allInvItems[j]);
                }
                foreach (var t in items)
                {
                    if (toRemove <= 0) break;
                    Pawn owner = CaravanInventoryUtility.GetOwnerOf(caravan, t);
                    owner ??= VehicleFrameworkOutpostDissolveCompat.TryGetVehicleInventoryOwner(caravan, t);
                    if (owner?.inventory?.innerContainer == null) continue;
                    if (t.stackCount <= toRemove)
                    {
                        toRemove -= t.stackCount;
                        owner.inventory.innerContainer.Remove(t);
                        t.Destroy(DestroyMode.Vanish);
                    }
                    else
                    {
                        Thing split = t.SplitOff(toRemove);
                        if (split != null) split.Destroy(DestroyMode.Vanish);
                        toRemove = 0;
                    }
                }
            }
            return true;
        }

        // --- Production pause (runtime requirements for existing outposts) ---

        /// <summary>Cumulative skill (sum of level) for one skill across all virtual pawns at the outpost.</summary>
        public static int GetCumulativeOutpostSkillForSkill(WorldObject_WD_Outpost outpost, SkillDef skillDef)
        {
            if (outpost?.VirtualPawns == null || skillDef == null) return 0;
            int sum = 0;
            foreach (var v in outpost.VirtualPawns)
            {
                if (v == null) continue;
                sum += v.GetSkill(skillDef);
            }
            return sum;
        }

        /// <summary>True if outpost has at least GetMinPawnsToFound pawns. reason set when false.</summary>
        public static bool MeetsMinPawnsAtOutpost(WorldObject_WD_Outpost outpost, out string reason)
        {
            reason = null;
            if (outpost?.def == null) return true;
            int need = GetMinPawnsToFound(outpost.def);
            int have = outpost.PawnCount;
            if (have >= need) return true;
            reason = "TSA_WD_Establish_MinPawns".Translate(outpost.def.label ?? "Outpost", need, have).ToString();
            return false;
        }

        /// <summary>True if outpost's virtual pawns meet all MinCumulativeSkill requirements. reason set when false.</summary>
        public static bool MeetsMinCumulativeSkillAtOutpost(WorldObject_WD_Outpost outpost, out string reason)
        {
            reason = null;
            var ext = outpost?.def?.GetModExtension<OutpostDefExtension>();
            if (ext?.MinCumulativeSkill == null || ext.MinCumulativeSkill.Count == 0) return true;
            foreach (var set in ext.MinCumulativeSkill)
            {
                if (set == null || !set.HasAnyRequirement()) continue;
                foreach (var kv in set.GetRequirements())
                {
                    if (kv.Key == null || kv.Value <= 0) continue;
                    int have = GetCumulativeOutpostSkillForSkill(outpost, kv.Key);
                    if (have >= kv.Value) continue;
                    reason = "TSA_WD_Establish_MinSkill".Translate(kv.Value, kv.Key.LabelCap, have).ToString();
                    return false;
                }
            }
            return true;
        }

        /// <summary>Fills reasons with translatable strings for each failing runtime requirement (min pawns, min skill, min settlements in radius). Returns true if production may run (reasons empty), false if paused.</summary>
        public static bool GetProductionPauseReasons(WorldObject_WD_Outpost outpost, List<string> reasons)
        {
            reasons?.Clear();
            if (outpost == null || reasons == null) return true;
            if (EnforceMinPawns && !MeetsMinPawnsAtOutpost(outpost, out string pawnReason))
                reasons.Add(pawnReason);
            if (EnforceMinSkill && !MeetsMinCumulativeSkillAtOutpost(outpost, out string skillReason))
                reasons.Add(skillReason);
            var ext = outpost.def?.GetModExtension<OutpostDefExtension>();
            if (EnforceNearbySettlements && ext != null && ext.minNearbySettlementsOrOutposts > 0 && !MeetsMinNearbySettlements(outpost.Tile, outpost.def, out string nearbyReason))
                reasons.Add(nearbyReason);

            return reasons.Count == 0;
        }

        /// <summary>Conquest path: outpost tier must be &lt;= conquered settlement tier; then tile type, biome, and min nearby settlements. No research, min distance, cost, pawns, or skill checks.</summary>
        public static bool CanEstablishAtForConquest(int tile, WorldObjectDef outpostDef, SettlementTier conquestTier, out string reason)
        {
            reason = null;
            if (Find.WorldGrid == null || tile < 0 || tile >= Find.WorldGrid.TilesCount)
            {
                reason = "Invalid tile.";
                return false;
            }
            if (TileHasActiveCamp(tile))
            {
                reason = "TSA_WD_Establish_ActiveCamp".Translate();
                return false;
            }
            int maxOutpostTier = (int)conquestTier + 1;
            int outpostTier = WorldObject_WD_Outpost.GetOutpostTier(outpostDef);
            if (outpostTier > maxOutpostTier)
            {
                reason = "TSA_WD_Establish_ConquestTierTooHigh".Translate(outpostDef?.label ?? "Outpost", outpostTier, maxOutpostTier);
                return false;
            }
            if (!TileSatisfiesOutpostType(tile, outpostDef, out reason))
                return false;
            if (EnforceBiome && !BiomeAllowedForOutpost(tile, outpostDef, out reason))
                return false;
            if (EnforceNearbySettlements && !MeetsMinNearbySettlements(tile, outpostDef, out reason))
                return false;
            return true;
        }
    }
}
