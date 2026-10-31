using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    [StaticConstructorOnStartup]
    public static class OutpostToolingGizmos
    {
        private static Texture2D cachedIcon;

        public static IEnumerable<Gizmo> GetGizmos(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null || outpost.Faction != Faction.OfPlayer) yield break;
            if (!OutpostTooling.Enabled || !OutpostTooling.CanGenerate(outpost.def)) yield break;

            ToolingOutputRole selected = outpost.SelectedToolingRole;
            ToolingOutputRole inForce = outpost.GetToolingRoleForCurrentCycle();

            string desc = "TSA_WD_ToolingRole_GizmoDesc".Translate(
                OutpostTooling.BaseDemandRangeLabel,
                OutpostTooling.RangeTiles.ToString("F0"),
                Mathf.RoundToInt(OutpostTooling.UntooledPenalty * 100f).ToString()).ToString();
            if (inForce != selected)
                desc += "\n\n" + "TSA_WD_FoodRole_PendingNextCycle".Translate(RoleLabel(inForce), RoleLabel(selected)).ToString();
            else if (outpost.IsSelectionLockedForThisCycle)
                desc += "\n\n" + "TSA_WD_FoodRole_LockedNote".Translate().ToString();

            yield return new Command_Action
            {
                defaultLabel = "TSA_WD_ToolingRole_GizmoLabel".Translate(RoleLabel(selected)).ToString(),
                defaultDesc = desc,
                icon = cachedIcon ??= ContentFinder<Texture2D>.Get("UI/Commands/Build", false) ?? TexCommand.Replant,
                action = delegate
                {
                    var options = new List<FloatMenuOption>();
                    foreach (ToolingOutputRole role in Enum.GetValues(typeof(ToolingOutputRole)))
                    {
                        ToolingOutputRole r = role;
                        string label = RoleLabel(r) + (r == selected ? " " + "TSA_WD_FoodRole_CurrentTag".Translate().ToString() : "");
                        options.Add(new FloatMenuOption(label, delegate
                        {
                            if (outpost.SelectedToolingRole == r) return;
                            outpost.SetSelectedToolingRole(r);
                            if (outpost.GetToolingRoleForCurrentCycle() != r)
                                Messages.Message("TSA_WD_FoodRole_AppliesNextCycle".Translate(outpost.LabelCap, RoleLabel(r)),
                                    outpost, MessageTypeDefOf.NeutralEvent, false);
                        }));
                    }
                    Find.WindowStack.Add(new FloatMenu(options));
                }
            };
        }

        private static string RoleLabel(ToolingOutputRole role) =>
            ("TSA_WD_ToolingRole_" + role).Translate().ToString();
    }
}
