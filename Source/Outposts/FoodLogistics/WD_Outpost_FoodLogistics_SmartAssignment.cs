using System;
using System.Collections.Generic;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Smart food logistics (multi-hub aware):
    /// (1) Clear all Smart hubs' outgoing, then cover every reachable deficit using <b>cumulative</b> surplus
    ///     from any in-range Smart hub (hungriest first; richest capable hub first).
    /// (2) Per hub, reserve Keep = remaining/(reachable+1).
    /// (3) Water-fill remaining into reachable receivers so projected nets equalize.
    /// </summary>
    public static class LogisticsSmartAssignment
    {
        private const float DeficitNetThreshold = -0.05f;
        private const float WaterFillEps = 0.001f;

        private struct HubState
        {
            public WorldObject Obj;
            public CompOutpostLogistics Logi;
            public int Tile;
            public float BudgetLeft;
            public List<int> ReachableReceiverIndices;
        }

        private static readonly List<HubState> hubsScratch = new List<HubState>(8);
        private static readonly List<WorldObject> receiversScratch = new List<WorldObject>(16);
        private static readonly List<float> projectedNetScratch = new List<float>(16);
        /// <summary>assignAmount[hubIndex][receiverIndex]</summary>
        private static readonly List<List<float>> assignMatrix = new List<List<float>>(8);
        private static readonly List<int> deficitOrderScratch = new List<int>(16);
        private static readonly HashSet<int> receiverTileSeen = new HashSet<int>();

        /// <summary>Legacy single-hub entry; routes through the global multi-hub pass.</summary>
        public static void ExecuteSmartLogic(WorldComponent_LogisticsManager manager, WorldObject hub, CompOutpostLogistics logi)
        {
            if (hub == null || logi == null || !LogisticsModeUtil.IsSmartMode(logi.mode)) return;
            var list = new List<(WorldObject Obj, CompOutpostLogistics Logi)> { (hub, logi) };
            ExecuteAllSmartHubs(manager, list);
        }

        public static void ExecuteAllSmartHubs(
            WorldComponent_LogisticsManager manager,
            List<(WorldObject Obj, CompOutpostLogistics Logi)> smartHubs)
        {
            if (manager == null || smartHubs == null || smartHubs.Count == 0) return;
            var s = WorldDominationMod.settings;
            if (s == null) return;

            hubsScratch.Clear();
            for (int i = 0; i < smartHubs.Count; i++)
            {
                var pair = smartHubs[i];
                if (pair.Obj == null || pair.Logi == null) continue;
                if (!LogisticsModeUtil.IsSmartMode(pair.Logi.mode)) continue;
                if (!Outpost_Production_Utils.IsFoodProducerOutpost(pair.Obj.def)) continue;
                float budget = Mathf.Max(0f, manager.GetDailyProduction(pair.Obj) - manager.GetDailyDemand(pair.Obj));
                hubsScratch.Add(new HubState
                {
                    Obj = pair.Obj,
                    Logi = pair.Logi,
                    Tile = pair.Obj.Tile,
                    BudgetLeft = budget,
                    ReachableReceiverIndices = new List<int>(8)
                });
            }
            if (hubsScratch.Count == 0) return;

            // Clear all Smart outgoing so baselines exclude prior Smart shipments (Manual links stay).
            bool anyCleared = false;
            var links = manager.manualLinks;
            for (int h = 0; h < hubsScratch.Count; h++)
            {
                int hubTile = hubsScratch[h].Tile;
                for (int i = 0; i < links.Count; i++)
                {
                    if (links[i].sourceTile != hubTile) continue;
                    if (links[i].manualAssignment > 0.001f)
                        anyCleared = true;
                    links[i].manualAssignment = 0f;
                }
            }
            if (anyCleared)
                manager.NotifyManualLinksChanged();
            manager.InvalidateLogisticsNumericCaches();

            // Union of receivers reachable by at least one Smart hub.
            receiversScratch.Clear();
            receiverTileSeen.Clear();
            float maxR = s.maxLogisticsRange;
            for (int h = 0; h < hubsScratch.Count; h++)
            {
                var hub = hubsScratch[h];
                var nodes = manager.GetCachedPlayerLogisticsNodes();
                for (int n = 0; n < nodes.Count; n++)
                {
                    var wo = nodes[n].Obj;
                    if (wo == null || wo == hub.Obj) continue;
                    if (Outpost_Production_Utils.IsFoodProducerOutpost(wo.def)) continue;
                    if (Find.WorldGrid.ApproxDistanceInTiles(hub.Tile, wo.Tile) > maxR) continue;
                    if (receiverTileSeen.Add(wo.Tile))
                        receiversScratch.Add(wo);
                }
            }
            if (receiversScratch.Count == 0)
                return;

            // Map hub → reachable receiver indices; build assign matrix.
            assignMatrix.Clear();
            for (int h = 0; h < hubsScratch.Count; h++)
            {
                var hub = hubsScratch[h];
                hub.ReachableReceiverIndices.Clear();
                var row = new List<float>(receiversScratch.Count);
                for (int r = 0; r < receiversScratch.Count; r++)
                {
                    row.Add(0f);
                    float dist = Find.WorldGrid.ApproxDistanceInTiles(hub.Tile, receiversScratch[r].Tile);
                    if (dist <= maxR)
                        hub.ReachableReceiverIndices.Add(r);
                }
                hubsScratch[h] = hub;
                assignMatrix.Add(row);
            }

            projectedNetScratch.Clear();
            for (int r = 0; r < receiversScratch.Count; r++)
                projectedNetScratch.Add(manager.GetLogisticsNetDailyForOutpost(receiversScratch[r]));

            // Phase 1: cover deficits with cumulative Smart surplus (hungriest first).
            deficitOrderScratch.Clear();
            for (int r = 0; r < receiversScratch.Count; r++)
            {
                if (projectedNetScratch[r] < DeficitNetThreshold)
                    deficitOrderScratch.Add(r);
            }
            deficitOrderScratch.Sort((a, b) => projectedNetScratch[a].CompareTo(projectedNetScratch[b]));

            for (int di = 0; di < deficitOrderScratch.Count; di++)
            {
                int rIdx = deficitOrderScratch[di];
                while (projectedNetScratch[rIdx] < DeficitNetThreshold)
                {
                    float need = Mathf.Abs(projectedNetScratch[rIdx]) + 0.05f;
                    int hubIdx = FindBestHubForReceiver(rIdx);
                    if (hubIdx < 0) break;
                    var hub = hubsScratch[hubIdx];
                    float give = Mathf.Min(need, hub.BudgetLeft);
                    if (give <= WaterFillEps) break;
                    assignMatrix[hubIdx][rIdx] += give;
                    projectedNetScratch[rIdx] += give;
                    hub.BudgetLeft -= give;
                    hubsScratch[hubIdx] = hub;
                }
            }

            // Phase 2: per hub Keep share, then water-fill into that hub's reachable receivers.
            for (int h = 0; h < hubsScratch.Count; h++)
            {
                var hub = hubsScratch[h];
                if (hub.BudgetLeft <= WaterFillEps) continue;
                int reach = hub.ReachableReceiverIndices.Count;
                if (reach == 0) continue;

                float keepShare = hub.BudgetLeft / (reach + 1);
                float waterBudget = hub.BudgetLeft - keepShare;
                hub.BudgetLeft = keepShare; // retained as Keep (unassigned)
                hubsScratch[h] = hub;
                WaterFillFromHub(h, waterBudget);
            }

            // Write rounded assignments.
            for (int h = 0; h < hubsScratch.Count; h++)
            {
                int hubTile = hubsScratch[h].Tile;
                float budgetCap = Mathf.Max(0f, manager.GetDailyProduction(hubsScratch[h].Obj) - manager.GetDailyDemand(hubsScratch[h].Obj));
                float assignedSum = 0f;
                for (int r = 0; r < receiversScratch.Count; r++)
                {
                    float amt = (float)Math.Round(Mathf.Max(0f, assignMatrix[h][r]), 1);
                    if (amt <= 0.01f) continue;
                    if (assignedSum + amt > budgetCap + 0.001f)
                        amt = (float)Math.Round(Mathf.Max(0f, budgetCap - assignedSum), 1);
                    if (amt <= 0.01f) continue;
                    manager.SetAssignment(hubTile, receiversScratch[r].Tile, amt);
                    assignedSum += amt;
                }
            }
        }

        /// <summary>Hub with most remaining budget that can reach this receiver.</summary>
        private static int FindBestHubForReceiver(int receiverIndex)
        {
            int best = -1;
            float bestBudget = WaterFillEps;
            for (int h = 0; h < hubsScratch.Count; h++)
            {
                var hub = hubsScratch[h];
                if (hub.BudgetLeft <= WaterFillEps) continue;
                if (!hub.ReachableReceiverIndices.Contains(receiverIndex)) continue;
                if (hub.BudgetLeft > bestBudget)
                {
                    bestBudget = hub.BudgetLeft;
                    best = h;
                }
            }
            return best;
        }

        private static void WaterFillFromHub(int hubIndex, float waterBudget)
        {
            float remaining = waterBudget;
            var reach = hubsScratch[hubIndex].ReachableReceiverIndices;
            int n = reach.Count;
            if (n == 0 || remaining <= WaterFillEps) return;

            for (int guard = 0; guard < n + 2 && remaining > WaterFillEps; guard++)
            {
                float minNet = float.MaxValue;
                for (int i = 0; i < n; i++)
                {
                    float net = projectedNetScratch[reach[i]];
                    if (net < minNet) minNet = net;
                }

                float nextHigher = float.MaxValue;
                int atMin = 0;
                for (int i = 0; i < n; i++)
                {
                    float net = projectedNetScratch[reach[i]];
                    if (net <= minNet + WaterFillEps) atMin++;
                    else if (net < nextHigher) nextHigher = net;
                }
                if (atMin <= 0) break;

                float gap = nextHigher < 1e20f ? nextHigher - minNet : remaining / atMin;
                if (gap < WaterFillEps) gap = remaining / atMin;

                float need = gap * atMin;
                float giveEach;
                if (need <= remaining + WaterFillEps)
                {
                    giveEach = gap;
                    remaining -= need;
                }
                else
                {
                    giveEach = remaining / atMin;
                    remaining = 0f;
                }
                if (giveEach <= WaterFillEps) break;

                for (int i = 0; i < n; i++)
                {
                    int rIdx = reach[i];
                    if (projectedNetScratch[rIdx] > minNet + WaterFillEps) continue;
                    assignMatrix[hubIndex][rIdx] += giveEach;
                    projectedNetScratch[rIdx] += giveEach;
                }
            }

            // Unused water returns to Keep on this hub.
            if (remaining > WaterFillEps)
            {
                var hub = hubsScratch[hubIndex];
                hub.BudgetLeft += remaining;
                hubsScratch[hubIndex] = hub;
            }
        }
    }
}
