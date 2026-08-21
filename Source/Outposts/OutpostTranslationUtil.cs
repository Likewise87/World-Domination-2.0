using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Keyed strings for outpost UI; all copy lives in Languages/Keyed translation XML.</summary>
    internal static class OutpostTranslationUtil
    {
        public static string Key(string translationKey) => translationKey.Translate().Resolve();

        public static string Key(string translationKey, NamedArgument arg0) => translationKey.Translate(arg0).Resolve();

        public static string Key(string translationKey, NamedArgument arg0, NamedArgument arg1)
            => translationKey.Translate(arg0, arg1).Resolve();

        public static string Key(string translationKey, NamedArgument arg0, NamedArgument arg1, NamedArgument arg2)
            => translationKey.Translate(arg0, arg1, arg2).Resolve();

        public static string Key(string translationKey, NamedArgument arg0, NamedArgument arg1, NamedArgument arg2, NamedArgument arg3)
            => translationKey.Translate(arg0, arg1, arg2, arg3).Resolve();

        public static string Key(string translationKey, NamedArgument arg0, NamedArgument arg1, NamedArgument arg2, NamedArgument arg3, NamedArgument arg4)
            => translationKey.Translate(arg0, arg1, arg2, arg3, arg4).Resolve();

        /// <summary>Tab headline: {tab name} ({outpost label}, {outpost type}).</summary>
        public static string TabHeadline(WorldObject_WD_Outpost outpost, string tabNameKey)
        {
            if (outpost == null) return Key(tabNameKey);
            string tabName = Key(tabNameKey);
            string typeName = TruncateTypeForHeadline(outpost.def?.LabelCap.Resolve() ?? "");
            return Key("TSA_WD_Outpost_TabHeadline", tabName, outpost.Label, typeName);
        }

        /// <summary>Outpost type in headlines: at most 19 characters (16 + "..." when longer).</summary>
        private static string TruncateTypeForHeadline(string typeName)
        {
            if (string.IsNullOrEmpty(typeName) || typeName.Length <= 19)
                return typeName ?? "";
            return typeName.Substring(0, 16) + "...";
        }
    }
}
