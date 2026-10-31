using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Left-column summaries for the Supply tab's Tooling and Power pages.</summary>
    public static class OutpostSupplySections
    {
        public static OutpostStatsSection BuildToolingSection(WorldObject_WD_Outpost outpost)
        {
            var section = new OutpostStatsSection { Title = "TSA_Supply_SectionTooling".Translate().ToString() };
            if (outpost == null) return section;

            if (OutpostTooling.CanGenerate(outpost.def))
                AddRole(section, ("TSA_WD_ToolingRole_" + outpost.GetToolingRoleForCurrentCycle()).Translate().ToString());

            if (OutpostTooling.IsGenerator(outpost))
            {
                float output = OutpostTooling.GetGeneratorOutput(outpost);
                float used = OutpostTooling.GetGeneratorUsed(outpost);
                AddRow(section, "TSA_Supply_Row_Output", PerDay(output), "TSA_Supply_Row_OutputTip");
                AddRow(section, "TSA_Supply_Row_Sent", PerDay(used), null);
                AddRow(section, "TSA_Supply_Row_Unassigned", PerDay(Mathf.Max(0f, output - used)), "TSA_Supply_UnassignedTip");
                AddRow(section, "TSA_Supply_Row_Served", OutpostTooling.GetGeneratorServedCount(outpost).ToString(), null);
                return section;
            }

            if (!OutpostTooling.RequiresTooling(outpost)) return section;

            OutpostTooling.TryGetStatus(outpost, out OutpostToolingStatus status);
            float net = status.supplied - status.demand;
            AddRow(section, "TSA_Supply_Row_Stock",
                outpost.currentTooling.ToString("F1") + " / " + OutpostTooling.MaxPerOutpost.ToString("F0"), "TSA_Supply_Row_StockTip");
            AddRow(section, "TSA_Supply_Row_Need", PerDay(status.demand), null);
            AddRow(section, "TSA_Supply_Row_Arriving", PerDay(status.supplied), "TSA_Supply_Row_ArrivingTip");
            AddRow(section, "TSA_Supply_Row_Net", PerDay(net), null, NetColor(net));
            if (OutpostTooling.Enabled)
            {
                bool tooled = OutpostTooling.IsTooled(outpost);
                AddRow(section, "TSA_Supply_Row_Status",
                    tooled
                        ? "TSA_Supply_StatusTooled".Translate().ToString()
                        : "TSA_Supply_StatusUntooled".Translate(Mathf.RoundToInt(OutpostTooling.UntooledPenalty * 100f)).ToString(),
                    null,
                    tooled ? Color.green : Color.red);
            }
            return section;
        }

        public static OutpostStatsSection BuildPowerSection(WorldObject_WD_Outpost outpost)
        {
            var section = new OutpostStatsSection { Title = "TSA_Supply_SectionPower".Translate().ToString() };
            if (outpost == null) return section;

            if (outpost.IsPowerPlantOutpost)
            {
                float capacity = Outpost_PowerPlant.GetRemotePowerWatts(outpost);
                AddRow(section, "TSA_Supply_Row_Role", ("TSA_WD_PowerRole_" + outpost.PowerRole).Translate().ToString(), null);
                AddRow(section, "TSA_Supply_Row_Capacity", Watts(capacity), null);
                if (outpost.PowerRole == PowerOutputRole.SupplyOutposts)
                {
                    float used = OutpostPowerGrid.GetPlantUsedWatts(outpost);
                    AddRow(section, "TSA_Supply_Row_Sent", Watts(used), null);
                    AddRow(section, "TSA_Supply_Row_Unassigned", Watts(Mathf.Max(0f, capacity - used)), "TSA_Supply_UnassignedTip");
                    AddRow(section, "TSA_Supply_Row_Served", OutpostPowerGrid.GetPlantServedCount(outpost).ToString(), null);
                }
                return section;
            }

            if (!OutpostPowerGrid.RequiresPower(outpost)) return section;

            float demand = OutpostPowerGrid.GetDemandWatts(outpost);
            float upgrades = outpost.GetBuiltUpgradePowerWattsNeed();
            OutpostPowerGrid.TryGetStatus(outpost, out OutpostPowerStatus status);
            string needTip = "TSA_Supply_Row_PowerNeedTip".Translate(OutpostPowerGrid.BaseDemandWatts.ToString("F0"), upgrades.ToString("F0")).ToString();
            AddRowRaw(section, "TSA_Supply_Row_PowerNeed".Translate().ToString(), Watts(demand), needTip, null);
            AddRow(section, "TSA_Supply_Row_Received", Watts(status.supplied), null,
                status.supplied + 0.01f >= demand ? Color.green : Color.red);
            if (OutpostPowerGrid.Enabled)
            {
                bool powered = OutpostPowerGrid.IsPowered(outpost);
                AddRow(section, "TSA_Supply_Row_Status",
                    powered
                        ? "TSA_Supply_StatusPowered".Translate().ToString()
                        : "TSA_Supply_StatusUnpowered".Translate(Mathf.RoundToInt(OutpostPowerGrid.UnpoweredEffectiveness * 100f)).ToString(),
                    null,
                    powered ? Color.green : Color.red);
            }
            return section;
        }

        public static string PerDay(float v) => "TSA_Supply_PerDay".Translate(v.ToString("F1")).ToString();

        public static string Watts(float v) => "TSA_Supply_Watts".Translate(v.ToString("F0")).ToString();

        private static Color NetColor(float net) => net > 0.1f ? Color.green : (net < -0.1f ? Color.red : Color.yellow);

        private static void AddRole(OutpostStatsSection section, string role) =>
            AddRowRaw(section, "TSA_Supply_Row_Role".Translate().ToString(), role, "", null);

        private static void AddRow(OutpostStatsSection section, string labelKey, string value, string tipKey, Color? color = null) =>
            AddRowRaw(section, labelKey.Translate().ToString(), value, string.IsNullOrEmpty(tipKey) ? "" : tipKey.Translate().ToString(), color);

        private static void AddRowRaw(OutpostStatsSection section, string label, string value, string tooltip, Color? color)
        {
            section.Rows.Add(new OutpostStatRow { Label = label, Value = value, Tooltip = tooltip ?? "", ValueColor = color });
        }
    }
}
