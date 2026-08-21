using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Yellow pollution banners shared by raid math dialog and Action Log detail windows.</summary>
    public static class PollutionRaidUi
    {
        public static void DrawBanners(Listing_Standard listing, bool damageExpected, bool routeAltered)
        {
            if (listing == null) return;
            if (damageExpected)
                DrawOne(listing, "TSA_WD_Pollution_BannerDamage".Translate());
            if (routeAltered)
                DrawOne(listing, "TSA_WD_Pollution_BannerRouteAltered".Translate());
        }

        private static void DrawOne(Listing_Standard listing, string text)
        {
            Text.Font = GameFont.Small;
            float textH = Mathf.Max(24f, Text.CalcHeight(text, listing.ColumnWidth - 12f));
            float boxH = textH + 12f;
            Rect boxRect = listing.GetRect(boxH);
            Widgets.DrawBoxSolid(boxRect, Outpost_Dialog_UI.SkillDrBoxYellow);
            Widgets.DrawBox(boxRect);
            GUI.color = Color.yellow;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(boxRect.ContractedBy(6f), text);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            listing.Gap(6f);
        }
    }
}
