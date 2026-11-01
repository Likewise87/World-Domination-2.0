using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Reach helper for outpost power (power supply range).</summary>
    public static class OutpostPowerReach
    {
        public static float GetMaxRangeTiles()
        {
            return Mathf.Max(0f, WorldDominationMod.settings?.maxPowerSupplyRange
                ?? WorldDominationSettings.DefMaxPowerSupplyRange);
        }

        public static bool InRange(PlanetTile a, PlanetTile b)
        {
            float max = GetMaxRangeTiles();
            if (max <= 0f) return false;
            return Find.WorldGrid.ApproxDistanceInTiles(a, b) <= max;
        }

        public static bool InRange(WorldObject a, WorldObject b)
        {
            if (a == null || b == null) return false;
            return InRange(a.Tile, b.Tile);
        }
    }

    /// <summary>
    /// SSoT for outpost power: Enabled gate, demand/factor resolvers, link allocation (Keep → colony),
    /// dirty hub, one-shot letter. Production uses supplied/demand lerp; combat gates stay binary.
    /// </summary>
    public static class OutpostPowerGrid
    {
        public const float DemandEpsilon = 0.01f;
        public const float DefaultUnpoweredOutputFactor = 0.5f;

        /// <summary>Bumped on every NotifyOutpostPowerDirty so Stats fingerprints refresh.</summary>
        public static int PowerStateGeneration { get; private set; }

        public static bool Enabled
        {
            get
            {
                if (WdMedievalMode.Active) return false;
                return WorldDominationMod.settings?.enableOutpostPowerRequirement
                    ?? WorldDominationSettings.DefEnableOutpostPowerRequirement;
            }
        }

        public static float GetPowerDemandWatts(WorldObject_WD_Outpost outpost)
        {
            if (outpost?.def == null) return 0f;
            var ext = outpost.def.GetModExtension<OutpostDefExtension>();
            float demand = ext != null ? Mathf.Max(0f, ext.powerDemandWatts) : 0f;
            var levels = outpost.BuiltUpgradeLevels;
            if (levels != null)
            {
                foreach (var kv in levels)
                {
                    if (kv.Value <= 0) continue;
                    var upg = DefDatabase<OutpostUpgradeDef>.GetNamedSilentFail(kv.Key);
                    if (upg == null || upg.powerDemandWatts <= 0f) continue;
                    demand += upg.powerDemandWatts * kv.Value;
                }
            }
            return Mathf.Max(0f, demand);
        }

        public static float GetBasePowerDemandWatts(WorldObject_WD_Outpost outpost)
        {
            if (outpost?.def == null) return 0f;
            var ext = outpost.def.GetModExtension<OutpostDefExtension>();
            return ext != null ? Mathf.Max(0f, ext.powerDemandWatts) : 0f;
        }

        public static bool RequiresPower(WorldObject_WD_Outpost outpost)
        {
            if (!Enabled || outpost == null || outpost.Destroyed) return false;
            if (outpost.Faction != Faction.OfPlayer) return false;
            return GetPowerDemandWatts(outpost) > DemandEpsilon;
        }

        public static float GetUnpoweredOutputFactor(WorldObject_WD_Outpost outpost)
        {
            float f = DefaultUnpoweredOutputFactor;
            var ext = outpost?.def?.GetModExtension<OutpostDefExtension>();
            if (ext != null && ext.unpoweredOutputFactor > 0f)
                f = ext.unpoweredOutputFactor;
            return Mathf.Clamp(f, 0.01f, 1f);
        }

        public static bool IsPowered(WorldObject_WD_Outpost outpost)
        {
            if (!RequiresPower(outpost)) return true;
            float demand = GetPowerDemandWatts(outpost);
            float supplied = GetSuppliedWatts(outpost);
            return supplied + DemandEpsilon >= demand;
        }

        public static bool IsPartiallyPowered(WorldObject_WD_Outpost outpost)
        {
            if (!RequiresPower(outpost) || IsPowered(outpost)) return false;
            return GetSuppliedWatts(outpost) > DemandEpsilon;
        }

        public static float GetSuppliedWatts(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null) return 0f;
            var comp = Find.World?.GetComponent<WorldComponent_OutpostPowerGrid>();
            if (comp != null)
                return comp.GetCachedSuppliedWatts(outpost);
            return 0f;
        }

        /// <summary>Colony inject watts from this plant (Keep = budget − outpost assignments). Full gen when feature off.</summary>
        public static float GetColonyKeepWatts(WorldObject_WD_Outpost plant)
        {
            if (plant == null || !plant.IsPowerPlantOutpost) return 0f;
            if (!Enabled) return Outpost_PowerPlant.GetRemotePowerWatts(plant);
            var comp = Find.World?.GetComponent<WorldComponent_OutpostPowerGrid>();
            if (comp != null)
                return comp.GetCachedColonyKeepWatts(plant);
            return Outpost_PowerPlant.GetRemotePowerWatts(plant);
        }

        /// <summary>
        /// 1 when feature off or no demand. Otherwise lerps XML unpoweredOutputFactor → 1 by supplied/demand.
        /// Combat/radius gates stay binary via <see cref="IsPowered"/>.
        /// </summary>
        public static float GetProductionPowerFactor(WorldObject_WD_Outpost outpost)
        {
            if (!Enabled || outpost == null) return 1f;
            if (!RequiresPower(outpost)) return 1f;
            float demand = GetPowerDemandWatts(outpost);
            if (demand <= DemandEpsilon) return 1f;
            float ratio = Mathf.Clamp01(GetSuppliedWatts(outpost) / demand);
            return Mathf.Lerp(GetUnpoweredOutputFactor(outpost), 1f, ratio);
        }

        public static bool IsUnpoweredForNerfs(WorldObject_WD_Outpost outpost)
            => RequiresPower(outpost) && !IsPowered(outpost);

        public static float GetUnpoweredRangeFactorClamped()
        {
            float f = WorldDominationMod.settings?.unpoweredRangeFactor
                ?? WorldDominationSettings.DefUnpoweredRangeFactor;
            return Mathf.Clamp(f, 0.05f, 1f);
        }

        public static int ApplyUnpoweredRadius(WorldObject_WD_Outpost outpost, int rawTiles)
        {
            if (rawTiles <= 0 || !IsUnpoweredForNerfs(outpost)) return rawTiles;
            return Mathf.Max(1, Mathf.RoundToInt(rawTiles * GetUnpoweredRangeFactorClamped()));
        }

        public static float ApplyUnpoweredRange(WorldObject_WD_Outpost outpost, float rawTiles)
        {
            if (rawTiles <= 0f || !IsUnpoweredForNerfs(outpost)) return rawTiles;
            return Mathf.Max(1f, rawTiles * GetUnpoweredRangeFactorClamped());
        }

        public static bool GetColonyReceivesRemotePower()
        {
            if (!Enabled) return true;
            IReadOnlyList<WorldObject_WD_Outpost> list = WdPlayerOutpostCache.PlayerOutposts;
            for (int i = 0; i < list.Count; i++)
            {
                WorldObject_WD_Outpost op = list[i];
                if (op == null || op.Destroyed || !op.IsPowerPlantOutpost) continue;
                if (GetColonyKeepWatts(op) > DemandEpsilon)
                    return true;
            }
            return false;
        }

        public static void NotifyOutpostPowerDirty(WorldObject_WD_Outpost op = null)
        {
            PowerStateGeneration++;
            Find.World?.GetComponent<WorldComponent_OutpostPowerGrid>()?.MarkDirty();
            Outpost_PowerPlant.NotifyRemotePowerDirty();

            if (op != null && !op.Destroyed)
            {
                op.InvalidateInspectCache();
                Outpost_Trading.InvalidateTradingRadiusProbeCache(op);
            }
            else
            {
                IReadOnlyList<WorldObject_WD_Outpost> list = WdPlayerOutpostCache.PlayerOutposts;
                for (int i = 0; i < list.Count; i++)
                {
                    WorldObject_WD_Outpost o = list[i];
                    if (o == null || o.Destroyed) continue;
                    o.InvalidateInspectCache();
                }
                Outpost_Trading.ClearAllTradingRadiusProbeCaches();
            }

            Window_OutpostOverview.InvalidateCache();
            Find.World?.GetComponent<WorldComponent_LogisticsManager>()?.NotifyFoodLogisticsInputsChanged();
        }

        public static void NotifyOutpostPowerTopologyChanged()
        {
            WdPlayerOutpostCache.Invalidate();
            NotifyOutpostPowerDirty(null);
        }

        public static void TrySendUnpoweredLetterOnce()
        {
            var comp = Find.World?.GetComponent<WorldComponent_OutpostPowerGrid>();
            comp?.TrySendUnpoweredLetterOnce();
        }

        /// <summary>Inspect line when feature on and outpost has demand. Null/empty when N/A.</summary>
        public static string FormatInspectPowerLine(WorldObject_WD_Outpost outpost)
        {
            if (!Enabled || outpost == null) return null;
            float demand = GetPowerDemandWatts(outpost);
            if (demand <= DemandEpsilon) return null;
            float supplied = GetSuppliedWatts(outpost);
            bool powered = supplied + DemandEpsilon >= demand;
            string demandStr = Outpost_PowerPlant.FormatWatts(demand);
            string suppliedStr = Outpost_PowerPlant.FormatWatts(supplied);
            if (powered)
                return ("TSA_WD_OutpostPower_InspectPowered".Translate(suppliedStr, demandStr)).Colorize(Color.cyan);
            if (supplied > DemandEpsilon)
                return ("TSA_WD_OutpostPower_InspectPartial".Translate(suppliedStr, demandStr)).Colorize(Color.yellow);
            return ("TSA_WD_OutpostPower_InspectUnpowered".Translate(suppliedStr, demandStr)).Colorize(Color.yellow);
        }

        /// <summary>Overview / tooltip breakdown: base, upgrades, total, supply sources or plant outs + Keep.</summary>
        public static string BuildPowerBreakdownTooltip(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null) return "";
            var sb = new System.Text.StringBuilder();
            if (outpost.IsPowerPlantOutpost)
            {
                float gen = Outpost_PowerPlant.GetRemotePowerWatts(outpost);
                float keep = GetColonyKeepWatts(outpost);
                sb.AppendLine("TSA_WD_OutpostPower_Tip_PlantGen".Translate(Outpost_PowerPlant.FormatWatts(gen)));
                var comp = Find.World?.GetComponent<WorldComponent_OutpostPowerGrid>();
                if (comp != null)
                {
                    foreach (var line in comp.EnumerateOutgoingAssignments(outpost))
                        sb.AppendLine("TSA_WD_OutpostPower_Tip_ToOutpost".Translate(line.Label, Outpost_PowerPlant.FormatWatts(line.Watts)));
                }
                sb.Append("TSA_WD_OutpostPower_Tip_KeepColony".Translate(Outpost_PowerPlant.FormatWatts(keep)));
                return sb.ToString();
            }

            float baseDemand = GetBasePowerDemandWatts(outpost);
            float total = GetPowerDemandWatts(outpost);
            sb.AppendLine("TSA_WD_OutpostPower_Tip_BaseDemand".Translate(Outpost_PowerPlant.FormatWatts(baseDemand)));
            var levels = outpost.BuiltUpgradeLevels;
            if (levels != null)
            {
                foreach (var kv in levels)
                {
                    if (kv.Value <= 0) continue;
                    var upg = DefDatabase<OutpostUpgradeDef>.GetNamedSilentFail(kv.Key);
                    if (upg == null || upg.powerDemandWatts <= 0f) continue;
                    float w = upg.powerDemandWatts * kv.Value;
                    sb.AppendLine("TSA_WD_OutpostPower_Tip_UpgradeDemand".Translate(
                        upg.LabelCap, kv.Value.ToString(), Outpost_PowerPlant.FormatWatts(w)));
                }
            }
            sb.AppendLine("TSA_WD_OutpostPower_Tip_TotalDemand".Translate(Outpost_PowerPlant.FormatWatts(total)));
            sb.Append("TSA_WD_OutpostPower_Tip_Supplied".Translate(Outpost_PowerPlant.FormatWatts(GetSuppliedWatts(outpost))));
            return sb.ToString();
        }
    }

    public class WorldComponent_OutpostPowerGrid : WorldComponent
    {
        public class PowerLogisticsLink : IExposable
        {
            public int sourceTile;
            public int destTile;
            public float assignment;
            public void ExposeData()
            {
                Scribe_Values.Look(ref sourceTile, "sourceTile");
                Scribe_Values.Look(ref destTile, "destTile");
                Scribe_Values.Look(ref assignment, "assignment");
            }
        }

        public List<PowerLogisticsLink> powerManualLinks = new List<PowerLogisticsLink>();

        private bool dirty = true;
        private readonly Dictionary<int, float> suppliedByOutpostId = new Dictionary<int, float>(64);
        private readonly Dictionary<int, float> colonyKeepByPlantId = new Dictionary<int, float>(16);
        private bool unpoweredLetterSent;
        private bool settingsWatchInit;
        private bool watchEnable;
        private float watchRangeFactor;
        private int watchMaxPowerSupplyRange;
        private bool smartDirty = true;

        public WorldComponent_OutpostPowerGrid(World world) : base(world) { }

        public override void FinalizeInit(bool fromLoad)
        {
            base.FinalizeInit(fromLoad);
            MarkDirty();
        }

        public override void WorldComponentTick()
        {
            base.WorldComponentTick();
            int tick = Find.TickManager.TicksGame;
            if (tick % 60 == 0)
                TickPowerSettingsSnapshot();
            if (dirty || tick % 250 == 0)
            {
                EnsureAllocationFresh();
                TrySendUnpoweredLetterOnce();
            }
        }

        public void MarkDirty()
        {
            dirty = true;
            smartDirty = true;
        }

        public void NotifyManualLinksChanged()
        {
            MarkDirty();
            OutpostPowerGrid.NotifyOutpostPowerDirty(null);
        }

        public float GetCachedSuppliedWatts(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null) return 0f;
            EnsureAllocationFresh();
            return suppliedByOutpostId.TryGetValue(outpost.ID, out float w) ? w : 0f;
        }

        public float GetCachedColonyKeepWatts(WorldObject_WD_Outpost plant)
        {
            if (plant == null) return 0f;
            EnsureAllocationFresh();
            return colonyKeepByPlantId.TryGetValue(plant.ID, out float w) ? w : 0f;
        }

        public float GetOutgoingSumFromTile(int sourceTile)
        {
            float sum = 0f;
            for (int i = 0; i < powerManualLinks.Count; i++)
            {
                var l = powerManualLinks[i];
                if (l.sourceTile == sourceTile && l.assignment > OutpostPowerGrid.DemandEpsilon)
                    sum += l.assignment;
            }
            return sum;
        }

        public float GetAssignment(int srcTile, int destTile)
        {
            for (int i = 0; i < powerManualLinks.Count; i++)
            {
                var l = powerManualLinks[i];
                if (l.sourceTile == srcTile && l.destTile == destTile)
                    return l.assignment;
            }
            return 0f;
        }

        public bool SetAssignment(int srcTile, int destTile, float watts)
        {
            watts = Mathf.Max(0f, watts);
            PowerLogisticsLink found = null;
            for (int i = 0; i < powerManualLinks.Count; i++)
            {
                var l = powerManualLinks[i];
                if (l.sourceTile == srcTile && l.destTile == destTile)
                {
                    found = l;
                    break;
                }
            }
            if (watts <= OutpostPowerGrid.DemandEpsilon)
            {
                if (found != null)
                {
                    powerManualLinks.Remove(found);
                    NotifyManualLinksChanged();
                    return true;
                }
                return false;
            }
            if (found != null)
            {
                if (Mathf.Abs(found.assignment - watts) < 0.05f) return false;
                found.assignment = watts;
            }
            else
            {
                powerManualLinks.Add(new PowerLogisticsLink
                {
                    sourceTile = srcTile,
                    destTile = destTile,
                    assignment = watts
                });
            }
            NotifyManualLinksChanged();
            return true;
        }

        public void ClearOutgoingFromTile(int sourceTile)
        {
            bool removed = false;
            for (int i = powerManualLinks.Count - 1; i >= 0; i--)
            {
                if (powerManualLinks[i].sourceTile == sourceTile)
                {
                    powerManualLinks.RemoveAt(i);
                    removed = true;
                }
            }
            if (removed) NotifyManualLinksChanged();
        }

        public struct OutgoingLine
        {
            public string Label;
            public float Watts;
        }

        public IEnumerable<OutgoingLine> EnumerateOutgoingAssignments(WorldObject_WD_Outpost plant)
        {
            if (plant == null) yield break;
            EnsureAllocationFresh();
            int src = plant.Tile.tileId;
            for (int i = 0; i < powerManualLinks.Count; i++)
            {
                var l = powerManualLinks[i];
                if (l.sourceTile != src || l.assignment <= OutpostPowerGrid.DemandEpsilon) continue;
                string label = ResolveOutpostLabelAtTile(l.destTile);
                yield return new OutgoingLine { Label = label, Watts = l.assignment };
            }
        }

        public IEnumerable<(WorldObject_WD_Outpost Source, float Watts)> EnumerateIncomingAssignments(WorldObject_WD_Outpost consumer)
        {
            if (consumer == null) yield break;
            EnsureAllocationFresh();
            int dest = consumer.Tile.tileId;
            for (int i = 0; i < powerManualLinks.Count; i++)
            {
                var l = powerManualLinks[i];
                if (l.destTile != dest || l.assignment <= OutpostPowerGrid.DemandEpsilon) continue;
                WorldObject_WD_Outpost src = FindPlantAtTile(l.sourceTile);
                if (src == null) continue;
                yield return (src, l.assignment);
            }
        }

        private static WorldObject_WD_Outpost FindPlantAtTile(int tileId)
        {
            IReadOnlyList<WorldObject_WD_Outpost> list = WdPlayerOutpostCache.PlayerOutposts;
            WorldObject_WD_Outpost best = null;
            for (int i = 0; i < list.Count; i++)
            {
                WorldObject_WD_Outpost op = list[i];
                if (op == null || op.Destroyed || op.Tile.tileId != tileId) continue;
                if (!op.IsPowerPlantOutpost) continue;
                if (best == null || op.ID < best.ID) best = op;
            }
            return best;
        }

        private static string ResolveOutpostLabelAtTile(int tileId)
        {
            IReadOnlyList<WorldObject_WD_Outpost> list = WdPlayerOutpostCache.PlayerOutposts;
            for (int i = 0; i < list.Count; i++)
            {
                WorldObject_WD_Outpost op = list[i];
                if (op == null || op.Destroyed) continue;
                if (op.Tile.tileId == tileId)
                    return op.Name ?? op.LabelCap ?? "?";
            }
            return "#" + tileId;
        }

        public void EnsureAllocationFresh()
        {
            if (!dirty) return;
            dirty = false;
            suppliedByOutpostId.Clear();
            colonyKeepByPlantId.Clear();

            if (!OutpostPowerGrid.Enabled)
                return;

            if (smartDirty)
            {
                RunSmartAssignment();
                smartDirty = false;
            }

            var plants = new List<WorldObject_WD_Outpost>(8);
            IReadOnlyList<WorldObject_WD_Outpost> list = WdPlayerOutpostCache.PlayerOutposts;
            for (int i = 0; i < list.Count; i++)
            {
                WorldObject_WD_Outpost op = list[i];
                if (op == null || op.Destroyed || op.Faction != Faction.OfPlayer) continue;
                if (op.IsPowerPlantOutpost && Outpost_PowerPlant.GetRemotePowerWatts(op) > OutpostPowerGrid.DemandEpsilon)
                    plants.Add(op);
            }
            plants.Sort((a, b) => a.ID.CompareTo(b.ID));

            for (int p = 0; p < plants.Count; p++)
            {
                WorldObject_WD_Outpost plant = plants[p];
                float budget = Outpost_PowerPlant.GetRemotePowerWatts(plant);
                float left = budget;
                int srcTile = plant.Tile.tileId;

                for (int i = 0; i < powerManualLinks.Count; i++)
                {
                    var link = powerManualLinks[i];
                    if (link.sourceTile != srcTile || link.assignment <= OutpostPowerGrid.DemandEpsilon) continue;
                    WorldObject_WD_Outpost consumer = FindConsumerAtTile(link.destTile);
                    if (consumer == null || !OutpostPowerReach.InRange(plant, consumer)) continue;
                    float demand = OutpostPowerGrid.GetPowerDemandWatts(consumer);
                    float have = suppliedByOutpostId.TryGetValue(consumer.ID, out float s) ? s : 0f;
                    float need = demand - have;
                    if (need <= OutpostPowerGrid.DemandEpsilon) continue;
                    float give = Mathf.Min(need, link.assignment, left);
                    if (give <= OutpostPowerGrid.DemandEpsilon) continue;
                    suppliedByOutpostId[consumer.ID] = have + give;
                    left -= give;
                }

                colonyKeepByPlantId[plant.ID] = Mathf.Max(0f, left);
            }
        }

        private void RunSmartAssignment()
        {
            var smartPlants = new List<(WorldObject_WD_Outpost Plant, float Budget)>(8);
            IReadOnlyList<WorldObject_WD_Outpost> list = WdPlayerOutpostCache.PlayerOutposts;
            for (int i = 0; i < list.Count; i++)
            {
                WorldObject_WD_Outpost op = list[i];
                if (op == null || op.Destroyed || op.Faction != Faction.OfPlayer) continue;
                if (!op.IsPowerPlantOutpost) continue;
                if (!LogisticsModeUtil.IsSmartMode(op.PowerLogisticsMode)) continue;
                float budget = Outpost_PowerPlant.GetRemotePowerWatts(op);
                if (budget <= OutpostPowerGrid.DemandEpsilon) continue;
                smartPlants.Add((op, budget));
                ClearOutgoingFromTileSilent(op.Tile.tileId);
            }
            if (smartPlants.Count == 0) return;

            smartPlants.Sort((a, b) => a.Plant.ID.CompareTo(b.Plant.ID));

            var remaining = new Dictionary<int, float>(smartPlants.Count);
            for (int i = 0; i < smartPlants.Count; i++)
                remaining[smartPlants[i].Plant.ID] = smartPlants[i].Budget;

            var consumers = new List<WorldObject_WD_Outpost>(32);
            for (int i = 0; i < list.Count; i++)
            {
                WorldObject_WD_Outpost op = list[i];
                if (op == null || op.Destroyed || op.Faction != Faction.OfPlayer) continue;
                if (OutpostPowerGrid.GetPowerDemandWatts(op) > OutpostPowerGrid.DemandEpsilon)
                    consumers.Add(op);
            }

            // Deficit-first: fully cover hungriest reachable outpost before the next.
            while (true)
            {
                WorldObject_WD_Outpost best = null;
                float bestNeed = 0f;
                for (int c = 0; c < consumers.Count; c++)
                {
                    WorldObject_WD_Outpost consumer = consumers[c];
                    float demand = OutpostPowerGrid.GetPowerDemandWatts(consumer);
                    float have = SumAssignedToTile(consumer.Tile.tileId);
                    float need = demand - have;
                    if (need <= OutpostPowerGrid.DemandEpsilon) continue;
                    if (need > bestNeed)
                    {
                        bestNeed = need;
                        best = consumer;
                    }
                }
                if (best == null) break;

                bool anyGive = false;
                for (int p = 0; p < smartPlants.Count; p++)
                {
                    WorldObject_WD_Outpost plant = smartPlants[p].Plant;
                    if (!remaining.TryGetValue(plant.ID, out float left) || left <= OutpostPowerGrid.DemandEpsilon)
                        continue;
                    if (!OutpostPowerReach.InRange(plant, best)) continue;
                    float give = Mathf.Min(bestNeed, left);
                    if (give <= OutpostPowerGrid.DemandEpsilon) continue;
                    AddAssignmentSilent(plant.Tile.tileId, best.Tile.tileId, give);
                    remaining[plant.ID] = left - give;
                    bestNeed -= give;
                    anyGive = true;
                    if (bestNeed <= OutpostPowerGrid.DemandEpsilon) break;
                }
                if (!anyGive) break;
            }
        }

        private float SumAssignedToTile(int destTile)
        {
            float sum = 0f;
            for (int i = 0; i < powerManualLinks.Count; i++)
            {
                var l = powerManualLinks[i];
                if (l.destTile == destTile) sum += l.assignment;
            }
            return sum;
        }

        private void ClearOutgoingFromTileSilent(int sourceTile)
        {
            for (int i = powerManualLinks.Count - 1; i >= 0; i--)
            {
                if (powerManualLinks[i].sourceTile == sourceTile)
                    powerManualLinks.RemoveAt(i);
            }
        }

        private void AddAssignmentSilent(int srcTile, int destTile, float watts)
        {
            for (int i = 0; i < powerManualLinks.Count; i++)
            {
                var l = powerManualLinks[i];
                if (l.sourceTile == srcTile && l.destTile == destTile)
                {
                    l.assignment += watts;
                    return;
                }
            }
            powerManualLinks.Add(new PowerLogisticsLink
            {
                sourceTile = srcTile,
                destTile = destTile,
                assignment = watts
            });
        }

        private static WorldObject_WD_Outpost FindConsumerAtTile(int tileId)
        {
            IReadOnlyList<WorldObject_WD_Outpost> list = WdPlayerOutpostCache.PlayerOutposts;
            WorldObject_WD_Outpost best = null;
            for (int i = 0; i < list.Count; i++)
            {
                WorldObject_WD_Outpost op = list[i];
                if (op == null || op.Destroyed || op.Tile.tileId != tileId) continue;
                if (OutpostPowerGrid.GetPowerDemandWatts(op) <= OutpostPowerGrid.DemandEpsilon) continue;
                if (best == null || op.ID < best.ID) best = op;
            }
            return best;
        }

        public void TrySendUnpoweredLetterOnce()
        {
            if (unpoweredLetterSent) return;
            if (!OutpostPowerGrid.Enabled) return;
            if (Current.ProgramState != ProgramState.Playing) return;

            EnsureAllocationFresh();
            IReadOnlyList<WorldObject_WD_Outpost> list = WdPlayerOutpostCache.PlayerOutposts;
            WorldObject_WD_Outpost first = null;
            for (int i = 0; i < list.Count; i++)
            {
                WorldObject_WD_Outpost op = list[i];
                if (op == null || op.Destroyed) continue;
                if (!OutpostPowerGrid.RequiresPower(op)) continue;
                if (OutpostPowerGrid.IsPowered(op)) continue;
                first = op;
                break;
            }
            if (first == null) return;

            unpoweredLetterSent = true;
            Find.LetterStack.ReceiveLetter(
                "TSA_WD_Letter_OutpostPowerUnpoweredLabel".Translate(),
                "TSA_WD_Letter_OutpostPowerUnpoweredText".Translate(),
                LetterDefOf.NeutralEvent,
                first);
        }

        private void TickPowerSettingsSnapshot()
        {
            var s = WorldDominationMod.settings;
            if (s == null) return;
            if (!settingsWatchInit)
            {
                settingsWatchInit = true;
                watchEnable = s.enableOutpostPowerRequirement;
                watchRangeFactor = s.unpoweredRangeFactor;
                watchMaxPowerSupplyRange = s.maxPowerSupplyRange;
                return;
            }

            if (s.enableOutpostPowerRequirement != watchEnable
                || Mathf.Abs(s.unpoweredRangeFactor - watchRangeFactor) > 0.0001f
                || s.maxPowerSupplyRange != watchMaxPowerSupplyRange)
            {
                watchEnable = s.enableOutpostPowerRequirement;
                watchRangeFactor = s.unpoweredRangeFactor;
                watchMaxPowerSupplyRange = s.maxPowerSupplyRange;
                OutpostPowerGrid.NotifyOutpostPowerDirty(null);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref unpoweredLetterSent, "wdOutpostPowerUnpoweredLetterSent", false);
            Scribe_Collections.Look(ref powerManualLinks, "powerManualLinks", LookMode.Deep);
            if (powerManualLinks == null) powerManualLinks = new List<PowerLogisticsLink>();
        }
    }

    /// <summary>
    /// 1.7 load guard: catches silent Always-Sequence / missing powerDemandWatts patches
    /// (see WD_OutpostPower_Tags.xml). Skips defs that are not loaded (MayRequire).
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class WD_OutpostPowerDemandAssert
    {
        static WD_OutpostPowerDemandAssert()
        {
            // Frozen 1.6 DLL has no powerDemandWatts field / no 1.7 patches — skip.
            // Heuristic: remotePowerWatts exists on both; powerDemandWatts only on 1.7.
            // If Farming extension reads 0 after patches, log — that is the failure mode we care about.
            AssertDemand("TSA_WD_Outpost_Farming", 250f);
            AssertDemand("TSA_WD_Outpost_Mining", 400f);
            AssertDemand("TSA_WD_Outpost_BiofuelRefinery", 500f);
            AssertDemand("TSA_WD_Outpost_Research", 600f);
        }

        private static void AssertDemand(string defName, float expectedWatts)
        {
            var def = DefDatabase<WorldObjectDef>.GetNamedSilentFail(defName);
            if (def == null) return;
            var ext = def.GetModExtension<OutpostDefExtension>();
            if (ext == null)
            {
                Log.Error("[WD] Outpost power demand assert: " + defName + " has no OutpostDefExtension.");
                return;
            }
            if (Mathf.Abs(ext.powerDemandWatts - expectedWatts) > 0.5f)
            {
                Log.Error("[WD] Outpost power demand assert: " + defName
                    + " powerDemandWatts=" + ext.powerDemandWatts
                    + " expected " + expectedWatts
                    + " (1.7/Patches/WD_OutpostPower_Tags.xml missing or Always-Sequence aborted).");
            }
        }
    }
}
