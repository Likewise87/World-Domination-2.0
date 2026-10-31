using System.Collections.Generic;
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
            float watts = Mathf.Max(0f, ext.remotePowerWatts + outpost.GetRemotePowerUpgradeBonus());
            return Mathf.Max(0f, Outpost_Production_Utils.ApplyExpertAndWarehouseYieldMultipliers(watts, outpost));
        }

        /// <summary>Watts this plant adds to the colony's power nets (zero while it supplies outposts instead).</summary>
        public static float GetColonyPowerWatts(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null) return 0f;
            // With the outpost power rule off there is no grid to supply, so every plant feeds the colony.
            if (OutpostPowerGrid.Enabled && outpost.PowerRole != PowerOutputRole.SupplyColony) return 0f;
            return GetRemotePowerWatts(outpost);
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
                    total += GetColonyPowerWatts(outpost);
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
            string line;
            if (outpost.PowerRole == PowerOutputRole.SupplyOutposts)
            {
                line = "TSA_WD_PowerPlant_InspectOutposts".Translate(
                    formatted,
                    FormatWatts(OutpostPowerGrid.GetPlantUsedWatts(outpost)),
                    OutpostPowerGrid.GetPlantServedCount(outpost).ToString());
            }
            else
            {
                line = HasPlayerColonyMap()
                    ? "TSA_WD_PowerPlant_Inspect".Translate(formatted)
                    : "TSA_WD_PowerPlant_InspectNoColony".Translate(formatted);
            }
            return line + Outpost_Production_Utils.BuildSoftProductionBonusSuffix(outpost);
        }

        public static string GetOverviewProductLine(WorldObject_WD_Outpost outpost)
        {
            return "TSA_WD_PowerPlant_OverviewProduct".Translate(FormatWatts(GetRemotePowerWatts(outpost)))
                + Outpost_Production_Utils.BuildSoftProductionBonusSuffix(outpost);
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
            OutpostPowerGrid.MarkDirty();
        }

        private static Texture2D cachedRoleIcon;

        /// <summary>Colony vs outposts switch. No production cycle on power plants, so it applies immediately.</summary>
        public static IEnumerable<Gizmo> GetGizmos(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null || outpost.Faction != Faction.OfPlayer || !outpost.IsPowerPlantOutpost) yield break;
            if (!OutpostPowerGrid.Enabled) yield break;

            PowerOutputRole current = outpost.PowerRole;
            yield return new Command_Action
            {
                defaultLabel = "TSA_WD_PowerRole_GizmoLabel".Translate(RoleLabel(current)).ToString(),
                defaultDesc = "TSA_WD_PowerRole_GizmoDesc".Translate(
                    OutpostPowerGrid.BaseDemandWatts.ToString("F0"),
                    OutpostPowerGrid.SupplyRangeTiles.ToString("F0"),
                    OutpostPowerGrid.PowerlineRangeBonusTiles.ToString("F0")).ToString(),
                icon = cachedRoleIcon ??= ContentFinder<Texture2D>.Get("WorldObjects/Outposts/WD_Outpost_Powerplant", false)
                    ?? ContentFinder<Texture2D>.Get("UI/Commands/Config", false)
                    ?? TexCommand.Replant,
                action = delegate
                {
                    var options = new List<FloatMenuOption>();
                    foreach (PowerOutputRole role in System.Enum.GetValues(typeof(PowerOutputRole)))
                    {
                        PowerOutputRole r = role;
                        string label = RoleLabel(r) + (r == outpost.PowerRole ? " " + "TSA_WD_FoodRole_CurrentTag".Translate().ToString() : "");
                        options.Add(new FloatMenuOption(label, delegate { outpost.SetPowerRole(r); }));
                    }
                    Find.WindowStack.Add(new FloatMenu(options));
                }
            };
        }

        private static string RoleLabel(PowerOutputRole role) =>
            ("TSA_WD_PowerRole_" + role).Translate().ToString();
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
