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

        public static float ExpandCdMultFor(Faction faction)
        {
            var seth = WorldDominationMod.settings;
            if (seth == null || faction == null) return 1f;
            if (WorldActions_DiplomacyBuffsNerfs.IsActiveUnderdog(faction))
                return Mathf.Max(0.1f, seth.underdogExpandCdMult);
            if (WorldActions_DiplomacyBuffsNerfs.IsActiveLeader(faction))
                return Mathf.Max(0.1f, seth.leaderExpandCdMult);
            return 1f;
        }

        /// <summary>Silent daily offensive gain for NPC settlements (no action log).</summary>
        public static void ApplyPassiveOffensiveGrowth(CompViralSpread comp)
        {
            if (comp == null || !comp.IsSettlement || comp.IsOutpost) return;
            WorldObject parent = comp.parent;
            if (parent?.Faction == null || parent.Faction.IsPlayer) return;

            float tierMax = comp.GetEffectiveOffensiveMaxForTier(comp.tier);
            float oldStr = comp.strength;
            if (oldStr >= tierMax - 0.01f) return;

            float gain = GetPassiveOffensiveGrowthGain(comp);
            if (gain <= 0f) return;
            comp.strength = Mathf.Min(oldStr + gain, tierMax);
        }

        /// <summary>Daily passive offensive gain for an NPC settlement (buffs, late game, Hospital), before the cap.</summary>
        public static float GetPassiveOffensiveGrowthGain(CompViralSpread comp)
        {
            if (comp == null || !comp.IsSettlement || comp.IsOutpost) return 0f;
            WorldObject parent = comp.parent;
            if (parent?.Faction == null || parent.Faction.IsPlayer) return 0f;
            var seth = WorldDominationMod.settings;
            if (seth == null) return 0f;

            float buffMult = 1f;
            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            if (manager != null && WorldActions_DiplomacyBuffsNerfs.IsActiveUnderdog(parent.Faction, manager))
                buffMult = seth.underdogGrowthGainMult;
            else if (manager != null && WorldActions_DiplomacyBuffsNerfs.IsActiveLeader(parent.Faction, manager))
                buffMult = seth.leaderGrowthGainMult;

            float gain = seth.GetPassiveGrowthAmount(comp.tier) * buffMult;
            if (manager != null && WdEscalation.IsMidOrLate(manager))
                gain *= manager.GetLateGameGrowthMultiplier();
            gain *= AdvancedBuildingEffects.RegenMult(parent);
            return Mathf.Max(0f, gain);
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

        /// <summary>
        /// Safe tug-away expand: sample K tiles outside hostile MaxLOS, off nearest-enemy geodesics, not toward the player.
        /// Cheaty perfect knowledge of nearby hostiles (no intel gate / Knows).
        /// </summary>
        public static bool AttemptStrategicExpand(WorldObject s, CompViralSpread comp, WorldComponent_SpreadManager manager)
        {
            if (s == null || comp == null || manager == null) return false;
            if (!IsDevelopEligible(comp)) return false;
            if (comp.IsExpansionOnCooldown) return false;
            if (!(s is Settlement parent)) return false;

            return ExecuteExpansion(parent, comp, manager, strategicOnly: true);
        }

        /// <summary>
        /// Growth <c>localMaxT*</c> room for promoting <paramref name="fromTier"/> to the next tier
        /// within <see cref="WorldDominationSettings.localTierClusterRadius"/>.
        /// </summary>
        public static bool CanPromoteNextTierRegionally(WorldObject settlement, SettlementTier fromTier)
        {
            if (settlement == null || settlement.Destroyed || fromTier >= SettlementTier.T4) return false;
            var seth = WorldDominationMod.settings;
            if (seth == null) return false;

            CountNearbySameFactionTiers(settlement, seth, out _, out _, out int t2Count, out int t3Count, out int t4Count);
            switch (fromTier)
            {
                case SettlementTier.T1:
                    return t2Count < seth.localMaxT2;
                case SettlementTier.T2:
                    return t3Count < seth.localMaxT3;
                case SettlementTier.T3:
                    return t4Count < seth.localMaxT4;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Pyramid support within Cluster radius: need N same-tier peers and N of each lower tier
        /// (<see cref="WorldDominationSettings.GetSameTierNeighborsRequiredForUpgrade"/>).
        /// </summary>
        public static bool MeetsPyramidSupportForUpgrade(WorldObject settlement, SettlementTier fromTier)
        {
            if (settlement == null || settlement.Destroyed || fromTier >= SettlementTier.T4) return false;
            var seth = WorldDominationMod.settings;
            if (seth == null) return false;

            int need = seth.GetSameTierNeighborsRequiredForUpgrade(fromTier);
            if (need <= 0) return true;

            CountNearbySameFactionTiers(settlement, seth, out int peers, out int t1Count, out int t2Count, out _, out _);
            if (peers < need) return false;
            switch (fromTier)
            {
                case SettlementTier.T1:
                    return true;
                case SettlementTier.T2:
                    return t1Count >= need;
                case SettlementTier.T3:
                    return t2Count >= need && t1Count >= need;
                default:
                    return false;
            }
        }

        /// <summary>Soft tier-up gate for Develop, trader arrival, and Gift/Buy/Bribe investment.</summary>
        public static bool CanUpgradeTierFully(WorldObject settlement, SettlementTier fromTier) =>
            MeetsPyramidSupportForUpgrade(settlement, fromTier) && CanPromoteNextTierRegionally(settlement, fromTier);

        private static void CountNearbySameFactionTiers(
            WorldObject settlement,
            WorldDominationSettings seth,
            out int sameTierNeighbors,
            out int t1Count,
            out int t2Count,
            out int t3Count,
            out int t4Count)
        {
            sameTierNeighbors = 0;
            t1Count = 0;
            t2Count = 0;
            t3Count = 0;
            t4Count = 0;
            if (settlement?.Faction == null || seth == null) return;

            float clusterRadius = Mathf.Max(0f, seth.localTierClusterRadius);
            SettlementTier selfTier = settlement.GetComponent<CompViralSpread>()?.tier ?? SettlementTier.T1;
            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();

            if (manager != null && manager.TryGetFactionSettlements(settlement.Faction, out var facSettlements) && facSettlements != null)
            {
                for (int ni = 0; ni < facSettlements.Count; ni++)
                {
                    var n = facSettlements[ni];
                    if (n == settlement || !DailyWorldSnapshot.IsSettlementStillValid(n)) continue;
                    if (Find.WorldGrid.ApproxDistanceInTiles(settlement.Tile, n.Tile) > clusterRadius) continue;
                    var nComp = n.GetComponent<CompViralSpread>();
                    if (nComp == null) continue;
                    if (nComp.tier == selfTier) sameTierNeighbors++;
                    if (nComp.tier == SettlementTier.T1) t1Count++;
                    if (nComp.tier == SettlementTier.T2) t2Count++;
                    if (nComp.tier == SettlementTier.T3) t3Count++;
                    if (nComp.tier == SettlementTier.T4) t4Count++;
                }
                return;
            }

            var allNeighborSettlements = Find.WorldObjects.Settlements;
            for (int ni = 0; ni < allNeighborSettlements.Count; ni++)
            {
                var n = allNeighborSettlements[ni];
                if (n.Faction != settlement.Faction || n == settlement) continue;
                if (Find.WorldGrid.ApproxDistanceInTiles(settlement.Tile, n.Tile) > clusterRadius) continue;
                var nComp = n.GetComponent<CompViralSpread>();
                if (nComp == null) continue;
                if (nComp.tier == selfTier) sameTierNeighbors++;
                if (nComp.tier == SettlementTier.T1) t1Count++;
                if (nComp.tier == SettlementTier.T2) t2Count++;
                if (nComp.tier == SettlementTier.T3) t3Count++;
                if (nComp.tier == SettlementTier.T4) t4Count++;
            }
        }

        private static bool TryUpgrade(
            WorldObject s,
            CompViralSpread comp,
            WorldComponent_SpreadManager manager,
            WorldDominationSettings seth)
        {
            if (comp.tier == SettlementTier.T4) return false;
            if (!CanUpgradeTierFully(s, comp.tier)) return false;

            CountNearbySameFactionTiers(s, seth, out int sameTierNeighbors, out _, out _, out _, out _);

            SettlementTier oldTier = comp.tier;
            SettlementTier nextTier = (oldTier == SettlementTier.T1) ? SettlementTier.T2 :
                                     (oldTier == SettlementTier.T2) ? SettlementTier.T3 : SettlementTier.T4;
            int requiredSameTierNeighbors = seth.GetSameTierNeighborsRequiredForUpgrade(oldTier);

            comp.SetState(nextTier);
            manager.AddLog(new SpreadLogEntry("TSA_WD_Log_Upgrade".Translate(oldTier.ToString(), comp.tier.ToString(), sameTierNeighbors, requiredSameTierNeighbors), s));
            WorldActions_Utils.RefreshMap();
            return true;
        }

        private static bool ExecuteExpansion(Settlement parent, CompViralSpread parentComp, WorldComponent_SpreadManager manager, bool strategicOnly = false)
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

            // Strategic Expand is safe tuck-away: full expand radius, never mid/late toward-player clamp.
            bool biasTowardPlayer = !strategicOnly && manager != null && WdEscalation.IsMidOrLate(manager);
            int expandTiles = manager != null
                ? WdEscalation.GetExpandTowardPlayerMaxTiles(WorldDominationMod.settings, manager.cachedEscalationStage)
                : 0;
            int minRadius = Mathf.Max(1, seth.expandMinRadius);
            int maxRadius = biasTowardPlayer
                ? Mathf.Min(seth.expandMaxRadius, Mathf.Max(minRadius, expandTiles))
                : seth.expandMaxRadius;
            if (maxRadius < minRadius) maxRadius = minRadius;
            // Scared expansion: prefer closer tiles after a recent intercept kill of an expand caravan.
            if (parentComp.IsExpansionRecentlyIntercepted() && maxRadius > minRadius)
                maxRadius = Mathf.Max(minRadius, (minRadius + maxRadius) / 2);

            int chosenTile = -1;
            bool hasTile;
            if (strategicOnly)
            {
                hasTile = TryFindStrategicExpandTile(parent, minRadius, maxRadius, seth, manager, out chosenTile);
            }
            else
            {
                hasTile = biasTowardPlayer
                    ? TryFindExpandTileTowardPlayer(parent, minRadius, maxRadius, seth, manager, out chosenTile)
                    : TryFindExpandTileRandomEarly(parent, minRadius, maxRadius, seth, manager, out chosenTile);

                // Mid/late corridor miss → same cheap random search as early game.
                if (!hasTile && biasTowardPlayer)
                    hasTile = TryFindExpandTileRandomEarly(parent, minRadius, maxRadius, seth, manager, out chosenTile);
            }

            if (!hasTile)
            {
                manager.AddLog(new SpreadLogEntry("TSA_WD_Log_Expand_SkippedNoValidTile".Translate(parent.LabelCap), parent));
                return false;
            }

            parentComp.expansionCooldownTick = Find.TickManager.TicksGame + Mathf.RoundToInt(seth.cooldownExpandDays * ExpandCdMultFor(parent.Faction) * 60000f);

            float cost = parentComp.strength * 0.25f;
            parentComp.strength -= cost;
            parentComp.CheckTierUpdate(false);

            // Log before pathing so a StartPath failure cannot swallow the dispatch entry.
            manager.AddLog(new SpreadLogEntry(
                strategicOnly
                    ? "TSA_WD_Log_StrategicExpeditionLaunched".Translate(parent.LabelCap)
                    : "TSA_WD_Log_ExpeditionLaunched".Translate(parent.LabelCap),
                parent, chosenTile));

            WorldObject_Traveler traveler = (WorldObject_Traveler)WorldObjectMaker.MakeWorldObject(DefDatabase<WorldObjectDef>.GetNamed("TSA_WD_Traveler_Expansion"));
            traveler.Tile = parent.Tile;
            traveler.SetFaction(parent.Faction);
            traveler.mission = TravelerMission.Expansion;
            traveler.travelerStrength = cost;
            traveler.initialStrength = cost;
            traveler.originObject = parent;
            traveler.strategicExpand = strategicOnly;
            AdvancedBuildingEffects.ApplyNpcLandSpeed(traveler, parent);

            Find.WorldObjects.Add(traveler);
            traveler.pather.StartPath(PlanetSurfaceWorldActions.PlanetTileForWdTravel(chosenTile, parent));
            return true;
        }

        private const int StrategicExpandCorridorEnemyCount = 5;
        private const float StrategicExpandCorridorHalfWidth = 1f;
        private const float StrategicExpandTowardPlayerWeight = 1.5f;

        private struct StrategicHostile
        {
            public int tile;
            public float maxLos;
            public float distToParent;
        }

        private static readonly List<StrategicHostile> strategicHostileScratch = new List<StrategicHostile>(64);
        private static readonly List<StrategicHostile> strategicCorridorScratch = new List<StrategicHostile>(8);

        /// <summary>
        /// Sample up to K expand candidates; hard-reject MaxLOS disks; score clearance, geodesic corridors to closest 5, and toward-player.
        /// </summary>
        private static bool TryFindStrategicExpandTile(
            Settlement parent,
            int minRadius,
            int maxRadius,
            WorldDominationSettings seth,
            WorldComponent_SpreadManager manager,
            out int chosenTile)
        {
            chosenTile = -1;
            if (parent?.Faction == null) return false;
            WorldGrid grid = Find.WorldGrid;
            if (grid == null) return false;

            CollectNearbyStrategicHostiles(parent, maxRadius);
            BuildStrategicCorridorEndpoints();

            int playerTile = -1;
            float dParentPlayer = 0f;
            Settlement colony = InfluenceUtils.GetPlayerColony();
            if (colony != null && colony.Tile.Valid)
            {
                playerTile = colony.Tile.tileId;
                dParentPlayer = grid.ApproxDistanceInTiles(parent.Tile.tileId, playerTile);
            }

            int samples = Mathf.Max(1, seth?.settlementIntelStrategicExpandSamples
                ?? WorldDominationSettings.DefSettlementIntelStrategicExpandSamples);
            float bestScore = float.MinValue;
            int bestTile = -1;

            for (int i = 0; i < samples; i++)
            {
                if (!TryFindExpandTileRandomEarly(parent, minRadius, maxRadius, seth, manager, out int tile))
                    continue;
                if (!TryScoreStrategicExpandTile(parent.Tile.tileId, tile, playerTile, dParentPlayer, out float score))
                    continue;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestTile = tile;
                }
            }

            if (bestTile < 0) return false;
            chosenTile = bestTile;
            return true;
        }

        private static void CollectNearbyStrategicHostiles(Settlement parent, int maxRadius)
        {
            strategicHostileScratch.Clear();
            if (parent?.Faction == null) return;
            WorldGrid grid = Find.WorldGrid;
            if (grid == null) return;

            int parentTile = parent.Tile.tileId;
            List<Settlement> settlements = Find.WorldObjects?.Settlements;
            if (settlements == null) return;

            for (int i = 0; i < settlements.Count; i++)
            {
                Settlement s = settlements[i];
                if (s?.Faction == null || s.Faction == parent.Faction) continue;
                if (!WorldActions_Utils.SafeHostileTo(s.Faction, parent.Faction)) continue;
                if (!PlanetSurfaceWorldActions.IsPlanetSurfaceWorldObjectForWorldActions(s)) continue;

                float maxLos = WorldComponent_SettlementIntel.GetMaxLos(s);
                if (maxLos <= 0f) maxLos = WorldComponent_SettlementIntel.ComputeSettlementMaxLos(s);
                float dist = grid.ApproxDistanceInTiles(parentTile, s.Tile.tileId);
                if (dist > maxRadius + maxLos) continue;

                strategicHostileScratch.Add(new StrategicHostile
                {
                    tile = s.Tile.tileId,
                    maxLos = maxLos,
                    distToParent = dist
                });
            }
        }

        private static void BuildStrategicCorridorEndpoints()
        {
            strategicCorridorScratch.Clear();
            int n = strategicHostileScratch.Count;
            if (n <= 0) return;

            // Insertion-pick closest N without full sort alloc.
            for (int i = 0; i < n; i++)
            {
                StrategicHostile h = strategicHostileScratch[i];
                if (strategicCorridorScratch.Count < StrategicExpandCorridorEnemyCount)
                {
                    strategicCorridorScratch.Add(h);
                    continue;
                }

                int worst = 0;
                for (int j = 1; j < strategicCorridorScratch.Count; j++)
                {
                    if (strategicCorridorScratch[j].distToParent > strategicCorridorScratch[worst].distToParent)
                        worst = j;
                }
                if (h.distToParent < strategicCorridorScratch[worst].distToParent)
                    strategicCorridorScratch[worst] = h;
            }
        }

        /// <summary>Hard-reject MaxLOS; score = clearance − corridor − towardPlayer.</summary>
        private static bool TryScoreStrategicExpandTile(
            int parentTile,
            int candidateTile,
            int playerTile,
            float dParentPlayer,
            out float score)
        {
            score = 0f;
            WorldGrid grid = Find.WorldGrid;
            if (grid == null || candidateTile < 0) return false;

            float clearance = 100f;
            for (int i = 0; i < strategicHostileScratch.Count; i++)
            {
                StrategicHostile h = strategicHostileScratch[i];
                float d = grid.ApproxDistanceInTiles(h.tile, candidateTile);
                if (d <= h.maxLos) return false;
                float edge = d - h.maxLos;
                if (edge < clearance) clearance = edge;
            }

            float corridorPenalty = 0f;
            for (int i = 0; i < strategicCorridorScratch.Count; i++)
            {
                StrategicHostile h = strategicCorridorScratch[i];
                float dParentCand = grid.ApproxDistanceInTiles(parentTile, candidateTile);
                float dCandEnemy = grid.ApproxDistanceInTiles(candidateTile, h.tile);
                float slack = Mathf.Abs(dParentCand + dCandEnemy - h.distToParent);
                corridorPenalty += Mathf.Max(0f, StrategicExpandCorridorHalfWidth + 1f - slack);
            }

            float towardPlayerPenalty = 0f;
            if (playerTile >= 0)
            {
                float dCandPlayer = grid.ApproxDistanceInTiles(candidateTile, playerTile);
                towardPlayerPenalty = Mathf.Max(0f, dParentPlayer - dCandPlayer) * StrategicExpandTowardPlayerWeight;
            }

            score = clearance - corridorPenalty - towardPlayerPenalty;
            return true;
        }

        /// <summary>
        /// Isolation Pressure: expand toward a player-centered annulus (not parent-radius).
        /// Ignores parent expand CD; stamps expand CD after launch. Sets <paramref name="markIsolationPressure"/> on the traveler.
        /// </summary>
        public static bool TryLaunchForcedExpansionNearPlayer(
            Settlement parent,
            CompViralSpread parentComp,
            WorldComponent_SpreadManager manager,
            int playerAnchorTile,
            int minFromPlayer,
            int maxFromPlayer,
            bool markIsolationPressure,
            HashSet<int> excludeDestTiles = null)
        {
            if (parent == null || parentComp == null || manager == null) return false;
            var seth = WorldDominationMod.settings;
            if (seth == null) return false;
            if (!PlanetSurfaceWorldActions.IsPlanetSurfaceWorldObjectForWorldActions(parent)) return false;
            if (playerAnchorTile < 0 || !Find.WorldGrid.InBounds(playerAnchorTile)) return false;

            int minR = Mathf.Max(1, minFromPlayer);
            int maxR = Mathf.Max(minR, maxFromPlayer);

            int totalGlobal = 0;
            var globalSettlements = Find.WorldObjects.Settlements;
            for (int i = 0; i < globalSettlements.Count; i++)
            {
                var gs = globalSettlements[i];
                if (gs.Faction != null && !gs.Faction.IsPlayer && !gs.Faction.def.hidden
                    && PlanetSurfaceWorldActions.IsPlanetSurfaceWorldObjectForWorldActions(gs))
                    totalGlobal++;
            }
            if (totalGlobal >= seth.maxSettlements) return false;

            if (!TryFindExpandTileNearPlayerAnchor(
                    parent, playerAnchorTile, minR, maxR, seth, manager, excludeDestTiles, out int chosenTile))
                return false;

            parentComp.expansionCooldownTick = Find.TickManager.TicksGame + Mathf.RoundToInt(seth.cooldownExpandDays * ExpandCdMultFor(parent.Faction) * 60000f);

            float cost = parentComp.strength * 0.25f;
            parentComp.strength -= cost;
            parentComp.CheckTierUpdate(false);

            manager.AddLog(new SpreadLogEntry(
                markIsolationPressure
                    ? "TSA_WD_Log_IsolationExpeditionLaunched".Translate(parent.LabelCap)
                    : "TSA_WD_Log_ExpeditionLaunched".Translate(parent.LabelCap),
                parent, chosenTile));

            WorldObject_Traveler traveler = (WorldObject_Traveler)WorldObjectMaker.MakeWorldObject(DefDatabase<WorldObjectDef>.GetNamed("TSA_WD_Traveler_Expansion"));
            traveler.Tile = parent.Tile;
            traveler.SetFaction(parent.Faction);
            traveler.mission = TravelerMission.Expansion;
            traveler.travelerStrength = cost;
            traveler.initialStrength = cost;
            traveler.originObject = parent;
            traveler.isolationPressureExpand = markIsolationPressure;
            AdvancedBuildingEffects.ApplyNpcLandSpeed(traveler, parent);

            Find.WorldObjects.Add(traveler);
            traveler.pather.StartPath(PlanetSurfaceWorldActions.PlanetTileForWdTravel(chosenTile, parent));
            excludeDestTiles?.Add(chosenTile);
            return true;
        }

        private static bool TryFindExpandTileNearPlayerAnchor(
            Settlement parent,
            int playerAnchorTile,
            int minFromPlayer,
            int maxFromPlayer,
            WorldDominationSettings seth,
            WorldComponent_SpreadManager manager,
            HashSet<int> excludeDestTiles,
            out int chosenTile)
        {
            chosenTile = -1;
            if (parent == null) return false;

            for (int attempt = 0; attempt < EarlyRandomSeedAttempts; attempt++)
            {
                if (!TryPickRandomAnnulusSeed(new PlanetTile(playerAnchorTile, Find.WorldGrid[playerAnchorTile].Layer), minFromPlayer, maxFromPlayer, out int seed))
                    continue;
                if (excludeDestTiles != null && excludeDestTiles.Contains(seed)) continue;
                if (IsValidExpandTileNearPlayer(seed, parent, playerAnchorTile, minFromPlayer, maxFromPlayer, seth, manager))
                {
                    chosenTile = seed;
                    return true;
                }
                if (TryFindFirstValidNearPlayerFromSeed(
                        seed, parent, playerAnchorTile, minFromPlayer, maxFromPlayer, seth, manager, excludeDestTiles, out chosenTile))
                    return true;
            }

            return TryFindFirstValidNearPlayerFromSeed(
                playerAnchorTile, parent, playerAnchorTile, minFromPlayer, maxFromPlayer, seth, manager, excludeDestTiles, out chosenTile,
                searchRadius: maxFromPlayer);
        }

        private static bool TryFindFirstValidNearPlayerFromSeed(
            int seedTileId,
            Settlement parent,
            int playerAnchorTile,
            int minFromPlayer,
            int maxFromPlayer,
            WorldDominationSettings seth,
            WorldComponent_SpreadManager manager,
            HashSet<int> excludeDestTiles,
            out int chosenTile,
            int searchRadius = LocalSearchRadiusFromSeed)
        {
            chosenTile = -1;
            if (parent == null || seedTileId < 0) return false;

            if ((excludeDestTiles == null || !excludeDestTiles.Contains(seedTileId))
                && IsValidExpandTileNearPlayer(seedTileId, parent, playerAnchorTile, minFromPlayer, maxFromPlayer, seth, manager))
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
                    if (excludeDestTiles != null && excludeDestTiles.Contains(pt.tileId)) return false;
                    if (IsValidExpandTileNearPlayer(pt.tileId, parent, playerAnchorTile, minFromPlayer, maxFromPlayer, seth, manager))
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

        private static bool IsValidExpandTileNearPlayer(
            int tileId,
            Settlement parent,
            int playerAnchorTile,
            int minFromPlayer,
            int maxFromPlayer,
            WorldDominationSettings seth,
            WorldComponent_SpreadManager manager)
        {
            if (parent == null || tileId < 0 || Find.WorldGrid == null) return false;
            if (!Find.WorldGrid.InBounds(tileId)) return false;

            int d = Mathf.RoundToInt(Find.WorldGrid.ApproxDistanceInTiles(playerAnchorTile, tileId));
            if (d < minFromPlayer || d > maxFromPlayer) return false;
            if (!TileFinder.IsValidTileForNewSettlement(tileId)) return false;
            if (Find.WorldObjects.AnyWorldObjectAt(tileId)) return false;
            if (Outpost_EstablishmentRequirements.IsTileBlockedByMinDistanceCached(tileId)) return false;
            if (IsTargetSaturated(tileId, parent.Faction, seth, manager)) return false;
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

            if (expandRingScratch.Count == 0) return false;

            // Cheap seed bias: score a few ring tiles, then first-valid from the best (no extra validity floods).
            const int seedScoreSamples = 3;
            int n = expandRingScratch.Count;
            int samples = Mathf.Min(seedScoreSamples, n);
            for (int i = 0; i < samples; i++)
            {
                int j = Rand.Range(i, n);
                int tmp = expandRingScratch[i];
                expandRingScratch[i] = expandRingScratch[j];
                expandRingScratch[j] = tmp;
            }

            int bestTile = expandRingScratch[0];
            float bestScore = NpcSettlementSubtypeUtil.ExpandTileAttractiveness(bestTile);
            for (int i = 1; i < samples; i++)
            {
                int tile = expandRingScratch[i];
                float score = NpcSettlementSubtypeUtil.ExpandTileAttractiveness(tile);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestTile = tile;
                }
            }

            seedTileId = bestTile;
            return true;
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
            float checkRadius = Mathf.Max(0f, seth.localTierClusterRadius);

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
