using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    public class Dialog_QualityOfLifeSettings : Window
    {
        private Vector2 scrollPosition;
        private readonly string windowTitle;

        public override Vector2 InitialSize => new Vector2(850f, 700f);

        public Dialog_QualityOfLifeSettings()
        {
            doCloseButton = true;
            forcePause = true;
            closeOnClickedOutside = true;
            windowTitle = "TSA_WD_BtnQualityOfLife".Translate();
            optionalTitle = null;
        }

        private static Dialog_QualityOfLifeSettings embeddedHost;

        public static void DrawEmbedded(Rect inRect)
        {
            if (embeddedHost == null) embeddedHost = new Dialog_QualityOfLifeSettings();
            embeddedHost.DrawBody(inRect);
        }

        public override void DoWindowContents(Rect inRect)
        {
            Rect contentRect = SettingsUI.DrawWindowTitle(inRect, windowTitle);
            DrawBody(contentRect);
        }

        private void DrawBody(Rect contentRect)
        {
            if (WdSettingsSearch.IsCollecting)
            {
                Listing_Standard l = new Listing_Standard();
                l.Begin(contentRect);
                DrawListing(l);
                l.End();
                return;
            }

            float contentWidth = SettingsUI.ScrollContentWidth(contentRect.width);
            Rect scrollViewRect = new Rect(0f, 0f, contentWidth, 1200f);
            SettingsUI.ApplyPendingHighlightScroll(ref scrollPosition, contentRect.height, scrollViewRect.height);
            Widgets.BeginScrollView(contentRect, ref scrollPosition, scrollViewRect);
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(scrollViewRect);
            DrawListing(listing);
            listing.End();
            Widgets.EndScrollView();
        }

        private void DrawListing(Listing_Standard l)
        {
            var s = WorldDominationMod.settings;
            SettingsUI.DrawMenuTopBar(l, SettingsUI.ResetPageToDefaultsLabel, () => s.ResetQualityOfLife(),
                () => { }, () => { });

            SettingsUI.DrawCheckbox(l, "TSA_WD_Experimental_ColonyWorldBuild".Translate(),
                ref s.experimentalColonyWorldBuild,
                "TSA_WD_Experimental_ColonyWorldBuildTip".Translate(),
                defaultValue: WorldDominationSettings.DefExperimentalColonyWorldBuild);

            DrawWorldMapOverlayHoldKeyRow(l, s);
            SettingsUI.DrawCheckbox(l, "TSA_WD_AutoAdd_DefaultOn".Translate(),
                ref s.autoAddPawnsOnArrivalDefault,
                "TSA_WD_AutoAdd_DefaultOnTip".Translate(),
                defaultValue: WorldDominationSettings.DefAutoAddPawnsOnArrivalDefault);
            SettingsUI.DrawCheckbox(l, "TSA_WD_TravelFood_PrisonerRecruit".Translate(),
                ref s.giveFoodOnPrisonerRecruitTransfer,
                "TSA_WD_TravelFood_PrisonerRecruitTip".Translate(),
                defaultValue: WorldDominationSettings.DefGiveFoodOnPrisonerRecruitTransfer);
            SettingsUI.DrawCheckbox(l, "TSA_WD_TravelFood_AllPlayerPawns".Translate(),
                ref s.giveFoodOnAllPlayerPawnsTransfer,
                "TSA_WD_TravelFood_AllPlayerPawnsTip".Translate(),
                defaultValue: WorldDominationSettings.DefGiveFoodOnAllPlayerPawnsTransfer);
            SettingsUI.DrawCheckbox(l, "TSA_WD_OutpostSim_ShowInWdMenu".Translate(),
                ref s.showOutpostRequirementsPreviewInWdMenu,
                "TSA_WD_OutpostSim_ShowInWdMenuTip".Translate(),
                defaultValue: WorldDominationSettings.DefShowOutpostRequirementsPreviewInWdMenu);
        }

        private static void DrawWorldMapOverlayHoldKeyRow(Listing_Standard l, WorldDominationSettings s)
        {
            l.Gap(2f);
            Rect row = l.GetRect(24f);
            string tip = SettingsUI.TooltipWithDefault(
                "TSA_WD_WorldMapOverlayHoldKey_Tooltip".Translate(),
                FormatOverlayHoldKey(WorldDominationSettings.DefWorldMapOverlayHoldKey));
            SettingsUI.TipRegion(row, tip);

            Rect labelRect = new Rect(row.x, row.y, row.width - 110f, row.height);
            Rect btnRect = new Rect(row.xMax - 106f, row.y, 106f, row.height);
            Widgets.Label(labelRect, "TSA_WD_WorldMapOverlayHoldKey".Translate());
            if (Widgets.ButtonText(btnRect, FormatOverlayHoldKey(s.worldMapOverlayHoldKey)))
            {
                var options = new List<FloatMenuOption>();
                foreach (KeyCode key in OverlayHoldKeyChoices)
                {
                    KeyCode captured = key;
                    options.Add(new FloatMenuOption(FormatOverlayHoldKey(captured), () => s.worldMapOverlayHoldKey = captured));
                }
                Find.WindowStack.Add(new FloatMenu(options));
            }
        }

        private static readonly KeyCode[] OverlayHoldKeyChoices =
        {
            KeyCode.None,
            KeyCode.LeftAlt, KeyCode.RightAlt,
            KeyCode.LeftControl, KeyCode.RightControl,
            KeyCode.LeftShift, KeyCode.RightShift
        };

        private static string FormatOverlayHoldKey(KeyCode key)
        {
            switch (key)
            {
                case KeyCode.None: return "TSA_WD_KeyUnbound".Translate();
                case KeyCode.LeftAlt: return "Left Alt";
                case KeyCode.RightAlt: return "Right Alt";
                case KeyCode.LeftControl: return "Left Ctrl";
                case KeyCode.RightControl: return "Right Ctrl";
                case KeyCode.LeftShift: return "Left Shift";
                case KeyCode.RightShift: return "Right Shift";
                default: return key.ToString();
            }
        }
    }
}
