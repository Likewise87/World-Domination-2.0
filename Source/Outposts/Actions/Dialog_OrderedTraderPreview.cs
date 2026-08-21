using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    public class Dialog_OrderedTraderPreview : Window
    {
        private readonly Settlement sender;
        private readonly TraderKindDef traderKind;
        private readonly Settlement destination;
        private readonly int totalCost;
        private readonly List<OutpostStatsSection> sections = new List<OutpostStatsSection>();

        private const float BoxGap = 12f;

        public override Vector2 InitialSize => new Vector2(480f, 320f);

        public Dialog_OrderedTraderPreview(Settlement sender, TraderKindDef traderKind)
        {
            this.sender = sender;
            this.traderKind = traderKind;
            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;

            destination = OrderedTraderUtility.FindNearestPlayerColonyWithMap(sender.Tile);
            totalCost = OrderedTraderUtility.GetOrderCost();
            BuildSections();
        }

        public override void DoWindowContents(Rect inRect)
        {
            float buttonsH = CloseButSize.y + 12f;
            Rect body = new Rect(0f, 0f, inRect.width, inRect.height - buttonsH);

            OutpostTabStatsUi.DrawHeadline(body, "TSA_WD_OrderedTrader_PreviewTitle".Translate(sender.LabelCap));

            float y = OutpostTabStatsUi.TabHeaderConsumedHeight;
            if (sections.Count > 0)
                y = OutpostTabStatsUi.DrawHighlightedKeyValueSection(body.x, y, body.width, sections[0]);
            if (sections.Count > 1)
            {
                y += BoxGap;
                OutpostTabStatsUi.DrawKeyValueRows(body.x, y, body.width, sections[1], zebraStriping: false);
            }

            bool canPay = destination != null
                && (totalCost <= 0 || GoodwillChangeNotifier.CanPayOrderedRoadCost(sender.Faction, totalCost, OrderedTraderUtility.GoodwillFloor));

            Rect cancelRect = new Rect(0f, inRect.height - CloseButSize.y, CloseButSize.x, CloseButSize.y);
            Rect confirmRect = new Rect(inRect.width - CloseButSize.x, inRect.height - CloseButSize.y, CloseButSize.x, CloseButSize.y);
            if (Widgets.ButtonText(cancelRect, "Cancel".Translate()))
                Close();
            if (canPay && Widgets.ButtonText(confirmRect, "TSA_WD_OrderedTrader_Confirm".Translate()))
            {
                if (destination == null)
                {
                    Messages.Message("TSA_WD_OrderedTrader_NoColony".Translate(), MessageTypeDefOf.RejectInput);
                    Close();
                    return;
                }

                if (totalCost > 0
                    && !GoodwillChangeNotifier.TryPayOrderedTraderOrder(sender.Faction, sender, destination, traderKind, totalCost, out _))
                {
                    return;
                }

                if (OrderedTraderUtility.LaunchPlayerOrderedTrader(sender, traderKind, destination))
                {
                    Messages.Message(
                        "TSA_WD_OrderedTrader_Started".Translate(sender.LabelCap, destination.LabelCap, traderKind.LabelCap),
                        sender,
                        MessageTypeDefOf.TaskCompletion);
                    Close();
                }
                else
                {
                    Messages.Message("TSA_WD_OrderedTrader_LaunchFailed".Translate(), MessageTypeDefOf.RejectInput);
                }
            }
        }

        private void BuildSections()
        {
            sections.Clear();

            int goodwill = GoodwillChangeNotifier.GetPlayerGoodwill(sender.Faction);
            bool canPay = destination != null
                && (totalCost <= 0 || GoodwillChangeNotifier.CanPayOrderedRoadCost(sender.Faction, totalCost, OrderedTraderUtility.GoodwillFloor));

            var cost = new OutpostStatsSection { Title = "", FullWidth = true };
            AddRow(cost, "TSA_WD_OrderedTrader_PreviewLabelTotal",
                "TSA_WD_OrderedTrader_PreviewTotalValue".Translate(totalCost).ToString());
            AddRow(cost, "TSA_WD_OrderedTrader_PreviewLabelCurrentGoodwill", goodwill.ToString());
            if (!canPay && destination != null)
            {
                cost.Rows.Add(new OutpostStatRow
                {
                    Label = "TSA_WD_OrderedTrader_PreviewLabelCannotAfford".Translate(),
                    Value = "TSA_WD_OrderedTrader_PreviewCannotAfford".Translate(
                        totalCost, goodwill, OrderedTraderUtility.GoodwillFloor).ToString(),
                    ValueColor = Color.yellow,
                    WrapValue = true
                });
            }
            sections.Add(cost);

            var details = new OutpostStatsSection { Title = "", FullWidth = true };
            AddRow(details, "TSA_WD_OrderedTrader_PreviewLabelKind", traderKind?.LabelCap ?? "-");
            AddRow(details, "TSA_WD_OrderedTrader_PreviewLabelDestination",
                destination != null ? destination.LabelCap.ToString() : "TSA_WD_OrderedTrader_NoColony".Translate().ToString());
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
    }
}
