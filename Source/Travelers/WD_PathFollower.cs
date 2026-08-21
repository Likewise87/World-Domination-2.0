using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    public class WD_PathFollower : IExposable
    {
        private WorldObject_Traveler traveler;
        public bool moving;
        public PlanetTile nextTile = PlanetTile.Invalid;
        public PlanetTile destTile = PlanetTile.Invalid;
        public float nextTileCostLeft;
        public float nextTileCostTotal = 1f;
        public WorldPath curPath;
        private List<PlanetTile> fallbackPathNodes;
        private int fallbackPathIndex;
        public int previousTileId = -1;
        private readonly List<Vector3> drawPathScratch = new List<Vector3>(32);
        private static int lastPathDrawDiagTick = -999999;
        /// <summary>One-shot repath guard when route nodes are exhausted before reaching <see cref="destTile"/>.</summary>
        private bool pathExhaustionRepathAttempted;
        private bool deferredPostLoadResume;

        public WD_PathFollower() { }
        public WD_PathFollower(WorldObject_Traveler traveler) => this.traveler = traveler;

        public void ExposeData()
        {
            Scribe_Values.Look(ref moving, "moving", false);
            Scribe_Values.Look(ref nextTile, "nextTile", PlanetTile.Invalid);
            Scribe_Values.Look(ref destTile, "destTile", PlanetTile.Invalid);
            Scribe_Values.Look(ref nextTileCostLeft, "nextTileCostLeft", 0f);
            Scribe_Values.Look(ref nextTileCostTotal, "nextTileCostTotal", 1f);
            Scribe_Values.Look(ref previousTileId, "previousTileId", -1);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && moving)
                deferredPostLoadResume = true;
        }

        public void StartPath(PlanetTile dest, bool skipLaunchTravelCache = false, float pollutionWeightMultiplier = 1f)
        {
            destTile = dest;
            if (dest == traveler.Tile)
            {
                // Fresh launch with dest == current would otherwise StopDead() and leak an already-added
                // world object (e.g. a mortar shell aimed at a target sitting on the firing tile). For mortars,
                // resolve arrival so the strike applies and the shell is destroyed instead of getting stuck.
                // Decontamination uses the same rule for NPC home-tile scrubs.
                if (skipLaunchTravelCache
                    || UsesBallisticWorldFlight(traveler)
                    || traveler.mission == TravelerMission.Decontamination)
                    HandleArrivedOnDestTile();
                else
                    StopDead();
                return;
            }
            if (curPath != null) curPath.ReleaseToPool();
            curPath = null;
            fallbackPathNodes = null;
            fallbackPathIndex = 0;

            // Mortar shells: fly straight from origin to destTile as a single ballistic hop.
            // The pather's Slerp in UpdateTweenedPos interpolates along the great-circle chord,
            // completely ignoring the tile grid, water, mountains, roads, etc.
            if (UsesBallisticWorldFlight(traveler))
            {
                var gridM = Find.WorldGrid;
                if (gridM == null || traveler.Tile.tileId < 0 || destTile.tileId < 0)
                {
                    StopDead();
                    traveler.Destroy();
                    return;
                }
                fallbackPathNodes = new List<PlanetTile> { traveler.Tile, destTile };
                fallbackPathIndex = 0;
                int tpmMortar = TravelUtils.ResolveTicksPerMove(traveler.ticksPerMove);
                float approxDistM = Mathf.Max(1f, gridM.ApproxDistanceInTiles(traveler.Tile.tileId, destTile.tileId));
                moving = true;
                pathExhaustionRepathAttempted = false;
                if (!skipLaunchTravelCache || traveler.CachedLaunchTotalTravelTicks < 0f)
                    traveler.SetLaunchTotalTravelTicks(approxDistM * tpmMortar);
                LockInProjectedArrivalStrength(approxDistM * tpmMortar, skipLaunchTravelCache);
                SetupNextTile();
                return;
            }

            var seth = WorldDominationMod.settings;
            bool allowWaterTravel = seth?.allowCaravansTravelOverWater ?? WorldDominationSettings.DefAllowCaravansTravelOverWater;
            bool strictWaterOnly = seth?.onlyTravelAcrossWaterIfNoOtherWay ?? WorldDominationSettings.DefOnlyTravelAcrossWaterIfNoOtherWay;
            bool usePollutionCost = TravelerPollutionDamage.UsesPollutionPathCost(traveler);
            WdPollutionPathContext.Scope pollutionScope = usePollutionCost
                ? WdPollutionPathContext.Activate(pollutionWeightMultiplier)
                : null;

            bool hasVanillaPath = false;
            int tpm = TravelUtils.ResolveTicksPerMove(traveler.ticksPerMove);
            try
            {
                curPath = traveler.Tile.Layer.Pather.FindPath(traveler.Tile, destTile, null);
                hasVanillaPath = curPath != null && curPath.Found;

                // Road builders: follow the planned corridor when available (no A* shortcuts that leave gaps at waypoints).
                if (traveler.mission == TravelerMission.RoadBuilding)
                {
                    List<PlanetTile> corridor = null;
                    if (traveler.cachedPathTiles != null && traveler.cachedPathTiles.Count >= 2)
                        corridor = WorldActions_Roads.TryBuildCorridorTravelNodes(
                            traveler.cachedPathTiles, traveler.Tile.tileId, destTile.tileId, traveler.Tile.Layer);

                    if (corridor != null && corridor.Count >= 2)
                    {
                        curPath?.ReleaseToPool();
                        curPath = null;
                        fallbackPathNodes = corridor;
                        fallbackPathIndex = 0;
                    }
                    else if (!hasVanillaPath || WorldActions_Roads.RoadBuildingPathTouchesWater(curPath))
                    {
                        StopDead();
                        traveler.Destroy();
                        return;
                    }
                }
                else
                {
                    List<PlanetTile> waterPath = null;
                    if (allowWaterTravel)
                    {
                        float thresholdDays = seth?.waterPathLandThresholdDays ?? WorldDominationSettings.DefWaterPathLandThresholdDays;
                        bool skipWaterPath = false;
                        if (!strictWaterOnly && hasVanillaPath)
                        {
                            if (thresholdDays > 0f)
                            {
                                float vanillaTicks = TravelUtils.SumFullPathTicks(curPath, tpm);
                                if (vanillaTicks <= thresholdDays * 60000f)
                                    skipWaterPath = true;
                            }

                            if (!skipWaterPath)
                            {
                                int hops = curPath.NodesReversed.Count - 1;
                                float approx = Find.WorldGrid.ApproxDistanceInTiles(traveler.Tile.tileId, destTile.tileId);
                                if (hops > 0 && approx > 0f && hops / approx < 1.4f)
                                    skipWaterPath = true;
                            }
                        }

                        bool attemptWaterPath = !skipWaterPath && (!strictWaterOnly || !hasVanillaPath);
                        if (attemptWaterPath)
                        {
                            WD_DevPerformanceSpikeLog.Msg(
                                $"Traveler.WaterPath TRY traveler=\"{traveler.Label}\" fromTile={traveler.Tile} toTile={destTile.tileId} strictWaterOnly={strictWaterOnly} vanillaFound={hasVanillaPath}");
                            if (TravelerWaterPathing.TryBuildFallbackPath(traveler.Tile, destTile, out var fullPath) &&
                                fullPath != null && fullPath.Count > 1)
                                waterPath = fullPath;
                            WD_DevPerformanceSpikeLog.Msg(
                                $"Traveler.WaterPath RESULT traveler=\"{traveler.Label}\" ok={(waterPath != null)} nodes={(waterPath?.Count ?? 0)}");
                        }
                    }

                    if (strictWaterOnly)
                    {
                        if (!hasVanillaPath && waterPath != null)
                        {
                            curPath?.ReleaseToPool();
                            curPath = null;
                            fallbackPathNodes = waterPath;
                        }
                    }
                    else if (hasVanillaPath && waterPath != null)
                    {
                        float vanillaTicks = TravelUtils.SumFullPathTicks(curPath, tpm);
                        float waterTicks = TravelUtils.SumFullPathTicks(waterPath, tpm);
                        if (waterTicks < vanillaTicks)
                        {
                            curPath.ReleaseToPool();
                            curPath = null;
                            fallbackPathNodes = waterPath;
                        }
                    }
                    else if (!hasVanillaPath && waterPath != null)
                        fallbackPathNodes = waterPath;
                }
            }
            finally
            {
                pollutionScope?.Dispose();
            }

            // Sum travel ticks with context off so hop timing stays pollution-blind (damage is the real cost).
            bool hasPath = (curPath != null && curPath.Found) || (fallbackPathNodes != null && fallbackPathNodes.Count > 1);
            WD_DevPerformanceSpikeLog.Msg(
                $"Traveler.WD_PathFollower.StartPath traveler=\"{traveler.Label}\" fromTile={traveler.Tile} toTile={destTile.tileId} vanillaFound={hasVanillaPath} fallbackNodeCount={fallbackPathNodes?.Count ?? 0} hasPath={hasPath}");
            if (hasPath)
            {
                moving = true;
                pathExhaustionRepathAttempted = false;
                bool usingFallback = fallbackPathNodes != null && fallbackPathNodes.Count > 1;
                float pathTicks = !usingFallback && curPath != null && curPath.Found
                    ? TravelUtils.SumFullPathTicks(curPath, tpm)
                    : TravelUtils.SumFullPathTicks(fallbackPathNodes, tpm);
                if (!skipLaunchTravelCache || traveler.CachedLaunchTotalTravelTicks < 0f)
                    traveler.SetLaunchTotalTravelTicks(pathTicks);
                LockInProjectedArrivalStrength(pathTicks, skipLaunchTravelCache);
                SetupNextTile();
            }
            else { StopDead(); traveler.Destroy(); }
        }

        /// <summary>Tile IDs the traveler will leave on this route (all nodes except destination).</summary>
        public List<int> CollectPathExitTileIds()
        {
            var list = new List<int>();
            if (curPath != null && curPath.Found)
            {
                var nodes = curPath.NodesReversed;
                // NodesReversed is dest..start; leave start through the tile before dest.
                for (int i = nodes.Count - 1; i >= 1; i--)
                    list.Add(nodes[i].tileId);
                return list;
            }
            if (fallbackPathNodes != null && fallbackPathNodes.Count > 1)
            {
                for (int i = 0; i < fallbackPathNodes.Count - 1; i++)
                    list.Add(fallbackPathNodes[i].tileId);
            }
            return list;
        }

        public bool RetargetDestinationAfterCurrentHop(PlanetTile dest)
        {
            if (traveler == null || !dest.Valid) return false;
            if (!moving || !nextTile.Valid)
            {
                StartPath(dest, skipLaunchTravelCache: true);
                return moving && destTile == dest;
            }

            if (dest == traveler.Tile)
                return false;

            if (dest == destTile)
                return true;

            PlanetTile pathStart = nextTile;
            WorldPath newPath = null;
            if (pathStart != dest)
            {
                newPath = pathStart.Layer.Pather.FindPath(pathStart, dest, null);
                if (newPath == null || !newPath.Found)
                {
                    newPath?.ReleaseToPool();
                    return false;
                }
            }

            if (curPath != null)
                curPath.ReleaseToPool();
            curPath = newPath;
            fallbackPathNodes = null;
            fallbackPathIndex = 0;
            destTile = dest;
            return true;
        }

        /// <summary>
        /// Locks in <see cref="WorldObject_Traveler.projectedArrivalStrength"/> exactly once at the
        /// real launch moment, using <c>initialStrength × efficiency(pathTicks)</c>. This is the
        /// "pre-raid analysis" number shown in the Active Travelers / Dashboard UI — it must not
        /// drift as the traveler advances or its current strength decays. On save reload,
        /// StartPath is re-called with skipLaunchTravelCache=true; in that case we only fill the
        /// value if it's still unset (e.g. old saves from before this fix).
        /// </summary>
        private void LockInProjectedArrivalStrength(float pathTicks, bool skipLaunchTravelCache)
        {
            if (traveler == null) return;
            // Drop-pod raids lock projected strength at launch with crow-flies × attrition mult (not ballistic flight ticks).
            if (traveler.mission == TravelerMission.RaidDropPod) return;
            if (skipLaunchTravelCache && traveler.projectedArrivalStrength > 0f) return;
            var seth = WorldDominationMod.settings;
            if (seth == null) return;
            float baseStrength = traveler.initialStrength > 0f ? traveler.initialStrength : traveler.travelerStrength;
            if (baseStrength <= 0f) return;
            if (!TravelUtils.TryEfficiencyFromPathTravelTicks(pathTicks, seth, traveler.Faction, out float eff)) return;
            traveler.projectedArrivalStrength = baseStrength * eff;
        }

        public void PatherTick(int delta)
        {
            if (!moving || traveler == null) return;
            if (TryResumeDeferredPostLoadPath())
                return;
            if (nextTileCostLeft > 0f)
                nextTileCostLeft -= (1.0f / (float)traveler.ticksPerMove) * (float)delta;
            else
            {
                int destTileId = nextTile.tileId;
                bool ballistic = UsesBallisticWorldFlight(traveler);
                if ((traveler.mission == TravelerMission.RoadBuilding
                        || traveler.mission == TravelerMission.RoadBlock
                        || traveler.mission == TravelerMission.SpikeTrap
                        || traveler.mission == TravelerMission.NpcFortify
                        || traveler.mission == TravelerMission.NpcAtTurret
                        || traveler.mission == TravelerMission.AtTurret
                        || traveler.mission == TravelerMission.Decontamination)
                    && Find.WorldGrid.InBounds(destTileId) && Find.WorldGrid[destTileId].WaterCovered)
                {
                    CancelMission("TSA_WD_RoadBuilderCannotCrossWater".Translate());
                    return;
                }
                if (!ballistic)
                {
                    WD_SameTileTravelerClash.TryBeforeTravelerEntersTile_TravelerVsTraveler(traveler, destTileId);
                    if (traveler.Destroyed) return;
                }

                previousTileId = traveler.Tile;
                traveler.Tile = nextTile;
                if (!ballistic)
                {
                    WorldComponent_RoadBlocks.Get()?.ApplyTravelerExitDamage(previousTileId, traveler);
                    WorldComponent_SpikeTraps.Get()?.TryTriggerOnTravelerExit(previousTileId, traveler);
                    if (traveler.Destroyed) return;
                    TravelerPollutionDamage.ApplyOnTileExit(previousTileId, traveler);
                    if (traveler.Destroyed) return;
                    WD_SameTileTravelerClash.AfterTravelerLanded_TravelerVsCaravan(traveler, traveler.Tile);
                    if (traveler.Destroyed) return;
                    WD_SameTileTravelerClash.AfterTravelerLanded_DeferredMeetups(traveler, traveler.Tile);
                    if (traveler.Destroyed) return;
                    // Hostile raid walking onto an AT Turret tile clashes by strength (destination turret left to arrival).
                    AtTurretRetaliationUtility.TryClashOnSharedTile(traveler, traveler.Tile);
                    if (traveler.Destroyed) return;
                    // Mortar / RR outposts on this tile act as choke-point fortresses vs hostile ground raids.
                    if (Raid_Simulated.TryInterceptRaidAtFortressOutpost(traveler))
                        return;
                    // Feature A: opportunistic retargeting onto a weaker settlement/outpost passed en route.
                    // AT Turret proximity detour first (always-on magnet at fire range; save/restore, not permanent ToO).
                    AtTurretRetaliationUtility.TryCheckProximityDetour(traveler, previousTileId);
                    if (traveler.Destroyed) return;
                    TargetOfOpportunityUtility.TryCheckTargetOfOpportunity(traveler, previousTileId);
                    if (traveler.Destroyed) return;
                    // Feature C (WD-traveler half): settlements with ambush capability may launch an interceptor at a passing traveler.
                    SettlementAmbushUtility.TryCheckAmbush(traveler, traveler.Tile.tileId);
                    if (traveler.Destroyed) return;
                    // Feature D: event-driven ground RR/mortar dispatch fast path (airborne/ballistic AA untouched).
                    WorldComponent_InterceptionScheduler.Current?.TryEventDrivenGroundIntercept(traveler, traveler.Tile.tileId);
                    if (traveler.Destroyed) return;
                }

                // BRANCHED HELPER: Validation (mortar shells fly a fixed ballistic arc; target validation only at arrival).
                if (!ballistic && !WorldActions_Traveler.ValidateMission(traveler, destTile))
                {
                    if (!traveler.Destroyed)
                        CancelMission(null);
                    return;
                }

                if (traveler.Tile == destTile) { StopDead(); ArrivalAction(); }
                else SetupNextTile();
            }
        }

        private void CancelMission(string reason)
        {
            TravelerEndpointUtility.RefundTravelerStrength(traveler, 1f);
            if (traveler.mission == TravelerMission.Raid || traveler.mission == TravelerMission.RaidDropPod)
                Raid_Simulated.RefundAlliedRaidOrderGoodwill(traveler);
            if (traveler is WorldObject_Traveler_SettlementBuy buyAbort)
            {
                SettlementBuyUtility.IsDealStillValid(buyAbort, out var fail);
                if (fail == SettlementBuyUtility.SettlementBuyFailReason.None)
                    fail = SettlementBuyUtility.SettlementBuyFailReason.SettlementGone;
                SettlementBuyUtility.RefundPayment(buyAbort, fail);
            }
            if (traveler is WorldObject_Traveler_SettlementGift giftAbort)
            {
                SettlementGiftUtility.IsGiftStillValid(giftAbort, out var fail);
                if (fail == SettlementGiftUtility.SettlementGiftFailReason.None)
                    fail = SettlementGiftUtility.SettlementGiftFailReason.SettlementGone;
                SettlementGiftUtility.RefundPayment(giftAbort, fail);
            }
            if (traveler is WorldObject_Traveler_SettlementBribe bribeAbort)
            {
                SettlementBribeUtility.IsBribeStillValid(bribeAbort, out var fail);
                if (fail == SettlementBribeUtility.BribeFailReason.None)
                    fail = SettlementBribeUtility.BribeFailReason.TargetGone;
                SettlementBribeUtility.RefundPayment(bribeAbort, fail);
            }
            if (traveler is WorldObject_Traveler_DiplomacyNegotiate negotiateAbort)
            {
                DiplomacyNegotiateUtility.IsDealStillValid(negotiateAbort, out var fail);
                if (fail == DiplomacyNegotiateUtility.NegotiateFailReason.None)
                    fail = DiplomacyNegotiateUtility.NegotiateFailReason.DestinationGone;
                DiplomacyNegotiateUtility.RefundPayment(negotiateAbort, fail);
            }

            if (string.IsNullOrEmpty(reason))
            {
                switch (traveler.mission)
                {
                    case TravelerMission.Raid:
                    case TravelerMission.RaidDropPod:
                        reason = "TSA_WD_Log_RaidCancelled".Translate(traveler.Label); break;
                    case TravelerMission.DebugRaidTransit: reason = "TSA_WD_Log_DebugRaidCancelled".Translate(traveler.Label); break;
                    case TravelerMission.Expansion: reason = "TSA_WD_Log_ExpansionCancelled".Translate(traveler.Label); break;
                    case TravelerMission.RoadBuilding: reason = "TSA_WD_Log_RoadCancelled".Translate(traveler.Label); break;
                    default: reason = "TSA_WD_Log_MissionAborted_TargetInvalid".Translate(); break;
                }
            }

            var manager = Find.World.GetComponent<WorldComponent_SpreadManager>();
            manager?.AddLog(new SpreadLogEntry(reason, traveler, traveler.originObject));

            StopDead();
            traveler.Destroy();
        }

        private void SetupNextTile()
        {
            if (curPath != null && curPath.Found)
            {
                if (curPath.NodesLeftCount == 0)
                {
                    HandlePathExhaustedUnexpectedly();
                    return;
                }
                nextTile = curPath.ConsumeNextNode();
            }
            else
            {
                if (fallbackPathNodes == null || fallbackPathIndex >= fallbackPathNodes.Count - 1)
                {
                    HandlePathExhaustedUnexpectedly();
                    return;
                }
                fallbackPathIndex++;
                nextTile = fallbackPathNodes[fallbackPathIndex];
            }

            float hopUnits;
            if (UsesBallisticWorldFlight(traveler))
            {
                // Single ballistic hop: cost = full tile distance so slerp interpolates across the whole flight.
                var grid = Find.WorldGrid;
                hopUnits = (grid != null && traveler.Tile.tileId >= 0 && nextTile.tileId >= 0)
                    ? Mathf.Max(1f, grid.ApproxDistanceInTiles(traveler.Tile.tileId, nextTile.tileId))
                    : 1f;
            }
            else
            {
                hopUnits = TravelUtils.GetTravelerHopDifficultyUnits(traveler.Tile, nextTile);
            }
            nextTileCostTotal = hopUnits;
            nextTileCostLeft = hopUnits;
        }

        private bool TryResumeDeferredPostLoadPath()
        {
            if (!deferredPostLoadResume)
                return false;

            if (WdPostLoadGuard.ShouldDeferTravelerArrival())
                return true;

            deferredPostLoadResume = false;
            if (destTile.Valid)
                StartPath(destTile, skipLaunchTravelCache: true);
            return false;
        }

        public void StopDead()
        {
            moving = false;
            if (curPath != null) { curPath.ReleaseToPool(); curPath = null; }
            fallbackPathNodes = null;
            fallbackPathIndex = 0;
        }

        private void HandleArrivedOnDestTile()
        {
            StopDead();
            ArrivalAction();
        }

        /// <summary>Route ended before destination: arrive if already there, one full repath attempt, else abort.</summary>
        private void HandlePathExhaustedUnexpectedly()
        {
            if (traveler == null || traveler.Destroyed) return;

            if (destTile.Valid && traveler.Tile == destTile)
            {
                HandleArrivedOnDestTile();
                return;
            }

            if (!pathExhaustionRepathAttempted && destTile.Valid)
            {
                pathExhaustionRepathAttempted = true;
                PlanetTile dest = destTile;
                StartPath(dest, skipLaunchTravelCache: true);
                if (moving)
                    return;
            }

            AbortStrandedTraveler();
        }

        private void AbortStrandedTraveler()
        {
            if (traveler == null || traveler.Destroyed) return;
            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            TravelerEndpointUtility.AbortTraveler(
                traveler,
                "TSA_WD_Log_TravelerAborted_Stranded".Translate(traveler.Label),
                manager);
        }

        private void ArrivalAction()
        {
            if (traveler == null) return;
            // BRANCHED HELPER: Outcome
            WorldActions_Traveler.ExecuteArrival(traveler, previousTileId);
        }

        public void DrawPathHelper()
        {
            if (traveler == null) return;

            // After load, curPath/fallback are not scribed. Rebuild on first draw so the white
            // route appears before the next TickInterval resume (construction crews especially).
            if (deferredPostLoadResume && destTile.Valid && !WdPostLoadGuard.ShouldDeferTravelerArrival())
            {
                deferredPostLoadResume = false;
                StartPath(destTile, skipLaunchTravelCache: true);
            }

            if (!moving)
            {
                LogPathDrawDiagOncePerInterval("pather not moving (no route polyline while stationary)");
                return;
            }

            WorldGrid worldGrid = Find.WorldGrid;
            if (worldGrid == null) return;

            drawPathScratch.Clear();
            bool drew = false;
            // Ground routes: one segment per hop (cheap). Ballistic: adaptive Slerp so the chord is not a line through the planet.
            int segOverride = UsesBallisticWorldFlight(traveler) ? GenDraw_WorldLineSmooth.AdaptiveSegments : 1;
            float lift = GenDraw_WorldLineSmooth.GetPathLineLift();
            Material mat = GenDraw_WorldLineSmooth.DefaultPathLineMat;

            drawPathScratch.Add(traveler.DrawPos);

            if (curPath != null && curPath.Found && curPath.NodesLeftCount > 0)
            {
                if (nextTile.Valid)
                    drawPathScratch.Add(worldGrid.GetTileCenter(nextTile));
                for (int i = 0; i < curPath.NodesLeftCount; i++)
                    drawPathScratch.Add(worldGrid.GetTileCenter(curPath.Peek(i)));
            }
            else if (fallbackPathNodes != null && fallbackPathNodes.Count > 0)
            {
                // Include the current hop (index) through dest. Old gate required index < Count-1,
                // which skipped the fallback branch on the final hop and could miss dest entirely
                // when nextTile was briefly invalid after load.
                int start = Mathf.Clamp(fallbackPathIndex, 0, fallbackPathNodes.Count - 1);
                for (int i = start; i < fallbackPathNodes.Count; i++)
                    drawPathScratch.Add(worldGrid.GetTileCenter(fallbackPathNodes[i]));
            }
            else if (nextTile.Valid)
            {
                drawPathScratch.Add(worldGrid.GetTileCenter(nextTile));
            }

            // Always end on destTile when known (covers short construction hops and path edge cases).
            if (destTile.Valid)
            {
                Vector3 destCenter = worldGrid.GetTileCenter(destTile);
                if (drawPathScratch.Count < 2
                    || (drawPathScratch[drawPathScratch.Count - 1] - destCenter).sqrMagnitude > 1e-4f)
                    drawPathScratch.Add(destCenter);
            }

            if (drawPathScratch.Count >= 2)
            {
                GenDraw_WorldLineSmooth.DrawSmoothWorldPolyline(drawPathScratch, mat, 1f, lift, segOverride);
                drew = true;
            }

            if (!drew)
                LogPathDrawDiagOncePerInterval(BuildPathDrawMissDetail());
        }

        /// <summary>Dev-only: throttled log when a selected traveler should show a path but nothing was drawn.</summary>
        private void LogPathDrawDiagOncePerInterval(string detail)
        {
            if (!Prefs.DevMode || traveler == null || !Find.WorldSelector.IsSelected(traveler)) return;
            int t = Find.TickManager.TicksGame;
            if (t - lastPathDrawDiagTick < 600) return;
            lastPathDrawDiagTick = t;
            Log.Message($"[TSA WD] Traveler path overlay: \"{traveler.Label}\" — {detail}");
        }

        private string BuildPathDrawMissDetail()
        {
            string cur = curPath == null ? "curPath=null" : $"curPath Found={curPath.Found} NodesLeft={curPath.NodesLeftCount}";
            string fb = fallbackPathNodes == null ? "fallback=null" : $"fallback count={fallbackPathNodes.Count} index={fallbackPathIndex}";
            string dest = destTile.Valid ? destTile.tileId.ToString() : "invalid";
            return $"moving but drew no line ({cur}; {fb}; destTile={dest}; nextTile.Valid={nextTile.Valid})";
        }

        public float GetRemainingTravelTicks()
        {
            if (!moving || traveler == null) return 0f;
            int tpm = TravelUtils.ResolveTicksPerMove(traveler.ticksPerMove);
            float ticks = nextTileCostLeft * tpm;

            if (curPath != null && curPath.Found)
            {
                if (curPath.NodesLeftCount <= 0 || !nextTile.Valid) return ticks;
                PlanetTile edgeFrom = nextTile;
                PlanetTile edgeTo = curPath.Peek(0);
                ticks += TravelUtils.GetTravelerHopDifficultyUnits(edgeFrom, edgeTo) * tpm;
                for (int i = 0; i < curPath.NodesLeftCount - 1; i++)
                {
                    PlanetTile a = curPath.Peek(i);
                    PlanetTile b = curPath.Peek(i + 1);
                    ticks += TravelUtils.GetTravelerHopDifficultyUnits(a, b) * tpm;
                }
                return ticks;
            }

            if (fallbackPathNodes == null || fallbackPathNodes.Count == 0) return ticks;
            int start = Mathf.Max(fallbackPathIndex, 1);
            for (int i = start; i < fallbackPathNodes.Count - 1; i++)
                ticks += TravelUtils.GetTravelerHopDifficultyUnits(fallbackPathNodes[i], fallbackPathNodes[i + 1]) * tpm;
            return ticks;
        }

        private static bool UsesBallisticWorldFlight(WorldObject_Traveler t) => IsBallisticWorldFlight(t);

        /// <summary>Mortar shells and drop-pod travelers fly one great-circle hop (no tile path).</summary>
        public static bool IsBallisticWorldFlight(WorldObject_Traveler t)
        {
            if (t == null) return false;
            if (t.mission == TravelerMission.MortarStrike) return true;
            if (t.mission == TravelerMission.AntiAirStrike) return true;
            if (t.mission == TravelerMission.RapidResponseDropPod) return true;
            if (t.mission == TravelerMission.RaidDropPod) return true;
            return t is WorldObject_Traveler_Outpost_Delivery d && d.deliveryViaDropPod
                || t is WorldObject_Traveler_Outpost_Upgrade u && u.upgradeViaDropPod
                || t is WorldObject_Traveler_TradePayment trade && trade.tradeViaDropPod;
        }
    }
}
