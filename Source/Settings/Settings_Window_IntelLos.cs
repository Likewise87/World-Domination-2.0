using RimWorld;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    public class Dialog_IntelLosSettings : Window
    {
        private Vector2 scrollPosition;
        private readonly string windowTitle;

        private bool gateExpanded = true;
        private bool ringsExpanded = true;
        private bool settlementLosExpanded = true;
        private bool travelerLosExpanded = true;
        private bool scoutsExpanded = true;
        private bool burnedExpanded = true;

        public override Vector2 InitialSize => new Vector2(850f, 750f);

        public Dialog_IntelLosSettings()
        {
            doCloseButton = true;
            forcePause = true;
            closeOnClickedOutside = true;
            windowTitle = "TSA_WD_BtnIntelLos".Translate();
            optionalTitle = null;
        }

        private void SetAllExpanded(bool expanded)
        {
            gateExpanded = ringsExpanded = settlementLosExpanded = travelerLosExpanded =
                scoutsExpanded = burnedExpanded = expanded;
        }

        private static Dialog_IntelLosSettings embeddedHost;

        public static void DrawEmbedded(Rect inRect)
        {
            if (embeddedHost == null) embeddedHost = new Dialog_IntelLosSettings();
            embeddedHost.DrawBody(inRect);
        }

        public static void ApplyOnSettingsClose()
        {
            WorldDominationMod.settings?.NormalizeIntelLosSliders();
            if (Current.ProgramState != ProgramState.Playing) return;
            Find.World?.GetComponent<WorldComponent_SpreadManager>()?.Notify_WeightsChanged();
        }

        public override void PreClose()
        {
            base.PreClose();
            ApplyOnSettingsClose();
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
            Rect scrollViewRect = new Rect(0f, 0f, contentWidth, 4200f);
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
            SettingsUI.DrawMenuTopBar(l, SettingsUI.ResetPageToDefaultsLabel, () => s.ResetIntelLos(),
                () => SetAllExpanded(true),
                () => SetAllExpanded(false));

            DrawFeatureGate(l, s);
            if (!s.enableSettlementIntel || s.gateSettlementIntel == WdThreatStageGate.Never)
                return;

            DrawSightRings(l, s);
            DrawSettlementLos(l, s);
            DrawTravelerLos(l, s);
            DrawScouts(l, s);
            DrawGotBurned(l, s);
            s.NormalizeIntelLosSliders();
        }

        private static void ApplyGate(WorldDominationSettings s, System.Action apply)
        {
            apply();
            s.SyncLegacyThreatFlagsFromGates();
        }

        private void DrawFeatureGate(Listing_Standard l, WorldDominationSettings s)
        {
            // Always at top of the page (even if Feature gate section is collapsed).
            bool prevEnable = s.enableSettlementIntel;
            SettingsUI.DrawCheckbox(l, "TSA_WD_Intel_EnableSettlementIntel".Translate(),
                ref s.enableSettlementIntel,
                "TSA_WD_Intel_EnableSettlementIntelTip".Translate());
            if (prevEnable && !s.enableSettlementIntel && Current.ProgramState == ProgramState.Playing)
                Messages.Message("TSA_WD_Intel_DisableRestartTip".Translate(), MessageTypeDefOf.CautionInput, false);

            if (!SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Intel_HeaderFeatureGate".Translate(), ref gateExpanded, SettingsUI.SectionHeaderColor,
                "TSA_WD_Intel_HeaderFeatureGateTip".Translate()))
                return;

            if (!s.enableSettlementIntel)
                return;

            SettingsUI.EnumDropdownApply(l, "TSA_WD_Combat_GateWhen".Translate(), s.gateSettlementIntel,
                v => ApplyGate(s, () => s.gateSettlementIntel = v),
                WdEscalation.ThreatGateLabel,
                "TSA_WD_Intel_GateSettlementIntelTip".Translate());

            if (s.gateSettlementIntel == WdThreatStageGate.Never)
                return;

            SettingsUI.DrawCheckbox(l, "TSA_WD_Intel_PlayerColoniesAlwaysKnown".Translate(),
                ref s.intelPlayerColoniesAlwaysKnown,
                "TSA_WD_Intel_PlayerColoniesAlwaysKnownTip".Translate());
            SettingsUI.DrawCheckbox(l, "TSA_WD_Intel_NotifyJumpUndiscovered".Translate(),
                ref s.intelNotifyJumpUndiscovered,
                "TSA_WD_Intel_NotifyJumpUndiscoveredTip".Translate());
            SettingsUI.DrawCheckbox(l, "TSA_WD_Intel_UncoverOnRaid".Translate(),
                ref s.intelUncoverOnRaid,
                "TSA_WD_Intel_UncoverOnRaidTip".Translate());
            SettingsUI.DrawCheckbox(l, "TSA_WD_Intel_ListUndiscoveredTravelers".Translate(),
                ref s.intelListUndiscoveredTravelers,
                "TSA_WD_Intel_ListUndiscoveredTravelersTip".Translate());
            SettingsUI.DrawCheckbox(l, "TSA_WD_Intel_ThreatKnownOnly".Translate(),
                ref s.intelThreatKnownOnly,
                "TSA_WD_Intel_ThreatKnownOnlyTip".Translate());
            SettingsUI.DrawCheckbox(l, "TSA_WD_Intel_TradersRequireKnown".Translate(),
                ref s.intelTradersRequireKnownTarget,
                "TSA_WD_Intel_TradersRequireKnownTip".Translate());
            SettingsUI.DrawCheckbox(l, "TSA_WD_Intel_SpottedCaravansRemainVisible".Translate(),
                ref s.intelSpottedCaravansRemainVisible,
                "TSA_WD_Intel_SpottedCaravansRemainVisibleTip".Translate());
        }

        private void DrawSightRings(Listing_Standard l, WorldDominationSettings s)
        {
            if (!SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Intel_HeaderSightRings".Translate(), ref ringsExpanded, SettingsUI.SectionHeaderColor,
                "TSA_WD_Intel_HeaderSightRingsTip".Translate()))
                return;

            s.intelLosFrac = SettingsUI.LabeledSlider(l, "TSA_WD_Intel_LosFrac".Translate(), s.intelLosFrac, 0.10f, 1f,
                "TSA_WD_Intel_LosFracTip".Translate(), 0.01f, SliderFormat.Percent, WorldDominationSettings.DefIntelLosFrac);
        }

        private void DrawSettlementLos(Listing_Standard l, WorldDominationSettings s)
        {
            if (!SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Intel_HeaderSettlementLos".Translate(), ref settlementLosExpanded, SettingsUI.SectionHeaderColor,
                "TSA_WD_Intel_HeaderSettlementLosTip".Translate()))
                return;

            s.intelSettlementLosMaxMult = SettingsUI.LabeledSlider(l, "TSA_WD_Intel_SettlementLosMaxMult".Translate(), s.intelSettlementLosMaxMult, 1f, 4f,
                "TSA_WD_Intel_SettlementLosMaxMultTip".Translate(), 0.05f, SliderFormat.Fixed2, WorldDominationSettings.DefIntelSettlementLosMaxMult);
        }

        private void DrawTravelerLos(Listing_Standard l, WorldDominationSettings s)
        {
            if (!SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Intel_HeaderTravelerLos".Translate(), ref travelerLosExpanded, SettingsUI.SectionHeaderColor,
                "TSA_WD_Intel_HeaderTravelerLosTip".Translate()))
                return;

            s.intelTravelerMaxLosT1 = SettingsUI.LabeledSlider(l, "TSA_WD_Intel_TravelerMaxLosT1".Translate(), s.intelTravelerMaxLosT1, 1f, 20f,
                "TSA_WD_Intel_TravelerMaxLosT1Tip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefIntelTravelerMaxLosT1);
            s.intelTravelerMaxLosT2 = SettingsUI.LabeledSlider(l, "TSA_WD_Intel_TravelerMaxLosT2".Translate(), s.intelTravelerMaxLosT2, 1f, 20f,
                "TSA_WD_Intel_TravelerMaxLosT2Tip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefIntelTravelerMaxLosT2);
            s.intelTravelerMaxLosT3 = SettingsUI.LabeledSlider(l, "TSA_WD_Intel_TravelerMaxLosT3".Translate(), s.intelTravelerMaxLosT3, 1f, 20f,
                "TSA_WD_Intel_TravelerMaxLosT3Tip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefIntelTravelerMaxLosT3);
            s.intelTravelerMaxLosT4 = SettingsUI.LabeledSlider(l, "TSA_WD_Intel_TravelerMaxLosT4".Translate(), s.intelTravelerMaxLosT4, 1f, 20f,
                "TSA_WD_Intel_TravelerMaxLosT4Tip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefIntelTravelerMaxLosT4);

            s.intelTravelerStrengthT2 = SettingsUI.LabeledSlider(l, "TSA_WD_Intel_TravelerStrengthT2".Translate(), s.intelTravelerStrengthT2, 1f, 2000f,
                "TSA_WD_Intel_TravelerStrengthT2Tip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefIntelTravelerStrengthT2);
            s.intelTravelerStrengthT3 = SettingsUI.LabeledSlider(l, "TSA_WD_Intel_TravelerStrengthT3".Translate(), s.intelTravelerStrengthT3, 1f, 4000f,
                "TSA_WD_Intel_TravelerStrengthT3Tip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefIntelTravelerStrengthT3);
            s.intelTravelerStrengthT4 = SettingsUI.LabeledSlider(l, "TSA_WD_Intel_TravelerStrengthT4".Translate(), s.intelTravelerStrengthT4, 1f, 8000f,
                "TSA_WD_Intel_TravelerStrengthT4Tip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefIntelTravelerStrengthT4);
        }

        private void DrawScouts(Listing_Standard l, WorldDominationSettings s)
        {
            if (!SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Intel_HeaderScoutsStrategic".Translate(), ref scoutsExpanded, SettingsUI.SectionHeaderColor,
                "TSA_WD_Intel_HeaderScoutsStrategicTip".Translate()))
                return;

            s.intelScoutMaxLos = SettingsUI.LabeledSlider(l, "TSA_WD_Intel_ScoutMaxLos".Translate(), s.intelScoutMaxLos, 1f, 20f,
                "TSA_WD_Intel_ScoutMaxLosTip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefIntelScoutMaxLos);
            s.intelScoutSpeedMult = SettingsUI.LabeledSlider(l, "TSA_WD_Intel_ScoutSpeedMult".Translate(), s.intelScoutSpeedMult,
                WorldDominationSettings.MinIntelScoutSpeedMult, WorldDominationSettings.MaxIntelScoutSpeedMult,
                "TSA_WD_Intel_ScoutSpeedMultTip".Translate(), 0.05f, SliderFormat.Fixed2, WorldDominationSettings.DefIntelScoutSpeedMult);

            s.weightScout = SettingsUI.LabeledSlider(l, "TSA_WD_Intel_WeightScout".Translate(), s.weightScout, 0f, 400f,
                "TSA_WD_Intel_WeightScoutTip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefWeightScout);
            s.settlementIntelScoutClearWeightMult = SettingsUI.LabeledSlider(l, "TSA_WD_Intel_ScoutClearWeightMult".Translate(), s.settlementIntelScoutClearWeightMult, 0.5f, 5f,
                "TSA_WD_Intel_ScoutClearWeightMultTip".Translate(), 0.05f, SliderFormat.Fixed2, WorldDominationSettings.DefSettlementIntelScoutClearWeightMult);
            s.settlementIntelScoutStrengthMin = SettingsUI.LabeledSlider(l, "TSA_WD_Intel_ScoutStrengthMin".Translate(), s.settlementIntelScoutStrengthMin, 50f, 1000f,
                "TSA_WD_Intel_ScoutStrengthMinTip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefSettlementIntelScoutStrengthMin);
            s.settlementIntelScoutStrengthMax = SettingsUI.LabeledSlider(l, "TSA_WD_Intel_ScoutStrengthMax".Translate(), s.settlementIntelScoutStrengthMax, 50f, 1000f,
                "TSA_WD_Intel_ScoutStrengthMaxTip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefSettlementIntelScoutStrengthMax);
            s.intelPlayerScoutMaxLifetimeDays = SettingsUI.LabeledSlider(l, "TSA_WD_Intel_PlayerScoutMaxLifetimeDays".Translate(), s.intelPlayerScoutMaxLifetimeDays, 0.5f, 15f,
                "TSA_WD_Intel_PlayerScoutMaxLifetimeDaysTip".Translate(), 0.5f, SliderFormat.Fixed1, WorldDominationSettings.DefIntelPlayerScoutMaxLifetimeDays);
            s.settlementIntelScoutCooldownDays = SettingsUI.LabeledSlider(l, "TSA_WD_Intel_ScoutCooldownDays".Translate(), s.settlementIntelScoutCooldownDays, 0f, 30f,
                "TSA_WD_Intel_ScoutCooldownDaysTip".Translate(), 0.5f, SliderFormat.Fixed1, WorldDominationSettings.DefSettlementIntelScoutCooldownDays);
            s.settlementIntelScoutCrackFallChance = SettingsUI.LabeledSlider(l, "TSA_WD_Intel_ScoutCrackFall".Translate(), s.settlementIntelScoutCrackFallChance, 0f, 1f,
                "TSA_WD_Intel_ScoutCrackFallTip".Translate(), 0.01f, SliderFormat.Percent, WorldDominationSettings.DefSettlementIntelScoutCrackFallChance);
            s.settlementIntelScoutBearingAvoidDeg = SettingsUI.LabeledSlider(l, "TSA_WD_Intel_ScoutBearingAvoid".Translate(), s.settlementIntelScoutBearingAvoidDeg, 0f, 180f,
                "TSA_WD_Intel_ScoutBearingAvoidTip".Translate(), 5f, SliderFormat.Fixed0, WorldDominationSettings.DefSettlementIntelScoutBearingAvoidDeg);
        }

        private void DrawGotBurned(Listing_Standard l, WorldDominationSettings s)
        {
            if (!SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Intel_HeaderGotBurned".Translate(), ref burnedExpanded, SettingsUI.SectionHeaderColor,
                "TSA_WD_Intel_HeaderGotBurnedTip".Translate()))
                return;

            s.settlementIntelBurnedHiddenStrengthPct = SettingsUI.LabeledSlider(l, "TSA_WD_Intel_BurnedHiddenPct".Translate(), s.settlementIntelBurnedHiddenStrengthPct, 0f, 2f,
                "TSA_WD_Intel_BurnedHiddenPctTip".Translate(), 0.05f, SliderFormat.Percent, WorldDominationSettings.DefSettlementIntelBurnedHiddenStrengthPct);
            s.settlementIntelGotBurnedDays = SettingsUI.LabeledSlider(l, "TSA_WD_Intel_GotBurnedDays".Translate(), s.settlementIntelGotBurnedDays, 1f, 60f,
                "TSA_WD_Intel_GotBurnedDaysTip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefSettlementIntelGotBurnedDays);
            s.settlementIntelBurnTriggerRatio = SettingsUI.LabeledSlider(l, "TSA_WD_Intel_BurnTriggerRatio".Translate(), s.settlementIntelBurnTriggerRatio, 1f, 3f,
                "TSA_WD_Intel_BurnTriggerRatioTip".Translate(), 0.05f, SliderFormat.Fixed2, WorldDominationSettings.DefSettlementIntelBurnTriggerRatio);
        }
    }
}
