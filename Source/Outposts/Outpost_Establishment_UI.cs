using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    internal struct EstablishmentReqLine
    {
        public string text;
        public string tooltip;
        public bool met;
        public bool useFixedColor;
        public Color color;
    }

    internal struct EstablishmentCostItem
    {
        public ThingDef thingDef;
        public string countLabel;
        public string tooltipLabel;
        /// <summary>When set from a real caravan establish: true if inventory covers this cost line.</summary>
        public bool met;
        public bool colorByAvailability;
        public bool waived;
    }

    internal struct EstablishmentRowCache
    {
        public List<SkillDef> displaySkills;
        public int reqCount;
        public string skillLine;
        public string skillTooltip;
        public string outpostTooltip;
        public EstablishmentCostItem[] costItems;
        public bool[] reqApplies;
        public EstablishmentReqLine[] reqs;
    }

    /// <summary>Shared drawing for the outpost establishment / requirements-preview dialog.</summary>
    internal static class Outpost_Establishment_UI
    {
        public const float DetailLineH = Outpost_Dialog_UI.OutcomeLineH;
        public const float DetailCostLineH = Outpost_Dialog_UI.YieldLineH;
        public const float DetailSectionGap = 8f;
        public const float DetailIconSize = 48f;
        public const float DetailTitleLineH = Outpost_Dialog_UI.OutcomeLineH;
        public const float EstablishButtonH = 36f;
        public const float PreviewStatusBoxMinH = 36f;
        public const float TierHeaderH = 42f;

        public static readonly Color RowBgPreview = new Color(0.25f, 0.2f, 0.05f, 0.42f);

        /// <summary>Keyed establishment description: TSA_WD_OutpostTooltip_{defName}.</summary>
        public static string GetOutpostDescription(WorldObjectDef def)
        {
            if (def == null) return "";
            string key = "TSA_WD_OutpostTooltip_" + def.defName;
            if (key.CanTranslate())
                return key.Translate();
            return "";
        }

        /// <summary>Tooltip for outpost type icons and compact rows.</summary>
        public static string GetOutpostTypeTooltip(WorldObjectDef def)
        {
            string desc = GetOutpostDescription(def);
            if (!desc.NullOrEmpty()) return desc;
            return def?.LabelCap ?? "";
        }

        public static float MeasureTileDetailsBoxHeight(bool tileValid, bool showProximityBlocked = false)
        {
            const float boxPad = Outpost_Dialog_UI.OutcomeBoxPad;
            const float lineH = Outpost_Dialog_UI.YieldLineH;
            if (!tileValid)
                return boxPad * 2f + lineH;
            // Biome, terrain, fertility, animals, fish, mining (no section header).
            float h = boxPad * 2f + 6f * lineH;
            if (showProximityBlocked) h += lineH;
            return h;
        }

        public static float DrawTileDetailsBox(
            float x,
            float y,
            float w,
            bool tileValid,
            bool requirementsPreviewOnly,
            string invalidHint,
            string biomeName,
            string terrainVal,
            int fertPct,
            int animPct,
            int fishPct,
            int minePct,
            string fertLabel,
            string animLabel,
            string fishLabel,
            string mineLabel,
            string biomeTip,
            string terrainTip,
            string fertTip,
            string huntTip,
            string fishTip,
            string miningTip,
            string colBiome,
            string colTerrain,
            string colFertility,
            string colAnimals,
            string colFish,
            string colMining,
            string proximityBlockedHint = null,
            string proximityBlockedTip = null)
        {
            bool showProximityBlocked = tileValid && !proximityBlockedHint.NullOrEmpty();
            float boxH = MeasureTileDetailsBoxHeight(tileValid, showProximityBlocked);
            Outpost_Dialog_UI.DrawOutcomeBox(new Rect(x, y, w, boxH));
            float cy = y + Outpost_Dialog_UI.OutcomeBoxPad;
            float ix = x + Outpost_Dialog_UI.OutcomeBoxPad;
            float iw = w - Outpost_Dialog_UI.OutcomeBoxPad * 2f;
            float valueX = ix + Outpost_Dialog_UI.OutcomeValueIndent;
            float valueW = iw - Outpost_Dialog_UI.OutcomeValueIndent;

            if (!tileValid)
            {
                GUI.color = requirementsPreviewOnly ? new Color(0.75f, 0.75f, 0.5f) : Color.yellow;
                Widgets.Label(new Rect(valueX, cy, valueW, Outpost_Dialog_UI.YieldLineH), invalidHint);
                GUI.color = Color.white;
            }
            else
            {
                if (showProximityBlocked)
                {
                    Rect blockedRect = new Rect(valueX, cy, valueW, Outpost_Dialog_UI.YieldLineH);
                    GUI.color = new Color(1f, 0.55f, 0.55f);
                    Widgets.Label(blockedRect, proximityBlockedHint);
                    GUI.color = Color.white;
                    if (!proximityBlockedTip.NullOrEmpty())
                        TooltipHandler.TipRegion(blockedRect, proximityBlockedTip);
                    cy += Outpost_Dialog_UI.YieldLineH;
                }

                Text.Font = GameFont.Small;
                cy = DrawTileStatLine(cy, valueX, valueW, colBiome, biomeName, biomeTip, Color.white);
                cy = DrawTileStatLine(cy, valueX, valueW, colTerrain, terrainVal, terrainTip, Color.white);
                cy = DrawTileStatLine(cy, valueX, valueW, colFertility, fertLabel, fertTip,
                    WorldTileProductivity.GetProductivityPercentDisplayColor(fertPct));
                cy = DrawTileStatLine(cy, valueX, valueW, colAnimals, animLabel, huntTip,
                    WorldTileProductivity.GetProductivityPercentDisplayColor(animPct));
                cy = DrawTileStatLine(cy, valueX, valueW, colFish, fishLabel, fishTip,
                    WorldTileProductivity.GetProductivityPercentDisplayColor(fishPct));
                DrawTileStatLine(cy, valueX, valueW, colMining, mineLabel, miningTip,
                    WorldTileProductivity.GetProductivityPercentDisplayColor(minePct));
                GUI.color = Color.white;
            }

            return y + boxH;
        }

        private static float DrawTileStatLine(float y, float x, float w, string label, string value, string tip, Color valueColor)
        {
            Rect rect = new Rect(x, y, w, Outpost_Dialog_UI.YieldLineH);
            string prefix = label + ": ";
            float prefixW = Text.CalcSize(prefix).x;
            GUI.color = Outpost_Dialog_UI.TheoreticalLabelColor;
            Widgets.Label(new Rect(x, y, prefixW, Outpost_Dialog_UI.YieldLineH), prefix);
            GUI.color = valueColor;
            Widgets.Label(new Rect(x + prefixW, y, w - prefixW, Outpost_Dialog_UI.YieldLineH), value);
            GUI.color = Color.white;
            if (!string.IsNullOrEmpty(tip))
                TooltipHandler.TipRegion(rect, tip);
            return y + Outpost_Dialog_UI.YieldLineH;
        }

        public static void DrawTierHeader(Rect rect, int tierVal)
        {
            Widgets.DrawLineHorizontal(rect.x, rect.y + rect.height - 3f, rect.width);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = new Color(0.9f, 0.85f, 0.6f);
            Widgets.Label(new Rect(rect.x + 8f, rect.y + 10f, rect.width - 16f, 20f),
                OutpostTranslationUtil.Key("TSA_WD_OutpostEstablish_TierHeader", tierVal.ToString()));
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        public static float MeasureSelectedDetailHeight(WorldObjectDef def, EstablishmentRowCache row, bool showCostColumn, bool hasDescription, bool requirementsPreviewOnly, float detailWidth)
        {
            if (def == null) return DetailLineH;
            float h = DetailTitleLineH + DetailIconSize + 4f + DetailSectionGap;
            string description = GetOutpostDescription(def);
            if (hasDescription && !description.NullOrEmpty())
                h += Mathf.Max(DetailCostLineH, Text.CalcHeight(description, detailWidth - DetailIconSize - 8f)) + 6f;
            if (requirementsPreviewOnly)
                h += MeasurePreviewStatusBoxHeight(detailWidth) + DetailSectionGap;
            else
                h += EstablishButtonH + DetailSectionGap;
            if (!string.IsNullOrEmpty(row.skillLine))
                h += MeasureSkillSectionHeight(row.skillLine, detailWidth);
            if (showCostColumn)
            {
                h += DetailLineH;
                int costLines = row.costItems != null && row.costItems.Length > 0 ? row.costItems.Length : 1;
                h += costLines * DetailCostLineH;
                h += DetailSectionGap;
            }
            else
            {
                h += DetailLineH + DetailCostLineH + DetailSectionGap;
            }
            h += DetailLineH;
            h += Mathf.Max(1, row.reqCount) * DetailCostLineH;
            return h;
        }

        public static float MeasurePreviewStatusBoxHeight(float width)
        {
            string text = OutpostTranslationUtil.Key("TSA_WD_OutpostEstablish_PreviewModeOnly");
            return Mathf.Max(PreviewStatusBoxMinH, Text.CalcHeight(text, width - 12f) + 12f);
        }

        public static float DrawPreviewModeStatusBox(float x, float y, float w)
        {
            string text = OutpostTranslationUtil.Key("TSA_WD_OutpostEstablish_PreviewModeOnly");
            float boxH = MeasurePreviewStatusBoxHeight(w);
            Rect boxRect = new Rect(x, y, w, boxH);
            Widgets.DrawBoxSolid(boxRect, RowBgPreview);
            Widgets.DrawBox(boxRect);
            GUI.color = Color.yellow;
            Text.Font = GameFont.Small;
            LabelAnchored(boxRect.ContractedBy(6f), text, TextAnchor.MiddleCenter);
            GUI.color = Color.white;
            return y + boxH;
        }

        public static float MeasureSkillSectionHeight(string skillLine, float width)
        {
            if (string.IsNullOrEmpty(skillLine)) return 0f;
            Text.Font = GameFont.Tiny;
            float bodyH = Mathf.Max(DetailCostLineH, Text.CalcHeight(skillLine, width));
            Text.Font = GameFont.Small;
            return DetailLineH + bodyH + DetailSectionGap;
        }

        public static float DrawSkillSection(float x, float y, float w, string skillsHeader, string skillLine, string skillTooltip)
        {
            if (string.IsNullOrEmpty(skillLine)) return y;

            GUI.color = Outpost_Dialog_UI.TheoreticalLabelColor;
            LabelAnchored(new Rect(x, y, w, DetailLineH), skillsHeader, TextAnchor.MiddleLeft);
            GUI.color = Color.white;
            y += DetailLineH;

            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            float bodyH = Mathf.Max(DetailCostLineH, Text.CalcHeight(skillLine, w));
            Rect bodyRect = new Rect(x, y, w, bodyH);
            Widgets.Label(bodyRect, skillLine);
            if (!string.IsNullOrEmpty(skillTooltip))
                TooltipHandler.TipRegion(bodyRect, skillTooltip);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            return y + bodyH + DetailSectionGap;
        }

        public static float DrawSelectedOutpostDetail(
            float x,
            float y,
            float w,
            WorldObjectDef def,
            EstablishmentRowCache row,
            bool showCostColumn,
            bool requirementsPreviewOnly,
            string costHeader,
            string noCostLabel,
            string skillsHeader,
            string requirementsHeader,
            string establishLabel,
            bool canEstablish,
            string blockReason,
            Action onEstablish)
        {
            if (def == null) return y;

            float top = y;
            Texture2D icon = def.ExpandingIconTexture;
            if (icon != null)
            {
                Rect iconRect = new Rect(x, y, DetailIconSize, DetailIconSize);
                GUI.color = Color.cyan;
                Widgets.DrawTextureFitted(iconRect.ContractedBy(3f), icon, 1f);
                GUI.color = Color.white;
                if (!string.IsNullOrEmpty(row.outpostTooltip))
                    TooltipHandler.TipRegion(iconRect, row.outpostTooltip);
            }

            float textX = x + DetailIconSize + 8f;
            float textW = w - DetailIconSize - 8f;
            LabelAnchored(new Rect(textX, y, textW, DetailTitleLineH), def.LabelCap, TextAnchor.MiddleLeft);
            y += DetailTitleLineH;
            string description = GetOutpostDescription(def);
            if (!description.NullOrEmpty())
            {
                Text.Font = GameFont.Tiny;
                GUI.color = Color.gray;
                float descH = Mathf.Max(DetailCostLineH, Text.CalcHeight(description, textW));
                Widgets.Label(new Rect(textX, y, textW, descH), description);
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
                y += descH + 6f;
            }
            else
            {
                y += 6f;
            }

            y = Mathf.Max(y, top + DetailIconSize + 4f);
            y += DetailSectionGap;

            if (requirementsPreviewOnly)
                y = DrawPreviewModeStatusBox(x, y, w) + DetailSectionGap;
            else
            {
                Rect actionRect = new Rect(x, y, w, EstablishButtonH);
                GUI.enabled = canEstablish;
                if (Widgets.ButtonText(actionRect, establishLabel))
                    onEstablish?.Invoke();
                GUI.enabled = true;
                if (!canEstablish && !string.IsNullOrEmpty(blockReason))
                    TooltipHandler.TipRegion(actionRect, blockReason);
                y += EstablishButtonH + DetailSectionGap;
            }

            y = DrawSkillSection(x, y, w, skillsHeader, row.skillLine, row.skillTooltip);

            GUI.color = Outpost_Dialog_UI.TheoreticalLabelColor;
            LabelAnchored(new Rect(x, y, w, DetailLineH), costHeader, TextAnchor.MiddleLeft);
            GUI.color = Color.white;
            y += DetailLineH;

            if (showCostColumn)
            {
                if (row.costItems != null && row.costItems.Length > 0)
                {
                    for (int i = 0; i < row.costItems.Length; i++)
                    {
                        var ci = row.costItems[i];
                        if (ci.thingDef == null) continue;
                        string line = "• " + ci.thingDef.LabelCap + " " + ci.countLabel;
                        if (ci.waived)
                            GUI.color = new Color(0.75f, 0.75f, 0.75f);
                        else if (ci.colorByAvailability)
                            GUI.color = ci.met
                                ? new Color(0.35f, 0.8f, 0.35f)
                                : new Color(1f, 0.35f, 0.35f);
                        LabelAnchored(new Rect(x, y, w, DetailCostLineH), line, TextAnchor.UpperLeft);
                        GUI.color = Color.white;
                        TooltipHandler.TipRegion(new Rect(x, y, w, DetailCostLineH), ci.tooltipLabel);
                        y += DetailCostLineH;
                    }
                }
                else
                {
                    LabelAnchored(new Rect(x, y, w, DetailCostLineH), "—", TextAnchor.UpperLeft);
                    y += DetailCostLineH;
                }
            }
            else
            {
                GUI.color = Color.gray;
                LabelAnchored(new Rect(x, y, w, DetailCostLineH), noCostLabel, TextAnchor.UpperLeft);
                GUI.color = Color.white;
                y += DetailCostLineH;
            }

            y += DetailSectionGap;
            GUI.color = Outpost_Dialog_UI.TheoreticalLabelColor;
            LabelAnchored(new Rect(x, y, w, DetailLineH), requirementsHeader, TextAnchor.MiddleLeft);
            GUI.color = Color.white;
            y += DetailLineH;

            if (row.reqCount == 0)
            {
                LabelAnchored(new Rect(x, y, w, DetailCostLineH), "—", TextAnchor.UpperLeft);
                y += DetailCostLineH;
            }
            else
            {
                for (int li = 0; li < 9; li++)
                {
                    if (!row.reqApplies[li]) continue;
                    y = DrawRequirementLine(x, y, w, row.reqs[li]);
                }
            }

            return y;
        }

        public static float DrawRequirementLine(float x, float y, float w, EstablishmentReqLine req)
        {
            Rect lineRect = new Rect(x, y, w, DetailCostLineH);
            if (!string.IsNullOrEmpty(req.tooltip))
                TooltipHandler.TipRegion(lineRect, req.tooltip);
            GUI.color = req.useFixedColor ? req.color : (req.met ? new Color(0.35f, 0.8f, 0.35f) : new Color(1f, 0.35f, 0.35f));
            Widgets.Label(lineRect, "• " + req.text);
            GUI.color = Color.white;
            return y + DetailCostLineH;
        }

        private static void LabelAnchored(Rect rect, string text, TextAnchor anchor)
        {
            Text.Anchor = anchor;
            Widgets.Label(rect, text);
            Text.Anchor = TextAnchor.UpperLeft;
        }
    }
}
