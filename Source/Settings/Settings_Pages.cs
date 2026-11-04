using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    public enum WdSettingsPage
    {
        Overview = 0,
        Notifications,
        Combat,
        Economic,
        DailyActions,
        WorldRaids,
        Growth,
        Diplomacy,
        Outposts,
        PlayerArtillery,
        Caravans,
        RoadBuilding,
        Mining,
        Sabotage,
        Disinformation,
        WorldGen,
        BaseGeneration,
        Experimental
    }

    public sealed class WdSettingsPageDef
    {
        public WdSettingsPage Id;
        public string LabelKey;
        public string DescKey;
        public string IconPath;
        /// <summary>Optional icon tint (cyan for utility pages, threat-red for combat/raids).</summary>
        public Color IconTint = Color.white;
        /// <summary>Keyed section header in the left nav (null = no new section).</summary>
        public string SectionKey;
        public bool Advanced;
        public Action<Rect> Draw;
        public Action ApplyOnClose;
    }

    /// <summary>Registry of live settings pages for the 3-pane shell.</summary>
    public static class WdSettingsPages
    {
        /// <summary>WD cyan for utility nav icons.</summary>
        public static readonly Color NavIconCyan = Color.cyan;
        /// <summary>Threat-red for combat / raid nav icons (dashboard raid header tint).</summary>
        public static readonly Color NavIconThreatRed = new Color(1f, 0.35f, 0.35f);
        /// <summary>Growth green for Growth &amp; Expansion nav icon.</summary>
        public static readonly Color NavIconGrowthGreen = new Color(0.35f, 0.85f, 0.40f);

        private static List<WdSettingsPageDef> pages;
        private static Dictionary<WdSettingsPage, WdSettingsPageDef> byId;

        public static IReadOnlyList<WdSettingsPageDef> All
        {
            get
            {
                EnsureInit();
                return pages;
            }
        }

        public static WdSettingsPageDef Get(WdSettingsPage id)
        {
            EnsureInit();
            return byId.TryGetValue(id, out WdSettingsPageDef d) ? d : pages[0];
        }

        public static Texture2D LoadIcon(string path)
        {
            if (path.NullOrEmpty()) path = "UI/Commands/Config";
            Texture2D tex = ContentFinder<Texture2D>.Get(path, false);
            if (tex == null)
                tex = ContentFinder<Texture2D>.Get("UI/Commands/Config", false);
            return tex ?? BaseContent.BadTex;
        }

        private static void EnsureInit()
        {
            if (pages != null) return;
            // SectionKey is set on every page in a group; nav draws the header when the visible section changes.
            pages = new List<WdSettingsPageDef>
            {
                P(WdSettingsPage.Overview, "TSA_WD_Settings_NavOverview", "TSA_WD_Settings_NavOverviewDesc",
                    "UI/Tab/WD", null, false, WdSettingsShell.DrawOverviewPage, null),
                P(WdSettingsPage.Notifications, "TSA_WD_BtnNotifications", "TSA_WD_DescNotifications",
                    "UI/Commands/Icon_ActionLog", "TSA_WD_HeaderGeneral", false,
                    Dialog_NotificationSettings.DrawEmbedded, null, NavIconCyan),
                P(WdSettingsPage.Combat, "TSA_WD_BtnCombatDifficulty", "TSA_WD_DescCombatDifficulty",
                    "WorldObjects/Travelers/Caravan_Raiders", "TSA_WD_HeaderGeneral", false,
                    Dialog_CombatDifficultySettings.DrawEmbedded, Dialog_CombatDifficultySettings.ApplyOnSettingsClose, NavIconThreatRed),
                P(WdSettingsPage.Economic, "TSA_WD_BtnEconomicDifficulty", "TSA_WD_DescEconomicDifficulty",
                    "UI/Commands/Bribe", "TSA_WD_HeaderGeneral", false,
                    Dialog_EconomicDifficultySettings.DrawEmbedded, null, NavIconCyan),
                P(WdSettingsPage.DailyActions, "TSA_WD_BtnDailyActions", "TSA_WD_DescDailyActions",
                    "UI/Commands/Icon_WorldStats", "TSA_WD_HeaderGeneral", false,
                    Dialog_DailyActionsSettings.DrawEmbedded, null, NavIconCyan),
                P(WdSettingsPage.WorldRaids, "TSA_WD_BtnWorldRaids", "TSA_WD_DescWorldRaids",
                    "WorldObjects/Travelers/Caravan_Invasion", "TSA_WD_HeaderGeneral", false,
                    Dialog_RaidSettings.DrawEmbedded, Dialog_RaidSettings.ApplyOnSettingsClose, NavIconThreatRed),
                P(WdSettingsPage.Growth, "TSA_WD_BtnGrowthExpand", "TSA_WD_DescGrowthExpand",
                    "UI/Commands/Expansionist", "TSA_WD_HeaderGeneral", false,
                    Dialog_GrowthSettings.DrawEmbedded, null, NavIconGrowthGreen),
                P(WdSettingsPage.Diplomacy, "TSA_WD_BtnDiplomacy", "TSA_WD_DescDiplomacy",
                    "UI/Commands/Icon_Diplomacy", "TSA_WD_HeaderGeneral", false,
                    Dialog_DiplomacySettings.DrawEmbedded, null, NavIconCyan),
                P(WdSettingsPage.Outposts, "TSA_WD_BtnOutpostSettings", "TSA_WD_DescOutpostSettings",
                    "UI/Commands/EstablishOutpost", "TSA_WD_HeaderOutposts", false,
                    Dialog_FoodSettings.DrawEmbedded, null, NavIconCyan),
                P(WdSettingsPage.PlayerArtillery, "TSA_WD_BtnPlayerArtillery", "TSA_WD_DescPlayerArtillery",
                    "UI/Commands/ShootMortar", "TSA_WD_HeaderOutposts", false,
                    Dialog_PlayerArtillerySettings.DrawEmbedded, null, NavIconCyan),
                P(WdSettingsPage.Caravans, "TSA_WD_BtnCaravans", "TSA_WD_DescCaravans",
                    "UI/Commands/Icon_ActiveTravelers", "TSA_WD_HeaderOutposts", false,
                    Dialog_CaravansSettings.DrawEmbedded, null, NavIconCyan),
                P(WdSettingsPage.RoadBuilding, "TSA_WD_BtnRoadBuilding", "TSA_WD_DescRoadBuilding",
                    "UI/Commands/Build", "TSA_WD_HeaderOutposts", true,
                    Dialog_RoadBuildingSettings.DrawEmbedded, null),
                P(WdSettingsPage.Mining, "TSA_WD_BtnMiningBaselines", "TSA_WD_DescMiningBaselines",
                    "WorldObjects/Outposts/WD_Outpost_Mining", "TSA_WD_HeaderOutposts", true,
                    Dialog_MiningBaselineSettings.DrawEmbedded, null, NavIconCyan),
                P(WdSettingsPage.Sabotage, "TSA_WD_BtnSabotage", "TSA_WD_DescSabotage",
                    "UI/Commands/Sabotage", "TSA_WD_HeaderPlayerInteractions", true,
                    Dialog_SabotageSettings.DrawEmbedded, null),
                P(WdSettingsPage.Disinformation, "TSA_WD_BtnDisinformation", "TSA_WD_DescDisinformation",
                    "UI/Commands/Disinformation", "TSA_WD_HeaderPlayerInteractions", true,
                    Dialog_DisinformationSettings.DrawEmbedded, null),
                P(WdSettingsPage.WorldGen, "TSA_WD_BtnWorldGen", "TSA_WD_DescWorldGen",
                    "UI/Commands/Randomize", "TSA_WD_HeaderMisc", true,
                    Dialog_WorldGenSettings.DrawEmbedded, null, NavIconCyan),
                P(WdSettingsPage.BaseGeneration, "TSA_WD_BtnBaseGeneration", "TSA_WD_DescBaseGeneration",
                    "WorldObjects/Settlements/T2_Vanguard", "TSA_WD_HeaderMisc", true,
                    Dialog_BaseGenerationSettings.DrawEmbedded, null, NavIconCyan),
                P(WdSettingsPage.Experimental, "TSA_WD_BtnExperimental", "TSA_WD_DescExperimental",
                    "UI/Commands/AdvancedFilter", "TSA_WD_HeaderMisc", false,
                    Dialog_ExperimentalSettings.DrawEmbedded, null, NavIconCyan),
            };
            byId = new Dictionary<WdSettingsPage, WdSettingsPageDef>();
            for (int i = 0; i < pages.Count; i++)
                byId[pages[i].Id] = pages[i];
        }

        private static WdSettingsPageDef P(
            WdSettingsPage id,
            string labelKey,
            string descKey,
            string iconPath,
            string sectionKey,
            bool advanced,
            Action<Rect> draw,
            Action applyOnClose,
            Color? iconTint = null)
        {
            return new WdSettingsPageDef
            {
                Id = id,
                LabelKey = labelKey,
                DescKey = descKey,
                IconPath = iconPath,
                IconTint = iconTint ?? Color.white,
                SectionKey = sectionKey,
                Advanced = advanced,
                Draw = draw,
                ApplyOnClose = applyOnClose
            };
        }
    }
}
