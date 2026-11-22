using UnityEngine;
using Verse;
using RimWorld;

namespace TSA_WorldDomination
{
    public class Window_CaravanClashDetails : Window
    {
        private readonly SpreadLogEntry entry;

        public override Vector2 InitialSize => new Vector2(640f, 440f);

        public Window_CaravanClashDetails(SpreadLogEntry entry)
        {
            this.entry = entry;
            doCloseX = true;
            draggable = true;
            absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            float sideABefore = Mathf.Max(0f, entry?.attStr ?? 0f);
            float sideBBefore = Mathf.Max(0f, entry?.defStr ?? 0f);
            bool sideAWon = entry?.victory ?? false;
            float winnerLossPct = RaidUIUtils.GetOpenFieldWinnerLossPct(entry);
            BattleMarginTier winTier = RaidUIUtils.GetOpenFieldWinnerTier(entry);

            float sideAAfter = sideAWon ? sideABefore * (1f - winnerLossPct) : 0f;
            float sideBAfter = sideAWon ? 0f : sideBBefore * (1f - winnerLossPct);
            string labelA = entry?.labelA ?? "?";
            string labelB = entry?.labelB ?? "?";
            string winnerLabel = sideAWon ? labelA : labelB;
            string loserLabel = sideAWon ? labelB : labelA;
            float winnerBefore = sideAWon ? sideABefore : sideBBefore;
            float winnerAfter = sideAWon ? sideAAfter : sideBAfter;
            float winnerLost = Mathf.Max(0f, winnerBefore - winnerAfter);

            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);

            Text.Font = GameFont.Medium;
            listing.Label("TSA_WD_CaravanClash_Header".Translate());
            Text.Font = GameFont.Small;
            listing.GapLine();

            listing.Label("TSA_WD_CaravanClash_Intro".Translate());
            listing.Gap(8f);

            Rect powers = listing.GetRect(70f);
            RaidUIUtils.DrawPowerBoxesRaw(powers, sideABefore, sideBBefore, labelA, labelB);

            listing.Gap(10f);
            float ratio = entry?.ratio ?? (sideBBefore > 0f ? sideABefore / sideBBefore : sideABefore);
            listing.Label("TSA_WD_CaravanClash_Ratio".Translate(ratio.ToString("F2")));
            listing.Label("TSA_WD_WinChance".Translate() + ": " + Mathf.Clamp01(entry?.winChance ?? 0f).ToStringPercent());

            listing.Gap(6f);
            Rect winBar = listing.GetRect(22f);
            RaidUIUtils.DrawWinChanceBar(winBar, Mathf.Clamp01(entry?.winChance ?? 0f));

            listing.Gap(8f);
            Text.Font = GameFont.Medium;
            listing.Label("TSA_WD_CaravanClash_Winner".Translate(winnerLabel).Colorize(Color.green));
            Text.Font = GameFont.Small;
            listing.Gap(6f);

            string winType = RaidUIUtils.FormatOpenFieldWinType(winTier);
            listing.Label(winType.Colorize(RaidUIUtils.GetMarginTierColor(winTier, isWinOutcome: true)));
            listing.Label("TSA_WD_CaravanClash_WinnerLoss".Translate(
                (winnerLossPct * 100f).ToString("F0"),
                winnerLost.ToString("F0"),
                winnerBefore.ToString("F0"),
                winnerAfter.ToString("F0")));
            listing.Label("TSA_WD_CaravanClash_LoserWiped".Translate(loserLabel)
                .Colorize(ColorLibrary.RedReadable));

            listing.Gap(10f);
            listing.Label("TSA_WD_CaravanClash_ResultingStrengths".Translate());
            listing.Label($"{labelA}: {sideAAfter:F0}");
            listing.Label($"{labelB}: {sideBAfter:F0}");

            listing.Gap(12f);
            if (Widgets.ButtonText(listing.GetRect(30f), "Close".Translate()))
                Close();

            listing.End();
            Text.Anchor = TextAnchor.UpperLeft;
        }
    }
}
