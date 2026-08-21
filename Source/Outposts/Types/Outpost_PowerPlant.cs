using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Power plant outposts provide abstract, always-on watts to the player's single colony map.</summary>
    public static class Outpost_PowerPlant
    {
        public static float GetRemotePowerWatts(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null || outpost.Destroyed || outpost.Faction != Faction.OfPlayer) return 0f;
            if (!Outpost_Production_Utils.TryGetPowerPlantExtension(outpost.def, out var ext)) return 0f;
            return Mathf.Max(0f, ext.remotePowerWatts + outpost.GetRemotePowerUpgradeBonus());
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
                    total += GetRemotePowerWatts(outpost);
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
            Map? colony = GetPlayerColonyMap();
            if (colony == null || net.Map != colony) return 0f;
            float totalWatts = GetTotalRemotePowerWatts();
            if (totalWatts <= 0f) return 0f;
            int netCount = CountPowerNets(colony);
            if (netCount <= 0) return 0f;
            return totalWatts / netCount;
        }

        public static string GetInspectProductLine(WorldObject_WD_Outpost outpost)
        {
            float watts = GetRemotePowerWatts(outpost);
            string formatted = FormatWatts(watts);
            return HasPlayerColonyMap()
                ? "TSA_WD_PowerPlant_Inspect".Translate(formatted)
                : "TSA_WD_PowerPlant_InspectNoColony".Translate(formatted);
        }

        public static string GetOverviewProductLine(WorldObject_WD_Outpost outpost)
        {
            return "TSA_WD_PowerPlant_OverviewProduct".Translate(FormatWatts(GetRemotePowerWatts(outpost)));
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
        private float cachedWattsPerNet;
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
            EnsureTotalWattsFresh();
            return cachedTotalWatts;
        }

        /// <summary>
        /// Fast path for PowerNet Harmony: when cache is fresh and total remote watts is 0, skip map/net work.
        /// When dirty, refreshes once then returns per-net watts (may still be 0).
        /// </summary>
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

            EnsureTotalWattsFresh();
            if (cachedTotalWatts <= 0f) return 0f;

            Map colony = cachedColonyMap;
            if (colony == null || !colony.IsPlayerHome)
            {
                colony = Outpost_PowerPlant.GetPlayerColonyMap();
                cachedColonyMap = colony;
                cachedNetCount = -1;
            }
            if (colony == null || net.Map != colony) return 0f;

            int netCount = Outpost_PowerPlant.CountPowerNets(colony);
            if (netCount <= 0) return 0f;

            if (netCount != cachedNetCount || !ReferenceEquals(cachedColonyMap, colony))
            {
                cachedColonyMap = colony;
                cachedNetCount = netCount;
                cachedWattsPerNet = cachedTotalWatts / netCount;
            }
            return cachedWattsPerNet;
        }

        private void EnsureTotalWattsFresh()
        {
            if (!wattsDirty) return;
            cachedTotalWatts = Outpost_PowerPlant.ComputeTotalRemotePowerWattsUncached();
            cachedColonyMap = Outpost_PowerPlant.GetPlayerColonyMap();
            cachedNetCount = -1;
            cachedWattsPerNet = 0f;
            wattsDirty = false;
            s_mayHaveRemoteWatts = cachedTotalWatts > 0f;
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
