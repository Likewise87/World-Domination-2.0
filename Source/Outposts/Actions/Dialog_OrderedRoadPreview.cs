using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    public class Dialog_OrderedRoadPreview : Window
    {
        private readonly Settlement builder;
        private readonly CompViralSpread comp;
        private readonly RoadTargetSelection selection;
        private readonly SettlementTier roadTier;

        private readonly float perSegmentRate;
        private readonly int totalCost;
        private readonly float estDaysPerSegment;
        private readonly List<OutpostStatsSection> sections = new List<OutpostStatsSection>();

        private const float BoxGap = 12f;

        public override Vector2 InitialSize => new Vector2(480f, 420f);

        public Dialog_OrderedRoadPreview(Settlement builder, CompViralSpread comp, RoadTargetSelection selection, SettlementTier roadTier)
        {
            this.builder = builder;
            this.comp = comp;
            this.selection = selection;
            this.roadTier = roadTier;
            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;

            var seth = WorldDominationMod.settings;
            seth.GetOrderedRoadGoodwillCostBreakdown(roadTier, selection.SegmentCount, out perSegmentRate, out totalCost);
            float workSpeed = WorldActions_Roads.GetAssumedConstructionForSettlementTier(comp.tier);
            float ticks = workSpeed > 0.01f
                ? WorldActions_Roads.GetRoadProgressRequiredTicks(roadTier) / workSpeed
                : -1f;
            estDaysPerSegment = ticks > 0f ? ticks / GenDate.TicksPerDay : -1f;

            BuildSections();
        }

        public override void DoWindowContents(Rect inRect)
        {
            float buttonsH = CloseButSize.y + 12f;
            Rect body = new Rect(0f, 0f, inRect.width, inRect.height - buttonsH);

            OutpostTabStatsUi.DrawHeadline(body, "TSA_WD_OrderedRoad_PreviewTitle".Translate(builder.LabelCap));

            float y = OutpostTabStatsUi.TabHeaderConsumedHeight;
            if (sections.Count > 0)
                y = OutpostTabStatsUi.DrawHighlightedKeyValueSection(body.x, y, body.width, sections[0]);
            if (sections.Count > 1)
            {
                y += BoxGap;
                OutpostTabStatsUi.DrawKeyValueRows(body.x, y, body.width, sections[1], zebraStriping: false);
            }

            bool canPay = GoodwillChangeNotifier.CanPayOrderedRoadCost(builder.Faction, totalCost, OrderedRoadUtility.GoodwillFloor);

            Rect cancelRect = new Rect(0f, inRect.height - CloseButSize.y, CloseButSize.x, CloseButSize.y);
            Rect confirmRect = new Rect(inRect.width - CloseButSize.x, inRect.height - CloseButSize.y, CloseButSize.x, CloseButSize.y);
            if (Widgets.ButtonText(cancelRect, "Cancel".Translate()))
                Close();
            if (canPay && Widgets.ButtonText(confirmRect, "TSA_WD_OrderedRoad_Confirm".Translate()))
            {
                if (GoodwillChangeNotifier.TryPayOrderedRoadOrder(builder.Faction, builder, ResolveTargetObject(), totalCost, out _))
                {
                    OrderedRoadUtility.ApplyPlayerOrderedRoadProject(builder, comp, selection, totalCost, perSegmentRate);
                    Messages.Message("TSA_WD_OrderedRoad_Started".Translate(builder.LabelCap, selection.TargetName), builder, MessageTypeDefOf.TaskCompletion);
                    Close();
                }
            }
        }

        private void BuildSections()
        {
            sections.Clear();

            int goodwill = GoodwillChangeNotifier.GetPlayerGoodwill(builder.Faction);
            bool canPay = GoodwillChangeNotifier.CanPayOrderedRoadCost(builder.Faction, totalCost, OrderedRoadUtility.GoodwillFloor);

            var cost = new OutpostStatsSection { Title = "", FullWidth = true };
            AddRow(cost, "TSA_WD_OrderedRoad_PreviewLabelTotal",
                "TSA_WD_OrderedRoad_PreviewTotalValue".Translate(totalCost).ToString());
            AddRow(cost, "TSA_WD_OrderedRoad_PreviewLabelCurrentGoodwill", goodwill.ToString());
            if (!canPay)
            {
                cost.Rows.Add(new OutpostStatRow
                {
                    Label = "TSA_WD_OrderedRoad_PreviewLabelCannotAfford".Translate(),
                    Value = "TSA_WD_OrderedRoad_PreviewCannotAfford".Translate(totalCost, goodwill, OrderedRoadUtility.GoodwillFloor).ToString(),
                    ValueColor = Color.yellow,
                    WrapValue = true
                });
            }
            sections.Add(cost);

            var details = new OutpostStatsSection { Title = "", FullWidth = true };
            AddRow(details, "TSA_WD_OrderedRoad_PreviewLabelRoadType", WorldActions_Roads.GetRoadTierLabel(roadTier));
            AddRow(details, "TSA_WD_OrderedRoad_PreviewLabelSegments", selection.SegmentCount.ToString());
            AddRow(details, "TSA_WD_OrderedRoad_PreviewLabelDaysPerSegment",
                estDaysPerSegment > 0f ? estDaysPerSegment.ToString("F2") : "-");
            AddRow(details, "TSA_WD_OrderedRoad_PreviewLabelPerSegment", perSegmentRate.ToString("0.##"));
            sections.Add(details);
        }

        private static void AddRow(OutpostStatsSection section, string labelKey, string value)
        {
            section.Rows.Add(new OutpostStatRow
            {
                Label = labelKey.Translate().ToString(),
                Value = value ?? "-"
            });
        }

        private WorldObject ResolveTargetObject()
        {
            return Find.WorldObjects.ObjectsAt(selection.TargetTile)
                .FirstOrDefault(x => x is Settlement || x is WorldObject_WD_Outpost) ?? builder;
        }
    }
}
