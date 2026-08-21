using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Result of travel time and strength efficiency between two world tiles.</summary>
    public struct TravelStrengthEstimate
    {
        public bool Found;
        public float TravelTicks;
        public float TravelHours;
        public float TravelDays;
        public float Efficiency;
    }

    /// <summary>Travel time and strength-at-arrival estimates using pathfinding and decay settings.</summary>
    public static class TravelUtils
    {
        private static readonly Dictionary<long, TravelStrengthEstimate> estimateCache = new Dictionary<long, TravelStrengthEstimate>();
        private static int estimateCacheTick = -1;

        private static WorldComponent_SpreadManager s_cachedSpreadManager;
        private static World s_cachedSpreadManagerWorld;

        private static WorldComponent_SpreadManager GetCachedSpreadManager()
        {
            World w = Find.World;
            if (w != s_cachedSpreadManagerWorld)
            {
                s_cachedSpreadManager = w?.GetComponent<WorldComponent_SpreadManager>();
                s_cachedSpreadManagerWorld = w;
            }
            return s_cachedSpreadManager;
        }

        /// <summary>Land path hops / straight-line distance below this ratio means the route is direct -- skip water pathing.</summary>
        private const float DetourRatioThreshold = 1.4f;

        /// <summary>Difficulty units for entering a water-covered tile (mod setting; default matches typical mountain cost).</summary>
        public static float GetTravelerWaterMovementDifficultyUnits()
        {
            var s = WorldDominationMod.settings;
            if (s != null)
                return Mathf.Max(0.01f, s.travelerWaterMovementDifficulty);
            return WorldDominationSettings.DefTravelerWaterMovementDifficulty;
        }

        /// <summary>
        /// Difficulty units for one hop (adjacent tiles): destination terrain × road edge multiplier (vanilla surface roads only).
        /// </summary>
        public static float GetHopDifficultyUnits(PlanetTile from, PlanetTile to)
        {
            if (!from.Valid || !to.Valid) return 0f;
            float baseDiff = WorldPathGrid.CalculatedMovementDifficultyAt(to, false);
            SurfaceLayer surface = Find.WorldGrid.Surface;
            if (surface != null && from.Layer == surface && to.Layer == surface)
            {
                // Road-block flat penalty is baked into GetRoadMovementDifficultyMultiplier
                // (terrain × roadMult' == terrain × road + penalty).
                return baseDiff * Find.WorldGrid.GetRoadMovementDifficultyMultiplier(from, to);
            }

            // Off-surface / no road-mult path: apply flat penalty directly.
            return baseDiff + WorldComponent_RoadBlocks.GetFlatPenalty(to.tileId);
        }

        /// <summary>
        /// Traveler movement difficulty with strict mixed traversal behavior:
        /// normal land difficulty on land hops, fixed fly-style cost for hops into water-covered tiles.
        /// </summary>
        public static float GetTravelerHopDifficultyUnits(PlanetTile from, PlanetTile to)
        {
            if (!from.Valid || !to.Valid) return 0f;
            if (Find.WorldGrid[to.tileId].WaterCovered)
                return GetTravelerWaterMovementDifficultyUnits();
            return GetHopDifficultyUnits(from, to);
        }

        /// <summary>Total world ticks for a <b>fresh</b> path (full node list). Not valid for in-progress WorldPath after ConsumeNextNode.</summary>
        public static float SumFullPathTicks(WorldPath path, int ticksPerMove)
        {
            if (path == null || !path.Found) return 0f;
            float total = 0f;
            var nodes = path.NodesReversed;
            for (int i = 1; i < nodes.Count; i++)
                total += GetTravelerHopDifficultyUnits(nodes[i - 1], nodes[i]) * ticksPerMove;
            return total;
        }

        /// <summary>Total world ticks for a full ordered node list [start..dest].</summary>
        public static float SumFullPathTicks(IReadOnlyList<PlanetTile> nodes, int ticksPerMove)
        {
            if (nodes == null || nodes.Count <= 1) return 0f;
            float total = 0f;
            for (int i = 1; i < nodes.Count; i++)
                total += GetTravelerHopDifficultyUnits(nodes[i - 1], nodes[i]) * ticksPerMove;
            return total;
        }

        /// <summary>
        /// WD world travel math: per-hop time uses <paramref name="ticksPerMoveOrZero"/> when &gt; 0, otherwise <see cref="WorldObject_Traveler.DefaultTicksPerMove"/>.
        /// Pass <see cref="WorldObject_Traveler.DefaultTicksPerMove"/> explicitly at call sites that have no traveler instance.
        /// </summary>
        public static int ResolveTicksPerMove(int ticksPerMoveOrZero) =>
            ticksPerMoveOrZero > 0 ? ticksPerMoveOrZero : WorldObject_Traveler.DefaultTicksPerMove;

        /// <summary>
        /// Attrition efficiency from path travel ticks already summed via <see cref="SumFullPathTicks"/> (same rules as <see cref="GetTravelStrengthEstimate"/>).
        /// Returns false if settings are null.
        /// </summary>
        public static bool TryEfficiencyFromPathTravelTicks(float totalRealTicks, WorldDominationSettings seth, Faction travelerFaction, out float efficiency)
        {
            efficiency = 0f;
            if (seth == null) return false;
            float totalHours = totalRealTicks / 2500f;
            float effectiveLossPerHour = seth.strengthLossPerHour;
            if (travelerFaction != null)
            {
                var manager = GetCachedSpreadManager();
                if (manager != null && travelerFaction == manager.expansionistZealFaction && Find.TickManager.TicksGame < manager.expansionistZealExpiryTick)
                    effectiveLossPerHour *= seth.zealAttritionMult;
            }
            float raw = Mathf.Pow(1.0f - effectiveLossPerHour, totalHours);
            float minEfficiency = 1f - Mathf.Clamp01(seth.maxTravelPercentageStrengthLoss);
            efficiency = Mathf.Max(Mathf.Clamp01(raw), minEfficiency);
            return true;
        }

        /// <summary>
        /// T4 drop-pod raid attrition: crow-flies distance × <see cref="WorldObject_Traveler.DefaultTicksPerMove"/>
        /// as synthetic travel ticks, with <see cref="WorldDominationSettings.dropPodRaidAttritionMult"/> on loss/hour.
        /// Same floor / zeal rules as walking. Used for gate, letter, and traveler strength (must match).
        /// </summary>
        public static bool TryDropPodRaidEfficiency(
            int startTileId,
            int destTileId,
            WorldDominationSettings seth,
            Faction travelerFaction,
            out float efficiency,
            out float syntheticTicks)
        {
            efficiency = 0f;
            syntheticTicks = 0f;
            if (seth == null) return false;
            if (startTileId < 0 || destTileId < 0
                || startTileId >= Find.WorldGrid.TilesCount
                || destTileId >= Find.WorldGrid.TilesCount)
                return false;

            float dist = Mathf.Max(1f, Find.WorldGrid.ApproxDistanceInTiles(startTileId, destTileId));
            syntheticTicks = dist * WorldObject_Traveler.DefaultTicksPerMove;
            float totalHours = syntheticTicks / 2500f;
            float mult = Mathf.Max(1f, seth.dropPodRaidAttritionMult);
            float effectiveLossPerHour = Mathf.Clamp(seth.strengthLossPerHour * mult, 0f, 0.99f);
            if (travelerFaction != null)
            {
                var manager = GetCachedSpreadManager();
                if (manager != null && travelerFaction == manager.expansionistZealFaction && Find.TickManager.TicksGame < manager.expansionistZealExpiryTick)
                    effectiveLossPerHour = Mathf.Clamp(effectiveLossPerHour * seth.zealAttritionMult, 0f, 0.99f);
            }
            float raw = Mathf.Pow(1.0f - effectiveLossPerHour, totalHours);
            float minEfficiency = 1f - Mathf.Clamp01(seth.maxTravelPercentageStrengthLoss);
            efficiency = Mathf.Max(Mathf.Clamp01(raw), minEfficiency);
            return true;
        }

        /// <summary>
        /// Single source of truth for travel time and strength efficiency between two tiles.
        /// Uses pathfinding and terrain difficulty; efficiency uses exponential decay (1 - rate)^hours.
        /// When travelerFaction has Expansionist Zeal active, effective attrition is reduced (ZealAttritionMult).
        /// </summary>
        /// <param name="ticksPerMoveOverride">0 = use <see cref="WorldObject_Traveler.DefaultTicksPerMove"/>; otherwise same units as <see cref="WorldObject_Traveler.ticksPerMove"/>.</param>
        public static TravelStrengthEstimate GetTravelStrengthEstimate(int startTileId, int destTileId, WorldDominationSettings seth, Faction travelerFaction = null, int ticksPerMoveOverride = 0)
        {
            var result = new TravelStrengthEstimate { Found = false, Efficiency = 0f };
            if (startTileId < 0 || destTileId < 0 ||
                startTileId >= Find.WorldGrid.TilesCount ||
                destTileId >= Find.WorldGrid.TilesCount)
                return result;

            int tick = Find.TickManager?.TicksGame ?? -1;
            if (tick != estimateCacheTick)
            {
                estimateCache.Clear();
                estimateCacheTick = tick;
            }
            long cacheKey = ((long)startTileId << 32) | (uint)destTileId;
            if (estimateCache.TryGetValue(cacheKey, out var cached))
                return cached;

            World world = Find.World;
            // WD travel is surface-only; resolve the surface layer explicitly rather than via WorldGrid[int].
            PlanetLayer layer = PlanetSurfaceWorldActions.WdSurfaceLayer;
            if (layer == null) return result;
            int moveSpeed = ResolveTicksPerMove(ticksPerMoveOverride);

            float? vanillaTicks = null;
            int vanillaHops = 0;
            using (WorldPath path = layer.Pather.FindPath(new PlanetTile(startTileId, layer), new PlanetTile(destTileId, layer), null))
            {
                if (path != null && path.Found)
                {
                    vanillaTicks = SumFullPathTicks(path, moveSpeed);
                    vanillaHops = path.NodesReversed.Count - 1;
                }
            }

            bool allowWaterTravel = seth?.allowCaravansTravelOverWater ?? WorldDominationSettings.DefAllowCaravansTravelOverWater;
            bool strictWaterOnly = seth?.onlyTravelAcrossWaterIfNoOtherWay ?? WorldDominationSettings.DefOnlyTravelAcrossWaterIfNoOtherWay;

            List<PlanetTile> waterPath = null;
            if (allowWaterTravel)
            {
                float thresholdDays = seth?.waterPathLandThresholdDays ?? WorldDominationSettings.DefWaterPathLandThresholdDays;
                float thresholdTicks = thresholdDays * 60000f;

                bool skipWaterPath = false;
                if (!strictWaterOnly && vanillaTicks.HasValue)
                {
                    if (thresholdDays > 0f && vanillaTicks.Value <= thresholdTicks)
                        skipWaterPath = true;

                    if (!skipWaterPath && vanillaHops > 0)
                    {
                        float approx = world.grid.ApproxDistanceInTiles(startTileId, destTileId);
                        if (approx > 0f && vanillaHops / approx < DetourRatioThreshold)
                            skipWaterPath = true;
                    }
                }

                if (!skipWaterPath &&
                    (!strictWaterOnly || !vanillaTicks.HasValue) &&
                    TravelerWaterPathing.TryBuildFallbackPath(new PlanetTile(startTileId, layer), new PlanetTile(destTileId, layer), out var built) &&
                    built != null && built.Count > 1)
                    waterPath = built;
            }

            float? waterTicks = waterPath != null ? SumFullPathTicks(waterPath, moveSpeed) : null;

            float totalRealTicks;
            if (strictWaterOnly)
            {
                if (vanillaTicks.HasValue) totalRealTicks = vanillaTicks.Value;
                else if (waterTicks.HasValue) totalRealTicks = waterTicks.Value;
                else return result;
            }
            else
            {
                if (vanillaTicks.HasValue && waterTicks.HasValue)
                    totalRealTicks = Mathf.Min(vanillaTicks.Value, waterTicks.Value);
                else if (vanillaTicks.HasValue) totalRealTicks = vanillaTicks.Value;
                else if (waterTicks.HasValue) totalRealTicks = waterTicks.Value;
                else return result;
            }

            result.TravelTicks = totalRealTicks;
            result.TravelHours = totalRealTicks / 2500f;
            result.TravelDays = totalRealTicks / 60000f;
            if (!TryEfficiencyFromPathTravelTicks(totalRealTicks, seth, travelerFaction, out float eff))
                return result;

            result.Found = true;
            result.Efficiency = eff;

            estimateCache[cacheKey] = result;
            return result;
        }

        public static float GetEstimatedEfficiency(int startTileId, int destTileId, WorldDominationSettings seth, Faction travelerFaction = null, int ticksPerMoveOverride = 0)
        {
            var est = GetTravelStrengthEstimate(startTileId, destTileId, seth, travelerFaction, ticksPerMoveOverride);
            return est.Found ? est.Efficiency : 0f;
        }

        /// <summary>
        /// Crow-flies travel tick estimate for raid/trader <i>prep</i> when exact pathing is skipped.
        /// Uses attrition from straight-line distance × ticks/move × detour factor — no pathfinding. Actual caravan still uses real path at launch.
        /// </summary>
        public static float GetHeuristicPrepEfficiency(int startTileId, int destTileId, WorldDominationSettings seth, Faction travelerFaction, int ticksPerMoveOverride = 0)
        {
            if (seth == null) return 0f;
            float approxDist = Find.WorldGrid.ApproxDistanceInTiles(startTileId, destTileId);
            int moveSpeed = ResolveTicksPerMove(ticksPerMoveOverride);
            const float detourFactor = 1.18f;
            float estTicks = Mathf.Max(1f, approxDist * moveSpeed * detourFactor);
            return TryEfficiencyFromPathTravelTicks(estTicks, seth, travelerFaction, out float eff) ? eff : 0f;
        }

        private static int prepExactCounter;

        /// <summary>
        /// Prep-time travel efficiency for raid/trader destination assess.
        /// <see cref="WorldDominationSettings.travelPrepExactPercent"/>: 0 = always crow-flies heuristic, 1 = always FindPath,
        /// otherwise a deterministic cadence (~fraction of assesses use exact path). Launch still uses a real path.
        /// </summary>
        public static float ResolvePrepEfficiency(int startTileId, int destTileId, WorldDominationSettings seth, Faction travelerFaction, int ticksPerMoveOverride = 0)
        {
            if (seth == null) return 0f;
            int pct = Mathf.Clamp(Mathf.RoundToInt(seth.travelPrepExactPercent * 100f), 0, 100);
            if (pct <= 0)
                return GetHeuristicPrepEfficiency(startTileId, destTileId, seth, travelerFaction, ticksPerMoveOverride);
            if (pct >= 100)
                return GetEstimatedEfficiency(startTileId, destTileId, seth, travelerFaction, ticksPerMoveOverride);

            // Deterministic: exact every Nth assess where N ≈ 100/pct (e.g. 30% → every 3rd).
            int period = Mathf.Max(1, Mathf.RoundToInt(100f / pct));
            bool useExact = (prepExactCounter++ % period) == 0;
            return useExact
                ? GetEstimatedEfficiency(startTileId, destTileId, seth, travelerFaction, ticksPerMoveOverride)
                : GetHeuristicPrepEfficiency(startTileId, destTileId, seth, travelerFaction, ticksPerMoveOverride);
        }

        /// <summary>
        /// Crow-flies travel-time estimate in days between two tiles — straight-line distance × ticks/move × detour factor,
        /// no pathfinding (mirrors <see cref="GetHeuristicPrepEfficiency"/>). Cheap enough for per-candidate threat display;
        /// the real caravan path is only resolved when a raid actually launches. Returns 0 for invalid tiles.
        /// </summary>
        public static float GetHeuristicTravelDays(int startTile, int destTile, int ticksPerMoveOverride = 0)
        {
            if (startTile < 0 || destTile < 0) return 0f;
            float approxDist = Find.WorldGrid.ApproxDistanceInTiles(startTile, destTile);
            int moveSpeed = ResolveTicksPerMove(ticksPerMoveOverride);
            const float detourFactor = 1.18f;
            float estTicks = Mathf.Max(1f, approxDist * moveSpeed * detourFactor);
            return estTicks / 60000f;
        }
    }
}
