using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    [StaticConstructorOnStartup]
    public static class Action_Settlement_OrderTrader
    {
        private static Texture2D cachedIcon;

        public static Texture2D SendTraderIcon =>
            cachedIcon ??= ContentFinder<Texture2D>.Get("UI/Commands/SendTrader", false) ?? TexCommand.Install;

        public static IEnumerable<Gizmo> GetGizmos(Settlement settlement)
        {
            if (!OrderedTraderUtility.CanShowOrderTraderGizmo(settlement, out string disabledReason))
                yield break;

            var order = new Command_Action
            {
                defaultLabel = "TSA_WD_OrderedTrader_GizmoLabel".Translate(),
                defaultDesc = "TSA_WD_OrderedTrader_GizmoDesc".Translate(),
                icon = SendTraderIcon,
                action = () => OpenTraderKindMenuOrDialog(settlement)
            };
            if (!disabledReason.NullOrEmpty())
                order.Disable(disabledReason);
            yield return order;
        }

        private static void OpenTraderKindMenuOrDialog(Settlement settlement)
        {
            List<TraderKindDef> kinds = OrderedTraderUtility.GetTraderKinds(settlement.Faction);
            if (kinds.Count == 0)
            {
                Messages.Message("TSA_WD_OrderedTrader_NoKinds".Translate(), settlement, MessageTypeDefOf.RejectInput);
                return;
            }

            if (kinds.Count == 1)
            {
                Find.WindowStack.Add(new Dialog_OrderedTraderPreview(settlement, kinds[0]));
                return;
            }

            var options = new List<FloatMenuOption>();
            for (int i = 0; i < kinds.Count; i++)
            {
                TraderKindDef kind = kinds[i];
                options.Add(new FloatMenuOption(kind.LabelCap, () =>
                {
                    Find.WindowStack.Add(new Dialog_OrderedTraderPreview(settlement, kind));
                }));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }
    }

    public static class Patch_SettlementOrderedTraderGizmo
    {
        public static IEnumerable<Gizmo> GetGizmos(Settlement settlement)
        {
            foreach (Gizmo gizmo in Action_Settlement_OrderTrader.GetGizmos(settlement))
                yield return gizmo;
        }
    }
}
