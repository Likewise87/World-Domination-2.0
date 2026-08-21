using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    public class Dialog_ConfirmCancelOrderedRoad : Window
    {
        private const float RowGap = 6f;

        private readonly Settlement builder;
        private readonly CompViralSpread comp;

        public override Vector2 InitialSize => new Vector2(480f, 240f);

        public Dialog_ConfirmCancelOrderedRoad(Settlement builder, CompViralSpread comp)
        {
            this.builder = builder;
            this.comp = comp;
            doCloseX = true;
            absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            int built = WorldActions_Roads.GetBuiltSegmentCount(comp);
            int remaining = WorldActions_Roads.CountRemainingWorkSegments(comp);
            int refund = WorldDominationSettings.CalcOrderedRoadRefund(comp.playerOrderedRoadPerSegmentRate, remaining);

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 32f), "TSA_WD_OrderedRoad_CancelTitle".Translate());
            Text.Font = GameFont.Small;

            Rect listRect = new Rect(0f, 36f, inRect.width, inRect.height - 36f - CloseButSize.y - 12f);
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(listRect);

            DrawRow(listing, "TSA_WD_OrderedRoad_CancelBuilt".Translate(built, comp.playerOrderedRoadInitialSegments));
            DrawRow(listing, "TSA_WD_OrderedRoad_CancelRefund".Translate(refund, remaining));

            listing.End();

            Rect noRect = new Rect(0f, inRect.height - CloseButSize.y, CloseButSize.x, CloseButSize.y);
            Rect yesRect = new Rect(inRect.width - CloseButSize.x, inRect.height - CloseButSize.y, CloseButSize.x, CloseButSize.y);
            if (Widgets.ButtonText(noRect, "CancelButton".Translate()))
                Close();
            if (Widgets.ButtonText(yesRect, "TSA_WD_OrderedRoad_CancelConfirm".Translate()))
            {
                WorldActions_Roads.ClearRoadProject(comp, RoadProjectClearReason.PlayerCancel);
                Messages.Message("TSA_WD_OrderedRoad_Cancelled".Translate(builder.LabelCap), MessageTypeDefOf.NeutralEvent);
                Close();
            }
        }

        private static void DrawRow(Listing_Standard listing, string text)
        {
            listing.Gap(RowGap);
            Widgets.Label(listing.GetRect(Text.LineHeight), text);
        }
    }
}
