#nullable disable
using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Shared stat columns (Gear, Shooting, Melee, Health, Strength) plus registry extras for the
    /// strength-budget pickers (<see cref="Dialog_OutpostStrengthWithdraw"/>, <see cref="Dialog_OutpostDefenseDeploy"/>).
    /// </summary>
    public static class PawnRosterBudgetTable
    {
        public const string DefaultSortId = PawnRosterColumnIds.Strength;

        private static string hdrGear, hdrShoot, hdrMelee, hdrHealth, hdrStrength;
        private static string tipGear, tipShoot, tipMelee, tipHealth, tipStrength;

        private static void EnsureLabels()
        {
            if (hdrGear != null) return;
            hdrGear = "TSA_WD_StrengthBudget_ColGear".Translate();
            hdrShoot = "TSA_WD_StrengthBudget_ColShooting".Translate();
            hdrMelee = "TSA_WD_StrengthBudget_ColMelee".Translate();
            hdrHealth = "TSA_WD_StrengthBudget_ColHealth".Translate();
            hdrStrength = "TSA_WD_StrengthBudget_ColStrength".Translate();
            tipGear = "TSA_WD_StrengthBudget_ColGearTip".Translate();
            tipShoot = "TSA_WD_StrengthBudget_ColShootingTip".Translate();
            tipMelee = "TSA_WD_StrengthBudget_ColMeleeTip".Translate();
            tipHealth = "TSA_WD_StrengthBudget_ColHealthTip".Translate();
            tipStrength = "TSA_WD_StrengthBudget_ColStrengthTip".Translate();
        }

        private static bool On(PawnRosterColumnWindow window, string id) => PlayerPawnRosterUtility.ColVisible(window, id);

        /// <summary>Visible stat columns + extras (excludes portrait, name and the action column).</summary>
        public static float ColumnsWidth(PawnRosterColumnWindow window)
        {
            float w = 0f;
            if (On(window, PawnRosterColumnIds.Gear)) w += OutpostStrengthBudgetUi.ColGear;
            if (On(window, PawnRosterColumnIds.Shooting)) w += OutpostStrengthBudgetUi.ColShoot;
            if (On(window, PawnRosterColumnIds.Melee)) w += OutpostStrengthBudgetUi.ColMelee;
            if (On(window, PawnRosterColumnIds.Health)) w += OutpostStrengthBudgetUi.ColHealth;
            if (On(window, PawnRosterColumnIds.Strength)) w += OutpostStrengthBudgetUi.ColStrengthDeploy;
            return w + PawnRosterColumnRegistry.ExtrasWidth(window);
        }

        public static void CollectSortOptions(PawnRosterColumnWindow window, List<PawnRosterSortOption> into)
        {
            PawnRosterViewUtil.AddUnique(into, PawnRosterColumnIds.Name, "TSA_WD_StrengthBudget_ColName".Translate());
            PawnRosterViewUtil.AddCatalogSortOptions(window, into);
        }

        /// <summary>Name / Gear / text extras first-click A to Z; numbers high first.</summary>
        public static bool FirstClickAscending(PawnRosterColumnWindow window, string id)
        {
            if (id == PawnRosterColumnIds.Name) return true;
            if (PawnRosterColumnRegistry.IsExtra(window, id)) return PawnRosterColumnRegistry.FirstClickAscending(id);
            return id == PawnRosterColumnIds.Gear;
        }

        /// <summary>Ascending compare for one chain level (Name handled by the chain tiebreak).</summary>
        public static int Compare(
            Pawn a, Pawn b, string id, PawnRosterColumnWindow window, Dictionary<Pawn, PawnRosterRowData> cache)
        {
            if (PawnRosterColumnRegistry.IsExtra(window, id))
                return PawnRosterColumnRegistry.Compare(id, PawnRosterRowData.ForPawn(cache, a), PawnRosterRowData.ForPawn(cache, b));
            switch (id)
            {
                case PawnRosterColumnIds.Name:
                    return 0;
                case PawnRosterColumnIds.Gear:
                    return CountGear(a).CompareTo(CountGear(b));
                case PawnRosterColumnIds.Shooting:
                    return OutpostStrengthBudgetUi.GetSkillLevel(a, SkillDefOf.Shooting)
                        .CompareTo(OutpostStrengthBudgetUi.GetSkillLevel(b, SkillDefOf.Shooting));
                case PawnRosterColumnIds.Melee:
                    return OutpostStrengthBudgetUi.GetSkillLevel(a, SkillDefOf.Melee)
                        .CompareTo(OutpostStrengthBudgetUi.GetSkillLevel(b, SkillDefOf.Melee));
                case PawnRosterColumnIds.Health:
                    return OutpostStrengthBudgetUi.GetHealthPercent(a)
                        .CompareTo(OutpostStrengthBudgetUi.GetHealthPercent(b));
                case PawnRosterColumnIds.Strength:
                    return OutpostStrengthBudget.GetPawnCost(a).CompareTo(OutpostStrengthBudget.GetPawnCost(b));
                default:
                    return 0;
            }
        }

        public static int CountGear(Pawn pawn)
        {
            int n = 0;
            if (pawn?.equipment?.AllEquipmentListForReading != null)
                n += pawn.equipment.AllEquipmentListForReading.Count;
            if (pawn?.apparel?.WornApparel != null)
                n += pawn.apparel.WornApparel.Count;
            return n;
        }

        /// <summary>Filters (name + common columns) and sorts <paramref name="source"/> into <paramref name="into"/>.</summary>
        public static void BuildRows<T>(
            List<T> source,
            List<T> into,
            Func<T, Pawn> pawnOf,
            PawnRosterViewState view,
            Dictionary<Pawn, PawnRosterRowData> cache)
        {
            into.Clear();
            cache.Clear();
            PawnRosterColumnWindow window = view.Window;
            for (int i = 0; i < source.Count; i++)
            {
                T row = source[i];
                Pawn p = pawnOf(row);
                if (p == null) continue;
                if (!PawnRosterViewUtil.PassesCommonFilters(view, PawnRosterRowData.ForPawn(cache, p)))
                    continue;
                into.Add(row);
            }
            if (into.Count < 2) return;
            PawnRosterSortChain sort = view.Sort.IsEmpty ? view.DefaultSort : view.Sort;
            sort.Sort(into, (x, y, id) => Compare(pawnOf(x), pawnOf(y), id, window, cache), r => pawnOf(r)?.LabelShortCap);
        }

        public static void DrawHeaders(
            ref float curX, Rect hRect, PawnRosterColumnWindow window, PawnRosterSortChain sort,
            Action<string> onSort, bool withTips)
        {
            EnsureLabels();
            if (On(window, PawnRosterColumnIds.Gear))
                DrawHeader(ref curX, hRect, OutpostStrengthBudgetUi.ColGear, hdrGear, PawnRosterColumnIds.Gear, TextAnchor.MiddleLeft, withTips ? tipGear : null, sort, onSort);
            if (On(window, PawnRosterColumnIds.Shooting))
                DrawHeader(ref curX, hRect, OutpostStrengthBudgetUi.ColShoot, hdrShoot, PawnRosterColumnIds.Shooting, TextAnchor.MiddleCenter, withTips ? tipShoot : null, sort, onSort);
            if (On(window, PawnRosterColumnIds.Melee))
                DrawHeader(ref curX, hRect, OutpostStrengthBudgetUi.ColMelee, hdrMelee, PawnRosterColumnIds.Melee, TextAnchor.MiddleCenter, withTips ? tipMelee : null, sort, onSort);
            if (On(window, PawnRosterColumnIds.Health))
                DrawHeader(ref curX, hRect, OutpostStrengthBudgetUi.ColHealth, hdrHealth, PawnRosterColumnIds.Health, TextAnchor.MiddleCenter, withTips ? tipHealth : null, sort, onSort);
            if (On(window, PawnRosterColumnIds.Strength))
                DrawHeader(ref curX, hRect, OutpostStrengthBudgetUi.ColStrengthDeploy, hdrStrength, PawnRosterColumnIds.Strength, TextAnchor.MiddleCenter, withTips ? tipStrength : null, sort, onSort);
            PawnRosterColumnRegistry.DrawExtraHeaders(ref curX, hRect.y, hRect.height, window, sort, onSort);
        }

        private static void DrawHeader(
            ref float curX, Rect hRect, float width, string label, string id, TextAnchor anchor, string tip,
            PawnRosterSortChain sort, Action<string> onSort)
        {
            Rect headerRect = new Rect(curX, hRect.y, width, hRect.height);
            if (Mouse.IsOver(headerRect)) Widgets.DrawHighlight(headerRect);
            string text = label + sort.ArrowFor(id);
            OutpostStrengthBudgetUi.LabelAnchored(headerRect, text.Truncate(width - 4f), anchor);
            if (!tip.NullOrEmpty())
                TooltipHandler.TipRegion(headerRect, tip);
            if (Widgets.ButtonInvisible(headerRect))
                onSort?.Invoke(id);
            curX += width;
        }

        /// <summary>
        /// Stat cells + extras. <paramref name="statColor"/> tints Shooting through Strength.
        /// Returns true when the gear icons consumed the click.
        /// </summary>
        public static bool DrawCells(
            ref float curX, float y, float rowH, PawnRosterColumnWindow window, Pawn pawn, float cost,
            Color statColor, bool withTips, string strengthTip, PawnRosterRowData data)
        {
            bool gearClicked = false;
            if (On(window, PawnRosterColumnIds.Gear))
            {
                Rect gearRect = new Rect(curX, y + 4f, OutpostStrengthBudgetUi.ColGear - 4f, rowH - 8f);
                gearClicked = OutpostStrengthBudgetUi.DrawEquippedItemIcons(gearRect, pawn);
                curX += OutpostStrengthBudgetUi.ColGear;
            }

            Color prev = GUI.color;
            GUI.color = statColor;
            if (On(window, PawnRosterColumnIds.Shooting))
            {
                Rect r = new Rect(curX, y, OutpostStrengthBudgetUi.ColShoot, rowH);
                OutpostStrengthBudgetUi.LabelAnchored(r, OutpostStrengthBudgetUi.GetSkillLevel(pawn, SkillDefOf.Shooting).ToString(), TextAnchor.MiddleCenter);
                if (withTips) TooltipHandler.TipRegion(r, SkillDefOf.Shooting.LabelCap);
                curX += OutpostStrengthBudgetUi.ColShoot;
            }
            if (On(window, PawnRosterColumnIds.Melee))
            {
                Rect r = new Rect(curX, y, OutpostStrengthBudgetUi.ColMelee, rowH);
                OutpostStrengthBudgetUi.LabelAnchored(r, OutpostStrengthBudgetUi.GetSkillLevel(pawn, SkillDefOf.Melee).ToString(), TextAnchor.MiddleCenter);
                if (withTips) TooltipHandler.TipRegion(r, SkillDefOf.Melee.LabelCap);
                curX += OutpostStrengthBudgetUi.ColMelee;
            }
            if (On(window, PawnRosterColumnIds.Health))
            {
                Rect r = new Rect(curX, y, OutpostStrengthBudgetUi.ColHealth, rowH);
                float healthPct = OutpostStrengthBudgetUi.GetHealthPercent(pawn);
                OutpostStrengthBudgetUi.LabelAnchored(r, healthPct.ToString("F0") + "%", TextAnchor.MiddleCenter);
                if (withTips)
                    TooltipHandler.TipRegion(r, "TSA_WD_StrengthBudget_HealthTip".Translate(healthPct.ToString("F0")));
                curX += OutpostStrengthBudgetUi.ColHealth;
            }
            if (On(window, PawnRosterColumnIds.Strength))
            {
                Rect r = new Rect(curX, y, OutpostStrengthBudgetUi.ColStrengthDeploy, rowH);
                OutpostStrengthBudgetUi.LabelAnchored(r, cost.ToString("F0"), TextAnchor.MiddleCenter);
                if (!strengthTip.NullOrEmpty()) TooltipHandler.TipRegion(r, strengthTip);
                curX += OutpostStrengthBudgetUi.ColStrengthDeploy;
            }
            GUI.color = prev;

            if (PawnRosterColumnRegistry.AnyVisibleExtra(window))
                PawnRosterColumnRegistry.DrawExtraCells(ref curX, y, rowH, window, data);
            return gearClicked;
        }
    }
}
