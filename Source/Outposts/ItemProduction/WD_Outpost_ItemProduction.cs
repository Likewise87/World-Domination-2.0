using System.Collections.Generic;

using System.Runtime.CompilerServices;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Production gizmo and selection dialog for WD outposts. Dispatches to Outpost_Farming, Outpost_Mining, Outpost_Hunting, and Outpost_Production_Utils.</summary>
    [StaticConstructorOnStartup]
    public static class Outpost_Production
    {
        private static Texture2D cachedRecruitIcon;
        private static Texture2D cachedCancelIcon;
        private static Texture2D cachedRecruitRedirectIcon;
        private static Texture2D cachedEmbassyIcon;

        private static int gizmoTooltipTick = -1;
        private static int gizmoTooltipFingerprint;
        private static string gizmoTooltipCached;

        private static int GizmoTooltipStateFingerprint(WorldObject_WD_Outpost o)
        {
            if (o == null) return 0;
            unchecked
            {
                int h = RuntimeHelpers.GetHashCode(o);
                h = h * 31 + (o.def?.defName?.GetHashCode() ?? 0);
                h = h * 31 + (o.SelectedProductionDef?.defName?.GetHashCode() ?? 0);
                h = h * 31 + (o.SelectedPawnKindForHunting?.defName?.GetHashCode() ?? 0);
                h = h * 31 + (o.SelectedScavengingKind.HasValue ? (int)o.SelectedScavengingKind.Value + 1 : 0);
                var lockedK = o.GetProducingScavengingKindForCurrentCycle();
                h = h * 31 + (lockedK.HasValue ? ((int)lockedK.Value + 1) * 17 : 0);
                h = h * 31 + (o.SelectedAcademySkillDefName?.GetHashCode() ?? 0);
                h = h * 31 + (o.LockedAcademySkillDefName?.GetHashCode() ?? 0);
                h = h * 31 + (o.IsSelectionLockedForThisCycle ? 1 : 0);
                return h;
            }
        }

        private static string GetProductionTooltipForGizmo(WorldObject_WD_Outpost outpost)
        {
            int tick = Find.TickManager.TicksGame;
            int fp = GizmoTooltipStateFingerprint(outpost);
            if (tick == gizmoTooltipTick && fp == gizmoTooltipFingerprint && gizmoTooltipCached != null)
                return gizmoTooltipCached;
            gizmoTooltipTick = tick;
            gizmoTooltipFingerprint = fp;
            gizmoTooltipCached = GetProductionTooltip(outpost);
            return gizmoTooltipCached;
        }

        public static IEnumerable<Gizmo> GetGizmos(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null || outpost.Faction != Faction.OfPlayer) yield break;

            bool isRecruiting = Outpost_Production_Utils.IsRecruitingOutpost(outpost.def);
            bool isTrading = Outpost_Production_Utils.IsTradingOutpost(outpost.def);
            bool isEmbassy = Outpost_Production_Utils.IsEmbassyOutpost(outpost.def);
            bool isScavenging = Outpost_Production_Utils.IsScavengingOutpost(outpost.def);
            bool isAcademy = Outpost_Production_Utils.IsAcademyOutpost(outpost.def);
            bool isHunting = Outpost_Production_Utils.IsHuntingOutpost(outpost.def);
            // Show what is producing THIS cycle (locked selection), not a pending next-cycle change.
            PawnKindDef huntingKind = outpost.GetProducingPawnKindForCurrentCycle();
            ThingDef currentProduct = outpost.GetProducingDefForCurrentCycle();

            string label;
            Texture2D icon;
            Color iconColor = Color.white;
            if (isEmbassy)
            {
                string delivery = Outpost_Embassy.GetInspectProductLine(outpost);
                label = "TSA_WD_Gizmo_Embassy".Translate(delivery).ToString();
                if (label == "TSA_WD_Gizmo_Embassy" || label.Contains("TSA_WD_"))
                    label = string.IsNullOrEmpty(delivery) ? "Embassy" : delivery;
                icon = cachedEmbassyIcon ??= ContentFinder<Texture2D>.Get("UI/Commands/ManageRelationships", false) ?? TexCommand.Replant;
            }
            else if (isAcademy)
            {
                var acSkill = Outpost_Academy.GetSkillForCurrentCycle(outpost) ?? outpost.SelectedAcademySkill;
                if (acSkill != null)
                {
                    label = "TSA_WD_Gizmo_Academy".Translate(acSkill.LabelCap).ToString();
                    if (label.Contains("TSA_WD_")) label = "Teaching: " + acSkill.LabelCap;
                }
                else
                {
                    label = "TSA_WD_Academy_SelectSkill".Translate().ToString();
                    if (label.Contains("TSA_WD_")) label = "Select academy skill";
                }
                icon = Outpost_Academy.GetGizmoIcon();
            }
            else if (isRecruiting)
            {
                label = Outpost_Recruiting.GetRecruitingFocusLabel(outpost);
                icon = cachedRecruitIcon ??= ContentFinder<Texture2D>.Get("UI/Commands/RecruitPawn", false) ?? TexCommand.Replant;
            }
            else if (isTrading && currentProduct != null)
            {
                string delivery = Outpost_Trading.GetTradingDeliveryProductLine(outpost);
                label = "TSA_WD_Gizmo_Trading".Translate(delivery).ToString();
                if (label == "TSA_WD_Gizmo_Trading" || label.Contains("TSA_WD_"))
                    label = string.IsNullOrEmpty(delivery) ? "Trading" : "Trading: " + delivery;
                icon = currentProduct.uiIcon ?? ThingDefOf.Silver?.uiIcon ?? TexCommand.Replant;
                iconColor = currentProduct.graphicData?.color ?? Color.white;
            }
            else if (isScavenging)
            {
                var scavKind = outpost.GetProducingScavengingKindForCurrentCycle();
                if (scavKind.HasValue)
                {
                    string scavShort = Outpost_Scavenging.GetKindShortLabel(scavKind.Value);
                    label = "TSA_WD_Gizmo_Scavenging_Kind".Translate(scavShort).ToString();
                    if (label.Contains("TSA_WD_"))
                        label = "Scavenging (" + scavShort + ")";
                    icon = WorldDomination_UIUtils.UnknownWorldTargetPlaceholderIcon ?? TexCommand.Replant;
                }
                else
                {
                    label = "TSA_WD_Production".Translate().ToString();
                    if (label == "TSA_WD_Production") label = "Select production";
                    icon = TexCommand.Replant; // match other outposts' "nothing selected" look
                }
            }
            else if (isHunting && huntingKind != null)
            {
                label = "TSA_WD_Producing_Hunting".Translate(huntingKind.LabelCap).ToString();
                if (label == "TSA_WD_Producing_Hunting") label = "Hunting " + huntingKind.LabelCap;
                icon = huntingKind.race?.uiIcon;
                if (icon == null && huntingKind.RaceProps?.meatDef != null) icon = huntingKind.RaceProps.meatDef.uiIcon;
                if (icon == null) icon = TexCommand.Replant;
                iconColor = huntingKind.race?.graphicData?.color ?? Color.white;
            }
            else if (currentProduct != null)
            {
                bool isMining = Outpost_Production_Utils.IsMiningOutpost(outpost.def);
                bool isFarming = Outpost_Production_Utils.IsFarmingOutpost(outpost.def);
                if (isMining)
                {
                    label = "TSA_WD_Gizmo_Mining".Translate(currentProduct.LabelCap).ToString();
                    if (label == "TSA_WD_Gizmo_Mining") label = "Mining " + currentProduct.LabelCap;
                }
                else if (isFarming)
                {
                    label = "TSA_WD_Gizmo_Harvesting".Translate(currentProduct.LabelCap).ToString();
                    if (label == "TSA_WD_Gizmo_Harvesting") label = "Harvesting " + currentProduct.LabelCap;
                }
                else
                {
                    label = "TSA_WD_Producing".Translate(currentProduct.LabelCap).ToString();
                    if (label == "TSA_WD_Producing") label = "Producing " + currentProduct.LabelCap;
                }
                icon = currentProduct.uiIcon ?? TexCommand.Replant;
                iconColor = currentProduct.graphicData?.color ?? (isMining ? Outpost_Mining.GetChunkColor(currentProduct) : null) ?? Color.white;
            }
            else
            {
                label = "TSA_WD_Production".Translate().ToString();
                if (label == "TSA_WD_Production") label = "Select production";
                icon = TexCommand.Replant;
            }

            yield return new Command_Action
            {
                defaultLabel = label,
                defaultDesc = GetProductionTooltipForGizmo(outpost),
                icon = icon,
                defaultIconColor = iconColor,
                action = () =>
                {
                    if (Outpost_Production_Utils.IsRecruitingOutpost(outpost.def))
                        Find.WindowStack.Add(new Dialog_OutpostRecruiting(outpost));
                    else if (Outpost_Production_Utils.IsTradingOutpost(outpost.def))
                        Find.WindowStack.Add(new Dialog_OutpostTrading(outpost));
                    else if (Outpost_Production_Utils.IsEmbassyOutpost(outpost.def))
                        Find.WindowStack.Add(new Dialog_OutpostEmbassy(outpost));
                    else if (Outpost_Production_Utils.IsAcademyOutpost(outpost.def))
                        Find.WindowStack.Add(new Dialog_OutpostAcademyProduction(outpost));
                    else
                        Find.WindowStack.Add(new Dialog_OutpostProduction(outpost));
                }
            };

            if (isRecruiting)
            {
                var comp = outpost.GetComponent<CompViralSpread>();
                if (comp != null)
                {
                    Texture2D redirectIcon = cachedRecruitRedirectIcon
                        ??= ContentFinder<Texture2D>.Get("UI/Commands/AutoSendPawn", false) ?? TexCommand.Attack;
                    yield return new Command_Action
                    {
                        defaultLabel = "TSA_WD_SetRedirection".Translate().ToString(),
                        defaultDesc = "TSA_WD_SetRedirectionDesc".Translate().ToString(),
                        icon = redirectIcon,
                        action = () => BeginRecruitRedirectTargeting(outpost, comp),
                        onHover = () => DrawRecruitRedirectHoverLine(outpost, comp)
                    };
                    if (comp.redirectionTargetTile >= 0)
                    {
                        yield return new Command_Action
                        {
                            defaultLabel = "TSA_WD_ClearRedirection".Translate().ToString(),
                            defaultDesc = "TSA_WD_ClearRedirectionDesc".Translate().ToString(),
                            icon = cachedCancelIcon ??= ContentFinder<Texture2D>.Get("UI/Designators/Cancel", false) ?? TexCommand.Replant,
                            action = () =>
                            {
                                comp.redirectionTargetTile = -1;
                                Messages.Message("TSA_WD_RecruitsRedirectCleared".Translate(), outpost, MessageTypeDefOf.NeutralEvent);
                            },
                            onHover = () => DrawRecruitRedirectHoverLine(outpost, comp)
                        };
                    }
                }
            }
        }

        private static void DrawRecruitRedirectHoverLine(WorldObject_WD_Outpost outpost, CompViralSpread comp)
        {
            WD_RadiusOverlayPrefs.NotifySuppressFillThisFrame();
            if (outpost == null || comp == null || comp.redirectionTargetTile < 0) return;
            GenDraw_WorldLineSmooth.DrawSmoothWorldLine(
                outpost.Tile,
                comp.redirectionTargetTile,
                Find.WorldGrid,
                WorldOverlayLineMaterials.RecruitRedirectLine,
                1f,
                GenDraw_WorldLineSmooth.GetPathLineLift());
        }

        private static void BeginRecruitRedirectTargeting(WorldObject_WD_Outpost outpost, CompViralSpread comp)
        {
            Texture2D mouseIcon = cachedRecruitRedirectIcon
                ??= ContentFinder<Texture2D>.Get("UI/Commands/AutoSendPawn", false) ?? TexCommand.Attack;
            CameraJumper.TryJump(outpost.Tile);
            Find.WorldTargeter.BeginTargeting(
                (GlobalTargetInfo target) =>
                {
                    if (target.Tile < 0) return false;
                    comp.redirectionTargetTile = target.Tile;
                    Messages.Message("TSA_WD_RecruitsRedirectSet".Translate(target.Tile), outpost, MessageTypeDefOf.PositiveEvent);
                    return true;
                },
                true,
                mouseIcon,
                false,
                () => WD_RadiusOverlayPrefs.NotifySuppressFillThisFrame(),
                null
            );
        }

        /// <summary>Summary line for production/trading outpost. Recruiting/Trading use type-specific lines.</summary>
        public static string FormatProductionSummaryLine(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null) return null;
            if (Outpost_Production_Utils.IsRecruitingOutpost(outpost.def))
                return Outpost_Recruiting.GetProductionSummaryLine(outpost, outpost.GetCapacityForYieldPreview());
            if (Outpost_Production_Utils.IsTradingOutpost(outpost.def))
                return Outpost_Trading.GetProductionSummaryLine(outpost);
            if (Outpost_Production_Utils.IsEmbassyOutpost(outpost.def))
                return Outpost_Embassy.GetProductionSummaryLine(outpost);
            if (Outpost_Production_Utils.IsScavengingOutpost(outpost.def))
                return Outpost_Scavenging.GetProductionSummaryLine(outpost);
            if (Outpost_Production_Utils.IsAcademyOutpost(outpost.def))
                return Outpost_Academy.GetProductionSummaryLine(outpost);
            if (Outpost_Production_Utils.IsResearchOutpost(outpost.def))
                return Outpost_Research.GetProductionSummaryLine(outpost);
            if (outpost.SelectedProductionDef == null) return null;
            ThingDef product = outpost.SelectedProductionDef;
            float cycleDays = Outpost_Production_Utils.GetProductionCycleDays(outpost);
            var items = GetCurrentDeliveryItems(outpost);
            string cycleStr = cycleDays.ToString("F0");
            if (items == null || items.Count == 0)
                return "TSA_WD_Prod_GenericSummary_None".Translate(product.LabelCap, cycleStr).ToString();
            var parts = new List<string>();
            foreach (var tc in items)
                if (tc?.thingDef != null) parts.Add("x" + tc.count + " " + tc.thingDef.LabelCap);
            string yieldStr = string.Join(" and ", parts);
            return "TSA_WD_Prod_GenericSummary".Translate(product.LabelCap, yieldStr, cycleStr).ToString();
        }

        public static string GetProductionDesc(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null) return "";
            if (Outpost_Production_Utils.IsRecruitingOutpost(outpost.def))
                return "TSA_WD_Production_RecruitingDesc".Translate().ToString();
            if (Outpost_Production_Utils.IsTradingOutpost(outpost.def))
                return "TSA_WD_Production_TradingDesc".Translate().ToString();
            if (Outpost_Production_Utils.IsEmbassyOutpost(outpost.def))
                return "TSA_WD_Production_EmbassyDesc".Translate().ToString();
            if (Outpost_Production_Utils.IsScavengingOutpost(outpost.def))
                return Outpost_Scavenging.GetProductionTooltip(outpost);
            if (Outpost_Production_Utils.IsAcademyOutpost(outpost.def))
                return Outpost_Academy.GetProductionTooltip(outpost);
            string productLine = Outpost_Production_Utils.FormatDeliveryProductLine(GetCurrentDeliveryItems(outpost));
            if (!string.IsNullOrEmpty(productLine)) return productLine;
            if (Outpost_Production_Utils.IsHuntingOutpost(outpost.def) && outpost.SelectedPawnKindForHunting != null)
            {
                string t = "TSA_WD_Production_CurrentHunting".Translate(outpost.SelectedPawnKindForHunting.LabelCap).ToString();
                return t;
            }
            if (Outpost_Production_Utils.IsFishingOutpost(outpost.def) && outpost.SelectedFishDef != null)
                return "TSA_WD_Production_CurrentFishing".Translate(outpost.SelectedFishDef.LabelCap).ToString();
            ThingDef current = outpost.SelectedProductionDef;
            if (current != null)
                return "TSA_WD_Production_Current".Translate(current.LabelCap).ToString();
            return "TSA_WD_Production_None".Translate().ToString();
        }

        /// <summary>Delivery-driving capacity for this outpost (averaged over cycle for spawn). When selection is locked, uses producing-for-this-cycle so we average capacity for the item actually being produced.</summary>
        public static float GetDeliveryDrivingCapacity(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null) return 0f;
            if (Outpost_Production_Utils.IsRecruitingOutpost(outpost.def))
                return Outpost_Recruiting.GetDeliveryDrivingCapacity(outpost);
            if (Outpost_Production_Utils.IsTradingOutpost(outpost.def))
                return Outpost_Recruiting.GetDeliveryDrivingCapacity(outpost);
            if (Outpost_Production_Utils.IsEmbassyOutpost(outpost.def))
                return Outpost_Embassy.GetDeliveryDrivingCapacity(outpost);
            if (Outpost_Production_Utils.IsScavengingOutpost(outpost.def))
                return Outpost_Scavenging.GetDeliveryDrivingCapacity(outpost);
            if (Outpost_Production_Utils.IsAcademyOutpost(outpost.def))
                return Outpost_Academy.GetDeliveryDrivingCapacity(outpost);
            if (Outpost_Production_Utils.IsHuntingOutpost(outpost.def))
                return Outpost_Production_Utils.GetSkillAssignedToPhysicalProduction(outpost);
            if (Outpost_Production_Utils.IsFishingOutpost(outpost.def))
                return Outpost_Production_Utils.GetSkillAssignedToPhysicalProduction(outpost);
            if (Outpost_Production_Utils.IsFarmingOutpost(outpost.def))
                return Outpost_Production_Utils.GetSkillAssignedToPhysicalProduction(outpost);
            if (Outpost_Production_Utils.IsRanchOutpost(outpost.def))
                return Outpost_Production_Utils.GetSkillAssignedToPhysicalProduction(outpost);
            if (Outpost_Production_Utils.IsMiningOutpost(outpost.def))
                return outpost.TotalMiningSkill();
            if (Outpost_Production_Utils.IsProductionOrTradingOutpost(outpost.def))
            {
                ThingDef producing = outpost.IsSelectionLockedForThisCycle ? outpost.GetProducingDefForCurrentCycle() : outpost.SelectedProductionDef;
                if (producing == null) return 0f;
                var option = Outpost_Production_Utils.GetProductionOption(outpost, producing);
                if (option == null) return 0f;
                return Outpost_Production_Utils.GetEligibleSkillForProduction(outpost, option);
            }
            return 0f;
        }

        /// <summary>Expected delivery this cycle using time-weighted capacity preview (same as inspect / gizmo).</summary>
        public static List<ThingDefCountClass> GetCurrentDeliveryItems(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null) return null;
            return GetCurrentDeliveryItems(outpost, outpost.GetCapacityForYieldPreview());
        }

        /// <summary>Current delivery items. When overrideDeliveryCapacity is set (spawn and explicit UI columns), uses producing-for-this-cycle and that capacity. Recruiting and Trading return null (handled in Tick).</summary>
        public static List<ThingDefCountClass> GetCurrentDeliveryItems(WorldObject_WD_Outpost outpost, float? overrideDeliveryCapacity)
        {
            if (outpost == null) return null;
            if (Outpost_Production_Utils.IsRecruitingOutpost(outpost.def) || Outpost_Production_Utils.IsTradingOutpost(outpost.def) || Outpost_Production_Utils.IsEmbassyOutpost(outpost.def))
                return null;
            if (Outpost_Production_Utils.IsScavengingOutpost(outpost.def))
                return null;
            if (Outpost_Production_Utils.IsAcademyOutpost(outpost.def))
                return null;
            bool useOverride = overrideDeliveryCapacity.HasValue;

            if (Outpost_Production_Utils.IsHuntingOutpost(outpost.def))
            {
                var kind = useOverride ? outpost.GetProducingPawnKindForCurrentCycle() : outpost.SelectedPawnKindForHunting;
                if (kind != null && kind.RaceProps != null)
                {
                    float capacity = useOverride ? overrideDeliveryCapacity.Value : Outpost_Production_Utils.GetSkillAssignedToPhysicalProduction(outpost);
                    var items = Outpost_Hunting.BuildHuntingDeliveryItems(kind, capacity, outpost);
                    var hList = items ?? new List<ThingDefCountClass>();
                    Outpost_Production_Utils.ApplyOutputMultiplierToDeliveryItems(hList, outpost);
                    return hList;
                }
                return null;
            }
            if (Outpost_Production_Utils.IsFishingOutpost(outpost.def))
            {
                var fish = useOverride ? outpost.GetProducingFishForCurrentCycle() : outpost.SelectedFishDef;
                if (fish != null)
                {
                    float capacity = useOverride ? overrideDeliveryCapacity.Value : Outpost_Production_Utils.GetSkillAssignedToPhysicalProduction(outpost);
                    var items = Outpost_Fishing.BuildFishingDeliveryItems(fish, capacity, outpost);
                    var fList = items ?? new List<ThingDefCountClass>();
                    Outpost_Production_Utils.ApplyOutputMultiplierToDeliveryItems(fList, outpost);
                    return fList;
                }
                return null;
            }
            ThingDef producing = useOverride ? outpost.GetProducingDefForCurrentCycle() : outpost.SelectedProductionDef;
            if (producing == null) return null;
            if (Outpost_Production_Utils.IsProductionOrTradingOutpost(outpost.def))
            {
                var option = Outpost_Production_Utils.GetProductionOption(outpost, producing);
                if (option == null) return null;
                float eligible = useOverride ? overrideDeliveryCapacity.Value : Outpost_Production_Utils.GetEligibleSkillForProduction(outpost, option);
                float tileFactor = Outpost_Production_Utils.IsRanchOutpost(outpost.def) ? Outpost_Production_Utils.GetRanchTileProductionFactor(outpost) : 1f;
                int qty = Mathf.Max(0, Mathf.RoundToInt(eligible * option.amountPerSkillLevel * tileFactor));
                if (qty <= 0) return null;
                // Do NOT clamp to producing.stackLimit here: SpawnOutpostDeliveryTraveler splits into
                // multiple stacks at delivery time. Clamping here would mask skill scaling in UI/inspect
                // for high amountPerSkillLevel items like Chemfuel (22/skill, stackLimit 150).
                var prodList = new List<ThingDefCountClass> { new ThingDefCountClass(producing, qty) };
                Outpost_Production_Utils.ApplyOutputMultiplierToDeliveryItems(prodList, outpost);
                return prodList;
            }
            if (Outpost_Production_Utils.IsMiningOutpost(outpost.def))
            {
                var mList = Outpost_Mining.GetDeliveryItems(outpost, producing, useOverride ? overrideDeliveryCapacity : null);
                if (mList != null && mList.Count > 0)
                    Outpost_Production_Utils.ApplyOutputMultiplierToDeliveryItems(mList, outpost);
                return mList;
            }
            if (Outpost_Production_Utils.IsFarmingOutpost(outpost.def))
            {
                var fList = Outpost_Farming.GetDeliveryItems(outpost, producing, useOverride ? overrideDeliveryCapacity : null);
                if (fList != null && fList.Count > 0)
                    Outpost_Production_Utils.ApplyOutputMultiplierToDeliveryItems(fList, outpost);
                return fList;
            }
            return null;
        }

        /// <summary>Full tooltip for production gizmo. Delegates by outpost type.</summary>
        public static string GetProductionTooltip(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null) return "";
            if (Outpost_Production_Utils.IsRecruitingOutpost(outpost.def))
                return Outpost_Recruiting.GetProductionTooltip(outpost, outpost.GetCapacityForYieldPreview());
            if (Outpost_Production_Utils.IsTradingOutpost(outpost.def))
                return Outpost_Trading.GetProductionTooltip(outpost);
            if (Outpost_Production_Utils.IsEmbassyOutpost(outpost.def))
                return Outpost_Embassy.GetProductionTooltip(outpost);
            if (Outpost_Production_Utils.IsScavengingOutpost(outpost.def))
                return Outpost_Scavenging.GetProductionTooltip(outpost);
            if (Outpost_Production_Utils.IsAcademyOutpost(outpost.def))
                return Outpost_Academy.GetProductionTooltip(outpost);
            if (Outpost_Production_Utils.IsHuntingOutpost(outpost.def) && outpost.SelectedPawnKindForHunting != null)
                return Outpost_Hunting.GetProductionTooltip(outpost, outpost.SelectedPawnKindForHunting);
            if (Outpost_Production_Utils.IsFishingOutpost(outpost.def) && outpost.SelectedFishDef != null)
                return Outpost_Fishing.GetProductionTooltip(outpost, outpost.SelectedFishDef);
            if (Outpost_Production_Utils.IsFarmingOutpost(outpost.def) && outpost.SelectedProductionDef != null)
                return Outpost_Farming.GetProductionTooltip(outpost, outpost.SelectedProductionDef);
            if (Outpost_Production_Utils.IsMiningOutpost(outpost.def) && outpost.SelectedProductionDef != null)
                return Outpost_Mining.GetProductionTooltip(outpost, outpost.SelectedProductionDef);
            if (Outpost_Production_Utils.IsProductionOrTradingOutpost(outpost.def) && outpost.SelectedProductionDef != null)
            {
                ThingDef product = outpost.SelectedProductionDef;
                var option = Outpost_Production_Utils.GetProductionOption(outpost, product);
                    if (option != null)
                    {
                        float eligible = outpost.GetCapacityForYieldPreview();
                        float tileFactor = Outpost_Production_Utils.IsRanchOutpost(outpost.def) ? Outpost_Production_Utils.GetRanchTileProductionFactor(outpost) : 1f;
                        int totalItems = Outpost_Production_Utils.ScaleOutputStackCount(Mathf.RoundToInt(eligible * option.amountPerSkillLevel * tileFactor), outpost);
                    var scaleSkill = Outpost_Production_Utils.GetScalingSkillDefForProduction(outpost, option);
                    string skillLabel = Outpost_Production_Utils.SkillLabelCap(scaleSkill);
                    if (string.IsNullOrEmpty(skillLabel))
                        skillLabel = "TSA_WD_Production_SkillFallback".Translate().ToString();
                    string line1Key = "TSA_WD_Production_TooltipFabrication_Line1";
                    string line1 = line1Key.Translate(
                        eligible.ToString("F0"),
                        skillLabel,
                        option.amountPerSkillLevel.ToString("F1"),
                        totalItems.ToString(),
                        product.LabelCap).ToString();
                    if (line1 == line1Key || line1.Contains("TSA_WD_Production_TooltipFabrication_Line1"))
                    {
                        string tilePart = Outpost_Production_Utils.IsRanchOutpost(outpost.def) ? " × " + tileFactor.ToString("P0") + " fertility" : "";
                        line1 = eligible.ToString("F0") + " " + skillLabel + " (eligible) × " + option.amountPerSkillLevel.ToString("F1") + " per skill" + tilePart + " → " + totalItems + " " + product.LabelCap + " per cycle (includes global output multiplier).";
                    }
                    string ranchTileExtra = Outpost_Production_Utils.IsRanchOutpost(outpost.def)
                        ? "\nTile fertility multiplier: " + tileFactor.ToString("P0") + "."
                        : "";
                    string line2Key = "TSA_WD_Production_TooltipFabrication_Line2";
                    string line2 = line2Key.Translate(skillLabel, option.minSkillLevel).ToString();
                    if (line2 == line2Key || line2.Contains("TSA_WD_Production_TooltipFabrication_Line2"))
                        line2 = "At least one pawn with " + skillLabel + " ≥ " + option.minSkillLevel + " required; output uses total " + skillLabel + " skill of the outpost.";
                    string researchExtra = "";
                    if (!string.IsNullOrEmpty(option.requiredResearch))
                    {
                        string rc = DefDatabase<ResearchProjectDef>.GetNamedSilentFail(option.requiredResearch)?.LabelCap ?? option.requiredResearch;
                        string resKey = "TSA_WD_Production_TooltipFabrication_Research";
                        string resLine = resKey.Translate(rc).ToString();
                        if (resLine == resKey || resLine.Contains("TSA_WD_Production_TooltipFabrication_Research"))
                            resLine = "Requires research: " + rc;
                        researchExtra = "\n" + resLine;
                    }
                    return line1 + ranchTileExtra + "\n\n" + line2 + researchExtra;
                }
            }
            return GetProductionDesc(outpost);
        }

        /// <summary>Options available for this outpost type. Delegates to Farming, Hunting, Mining, or production def. Recruiting/Trading get one virtual option for dialog alignment.</summary>
        public static List<ThingDef> GetProducibleOptions(WorldObject_WD_Outpost outpost)
        {
            if (outpost?.def == null) return new List<ThingDef>();
            if (Outpost_Production_Utils.IsRecruitingOutpost(outpost.def))
                return new List<ThingDef> { ThingDefOf.Human };
            if (Outpost_Production_Utils.IsTradingOutpost(outpost.def))
                return new List<ThingDef> { ThingDefOf.Silver };
            if (Outpost_Production_Utils.IsScavengingOutpost(outpost.def))
                return new List<ThingDef> { ThingDefOf.ComponentIndustrial };
            if (Outpost_Production_Utils.IsFarmingOutpost(outpost.def))
                return Outpost_Farming.GetProducibleOptions(outpost);
            if (Outpost_Production_Utils.IsHuntingOutpost(outpost.def))
            {
                var animalOpts = Outpost_Hunting.GetHuntingAnimalOptions(outpost);
                var list = new List<ThingDef>();
                foreach (var opt in animalOpts)
                    foreach (var thing in opt.Products)
                        if (thing != null && !list.Contains(thing)) list.Add(thing);
                if (list.Count == 0)
                {
                    Outpost_Production_Utils.AddIfExists(list, "Meat_Bison");
                    Outpost_Production_Utils.AddIfExists(list, "Meat_Deer");
                    Outpost_Production_Utils.AddIfExists(list, "Meat_Muffalo");
                }
                return list;
            }
            if (Outpost_Production_Utils.IsFishingOutpost(outpost.def))
            {
                var fishOpts = Outpost_Fishing.GetFishingFishOptions(outpost);
                var list = new List<ThingDef>();
                foreach (var opt in fishOpts)
                    if (opt.Fish != null && !list.Contains(opt.Fish)) list.Add(opt.Fish);
                return list;
            }
            if (Outpost_Production_Utils.IsMiningOutpost(outpost.def))
                return Outpost_Mining.GetProducibleOptions(outpost);
            if (Outpost_Production_Utils.IsProductionOrTradingOutpost(outpost.def))
            {
                var list = new List<ThingDef>();
                var opts = Outpost_Production_Utils.GetProductionOptions(outpost);
                if (opts != null)
                    foreach (var opt in opts)
                    {
                        if (!Outpost_Production_Utils.ProductionOptionPassesMayRequire(opt)) continue;
                        var thing = DefDatabase<ThingDef>.GetNamedSilentFail(opt.thingDef);
                        if (thing != null && !list.Contains(thing)) list.Add(thing);
                    }
                if (list.Count == 0)
                {
                    string d = outpost.def.defName.ToLowerInvariant();
                    if (d.Contains("production")) { list.Add(ThingDefOf.Cloth); list.Add(ThingDefOf.Steel); }
                    else list.Add(ThingDefOf.Silver);
                }
                return list;
            }
            return new List<ThingDef> { ThingDefOf.Silver };
        }

        /// <summary>Output per skill point. Delegates by outpost type.</summary>
        public static float GetOutputPerSkillPoint(WorldObject_WD_Outpost outpost, ThingDef product)
        {
            if (outpost?.def == null) return 0f;
            if (Outpost_Production_Utils.IsFarmingOutpost(outpost.def) && product != null)
                return Outpost_Farming.GetOutputPerSkillPoint(outpost, product);
            if (Outpost_Production_Utils.IsHuntingOutpost(outpost.def))
                return Outpost_Hunting.GetOutputPerSkillPoint(outpost);
            if (Outpost_Production_Utils.IsFishingOutpost(outpost.def))
                return Outpost_Fishing.GetOutputPerSkillPoint(outpost);
            if (Outpost_Production_Utils.IsMiningOutpost(outpost.def) && product != null)
                return Outpost_Mining.GetOutputPerSkillPoint(outpost, product);
            if (Outpost_Production_Utils.IsProductionOrTradingOutpost(outpost.def) && product != null)
            {
                var option = Outpost_Production_Utils.GetProductionOption(outpost, product);
                if (option != null)
                {
                    float tileFactor = Outpost_Production_Utils.IsRanchOutpost(outpost.def) ? Outpost_Production_Utils.GetRanchTileProductionFactor(outpost) : 1f;
                    return option.amountPerSkillLevel * tileFactor;
                }
            }
            return Outpost_Production_Utils.GetBaselineOutputPerSkill();
        }

        // ---- Re-expose for dialog and other callers ----
        public static List<HuntingAnimalOption> GetHuntingAnimalOptions(WorldObject_WD_Outpost outpost) => Outpost_Hunting.GetHuntingAnimalOptions(outpost);
        public static List<ThingDefCountClass> BuildHuntingDeliveryItems(PawnKindDef kind, float capacity, WorldObject_WD_Outpost outpost = null) => Outpost_Hunting.BuildHuntingDeliveryItems(kind, capacity, outpost);
        public static string FormatDeliveryProductLine(List<ThingDefCountClass> items) => Outpost_Production_Utils.FormatDeliveryProductLine(items);
        public static string FormatHuntingSummaryLine(WorldObject_WD_Outpost outpost) => Outpost_Hunting.FormatHuntingSummaryLine(outpost);
        public static string GetCropBaselineTooltip(ThingDef harvest) => Outpost_Farming.GetCropBaselineTooltip(harvest);
        public static string GetFarmingEfficiencyTooltip(WorldObject_WD_Outpost outpost) => Outpost_Farming.GetFarmingEfficiencyTooltip(outpost);
        public static string GetAnimalBaselineTooltip(PawnKindDef kind, WorldObject_WD_Outpost outpost = null) => Outpost_Hunting.GetAnimalBaselineTooltip(kind, outpost);
        public static string GetHuntingEfficiencyTooltip(WorldObject_WD_Outpost outpost) => Outpost_Hunting.GetHuntingEfficiencyTooltip(outpost);
        public static string GetMiningEfficiencyTooltip(WorldObject_WD_Outpost outpost) => Outpost_Mining.GetMiningEfficiencyTooltip(outpost);
        public static string GetMiningBaselineTooltip(ThingDef product) => Outpost_Mining.GetMiningBaselineTooltip(product);
        public static string GetSkillFactorTooltip(string skillName) => Outpost_Production_Utils.GetSkillFactorTooltip(skillName);
        public static float GetProductionCycleDays(WorldObject_WD_Outpost outpost) => Outpost_Production_Utils.GetProductionCycleDays(outpost);
        public static float GetProductionCycleDaysBase(WorldObject_WD_Outpost outpost) => Outpost_Production_Utils.GetProductionCycleDaysBase(outpost);
        public static float GetFarmingTileProductionFactor(WorldObject_WD_Outpost outpost) => Outpost_Production_Utils.GetFarmingTileProductionFactor(outpost);
        public static float GetHuntingTileProductionFactor(WorldObject_WD_Outpost outpost) => Outpost_Production_Utils.GetHuntingTileProductionFactor(outpost);
        public static float GetMiningTileProductionFactor(WorldObject_WD_Outpost outpost) => Outpost_Production_Utils.GetMiningTileProductionFactor(outpost);
        public static ProductionOption GetProductionOption(WorldObject_WD_Outpost outpost, ThingDef product) => Outpost_Production_Utils.GetProductionOption(outpost, product);
        public static float GetEligibleSkillForProduction(WorldObject_WD_Outpost outpost, ProductionOption option) => Outpost_Production_Utils.GetEligibleSkillForProduction(outpost, option);
        public static float GetScalingSkillTotalForProductionPreview(WorldObject_WD_Outpost outpost, ProductionOption option) => Outpost_Production_Utils.GetScalingSkillTotalForProductionPreview(outpost, option);
        public static bool IsResearchDoneForOption(ProductionOption option) => Outpost_Production_Utils.IsResearchDoneForOption(option);
        public static bool OutpostCanProduceItem(WorldObject_WD_Outpost outpost, ThingDef product) => Outpost_Production_Utils.OutpostCanProduceItem(outpost, product);
    }
}
