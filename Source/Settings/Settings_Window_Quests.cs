using RimWorld;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    public class Dialog_QuestSettings : Window
    {
        private Vector2 scrollPosition;
        private readonly string windowTitle;

        public override Vector2 InitialSize => new Vector2(850f, 520f);

        public Dialog_QuestSettings()
        {
            doCloseButton = true;
            forcePause = true;
            closeOnClickedOutside = true;
            windowTitle = "TSA_WD_BtnQuests".Translate();
            optionalTitle = null;
        }

        private static Dialog_QuestSettings embeddedHost;

        public static void DrawEmbedded(Rect inRect)
        {
            if (embeddedHost == null) embeddedHost = new Dialog_QuestSettings();
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
            Rect scrollViewRect = new Rect(0f, 0f, contentWidth, 520f);
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
            SettingsUI.DrawMenuTopBar(
                l,
                SettingsUI.ResetPageToDefaultsLabel,
                () => s.ResetQuests(),
                () => { },
                () => { });

            SettingsUI.DrawCheckbox(l, "TSA_WD_Quest_EnableFirstOutpost".Translate(),
                ref s.enableFirstOutpostQuest,
                "TSA_WD_Quest_EnableFirstOutpostTip".Translate(),
                defaultValue: WorldDominationSettings.DefEnableFirstOutpostQuest);
            SettingsUI.DrawCheckbox(l, "TSA_WD_Quest_EnableSpyOpsSettlement".Translate(),
                ref s.enableSpyOpsSettlementQuest,
                "TSA_WD_Quest_EnableSpyOpsSettlementTip".Translate(),
                defaultValue: WorldDominationSettings.DefEnableSpyOpsSettlementQuest);
            SettingsUI.DrawCheckbox(l, "TSA_WD_Quest_EnableFortifySettlement".Translate(),
                ref s.enableFortifySettlementQuest,
                "TSA_WD_Quest_EnableFortifySettlementTip".Translate(),
                defaultValue: WorldDominationSettings.DefEnableFortifySettlementQuest);
            SettingsUI.DrawCheckbox(l, "TSA_WD_Quest_EnableCommonEnemySettlement".Translate(),
                ref s.enableCommonEnemySettlementQuest,
                "TSA_WD_Quest_EnableCommonEnemySettlementTip".Translate(),
                defaultValue: WorldDominationSettings.DefEnableCommonEnemySettlementQuest);
            SettingsUI.DrawCheckbox(l, "TSA_WD_Quest_EnableColonyRoadLink".Translate(),
                ref s.enableColonyRoadLinkQuest,
                "TSA_WD_Quest_EnableColonyRoadLinkTip".Translate(),
                defaultValue: WorldDominationSettings.DefEnableColonyRoadLinkQuest);

            bool victoryQuestWas = s.enableWorldDominationVictoryQuest;
            SettingsUI.DrawCheckbox(l, "TSA_WD_Quest_EnableWorldDominationVictory".Translate(),
                ref s.enableWorldDominationVictoryQuest,
                "TSA_WD_Quest_EnableWorldDominationVictoryTip".Translate(),
                defaultValue: WorldDominationSettings.DefEnableWorldDominationVictoryQuest);
            if (Current.ProgramState == ProgramState.Playing
                && victoryQuestWas != s.enableWorldDominationVictoryQuest)
            {
                if (s.enableWorldDominationVictoryQuest)
                    WdWorldDominationVictoryQuestHelper.TryLaunchNowIfEligible();
                else
                    WdWorldDominationVictoryQuestHelper.RemoveActiveIfAny();
            }
        }
    }
}
