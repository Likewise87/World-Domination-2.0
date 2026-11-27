using System;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Scribed Action Log type for header filters. Prefer write-time assignment; Infer covers legacy/TAG rows.</summary>
    public enum SpreadLogCategory : byte
    {
        Other = 0,
        Raid,
        Trader,
        Trade,
        Road,
        Expand,
        Grow,
        Upgrade,
        Turtle,
        Vanguard,
        Invasion,
        Desperation,
        Conquest,
        Revolt,
        Fortify,
        OutpostUpgrade,
        MajorIncident,
        MinorIncident,
        Isolation,
        Scout,
        Intel,
        Sabotage,
        Disinformation,
        Diplomacy,
        Pollution,
        BuiltAdvancedBuilding,
        BuiltFortification,
        FortifyOwnTownsOpen,
        ClearedFortification,
    }

    public static class SpreadLogCategoryUtil
    {
        public static readonly SpreadLogCategory[] All =
        {
            SpreadLogCategory.Raid,
            SpreadLogCategory.Trader,
            SpreadLogCategory.Trade,
            SpreadLogCategory.Road,
            SpreadLogCategory.Expand,
            SpreadLogCategory.Grow,
            SpreadLogCategory.Upgrade,
            SpreadLogCategory.Turtle,
            SpreadLogCategory.Vanguard,
            SpreadLogCategory.Invasion,
            SpreadLogCategory.Desperation,
            SpreadLogCategory.Conquest,
            SpreadLogCategory.Revolt,
            SpreadLogCategory.Fortify,
            SpreadLogCategory.OutpostUpgrade,
            SpreadLogCategory.MajorIncident,
            SpreadLogCategory.MinorIncident,
            SpreadLogCategory.Isolation,
            SpreadLogCategory.Scout,
            SpreadLogCategory.Intel,
            SpreadLogCategory.Sabotage,
            SpreadLogCategory.Disinformation,
            SpreadLogCategory.Diplomacy,
            SpreadLogCategory.Pollution,
            SpreadLogCategory.BuiltAdvancedBuilding,
            SpreadLogCategory.BuiltFortification,
            SpreadLogCategory.FortifyOwnTownsOpen,
            SpreadLogCategory.ClearedFortification,
            SpreadLogCategory.Other,
        };

        public static string LabelKey(SpreadLogCategory c) =>
            "TSA_WD_LogCategory_" + c;

        public static string Label(SpreadLogCategory c) => LabelKey(c).Translate().ToString();

        public static SpreadLogCategory Infer(SpreadLogEntry entry)
        {
            if (entry == null) return SpreadLogCategory.Other;
            if (entry.isRaid || entry.isCaravanClash)
                return SpreadLogCategory.Raid;
            if (entry.highlightKind == SpreadLogHighlightKind.Diplomacy)
                return SpreadLogCategory.Diplomacy;

            string msg = entry.message;
            if (string.IsNullOrEmpty(msg)) return SpreadLogCategory.Other;

            SpreadLogCategory fromTag = InferFromTagPrefix(msg);
            if (fromTag != SpreadLogCategory.Other)
                return fromTag;

            return InferFromUnprefixedMessage(msg);
        }

        public static SpreadLogCategory InferFromTagPrefix(string msg)
        {
            if (string.IsNullOrEmpty(msg)) return SpreadLogCategory.Other;
            // EN COPY_STYLE tags + SABOTAGE/DISINFORMATION + legacy long tags.
            if (StartsWithIgnoreCase(msg, "RAID:")) return SpreadLogCategory.Raid;
            if (StartsWithIgnoreCase(msg, "TRADER:")) return SpreadLogCategory.Trader;
            if (StartsWithIgnoreCase(msg, "TRADE:")) return SpreadLogCategory.Trade;
            if (StartsWithIgnoreCase(msg, "ROAD:") || StartsWithIgnoreCase(msg, "ROAD BUILDING ATTEMPT:"))
                return SpreadLogCategory.Road;
            if (StartsWithIgnoreCase(msg, "EXPAND:") || StartsWithIgnoreCase(msg, "EXPANSION ATTEMPT:"))
                return SpreadLogCategory.Expand;
            if (StartsWithIgnoreCase(msg, "GROW:")) return SpreadLogCategory.Grow;
            if (StartsWithIgnoreCase(msg, "UPGRADE:")) return SpreadLogCategory.Upgrade;
            if (StartsWithIgnoreCase(msg, "TURTLE:")) return SpreadLogCategory.Turtle;
            if (StartsWithIgnoreCase(msg, "VANGUARD:")) return SpreadLogCategory.Vanguard;
            if (StartsWithIgnoreCase(msg, "INVASION:")) return SpreadLogCategory.Invasion;
            if (StartsWithIgnoreCase(msg, "DESPERATION:")) return SpreadLogCategory.Desperation;
            if (StartsWithIgnoreCase(msg, "CONQUEST:")) return SpreadLogCategory.Conquest;
            if (StartsWithIgnoreCase(msg, "REVOLT:")) return SpreadLogCategory.Revolt;
            if (StartsWithIgnoreCase(msg, "FORTIFY:")) return SpreadLogCategory.Fortify;
            if (StartsWithIgnoreCase(msg, "OUTPOST UPGRADE:")) return SpreadLogCategory.OutpostUpgrade;
            if (StartsWithIgnoreCase(msg, "MAJOR INCIDENT:")) return SpreadLogCategory.MajorIncident;
            if (StartsWithIgnoreCase(msg, "MINOR INCIDENT:")) return SpreadLogCategory.MinorIncident;
            if (StartsWithIgnoreCase(msg, "ISOLATION:") || StartsWithIgnoreCase(msg, "ISOLATION PRESSURE:"))
                return SpreadLogCategory.Isolation;
            if (StartsWithIgnoreCase(msg, "SCOUT:")) return SpreadLogCategory.Scout;
            if (StartsWithIgnoreCase(msg, "INTEL:")) return SpreadLogCategory.Intel;
            if (StartsWithIgnoreCase(msg, "SABOTAGE:")) return SpreadLogCategory.Sabotage;
            if (StartsWithIgnoreCase(msg, "DISINFORMATION:")) return SpreadLogCategory.Disinformation;
            if (StartsWithIgnoreCase(msg, "DIPLOMACY:")) return SpreadLogCategory.Diplomacy;
            if (StartsWithIgnoreCase(msg, "POLLUTION:")) return SpreadLogCategory.Pollution;
            if (StartsWithIgnoreCase(msg, "ADV BUILD:") || StartsWithIgnoreCase(msg, "ADVANCED BUILDING:"))
                return SpreadLogCategory.BuiltAdvancedBuilding;
            if (StartsWithIgnoreCase(msg, "FORTIFICATION:") || StartsWithIgnoreCase(msg, "BUILT FORTIFICATION:"))
                return SpreadLogCategory.BuiltFortification;
            if (StartsWithIgnoreCase(msg, "FORTIFY OWN TOWNS:"))
                return SpreadLogCategory.FortifyOwnTownsOpen;
            if (StartsWithIgnoreCase(msg, "REMOVE FORTIFICATION:"))
                return SpreadLogCategory.ClearedFortification;

            // Common ES/ZH tag stems (defensive; EN Infer covers most live saves).
            if (msg.StartsWith("突襲：", StringComparison.Ordinal) || msg.StartsWith("突袭：", StringComparison.Ordinal)
                || msg.StartsWith("RAID：", StringComparison.OrdinalIgnoreCase))
                return SpreadLogCategory.Raid;
            if (msg.StartsWith("情報：", StringComparison.Ordinal) || msg.StartsWith("情报：", StringComparison.Ordinal)
                || msg.StartsWith("INTEL：", StringComparison.OrdinalIgnoreCase))
                return SpreadLogCategory.Intel;

            return SpreadLogCategory.Other;
        }

        private static SpreadLogCategory InferFromUnprefixedMessage(string msg)
        {
            // High-traffic EN / current-locale patterns without TAG: prefix.
            if (ContainsIgnoreCase(msg, "ambush") || ContainsIgnoreCase(msg, "Caravan clash")
                || ContainsIgnoreCase(msg, "Raid intercepted") || ContainsIgnoreCase(msg, "mortar")
                || ContainsIgnoreCase(msg, "anti-air") || ContainsIgnoreCase(msg, "detour")
                || ContainsIgnoreCase(msg, "choke"))
                return SpreadLogCategory.Raid;

            if (ContainsIgnoreCase(msg, "bribe") || ContainsIgnoreCase(msg, "gift")
                || ContainsIgnoreCase(msg, "negotiat") || ContainsIgnoreCase(msg, "diplomacy")
                || ContainsIgnoreCase(msg, "goodwill"))
                return SpreadLogCategory.Diplomacy;

            if (ContainsIgnoreCase(msg, "scout") && (ContainsIgnoreCase(msg, "cancel") || ContainsIgnoreCase(msg, "expired")))
                return SpreadLogCategory.Scout;

            if (ContainsIgnoreCase(msg, "pollution") || ContainsIgnoreCase(msg, "decontaminat"))
                return SpreadLogCategory.Pollution;

            return SpreadLogCategory.Other;
        }

        /// <summary>
        /// True for Pollution-category rows and any raid/other entry that carries pollution path flags or pollution wording.
        /// </summary>
        public static bool IsPollutionRelated(SpreadLogEntry entry)
        {
            if (entry == null) return false;
            if (entry.category == SpreadLogCategory.Pollution) return true;
            if (entry.pollutionDamageExpected || entry.pollutionRouteAltered) return true;
            if (entry.pollutionExpectedLoss > 0.01f) return true;

            string msg = entry.message;
            if (string.IsNullOrEmpty(msg)) return false;
            if (InferFromTagPrefix(msg) == SpreadLogCategory.Pollution) return true;
            return ContainsIgnoreCase(msg, "pollution") || ContainsIgnoreCase(msg, "decontaminat");
        }

        /// <summary>Type-filter match. Pollution selection also includes pollution-related raid rows.</summary>
        public static bool PassesFilter(SpreadLogEntry entry, System.Collections.Generic.HashSet<SpreadLogCategory> selected, bool exclude)
        {
            if (selected == null || selected.Count == 0) return true;
            SpreadLogCategory cat = entry != null && entry.category != SpreadLogCategory.Other
                ? entry.category
                : Infer(entry);
            bool inSel = selected.Contains(cat);
            if (!inSel && selected.Contains(SpreadLogCategory.Pollution) && IsPollutionRelated(entry))
                inSel = true;
            return exclude ? !inSel : inSel;
        }

        private static bool StartsWithIgnoreCase(string msg, string prefix) =>
            msg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

        private static bool ContainsIgnoreCase(string msg, string needle) =>
            msg.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

        public static bool PassesFilter(SpreadLogCategory category, System.Collections.Generic.HashSet<SpreadLogCategory> selected, bool exclude)
        {
            if (selected == null || selected.Count == 0) return true;
            bool inSel = selected.Contains(category);
            return exclude ? !inSel : inSel;
        }
    }
}
