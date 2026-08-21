using System.Collections.Generic;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Live force-breakdown row for raid preview / launch (icon + name + compact +strength).</summary>
    public class RaidForceRow
    {
        public WorldObject WorldObject;
        public Faction Faction;
        public string Label;
        public RaidContribRole Role;
        /// <summary>Available strength this participant would commit when included.</summary>
        public float Committed;
        /// <summary>Value shown as +N (0 when an ally is toggled off).</summary>
        public float DisplayStrength;
        public bool Included = true;
        public bool CanToggle;
        public float CurrentStrength;
        public float RetainFloor;
        public bool HitGarrisonCap;
        public string Tooltip;

        public bool IsPrimary => Role == RaidContribRole.AttackerPrimary || Role == RaidContribRole.DefenderPrimary;

        public static RaidForceRow FromWorldObject(
            WorldObject wo,
            RaidContribRole role,
            float committed,
            WorldDominationSettings seth,
            bool included = true,
            bool canToggle = false)
        {
            var comp = wo?.GetComponent<CompViralSpread>();
            float current = role == RaidContribRole.DefenderPrimary
                ? (comp?.GetTotalLocalDefensePower() ?? committed)
                : (comp?.strength ?? 0f);
            float retain = WorldActions_Utils.GetGarrisonRetainFloor(comp, seth);
            bool hitCap = role == RaidContribRole.DefenderPrimary
                ? false
                : Raid_ReinforcementLogic.HitMinGarrisonCap(comp?.strength ?? 0f, committed, seth);

            var row = new RaidForceRow
            {
                WorldObject = wo,
                Faction = wo?.Faction,
                Label = wo?.LabelCap ?? "?",
                Role = role,
                Committed = committed,
                Included = included,
                CanToggle = canToggle,
                CurrentStrength = current,
                RetainFloor = retain,
                HitGarrisonCap = hitCap,
            };
            row.DisplayStrength = included ? committed : 0f;
            row.Tooltip = BuildTooltip(row);
            return row;
        }

        public static RaidForceRow FromDefenderEntry(RaidContribEntry entry, WorldDominationSettings seth)
        {
            if (entry == null) return null;
            return FromWorldObject(entry.obj, entry.role, entry.committed, seth, included: true, canToggle: false);
        }

        /// <summary>Attacker coalition rows from launch contributions (index 0 = primary).</summary>
        public static List<RaidForceRow> FromAttackerContributions(
            List<WorldObject> attackers,
            Dictionary<WorldObject, float> contributions,
            WorldDominationSettings seth)
        {
            var rows = new List<RaidForceRow>();
            if (attackers == null || contributions == null) return rows;
            for (int i = 0; i < attackers.Count; i++)
            {
                WorldObject wo = attackers[i];
                if (wo == null) continue;
                if (!contributions.TryGetValue(wo, out float committed))
                    committed = 0f;
                RaidContribRole role = i == 0 ? RaidContribRole.AttackerPrimary : RaidContribRole.AttackerAlly;
                rows.Add(FromWorldObject(wo, role, committed, seth, included: true, canToggle: false));
            }
            return rows;
        }

        /// <summary>Resolution defender rows: +N = remaining local defense; tip keeps before → after.</summary>
        public static List<RaidForceLogRow> BuildResolutionDefenderLogRows(
            List<WorldObject> fullDefList,
            Dictionary<WorldObject, float> before,
            bool won,
            WorldObject target)
        {
            var list = new List<RaidForceLogRow>();
            if (fullDefList == null) return list;
            for (int i = 0; i < fullDefList.Count; i++)
            {
                WorldObject wo = fullDefList[i];
                if (wo == null) continue;
                float b = before != null && before.TryGetValue(wo, out float beforeVal) ? beforeVal : 0f;
                var woComp = wo.GetComponent<CompViralSpread>();
                float a = (won && wo == target) ? 0f : (woComp?.GetTotalLocalDefensePower() ?? 0f);
                string tip = "TSA_WD_OfCurrentStrength".Translate(b.ToString("F0")) + " → " + "TSA_WD_StrengthAfter".Translate(a.ToString("F0"));
                list.Add(new RaidForceLogRow
                {
                    label = wo.LabelCap,
                    committed = a,
                    tooltip = tip,
                    faction = wo.Faction,
                });
            }
            return list;
        }

        public void RefreshDisplayAndTooltip()
        {
            DisplayStrength = Included ? Committed : 0f;
            Tooltip = BuildTooltip(this);
        }

        public static string BuildTooltip(RaidForceRow row)
        {
            if (row == null) return "";
            var sb = new StringBuilder(192);
            sb.AppendLine(row.Label);
            if (row.Faction != null)
                sb.AppendLine(row.Faction.Name);
            sb.AppendLine(RoleLabel(row.Role));
            sb.AppendLine("TSA_WD_ContribStrength".Translate(row.DisplayStrength.ToString("F0")));
            if (row.Role != RaidContribRole.DefenderPrimary)
                sb.AppendLine("TSA_WD_OfCurrentOffStrength".Translate(row.CurrentStrength.ToString("F0")));
            if (row.HitGarrisonCap)
                sb.AppendLine("TSA_WD_MinGarrisonCap".Translate(row.RetainFloor.ToString("F0")));
            if (row.CanToggle)
            {
                sb.AppendLine();
                sb.Append(row.Included
                    ? "TSA_WD_RaidForce_ClickExclude".Translate()
                    : "TSA_WD_RaidForce_ClickInclude".Translate());
            }
            return sb.ToString().TrimEnd();
        }

        public static string RoleLabel(RaidContribRole role)
        {
            switch (role)
            {
                case RaidContribRole.AttackerPrimary: return "TSA_WD_PrimaryAttacker".Translate();
                case RaidContribRole.AttackerAlly: return "TSA_WD_Ally".Translate();
                case RaidContribRole.DefenderPrimary: return "TSA_WD_Target".Translate();
                case RaidContribRole.DefenderAlly: return "TSA_WD_Ally".Translate();
                default: return "";
            }
        }

        /// <summary>Legacy display|tooltip string for older consumers / save fallback.</summary>
        public string ToLegacyDetailLine()
        {
            string display = Label + ": +" + DisplayStrength.ToString("F0");
            return display + Raid_ReinforcementLogic.DetailTooltipDelimiter + (Tooltip ?? "");
        }

        public RaidForceLogRow ToLogRow()
        {
            return new RaidForceLogRow
            {
                label = Label,
                committed = DisplayStrength,
                tooltip = Tooltip ?? "",
                faction = Faction,
            };
        }
    }

    /// <summary>Persisted force-breakdown row for action-log Details windows.</summary>
    public class RaidForceLogRow : IExposable
    {
        public string label;
        public float committed;
        public string tooltip;
        public Faction faction;

        public void ExposeData()
        {
            Scribe_Values.Look(ref label, "label");
            Scribe_Values.Look(ref committed, "committed", 0f);
            Scribe_Values.Look(ref tooltip, "tooltip");
            Scribe_References.Look(ref faction, "faction");
        }

        public RaidForceLogRow Clone()
        {
            return new RaidForceLogRow
            {
                label = label,
                committed = committed,
                tooltip = tooltip,
                faction = faction,
            };
        }

        public static List<RaidForceLogRow> CloneList(List<RaidForceLogRow> src)
        {
            var list = new List<RaidForceLogRow>();
            if (src == null) return list;
            for (int i = 0; i < src.Count; i++)
            {
                if (src[i] != null)
                    list.Add(src[i].Clone());
            }
            return list;
        }

        public static List<RaidForceLogRow> FromLiveRows(List<RaidForceRow> rows)
        {
            var list = new List<RaidForceLogRow>();
            if (rows == null) return list;
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i] != null)
                    list.Add(rows[i].ToLogRow());
            }
            return list;
        }

        public static List<RaidForceRow> ToDisplayRows(List<RaidForceLogRow> logRows, bool allowToggle = false)
        {
            var list = new List<RaidForceRow>();
            if (logRows == null) return list;
            for (int i = 0; i < logRows.Count; i++)
            {
                var lr = logRows[i];
                if (lr == null) continue;
                list.Add(new RaidForceRow
                {
                    Label = lr.label ?? "?",
                    Faction = lr.faction,
                    Committed = lr.committed,
                    DisplayStrength = lr.committed,
                    Included = true,
                    CanToggle = allowToggle,
                    Tooltip = lr.tooltip ?? "",
                });
            }
            return list;
        }
    }
}
