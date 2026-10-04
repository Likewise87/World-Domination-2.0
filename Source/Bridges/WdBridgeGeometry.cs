using System.Collections.Generic;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Bank-to-bank bridge span validation (1..MaxWaterTiles WaterCovered middles).</summary>
    public static class WdBridgeGeometry
    {
        private static readonly List<PlanetTile> NeighborScratch = new List<PlanetTile>(8);
        private static readonly List<PlanetTile> BankNeighborScratch = new List<PlanetTile>(8);
        private static readonly Queue<int> BfsQueue = new Queue<int>(16);
        private static readonly Dictionary<int, int> BfsPrev = new Dictionary<int, int>(16);
        private static readonly Dictionary<int, int> BfsDepth = new Dictionary<int, int>(16);
        private static readonly Dictionary<int, int> LandDepthScratch = new Dictionary<int, int>(64);
        private static readonly Queue<int> LandBfsQueue = new Queue<int>(16);

        public enum RejectReason
        {
            None,
            InvalidTile,
            StartNotLand,
            StartNotAdjacentToWater,
            EndNotLand,
            EndNotAdjacentToWater,
            SameTile,
            NotStraightOrTooLong,
            AlreadyBridged,
            BankAlreadyUsed,
            NotBridged,
            AmbiguousBridge,
            SpanOccupied,
            ProjectLocked,
            ImpassableBank,
            SameShore
        }

        /// <summary>Land tile with at least one WaterCovered neighbor. Incomplete stub banks stay valid for resume.</summary>
        public static bool IsValidStartBank(int tileId) => IsLandAdjacentToWater(tileId);

        /// <summary>
        /// True when this land tile already has a road link onto a WD-bridged water tile
        /// (it is a start or end of an existing bridge).
        /// </summary>
        public static bool BankAlreadyAnchorsBridge(int landTileId)
        {
            WorldGrid grid = Find.WorldGrid;
            if (grid == null || landTileId < 0 || !grid.InBounds(landTileId)) return false;
            if (grid[landTileId].WaterCovered) return false;

            PlanetLayer layer = PlanetSurfaceWorldActions.WdSurfaceLayer ?? grid.Surface;
            BankNeighborScratch.Clear();
            grid.GetTileNeighbors(new PlanetTile(landTileId, layer), BankNeighborScratch);
            for (int i = 0; i < BankNeighborScratch.Count; i++)
            {
                int n = BankNeighborScratch[i].tileId;
                if (!grid[n].WaterCovered) continue;
                if (!WorldComponent_WdBridges.IsBridgedWaterTile(n)) continue;
                if (WorldActions_Roads.HasRoadLink(landTileId, n))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Land-adjacent-to-water banks in a hop window, optionally clipped by ApproxDistance.
        /// One BFS; skips reserved banks. Clears <paramref name="into"/> first.
        /// </summary>
        public static void CollectLandBanks(
            int originTileId,
            int maxHops,
            float maxApproxDistance,
            HashSet<int> reserved,
            HashSet<int> into,
            int excludeTileId = -1,
            bool traverseWater = true)
        {
            into?.Clear();
            if (into == null || originTileId < 0 || maxHops < 0) return;
            WorldGrid grid = Find.WorldGrid;
            if (grid == null || !grid.InBounds(originTileId)) return;

            PlanetLayer layer = PlanetSurfaceWorldActions.WdSurfaceLayer ?? grid.Surface;
            PlanetTile originPt = new PlanetTile(originTileId, layer);
            BfsQueue.Clear();
            BfsDepth.Clear();
            BfsQueue.Enqueue(originTileId);
            BfsDepth[originTileId] = 0;
            while (BfsQueue.Count > 0)
            {
                int cur = BfsQueue.Dequeue();
                int depth = BfsDepth[cur];
                bool curLand = grid.InBounds(cur) && !grid[cur].WaterCovered;
                bool touchesWater = false;

                NeighborScratch.Clear();
                grid.GetTileNeighbors(new PlanetTile(cur, layer), NeighborScratch);
                for (int i = 0; i < NeighborScratch.Count; i++)
                {
                    int n = NeighborScratch[i].tileId;
                    if (!grid.InBounds(n)) continue;
                    if (grid[n].WaterCovered)
                    {
                        touchesWater = true;
                        if (!traverseWater) continue;
                    }
                    if (depth >= maxHops) continue;
                    if (BfsDepth.ContainsKey(n)) continue;
                    // Prune outside ApproxDistance so we do not flood the whole hop diamond.
                    if (maxApproxDistance >= 0f
                        && grid.ApproxDistanceInTiles(originPt, new PlanetTile(n, layer)) > maxApproxDistance)
                        continue;
                    BfsDepth[n] = depth + 1;
                    BfsQueue.Enqueue(n);
                }

                if (!curLand || cur == excludeTileId) continue;
                if (reserved != null && reserved.Contains(cur)) continue;
                if (!touchesWater) continue;
                into.Add(cur);
            }
        }

        /// <summary>
        /// Opposite land banks reachable by one water-only BFS (1..MaxWaterTiles).
        /// Same-shore exits (land-reachable within the water span length) are rejected.
        /// Fills <paramref name="chainsByEnd"/> with the shortest accepted build chain per end bank.
        /// </summary>
        public static void CollectFarBanksWithChains(
            int startBankTileId,
            HashSet<int> reserved,
            HashSet<int> into,
            Dictionary<int, List<int>> chainsByEnd)
        {
            into?.Clear();
            chainsByEnd?.Clear();
            if (into == null || startBankTileId < 0) return;
            WorldGrid grid = Find.WorldGrid;
            if (grid == null || !grid.InBounds(startBankTileId) || grid[startBankTileId].WaterCovered)
                return;

            PlanetLayer layer = PlanetSurfaceWorldActions.WdSurfaceLayer ?? grid.Surface;
            // Land depths from the start bank: used to reject coastal same-shore exits.
            FillLandDepths(startBankTileId, WorldComponent_WdBridges.MaxWaterTiles, layer, grid);

            BfsQueue.Clear();
            BfsDepth.Clear();
            BfsPrev.Clear();

            NeighborScratch.Clear();
            grid.GetTileNeighbors(new PlanetTile(startBankTileId, layer), NeighborScratch);
            for (int i = 0; i < NeighborScratch.Count; i++)
            {
                int water = NeighborScratch[i].tileId;
                if (!grid.InBounds(water) || !grid[water].WaterCovered) continue;
                if (BfsPrev.ContainsKey(water)) continue;
                BfsPrev[water] = startBankTileId;
                BfsDepth[water] = 1;
                BfsQueue.Enqueue(water);
            }

            while (BfsQueue.Count > 0)
            {
                int water = BfsQueue.Dequeue();
                int depth = BfsDepth[water];

                NeighborScratch.Clear();
                grid.GetTileNeighbors(new PlanetTile(water, layer), NeighborScratch);
                for (int i = 0; i < NeighborScratch.Count; i++)
                {
                    int n = NeighborScratch[i].tileId;
                    if (!grid.InBounds(n)) continue;
                    if (grid[n].WaterCovered)
                    {
                        if (depth >= WorldComponent_WdBridges.MaxWaterTiles) continue;
                        if (BfsPrev.ContainsKey(n)) continue;
                        BfsPrev[n] = water;
                        BfsDepth[n] = depth + 1;
                        BfsQueue.Enqueue(n);
                        continue;
                    }

                    if (n == startBankTileId) continue;
                    if (chainsByEnd != null && chainsByEnd.ContainsKey(n)) continue;
                    if (reserved != null && reserved.Contains(n)) continue;
                    // Same shore: reachable on land in ≤ water-span hops (coastal hug).
                    if (LandDepthScratch.TryGetValue(n, out int landDist) && landDist <= depth)
                        continue;
                    if (!TryBuildChainFromPrev(startBankTileId, water, n, out List<int> chain))
                        continue;
                    if (!TryAcceptBuildChain(chain, out _))
                        continue;
                    into.Add(n);
                    chainsByEnd?.Add(n, chain);
                }
            }
        }

        /// <summary>
        /// Land-only BFS depths from <paramref name="startBankTileId"/> up to <paramref name="maxHops"/>.
        /// Uses <see cref="LandDepthScratch"/> (overwrites). Must not run during another BfsQueue walk.
        /// </summary>
        private static void FillLandDepths(int startBankTileId, int maxHops, PlanetLayer layer, WorldGrid grid)
        {
            LandDepthScratch.Clear();
            if (maxHops < 0 || startBankTileId < 0) return;
            LandBfsQueue.Clear();
            LandDepthScratch[startBankTileId] = 0;
            LandBfsQueue.Enqueue(startBankTileId);
            while (LandBfsQueue.Count > 0)
            {
                int cur = LandBfsQueue.Dequeue();
                int depth = LandDepthScratch[cur];
                if (depth >= maxHops) continue;
                BankNeighborScratch.Clear();
                grid.GetTileNeighbors(new PlanetTile(cur, layer), BankNeighborScratch);
                for (int i = 0; i < BankNeighborScratch.Count; i++)
                {
                    int n = BankNeighborScratch[i].tileId;
                    if (!grid.InBounds(n) || grid[n].WaterCovered) continue;
                    if (LandDepthScratch.ContainsKey(n)) continue;
                    LandDepthScratch[n] = depth + 1;
                    LandBfsQueue.Enqueue(n);
                }
            }
        }

        /// <summary>True when end is on the same local shore (land path ≤ water span length).</summary>
        public static bool IsSameShore(int startBankTileId, int endBankTileId, int waterCount)
        {
            if (waterCount < 1 || startBankTileId < 0 || endBankTileId < 0) return false;
            if (startBankTileId == endBankTileId) return true;
            WorldGrid grid = Find.WorldGrid;
            if (grid == null) return false;
            PlanetLayer layer = PlanetSurfaceWorldActions.WdSurfaceLayer ?? grid.Surface;
            FillLandDepths(startBankTileId, waterCount, layer, grid);
            return LandDepthScratch.TryGetValue(endBankTileId, out int landDist) && landDist <= waterCount;
        }

        private static bool TryBuildChainFromPrev(int startBank, int lastWater, int endBank, out List<int> chain)
        {
            chain = null;
            var waters = new List<int>(WorldComponent_WdBridges.MaxWaterTiles);
            int cur = lastWater;
            int guard = 0;
            while (cur != startBank)
            {
                waters.Add(cur);
                if (!BfsPrev.TryGetValue(cur, out int prev) || prev < 0)
                    return false;
                cur = prev;
                if (++guard > WorldComponent_WdBridges.MaxWaterTiles + 2)
                    return false;
            }

            waters.Reverse();
            chain = new List<int>(waters.Count + 2) { startBank };
            chain.AddRange(waters);
            chain.Add(endBank);
            return chain.Count >= 3;
        }

        /// <summary>Post-geometry gates used after a corridor is already known (no second BFS).</summary>
        public static bool TryAcceptBuildChain(List<int> fullChain, out RejectReason reason)
        {
            reason = RejectReason.None;
            if (fullChain == null || fullChain.Count < 3)
            {
                reason = RejectReason.NotStraightOrTooLong;
                return false;
            }

            int waterCount = fullChain.Count - 2;
            if (waterCount < 1 || waterCount > WorldComponent_WdBridges.MaxWaterTiles)
            {
                reason = RejectReason.NotStraightOrTooLong;
                return false;
            }

            if (SpanIsFullyBridged(fullChain))
            {
                reason = RejectReason.AlreadyBridged;
                return false;
            }

            OrientChainGrowFromPaintedBank(fullChain);
            int painted = CountPaintedWaterTiles(fullChain);
            if (painted > 0)
            {
                if (!PaintedWatersAreConnectedPrefix(fullChain))
                {
                    reason = RejectReason.BankAlreadyUsed;
                    return false;
                }
                return true;
            }

            if (BankAlreadyAnchorsBridge(fullChain[0]) || BankAlreadyAnchorsBridge(fullChain[fullChain.Count - 1]))
            {
                reason = RejectReason.BankAlreadyUsed;
                return false;
            }
            return true;
        }

        public static bool IsLandAdjacentToWater(int tileId)
        {
            WorldGrid grid = Find.WorldGrid;
            if (grid == null || tileId < 0 || !grid.InBounds(tileId)) return false;
            if (grid[tileId].WaterCovered) return false;
            PlanetLayer layer = PlanetSurfaceWorldActions.WdSurfaceLayer ?? grid.Surface;
            PlanetTile pt = new PlanetTile(tileId, layer);
            if (Find.World.Impassable(pt)) return false;
            BankNeighborScratch.Clear();
            grid.GetTileNeighbors(pt, BankNeighborScratch);
            for (int i = 0; i < BankNeighborScratch.Count; i++)
            {
                if (grid[BankNeighborScratch[i]].WaterCovered)
                    return true;
            }
            return false;
        }

        /// <summary>BFS hop distance on the surface grid (matches orange radius fills).</summary>
        public static bool IsWithinHopRange(int fromTileId, int toTileId, int maxHops)
        {
            if (fromTileId == toTileId) return true;
            if (maxHops < 0) return false;
            var set = new HashSet<int>(32);
            CollectTilesWithinHopRange(fromTileId, maxHops, set);
            return set.Contains(toTileId);
        }

        /// <summary>Land-only hop check (water not entered). Used for NPC hybrid bank nearness.</summary>
        public static bool IsWithinLandHopRange(int fromTileId, int toTileId, int maxHops)
        {
            if (fromTileId == toTileId) return true;
            if (maxHops < 0) return false;
            var set = new HashSet<int>(32);
            CollectTilesWithinHopRange(fromTileId, maxHops, set, traverseWater: false);
            return set.Contains(toTileId);
        }

        /// <summary>
        /// One BFS from <paramref name="fromTileId"/>; fills <paramref name="into"/> with all tiles
        /// within <paramref name="maxHops"/> (including the origin). Clears <paramref name="into"/> first.
        /// When <paramref name="traverseWater"/> is false, water is not entered (outpost start-bank pick).
        /// </summary>
        public static void CollectTilesWithinHopRange(
            int fromTileId, int maxHops, HashSet<int> into, bool traverseWater = true)
        {
            into?.Clear();
            if (into == null || fromTileId < 0 || maxHops < 0) return;
            WorldGrid grid = Find.WorldGrid;
            if (grid == null || !grid.InBounds(fromTileId)) return;

            PlanetLayer layer = PlanetSurfaceWorldActions.WdSurfaceLayer ?? grid.Surface;
            BfsQueue.Clear();
            BfsDepth.Clear();
            BfsQueue.Enqueue(fromTileId);
            BfsDepth[fromTileId] = 0;
            into.Add(fromTileId);
            while (BfsQueue.Count > 0)
            {
                int cur = BfsQueue.Dequeue();
                int depth = BfsDepth[cur];
                if (depth >= maxHops) continue;

                NeighborScratch.Clear();
                grid.GetTileNeighbors(new PlanetTile(cur, layer), NeighborScratch);
                for (int i = 0; i < NeighborScratch.Count; i++)
                {
                    int n = NeighborScratch[i].tileId;
                    if (BfsDepth.ContainsKey(n)) continue;
                    if (!traverseWater && grid.InBounds(n) && grid[n].WaterCovered) continue;
                    BfsDepth[n] = depth + 1;
                    into.Add(n);
                    BfsQueue.Enqueue(n);
                }
            }
        }

        /// <summary>
        /// Full chain [startBank, water..., endBank]. Water count is 1..MaxWaterTiles.
        /// Uses a shortest water-only corridor between the banks (bank-to-bank click UX).
        /// </summary>
        public static bool TryResolveSpan(int startBankTileId, int endBankTileId, out List<int> fullChain, out RejectReason reason)
        {
            if (!TryResolveSpanIgnoringBridgeState(startBankTileId, endBankTileId, out fullChain, out reason))
                return false;

            int waterCount = fullChain.Count - 2;
            if (IsSameShore(startBankTileId, endBankTileId, waterCount))
            {
                fullChain = null;
                reason = RejectReason.SameShore;
                return false;
            }

            if (SpanIsFullyBridged(fullChain))
            {
                fullChain = null;
                reason = RejectReason.AlreadyBridged;
                return false;
            }

            OrientChainGrowFromPaintedBank(fullChain);
            int painted = CountPaintedWaterTiles(fullChain);
            if (painted > 0)
            {
                if (!PaintedWatersAreConnectedPrefix(fullChain))
                {
                    fullChain = null;
                    reason = RejectReason.BankAlreadyUsed;
                    return false;
                }
                return true;
            }

            if (BankAlreadyAnchorsBridge(fullChain[0]) || BankAlreadyAnchorsBridge(fullChain[fullChain.Count - 1]))
            {
                fullChain = null;
                reason = RejectReason.BankAlreadyUsed;
                return false;
            }
            return true;
        }

        /// <summary>Same corridor resolution as build, but requires every water middle to already be bridged.</summary>
        public static bool TryResolveExistingBridgeSpan(
            int startBankTileId, int endBankTileId, out List<int> fullChain, out RejectReason reason)
        {
            fullChain = null;
            reason = RejectReason.None;
            WorldGrid grid = Find.WorldGrid;
            if (grid == null)
            {
                reason = RejectReason.InvalidTile;
                return false;
            }

            if (!TryResolveSpanIgnoringBridgeState(startBankTileId, endBankTileId, out List<int> chain, out reason))
                return false;

            if (!SpanIsFullyBridged(chain))
            {
                reason = RejectReason.NotBridged;
                return false;
            }

            fullChain = chain;
            reason = RejectReason.None;
            return true;
        }

        /// <summary>
        /// Painted corridor from one bank click (complete span or stub). Chain[0] is the clicked bank.
        /// Stubs may end on water (no far land yet).
        /// </summary>
        public static bool TryResolveExistingBridgeFromBank(
            int bankTileId, out List<int> fullChain, out RejectReason reason)
        {
            fullChain = null;
            reason = RejectReason.None;
            WorldGrid grid = Find.WorldGrid;
            if (grid == null || bankTileId < 0 || !grid.InBounds(bankTileId))
            {
                reason = RejectReason.InvalidTile;
                return false;
            }
            if (grid[bankTileId].WaterCovered)
            {
                reason = RejectReason.StartNotLand;
                return false;
            }
            if (!IsLandAdjacentToWater(bankTileId))
            {
                reason = RejectReason.StartNotAdjacentToWater;
                return false;
            }

            PlanetLayer layer = PlanetSurfaceWorldActions.WdSurfaceLayer ?? grid.Surface;
            PlanetTile bankPt = new PlanetTile(bankTileId, layer);
            if (Find.World.Impassable(bankPt))
            {
                reason = RejectReason.ImpassableBank;
                return false;
            }

            NeighborScratch.Clear();
            grid.GetTileNeighbors(bankPt, NeighborScratch);
            var seedWaters = new List<int>(4);
            for (int i = 0; i < NeighborScratch.Count; i++)
            {
                int water = NeighborScratch[i].tileId;
                if (!grid[water].WaterCovered) continue;
                if (!WorldComponent_WdBridges.IsBridgedWaterTile(water)) continue;
                if (!WorldActions_Roads.HasRoadLink(bankTileId, water)) continue;
                seedWaters.Add(water);
            }

            List<int> foundEnds = null;
            List<int> chosen = null;
            for (int i = 0; i < seedWaters.Count; i++)
            {
                if (!TryWalkPaintedFromBank(grid, layer, bankTileId, seedWaters[i], out List<int> chain))
                    continue;

                int endId = chain[chain.Count - 1];
                if (foundEnds == null)
                {
                    foundEnds = new List<int>(2) { endId };
                    chosen = chain;
                }
                else if (!foundEnds.Contains(endId))
                {
                    reason = RejectReason.AmbiguousBridge;
                    return false;
                }
            }

            if (chosen == null || chosen.Count < 2)
            {
                reason = RejectReason.NotBridged;
                return false;
            }

            fullChain = chosen;
            reason = RejectReason.None;
            return true;
        }

        /// <summary>
        /// Land tiles with a road link onto a registered bridged water tile.
        /// Does not walk the full span (safe to run over a large registry).
        /// </summary>
        public static void CollectAnchoredBanksFromBridgedWaters(HashSet<int> into)
        {
            into?.Clear();
            if (into == null) return;
            WorldGrid grid = Find.WorldGrid;
            WorldComponent_WdBridges bridges = WorldComponent_WdBridges.Get();
            if (grid == null || bridges == null || !bridges.HasAnyBridges) return;
            PlanetLayer layer = PlanetSurfaceWorldActions.WdSurfaceLayer ?? grid.Surface;
            IReadOnlyList<int> waters = bridges.BridgedWaterTiles;
            for (int i = 0; i < waters.Count; i++)
            {
                int water = waters[i];
                if (water < 0 || !grid.InBounds(water) || !grid[water].WaterCovered) continue;
                NeighborScratch.Clear();
                grid.GetTileNeighbors(new PlanetTile(water, layer), NeighborScratch);
                for (int n = 0; n < NeighborScratch.Count; n++)
                {
                    int land = NeighborScratch[n].tileId;
                    if (!grid.InBounds(land) || grid[land].WaterCovered) continue;
                    if (!WorldActions_Roads.HasRoadLink(water, land)) continue;
                    into.Add(land);
                }
            }
        }

        /// <summary>
        /// Land banks at both ends of the bridged span containing <paramref name="waterTileId"/>.
        /// </summary>
        public static bool TryGetBridgedSpanBanks(int waterTileId, out int bankA, out int bankB)
        {
            bankA = -1;
            bankB = -1;
            WorldGrid grid = Find.WorldGrid;
            if (grid == null || waterTileId < 0 || !grid.InBounds(waterTileId)) return false;
            if (!grid[waterTileId].WaterCovered) return false;
            if (!WorldComponent_WdBridges.IsBridgedWaterTile(waterTileId)) return false;

            PlanetLayer layer = PlanetSurfaceWorldActions.WdSurfaceLayer ?? grid.Surface;
            NeighborScratch.Clear();
            grid.GetTileNeighbors(new PlanetTile(waterTileId, layer), NeighborScratch);
            var neighbors = new List<int>(NeighborScratch.Count);
            for (int i = 0; i < NeighborScratch.Count; i++)
                neighbors.Add(NeighborScratch[i].tileId);

            // Prefer resolving via a land-bank walk when a road-linked bank is adjacent.
            for (int i = 0; i < neighbors.Count; i++)
            {
                int n = neighbors[i];
                if (grid[n].WaterCovered) continue;
                if (!WorldActions_Roads.HasRoadLink(waterTileId, n)) continue;
                if (!TryResolveExistingBridgeFromBank(n, out List<int> chain, out _)) continue;
                if (chain == null || chain.Count < 2) continue;
                bankA = chain[0];
                bankB = chain[chain.Count - 1];
                return bankA >= 0 && bankB >= 0;
            }

            return false;
        }

        /// <summary>Corridor geometry only (no AlreadyBridged / NotBridged gate).</summary>
        public static bool TryResolveSpanIgnoringBridgeState(
            int startBankTileId, int endBankTileId, out List<int> fullChain, out RejectReason reason)
        {
            fullChain = null;
            reason = RejectReason.None;
            WorldGrid grid = Find.WorldGrid;
            if (grid == null)
            {
                reason = RejectReason.InvalidTile;
                return false;
            }

            if (startBankTileId == endBankTileId)
            {
                reason = RejectReason.SameTile;
                return false;
            }
            if (!grid.InBounds(startBankTileId) || !grid.InBounds(endBankTileId))
            {
                reason = RejectReason.InvalidTile;
                return false;
            }
            if (grid[startBankTileId].WaterCovered)
            {
                reason = RejectReason.StartNotLand;
                return false;
            }
            if (!IsLandAdjacentToWater(startBankTileId))
            {
                reason = RejectReason.StartNotAdjacentToWater;
                return false;
            }
            if (grid[endBankTileId].WaterCovered)
            {
                reason = RejectReason.EndNotLand;
                return false;
            }
            if (!IsLandAdjacentToWater(endBankTileId))
            {
                reason = RejectReason.EndNotAdjacentToWater;
                return false;
            }

            PlanetLayer layer = PlanetSurfaceWorldActions.WdSurfaceLayer ?? grid.Surface;
            PlanetTile startPt = new PlanetTile(startBankTileId, layer);
            PlanetTile endPt = new PlanetTile(endBankTileId, layer);
            if (Find.World.Impassable(startPt) || Find.World.Impassable(endPt))
            {
                reason = RejectReason.ImpassableBank;
                return false;
            }

            if (!TryFindWaterCorridor(grid, layer, startBankTileId, endBankTileId, out List<int> chain))
            {
                reason = RejectReason.NotStraightOrTooLong;
                return false;
            }

            fullChain = chain;
            reason = RejectReason.None;
            return true;
        }

        public static bool SpanIsFullyBridged(List<int> fullChain)
        {
            if (fullChain == null || fullChain.Count < 3) return false;
            WorldGrid grid = Find.WorldGrid;
            if (grid == null) return false;
            int last = fullChain.Count - 1;
            if (grid[fullChain[last]].WaterCovered) return false;
            for (int i = 1; i < last; i++)
            {
                if (!WorldComponent_WdBridges.IsBridgedWaterTile(fullChain[i]))
                    return false;
            }
            return true;
        }

        public static void CollectWaterTiles(List<int> fullChain, List<int> into)
        {
            into.Clear();
            if (fullChain == null || fullChain.Count < 2) return;
            WorldGrid grid = Find.WorldGrid;
            for (int i = 0; i < fullChain.Count; i++)
            {
                int id = fullChain[i];
                if (grid != null && grid.InBounds(id) && grid[id].WaterCovered)
                    into.Add(id);
            }
        }

        /// <summary>Follow painted road links from a bank. Stubs may end on water.</summary>
        private static bool TryWalkPaintedFromBank(
            WorldGrid grid,
            PlanetLayer layer,
            int bankTileId,
            int entryWater,
            out List<int> fullChain)
        {
            fullChain = null;
            var waters = new List<int>(WorldComponent_WdBridges.MaxWaterTiles) { entryWater };
            int prev = bankTileId;
            int cur = entryWater;

            while (waters.Count <= WorldComponent_WdBridges.MaxWaterTiles)
            {
                int nextWater = -1;
                int farLand = -1;
                NeighborScratch.Clear();
                grid.GetTileNeighbors(new PlanetTile(cur, layer), NeighborScratch);
                for (int i = 0; i < NeighborScratch.Count; i++)
                {
                    int n = NeighborScratch[i].tileId;
                    if (n == prev) continue;
                    if (!WorldActions_Roads.HasRoadLink(cur, n)) continue;

                    if (!grid[n].WaterCovered)
                    {
                        if (n == bankTileId) continue;
                        if (Find.World.Impassable(new PlanetTile(n, layer))) return false;
                        if (farLand >= 0 && farLand != n) return false;
                        farLand = n;
                        continue;
                    }

                    if (!WorldComponent_WdBridges.IsBridgedWaterTile(n)) continue;
                    if (waters.Contains(n)) continue;
                    if (nextWater >= 0) return false;
                    nextWater = n;
                }

                if (farLand >= 0)
                {
                    fullChain = new List<int>(waters.Count + 2) { bankTileId };
                    fullChain.AddRange(waters);
                    fullChain.Add(farLand);
                    return fullChain.Count >= 2;
                }

                if (nextWater < 0)
                {
                    fullChain = new List<int>(waters.Count + 1) { bankTileId };
                    fullChain.AddRange(waters);
                    return fullChain.Count >= 2;
                }

                waters.Add(nextWater);
                prev = cur;
                cur = nextWater;
            }

            return false;
        }

        public static int CountPaintedWaterTiles(List<int> fullChain)
        {
            if (fullChain == null || fullChain.Count < 2) return 0;
            int n = 0;
            WorldGrid grid = Find.WorldGrid;
            int last = fullChain.Count - 1;
            int start = 1;
            int end = grid != null && grid.InBounds(fullChain[last]) && !grid[fullChain[last]].WaterCovered
                ? last - 1
                : last;
            for (int i = start; i <= end; i++)
            {
                if (WorldComponent_WdBridges.IsBridgedWaterTile(fullChain[i]))
                    n++;
            }
            return n;
        }

        /// <summary>Reverse the chain if the far bank already has more painted water attached.</summary>
        public static void OrientChainGrowFromPaintedBank(List<int> chain)
        {
            if (chain == null || chain.Count < 2) return;
            int forward = CountPaintedPrefix(chain);
            chain.Reverse();
            int backward = CountPaintedPrefix(chain);
            if (forward >= backward)
                chain.Reverse();
        }

        private static int CountPaintedPrefix(List<int> chain)
        {
            int n = 0;
            WorldGrid grid = Find.WorldGrid;
            int last = chain.Count - 1;
            int end = grid != null && grid.InBounds(chain[last]) && !grid[chain[last]].WaterCovered
                ? last - 1
                : last;
            for (int i = 1; i <= end; i++)
            {
                if (!WorldComponent_WdBridges.IsBridgedWaterTile(chain[i]))
                    break;
                n++;
            }
            return n;
        }

        public static bool PaintedWatersAreConnectedPrefix(List<int> chain)
        {
            if (chain == null || chain.Count < 2) return false;
            WorldGrid grid = Find.WorldGrid;
            int last = chain.Count - 1;
            int end = grid != null && grid.InBounds(chain[last]) && !grid[chain[last]].WaterCovered
                ? last - 1
                : last;
            bool seenGap = false;
            for (int i = 1; i <= end; i++)
            {
                bool painted = WorldComponent_WdBridges.IsBridgedWaterTile(chain[i]);
                if (!painted)
                    seenGap = true;
                else if (seenGap)
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Shortest path startBank -> (water only) -> endBank with 1..MaxWaterTiles water tiles.
        /// </summary>
        private static bool TryFindWaterCorridor(
            WorldGrid grid,
            PlanetLayer layer,
            int startBank,
            int endBank,
            out List<int> fullChain)
        {
            fullChain = null;
            BfsQueue.Clear();
            BfsPrev.Clear();
            BfsDepth.Clear();

            // Seed with water neighbors of the start bank (depth 1 = first water tile).
            NeighborScratch.Clear();
            grid.GetTileNeighbors(new PlanetTile(startBank, layer), NeighborScratch);
            for (int i = 0; i < NeighborScratch.Count; i++)
            {
                int w = NeighborScratch[i].tileId;
                if (!grid[w].WaterCovered) continue;
                BfsQueue.Enqueue(w);
                BfsPrev[w] = startBank;
                BfsDepth[w] = 1;
            }

            int endWater = -1;
            while (BfsQueue.Count > 0)
            {
                int cur = BfsQueue.Dequeue();
                int depth = BfsDepth[cur];

                if (grid.IsNeighbor(cur, endBank))
                {
                    endWater = cur;
                    break;
                }

                if (depth >= WorldComponent_WdBridges.MaxWaterTiles) continue;

                NeighborScratch.Clear();
                grid.GetTileNeighbors(new PlanetTile(cur, layer), NeighborScratch);
                for (int i = 0; i < NeighborScratch.Count; i++)
                {
                    int n = NeighborScratch[i].tileId;
                    if (n == startBank || n == endBank) continue;
                    if (!grid[n].WaterCovered) continue;
                    if (BfsPrev.ContainsKey(n)) continue;
                    BfsPrev[n] = cur;
                    BfsDepth[n] = depth + 1;
                    BfsQueue.Enqueue(n);
                }
            }

            if (endWater < 0) return false;

            // Reconstruct water tiles back to start bank, then append end bank.
            var waters = new List<int>(WorldComponent_WdBridges.MaxWaterTiles);
            int walk = endWater;
            while (walk != startBank)
            {
                waters.Add(walk);
                if (!BfsPrev.TryGetValue(walk, out int prev)) return false;
                walk = prev;
                if (waters.Count > WorldComponent_WdBridges.MaxWaterTiles) return false;
            }
            waters.Reverse();

            fullChain = new List<int>(waters.Count + 2) { startBank };
            fullChain.AddRange(waters);
            fullChain.Add(endBank);
            return fullChain.Count >= 3
                && (fullChain.Count - 2) >= 1
                && (fullChain.Count - 2) <= WorldComponent_WdBridges.MaxWaterTiles;
        }

        private static bool SpanAlreadyBridged(List<int> fullChain)
        {
            WorldGrid grid = Find.WorldGrid;
            if (grid == null || fullChain == null || fullChain.Count < 3) return false;
            for (int i = 1; i < fullChain.Count - 1; i++)
            {
                int id = fullChain[i];
                if (WorldComponent_WdBridges.IsBridgedWaterTile(id)) return true;
                if (grid[id] is SurfaceTile st && st.potentialRoads != null && st.potentialRoads.Count > 0)
                    return true;
            }
            return false;
        }
    }
}
