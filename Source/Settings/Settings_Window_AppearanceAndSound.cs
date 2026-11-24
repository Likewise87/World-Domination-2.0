using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    public class Dialog_AppearanceAndSoundSettings : Window
    {
        private Vector2 scrollPosition;
        private readonly string windowTitle;

        public override Vector2 InitialSize => new Vector2(850f, 700f);

        public Dialog_AppearanceAndSoundSettings()
        {
            doCloseButton = true;
            forcePause = true;
            closeOnClickedOutside = true;
            windowTitle = "TSA_WD_BtnAppearanceAndSound".Translate();
            optionalTitle = null;
        }

        private static Dialog_AppearanceAndSoundSettings embeddedHost;

        public static void DrawEmbedded(Rect inRect)
        {
            if (embeddedHost == null) embeddedHost = new Dialog_AppearanceAndSoundSettings();
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
            Rect scrollViewRect = new Rect(0f, 0f, contentWidth, 800f);
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
            SettingsUI.DrawMenuTopBar(l, SettingsUI.ResetPageToDefaultsLabel, () => s.ResetAppearanceAndSound(),
                () => { }, () => { });

            bool prevOutpostTravelerIcons = s.alwaysShowOutpostTravelerIconsRegardlessOfZoom;
            bool prevSettlementIcons = s.alwaysShowSettlementIconsRegardlessOfZoom;

            SettingsUI.DrawCheckbox(l, "TSA_WD_Notify_AlwaysShowOutpostTravelerIcons".Translate(),
                ref s.alwaysShowOutpostTravelerIconsRegardlessOfZoom,
                "TSA_WD_Notify_AlwaysShowOutpostTravelerIconsTip".Translate(),
                defaultValue: WorldDominationSettings.DefAlwaysShowOutpostTravelerIconsRegardlessOfZoom);

            SettingsUI.DrawCheckbox(l, "TSA_WD_Notify_AlwaysShowSettlementIcons".Translate(),
                ref s.alwaysShowSettlementIconsRegardlessOfZoom,
                "TSA_WD_Notify_AlwaysShowSettlementIconsTip".Translate(),
                defaultValue: WorldDominationSettings.DefAlwaysShowSettlementIconsRegardlessOfZoom);

            if (prevOutpostTravelerIcons != s.alwaysShowOutpostTravelerIconsRegardlessOfZoom
                || prevSettlementIcons != s.alwaysShowSettlementIconsRegardlessOfZoom)
            {
                Patch_WdWorldObjectNoExpandingIcon.NotifyIconModeChanged();
            }

            SettingsUI.DrawCheckbox(l, "TSA_WD_Experimental_EnableWorldMapSounds".Translate(),
                ref s.enableWorldMapSounds,
                "TSA_WD_Experimental_EnableWorldMapSoundsTip".Translate(),
                defaultValue: WorldDominationSettings.DefEnableWorldMapSounds);
        }
    }
}
