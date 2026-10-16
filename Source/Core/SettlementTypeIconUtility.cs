using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// NPC settlement ExpandingIcon: subtype art under <c>WorldObjects/Settlements/</c>.
    /// Type art is faction-tinted via ExpandingIconColor. No faction badge overlay.
    /// Unknown / missing file / player Colony·Outpost → caller keeps vanilla FactionIcon.
    /// Resolved tex is cached on <see cref="CompViralSpread"/> until tier/subtype changes.
    /// </summary>
    public static class SettlementTypeIconUtility
    {
        private const string TextureFolder = "WorldObjects/Settlements/";

        /// <summary>When true, <paramref name="tex"/> is subtype art (faction tint via ExpandingIconColor).</summary>
        public static bool TryGetExpandingIcon(Settlement settlement, out Texture2D tex)
        {
            tex = null;
            if (settlement == null) return false;
            CompViralSpread comp = settlement.GetComponent<CompViralSpread>();
            if (!TryResolveTypePath(comp, out string typePath)) return false;

            if (comp.TryGetCachedSettlementTypeExpandingIcon(typePath, out tex))
                return true;

            tex = ContentFinder<Texture2D>.Get(typePath, false);
            if (tex == null) return false;
            comp.SetCachedSettlementTypeExpandingIcon(typePath, tex);
            return true;
        }

        public static bool TryResolveTypePath(CompViralSpread comp, out string path)
        {
            path = null;
            if (comp == null || !comp.IsSettlement || comp.IsPlayerMapSettlement) return false;
            Faction f = comp.parent?.Faction;
            if (f?.def == null || f.IsPlayer) return false;

            string sub = comp.subType;
            if (string.IsNullOrEmpty(sub)
                || sub == "Colony"
                || sub == "Outpost"
                || sub == "Excluded")
                return false;

            string file = FileNameFor(comp.tier, sub);
            if (file == null) return false;
            path = TextureFolder + file;
            return true;
        }

        private static string FileNameFor(SettlementTier tier, string subType)
        {
            // Slavery scribes as Slavery; layout token Prison may appear in odd saves.
            if (string.Equals(subType, NpcSettlementSubtypeUtil.SubTypeSlavery, System.StringComparison.Ordinal)
                || string.Equals(subType, "Prison", System.StringComparison.Ordinal))
            {
                return tier == SettlementTier.T2 ? "T2_Prison" : null;
            }

            if (string.Equals(subType, "Vanguard", System.StringComparison.Ordinal))
            {
                if (tier == SettlementTier.T1) return "T1_Vanguard";
                if (tier == SettlementTier.T2) return "T2_Vanguard";
                if (tier == SettlementTier.T3) return "T3_Vanguard";
                if (tier == SettlementTier.T4) return "T4_Vanguard";
                return null;
            }

            if (string.Equals(subType, "Fortress", System.StringComparison.Ordinal))
                return tier == SettlementTier.T3 ? "T3_Fortress" : null;

            if (string.Equals(subType, "Citadel", System.StringComparison.Ordinal))
                return tier == SettlementTier.T4 ? "T4_Citadel" : null;

            switch (subType)
            {
                case NpcSettlementSubtypeUtil.SubTypeFarming:
                    return tier == SettlementTier.T1 ? "T1_Farm" : null;
                case NpcSettlementSubtypeUtil.SubTypeLogging:
                    return tier == SettlementTier.T1 ? "T1_Logging" : null;
                case NpcSettlementSubtypeUtil.SubTypeMining:
                    return tier == SettlementTier.T1 ? "T1_Mining" : null;
                case NpcSettlementSubtypeUtil.SubTypeRefuge:
                    return tier == SettlementTier.T1 ? "T1_Refuge" : null;
                case NpcSettlementSubtypeUtil.SubTypeCamp:
                    if (tier == SettlementTier.T1) return "T1_Camp";
                    if (tier == SettlementTier.T2) return "T2_Camp";
                    return null;
                case NpcSettlementSubtypeUtil.SubTypeProduction:
                    return tier == SettlementTier.T2 ? "T2_Production" : null;
                default:
                    return null;
            }
        }
    }
}
