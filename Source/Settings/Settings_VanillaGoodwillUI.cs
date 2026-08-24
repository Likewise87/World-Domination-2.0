using RimWorld;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Vanilla goodwill tweak rows used by <see cref="Dialog_DiplomacySettings"/>.</summary>
    internal static class VanillaGoodwillSettingsUI
    {
        internal static void DrawListingRows(Listing_Standard l, WorldDominationSettings s)
        {
            l.CheckboxLabeled(
                "TS_WD_Threat_NoGoodwillHostiles".Translate(),
                ref s.noGoodwillFromHostilesOnConquest,
                SettingsUI.TooltipWithDefault("TS_WD_Threat_NoGoodwillHostilesTooltip".Translate(), WorldDominationSettings.DefNoGoodwillFromHostilesOnConquest));
            l.CheckboxLabeled(
                "TSA_WD_DisableSettlementProximityGoodwill".Translate(),
                ref s.disableSettlementProximityGoodwill,
                SettingsUI.TooltipWithDefault("TSA_WD_DisableSettlementProximityGoodwillTooltip".Translate(), WorldDominationSettings.DefDisableSettlementProximityGoodwill));
        }
    }
}
