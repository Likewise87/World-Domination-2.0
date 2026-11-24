using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Power plant outposts generate remote watts. Outpost assignments leave Keep residual for colony spots
    /// (<see cref="CompWdRemotePowerSpot"/> → <see cref="CompPowerTrader.PowerOutput"/>).
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

        /// <summary>Watts this plant contributes to colony Keep (after outpost links).</summary>
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

        public static Map GetPlayerColonyMap()
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

        public static bool HasEligibleRemotePowerSpots(Map map = null)
        {
            Map colony = map ?? GetPlayerColonyMap();
            return TryCollectEligibleSpots(colony, out _, out float sum) && sum > 0f;
        }

        internal static ThingDef ResolveRemotePowerSpotDef()
        {
            ThingDef def = WD_BuildingDefOf.TSA_WD_RemotePowerSpot;
            if (def != null) return def;
            return DefDatabase<ThingDef>.GetNamedSilentFail("TSA_WD_RemotePowerSpot");
        }

        /// <summary>
        /// Effective fraction of Keep watts this eligible spot receives (0..1), or -1 if ineligible / no spots.
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

        /// <summary>Live allocation for UI preview only. Plant CompTick must use the cached plant field.</summary>
        public static float GetSpotAllocatedWatts(CompWdRemotePowerSpot spot)
        {
            float frac = GetSpotEffectiveShareFraction(spot);
            if (frac < 0f) return 0f;
            return GetTotalRemotePowerWatts() * frac;
        }

        /// <summary>Collects wired spots with weight &gt; 0. Returns false when none.</summary>
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
            line += Outpost_Production_Utils.BuildSoftProductionBonusSuffix(outpost);

            float keep = OutpostPowerGrid.GetColonyKeepWatts(outpost);
            if (keep > 0f && HasPlayerColonyMap() && !HasEligibleRemotePowerSpots())
                line += "\n" + "TSA_WD_PowerPlant_InspectNeedsSpot".Translate(FormatWatts(keep));

            return line;
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
            product += Outpost_Production_Utils.BuildSoftProductionBonusSuffix(outpost);

            if (keep > 0f && HasPlayerColonyMap() && !HasEligibleRemotePowerSpots())
                product += "\n" + "TSA_WD_PowerPlant_InspectNeedsSpot".Translate(FormatWatts(keep));

            return product;
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
        private bool cachedUsingSpots;
        private float cachedSpotSumShares;
        private List<CompWdRemotePowerSpot> cachedEligibleSpots;

        public WorldComponent_OutpostPowerPlant(World world) : base(world) { }

        public override void FinalizeInit(bool fromLoad)
        {
            base.FinalizeInit(fromLoad);
            NotifyDirty();
        }

        public void NotifyDirty()
        {
            wattsDirty = true;
        }

        public float GetCachedTotalRemotePowerWatts()
        {
            EnsureFresh();
            return cachedTotalWatts;
        }

        public bool HasEligibleSpotsCached()
        {
            EnsureFresh();
            return cachedUsingSpots;
        }

        private void EnsureFresh()
        {
            if (!wattsDirty) return;
            Find.World?.GetComponent<WorldComponent_OutpostPowerGrid>()?.EnsureAllocationFresh();
            cachedTotalWatts = Outpost_PowerPlant.ComputeTotalRemotePowerWattsUncached();
            cachedColonyMap = Outpost_PowerPlant.GetPlayerColonyMap();
            RebuildSplitCacheAndSyncSpots();
            wattsDirty = false;
        }

        private void RebuildSplitCacheAndSyncSpots()
        {
            cachedUsingSpots = false;
            cachedSpotSumShares = 0f;
            cachedEligibleSpots = null;

            Map colony = cachedColonyMap;
            if (colony == null)
            {
                ZeroAllSpotOutputs(null);
                return;
            }

            if (cachedTotalWatts > 0f
                && Outpost_PowerPlant.TryCollectEligibleSpots(colony, out List<CompWdRemotePowerSpot> spots, out float sumShares)
                && spots != null
                && sumShares > 0f)
            {
                cachedUsingSpots = true;
                cachedEligibleSpots = spots;
                cachedSpotSumShares = sumShares;
                SyncSpotOutputs(colony, spots, sumShares, cachedTotalWatts);
                return;
            }

            ZeroAllSpotOutputs(colony);
        }

        private static void SyncSpotOutputs(Map colony, List<CompWdRemotePowerSpot> eligible, float sumShares, float totalWatts)
        {
            ThingDef spotDef = Outpost_PowerPlant.ResolveRemotePowerSpotDef();
            var all = colony?.listerThings?.ThingsOfDef(spotDef);
            if (all != null)
            {
                for (int i = 0; i < all.Count; i++)
                {
                    Thing t = all[i];
                    if (t == null || t.Destroyed) continue;
                    var comp = t.TryGetComp<CompWdRemotePowerSpot>();
                    if (comp == null) continue;
                    float watts = 0f;
                    if (comp.IsEligible && sumShares > 0f && totalWatts > 0f)
                        watts = totalWatts * (comp.SharePercent / sumShares);
                    comp.ApplyCachedAllocatedWatts(watts);
                }
                return;
            }

            for (int i = 0; i < eligible.Count; i++)
            {
                CompWdRemotePowerSpot spot = eligible[i];
                if (spot == null) continue;
                float watts = totalWatts * (spot.SharePercent / sumShares);
                spot.ApplyCachedAllocatedWatts(watts);
            }
        }

        private static void ZeroAllSpotOutputs(Map colony)
        {
            if (colony == null) return;
            ThingDef spotDef = Outpost_PowerPlant.ResolveRemotePowerSpotDef();
            var all = colony.listerThings?.ThingsOfDef(spotDef);
            if (all == null) return;
            for (int i = 0; i < all.Count; i++)
            {
                Thing t = all[i];
                if (t == null || t.Destroyed) continue;
                t.TryGetComp<CompWdRemotePowerSpot>()?.ApplyCachedAllocatedWatts(0f);
            }
        }
    }
}
