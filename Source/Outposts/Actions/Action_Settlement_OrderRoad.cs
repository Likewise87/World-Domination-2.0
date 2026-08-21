using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    [StaticConstructorOnStartup]
    public static class Action_Settlement_OrderRoad
    {
        private static Texture2D cachedBuildRoadIcon;
        private static Texture2D cachedCancelIcon;

        public static Texture2D BuildRoadIcon =>
            cachedBuildRoadIcon ??= ContentFinder<Texture2D>.Get("UI/Commands/BuildRoad", false) ?? TexCommand.Replant;

        public static IEnumerable<Gizmo> GetGizmos(Settlement settlement)
        {
            if (!OrderedRoadUtility.CanShowOrderRoadGizmo(settlement, out string disabledReason))
                yield break;

            var comp = settlement.GetComponent<CompViralSpread>();
            if (comp == null) yield break;

            if (comp.HasActivePlayerOrderedRoadProject)
            {
                string roadTypeLabel = WorldActions_Roads.GetRoadTierLabel(comp.selectedRoadTier);
                string insufficient = comp.GetInsufficientStrengthConstructionMessage();
                string status = insufficient
                    ?? "TSA_WD_OrderedRoad_Status".Translate(comp.roadTargetName, (Mathf.Min(1f, comp.roadProgress) * 100f).ToString("F0")).ToString();
                yield return new Command_Action
                {
                    defaultLabel = insufficient != null ? insufficient : roadTypeLabel,
                    defaultDesc = status,
                    icon = BuildRoadIcon,
                    Disabled = true
                };

                yield return new Command_Action
                {
                    defaultLabel = "TSA_WD_CancelRoad".Translate(),
                    defaultDesc = "TSA_WD_OrderedRoad_CancelDesc".Translate(),
                    icon = cachedCancelIcon ??= ContentFinder<Texture2D>.Get("UI/Designators/Cancel"),
                    action = () => Find.WindowStack.Add(new Dialog_ConfirmCancelOrderedRoad(settlement, comp))
                };
                yield break;
            }

            var orderRoad = new Command_Action
            {
                defaultLabel = "TSA_WD_OrderedRoad_GizmoLabel".Translate(),
                defaultDesc = "TSA_WD_OrderedRoad_GizmoDesc".Translate(),
                icon = BuildRoadIcon,
                action = () => OpenRoadTierMenu(settlement, comp)
            };
            if (!disabledReason.NullOrEmpty())
                orderRoad.Disable(disabledReason);
            yield return orderRoad;
        }

        private static void OpenRoadTierMenu(Settlement settlement, CompViralSpread comp)
        {
            SettlementTier maxTier = WorldActions_Roads.GetMaxBuildableRoadTierForSettlement(comp.tier);
            var options = new List<FloatMenuOption>();
            TryAddTierOption(options, settlement, comp, SettlementTier.T1, maxTier);
            TryAddTierOption(options, settlement, comp, SettlementTier.T2, maxTier);
            TryAddTierOption(options, settlement, comp, SettlementTier.T3, maxTier);
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static void TryAddTierOption(List<FloatMenuOption> options, Settlement settlement, CompViralSpread comp,
            SettlementTier tier, SettlementTier maxTier)
        {
            if (tier > maxTier) return;
            string label = WorldActions_Roads.GetRoadTierLabel(tier);
            options.Add(new FloatMenuOption(label, () =>
            {
                comp.selectedRoadTier = tier;
                Action_Outpost_BuildRoad.StartRoadTargeting(settlement, comp, selection =>
                {
                    Find.WindowStack.Add(new Dialog_OrderedRoadPreview(settlement, comp, selection, tier));
                });
            }));
        }
    }

    public static class Patch_SettlementOrderedRoadGizmo
    {
        public static IEnumerable<Gizmo> GetGizmos(Settlement settlement)
        {
            foreach (Gizmo gizmo in Action_Settlement_OrderRoad.GetGizmos(settlement))
                yield return gizmo;

            foreach (Gizmo gizmo in Action_Settlement_AutoTravelFood.GetGizmos(settlement))
                yield return gizmo;

            foreach (Gizmo gizmo in Action_Outpost_Build.GetColonyGizmos(settlement))
                yield return gizmo;

            foreach (Gizmo gizmo in Outpost_DispatchMode_Gizmos.GetColonyGizmos(settlement))
                yield return gizmo;
        }
    }
}
