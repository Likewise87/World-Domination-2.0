using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using RimWorld.Planet;
using Verse;
using UnityEngine;

namespace TSA_WorldDomination
{
    public enum RoadProjectClearReason
    {
        PlayerCancel,
        Completed,
        AbortedInvalidTarget,
        SettlementDestroyed,
        FactionHostile
    }

    [StaticConstructorOnStartup]
    public static class WorldActions_Roads
    {
        /// <summary>Do not dispatch road builders if a hostile settlement is within this many world tiles of the actor.</summary>
        private const int RoadBuildMinHostileClearanceTiles = 2;
        private const string RoadsOfTheRimPackageId = "Mlie.RoadsOfTheRim";

        /// <summary>Baseline: this much cumulative Construction skill completes one dirt segment in one day (at dirt Work default).</summary>
        private const float OutpostRoadReferenceConstructionSkill = 10f;

        private static bool _fallbackRoadResolved;
        private static RoadDef _fallbackRoadDef;
        private static bool rotrActiveResolved;
        private static bool rotrActive;

        /// <summary>Per-world cache: only road-related draw layers need SetDirty after OverlayRoad (not all layers).</summary>
        private static World cachedWorldForRoadLayerDirty;
        private static List<object> cachedRoadDrawLayers;
        /// <summary>Coalesce redraw work to one flush per tick/event when multiple segments apply in the same stack.</summary>
        private static bool roadLayerDirtyFlushQueued;
        private static readonly Dictionary<Type, MethodInfo> setDirtyMethodByLayerType = new Dictionary<Type, MethodInfo>();

        static WorldActions_Roads()
        {
            LongEventHandler.ExecuteWhenFinished(ApplyVanillaRoadMovementSettings);
        }

        public static bool RoadsOfTheRimActive
        {
            get
            {
                if (!rotrActiveResolved)
                {
                    rotrActiveResolved = true;
                    rotrActive = IsRoadsOfTheRimModActive()
                        || DefDatabase<RoadDef>.GetNamed("DirtRoadBuilt", false) != null
                        || DefDatabase<RoadDef>.GetNamed("StoneRoadBuilt", false) != null
                        || DefDatabase<RoadDef>.GetNamed("AsphaltRoad", false) != null;
                }
                return rotrActive;
            }
        }

