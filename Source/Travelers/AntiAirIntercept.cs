using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Lead intercept for ballistic airborne targets (drop pods / mortar shells / vanilla transport pods).</summary>
    public static class AntiAirIntercept
    {
        private const int SampleCount = 40;
        private const int RefineSteps = 12;
        /// <summary>Matches vanilla <see cref="TravellingTransporters"/> travel speed (traveledPct per tick per radian of arc).</summary>
        private const float VanillaPodTravelSpeed = 0.00025f;

        /// <summary>Solve where a flak shell from <paramref name="origin"/> should fly to meet a WD ballistic traveler mid-arc.</summary>
        public static bool TryResolveLeadFlight(
            WorldObject origin,
            WorldObject_Traveler pod,
            float maxRange,
            int shellTicksPerMove,
            out Vector3 meetWorldPos,
            out float flightTicks,
            out float rangeTiles)
        {
            meetWorldPos = Vector3.zero;
            flightTicks = 0f;
            rangeTiles = 0f;
            if (origin == null || pod == null || pod.Destroyed) return false;
            if (!WD_PathFollower.IsBallisticWorldFlight(pod) || pod.mission == TravelerMission.AntiAirStrike)
                return false;

            WorldGrid grid = Find.WorldGrid;
            if (grid == null) return false;

            Vector3 aaPos = grid.GetTileCenter(origin.Tile);
            if (!TryGetPodArc(pod, grid, out Vector3 podFrom, out Vector3 podTo, out float progress, out float remainingPodTicks))
                return false;

            Vector3 podNow = Vector3.Slerp(podFrom, podTo, Mathf.Clamp01(progress));
            return TryResolveLeadOnArc(aaPos, podNow, podTo, remainingPodTicks, maxRange, shellTicksPerMove, grid,
                out meetWorldPos, out flightTicks, out rangeTiles, origin.LabelCap, pod.LabelCap);
        }

        /// <summary>Lead intercept on the remaining great-circle arc for vanilla transport pods.</summary>
        public static bool TryResolveLeadFlightForVanillaPods(
            WorldObject origin,
            TravellingTransporters pods,
            float maxRange,
            int shellTicksPerMove,
            out Vector3 meetWorldPos,
            out float flightTicks,
            out float rangeTiles)
        {
            meetWorldPos = Vector3.zero;
            flightTicks = 0f;
            rangeTiles = 0f;
            if (origin == null || pods == null || pods.Destroyed) return false;
            if (origin.Tile.tileId < 0) return false;

            WorldGrid grid = Find.WorldGrid;
            if (grid == null) return false;

            Vector3 aaPos = grid.GetTileCenter(origin.Tile);
            if (!TryGetVanillaPodArc(pods, grid, out Vector3 podFrom, out Vector3 podTo, out float progress, out float remainingPodTicks))
                return false;

            Vector3 podNow = pods.DrawPos;
            if (podNow.sqrMagnitude < 0.0001f)
                podNow = Vector3.Slerp(podFrom, podTo, Mathf.Clamp01(progress));

            return TryResolveLeadOnArc(aaPos, podNow, podTo, remainingPodTicks, maxRange, shellTicksPerMove, grid,
                out meetWorldPos, out flightTicks, out rangeTiles, origin.LabelCap, pods.LabelCap);
        }

        /// <summary>Aim at current DrawPos / tile center for non-WD airborne world objects.</summary>
        public static bool TryResolveLeadFlightForWorldObject(
            WorldObject origin,
            WorldObject target,
            float maxRange,
            int shellTicksPerMove,
            out Vector3 meetWorldPos,
            out float flightTicks,
            out float rangeTiles)
        {
            meetWorldPos = Vector3.zero;
            flightTicks = 0f;
            rangeTiles = 0f;
            if (origin == null || target == null || target.Destroyed) return false;

            if (target is TravellingTransporters pods)
                return TryResolveLeadFlightForVanillaPods(origin, pods, maxRange, shellTicksPerMove,
                    out meetWorldPos, out flightTicks, out rangeTiles);

            if (VehicleFrameworkAerialAaCompat.IsAerialVehicleInFlight(target))
            {
                WorldGrid gridVf = Find.WorldGrid;
                if (gridVf == null) return false;
                if (origin.Tile.tileId < 0) return false;
                Vector3 aaPosVf = gridVf.GetTileCenter(origin.Tile);
                Vector3 meetVf = VehicleFrameworkAerialAaCompat.GetAimPos(target, ticksAhead: 40);
                if (meetVf.sqrMagnitude < 0.0001f)
                    meetVf = target.DrawPos;
                if (meetVf.sqrMagnitude < 0.0001f) return false;
                rangeTiles = WorldDistTiles(aaPosVf, meetVf, gridVf);
                if (rangeTiles > maxRange) return false;
                meetWorldPos = meetVf;
                flightTicks = Mathf.Max(1f, rangeTiles * Mathf.Max(1, shellTicksPerMove));
                return true;
            }

            WorldGrid grid = Find.WorldGrid;
            if (grid == null) return false;
            if (origin.Tile.tileId < 0 || target.Tile.tileId < 0) return false;

            Vector3 aaPos = grid.GetTileCenter(origin.Tile);
            Vector3 meet = target.DrawPos;
            if (meet.sqrMagnitude < 0.0001f)
                meet = grid.GetTileCenter(target.Tile);
            if (meet.sqrMagnitude < 0.0001f) return false;

            rangeTiles = WorldDistTiles(aaPos, meet, grid);
            if (rangeTiles > maxRange) return false;

            meetWorldPos = meet;
            flightTicks = Mathf.Max(1f, rangeTiles * Mathf.Max(1, shellTicksPerMove));
            return true;
        }

        public static bool TryResolveAimTile(WorldObject origin, WorldObject_Traveler pod, float maxRange, out int aimTileId)
        {
            aimTileId = -1;
            if (!TryResolveLeadFlight(origin, pod, maxRange, WorldActions_Traveler.GetFlakShellTicksPerMove(),
                    out _, out _, out float rangeTiles))
                return false;
            WD_PathFollower tp = pod?.pather;
            if (tp != null && tp.moving && tp.nextTile.Valid)
                aimTileId = tp.nextTile.tileId;
            else if (pod != null)
                aimTileId = pod.Tile.tileId;
            return aimTileId >= 0 && rangeTiles <= maxRange + 0.01f;
        }

        private static bool TryResolveLeadOnArc(
            Vector3 aaPos,
            Vector3 podNow,
            Vector3 podTo,
            float remainingPodTicks,
            float maxRange,
            int shellTicksPerMove,
            WorldGrid grid,
            out Vector3 meetWorldPos,
            out float flightTicks,
            out float rangeTiles,
            string originLabel,
            string targetLabel)
        {
            meetWorldPos = Vector3.zero;
            flightTicks = 0f;
            rangeTiles = 0f;
            int shellTpm = Mathf.Max(1, shellTicksPerMove);
            float rem = Mathf.Max(0f, remainingPodTicks);

            // True intercept: shell flight time ≈ pod time-to-point. Old code required shell+8 < pod
            // (rejecting the real meet) and fell back to the nearest arc point — classic under-lead
            // (~shellFlightTicks / podTpm tiles when aiming at "now").
            float bestU = -1f;
            float bestAbsDiff = float.MaxValue;
            bool foundCatchable = false;

            for (int i = 0; i <= SampleCount; i++)
            {
                float u = i / (float)SampleCount;
                if (!TryScoreLeadSample(aaPos, podNow, podTo, rem, u, maxRange, shellTpm, grid,
                        out float absDiff, out bool catchable))
                    continue;

                if (catchable)
                {
                    if (!foundCatchable || absDiff < bestAbsDiff - 0.01f
                        || (Mathf.Abs(absDiff - bestAbsDiff) <= 0.01f && u > bestU))
                    {
                        foundCatchable = true;
                        bestAbsDiff = absDiff;
                        bestU = u;
                    }
                }
                else if (!foundCatchable && absDiff < bestAbsDiff)
                {
                    // Best-effort when nothing is catchable: least-late sample (still leads ahead).
                    bestAbsDiff = absDiff;
                    bestU = u;
                }
            }

            if (bestU < 0f)
            {
                WDVerbose.Msg($"AA lead FAIL origin={originLabel} pod={targetLabel} maxRange={maxRange:F1} (no arc sample in range)");
                return false;
            }

            // Refine around the discrete sample for a tighter shell/pod time match.
            if (foundCatchable && bestU >= 0f)
            {
                float lo = Mathf.Max(0f, bestU - 1f / SampleCount);
                float hi = Mathf.Min(1f, bestU + 1f / SampleCount);
                for (int r = 0; r < RefineSteps; r++)
                {
                    float u1 = lo + (hi - lo) * (1f / 3f);
                    float u2 = lo + (hi - lo) * (2f / 3f);
                    bool ok1 = TryScoreLeadSample(aaPos, podNow, podTo, rem, u1, maxRange, shellTpm, grid,
                        out float d1, out bool c1);
                    bool ok2 = TryScoreLeadSample(aaPos, podNow, podTo, rem, u2, maxRange, shellTpm, grid,
                        out float d2, out bool c2);
                    float score1 = !ok1 ? float.MaxValue : (c1 ? d1 : d1 + 1e6f);
                    float score2 = !ok2 ? float.MaxValue : (c2 ? d2 : d2 + 1e6f);
                    if (score1 <= score2)
                    {
                        hi = u2;
                        if (ok1 && (c1 || !foundCatchable) && d1 <= bestAbsDiff + 0.01f)
                        {
                            bestU = u1;
                            bestAbsDiff = d1;
                            if (c1) foundCatchable = true;
                        }
                    }
                    else
                    {
                        lo = u1;
                        if (ok2 && (c2 || !foundCatchable) && d2 <= bestAbsDiff + 0.01f)
                        {
                            bestU = u2;
                            bestAbsDiff = d2;
                            if (c2) foundCatchable = true;
                        }
                    }
                }
            }

            Vector3 meet = Vector3.Slerp(podNow, podTo, bestU);
            float tiles = WorldDistTiles(aaPos, meet, grid);
            // Fixed shell speed (tiles × tpm). Lead already picked a meet where shell ≈ pod time when catchable;
            // do not stretch closer shells so a volley arrives together.
            float shellTicks = tiles * shellTpm;

            meetWorldPos = meet;
            flightTicks = Mathf.Max(1f, shellTicks);
            rangeTiles = tiles;
            return true;
        }

        /// <summary>
        /// Scores one arc fraction u. Catchable = shell at fixed speed can reach the point by the time the pod does.
        /// </summary>
        private static bool TryScoreLeadSample(
            Vector3 aaPos,
            Vector3 podNow,
            Vector3 podTo,
            float remainingPodTicks,
            float u,
            float maxRange,
            int shellTpm,
            WorldGrid grid,
            out float absDiff,
            out bool catchable)
        {
            absDiff = float.MaxValue;
            catchable = false;
            Vector3 p = Vector3.Slerp(podNow, podTo, u);
            float tiles = WorldDistTiles(aaPos, p, grid);
            if (tiles > maxRange) return false;

            float shellTicks = tiles * shellTpm;
            float podTicks = remainingPodTicks * u;
            absDiff = Mathf.Abs(shellTicks - podTicks);
            // u=0 is always "catchable" only if shell distance is ~0; otherwise need podTicks >= shellTicks.
            catchable = podTicks + 0.5f >= shellTicks || (u <= 0.0001f && tiles <= 0.15f);
            return true;
        }

        private static bool TryGetPodArc(
            WorldObject_Traveler pod,
            WorldGrid grid,
            out Vector3 from,
            out Vector3 to,
            out float progress,
            out float remainingTicks)
        {
            from = to = Vector3.zero;
            progress = 0f;
            remainingTicks = 0f;
            WD_PathFollower tp = pod.pather;
            if (tp == null || !tp.moving || !tp.nextTile.Valid)
            {
                from = grid.GetTileCenter(pod.Tile);
                int destId = -1;
                if (tp != null && tp.destTile.Valid)
                    destId = tp.destTile.tileId;
                else if (pod.targetObject != null && !pod.targetObject.Destroyed && pod.targetObject.Tile.tileId >= 0)
                    destId = pod.targetObject.Tile.tileId;

                if (destId >= 0 && destId != pod.Tile.tileId)
                {
                    to = grid.GetTileCenter(destId);
                    progress = 0f;
                    int podTpm = Mathf.Max(1, pod.ticksPerMove);
                    remainingTicks = Mathf.Max(1f, WorldDistTiles(from, to, grid) * podTpm);
                    return true;
                }

                // No destination yet: point-arc at launch (callers should prefer waiting for a hop).
                from = to = grid.GetTileCenter(pod.Tile);
                return true;
            }

            from = grid.GetTileCenter(pod.Tile);
            to = grid.GetTileCenter(tp.nextTile);
            float total = Mathf.Max(0.001f, tp.nextTileCostTotal);
            float left = Mathf.Max(0f, tp.nextTileCostLeft);
            progress = Mathf.Clamp01(1f - left / total);
            int hopTpm = Mathf.Max(1, pod.ticksPerMove);
            // Prefer spherical remaining distance so lead matches DrawPos Slerp, not only cost units
            // (cost uses Max(1, tileDist) which can disagree slightly with vector arc length).
            Vector3 now = Vector3.Slerp(from, to, progress);
            float remTiles = WorldDistTiles(now, to, grid);
            remainingTicks = Mathf.Max(left * hopTpm, remTiles * hopTpm);
            if (remainingTicks < 1f)
                remainingTicks = remTiles * hopTpm;
            return true;
        }

        private static bool TryGetVanillaPodArc(
            TravellingTransporters pods,
            WorldGrid grid,
            out Vector3 from,
            out Vector3 to,
            out float progress,
            out float remainingTicks)
        {
            from = to = Vector3.zero;
            progress = 0f;
            remainingTicks = 0f;
            if (pods == null || grid == null) return false;

            int fromTile = pods.Tile.tileId;
            if (fromTile < 0) return false;

            from = grid.GetTileCenter(fromTile);
            int toTile = pods.destinationTile.Valid ? pods.destinationTile.tileId : -1;
            if (toTile < 0)
            {
                to = from;
                progress = pods.DrawPos.sqrMagnitude > 0.0001f ? 1f : 0f;
                return true;
            }

            to = grid.GetTileCenter(toTile);
            Vector3 now = pods.DrawPos;
            if (now.sqrMagnitude < 0.0001f)
                now = from;

            float arcRad = GenMath.SphericalDistance(from.normalized, to.normalized);
            if (arcRad <= 0.0001f)
            {
                progress = 1f;
                remainingTicks = 0f;
                return true;
            }

            progress = Mathf.Clamp01(GenMath.SphericalDistance(from.normalized, now.normalized) / arcRad);
            float totalTicks = arcRad / VanillaPodTravelSpeed;
            remainingTicks = (1f - progress) * totalTicks;
            return true;
        }

        private static float WorldDistTiles(Vector3 a, Vector3 b, WorldGrid grid)
        {
            float cos = Mathf.Clamp(Vector3.Dot(a.normalized, b.normalized), -1f, 1f);
            float angle = Mathf.Acos(cos);
            return Mathf.Max(0.05f, grid.ApproxDistanceInTiles(angle));
        }

        /// <summary>Flight ticks for a fixed flak shell speed over the great-circle distance between two world positions.</summary>
        public static float FlightTicksAtFixedSpeed(Vector3 from, Vector3 to, int shellTicksPerMove)
        {
            WorldGrid grid = Find.WorldGrid;
            if (grid == null) return 1f;
            float tiles = WorldDistTiles(from, to, grid);
            return Mathf.Max(1f, tiles * Mathf.Max(1, shellTicksPerMove));
        }
    }
}
