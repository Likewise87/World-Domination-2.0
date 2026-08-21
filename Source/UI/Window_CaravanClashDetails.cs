using UnityEngine;
using Verse;
using RimWorld;

namespace TSA_WorldDomination
{
    public class Window_CaravanClashDetails : Window
    {
        private readonly SpreadLogEntry entry;

        public override Vector2 InitialSize => new Vector2(640f, 420f);

        public Window_CaravanClashDetails(SpreadLogEntry entry)
        {
            this.entry = entry;
            doCloseX = true;
            draggable = true;
            absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            float attBefore = Mathf.Max(0f, entry?.attStr ?? 0f);
            float defBefore = Mathf.Max(0f, entry?.defStr ?? 0f);
            bool attWon = entry?.victory ?? false;
            float attAfter = attWon ? attBefore * (1f - Mathf.Clamp01(entry?.attLossPct ?? 0f)) : 0f;
            float defAfter = attWon ? 0f : defBefore * (1f - Mathf.Clamp01(entry?.defLossPct ?? 0f));

            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);

            Text.Font = GameFont.Medium;
            listing.Label("TSA_WD_CaravanClash_Header".Translate());
            Text.Font = GameFont.Small;
            listing.GapLine();

            listing.Label("TSA_WD_CaravanClash_Intro".Translate());
            listing.Gap(8f);

            listing.Label("TSA_WD_CaravanClash_Attacker".Translate(entry?.labelA ?? "TSA_WD_Attackers".Translate()));
            listing.Label("TSA_WD_CaravanClash_Defender".Translate(entry?.labelB ?? "TSA_WD_Defender".Translate()));
            listing.Gap(10f);

            Rect powers = listing.GetRect(70f);
            RaidUIUtils.DrawRaidPowerBoxes(powers, attBefore, defBefore, "TSA_WD_Attackers", "TSA_WD_Defender");

            listing.Gap(10f);
            float ratio = entry?.ratio ?? (defBefore > 0f ? attBefore / defBefore : attBefore);
            listing.Label("TSA_WD_CaravanClash_Ratio".Translate(ratio.ToString("F2")));
            listing.Label("TSA_WD_WinChance".Translate() + ": " + Mathf.Clamp01(entry?.winChance ?? 0f).ToStringPercent());

            listing.Gap(6f);
            Rect winBar = listing.GetRect(22f);
            RaidUIUtils.DrawWinChanceBar(winBar, Mathf.Clamp01(entry?.winChance ?? 0f));

            listing.Gap(8f);
            Color headlineColor = attWon ? Color.green : ColorLibrary.RedReadable;
            string outcomeKey = attWon ? "TSA_WD_CaravanClash_AttackerWon" : "TSA_WD_CaravanClash_DefenderWon";
            Text.Font = GameFont.Medium;
            listing.Label(outcomeKey.Translate().Colorize(headlineColor));
            Text.Font = GameFont.Small;
            listing.Gap(6f);

            BattleMarginTier attTier = entry != null ? RaidUIUtils.GetAttSeverityTier(entry) : BattleMarginTier.Normal;
            BattleMarginTier defTier = entry?.defCoalitionSeverityTier ?? BattleMarginTier.Normal;
            listing.Label(RaidUIUtils.FormatResolutionMarginLine(attTier, true, attWon, entry?.attLossPct ?? 0f, useVictoryLabel: true)
                .Colorize(RaidUIUtils.GetMarginTierColor(attTier, attWon)));
            listing.Label(RaidUIUtils.FormatResolutionMarginLine(defTier, false, !attWon, entry?.defLossPct ?? 0f)
                .Colorize(RaidUIUtils.GetMarginTierColor(defTier, !attWon)));

            listing.Gap(10f);
            listing.Label("TSA_WD_CaravanClash_ResultingStrengths".Translate());
            listing.Label($"{entry?.labelA ?? "TSA_WD_Attackers".Translate()}: {attAfter:F0}");
            listing.Label($"{entry?.labelB ?? "TSA_WD_Defender".Translate()}: {defAfter:F0}");

            listing.Gap(12f);
            if (Widgets.ButtonText(listing.GetRect(30f), "Close".Translate()))
                Close();

            listing.End();
            Text.Anchor = TextAnchor.UpperLeft;
        }
    }
}
