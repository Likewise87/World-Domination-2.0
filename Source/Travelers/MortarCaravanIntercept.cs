using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Predicts a world tile along a hostile caravan's or WD traveler's route where a ballistic mortar shell (fixed speed per tile distance)
    /// arrives in the same tick window as the target, using per-edge travel costs. Assumes no rest, reroutes, or incidents.
    /// Aim is resolved once at fire time; shells never retarget in flight.
    /// </summary>
    public static class MortarCaravanIntercept
    {
        private const int MaxPathHopsConsidered = 48;

        // Cached open-instance delegate for Caravan_PathFollower.CostToMove(PlanetTile, PlanetTile). Building this
        // once avoids MethodInfo.Invoke reflection + boxing per path edge during route-prediction (50+ hops possible).
        private delegate int CostToMoveDelegate(Caravan_PathFollower pather, PlanetTile from, PlanetTile to);
        private static readonly CostToMoveDelegate CostToMoveFast = BuildCostToMoveDelegate();

        private static readonly List<int> scratchForward = new List<int>(64);
        private static float[] scratchArrivalTicks = new float[64];

        private static CostToMoveDelegate BuildCostToMoveDelegate()
        {
            var mi = AccessTools.Method(
                typeof(Caravan_PathFollower),
                "CostToMove",
                new[] { typeof(PlanetTile), typeof(PlanetTile) });
            if (mi == null) return null;
            try
            {
                return (CostToMoveDelegate)Delegate.CreateDelegate(typeof(CostToMoveDelegate), mi);
            }
            catch (Exception ex)
            {
                Log.Warning($"[TSA WD] MortarCaravanIntercept: failed to bind CostToMove delegate: {ex.Message}");
                return null;
            }
        }

        /// <summary>World ticks for the shell to fly from <paramref name="originTileId"/> to <paramref name="destTileId"/> (matches <see cref="WD_PathFollower"/> mortar hop).</summary>
        public static float ShellTravelTicks(int originTileId, int destTileId, int mortarTicksPerMove)
        {
            var grid = Find.WorldGrid;
            if (grid == null) return 0f;
            float d = Mathf.Max(1f, grid.ApproxDistanceInTiles(originTileId, destTileId));
            return d * TravelUtils.ResolveTicksPerMove(mortarTicksPerMove);
        }

        /// <summary>Tile the shell should aim at; for static targets this is <paramref name="target"/>.<see cref="WorldObject.Tile"/>.</summary>
        public static int ResolveMortarAimTileId(WorldObject origin, WorldObject target, float mortarMaxRange)
        {
            if (origin == null || target == null || target.Destroyed) return target?.Tile ?? -1;
            int mortarTpm = WorldActions_Traveler.GetMortarShellTicksPerMove();
            if (target is Caravan caravan)
                return TryResolveCaravanInterceptTileId(origin.Tile, caravan, mortarMaxRange, mortarTpm);
            if (target is WorldObject_Traveler traveler)
                return TryResolveTravelerInterceptTileId(origin.Tile, traveler, mortarMaxRange, mortarTpm);
            return target.Tile;
        }

        private static int TryResolveCaravanInterceptTileId(int originTileId, Caravan caravan, float mortarMaxRange, int mortarTicksPerMove)
        {
            if (caravan == null || caravan.Destroyed || !caravan.Spawned) return caravan?.Tile ?? -1;
            if (!caravan.pather.Moving || !caravan.pather.Destination.Valid)
                return caravan.Tile;

            PlanetLayer layer = Find.World?.grid?[caravan.Tile].Layer;
            if (layer?.Pather == null) return caravan.Tile;

            scratchForward.Clear();
            if (!TryFillCaravanForwardFromExistingPath(caravan, scratchForward)
                && !TryFillCaravanForwardFromFindPath(caravan, layer, scratchForward))
                return caravan.Tile;

            if (scratchForward.Count < 2 || scratchForward[0] != caravan.Tile)
                return caravan.Tile;

            if (CostToMoveFast == null)
            {
                Log.WarningOnce("[TSA WD] MortarCaravanIntercept: CostToMove not found; mortar aims at caravan's current tile.", 0x4d07_4d07);
                return caravan.Tile;
            }

            EnsureArrivalScratch(scratchForward.Count);
            float[] caravanArrivalTicks = scratchArrivalTicks;
            caravanArrivalTicks[0] = 0f;

            int firstTo = scratchForward[1];
            float firstEdgeFull = InvokeCostToMove(caravan.pather, caravan.Tile, firstTo, layer);
            float toFirstNode;
            if (caravan.pather.nextTile.Valid &&
                caravan.pather.nextTile.tileId == firstTo &&
                caravan.pather.nextTileCostTotal > 1e-4f)
            {
                float frac = Mathf.Clamp01(caravan.pather.nextTileCostLeft / caravan.pather.nextTileCostTotal);
                toFirstNode = frac * firstEdgeFull;
            }
            else
                toFirstNode = firstEdgeFull;

            caravanArrivalTicks[1] = toFirstNode;
            for (int i = 1; i < scratchForward.Count - 1; i++)
            {
                float hop = InvokeCostToMove(caravan.pather, scratchForward[i], scratchForward[i + 1], layer);
                caravanArrivalTicks[i + 1] = caravanArrivalTicks[i] + hop;
            }

            return PickBestInRangeAimTile(originTileId, scratchForward, caravanArrivalTicks, mortarMaxRange, mortarTicksPerMove, caravan.Tile);
        }

        private static bool TryFillCaravanForwardFromExistingPath(Caravan caravan, List<int> forward)
        {
            WorldPath path = caravan.pather.curPath;
            if (path == null || !path.Found || path.NodesLeftCount <= 0) return false;

            forward.Add(caravan.Tile);
            int nextId = caravan.pather.nextTile.Valid ? caravan.pather.nextTile.tileId : -1;
            if (nextId >= 0 && nextId != caravan.Tile)
                forward.Add(nextId);

            int maxPeek = Mathf.Min(path.NodesLeftCount, MaxPathHopsConsidered);
            for (int i = 0; i < maxPeek; i++)
            {
                PlanetTile node = path.Peek(i);
                if (!node.Valid) continue;
                int id = node.tileId;
                if (forward.Count > 0 && forward[forward.Count - 1] == id) continue;
                forward.Add(id);
                if (forward.Count > MaxPathHopsConsidered + 1) break;
            }

            return forward.Count >= 2;
        }

        private static bool TryFillCaravanForwardFromFindPath(Caravan caravan, PlanetLayer layer, List<int> forward)
        {
            PlanetTile dest = caravan.pather.Destination;
            using WorldPath path = layer.Pather.FindPath(caravan.Tile, dest, caravan, null);
            if (path == null || !path.Found || path.NodesReversed == null || path.NodesReversed.Count < 2)
                return false;

            var rev = path.NodesReversed;
            int start = Mathf.Max(0, rev.Count - 1 - MaxPathHopsConsidered);
            for (int i = rev.Count - 1; i >= start; i--)
                forward.Add(rev[i].tileId);
            return forward.Count >= 2;
        }

        private static int TryResolveTravelerInterceptTileId(int originTileId, WorldObject_Traveler traveler, float mortarMaxRange, int mortarTicksPerMove)
        {
            if (traveler == null || traveler.Destroyed) return traveler?.Tile ?? -1;
            WD_PathFollower tp = traveler.pather;
            if (tp == null || !tp.moving)
                return traveler.Tile;

            scratchForward.Clear();
            scratchForward.Add(traveler.Tile);
            EnsureArrivalScratch(MaxPathHopsConsidered + 2);
            float[] arrival = scratchArrivalTicks;
            arrival[0] = 0f;

            int tpm = Mathf.Max(1, traveler.ticksPerMove);
            int fromTile = traveler.Tile;

            if (tp.nextTile.Valid && tp.nextTile.tileId != traveler.Tile)
            {
                scratchForward.Add(tp.nextTile.tileId);
                arrival[1] = Mathf.Max(0f, tp.nextTileCostLeft) * tpm;
                fromTile = tp.nextTile.tileId;
            }

            WorldPath path = tp.curPath;
            if (path != null && path.Found)
            {
                int maxLookahead = Mathf.Min(path.NodesLeftCount, MaxPathHopsConsidered);
                for (int i = 0; i < maxLookahead; i++)
                {
                    PlanetTile node = path.Peek(i);
                    if (!node.Valid) continue;
                    int id = node.tileId;
                    if (scratchForward[scratchForward.Count - 1] == id) continue;
                    float hop = TravelUtils.GetTravelerHopDifficultyUnits(
                        new PlanetTile(fromTile, traveler.Tile.Layer),
                        new PlanetTile(id, traveler.Tile.Layer)) * tpm;
                    int idx = scratchForward.Count;
                    EnsureArrivalScratch(idx + 1);
                    arrival = scratchArrivalTicks;
                    arrival[idx] = arrival[idx - 1] + hop;
                    scratchForward.Add(id);
                    fromTile = id;
                    if (scratchForward.Count > MaxPathHopsConsidered + 1) break;
                }
            }

            if (scratchForward.Count < 2)
                return traveler.Tile;

            return PickBestInRangeAimTile(originTileId, scratchForward, arrival, mortarMaxRange, mortarTicksPerMove, traveler.Tile);
        }

        private static int PickBestInRangeAimTile(
            int originTileId,
            List<int> forward,
            float[] arrivalTicks,
            float mortarMaxRange,
            int mortarTicksPerMove,
            int fallbackTile)
        {
            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            int bestIdx = -1;
            float bestDiff = float.MaxValue;
            bool sawInRange = false;
            for (int j = 1; j < forward.Count; j++)
            {
                float range = RangeDistance(originTileId, forward[j], manager);
                if (range > mortarMaxRange)
                {
                    if (sawInRange) break;
                    continue;
                }

                sawInRange = true;
                float shellTicks = ShellTravelTicks(originTileId, forward[j], mortarTicksPerMove);
                float diff = Mathf.Abs(shellTicks - arrivalTicks[j]);
                if (bestIdx < 0 || diff < bestDiff || (Mathf.Approximately(diff, bestDiff) && j > bestIdx))
                {
                    bestDiff = diff;
                    bestIdx = j;
                }
            }

            if (bestIdx < 0)
                return fallbackTile;
            return forward[bestIdx];
        }

        private static void EnsureArrivalScratch(int count)
        {
            if (scratchArrivalTicks == null || scratchArrivalTicks.Length < count)
                scratchArrivalTicks = new float[Mathf.Max(count, 64)];
        }

        /// <summary>Same range metric as manual mortar validation (spherical world when manager present).</summary>
        private static float RangeDistance(int fromTileId, int toTileId, WorldComponent_SpreadManager manager)
        {
            if (manager != null)
                return WorldActions_Utils.GetDistance(fromTileId, toTileId, manager);
            return Find.WorldGrid.ApproxDistanceInTiles(fromTileId, toTileId);
        }

        private static float InvokeCostToMove(Caravan_PathFollower pather, int fromTileId, int toTileId, PlanetLayer layer)
        {
            if (layer == null) return 0f;
            return InvokeCostToMove(pather, new PlanetTile(fromTileId, layer), new PlanetTile(toTileId, layer));
        }

        private static float InvokeCostToMove(Caravan_PathFollower pather, PlanetTile from, PlanetTile to)
        {
            var del = CostToMoveFast;
            if (del == null) return 0f;
            try
            {
                return del(pather, from, to);
            }
            catch (Exception ex)
            {
                Log.Warning($"[TSA WD] MortarCaravanIntercept.CostToMove invoke: {ex.Message}");
                return 0f;
            }
        }
    }
}
