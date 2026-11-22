using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Power plant outposts generate remote watts. Outpost assignments leave Keep residual for colony Harmony inject.
    /// </summary>
    public static class Outpost_PowerPlant
    {
        public static float GetRemotePowerWatts(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null || outpost.Destroyed || outpost.Faction != Faction.OfPlayer) return 0f;
            if (!Outpost_Production_Utils.TryGetPowerPlantExtension(outpost.def, out var ext)) return 0f;
            float watts = Mathf.Max(0f, ext.remotePowerWatts + outpost.GetRemotePowerUpgradeBonus());
            // Warehouse aura range must not depend on power here: power depends on this budget (see ComputingPlantBudget).
            OutpostPowerGrid.BeginPlantBudget();
            try
            {
                return Mathf.Max(0f, Outpost_Production_Utils.ApplyExpertAndWarehouseYieldMultipliers(watts, outpost));
            }
            finally
            {
                OutpostPowerGrid.EndPlantBudget();
            }
        }

        /// <summary>Watts this plant contributes to colony PowerNets (Keep residual after outpost links).</summary>
        public static float GetColonyFacingRemotePowerWatts(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null || !outpost.IsPowerPlantOutpost) return 0f;
            return OutpostPowerGrid.GetColonyKeepWatts(outpost);
        }

        public static float GetTotalRemotePowerWatts()
        {
            var comp = Find.World?.GetComponent<WorldComponent_OutpostPowerPlant>();
            if (comp != null)
                return comp.GetCachedTotalRemotePowerWatts();
            return ComputeTotalRemotePowerWattsUncached();
        }

        internal static float ComputeTotalRemotePowerWattsUncached()
        {
            if (Find.WorldObjects == null) return 0f;
            float total = 0f;
            var all = Find.WorldObjects.AllWorldObjects;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] is WorldObject_WD_Outpost outpost)
                    total += GetColonyFacingRemotePowerWatts(outpost);
            }
            return total;
        }

        public static Map? GetPlayerColonyMap()
        {
            if (Current.Game?.Maps == null) return null;
            var maps = Current.Game.Maps;
            for (int i = 0; i < maps.Count; i++)
            {
                Map map = maps[i];
                if (map != null && map.IsPlayerHome && map.ParentFaction == Faction.OfPlayer
                    && !Outpost_EstablishmentRequirements.IsActiveCamp(map.Parent))
                    return map;
            }
            return null;
        }

        public static bool HasPlayerColonyMap() => GetPlayerColonyMap() != null;

        public static float GetRemotePowerWattsForPowerNet(PowerNet net)
        {
            var comp = Find.World?.GetComponent<WorldComponent_OutpostPowerPlant>();
            if (comp != null)
                return comp.GetCachedWattsPerPowerNet(net);
            return GetRemotePowerWattsForPowerNetUncached(net);
        }

        internal static float GetRemotePowerWattsForPowerNetUncached(PowerNet net)
        {
            if (net?.Map == null) return 0f;
            if (!OutpostPowerGrid.GetColonyReceivesRemotePower() && OutpostPowerGrid.Enabled)
                return 0f;
            Map? colony = GetPlayerColonyMap();
            if (colony == null || net.Map != colony) return 0f;
            float totalWatts = GetTotalRemotePowerWatts();
            if (totalWatts <= 0f) return 0f;

            if (TryCollectEligibleSpots(colony, out List<CompWdRemotePowerSpot> spots, out float sumShares)
                && spots != null
                && sumShares > 0f)
                return SumWattsForPowerNet(net, totalWatts, spots, sumShares);

            int netCount = CountPowerNets(colony);
            if (netCount <= 0) return 0f;
            return totalWatts / netCount;
        }

        internal static ThingDef ResolveRemotePowerSpotDef()
        {
            ThingDef def = WD_BuildingDefOf.TSA_WD_RemotePowerSpot;
            if (def != null) return def;
            return DefDatabase<ThingDef>.GetNamedSilentFail("TSA_WD_RemotePowerSpot");
        }

        /// <summary>
        /// Effective fraction of Keep watts this eligible spot receives (0..1), or -1 if ineligible / no spot mode.
        /// </summary>
        public static float GetSpotEffectiveShareFraction(CompWdRemotePowerSpot spot)
        {
            if (spot == null || !spot.IsEligible) return -1f;
            Map map = spot.parent?.Map;
            if (map == null) return -1f;
            float sum = SumEligibleShareWeights(map);
            if (sum <= 0f) return -1f;
            return spot.SharePercent / sum;
        }

        public static float GetSpotAllocatedWatts(CompWdRemotePowerSpot spot)
        {
            float frac = GetSpotEffectiveShareFraction(spot);
            if (frac < 0f) return 0f;
            return GetTotalRemotePowerWatts() * frac;
        }

        /// <summary>
        /// Collects wired spots with weight &gt; 0. Returns false when none (equal-net fallback).
        /// </summary>
        internal static bool TryCollectEligibleSpots(
            Map colony,
            out List<CompWdRemotePowerSpot> spots,
            out float sumShares)
        {
            spots = null;
            sumShares = 0f;
            if (colony == null) return false;
            ThingDef spotDef = ResolveRemotePowerSpotDef();
            if (spotDef == null) return false;
            var list = colony.listerThings?.ThingsOfDef(spotDef);
            if (list == null || list.Count == 0) return false;

            var eligible = new List<CompWdRemotePowerSpot>(list.Count);
            for (int i = 0; i < list.Count; i++)
            {
                Thing t = list[i];
                if (t == null || t.Destroyed || !t.Spawned) continue;
                var comp = t.TryGetComp<CompWdRemotePowerSpot>();
                if (comp == null || !comp.IsEligible) continue;
                eligible.Add(comp);
                sumShares += comp.SharePercent;
            }
            if (eligible.Count == 0 || sumShares <= 0f) return false;
            spots = eligible;
            return true;
        }

        /// <summary>
        /// Live PowerNet match (do not key a cache by PowerNet identity; nets rebuild often).
        /// </summary>
        internal static float SumWattsForPowerNet(
            PowerNet net,
            float totalWatts,
            List<CompWdRemotePowerSpot> spots,
            float sumShares)
        {
            if (net == null || spots == null || sumShares <= 0f || totalWatts <= 0f) return 0f;
            float watts = 0f;
            for (int i = 0; i < spots.Count; i++)
            {
                CompWdRemotePowerSpot spot = spots[i];
                if (spot == null || !spot.IsEligible) continue;
                if (!ReferenceEquals(spot.PowerNet, net)) continue;
                watts += totalWatts * (spot.SharePercent / sumShares);
            }
            return watts;
        }

        internal static float SumEligibleShareWeights(Map map)
        {
            if (!TryCollectEligibleSpots(map, out List<CompWdRemotePowerSpot> spots, out float sum) || spots == null)
                return 0f;
            return sum;
        }

        public static string GetInspectProductLine(WorldObject_WD_Outpost outpost)
        {
            if (!OutpostPowerGrid.Enabled) return null;
            float watts = GetRemotePowerWatts(outpost);
            string formatted = FormatWatts(watts);
            string line;
            if (!HasPlayerColonyMap())
                line = "TSA_WD_PowerPlant_InspectNoColony".Translate(formatted);
            else
            {
                line = "TSA_WD_PowerPlant_InspectGenerated".Translate(formatted);
                if (line.Contains("TSA_WD_"))
                    line = formatted + " generated";
            }
            return line + Outpost_Production_Utils.BuildSoftProductionBonusSuffix(outpost);
        }

        public static string GetOverviewProductLine(WorldObject_WD_Outpost outpost)
        {
            if (!OutpostPowerGrid.Enabled) return null;
            float watts = GetRemotePowerWatts(outpost);
            float keep = OutpostPowerGrid.GetColonyKeepWatts(outpost);
            string product = "TSA_WD_PowerPlant_OverviewProductSplit".Translate(
                FormatWatts(watts),
                FormatWatts(Mathf.Max(0f, watts - keep)),
                FormatWatts(keep));
            if (product.Contains("TSA_WD_"))
                product = FormatWatts(watts) + " (" + FormatWatts(watts - keep) + " outposts / " + FormatWatts(keep) + " colony)";
            return product + Outpost_Production_Utils.BuildSoftProductionBonusSuffix(outpost);
        }

        public static IEnumerable<Gizmo> GetGizmos(WorldObject_WD_Outpost outpost)
        {
            // XOR colony/outposts gizmo removed; allocation is on the Logistics Electricity tab.
            yield break;
        }

        public static string GetOverviewTimeLine()
        {
            return "TSA_WD_PowerPlant_OverviewAlways".Translate();
        }

        public static string FormatWatts(float watts)
        {
            if (Mathf.Abs(watts) >= 10000f)
                return (watts / 1000f).ToString("F1") + " kW";
            return watts.ToString("F0") + " W";
        }

        internal static int CountPowerNets(Map map)
        {
            var nets = map?.powerNetManager?.AllNetsListForReading;
            return nets?.Count ?? 0;
        }

        public static void NotifyRemotePowerDirty()
        {
            Find.World?.GetComponent<WorldComponent_OutpostPowerPlant>()?.NotifyDirty();
        }
    }

    public class WorldComponent_OutpostPowerPlant : WorldComponent
    {
        private bool wattsDirty = true;
        private float cachedTotalWatts;
        private Map cachedColonyMap;
        private int cachedNetCount = -1;
        private float cachedEqualWattsPerNet;
        private bool cachedUsingSpots;
        private float cachedSpotSumShares;
        private List<CompWdRemotePowerSpot> cachedEligibleSpots;
        /// <summary>True when dirty or last refresh had watts &gt; 0. Harmony skips Find.World when false.</summary>
        private static bool s_mayHaveRemoteWatts = true;

        public WorldComponent_OutpostPowerPlant(World world) : base(world) { }

        public static bool MayHaveRemoteWatts => s_mayHaveRemoteWatts;

        public override void FinalizeInit(bool fromLoad)
        {
            base.FinalizeInit(fromLoad);
            NotifyDirty();
        }

        public void NotifyDirty()
        {
            wattsDirty = true;
            s_mayHaveRemoteWatts = true;
        }

        public float GetCachedTotalRemotePowerWatts()
        {
            EnsureFresh();
            return cachedTotalWatts;
        }

        public bool TryGetRemoteWattsOrZero(PowerNet net, out float watts)
        {
            watts = 0f;
            if (net?.Map == null) return false;

            if (!wattsDirty && cachedTotalWatts <= 0f)
                return false;

            watts = GetCachedWattsPerPowerNet(net);
            return watts > 0f;
        }

        public float GetCachedWattsPerPowerNet(PowerNet net)
        {
            if (net?.Map == null) return 0f;

            EnsureFresh();
            if (cachedTotalWatts <= 0f) return 0f;

            Map colony = cachedColonyMap;
            if (colony == null || !colony.IsPlayerHome)
            {
                colony = Outpost_PowerPlant.GetPlayerColonyMap();
                if (!ReferenceEquals(colony, cachedColonyMap))
                {
                    cachedColonyMap = colony;
                    RebuildSplitCache();
                }
            }
            if (colony == null || net.Map != colony) return 0f;

            if (cachedUsingSpots)
            {
                // Match live PowerNet refs on spots (nets rebuild; do not Dictionary-key by PowerNet).
                return Outpost_PowerPlant.SumWattsForPowerNet(
                    net, cachedTotalWatts, cachedEligibleSpots, cachedSpotSumShares);
            }

            int netCount = Outpost_PowerPlant.CountPowerNets(colony);
            if (netCount <= 0) return 0f;
            if (netCount != cachedNetCount || !ReferenceEquals(cachedColonyMap, colony))
            {
                cachedColonyMap = colony;
                cachedNetCount = netCount;
                cachedEqualWattsPerNet = cachedTotalWatts / netCount;
            }
            return cachedEqualWattsPerNet;
        }

        private void EnsureFresh()
        {
            if (!wattsDirty) return;
            // Refresh outpost allocation first so Keep watts are current.
            Find.World?.GetComponent<WorldComponent_OutpostPowerGrid>()?.EnsureAllocationFresh();
            cachedTotalWatts = Outpost_PowerPlant.ComputeTotalRemotePowerWattsUncached();
            cachedColonyMap = Outpost_PowerPlant.GetPlayerColonyMap();
            RebuildSplitCache();
            wattsDirty = false;
            s_mayHaveRemoteWatts = cachedTotalWatts > 0f;
        }

        private void RebuildSplitCache()
        {
            cachedNetCount = -1;
            cachedEqualWattsPerNet = 0f;
            cachedUsingSpots = false;
            cachedSpotSumShares = 0f;
            cachedEligibleSpots = null;
            if (cachedTotalWatts <= 0f || cachedColonyMap == null) return;

            if (Outpost_PowerPlant.TryCollectEligibleSpots(cachedColonyMap, out List<CompWdRemotePowerSpot> spots, out float sumShares)
                && spots != null
                && sumShares > 0f)
            {
                cachedUsingSpots = true;
                cachedEligibleSpots = spots;
                cachedSpotSumShares = sumShares;
                return;
            }

            int netCount = Outpost_PowerPlant.CountPowerNets(cachedColonyMap);
            cachedNetCount = netCount;
            if (netCount > 0)
                cachedEqualWattsPerNet = cachedTotalWatts / netCount;
        }
    }

    [HarmonyPatch(typeof(PowerNet), nameof(PowerNet.CurrentEnergyGainRate))]
    public static class Patch_PowerNet_CurrentEnergyGainRate_RemoteOutpostPower
    {
        [HarmonyPostfix]
        public static void Postfix(PowerNet __instance, ref float __result)
        {
            if (!WorldComponent_OutpostPowerPlant.MayHaveRemoteWatts) return;
            var comp = Find.World?.GetComponent<WorldComponent_OutpostPowerPlant>();
            if (comp == null || !comp.TryGetRemoteWattsOrZero(__instance, out float watts))
                return;
            __result += watts * CompPower.WattsToWattDaysPerTick;
        }
    }

    [HarmonyPatch(typeof(PowerNet), "get_HasActivePowerSource")]
    public static class Patch_PowerNet_HasActivePowerSource_RemoteOutpostPower
    {
        [HarmonyPostfix]
        public static void Postfix(PowerNet __instance, ref bool __result)
        {
            if (__result || !WorldComponent_OutpostPowerPlant.MayHaveRemoteWatts) return;
            var comp = Find.World?.GetComponent<WorldComponent_OutpostPowerPlant>();
            if (comp != null && comp.TryGetRemoteWattsOrZero(__instance, out _))
                __result = true;
        }
    }
}
