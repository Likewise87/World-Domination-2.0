using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    public class Dialog_NotificationSettings : Window
    {
        private Vector2 scrollPosition;
        private readonly string windowTitle;
        private bool nearbyExpanded = true;
        private bool outpostsRaidsExpanded = true;
        private bool artilleryExpanded = true;
        private bool atTurretsExpanded = true;
        private bool diplomacyExpanded = true;
        private bool worldThreatsExpanded = true;
        private bool alertsExpanded = true;

        public override Vector2 InitialSize => new Vector2(850f, 750f);

        public Dialog_NotificationSettings()
        {
            doCloseButton = true;
            forcePause = true;
            closeOnClickedOutside = true;
            windowTitle = "TSA_WD_BtnNotifications".Translate();
            optionalTitle = null;
        }

        
        private static Dialog_NotificationSettings embeddedHost;

        public static void DrawEmbedded(Rect inRect)
        {
            if (embeddedHost == null) embeddedHost = new Dialog_NotificationSettings();
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
            Rect scrollViewRect = new Rect(0f, 0f, contentWidth, 3000f);
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
            SettingsUI.DrawMenuTopBar(l, SettingsUI.ResetPageToDefaultsLabel, () => s.ResetNotifications(),
                () => { alertsExpanded = nearbyExpanded = worldThreatsExpanded = outpostsRaidsExpanded = artilleryExpanded = atTurretsExpanded = diplomacyExpanded = true; },
                () => { alertsExpanded = nearbyExpanded = worldThreatsExpanded = outpostsRaidsExpanded = artilleryExpanded = atTurretsExpanded = diplomacyExpanded = false; });
            
            // 1. Right-side alerts
            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Notify_HeaderAlerts".Translate(), ref alertsExpanded, SettingsUI.SectionHeaderColor))
            {
                SettingsUI.DrawCheckbox(l, "TSA_WD_Notify_ThreatLevel".Translate(),
                    ref s.notifyThreatLevel,
                    SettingsUI.TooltipWithDefault("TSA_WD_Notify_ThreatLevelTooltip".Translate(), WorldDominationSettings.DefNotifyThreatLevel));

                SettingsUI.DrawCheckbox(l, "TSA_WD_Notify_CriticalFood".Translate(),
                    ref s.notifyCriticalFood,
                    SettingsUI.TooltipWithDefault("TSA_WD_Notify_CriticalFoodTooltip".Translate(), WorldDominationSettings.DefNotifyCriticalFood));

                SettingsUI.DrawCheckbox(l, "TSA_WD_Notify_DropPodDeliveryInAaRange".Translate(),
                    ref s.notifyDropPodDeliveryInAaRange,
                    SettingsUI.TooltipWithDefault("TSA_WD_Notify_DropPodDeliveryInAaRangeTooltip".Translate(), WorldDominationSettings.DefNotifyDropPodDeliveryInAaRange));

                SettingsUI.DrawCheckbox(l, "TSA_WD_Notify_OutpostUpkeep".Translate(),
                    ref s.notifyOutpostUpkeep,
                    SettingsUI.TooltipWithDefault("TSA_WD_Notify_OutpostUpkeepTooltip".Translate(), WorldDominationSettings.DefNotifyOutpostUpkeep));

                SettingsUI.DrawCheckbox(l, "TSA_WD_Notify_ConstructionInsufficientStrength".Translate(),
                    ref s.notifyConstructionInsufficientStrength,
                    SettingsUI.TooltipWithDefault("TSA_WD_Notify_ConstructionInsufficientStrengthTooltip".Translate(), WorldDominationSettings.DefNotifyConstructionInsufficientStrength));

                SettingsUI.DrawCheckbox(l, "TSA_WD_Notify_ConstructionInsufficientMaterials".Translate(),
                    ref s.notifyConstructionInsufficientMaterials,
                    SettingsUI.TooltipWithDefault("TSA_WD_Notify_ConstructionInsufficientMaterialsTooltip".Translate(), WorldDominationSettings.DefNotifyConstructionInsufficientMaterials));

                SettingsUI.DrawCheckbox(l, "TSA_WD_Notify_BridgeDeconstructBlocked".Translate(),
                    ref s.notifyBridgeDeconstructBlocked,
                    SettingsUI.TooltipWithDefault("TSA_WD_Notify_BridgeDeconstructBlockedTooltip".Translate(), WorldDominationSettings.DefNotifyBridgeDeconstructBlocked));

                SettingsUI.DrawCheckbox(l, "TSA_WD_Notify_RoadCancelledByTrench".Translate(),
                    ref s.notifyRoadCancelledByTrench,
                    SettingsUI.TooltipWithDefault("TSA_WD_Notify_RoadCancelledByTrenchTooltip".Translate(), WorldDominationSettings.DefNotifyRoadCancelledByTrench));

                SettingsUI.DrawCheckbox(l, "TSA_WD_Notify_TrenchCancelledByRoad".Translate(),
                    ref s.notifyTrenchCancelledByRoad,
                    SettingsUI.TooltipWithDefault("TSA_WD_Notify_TrenchCancelledByRoadTooltip".Translate(), WorldDominationSettings.DefNotifyTrenchCancelledByRoad));

                SettingsUI.DrawCheckbox(l, "TSA_WD_Notify_OutpostNoProduction".Translate(),
                    ref s.notifyOutpostNoProduction,
                    SettingsUI.TooltipWithDefault("TSA_WD_Notify_OutpostNoProductionTooltip".Translate(), WorldDominationSettings.DefNotifyOutpostNoProduction));

                SettingsUI.DrawCheckbox(l, "TSA_WD_Notify_OutpostUnusedExperts".Translate(),
                    ref s.notifyOutpostUnusedExperts,
                    SettingsUI.TooltipWithDefault("TSA_WD_Notify_OutpostUnusedExpertsTooltip".Translate(), WorldDominationSettings.DefNotifyOutpostUnusedExperts));

                SettingsUI.DrawCheckbox(l, "TSA_WD_Notify_MidGameActive".Translate(),
                    ref s.notifyMidGameActive,
                    SettingsUI.TooltipWithDefault("TSA_WD_Notify_MidGameActiveTooltip".Translate(), WorldDominationSettings.DefNotifyMidGameActive));

                SettingsUI.DrawCheckbox(l, "TSA_WD_Notify_LateGameActive".Translate(),
                    ref s.notifyLateGameActive,
                    SettingsUI.TooltipWithDefault("TSA_WD_Notify_LateGameActiveTooltip".Translate(), WorldDominationSettings.DefNotifyLateGameActive));
            }

            // 2. Nearby world events (radius-gated)
            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Notify_HeaderNearby".Translate(), ref nearbyExpanded, SettingsUI.SectionHeaderColor))
            {
                s.notificationRadiusTiles = SettingsUI.LabeledSlider(l, "TSA_WD_Notify_NotificationRadius".Translate(), s.notificationRadiusTiles, 1f, 250f,
                    "TSA_WD_Notify_NotificationRadiusTooltip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefNotificationRadiusTiles);

                SettingsUI.DrawCheckbox(l, "TSA_WD_Notify_NewSettlement".Translate(),
                    ref s.notifyNewSettlement,
                    SettingsUI.TooltipWithDefault("TSA_WD_Notify_NewSettlementTooltip".Translate(), WorldDominationSettings.DefNotifyNewSettlement));

                SettingsUI.DrawCheckbox(l, "TSA_WD_Notify_NpcConquestSettlement".Translate(),
                    ref s.notifyNpcConquestSettlement,
                    SettingsUI.TooltipWithDefault("TSA_WD_Notify_NpcConquestSettlementTooltip".Translate(), WorldDominationSettings.DefNotifyNpcConquestSettlement));

                SettingsUI.DrawCheckbox(l, "TSA_WD_Notify_SettlementRaided".Translate(),
                    ref s.notifySettlementRaided,
                    SettingsUI.TooltipWithDefault("TSA_WD_Notify_SettlementRaidedTooltip".Translate(), WorldDominationSettings.DefNotifySettlementRaided));

                SettingsUI.DrawCheckbox(l, "TSA_WD_Notify_SettlementRazed".Translate(),
                    ref s.notifySettlementRazed,
                    SettingsUI.TooltipWithDefault("TSA_WD_Notify_SettlementRazedTooltip".Translate(), WorldDominationSettings.DefNotifySettlementRazed));
            }

            // 3. World threat actions (desperation / turtle / strong-faction war)
            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Notify_HeaderWorldThreats".Translate(), ref worldThreatsExpanded, SettingsUI.SectionHeaderColor,
                "TSA_WD_Notify_HeaderWorldThreatsTip".Translate()))
            {
                SettingsUI.DrawCheckbox(l, "TSA_WD_Notify_DesperationRaid".Translate(),
                    ref s.notifyDesperationRaid,
                    SettingsUI.TooltipWithDefault("TSA_WD_Notify_DesperationRaidTooltip".Translate(), WorldDominationSettings.DefNotifyDesperationRaid));

                SettingsUI.DrawCheckbox(l, "TSA_WD_Notify_Turtle".Translate(),
                    ref s.notifyTurtle,
                    SettingsUI.TooltipWithDefault("TSA_WD_Notify_TurtleTooltip".Translate(), WorldDominationSettings.DefNotifyTurtle));

                SettingsUI.DrawCheckbox(l, "TSA_WD_Notify_StrongFactionWar".Translate(),
                    ref s.notifyStrongFactionWar,
                    SettingsUI.TooltipWithDefault("TSA_WD_Notify_StrongFactionWarTooltip".Translate(), WorldDominationSettings.DefNotifyStrongFactionWar));
            }

            // 4. Your outposts and raids
            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Notify_HeaderOutpostsRaids".Translate(), ref outpostsRaidsExpanded, SettingsUI.SectionHeaderColor))
            {
                SettingsUI.DrawCheckbox(l, "TSA_WD_Notify_OutpostDestroyed".Translate(),
                    ref s.notifyOutpostDestroyed,
                    SettingsUI.TooltipWithDefault("TSA_WD_Notify_OutpostDestroyedTooltip".Translate(), WorldDominationSettings.DefNotifyOutpostDestroyed));

                SettingsUI.DrawCheckbox(l, "TSA_WD_Notify_IncomingRaidColony".Translate(),
                    ref s.notifyIncomingRaidColony,
                    SettingsUI.TooltipWithDefault("TSA_WD_Notify_IncomingRaidColonyTooltip".Translate(), WorldDominationSettings.DefNotifyIncomingRaidColony));

                SettingsUI.DrawCheckbox(l, "TSA_WD_Notify_RaidArrivalColony".Translate(),
                    ref s.notifyRaidArrivalColony,
                    SettingsUI.TooltipWithDefault("TSA_WD_Notify_RaidArrivalColonyTooltip".Translate(), WorldDominationSettings.DefNotifyRaidArrivalColony));

                SettingsUI.DrawCheckbox(l, "TSA_WD_Notify_IncomingRaidOutpost".Translate(),
                    ref s.notifyIncomingRaidOutpost,
                    SettingsUI.TooltipWithDefault("TSA_WD_Notify_IncomingRaidOutpostTooltip".Translate(), WorldDominationSettings.DefNotifyIncomingRaidOutpost));

                SettingsUI.DrawCheckbox(l, "TSA_WD_Notify_RaidArrivalOutpost".Translate(),
                    ref s.notifyRaidArrivalOutpost,
                    SettingsUI.TooltipWithDefault("TSA_WD_Notify_RaidArrivalOutpostTooltip".Translate(), WorldDominationSettings.DefNotifyRaidArrivalOutpost));

                SettingsUI.DrawCheckbox(l, "TSA_WD_Notify_RaidDivertedFromPlayer".Translate(),
                    ref s.notifyRaidDivertedFromPlayer,
                    SettingsUI.TooltipWithDefault("TSA_WD_Notify_RaidDivertedFromPlayerTooltip".Translate(), WorldDominationSettings.DefNotifyRaidDivertedFromPlayer));

                SettingsUI.DrawCheckbox(l, "TSA_WD_Notify_RaidPartyDestroyed".Translate(),
                    ref s.notifyRaidPartyDestroyed,
                    SettingsUI.TooltipWithDefault("TSA_WD_Notify_RaidPartyDestroyedTooltip".Translate(), WorldDominationSettings.DefNotifyRaidPartyDestroyed));

                SettingsUI.DrawCheckbox(l, "TSA_WD_Difficulty_NotifyOutpostInc".Translate(),
                    ref s.notifyOutpostIncident,
                    SettingsUI.TooltipWithDefault("TSA_WD_Difficulty_NotifyOutpostIncTooltip".Translate(), WorldDominationSettings.DefNotifyOutpostIncident));

                SettingsUI.DrawCheckbox(l, "TSA_WD_Notify_WarehouseGoodsArrived".Translate(),
                    ref s.notifyWarehouseGoodsArrived,
                    SettingsUI.TooltipWithDefault("TSA_WD_Notify_WarehouseGoodsArrivedTooltip".Translate(), WorldDominationSettings.DefNotifyWarehouseGoodsArrived));

                SettingsUI.DrawCheckbox(l, "TSA_WD_Notify_OutpostDeliveryToColonyArrived".Translate(),
                    ref s.notifyOutpostDeliveryToColonyArrived,
                    SettingsUI.TooltipWithDefault("TSA_WD_Notify_OutpostDeliveryToColonyArrivedTooltip".Translate(), WorldDominationSettings.DefNotifyOutpostDeliveryToColonyArrived));

                SettingsUI.DrawCheckbox(l, "TSA_WD_Notify_PrisonerRecruitedUnderway".Translate(),
                    ref s.notifyPrisonerRecruitedUnderway,
                    SettingsUI.TooltipWithDefault("TSA_WD_Notify_PrisonerRecruitedUnderwayTooltip".Translate(), WorldDominationSettings.DefNotifyPrisonerRecruitedUnderway));

                SettingsUI.DrawCheckbox(l, "TSA_WD_Notify_SettlementBuyCompleted".Translate(),
                    ref s.notifySettlementBuyCompleted,
                    SettingsUI.TooltipWithDefault("TSA_WD_Notify_SettlementBuyCompletedTooltip".Translate(), WorldDominationSettings.DefNotifySettlementBuyCompleted));
                SettingsUI.DrawCheckbox(l, "TSA_WD_Notify_SettlementBuyAborted".Translate(),
                    ref s.notifySettlementBuyAborted,
                    SettingsUI.TooltipWithDefault("TSA_WD_Notify_SettlementBuyAbortedTooltip".Translate(), WorldDominationSettings.DefNotifySettlementBuyAborted));

                SettingsUI.DrawCheckbox(l, "TSA_WD_Notify_NegotiateStarted".Translate(),
                    ref s.notifyDiplomacyNegotiateStarted,
                    SettingsUI.TooltipWithDefault("TSA_WD_Notify_NegotiateStartedTooltip".Translate(), WorldDominationSettings.DefNotifyDiplomacyNegotiateStarted));
                SettingsUI.DrawCheckbox(l, "TSA_WD_Notify_NegotiateCompleted".Translate(),
                    ref s.notifyDiplomacyNegotiateCompleted,
                    SettingsUI.TooltipWithDefault("TSA_WD_Notify_NegotiateCompletedTooltip".Translate(), WorldDominationSettings.DefNotifyDiplomacyNegotiateCompleted));
                SettingsUI.DrawCheckbox(l, "TSA_WD_Notify_NegotiateAborted".Translate(),
                    ref s.notifyDiplomacyNegotiateAborted,
                    SettingsUI.TooltipWithDefault("TSA_WD_Notify_NegotiateAbortedTooltip".Translate(), WorldDominationSettings.DefNotifyDiplomacyNegotiateAborted));

                SettingsUI.DrawCheckbox(l, "TSA_WD_Notify_BribeSettlementCompleted".Translate(),
                    ref s.notifyBribeSettlementCompleted,
                    SettingsUI.TooltipWithDefault("TSA_WD_Notify_BribeSettlementCompletedTooltip".Translate(), WorldDominationSettings.DefNotifyBribeSettlementCompleted));
                SettingsUI.DrawCheckbox(l, "TSA_WD_Notify_BribeSettlementAborted".Translate(),
                    ref s.notifyBribeSettlementAborted,
                    SettingsUI.TooltipWithDefault("TSA_WD_Notify_BribeSettlementAbortedTooltip".Translate(), WorldDominationSettings.DefNotifyBribeSettlementAborted));
                SettingsUI.DrawCheckbox(l, "TSA_WD_Notify_BribeRaidCompleted".Translate(),
                    ref s.notifyBribeRaidCompleted,
                    SettingsUI.TooltipWithDefault("TSA_WD_Notify_BribeRaidCompletedTooltip".Translate(), WorldDominationSettings.DefNotifyBribeRaidCompleted));
                SettingsUI.DrawCheckbox(l, "TSA_WD_Notify_BribeRaidAborted".Translate(),
                    ref s.notifyBribeRaidAborted,
                    SettingsUI.TooltipWithDefault("TSA_WD_Notify_BribeRaidAbortedTooltip".Translate(), WorldDominationSettings.DefNotifyBribeRaidAborted));
                SettingsUI.DrawCheckbox(l, "TSA_WD_Notify_BribeLostInTransit".Translate(),
                    ref s.notifyBribeLostInTransit,
                    SettingsUI.TooltipWithDefault("TSA_WD_Notify_BribeLostInTransitTooltip".Translate(), WorldDominationSettings.DefNotifyBribeLostInTransit));
                SettingsUI.DrawCheckbox(l, "TSA_WD_Notify_BribeCeasefireExpired".Translate(),
                    ref s.notifyBribeCeasefireExpired,
                    SettingsUI.TooltipWithDefault("TSA_WD_Notify_BribeCeasefireExpiredTooltip".Translate(), WorldDominationSettings.DefNotifyBribeCeasefireExpired));

                SettingsUI.DrawCheckbox(l, "TSA_WD_Notify_PlayerCaravanClash".Translate(),
                    ref s.notifyPlayerCaravanClash,
                    SettingsUI.TooltipWithDefault("TSA_WD_Notify_PlayerCaravanClashTooltip".Translate(), WorldDominationSettings.DefNotifyPlayerCaravanClash));

                SettingsUI.DrawCheckbox(l, "TSA_WD_Notify_RapidResponseCaravanClash".Translate(),
                    ref s.notifyRapidResponseCaravanClash,
                    SettingsUI.TooltipWithDefault("TSA_WD_Notify_RapidResponseCaravanClashTooltip".Translate(), WorldDominationSettings.DefNotifyRapidResponseCaravanClash));

                SettingsUI.DrawCheckbox(l, "TSA_WD_Notify_TravelerPollutionDamage".Translate(),
                    ref s.notifyTravelerPollutionDamage,
                    SettingsUI.TooltipWithDefault("TSA_WD_Notify_TravelerPollutionDamageTooltip".Translate(), WorldDominationSettings.DefNotifyTravelerPollutionDamage));

                SettingsUI.DrawCheckbox(l, "TSA_WD_Notify_OutpostPollutionDamage".Translate(),
                    ref s.notifyOutpostPollutionDamage,
                    SettingsUI.TooltipWithDefault("TSA_WD_Notify_OutpostPollutionDamageTooltip".Translate(), WorldDominationSettings.DefNotifyOutpostPollutionDamage));

                SettingsUI.DrawCheckbox(l, "TSA_WD_Notify_SettlementDestroyedByPollution".Translate(),
                    ref s.notifySettlementDestroyedByPollution,
                    SettingsUI.TooltipWithDefault("TSA_WD_Notify_SettlementDestroyedByPollutionTooltip".Translate(), WorldDominationSettings.DefNotifySettlementDestroyedByPollution));
            }

            // 4. Mortar and anti-air
            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Notify_HeaderMortarAntiAir".Translate(), ref artilleryExpanded, SettingsUI.SectionHeaderColor))
            {
                SettingsUI.DrawCheckbox(l, "TSA_WD_Notify_MortarHit".Translate(),
                    ref s.notifyMortarHit,
                    SettingsUI.TooltipWithDefault("TSA_WD_Notify_MortarHitTooltip".Translate(), WorldDominationSettings.DefNotifyMortarHit));

                SettingsUI.DrawCheckbox(l, "TSA_WD_Notify_MortarMiss".Translate(),
                    ref s.notifyMortarMiss,
                    SettingsUI.TooltipWithDefault("TSA_WD_Notify_MortarMissTooltip".Translate(), WorldDominationSettings.DefNotifyMortarMiss));

                SettingsUI.DrawCheckbox(l, "TSA_WD_Notify_AntiAirHit".Translate(),
                    ref s.notifyAntiAirHit,
                    SettingsUI.TooltipWithDefault("TSA_WD_Notify_AntiAirHitTooltip".Translate(), WorldDominationSettings.DefNotifyAntiAirHit));

                SettingsUI.DrawCheckbox(l, "TSA_WD_Notify_PlayerAntiAirVsHostileMortarShell".Translate(),
                    ref s.notifyPlayerAntiAirVsHostileMortarShell,
                    SettingsUI.TooltipWithDefault("TSA_WD_Notify_PlayerAntiAirVsHostileMortarShellTooltip".Translate(), WorldDominationSettings.DefNotifyPlayerAntiAirVsHostileMortarShell));

                SettingsUI.DrawCheckbox(l, "TSA_WD_Notify_NpcMortarHitPlayer".Translate(),
                    ref s.notifyNpcMortarHitPlayer,
                    SettingsUI.TooltipWithDefault("TSA_WD_Notify_NpcMortarHitPlayerTooltip".Translate(), WorldDominationSettings.DefNotifyNpcMortarHitPlayer));

                SettingsUI.DrawCheckbox(l, "TSA_WD_Notify_T4AntiAirHitPlayer".Translate(),
                    ref s.notifyT4AntiAirHitPlayer,
                    SettingsUI.TooltipWithDefault("TSA_WD_Notify_T4AntiAirHitPlayerTooltip".Translate(), WorldDominationSettings.DefNotifyT4AntiAirHitPlayer));

                SettingsUI.DrawCheckbox(l, "TSA_WD_Notify_PlayerMortarShellShotDown".Translate(),
                    ref s.notifyPlayerMortarShellShotDown,
                    SettingsUI.TooltipWithDefault("TSA_WD_Notify_PlayerMortarShellShotDownTooltip".Translate(), WorldDominationSettings.DefNotifyPlayerMortarShellShotDown));

                SettingsUI.DrawCheckbox(l, "TSA_WD_Notify_NpcMortarHitNpc".Translate(),
                    ref s.notifyNpcMortarHitNpc,
                    SettingsUI.TooltipWithDefault("TSA_WD_Notify_NpcMortarHitNpcTooltip".Translate(), WorldDominationSettings.DefNotifyNpcMortarHitNpc));
            }

            // 4b. AT Turrets (separate from mortar / T4 settlement guns)
            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Notify_HeaderAtTurrets".Translate(), ref atTurretsExpanded, SettingsUI.SectionHeaderColor))
            {
                SettingsUI.DrawCheckbox(l, "TSA_WD_Notify_PlayerAtTurretKilledTarget".Translate(),
                    ref s.notifyPlayerAtTurretKilledTarget,
                    SettingsUI.TooltipWithDefault("TSA_WD_Notify_PlayerAtTurretKilledTargetTooltip".Translate(), WorldDominationSettings.DefNotifyPlayerAtTurretKilledTarget));

                SettingsUI.DrawCheckbox(l, "TSA_WD_Notify_PlayerAtTurretDamagedTarget".Translate(),
                    ref s.notifyPlayerAtTurretDamagedTarget,
                    SettingsUI.TooltipWithDefault("TSA_WD_Notify_PlayerAtTurretDamagedTargetTooltip".Translate(), WorldDominationSettings.DefNotifyPlayerAtTurretDamagedTarget));

                SettingsUI.DrawCheckbox(l, "TSA_WD_Notify_PlayerAtTurretDestroyed".Translate(),
                    ref s.notifyPlayerAtTurretDestroyed,
                    SettingsUI.TooltipWithDefault("TSA_WD_Notify_PlayerAtTurretDestroyedTooltip".Translate(), WorldDominationSettings.DefNotifyPlayerAtTurretDestroyed));

                SettingsUI.DrawCheckbox(l, "TSA_WD_Notify_NpcAtTurretDamagedPlayer".Translate(),
                    ref s.notifyNpcAtTurretDamagedPlayer,
                    SettingsUI.TooltipWithDefault("TSA_WD_Notify_NpcAtTurretDamagedPlayerTooltip".Translate(), WorldDominationSettings.DefNotifyNpcAtTurretDamagedPlayer));

                SettingsUI.DrawCheckbox(l, "TSA_WD_Notify_NpcAtTurretKilledPlayer".Translate(),
                    ref s.notifyNpcAtTurretKilledPlayer,
                    SettingsUI.TooltipWithDefault("TSA_WD_Notify_NpcAtTurretKilledPlayerTooltip".Translate(), WorldDominationSettings.DefNotifyNpcAtTurretKilledPlayer));
            }

            // 5. Diplomacy and buffs
            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Notify_HeaderDiplomacyBuffs".Translate(), ref diplomacyExpanded, SettingsUI.SectionHeaderColor))
            {
                SettingsUI.DrawCheckbox(l, "TSA_WD_Notify_LeaderHandicap".Translate(),
                    ref s.notifyLeaderHandicap,
                    SettingsUI.TooltipWithDefault("TSA_WD_Notify_LeaderHandicapTooltip".Translate(), WorldDominationSettings.DefNotifyLeaderHandicap));

                SettingsUI.DrawCheckbox(l, "TSA_WD_Notify_UnderdogBuff".Translate(),
                    ref s.notifyUnderdogBuff,
                    SettingsUI.TooltipWithDefault("TSA_WD_Notify_UnderdogBuffTooltip".Translate(), WorldDominationSettings.DefNotifyUnderdogBuff));

                SettingsUI.DrawCheckbox(l, "TSA_WD_Notify_ExpansionistZeal".Translate(),
                    ref s.notifyExpansionistZeal,
                    SettingsUI.TooltipWithDefault("TSA_WD_Notify_ExpansionistZealTooltip".Translate(), WorldDominationSettings.DefNotifyExpansionistZeal));

                SettingsUI.DrawCheckbox(l, "TSA_WD_Notify_AntiLeaderCoalition".Translate(),
                    ref s.notifyAntiLeaderCoalition,
                    SettingsUI.TooltipWithDefault("TSA_WD_Notify_AntiLeaderCoalitionTooltip".Translate(), WorldDominationSettings.DefNotifyAntiLeaderCoalition));

                SettingsUI.DrawCheckbox(l, "TSA_WD_Notify_RandomDiplomacy".Translate(),
                    ref s.notifyRandomDiplomacy,
                    SettingsUI.TooltipWithDefault("TSA_WD_Notify_RandomDiplomacyTooltip".Translate(), WorldDominationSettings.DefNotifyRandomDiplomacy));

                SettingsUI.DrawCheckbox(l, "TSA_WD_Notify_TradeAllyDiplomacy".Translate(),
                    ref s.notifyTradeAllyDiplomacy,
                    SettingsUI.TooltipWithDefault("TSA_WD_Notify_TradeAllyDiplomacyTooltip".Translate(), WorldDominationSettings.DefNotifyTradeAllyDiplomacy));
            }
        }
    }
}
