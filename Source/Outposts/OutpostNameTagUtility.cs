using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Short role tags for outpost names (Farm, Mine, …). Not the full outpost type label.
    /// </summary>
    public static class OutpostNameTagUtility
    {
        public const int MaxNameLength = 30;
        private const int MaxGenAttempts = 40;

        /// <summary>defName → translation key for the short EN/ES/ZH tag word.</summary>
        private static readonly Dictionary<string, string> DefToTagKey = new Dictionary<string, string>
        {
            { "TSA_WD_Outpost_Farming", "TSA_WD_Outpost_NameTag_Farm" },
            { "TSA_WD_Outpost_Mining", "TSA_WD_Outpost_NameTag_Mine" },
            { "TSA_WD_Outpost_Ranch", "TSA_WD_Outpost_NameTag_Ranch" },
            { "TSA_WD_Outpost_Academy", "TSA_WD_Outpost_NameTag_Academy" },
            { "TSA_WD_Outpost_Research", "TSA_WD_Outpost_NameTag_Institute" },
            { "TSA_WD_Outpost_BiofuelRefinery", "TSA_WD_Outpost_NameTag_Refinery" },
            { "TSA_WD_Outpost_Warehouse", "TSA_WD_Outpost_NameTag_Warehouse" },
            { "TSA_WD_Outpost_Embassy", "TSA_WD_Outpost_NameTag_Embassy" },
            { "TSA_WD_Outpost_Trading", "TSA_WD_Outpost_NameTag_Market" },
            { "TSA_WD_Outpost_Fabrication", "TSA_WD_Outpost_NameTag_Factory" },
            { "BOR_TSA_WD_Outpost_Fabrication", "TSA_WD_Outpost_NameTag_Factory" },
            { "TSA_WD_Outpost_Fishing", "TSA_WD_Outpost_NameTag_Fishery" },
            { "TSA_WD_Outpost_Hunting", "TSA_WD_Outpost_NameTag_Lodge" },
            { "TSA_WD_Outpost_Mortar", "TSA_WD_Outpost_NameTag_Battery" },
            { "TSA_WD_Outpost_PowerPlant", "TSA_WD_Outpost_NameTag_Reactor" },
            { "TSA_WD_Outpost_DrugLab", "TSA_WD_Outpost_NameTag_Lab" },
            { "BOR_TSA_WD_Outpost_DrugLab", "TSA_WD_Outpost_NameTag_Lab" },
            { "TSA_WD_Outpost_DeepchemDrill", "TSA_WD_Outpost_NameTag_Rig" },
            { "TSA_WD_Outpost_DeepchemFactory", "TSA_WD_Outpost_NameTag_Chemworks" },
            { "TSA_WD_Outpost_Scavenging", "TSA_WD_Outpost_NameTag_Scrapyard" },
            { "TSA_WD_Outpost_RapidResponse", "TSA_WD_Outpost_NameTag_Garrison" },
        };

        public static bool SupportsTypeTag(WorldObjectDef def)
            => def != null && DefToTagKey.ContainsKey(def.defName);

        public static bool TryGetTypeTag(WorldObjectDef def, out string tag)
        {
            tag = null;
            if (def == null || !DefToTagKey.TryGetValue(def.defName, out string key))
                return false;
            tag = key.Translate().Resolve();
            return !string.IsNullOrEmpty(tag);
        }

        /// <summary>Language-keyed format: EN/ES <c>{0} {1}</c>, ZH <c>{0}·{1}</c>.</summary>
        public static string Compose(string root, string tag)
        {
            if (string.IsNullOrEmpty(root)) return tag ?? "";
            if (string.IsNullOrEmpty(tag)) return root;
            return "TSA_WD_Outpost_NameTag_Format".Translate(root.Trim(), tag).Resolve();
        }

        /// <summary>Compose when it fits <see cref="MaxNameLength"/>; otherwise return root only.</summary>
        public static string ComposeIfFits(string root, string tag)
        {
            string composed = Compose(root, tag);
            if (string.IsNullOrEmpty(composed)) return root ?? "";
            if (composed.Length <= MaxNameLength) return composed;
            return root?.Trim() ?? "";
        }

        /// <summary>Reverse Compose using the active language format (sentinel probe).</summary>
        public static bool TrySplitRoot(string fullName, string tag, out string root)
        {
            root = fullName;
            if (string.IsNullOrEmpty(fullName) || string.IsNullOrEmpty(tag))
                return false;

            const char sentinel = '\u0001';
            string probe = Compose(sentinel.ToString(), tag);
            int idx = probe.IndexOf(sentinel);
            if (idx < 0) return false;

            string prefix = probe.Substring(0, idx);
            string suffix = probe.Substring(idx + 1);
            if (string.IsNullOrEmpty(suffix) || !fullName.EndsWith(suffix))
                return false;
            if (!string.IsNullOrEmpty(prefix) && !fullName.StartsWith(prefix))
                return false;

            int rootLen = fullName.Length - prefix.Length - suffix.Length;
            if (rootLen <= 0) return false;
            root = fullName.Substring(prefix.Length, rootLen);
            return !string.IsNullOrEmpty(root);
        }

        /// <summary>
        /// Settlement nameMaker root, optionally with type tag. Uniqueness and length checked on the final string.
        /// Never substring-clips a composed name.
        /// </summary>
        public static string GenerateName(WorldObjectDef outpostDef, int outpostTile, bool includeTypeTag, out string rootName)
        {
            rootName = outpostTile.ToString();
            var faction = Faction.OfPlayer;
            var nameMaker = faction?.def?.settlementNameMaker;
            if (nameMaker == null)
                return rootName;

            List<string> existing = CollectExistingOutpostNames();
            string tag = null;
            bool useTag = includeTypeTag && TryGetTypeTag(outpostDef, out tag);

            var forbiddenRoots = new HashSet<string>();
            for (int attempt = 0; attempt < MaxGenAttempts; attempt++)
            {
                // Validator: true = acceptable (RimWorld NameGenerator contract).
                string root = NameGenerator.GenerateName(
                    nameMaker,
                    n => !string.IsNullOrEmpty(n)
                         && !forbiddenRoots.Contains(n)
                         && !existing.Contains(n));
                if (string.IsNullOrEmpty(root))
                    continue;
                forbiddenRoots.Add(root);
                rootName = root;

                if (!useTag)
                {
                    if (root.Length <= MaxNameLength)
                        return root;
                    continue;
                }

                string final = Compose(root, tag);
                if (string.IsNullOrEmpty(final) || final.Length > MaxNameLength)
                    continue;
                if (existing.Contains(final))
                    continue;
                return final;
            }

            // Fallback: last root if it fits; else tile id (never clip a composed tag name).
            if (!string.IsNullOrEmpty(rootName) && rootName.Length <= MaxNameLength && !existing.Contains(rootName))
                return rootName;
            rootName = outpostTile.ToString();
            return rootName;
        }

        private static List<string> CollectExistingOutpostNames()
        {
            var existing = new List<string>();
            var allWo = Find.WorldObjects?.AllWorldObjects;
            if (allWo == null) return existing;
            for (int i = 0; i < allWo.Count; i++)
            {
                if (allWo[i] is WorldObject_WD_Outpost o && !string.IsNullOrEmpty(o.Name))
                    existing.Add(o.Name);
            }
            return existing;
        }
    }
}
