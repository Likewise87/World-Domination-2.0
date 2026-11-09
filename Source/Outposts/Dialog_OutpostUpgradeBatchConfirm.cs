using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Confirm prepaid multi-select outpost upgrades with per-line costs, pods, and total.</summary>
    public class Dialog_OutpostUpgradeBatchConfirm : Window
    {
        private const float SectionGap = 10f;
        private const float LeadIconSize = 36f;
        private const float LeadGap = 8f;
        private const float CostIconSize = 20f;
        private const float CostLineH = 22f;
        private const float UpgradeNameH = 26f;
        /// <summary>Matches <see cref="SettingsUI.DrawCollapsibleHeader"/> title row height.</summary>
        private const float SectionHeaderH = 26f;
        private const float TotalBoxPad = 8f;
        private const float SeparatorH = 8f;
        /// <summary>Shared left edge for upgrade names, cost icons, and pod cost text.</summary>
        private const float CostColX = LeadIconSize + LeadGap;

        /// <summary>Same cyan as settings section titles (<see cref="SettingsUI.SectionHeaderColor"/>).</summary>
        private static readonly Color SectionHeaderColor = SettingsUI.SectionHeaderColor;
        private static readonly Color CostTextColor = Color.gray;
        private static readonly Color SeparatorColor = new Color(1f, 1f, 1f, 0.22f);
        private static readonly Color TotalBoxBg = new Color(0.12f, 0.14f, 0.16f, 0.55f);
        private static Texture2D cachedDropPodIcon;

        private readonly WorldObject_WD_Outpost outpost;
        private readonly OutpostUpgradeUtility.BatchPurchaseCheck batch;
        private readonly Action onLaunched;
        private Vector2 bodyScroll;
        private List<OutpostUpgradeCostEntry> cachedMergedTotal;

        private static Texture2D DropPodIcon
        {
            get
            {
                if (cachedDropPodIcon != null) return cachedDropPodIcon;
                cachedDropPodIcon = WITab_Outpost_WarehouseAssets.DropPodIcon
                    ?? ContentFinder<Texture2D>.Get("WorldObjects/Travelers/DropPod_OutpostUpgrade", false)
                    ?? ContentFinder<Texture2D>.Get("WorldObjects/Travelers/DropPod_OutpostGoods", false);
                return cachedDropPodIcon;
            }
        }

        public override Vector2 InitialSize => new Vector2(540f, 560f);

        public Dialog_OutpostUpgradeBatchConfirm(
            WorldObject_WD_Outpost outpost,
            OutpostUpgradeUtility.BatchPurchaseCheck batch,
            Action onLaunched = null)
        {
            this.outpost = outpost;
            this.batch = batch;
            this.onLaunched = onLaunched;
            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
        }

        public override void DoWindowContents(Rect inRect)
        {
            float buttonsH = CloseButSize.y + 12f;
            Rect body = new Rect(0f, 0f, inRect.width, inRect.height - buttonsH);

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, body.width, 32f),
                "TSA_WD_OutpostUpgrades_BatchTitle".Translate(outpost?.LabelCap ?? ""));
            Text.Font = GameFont.Small;

            float contentTop = 36f;
            if (!string.IsNullOrEmpty(batch?.sameLineDroppedNote))
            {
                GUI.color = new Color(0.95f, 0.75f, 0.35f);
                float noteH = Text.CalcHeight(batch.sameLineDroppedNote, body.width);
                Widgets.Label(new Rect(0f, contentTop, body.width, noteH), batch.sameLineDroppedNote);
                GUI.color = Color.white;
                contentTop += noteH + 6f;
            }

            float contentH = MeasureContentHeight(body.width - 16f);
            Rect scrollOuter = new Rect(0f, contentTop, body.width, Mathf.Max(40f, body.yMax - contentTop));
            Rect scrollView = new Rect(0f, 0f, body.width - 16f, Mathf.Max(contentH, scrollOuter.height));
            Widgets.BeginScrollView(scrollOuter, ref bodyScroll, scrollView);
            DrawScrollContent(scrollView.width);
            Widgets.EndScrollView();

            int acceptCount = batch?.accepted?.Count ?? 0;
            Rect cancelRect = new Rect(0f, inRect.height - CloseButSize.y, CloseButSize.x, CloseButSize.y);
            Rect confirmRect = new Rect(inRect.width - CloseButSize.x, inRect.height - CloseButSize.y, CloseButSize.x, CloseButSize.y);
            if (Widgets.ButtonText(cancelRect, "Cancel".Translate()))
                Close();

            bool canConfirm = batch != null && batch.canBuy && acceptCount > 0;
            GUI.enabled = canConfirm;
            if (Widgets.ButtonText(confirmRect, "TSA_WD_OutpostUpgrades_BatchConfirm".Translate()))
                TryConfirm();
            GUI.enabled = true;
            if (!canConfirm && !string.IsNullOrEmpty(batch?.reason))
                TooltipHandler.TipRegion(confirmRect, batch.reason);
        }

        private float MeasureContentHeight(float width)
        {
            float y = 0f;
            y += SectionHeaderH + 4f;
            if (batch?.accepted != null)
            {
                for (int i = 0; i < batch.accepted.Count; i++)
                {
                    OutpostUpgradeDef def = batch.accepted[i];
                    if (def == null) continue;
                    if (i > 0) y += SeparatorH;
                    y += MeasureUpgradeBlockHeight(def);
                }
            }
            if (HasPodCost)
            {
                if (batch?.accepted != null && batch.accepted.Count > 0)
                    y += SeparatorH;
                y += MeasurePodTransportBlockHeight();
            }
            y += SectionGap;
            y += SectionHeaderH + 4f;
            y += MeasureTotalBlockHeight();
            return y + 4f;
        }

        private static float MeasureUpgradeBlockHeight(OutpostUpgradeDef def)
        {
            int costLines = Mathf.Max(1, CountCostLines(def?.GetEffectiveCost()));
            float textColH = UpgradeNameH + costLines * CostLineH;
            return Mathf.Max(LeadIconSize + 4f, textColH);
        }

        private static float MeasurePodTransportBlockHeight()
        {
            float textColH = UpgradeNameH + CostLineH;
            return Mathf.Max(LeadIconSize + 4f, textColH);
        }

        private float MeasureTotalBlockHeight()
        {
            int lines = Mathf.Max(1, CountCostLines(GetMergedTotalCosts()));
            float costsH = TotalBoxPad * 2f + lines * CostLineH;
            return Mathf.Max(LeadIconSize + TotalBoxPad * 2f, costsH);
        }

        private bool HasPodCost =>
            batch != null && batch.originUsesDropPod && batch.launchCount > 0;

        private int PodComponentCount =>
            HasPodCost
                ? Mathf.Max(0, batch.launchCount) * PlayerPawnDropPodUtility.ComponentCostPerLaunch
                : 0;

        private List<OutpostUpgradeCostEntry> GetMergedTotalCosts()
        {
            if (cachedMergedTotal != null) return cachedMergedTotal;
            cachedMergedTotal = new List<OutpostUpgradeCostEntry>();
            CloneCostsInto(cachedMergedTotal, batch?.aggregatedCost);
            int podComps = PodComponentCount;
            if (podComps > 0)
            {
                ThingDef comp = ThingDefOf.ComponentIndustrial;
                if (comp != null)
                {
                    OutpostUpgradeCostEntry match = FindCost(cachedMergedTotal, comp, stone: false);
                    if (match != null)
                        match.count += podComps;
                    else
                    {
                        cachedMergedTotal.Add(new OutpostUpgradeCostEntry
                        {
                            thingDef = comp,
                            count = podComps,
                            costMode = OutpostUpgradeCostMode.SpecificThingDef
                        });
                    }
                }
            }
            return cachedMergedTotal;
        }

        private static void CloneCostsInto(List<OutpostUpgradeCostEntry> dest, List<OutpostUpgradeCostEntry> src)
        {
            if (dest == null || src == null) return;
            for (int i = 0; i < src.Count; i++)
            {
                OutpostUpgradeCostEntry c = src[i];
                if (c == null || c.count <= 0) continue;
                if (!ColonyWorldBuildMaterials.IsAnyStoneBlocksCost(c) && c.thingDef == null) continue;
                dest.Add(new OutpostUpgradeCostEntry
                {
                    thingDef = c.thingDef,
                    count = c.count,
                    costMode = c.costMode
                });
            }
        }

        private static OutpostUpgradeCostEntry FindCost(List<OutpostUpgradeCostEntry> list, ThingDef thingDef, bool stone)
        {
            if (list == null) return null;
            for (int i = 0; i < list.Count; i++)
            {
                OutpostUpgradeCostEntry d = list[i];
                if (d == null) continue;
                bool dStone = ColonyWorldBuildMaterials.IsAnyStoneBlocksCost(d);
                if (stone && dStone) return d;
                if (!stone && !dStone && d.thingDef == thingDef) return d;
            }
            return null;
        }

        private static int CountCostLines(List<OutpostUpgradeCostEntry> costs)
        {
            if (costs == null) return 0;
            int n = 0;
            for (int i = 0; i < costs.Count; i++)
            {
                OutpostUpgradeCostEntry c = costs[i];
                if (c == null || c.count <= 0) continue;
                if (!ColonyWorldBuildMaterials.IsAnyStoneBlocksCost(c) && c.thingDef == null) continue;
                n++;
            }
            return n;
        }

        private void DrawScrollContent(float width)
        {
            float y = 0f;
            y = DrawSectionHeader(0f, y, width, "TSA_WD_OutpostUpgrades_BatchListHeader".Translate());

            if (batch?.accepted != null)
            {
                for (int i = 0; i < batch.accepted.Count; i++)
                {
                    OutpostUpgradeDef def = batch.accepted[i];
                    if (def == null) continue;
                    if (i > 0)
                    {
                        GUI.color = SeparatorColor;
                        Widgets.DrawLineHorizontal(0f, y + SeparatorH * 0.5f, width);
                        GUI.color = Color.white;
                        y += SeparatorH;
                    }
                    y = DrawUpgradeBlock(0f, y, width, def);
                }
            }

            if (HasPodCost)
            {
                bool hadUpgrades = false;
                if (batch?.accepted != null)
                {
                    for (int i = 0; i < batch.accepted.Count; i++)
                    {
                        if (batch.accepted[i] != null) { hadUpgrades = true; break; }
                    }
                }
                if (hadUpgrades)
                {
                    GUI.color = SeparatorColor;
                    Widgets.DrawLineHorizontal(0f, y + SeparatorH * 0.5f, width);
                    GUI.color = Color.white;
                    y += SeparatorH;
                }
                y = DrawPodTransportBlock(0f, y, width, PodComponentCount);
            }

            y += SectionGap;
            y = DrawSectionHeader(0f, y, width, "TSA_WD_OutpostUpgrades_BatchTotalHeader".Translate());

            float totalH = MeasureTotalBlockHeight();
            Rect totalRect = new Rect(0f, y, width, totalH);
            Widgets.DrawBoxSolid(totalRect, TotalBoxBg);
            Widgets.DrawBox(totalRect);
            DrawTotalBlockContents(totalRect);
        }

        private static float DrawSectionHeader(float x, float y, float width, string label)
        {
            // Non-collapsible settings section title: Small + bold + cyan.
            string hex = ColorUtility.ToHtmlStringRGBA(SectionHeaderColor);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(x, y, width, SectionHeaderH), $"<b><color=#{hex}>{label}</color></b>");
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            return y + SectionHeaderH + 4f;
        }

        private float DrawUpgradeBlock(float x, float y, float width, OutpostUpgradeDef def)
        {
            float blockH = MeasureUpgradeBlockHeight(def);
            Rect iconRect = new Rect(x, y + 2f, LeadIconSize, LeadIconSize);
            Texture2D icon = Outpost_Upgrade_UI.GetUpgradeIcon(def);
            if (icon != null)
                Outpost_Upgrade_UI.DrawTextureTopFit(iconRect.ContractedBy(1f), icon);

            float textX = x + CostColX;
            float textW = width - CostColX;
            float ty = y;

            string label = def.LabelCap;
            if (def.lineTier > 1)
                label += " (Lv " + def.lineTier + ")";
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(textX, ty, textW, UpgradeNameH), label);
            Text.Anchor = TextAnchor.UpperLeft;
            ty += UpgradeNameH;

            List<OutpostUpgradeCostEntry> costs = def.GetEffectiveCost();
            if (CountCostLines(costs) == 0)
            {
                Text.Font = GameFont.Tiny;
                GUI.color = CostTextColor;
                Widgets.Label(new Rect(textX, ty, textW, CostLineH),
                    "TSA_WD_OutpostUpgrades_NoCost".Translate());
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
            }
            else
            {
                DrawCostLines(textX, ty, textW, costs);
            }

            return y + blockH;
        }

        /// <summary>Same layout as an upgrade block: lead icon, Small name, Tiny gray cost with ThingIcon.</summary>
        private float DrawPodTransportBlock(float x, float y, float width, int componentCount)
        {
            float blockH = MeasurePodTransportBlockHeight();
            Rect iconRect = new Rect(x, y + 2f, LeadIconSize, LeadIconSize);
            if (DropPodIcon != null)
            {
                Color prev = GUI.color;
                GUI.color = WorldOverlayLineMaterials.DarkCyanColor;
                GUI.DrawTexture(iconRect, DropPodIcon, ScaleMode.ScaleToFit);
                GUI.color = prev;
            }

            float textX = x + CostColX;
            float textW = width - CostColX;
            float ty = y;

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(textX, ty, textW, UpgradeNameH),
                "TSA_WD_OutpostUpgrades_BatchPodTitle".Translate());
            Text.Anchor = TextAnchor.UpperLeft;
            ty += UpgradeNameH;

            ThingDef comp = ThingDefOf.ComponentIndustrial;
            Rect costIcon = new Rect(
                textX,
                ty + (CostLineH - CostIconSize) * 0.5f,
                CostIconSize,
                CostIconSize);
            if (comp != null)
                Widgets.ThingIcon(costIcon, comp);

            Text.Font = GameFont.Tiny;
            GUI.color = CostTextColor;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(
                new Rect(textX + CostIconSize + 6f, ty, textW - CostIconSize - 6f, CostLineH),
                FormatComponentsLabel(componentCount));
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            return y + blockH;
        }

        private void DrawTotalBlockContents(Rect box)
        {
            // Same horizontal inset left of the outpost icon and right of the cost column.
            float pad = TotalBoxPad;
            float contentLeft = box.x + pad;
            Texture2D outpostIcon = outpost?.ExpandingIcon ?? outpost?.def?.ExpandingIconTexture;
            if (outpostIcon != null)
            {
                Rect iconRect = new Rect(
                    contentLeft,
                    box.y + (box.height - LeadIconSize) * 0.5f,
                    LeadIconSize,
                    LeadIconSize);
                Color prev = GUI.color;
                GUI.color = outpost?.Faction?.Color ?? Color.white;
                GUI.DrawTexture(iconRect.ContractedBy(1f), outpostIcon, ScaleMode.ScaleToFit);
                GUI.color = prev;
            }

            float x = contentLeft + CostColX;
            float y = box.y + pad;
            float w = box.width - pad * 2f - CostColX;
            List<OutpostUpgradeCostEntry> costs = GetMergedTotalCosts();
            if (CountCostLines(costs) == 0)
            {
                Text.Font = GameFont.Tiny;
                GUI.color = CostTextColor;
                Widgets.Label(new Rect(x, y, w, CostLineH), "TSA_WD_OutpostUpgrades_NoCost".Translate());
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
                return;
            }
            DrawCostLines(x, y, w, costs);
        }

        private static float DrawCostLines(float x, float y, float width, List<OutpostUpgradeCostEntry> costs)
        {
            if (costs == null) return y;
            Text.Font = GameFont.Tiny;
            for (int i = 0; i < costs.Count; i++)
            {
                OutpostUpgradeCostEntry c = costs[i];
                if (c == null || c.count <= 0) continue;
                if (!ColonyWorldBuildMaterials.IsAnyStoneBlocksCost(c) && c.thingDef == null) continue;

                ThingDef iconDef = ColonyWorldBuildMaterials.GetCostIconThingDef(c);
                Rect iconRect = new Rect(x, y + (CostLineH - CostIconSize) * 0.5f, CostIconSize, CostIconSize);
                if (iconDef != null)
                    Widgets.ThingIcon(iconRect, iconDef);

                GUI.color = CostTextColor;
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(
                    new Rect(x + CostIconSize + 6f, y, width - CostIconSize - 6f, CostLineH),
                    FormatCostLabel(c));
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
                y += CostLineH;
            }
            Text.Font = GameFont.Small;
            return y;
        }

        private static string FormatCostLabel(OutpostUpgradeCostEntry c)
        {
            if (c == null) return "";
            if (c.thingDef == ThingDefOf.ComponentIndustrial)
                return FormatComponentsLabel(c.count);
            return ColonyWorldBuildMaterials.GetCostDisplayLabel(c) + " x" + c.count;
        }

        private static string FormatComponentsLabel(int count) =>
            "TSA_WD_OutpostUpgrades_ComponentsLabel".Translate().ToString() + " x" + count;

        private void TryConfirm()
        {
            if (outpost == null || batch == null || !batch.canBuy) return;

            Action launch = () =>
            {
                if (!OutpostUpgradeUtility.TryPurchaseBatchAndLaunch(outpost, batch, out string failReason))
                {
                    Messages.Message(
                        failReason ?? "TSA_WD_OutpostUpgrades_UnableBuy".Translate(),
                        MessageTypeDefOf.RejectInput);
                    return;
                }
                onLaunched?.Invoke();
                Close();
            };

            WorldObject origin = batch.origin;
            if (batch.originUsesDropPod
                && origin != null
                && PlayerPawnDropPodUtility.ConfirmHostileAaThen(origin.Tile.tileId, outpost.Tile.tileId, launch))
                return;
            launch();
        }
    }
}