        private static bool IsRoadsOfTheRimModActive()
        {
            if (ModsConfig.IsActive(RoadsOfTheRimPackageId)) return true;
            List<ModContentPack> mods = LoadedModManager.RunningModsListForReading;
            for (int i = 0; i < mods.Count; i++)
            {
                string packageId = mods[i]?.PackageId;
                if (string.Equals(packageId, RoadsOfTheRimPackageId, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        public static void ApplyVanillaRoadMovementSettings()
        {
            WorldDominationSettings s = WorldDominationMod.settings;
            if (s == null) return;

            if (!RoadsOfTheRimActive)
            {
                SetRoadMovement("DirtRoad", s.GetFallbackRoadMovement(SettlementTier.T1));
                SetRoadMovement("StoneRoad", s.GetFallbackRoadMovement(SettlementTier.T2));
                SetRoadMovement("AncientAsphaltRoad", s.GetFallbackRoadMovement(SettlementTier.T3));
            }

            ApplyRoadWinterReductionSettings();
        }

        /// <summary>
        /// Writes winter reduction into Roads of the Rim <c>winterFactor</c> when present;
        /// otherwise our Harmony winter patch reads the same settings.
        /// </summary>
        public static void ApplyRoadWinterReductionSettings()
        {
            WorldDominationSettings s = WorldDominationMod.settings;
            if (s == null) return;

            float dirt = s.GetFallbackRoadWinterReduction(SettlementTier.T1);
            float stone = s.GetFallbackRoadWinterReduction(SettlementTier.T2);
            float asphalt = s.GetFallbackRoadWinterReduction(SettlementTier.T3);

            if (RoadsOfTheRimActive)
            {
                SetRotRWinterFactor("DirtRoad", dirt);
                SetRotRWinterFactor("DirtRoadBuilt", dirt);
                SetRotRWinterFactor("DirtPath", dirt);
                SetRotRWinterFactor("StoneRoad", stone);
                SetRotRWinterFactor("StoneRoadBuilt", stone);
                SetRotRWinterFactor("AncientAsphaltRoad", asphalt);
                SetRotRWinterFactor("AsphaltRoad", asphalt);
                SetRotRWinterFactor("AncientAsphaltHighway", asphalt);
            }
        }

        public static float GetWinterReductionForRoadDef(RoadDef road)
        {
            if (road == null) return 0f;
            var s = WorldDominationMod.settings;
            if (s == null) return 0f;

            string n = road.defName ?? "";
            if (n.IndexOf("Asphalt", System.StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Highway", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return s.GetFallbackRoadWinterReduction(SettlementTier.T3);
            if (n.IndexOf("Stone", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return s.GetFallbackRoadWinterReduction(SettlementTier.T2);
            if (n.IndexOf("Dirt", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return s.GetFallbackRoadWinterReduction(SettlementTier.T1);
            return s.GetFallbackRoadWinterReduction(SettlementTier.T1);
        }

        private static void SetRoadMovement(string defName, float movementCostMultiplier)
        {
            RoadDef road = DefDatabase<RoadDef>.GetNamed(defName, false);
            if (road != null)
                road.movementCostMultiplier = movementCostMultiplier;
        }

        private static void SetRotRWinterFactor(string defName, float winterFactor)
        {
            RoadDef road = DefDatabase<RoadDef>.GetNamed(defName, false);
            if (road?.modExtensions == null) return;

            for (int i = 0; i < road.modExtensions.Count; i++)
            {
                DefModExtension ext = road.modExtensions[i];
                if (ext == null) continue;
                if (ext.GetType().FullName != "RoadsOfTheRim.DefModExtension_RotR_RoadDef") continue;
                FieldInfo field = ext.GetType().GetField("winterFactor", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null && field.FieldType == typeof(float))
                    field.SetValue(ext, Mathf.Clamp01(winterFactor));
                return;
            }
        }

        public static int GetMinConstructionToBuildRoad(SettlementTier tier)
        {
            if (RoadsOfTheRimActive)
            {
                if (tier == SettlementTier.T3 || tier == SettlementTier.T4) return 25;
                if (tier == SettlementTier.T2) return 15;
                return 5;
            }

            return WorldDominationMod.settings?.GetFallbackRoadMinConstruction(tier) ?? 0;
        }

        /// <summary>Relative segment duration vs dirt, derived from Work settings (defaults preserve 1 : 1.5 : 2).</summary>
        public static float GetRoadTierDurationMultiplier(SettlementTier tier)
        {
            var s = WorldDominationMod.settings;
            float dirtWork = s != null
                ? s.GetFallbackRoadWork(SettlementTier.T1)
                : WorldDominationSettings.DefFallbackDirtRoadWork;
            float work = s != null
                ? s.GetFallbackRoadWork(tier)
                : (tier == SettlementTier.T3 || tier == SettlementTier.T4
                    ? WorldDominationSettings.DefFallbackAsphaltRoadWork
                    : tier == SettlementTier.T2
                        ? WorldDominationSettings.DefFallbackStoneRoadWork
                        : WorldDominationSettings.DefFallbackDirtRoadWork);
            if (dirtWork < 1f) dirtWork = 1f;
            return Mathf.Max(0.01f, work / dirtWork);
        }

        /// <summary>Ticks of progress work for one segment at <see cref="OutpostRoadReferenceConstructionSkill"/> cumulative Construction.</summary>
        public static float GetRoadProgressRequiredTicks(SettlementTier tier)
        {
            return OutpostRoadReferenceConstructionSkill * GenDate.TicksPerDay * GetRoadTierDurationMultiplier(tier);
        }

        /// <summary>Localized label for the highest road tier this outpost can start (by cumulative Construction skill).</summary>
        public static string GetHighestBuildableRoadTierLabel(float totalConstruction)
        {
            if (totalConstruction >= GetMinConstructionToBuildRoad(SettlementTier.T3))
                return "TSA_WD_RoadAsphalt".Translate().ToString();
            if (totalConstruction >= GetMinConstructionToBuildRoad(SettlementTier.T2))
                return "TSA_WD_RoadStone".Translate().ToString();
            return "TSA_WD_RoadDirt".Translate().ToString();
        }

        /// <summary>Localized road tier label (dirt / stone / asphalt).</summary>
        public static string GetRoadTierLabel(SettlementTier tier)
        {
            if (tier == SettlementTier.T3 || tier == SettlementTier.T4)
                return "TSA_WD_RoadAsphalt".Translate().ToString();
            if (tier == SettlementTier.T2)
                return "TSA_WD_RoadStone".Translate().ToString();
            return "TSA_WD_RoadDirt".Translate().ToString();
        }

        /// <summary>Strength cost to dispatch one road-builder crew for this road tier (same basis as NPC road launches).</summary>
        public static float GetExpeditionStrengthCost(SettlementTier roadTier)
        {
            float cost = WorldDominationMod.settings != null
                ? WorldDominationMod.settings.GetFallbackRoadExpeditionStrength(roadTier)
                : WorldDominationSettings.DefFallbackDirtRoadExpeditionStrength;
            return Mathf.Max(1f, cost);
        }

        /// <summary>Cumulative Construction skill used as work speed when filling road segment progress.</summary>
        public static float GetRoadProgressWorkSpeed(WorldObject actor)
        {
            if (actor is WorldObject_WD_Outpost wdOutpost)
            {
                float speed = wdOutpost.TotalConstructionSkill();
                return speed * (1f + OutpostExpertUtility.GetEngineerRoadSpeedBonus(wdOutpost));
            }
            if (actor is Settlement settlement && ColonyWorldBuildUtility.IsPlayerColonyBuildActor(settlement))
                return ColonyWorldBuildUtility.GetConstructionSkillEffective(settlement);
            if (actor is Settlement && actor.GetComponent<CompViralSpread>() is CompViralSpread comp && comp.playerOrderedRoad)
                return GetAssumedConstructionForSettlementTier(comp.tier);
            return 0f;
        }

        /// <summary>Tier-assumed Construction for player-ordered NPC settlement roads (T1=5 … T4=30).</summary>
        public static float GetAssumedConstructionForSettlementTier(SettlementTier tier)
        {
            return tier switch
            {
                SettlementTier.T4 => 30f,
                SettlementTier.T3 => 20f,
                SettlementTier.T2 => 12f,
                _ => 5f
            };
        }

        /// <summary>Highest road tier a settlement may build when ordered by the player.</summary>
        public static SettlementTier GetMaxBuildableRoadTierForSettlement(SettlementTier settlementTier)
        {
            if (settlementTier >= SettlementTier.T3) return SettlementTier.T3;
            if (settlementTier == SettlementTier.T2) return SettlementTier.T2;
            return SettlementTier.T1;
        }

        public static int CountRoadWorkSegmentsOnPath(WorldPath path, SettlementTier tier)
        {
            if (path == null || !path.Found) return 0;
            return CountRoadWorkSegmentsOnTileList(PathNodesToTileIds(path.NodesReversed), tier);
        }

        /// <summary>Count upgrade segments on a dest-first tile list (same order as <see cref="WorldPath.NodesReversed"/>).</summary>
        public static int CountRoadWorkSegmentsOnTileList(List<int> nodesDestFirst, SettlementTier tier)
        {
            if (nodesDestFirst == null || nodesDestFirst.Count < 2) return 0;
            PlanetLayer layer = PlanetSurfaceWorldActions.WdSurfaceLayer;
            if (layer == null) return 0;
            RoadDef targetRoad = GetRoadDefByTier(tier);
            int count = 0;
            for (int i = nodesDestFirst.Count - 1; i > 0; i--)
            {
                if (ShouldUpgradeRoad(
                        new PlanetTile(nodesDestFirst[i], layer),
                        new PlanetTile(nodesDestFirst[i - 1], layer),
                        targetRoad))
                    count++;
            }
            return count;
        }

        public static int CountRemainingWorkSegments(CompViralSpread comp)
        {
            if (comp == null || comp.roadTargetTile < 0) return 0;
            if (comp.cachedRoadPathTiles != null && comp.cachedRoadPathTiles.Count >= 2)
            {
                if (comp.roadIsClearing)
                    return CountRoadRemovalSegmentsOnTileList(comp.cachedRoadPathTiles);
                return CountRoadWorkSegmentsOnTileList(comp.cachedRoadPathTiles, comp.selectedRoadTier);
            }

            if (comp.parent == null) return 0;
            using (WorldPath path = PlanetSurfaceWorldActions.WdSurfaceLayer?.Pather.FindPath(
                new PlanetTile(comp.parent.Tile, PlanetSurfaceWorldActions.WdSurfaceLayer),
                new PlanetTile(comp.roadTargetTile, PlanetSurfaceWorldActions.WdSurfaceLayer), null))
            {
                if (comp.roadIsClearing)
                    return path != null && path.Found
                        ? CountRoadRemovalSegmentsOnTileList(PathNodesToTileIds(path.NodesReversed))
                        : 0;
                return CountRoadWorkSegmentsOnPath(path, comp.selectedRoadTier);
            }
        }

        public static int GetBuiltSegmentCount(CompViralSpread comp)
        {
            if (comp == null || comp.playerOrderedRoadInitialSegments <= 0) return 0;
            int remaining = CountRemainingWorkSegments(comp);
            return Mathf.Max(0, comp.playerOrderedRoadInitialSegments - remaining);
        }

        /// <summary>Estimated in-game days to fill progress for one road segment at current Construction skill. Excludes caravan travel.</summary>
        public static float GetEstimatedDaysPerRoadSegment(WorldObject actor, SettlementTier tier)
        {
            if (actor == null) return -1f;
            float workSpeed = GetRoadProgressWorkSpeed(actor);
            if (workSpeed < 0.01f) return -1f;
            float ticks = GetRoadProgressRequiredTicks(tier) / workSpeed;
            return ticks / GenDate.TicksPerDay;
        }

        /// <summary>Outpost overload kept for call sites.</summary>
        public static float GetEstimatedDaysPerRoadSegment(WorldObject_WD_Outpost outpost, SettlementTier tier) =>
            GetEstimatedDaysPerRoadSegment((WorldObject)outpost, tier);

        // SURGICAL: Changed to bool for Waterfall Fallback support
        public static bool AttemptBuildRoad(Settlement actor, CompViralSpread comp, WorldComponent_SpreadManager manager)
        {
            var seth = WorldDominationMod.settings;

            // --- SURGICAL: Respect the independent Road Cooldown ---
            if (comp.IsRoadOnCooldown)
            {
                manager.AddLog(new SpreadLogEntry("TSA_WD_Log_Road_SkippedCooldown".Translate(actor.LabelCap), actor));
                return false;
            }

            if (comp.HasActivePlayerOrderedRoadProject)
                return false;

            if (!PlanetSurfaceWorldActions.IsPlanetSurfaceWorldObjectForWorldActions(actor))
            {
                WDVerbose.Msg($"AttemptBuildRoad: skip non-surface actor {actor.LabelCap}");
                return false;
            }

            var allSettlements = Find.WorldObjects.Settlements;
            Settlement closestEnemy = null;
            int closestEnemyDist = int.MaxValue;
            Settlement bestOwnTarget = null;
            int bestOwnDist = int.MaxValue;
            Settlement bestAllyTarget = null;
            int bestAllyDist = int.MaxValue;

            float roadScanApprox = seth.maxRoadRangeNpc + 45f;
            for (int i = 0; i < allSettlements.Count; i++)
            {
                Settlement s = allSettlements[i];
                if (s.Faction == null || s.Faction.def.hidden) continue;
                if (!PlanetSurfaceWorldActions.IsPlanetSurfaceWorldObjectForWorldActions(s)) continue;

                float approx = Find.WorldGrid.ApproxDistanceInTiles(actor.Tile, s.Tile);

                if (WorldActions_Utils.SafeHostileTo(s.Faction, actor.Faction))
                {
                    if (approx > 45f) continue;
                    int d = WorldActions_Utils.GetDistance(actor.Tile, s.Tile, manager);
                    if (d < closestEnemyDist) { closestEnemy = s; closestEnemyDist = d; }
                }
                else if (s != actor)
                {
                    if (approx > roadScanApprox) continue;
                    bool sameF = s.Faction == actor.Faction;
                    bool allied = sameF || WorldActions_Utils.SafeRelationKindWith(actor.Faction, s.Faction) == FactionRelationKind.Ally;
                    if (!allied) continue;
                    int d = WorldActions_Utils.GetDistance(actor.Tile, s.Tile, manager);
                    if (sameF) { if (d < bestOwnDist) { bestOwnTarget = s; bestOwnDist = d; } }
                    else       { if (d < bestAllyDist) { bestAllyTarget = s; bestAllyDist = d; } }
                }
            }

            if (closestEnemy != null)
            {
                if (closestEnemyDist <= RoadBuildMinHostileClearanceTiles)
                {
                    manager.AddLog(new SpreadLogEntry("TSA_WD_Log_Road_AbortedDanger".Translate(actor.LabelCap, closestEnemy.LabelCap, closestEnemyDist), actor, closestEnemy)
                    {
                        labelA = actor.LabelCap,
                        labelB = closestEnemy.LabelCap
                    });
                    return false;
                }
            }

            Settlement target = bestOwnTarget ?? bestAllyTarget;

            if (target == null)
            {
                manager.AddLog(new SpreadLogEntry("TSA_WD_Log_Road_SkippedNoAllyTarget".Translate(actor.LabelCap), actor));
                return false;
            }

            int dist = WorldActions_Utils.GetDistance(actor.Tile, target.Tile, manager);
            if (dist > seth.maxRoadRangeNpc)
            {
                manager.AddLog(new SpreadLogEntry("TSA_WD_Log_Road_SkippedTargetTooFar".Translate(actor.LabelCap, target.LabelCap, dist, seth.maxRoadRangeNpc), actor, target));
                return false;
            }

            return LaunchRoadBuilder(actor, target, manager);
        }

        /// <summary>True if a road-builder traveler was spawned and still exists (path started). False if project finished (no gaps) or launch failed.</summary>
        public static bool LaunchRoadBuilderFromOutpost(WorldObject actor)
        {
            var comp = actor.GetComponent<CompViralSpread>();
            if (comp == null || comp.roadTargetTile == -1) return false;

            if (!TryPopulateOutpostRoadPathCache(actor.Tile, comp.roadTargetTile, comp, comp.selectedRoadTier, out int nextGapTile))
            {
                ClearRoadProject(comp, comp.playerOrderedRoad ? RoadProjectClearReason.AbortedInvalidTarget : RoadProjectClearReason.Completed);
                return false;
            }

            if (nextGapTile == -1)
            {
                ClearRoadProject(comp, RoadProjectClearReason.Completed);
                return false;
            }

            if (!ColonyWorldBuildRequirements.MeetsRoadRequirements(actor, comp.selectedRoadTier))
                return false;

            comp.cachedWorkTile = nextGapTile;
            float cost = GetExpeditionStrengthCost(comp.selectedRoadTier);
            if (!WorldActions_Utils.CanAffordExpeditionLeavingGarrison(comp, cost)) return false;
            return SpawnRoadTraveler(actor, nextGapTile, comp.selectedRoadTier);
        }

        /// <summary>After a segment is paved, move the orange path + worksite marker to the next gap immediately (do not wait for <see cref="CompViralSpread.roadProgress"/>).</summary>
        public static void RefreshOutpostRoadProjectVisualsAfterSegment(WorldObject origin)
        {
            if (origin == null || origin.Destroyed) return;
            var comp = origin.GetComponent<CompViralSpread>();
            if (comp == null || comp.roadTargetTile < 0) return;

            if (!TryPopulateOutpostRoadPathCache(origin.Tile, comp.roadTargetTile, comp, comp.selectedRoadTier, out int workTile))
            {
                ClearRoadProject(comp, comp.playerOrderedRoad ? RoadProjectClearReason.AbortedInvalidTarget : RoadProjectClearReason.Completed);
                return;
            }

            comp.cachedWorkTile = workTile;
            if (workTile == -1)
                ClearRoadProject(comp, RoadProjectClearReason.Completed);
        }

        public static void ClearRoadProject(CompViralSpread comp, RoadProjectClearReason reason)
        {
            ClearRoadProject(comp, reason, refundFactionOverride: null);
        }

        /// <param name="refundFactionOverride">
        /// Use when clearing during destroy/buy and <see cref="WorldObject.Faction"/> may already be unreliable.
        /// </param>
        public static void ClearRoadProject(CompViralSpread comp, RoadProjectClearReason reason, Faction refundFactionOverride)
        {
            if (comp == null) return;

            Faction refundFaction = refundFactionOverride ?? comp.parent?.Faction;
            WorldObject builder = comp.parent;

            if (comp.playerOrderedRoad && !comp.playerOrderedRoadGoodwillRefunded
                && reason != RoadProjectClearReason.Completed
                && comp.playerOrderedRoadPerSegmentRate > 0f)
            {
                int remaining = CountRemainingWorkSegments(comp);
                int refund = WorldDominationSettings.CalcOrderedRoadRefund(comp.playerOrderedRoadPerSegmentRate, remaining);
                if (refund > 0 && refundFaction != null)
                {
                    WorldObject target = ResolveRoadTargetLabelObject(comp);
                    GoodwillChangeNotifier.RefundOrderedRoad(refundFaction, builder, target, refund, remaining, reason);
                    comp.playerOrderedRoadGoodwillRefunded = true;
                }
            }

            DestroyActiveRoadBuildersFrom(builder);
            comp.roadTargetTile = -1;
            comp.roadTargetName = string.Empty;
            comp.roadProgress = 0f;
            comp.cachedRoadPathTiles?.Clear();
            comp.roadWaypointTiles?.Clear();
            comp.cachedWorkTile = -1;
            comp.roadTargetUsesDetachedStart = false;
            comp.roadIsClearing = false;
            comp.NotifyRoadBuilderReturned();
            if (comp.playerOrderedRoad)
                comp.ClearPlayerOrderedRoadBilling();
        }

        private static WorldObject ResolveRoadTargetLabelObject(CompViralSpread comp)
        {
            if (comp == null || comp.roadTargetTile < 0) return comp?.parent;
            var atTarget = Find.WorldObjects.ObjectsAt(comp.roadTargetTile).ToList();
            for (int i = 0; i < atTarget.Count; i++)
            {
                if (atTarget[i] is Settlement || atTarget[i] is WorldObject_WD_Outpost)
                    return atTarget[i];
            }
            return comp.parent;
        }

        public static void DestroyActiveRoadBuildersFrom(WorldObject origin)
        {
            if (origin == null || Find.WorldObjects == null) return;
            var allWo = Find.WorldObjects.AllWorldObjects;
            for (int wi = allWo.Count - 1; wi >= 0; wi--)
            {
                if (allWo[wi] is WorldObject_Traveler t && t.mission == TravelerMission.RoadBuilding && t.originObject == origin)
                    t.Destroy();
            }
        }

        private static void ClearOutpostRoadProject(CompViralSpread comp)
        {
            ClearRoadProject(comp, RoadProjectClearReason.Completed);
        }

        /// <summary>
        /// Refresh the next work tile from the stored corridor.
        /// Does not re-A* mid-project — that would ignore waypoints and often re-target the same corner.
        /// </summary>
        private static bool TryPopulateOutpostRoadPathCache(int sourceTileId, int destTileId, CompViralSpread comp, SettlementTier tier, out int workTile)
        {
            workTile = -1;
            if (comp == null) return false;

            // Authoritative corridor already planned (targeting / prior refresh).
            if (comp.cachedRoadPathTiles != null && comp.cachedRoadPathTiles.Count >= 2)
            {
                if (RoadBuildingTileListTouchesWater(comp.cachedRoadPathTiles))
                    return false;
                workTile = comp.roadIsClearing
                    ? GetFirstRoadRemovalWorkTileOnTileList(comp.cachedRoadPathTiles, sourceTileId)
                    : GetFirstRoadWorkTileOnTileList(comp.cachedRoadPathTiles, tier, sourceTileId);
                comp.lastPathSourceTile = sourceTileId;
                return true;
            }

            // Missing cache (legacy save / cleared): rebuild from waypoints or single leg.
            bool detachedStart = comp.roadTargetUsesDetachedStart
                && comp.roadWaypointTiles != null
                && comp.roadWaypointTiles.Count > 0;
            var chain = new List<int>(2 + (comp.roadWaypointTiles?.Count ?? 0));
            if (!detachedStart)
                chain.Add(sourceTileId);
            if (comp.roadWaypointTiles != null)
            {
                for (int i = 0; i < comp.roadWaypointTiles.Count; i++)
                    chain.Add(comp.roadWaypointTiles[i]);
            }
            chain.Add(destTileId);

            if (!TryBuildPathAlongWaypoints(chain, out List<int> pathTiles))
                return false;
            if (RoadBuildingTileListTouchesWater(pathTiles))
                return false;

            if (comp.cachedRoadPathTiles == null)
                comp.cachedRoadPathTiles = new List<int>(pathTiles.Count);
            else
                comp.cachedRoadPathTiles.Clear();
            comp.cachedRoadPathTiles.AddRange(pathTiles);
            comp.lastPathSourceTile = sourceTileId;
            workTile = comp.roadIsClearing
                ? GetFirstRoadRemovalWorkTileOnTileList(pathTiles, sourceTileId)
                : GetFirstRoadWorkTileOnTileList(pathTiles, tier, sourceTileId);
            return true;
        }

        /// <summary>
        /// Builds a dest-first tile list (same convention as <see cref="WorldPath.NodesReversed"/>)
        /// along consecutive chain tiles: chain[0]→chain[1]→…→chain[n].
        /// </summary>
        public static bool TryBuildPathAlongWaypoints(IList<int> chain, out List<int> pathTilesDestFirst)
        {
            pathTilesDestFirst = null;
            if (chain == null || chain.Count < 2) return false;
            PlanetLayer layer = PlanetSurfaceWorldActions.WdSurfaceLayer;
            if (layer == null) return false;

            var forward = new List<int>(32);
            for (int leg = 0; leg < chain.Count - 1; leg++)
            {
                int from = chain[leg];
                int to = chain[leg + 1];
                if (from == to) continue;
                using (WorldPath path = layer.Pather.FindPath(new PlanetTile(from, layer), new PlanetTile(to, layer), null))
                {
                    if (path == null || !path.Found) return false;
                    if (RoadBuildingPathTouchesWater(path)) return false;
                    var nodes = path.NodesReversed;
                    // NodesReversed is dest-first; walk start→dest into forward list.
                    for (int i = nodes.Count - 1; i >= 0; i--)
                    {
                        int id = nodes[i].tileId;
                        if (forward.Count > 0 && forward[forward.Count - 1] == id)
                            continue;
                        forward.Add(id);
                    }
                }
            }

            if (forward.Count < 2) return false;
            // Store dest-first to match existing cache / GetFirstRoadWorkTile convention.
            pathTilesDestFirst = new List<int>(forward.Count);
            for (int i = forward.Count - 1; i >= 0; i--)
                pathTilesDestFirst.Add(forward[i]);
            return true;
        }

        public static bool RoadBuildingTileListTouchesWater(List<int> tiles)
        {
            if (tiles == null || tiles.Count == 0) return false;
            WorldGrid grid = Find.WorldGrid;
            if (grid == null) return false;
            for (int i = 0; i < tiles.Count; i++)
            {
                int id = tiles[i];
                if (id >= 0 && id < grid.TilesCount && grid[id].WaterCovered)
                    return true;
            }
            return false;
        }

        private static List<int> PathNodesToTileIds(List<PlanetTile> nodes)
        {
            var list = new List<int>(nodes.Count);
            for (int i = 0; i < nodes.Count; i++)
                list.Add(nodes[i].tileId);
            return list;
        }

        /// <summary>Used after load / cancel to detect stale <see cref="CompViralSpread"/> builder-in-field state.</summary>
        public static bool HasActiveRoadBuilderFrom(WorldObject origin)
        {
            if (origin == null || Find.WorldObjects == null) return false;
            foreach (var wo in Find.WorldObjects.AllWorldObjects)
            {
                if (wo is WorldObject_Traveler t && !t.Destroyed
                    && t.mission == TravelerMission.RoadBuilding
                    && t.originObject == origin)
                    return true;
            }
            return false;
        }

        public static bool LaunchRoadBuilder(Settlement actor, Settlement target, WorldComponent_SpreadManager manager)
        {
            var comp = actor.GetComponent<CompViralSpread>();
            SettlementTier tier = comp != null ? comp.tier : SettlementTier.T1;
            float costNeeded = GetExpeditionStrengthCost(tier);
            if (comp == null || costNeeded <= 0.01f)
            {
                manager?.AddLog(new SpreadLogEntry("TSA_WD_Log_Road_SkippedLowStrength".Translate(actor.LabelCap, 0f.ToString("F0")), actor, target));
                return false;
            }

            PlanetLayer layer = PlanetSurfaceWorldActions.WdSurfaceLayer;
            if (layer == null)
            {
                manager?.AddLog(new SpreadLogEntry("TSA_WD_Log_Road_SkippedLaunchFailed".Translate(actor.LabelCap, target.LabelCap), actor, target));
                return false;
            }

            List<int> pathTilesDestFirst;
            List<int> workTiles = new List<int>();
            using (WorldPath path = layer.Pather.FindPath(
                new PlanetTile(actor.Tile, layer),
                new PlanetTile(target.Tile, layer),
                null))
            {
                if (path == null || !path.Found || RoadBuildingPathTouchesWater(path))
                {
                    manager?.AddLog(new SpreadLogEntry("TSA_WD_Log_Road_SkippedNoWork".Translate(actor.LabelCap, target.LabelCap), actor, target));
                    return false;
                }

                pathTilesDestFirst = PathNodesToTileIds(path.NodesReversed);
            }

            CollectUnfinishedRoadWorkTiles(pathTilesDestFirst, tier, workTiles);
            if (workTiles.Count == 0)
            {
                manager?.AddLog(new SpreadLogEntry("TSA_WD_Log_Road_SkippedNoWork".Translate(actor.LabelCap, target.LabelCap), actor, target));
                return false;
            }

            int desired = RollRoadCaravanCount(tier);
            int maxAffordable = WorldActions_Utils.MaxAffordableExpeditionsLeavingGarrison(comp, costNeeded);
            int toLaunch = Mathf.Min(desired, maxAffordable, workTiles.Count);
            if (toLaunch < 1)
            {
                manager?.AddLog(new SpreadLogEntry(
                    "TSA_WD_Log_Road_SkippedLowStrength".Translate(actor.LabelCap, comp.strength.ToString("F0")),
                    actor,
                    target));
                return false;
            }

            int launched = 0;
            WorldGrid grid = Find.WorldGrid;
            int nextDue = Find.TickManager.TicksGame;
            for (int i = 0; i < toLaunch; i++)
            {
                if (!WorldActions_Utils.CanAffordExpeditionLeavingGarrison(comp, costNeeded))
                    break;
                // Keep multi-launches on one contiguous unfinished stretch (stop at path gaps).
                if (i > 0 && (grid == null || !grid.IsNeighbor(workTiles[i - 1], workTiles[i])))
                    break;
                if (i == 0)
                {
                    if (!SpawnRoadTraveler(actor, workTiles[i], tier, pathTilesDestFirst))
                        break;
                }
                else
                {
                    comp.strength = Mathf.Max(0f, comp.strength - costNeeded);
                    comp.CheckTierUpdate(false);
                    nextDue += WorldActions_NpcLaunchStagger.NextGapTicks();
                    WorldActions_NpcLaunchStagger.EnqueueRoad(
                        nextDue, actor, workTiles[i], tier, pathTilesDestFirst, costNeeded);
                }
                launched++;
            }

            if (launched < 1)
            {
                manager?.AddLog(new SpreadLogEntry("TSA_WD_Log_Road_SkippedLaunchFailed".Translate(actor.LabelCap, target.LabelCap), actor, target));
                return false;
            }

            comp.roadCooldownTick = Find.TickManager.TicksGame + Mathf.RoundToInt(WorldDominationMod.settings.cooldownGrowDays * 60000f);

            manager.AddLog(new SpreadLogEntry("TSA_WD_Log_Road_ExpeditionLaunched".Translate(actor.LabelCap, target.LabelCap), actor, target));
            return true;
        }

        /// <summary>
        /// T1–T4 multi-caravan chances come from fortify multi-launch settings (same roll as NPC Fortify).
        /// Same roll as NPC Fortify multi-caravan launches.
        /// </summary>
        public static int RollRoadCaravanCount(SettlementTier tier)
            => WorldActions_NpcFortify.RollFortifyCaravanCount(tier);

        /// <summary>
        /// Dest tiles of unfinished corridor edges in source→dest travel order
        /// (same convention as <see cref="GetFirstRoadWorkTileOnTileList"/>).
        /// </summary>
        private static void CollectUnfinishedRoadWorkTiles(List<int> nodesDestFirst, SettlementTier tier, List<int> into)
        {
            into.Clear();
            if (nodesDestFirst == null || nodesDestFirst.Count < 2) return;
            PlanetLayer layer = PlanetSurfaceWorldActions.WdSurfaceLayer;
            if (layer == null) return;
            RoadDef targetRoad = GetRoadDefByTier(tier);
            if (targetRoad == null) return;

            for (int i = nodesDestFirst.Count - 1; i > 0; i--)
            {
                int from = nodesDestFirst[i];
                int to = nodesDestFirst[i - 1];
                if (ShouldUpgradeRoad(new PlanetTile(from, layer), new PlanetTile(to, layer), targetRoad))
                    // NPC multi-launch uses unique adjacent edge ends (not GetRoadEdgeWorkTile, which can repeat).
                    into.Add(to);
            }
        }

        /// <summary>Returns false if the traveler was destroyed immediately (e.g. no path), or garrison retain would be breached.</summary>
        private static bool SpawnRoadTraveler(WorldObject origin, int destTile, SettlementTier tier, List<int> pathTilesDestFirst = null)
        {
            var comp = origin.GetComponent<CompViralSpread>();
            float cost = GetExpeditionStrengthCost(tier);
            if (!WorldActions_Utils.TryConsumeExpeditionStrength(comp, cost)) return false;
            if (SpawnRoadTravelerPrepaid(origin, destTile, tier, pathTilesDestFirst, cost))
                return true;
            WorldActions_Utils.RefundExpeditionStrength(comp, cost);
            return false;
        }

        /// <summary>Spawn after strength was already reserved (staggered multi-launch).</summary>
        internal static bool SpawnRoadTravelerPrepaid(
            WorldObject origin,
            int destTile,
            SettlementTier tier,
            List<int> pathTilesDestFirst,
            float cost)
        {
            var comp = origin.GetComponent<CompViralSpread>();
            if (comp == null) return false;

            WorldObject_Traveler traveler = (WorldObject_Traveler)WorldObjectMaker.MakeWorldObject(DefDatabase<WorldObjectDef>.GetNamed("TSA_WD_Traveler_Outpost_RoadBuilder"));
            traveler.Tile = origin.Tile;
            traveler.SetFaction(origin.Faction);
            traveler.mission = TravelerMission.RoadBuilding;
            traveler.originObject = origin;

            traveler.travelerStrength = cost;
            traveler.initialStrength = cost;

            Find.WorldObjects.Add(traveler);
            List<int> pathSource = pathTilesDestFirst;
            if (pathSource == null || pathSource.Count == 0)
                pathSource = comp.cachedRoadPathTiles;
            if (pathSource != null && pathSource.Count > 0)
            {
                traveler.cachedPathTiles.Clear();
                traveler.cachedPathTiles.AddRange(pathSource);
            }

            traveler.pather.StartPath(PlanetSurfaceWorldActions.PlanetTileForWdTravel(destTile, origin));
            return !traveler.Destroyed;
        }

        /// <summary>Road builders never cross ocean/water tiles; paths must match land-only <see cref="WorldPath"/>.</summary>
        public static bool RoadBuildingPathTouchesWater(WorldPath path)
        {
            if (path == null || !path.Found) return false;
            WorldGrid grid = Find.WorldGrid;
            if (grid == null) return false;
            var nodes = path.NodesReversed;
            for (int i = 0; i < nodes.Count; i++)
            {
                int id = nodes[i].tileId;
                if (id >= 0 && id < grid.TilesCount && grid[id].WaterCovered)
                    return true;
            }
            return false;
        }

        /// <summary>First segment that still needs work along a path (caller ensures path is found and land-only if required).</summary>
        /// <summary>
        /// Tile the crew travels to for one corridor edge (from → to) in source→dest order.
        /// Prefer the edge start (go to that tile and pave/remove the outgoing link). When the start
        /// is the builder itself, travel to the neighbor instead so the crew leaves home.
        /// </summary>
        public static int GetRoadEdgeWorkTile(int fromTile, int toTile, int builderTile)
        {
            if (builderTile >= 0 && fromTile == builderTile)
                return toTile;
            return fromTile;
        }

        public static int GetFirstRoadWorkTileOnPath(WorldPath path, SettlementTier requestedTier, int builderTile = -1)
        {
            if (path == null || !path.Found) return -1;
            return GetFirstRoadWorkTileOnTileList(PathNodesToTileIds(path.NodesReversed), requestedTier, builderTile);
        }

        /// <summary>First work tile on a dest-first tile list (same order as <see cref="WorldPath.NodesReversed"/>).</summary>
        public static int GetFirstRoadWorkTileOnTileList(List<int> nodesDestFirst, SettlementTier requestedTier, int builderTile = -1)
        {
            if (!TryGetFirstUnfinishedRoadEdge(nodesDestFirst, requestedTier, out int fromTile, out int toTile))
                return -1;
            return GetRoadEdgeWorkTile(fromTile, toTile, builderTile);
        }

        /// <summary>
        /// First unfinished corridor edge in travel order (source → dest).
        /// <paramref name="nodesDestFirst"/> uses <see cref="WorldPath.NodesReversed"/> order.
        /// </summary>
        public static bool TryGetFirstUnfinishedRoadEdge(List<int> nodesDestFirst, SettlementTier requestedTier, out int fromTile, out int toTile)
        {
            fromTile = -1;
            toTile = -1;
            if (nodesDestFirst == null || nodesDestFirst.Count < 2) return false;
            PlanetLayer layer = PlanetSurfaceWorldActions.WdSurfaceLayer;
            if (layer == null) return false;
            RoadDef targetRoad = GetRoadDefByTier(requestedTier);
            if (targetRoad == null) return false;

            // Dest-first: index Count-1 is source, 0 is dest. Walk source → dest.
            for (int i = nodesDestFirst.Count - 1; i > 0; i--)
            {
                int from = nodesDestFirst[i];
                int to = nodesDestFirst[i - 1];
                if (ShouldUpgradeRoad(new PlanetTile(from, layer), new PlanetTile(to, layer), targetRoad))
                {
                    fromTile = from;
                    toTile = to;
                    return true;
                }
            }
            return false;
        }

        /// <summary>True if any road link exists between neighboring tiles (including biome-hidden potential roads).</summary>
        public static bool HasRoadLink(int tileA, int tileB)
        {
            WorldGrid grid = Find.WorldGrid;
            if (grid == null || !grid.InBounds(tileA) || !grid.InBounds(tileB)) return false;
            if (!grid.IsNeighbor(tileA, tileB)) return false;
            return grid.GetRoadDef(tileA, tileB, visibleOnly: false) != null;
        }

        public static bool HasRoadLink(PlanetTile a, PlanetTile b) => HasRoadLink(a.tileId, b.tileId);

        /// <summary>Count edges that still have a road along a dest-first corridor (removal work units).</summary>
        public static int CountRoadRemovalSegmentsOnTileList(List<int> nodesDestFirst)
        {
            if (nodesDestFirst == null || nodesDestFirst.Count < 2) return 0;
            int count = 0;
            for (int i = nodesDestFirst.Count - 1; i > 0; i--)
            {
                if (HasRoadLink(nodesDestFirst[i], nodesDestFirst[i - 1]))
                    count++;
            }
            return count;
        }

        /// <summary>First work tile for road removal on a dest-first corridor.</summary>
        public static int GetFirstRoadRemovalWorkTileOnTileList(List<int> nodesDestFirst, int builderTile = -1)
        {
            if (!TryGetFirstRemovableRoadEdge(nodesDestFirst, out int fromTile, out int toTile))
                return -1;
            return GetRoadEdgeWorkTile(fromTile, toTile, builderTile);
        }

        /// <summary>
        /// First corridor edge that still has any road, in travel order (source → dest).
        /// <paramref name="nodesDestFirst"/> uses <see cref="WorldPath.NodesReversed"/> order.
        /// </summary>
        public static bool TryGetFirstRemovableRoadEdge(List<int> nodesDestFirst, out int fromTile, out int toTile)
        {
            fromTile = -1;
            toTile = -1;
            if (nodesDestFirst == null || nodesDestFirst.Count < 2) return false;

            for (int i = nodesDestFirst.Count - 1; i > 0; i--)
            {
                int from = nodesDestFirst[i];
                int to = nodesDestFirst[i - 1];
                if (HasRoadLink(from, to))
                {
                    fromTile = from;
                    toTile = to;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Forced travel nodes along the planned corridor from <paramref name="fromTileId"/> to <paramref name="toTileId"/> (inclusive).
        /// Prevents A* shortcuts that pave off-corridor edges and strand the worksite on a waypoint.
        /// </summary>
        public static List<PlanetTile> TryBuildCorridorTravelNodes(List<int> nodesDestFirst, int fromTileId, int toTileId, PlanetLayer layer)
        {
            if (nodesDestFirst == null || nodesDestFirst.Count < 2 || layer == null) return null;

            int fromIdx = -1;
            int toIdx = -1;
            // Prefer the later occurrence of from (near source end) and the matching to toward dest.
            for (int i = 0; i < nodesDestFirst.Count; i++)
            {
                if (nodesDestFirst[i] == fromTileId) fromIdx = i;
                if (nodesDestFirst[i] == toTileId) toIdx = i;
            }
            // If to appears multiple times, pick the one on the travel side of from (toward dest = lower index).
            if (fromIdx >= 0)
            {
                for (int i = fromIdx; i >= 0; i--)
                {
                    if (nodesDestFirst[i] == toTileId)
                    {
                        toIdx = i;
                        break;
                    }
                }
            }
            if (fromIdx < 0 || toIdx < 0) return null;

            // In dest-first storage, traveling source→dest means fromIdx >= toIdx (from closer to source end).
            if (fromIdx < toIdx)
            {
                // Traveler somehow ahead of work tile on the list — allow reverse segment.
                int tmp = fromIdx;
                fromIdx = toIdx;
                toIdx = tmp;
            }

            var nodes = new List<PlanetTile>(fromIdx - toIdx + 1);
            for (int i = fromIdx; i >= toIdx; i--)
                nodes.Add(new PlanetTile(nodesDestFirst[i], layer));

            return nodes.Count >= 2 ? nodes : null;
        }

        public static int GetCurrentWorkTile(int startTileId, int destTileId, SettlementTier requestedTier = SettlementTier.T1)
        {
            // Roads are surface-only; resolve the surface layer explicitly rather than via WorldGrid[int].
            PlanetLayer layer = PlanetSurfaceWorldActions.WdSurfaceLayer;
            if (layer == null) return -1;

            using (WorldPath path = layer.Pather.FindPath(new PlanetTile(startTileId, layer), new PlanetTile(destTileId, layer), null))
            {
                if (path == null || !path.Found) return -1;
                if (RoadBuildingPathTouchesWater(path)) return -1;
                return GetFirstRoadWorkTileOnPath(path, requestedTier, startTileId);
            }
        }

        /// <summary>
        /// Vanilla <see cref="Tile.RoadLink"/> uses <c>int neighbor</c>, not <see cref="PlanetTile"/>. Old reflection wrote wrong types and corrupted tiles (broken roads, inspect MaxBy on empty).
        /// Uses <see cref="WorldGrid.GetRoadDef"/> with <c>visibleOnly: false</c> so we read <c>potentialRoads</c> even when biome hides roads from UI lists.
        /// </summary>
        public static bool ShouldUpgradeRoad(PlanetTile a, PlanetTile b, RoadDef plannedRoad)
        {
            if (plannedRoad == null) return false;
            if (Find.WorldGrid == null || !Find.WorldGrid.InBounds(a.tileId) || !Find.WorldGrid.InBounds(b.tileId)) return false;
            if (!Find.WorldGrid.IsNeighbor(a.tileId, b.tileId)) return false;

            RoadDef existing = Find.WorldGrid.GetRoadDef(a.tileId, b.tileId, visibleOnly: false);
            if (existing == null) return true;
            return plannedRoad.priority > existing.priority;
        }

        /// <summary>
        /// Per-hop validation for road-building travelers: allow crossing edges that already meet the planned tier.
        /// <see cref="ShouldUpgradeRoad"/> is false on those edges and would cancel the caravan when the route revisits completed segments.
        /// </summary>
        public static bool RoadBuilderMayCrossEdge(int fromTileId, int toTileId, PlanetLayer layer, RoadDef plannedRoad)
        {
            if (plannedRoad == null) return true;
            WorldGrid grid = Find.WorldGrid;
            if (grid == null || !grid.InBounds(fromTileId) || !grid.InBounds(toTileId)) return false;
            if (!grid.IsNeighbor(fromTileId, toTileId)) return false;

            RoadDef existing = grid.GetRoadDef(fromTileId, toTileId, visibleOnly: false);
            if (existing != null && existing.priority >= plannedRoad.priority)
                return true;

            return ShouldUpgradeRoad(new PlanetTile(fromTileId, layer), new PlanetTile(toTileId, layer), plannedRoad);
        }

        /// <summary>Delegates to vanilla <see cref="WorldGrid.OverlayRoad"/> (updates both tiles and replaces reflection).
        /// Paving/upgrading a road clears spike traps and road blocks on both endpoint tiles.
        /// Road blocks and traps never remove roads.</summary>
        public static void ApplyRoadLink(PlanetTile a, PlanetTile b, WorldObject actor)
        {
            ApplyRoadLink(a, b, GetRoadDefForActor(actor));
        }

        /// <summary>Paint a specific <see cref="RoadDef"/> between neighboring tiles (World Setup / tools).</summary>
        public static void ApplyRoadLink(PlanetTile a, PlanetTile b, RoadDef road)
        {
            if (road == null) return;
            if (Find.WorldGrid == null || !Find.WorldGrid.InBounds(a.tileId) || !Find.WorldGrid.InBounds(b.tileId)) return;
            if (!Find.WorldGrid.IsNeighbor(a.tileId, b.tileId))
            {
                Log.Warning($"TSA World Domination: ApplyRoadLink skipped non-adjacent tiles {a.tileId} ↔ {b.tileId}.");
                return;
            }

            Find.WorldGrid.OverlayRoad(a.tileId, b.tileId, road);
            // Road wins over fortifications on the paved tiles.
            WorldActions_SpikeTraps.ClearIfPresent(a.tileId);
            WorldActions_SpikeTraps.ClearIfPresent(b.tileId);
            WorldActions_RoadBlocks.ClearIfPresent(a.tileId);
            WorldActions_RoadBlocks.ClearIfPresent(b.tileId);
            ScheduleRoadLayerDirtyFlush();
            WD_WorldLayer_MovementDifficultyOverlay.InvalidateAndDirtyIfActive();
        }

        /// <summary>
        /// Remove any road link between neighboring tiles. Does not touch fortifications.
        /// Strips both tiles' <c>potentialRoads</c> (and <c>Roads</c> when that list is distinct).
        /// </summary>
        public static void RemoveRoadLink(PlanetTile a, PlanetTile b)
        {
            WorldGrid grid = Find.WorldGrid;
            if (grid == null || !grid.InBounds(a.tileId) || !grid.InBounds(b.tileId)) return;
            if (!grid.IsNeighbor(a.tileId, b.tileId))
            {
                Log.Warning($"TSA World Domination: RemoveRoadLink skipped non-adjacent tiles {a.tileId} ↔ {b.tileId}.");
                return;
            }

            RemoveRoadLinkOneWay(a.tileId, b);
            RemoveRoadLinkOneWay(b.tileId, a);
            ScheduleRoadLayerDirtyFlush();
            WD_WorldLayer_MovementDifficultyOverlay.InvalidateAndDirtyIfActive();
        }

        private static void RemoveRoadLinkOneWay(int fromTileId, PlanetTile toNeighbor)
        {
            if (!(Find.WorldGrid[fromTileId] is SurfaceTile surface)) return;

            RemoveRoadLinkFromList(surface.potentialRoads, toNeighbor);
            // Roads may be a filtered view or a distinct list depending on game version / biome visibility.
            List<SurfaceTile.RoadLink> roads = surface.Roads;
            if (roads != null && !ReferenceEquals(roads, surface.potentialRoads))
                RemoveRoadLinkFromList(roads, toNeighbor);

            if (surface.potentialRoads != null && surface.potentialRoads.Count == 0)
                surface.potentialRoads = null;
        }

        private static void RemoveRoadLinkFromList(List<SurfaceTile.RoadLink> links, PlanetTile toNeighbor)
        {
            if (links == null || links.Count == 0) return;
            for (int i = links.Count - 1; i >= 0; i--)
            {
                if (links[i].neighbor == toNeighbor || links[i].neighbor.tileId == toNeighbor.tileId)
                    links.RemoveAt(i);
            }
        }

        private static void ScheduleRoadLayerDirtyFlush()
        {
            if (roadLayerDirtyFlushQueued) return;
            roadLayerDirtyFlushQueued = true;
            LongEventHandler.ExecuteWhenFinished(FlushRoadLayerDirty);
        }

        private static void FlushRoadLayerDirty()
        {
            roadLayerDirtyFlushQueued = false;
            World w = Find.World;
            if (w?.renderer == null) return;

            if (cachedWorldForRoadLayerDirty != w)
            {
                cachedWorldForRoadLayerDirty = w;
                cachedRoadDrawLayers = null;
            }

            if (cachedRoadDrawLayers == null)
            {
                var list = new List<object>();
                foreach (object layer in w.renderer.AllDrawLayers)
                {
                    if (layer == null) continue;
                    if (layer.GetType().Name.IndexOf("Road", StringComparison.OrdinalIgnoreCase) >= 0)
                        list.Add(layer);
                }
                cachedRoadDrawLayers = list;
            }

            for (int i = 0; i < cachedRoadDrawLayers.Count; i++)
                InvokeDrawLayerSetDirty(cachedRoadDrawLayers[i]);
        }

        private static void InvokeDrawLayerSetDirty(object layer)
        {
            if (layer == null) return;
            Type t = layer.GetType();
            if (!setDirtyMethodByLayerType.TryGetValue(t, out MethodInfo mi))
            {
                mi = t.GetMethod("SetDirty", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                setDirtyMethodByLayerType[t] = mi;
            }
            mi?.Invoke(layer, null);
        }

        public static RoadDef GetRoadDefByTier(SettlementTier tier, TechLevel tech = TechLevel.Industrial)
        {
            if (RoadsOfTheRimActive)
            {
                if (tier == SettlementTier.T3 || tier == SettlementTier.T4)
                {
                    RoadDef asphalt = DefDatabase<RoadDef>.GetNamed("AsphaltRoad", false);
                    if (asphalt != null) return asphalt;
                    RoadDef stone = DefDatabase<RoadDef>.GetNamed("StoneRoadBuilt", false);
                    if (stone != null) return stone;
                }

                if (tier == SettlementTier.T2)
                {
                    RoadDef stone = DefDatabase<RoadDef>.GetNamed("StoneRoadBuilt", false);
                    if (stone != null) return stone;
                }

                RoadDef dirtBuilt = DefDatabase<RoadDef>.GetNamed("DirtRoadBuilt", false);
                if (dirtBuilt != null) return dirtBuilt;
            }
            else
            {
                if (tier == SettlementTier.T3 || tier == SettlementTier.T4)
                {
                    RoadDef asphalt = DefDatabase<RoadDef>.GetNamed("AncientAsphaltRoad", false);
                    if (asphalt != null) return asphalt;
                    RoadDef stone = DefDatabase<RoadDef>.GetNamed("StoneRoad", false);
                    if (stone != null) return stone;
                }

                if (tier == SettlementTier.T2)
                {
                    RoadDef stone = DefDatabase<RoadDef>.GetNamed("StoneRoad", false);
                    if (stone != null) return stone;
                }

                RoadDef dirt = DefDatabase<RoadDef>.GetNamed("DirtRoad", false);
                if (dirt != null) return dirt;
            }

            if (!_fallbackRoadResolved)
            {
                _fallbackRoadResolved = true;
                RoadDef best = null;
                foreach (var d in DefDatabase<RoadDef>.AllDefs)
                {
                    if (d.defName.IndexOf("road", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (best == null || d.priority < best.priority)
                        best = d;
                }
                _fallbackRoadDef = best;
            }
            return _fallbackRoadDef;
        }

        /// <summary>
        /// Road def for an actor's road work. Outposts and any active road project
        /// (<see cref="CompViralSpread.roadTargetTile"/>) use <see cref="CompViralSpread.selectedRoadTier"/>.
        /// Player colonies never advance <see cref="CompViralSpread.tier"/>, so using tier alone
        /// would always pave dirt and skip upgrades when asphalt/stone is selected.
        /// NPC auto-builders without a project still use settlement tier.
        /// </summary>
        public static RoadDef GetRoadDefForActor(WorldObject actor)
        {
            var comp = actor.GetComponent<CompViralSpread>();
            TechLevel factionTech = actor.Faction?.def.techLevel ?? TechLevel.Undefined;

            if (actor is WorldObject_WD_Outpost || (comp != null && comp.roadTargetTile != -1))
                return GetRoadDefByTier(comp?.selectedRoadTier ?? SettlementTier.T1, factionTech);

            return GetRoadDefByTier(comp?.tier ?? SettlementTier.T1, factionTech);
        }
    }
}
