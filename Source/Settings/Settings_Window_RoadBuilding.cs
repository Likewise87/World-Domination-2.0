using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    public class Dialog_RoadBuildingSettings : Window
    {
        private Vector2 scrollPosition;
        private readonly string windowTitle;
        private bool roadsExpanded = true;
        private bool roadBlocksExpanded = true;
        private bool trapsExpanded = true;
        private bool atTurretsExpanded = true;
        private bool decontamExpanded = true;

        private static readonly Color SectionHeaderColor = new Color(0.55f, 0.85f, 1f);

        public override Vector2 InitialSize => new Vector2(850f, 700f);

        public Dialog_RoadBuildingSettings()
        {
            doCloseButton = true;
            forcePause = true;
            closeOnClickedOutside = true;
            windowTitle = "TSA_WD_BtnRoadBuilding".Translate();
            optionalTitle = null;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Rect contentRect = SettingsUI.DrawWindowTitle(inRect, windowTitle);
            float contentWidth = contentRect.width - 24f;
            Rect scrollViewRect = new Rect(0f, 0f, contentWidth, 4800f);

            Widgets.BeginScrollView(contentRect, ref scrollPosition, scrollViewRect);

            Listing_Standard l = new Listing_Standard();
            l.Begin(scrollViewRect);
            WorldDominationSettings s = WorldDominationMod.settings;

            l.Label("TSA_WD_RoadBuilding_Note".Translate());
            l.Gap(8f);
            SettingsUI.DrawMenuTopBar(l, SettingsUI.ResetPageToDefaultsLabel, () =>
            {
                s.ResetRoadBuildingFallback();
                WorldActions_Roads.ApplyVanillaRoadMovementSettings();
                WD_WorldLayer_MovementDifficultyOverlay.InvalidateAndDirtyIfActive();
            },
                () => { roadsExpanded = roadBlocksExpanded = trapsExpanded = atTurretsExpanded = decontamExpanded = true; },
                () => { roadsExpanded = roadBlocksExpanded = trapsExpanded = atTurretsExpanded = decontamExpanded = false; });

            float oldDirtMovement = s.fallbackDirtRoadMovement;
            float oldStoneMovement = s.fallbackStoneRoadMovement;
            float oldAsphaltMovement = s.fallbackAsphaltRoadMovement;
            float oldDirtWinter = s.fallbackDirtRoadWinterReduction;
            float oldStoneWinter = s.fallbackStoneRoadWinterReduction;
            float oldAsphaltWinter = s.fallbackAsphaltRoadWinterReduction;

            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_RoadBuilding_RoadsHeader".Translate(), ref roadsExpanded, SectionHeaderColor))
            {
                float[] roadRanges = { s.maxRoadRange, s.maxRoadRangeNpc };
                SettingsUI.MultiColumnSlider(l,
                    new[]
                    {
                        "TSA_WD_RoadBuilding_RoadRangePlayer".Translate().ToString(),
                        "TSA_WD_RoadBuilding_RoadRangeNpc".Translate().ToString()
                    },
                    roadRanges, new Vector2(5f, 150f),
                    new[]
                    {
                        "TSA_WD_RoadBuilding_RoadRangePlayerTip".Translate().ToString(),
                        "TSA_WD_RoadBuilding_RoadRangeNpcTip".Translate().ToString()
                    },
                    1f, SliderFormat.Fixed0, 38f,
                    new[]
                    {
                        (float)WorldDominationSettings.DefMaxRoadRange,
                        (float)WorldDominationSettings.DefMaxRoadRangeNpc
                    });
                s.maxRoadRange = (int)roadRanges[0];
                s.maxRoadRangeNpc = (int)roadRanges[1];

                DrawRoadSection(l, "TSA_WD_RoadBuilding_DirtRoad".Translate(),
                    ref s.fallbackDirtRoadMovement, ref s.fallbackDirtRoadWork, ref s.fallbackDirtRoadExpeditionStrength,
                    ref s.fallbackDirtRoadMinConstruction, ref s.fallbackDirtRoadWinterReduction,
                    WorldDominationSettings.DefFallbackDirtRoadMovement, WorldDominationSettings.DefFallbackDirtRoadWork,
                    WorldDominationSettings.DefFallbackDirtRoadExpeditionStrength,
                    WorldDominationSettings.DefFallbackDirtRoadMinConstruction,
                    WorldDominationSettings.DefFallbackDirtRoadWinterReduction);
                DrawRoadSection(l, "TSA_WD_RoadBuilding_StoneRoad".Translate(),
                    ref s.fallbackStoneRoadMovement, ref s.fallbackStoneRoadWork, ref s.fallbackStoneRoadExpeditionStrength,
                    ref s.fallbackStoneRoadMinConstruction, ref s.fallbackStoneRoadWinterReduction,
                    WorldDominationSettings.DefFallbackStoneRoadMovement, WorldDominationSettings.DefFallbackStoneRoadWork,
                    WorldDominationSettings.DefFallbackStoneRoadExpeditionStrength,
                    WorldDominationSettings.DefFallbackStoneRoadMinConstruction,
                    WorldDominationSettings.DefFallbackStoneRoadWinterReduction);
                DrawRoadSection(l, "TSA_WD_RoadBuilding_AsphaltRoad".Translate(),
                    ref s.fallbackAsphaltRoadMovement, ref s.fallbackAsphaltRoadWork, ref s.fallbackAsphaltRoadExpeditionStrength,
                    ref s.fallbackAsphaltRoadMinConstruction, ref s.fallbackAsphaltRoadWinterReduction,
                    WorldDominationSettings.DefFallbackAsphaltRoadMovement, WorldDominationSettings.DefFallbackAsphaltRoadWork,
                    WorldDominationSettings.DefFallbackAsphaltRoadExpeditionStrength,
                    WorldDominationSettings.DefFallbackAsphaltRoadMinConstruction,
                    WorldDominationSettings.DefFallbackAsphaltRoadWinterReduction);
            }

            bool roadMovementSettingsChanged =
                !Mathf.Approximately(oldDirtMovement, s.fallbackDirtRoadMovement)
                || !Mathf.Approximately(oldStoneMovement, s.fallbackStoneRoadMovement)
                || !Mathf.Approximately(oldAsphaltMovement, s.fallbackAsphaltRoadMovement)
                || !Mathf.Approximately(oldDirtWinter, s.fallbackDirtRoadWinterReduction)
                || !Mathf.Approximately(oldStoneWinter, s.fallbackStoneRoadWinterReduction)
                || !Mathf.Approximately(oldAsphaltWinter, s.fallbackAsphaltRoadWinterReduction);
            if (roadMovementSettingsChanged)
            {
                WorldActions_Roads.ApplyVanillaRoadMovementSettings();
                WD_WorldLayer_MovementDifficultyOverlay.InvalidateAndDirtyIfActive();
            }

            float oldLightPenalty = s.roadBlockLightFlatPenalty;
            float oldNormalPenalty = s.roadBlockNormalFlatPenalty;
            float oldHeavyPenalty = s.roadBlockHeavyFlatPenalty;

            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_RoadBuilding_RoadBlocksHeader".Translate(), ref roadBlocksExpanded, SectionHeaderColor))
            {
                s.maxRoadBlockRange = (int)SettingsUI.LabeledSlider(l, "TSA_WD_RoadBuilding_RoadBlockRange".Translate(), s.maxRoadBlockRange, 1f, 30f,
                    "TSA_WD_RoadBuilding_RoadBlockRangeTip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefMaxRoadBlockRange);

                DrawRoadBlockSection(l, "TSA_WD_RoadBlockLight".Translate(),
                    ref s.roadBlockLightWork, ref s.roadBlockLightExpeditionStrength, ref s.roadBlockLightFlatPenalty, ref s.roadBlockLightMaxHealth,
                    WorldDominationSettings.DefRoadBlockLightWork, WorldDominationSettings.DefRoadBlockLightExpeditionStrength,
                    WorldDominationSettings.DefRoadBlockLightFlatPenalty, WorldDominationSettings.DefRoadBlockLightMaxHealth);
                DrawRoadBlockSection(l, "TSA_WD_RoadBlockNormal".Translate(),
                    ref s.roadBlockNormalWork, ref s.roadBlockNormalExpeditionStrength, ref s.roadBlockNormalFlatPenalty, ref s.roadBlockNormalMaxHealth,
                    WorldDominationSettings.DefRoadBlockNormalWork, WorldDominationSettings.DefRoadBlockNormalExpeditionStrength,
                    WorldDominationSettings.DefRoadBlockNormalFlatPenalty, WorldDominationSettings.DefRoadBlockNormalMaxHealth);
                DrawRoadBlockSection(l, "TSA_WD_RoadBlockHeavy".Translate(),
                    ref s.roadBlockHeavyWork, ref s.roadBlockHeavyExpeditionStrength, ref s.roadBlockHeavyFlatPenalty, ref s.roadBlockHeavyMaxHealth,
                    WorldDominationSettings.DefRoadBlockHeavyWork, WorldDominationSettings.DefRoadBlockHeavyExpeditionStrength,
                    WorldDominationSettings.DefRoadBlockHeavyFlatPenalty, WorldDominationSettings.DefRoadBlockHeavyMaxHealth);
            }

            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_RoadBuilding_SpikeTrapsHeader".Translate(), ref trapsExpanded, SectionHeaderColor))
            {
                s.maxSpikeTrapRange = (int)SettingsUI.LabeledSlider(l, "TSA_WD_RoadBuilding_SpikeTrapRange".Translate(), s.maxSpikeTrapRange, 1f, 30f,
                    "TSA_WD_RoadBuilding_SpikeTrapRangeTip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefMaxSpikeTrapRange);

                DrawSpikeTrapSection(l, "TSA_WD_SpikeTrapSpike".Translate(),
                    ref s.spikeTrapSpikeWork, ref s.spikeTrapSpikeExpeditionStrength, ref s.spikeTrapSpikeDamage, ref s.spikeTrapSpikeMaxHealth,
                    WorldDominationSettings.DefSpikeTrapSpikeWork, WorldDominationSettings.DefSpikeTrapSpikeExpeditionStrength,
                    WorldDominationSettings.DefSpikeTrapSpikeDamage, WorldDominationSettings.DefSpikeTrapSpikeMaxHealth);
                DrawSpikeTrapSection(l, "TSA_WD_SpikeTrapCaltrops".Translate(),
                    ref s.spikeTrapCaltropsWork, ref s.spikeTrapCaltropsExpeditionStrength, ref s.spikeTrapCaltropsDamage, ref s.spikeTrapCaltropsMaxHealth,
                    WorldDominationSettings.DefSpikeTrapCaltropsWork, WorldDominationSettings.DefSpikeTrapCaltropsExpeditionStrength,
                    WorldDominationSettings.DefSpikeTrapCaltropsDamage, WorldDominationSettings.DefSpikeTrapCaltropsMaxHealth);

                s.spikeTrapMaxTriggersPerTraveler = (int)SettingsUI.LabeledSlider(l, "TSA_WD_RoadBuilding_SpikeTrapMaxTriggers".Translate(), s.spikeTrapMaxTriggersPerTraveler, 0f, 10f,
                    "TSA_WD_RoadBuilding_SpikeTrapMaxTriggersTip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefSpikeTrapMaxTriggersPerTraveler);
            }

            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_RoadBuilding_AT_TurretsHeader".Translate(), ref atTurretsExpanded, SectionHeaderColor))
            {
                l.Label("TSA_WD_RoadBuilding_AT_TurretsNote".Translate());
                s.atTurretPlayerGlobalMax = Mathf.RoundToInt(SettingsUI.LabeledSlider(l,
                    "TSA_WD_Experimental_AT_TurretPlayerGlobalMax".Translate(),
                    s.atTurretPlayerGlobalMax, 0f, 200f,
                    "TSA_WD_Experimental_AT_TurretPlayerGlobalMaxTip".Translate(),
                    1f, SliderFormat.Fixed0, WorldDominationSettings.DefAtTurretPlayerGlobalMax));
                s.atTurretPlayerPerSiteMax = Mathf.RoundToInt(SettingsUI.LabeledSlider(l,
                    "TSA_WD_Experimental_AT_TurretPlayerPerSiteMax".Translate(),
                    s.atTurretPlayerPerSiteMax, 0f, 20f,
                    "TSA_WD_Experimental_AT_TurretPlayerPerSiteMaxTip".Translate(),
                    1f, SliderFormat.Fixed0, WorldDominationSettings.DefAtTurretPlayerPerSiteMax));

                DrawAtTurretSection(l, "TSA_WD_AT_TurretTier_Light".Translate(),
                    ref s.atTurretLightWork, ref s.atTurretLightExpeditionStrength, ref s.atTurretLightMinConstruction,
                    ref s.atTurretLightMaxStrength, ref s.atTurretLightDamage, ref s.atTurretLightCooldownDays, ref s.atTurretLightRange,
                    WorldDominationSettings.DefAtTurretLightWork, WorldDominationSettings.DefAtTurretLightExpeditionStrength,
                    WorldDominationSettings.DefAtTurretLightMinConstruction,
                    WorldDominationSettings.DefAtTurretLightMaxStrength, WorldDominationSettings.DefAtTurretLightDamage,
                    WorldDominationSettings.DefAtTurretLightCooldownDays, WorldDominationSettings.DefAtTurretLightRange);
                DrawAtTurretSection(l, "TSA_WD_AT_TurretTier_Medium".Translate(),
                    ref s.atTurretMediumWork, ref s.atTurretMediumExpeditionStrength, ref s.atTurretMediumMinConstruction,
                    ref s.atTurretMediumMaxStrength, ref s.atTurretDamage, ref s.atTurretCooldownDays, ref s.atTurretMediumRange,
                    WorldDominationSettings.DefAtTurretMediumWork, WorldDominationSettings.DefAtTurretMediumExpeditionStrength,
                    WorldDominationSettings.DefAtTurretMediumMinConstruction,
                    WorldDominationSettings.DefAtTurretMediumMaxStrength, WorldDominationSettings.DefAtTurretDamage,
                    WorldDominationSettings.DefAtTurretCooldownDays, WorldDominationSettings.DefAtTurretMediumRange);
                DrawAtTurretSection(l, "TSA_WD_AT_TurretTier_Heavy".Translate(),
                    ref s.atTurretHeavyWork, ref s.atTurretHeavyExpeditionStrength, ref s.atTurretHeavyMinConstruction,
                    ref s.atTurretHeavyMaxStrength, ref s.atTurretHeavyDamage, ref s.atTurretHeavyCooldownDays, ref s.atTurretHeavyRange,
                    WorldDominationSettings.DefAtTurretHeavyWork, WorldDominationSettings.DefAtTurretHeavyExpeditionStrength,
                    WorldDominationSettings.DefAtTurretHeavyMinConstruction,
                    WorldDominationSettings.DefAtTurretHeavyMaxStrength, WorldDominationSettings.DefAtTurretHeavyDamage,
                    WorldDominationSettings.DefAtTurretHeavyCooldownDays, WorldDominationSettings.DefAtTurretHeavyRange);

                s.atTurretHitChance0To50PctRange = SettingsUI.LabeledSlider(l, "TSA_WD_Experimental_AT_TurretHitBand0To50".Translate(), s.atTurretHitChance0To50PctRange, 0f, 1f,
                    "TSA_WD_Experimental_AT_TurretHitBand0To50Tip".Translate(), 0.01f, SliderFormat.PercentDecimal, WorldDominationSettings.DefAtTurretHitChance0To50PctRange);
                s.atTurretHitChance51To75PctRange = SettingsUI.LabeledSlider(l, "TSA_WD_Experimental_AT_TurretHitBand51To75".Translate(), s.atTurretHitChance51To75PctRange, 0f, 1f,
                    "TSA_WD_Experimental_AT_TurretHitBand51To75Tip".Translate(), 0.01f, SliderFormat.PercentDecimal, WorldDominationSettings.DefAtTurretHitChance51To75PctRange);
                s.atTurretHitChance76To100PctRange = SettingsUI.LabeledSlider(l, "TSA_WD_Experimental_AT_TurretHitBand76To100".Translate(), s.atTurretHitChance76To100PctRange, 0f, 1f,
                    "TSA_WD_Experimental_AT_TurretHitBand76To100Tip".Translate(), 0.01f, SliderFormat.PercentDecimal, WorldDominationSettings.DefAtTurretHitChance76To100PctRange);
            }

            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_RoadBuilding_DecontaminationHeader".Translate(), ref decontamExpanded, SectionHeaderColor))
            {
                s.maxDecontaminationRange = (int)SettingsUI.LabeledSlider(l, "TSA_WD_RoadBuilding_DecontaminationRange".Translate(), s.maxDecontaminationRange, 1f, 40f,
                    "TSA_WD_RoadBuilding_DecontaminationRangeTip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefMaxDecontaminationRange);
                s.decontaminationWork = SettingsUI.LabeledSlider(l, "TSA_WD_RoadBuilding_Work".Translate(), s.decontaminationWork, 100f, 3000f,
                    "TSA_WD_RoadBuilding_DecontaminationWorkTip".Translate(), 25f, SliderFormat.Fixed0, WorldDominationSettings.DefDecontaminationWork);
                s.decontaminationExpeditionStrength = SettingsUI.LabeledSlider(l, "TSA_WD_RoadBuilding_DecontaminationStrength".Translate(), s.decontaminationExpeditionStrength, 10f, 200f,
                    "TSA_WD_RoadBuilding_DecontaminationStrengthTip".Translate(), 5f, SliderFormat.Fixed0, WorldDominationSettings.DefDecontaminationExpeditionStrength);
                s.decontaminationPollutionReductionPp = SettingsUI.LabeledSlider(l, "TSA_WD_RoadBuilding_DecontaminationReduction".Translate(), s.decontaminationPollutionReductionPp, 1f, 100f,
                    "TSA_WD_RoadBuilding_DecontaminationReductionTip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefDecontaminationPollutionReductionPp);
            }

            s.ClampRoadBuildingCascades();

            if (!Mathf.Approximately(oldLightPenalty, s.roadBlockLightFlatPenalty)
                || !Mathf.Approximately(oldNormalPenalty, s.roadBlockNormalFlatPenalty)
                || !Mathf.Approximately(oldHeavyPenalty, s.roadBlockHeavyFlatPenalty))
            {
                WD_WorldLayer_MovementDifficultyOverlay.InvalidateAndDirtyIfActive();
            }

            l.End();
            Widgets.EndScrollView();
        }

        private static void DrawRoadSection(
            Listing_Standard l,
            string header,
            ref float movement,
            ref float work,
            ref float strength,
            ref int minConstruction,
            ref float winterReduction,
            float defaultMovement,
            float defaultWork,
            float defaultStrength,
            int defaultMinConstruction,
            float defaultWinterReduction)
        {
            SettingsUI.DrawHeader(l, header);
            movement = SettingsUI.LabeledSlider(l, "TSA_WD_RoadBuilding_Movement".Translate(), movement, 0.05f, 2f,
                "TSA_WD_RoadBuilding_MovementTip".Translate(), 0.05f, SliderFormat.Fixed2, defaultMovement);
            work = SettingsUI.LabeledSlider(l, "TSA_WD_RoadBuilding_Work".Translate(), work, 100f, 3000f,
                "TSA_WD_RoadBuilding_WorkTip".Translate(), 25f, SliderFormat.Fixed0, defaultWork);
            strength = SettingsUI.LabeledSlider(l, "TSA_WD_RoadBuilding_RoadStrength".Translate(), strength, 10f, 200f,
                "TSA_WD_RoadBuilding_RoadStrengthTip".Translate(), 5f, SliderFormat.Fixed0, defaultStrength);
            minConstruction = (int)SettingsUI.LabeledSlider(l, "TSA_WD_RoadBuilding_MinConstruction".Translate(), minConstruction, 0f, 60f,
                "TSA_WD_RoadBuilding_MinConstructionTip".Translate(), 1f, SliderFormat.Fixed0, defaultMinConstruction);
            winterReduction = SettingsUI.LabeledSlider(l, "TSA_WD_RoadBuilding_WinterReduction".Translate(), winterReduction, 0f, 1f,
                "TSA_WD_RoadBuilding_WinterReductionTip".Translate(), 0.05f, SliderFormat.Percent, defaultWinterReduction);
        }

        private static void DrawRoadBlockSection(
            Listing_Standard l,
            string header,
            ref float work,
            ref float strength,
            ref float penalty,
            ref float maxHealth,
            float defaultWork,
            float defaultStrength,
            float defaultPenalty,
            float defaultMaxHealth)
        {
            SettingsUI.DrawHeader(l, header);
            work = SettingsUI.LabeledSlider(l, "TSA_WD_RoadBuilding_Work".Translate(), work, 100f, 3000f,
                "TSA_WD_RoadBuilding_RoadBlockWorkTip".Translate(), 25f, SliderFormat.Fixed0, defaultWork);
            strength = SettingsUI.LabeledSlider(l, "TSA_WD_RoadBuilding_RoadBlockStrength".Translate(), strength, 10f, 200f,
                "TSA_WD_RoadBuilding_RoadBlockStrengthTip".Translate(), 5f, SliderFormat.Fixed0, defaultStrength);
            penalty = SettingsUI.LabeledSlider(l, "TSA_WD_RoadBuilding_RoadBlockPenalty".Translate(), penalty, 0f, 6f,
                "TSA_WD_RoadBuilding_RoadBlockPenaltyTip".Translate(), 0.25f, SliderFormat.Fixed2, defaultPenalty);
            maxHealth = SettingsUI.LabeledSlider(l, "TSA_WD_RoadBuilding_MaxHealth".Translate(), maxHealth, 100f, 5000f,
                "TSA_WD_RoadBuilding_MaxHealthTip".Translate(), 50f, SliderFormat.Fixed0, defaultMaxHealth);
        }

        private static void DrawSpikeTrapSection(
            Listing_Standard l,
            string header,
            ref float work,
            ref float strength,
            ref float damage,
            ref float maxHealth,
            float defaultWork,
            float defaultStrength,
            float defaultDamage,
            float defaultMaxHealth)
        {
            SettingsUI.DrawHeader(l, header);
            work = SettingsUI.LabeledSlider(l, "TSA_WD_RoadBuilding_Work".Translate(), work, 100f, 3000f,
                "TSA_WD_RoadBuilding_SpikeTrapWorkTip".Translate(), 25f, SliderFormat.Fixed0, defaultWork);
            strength = SettingsUI.LabeledSlider(l, "TSA_WD_RoadBuilding_SpikeTrapStrength".Translate(), strength, 10f, 200f,
                "TSA_WD_RoadBuilding_SpikeTrapStrengthTip".Translate(), 5f, SliderFormat.Fixed0, defaultStrength);
            damage = SettingsUI.LabeledSlider(l, "TSA_WD_RoadBuilding_SpikeTrapDamage".Translate(), damage, 10f, 500f,
                "TSA_WD_RoadBuilding_SpikeTrapDamageTip".Translate(), 5f, SliderFormat.Fixed0, defaultDamage);
            maxHealth = SettingsUI.LabeledSlider(l, "TSA_WD_RoadBuilding_MaxHealth".Translate(), maxHealth, 100f, 5000f,
                "TSA_WD_RoadBuilding_MaxHealthTip".Translate(), 50f, SliderFormat.Fixed0, defaultMaxHealth);
        }

        private static void DrawAtTurretSection(
            Listing_Standard l,
            string header,
            ref float work,
            ref float strength,
            ref int minConstruction,
            ref float maxStrength,
            ref float damage,
            ref float cooldownDays,
            ref float range,
            float defaultWork,
            float defaultStrength,
            int defaultMinConstruction,
            float defaultMaxStrength,
            float defaultDamage,
            float defaultCooldownDays,
            float defaultRange)
        {
            SettingsUI.DrawHeader(l, header);
            work = SettingsUI.LabeledSlider(l, "TSA_WD_RoadBuilding_Work".Translate(), work, 100f, 3000f,
                "TSA_WD_RoadBuilding_AT_TurretWorkTip".Translate(), 25f, SliderFormat.Fixed0, defaultWork);
            strength = SettingsUI.LabeledSlider(l, "TSA_WD_RoadBuilding_AT_TurretStrength".Translate(), strength, 10f, 300f,
                "TSA_WD_RoadBuilding_AT_TurretStrengthTip".Translate(), 5f, SliderFormat.Fixed0, defaultStrength);
            minConstruction = (int)SettingsUI.LabeledSlider(l, "TSA_WD_RoadBuilding_MinConstruction".Translate(), minConstruction, 0f, 60f,
                "TSA_WD_RoadBuilding_MinConstructionTip".Translate(), 1f, SliderFormat.Fixed0, defaultMinConstruction);
            maxStrength = SettingsUI.LabeledSlider(l, "TSA_WD_Experimental_AT_TurretMaxStrength".Translate(), maxStrength, 10f, 500f,
                "TSA_WD_Experimental_AT_TurretMaxStrengthTip".Translate(), 5f, SliderFormat.Fixed0, defaultMaxStrength);
            damage = SettingsUI.LabeledSlider(l, "TSA_WD_Experimental_AT_TurretDamage".Translate(), damage, 10f, 1000f,
                "TSA_WD_Experimental_AT_TurretDamageTip".Translate(), 10f, SliderFormat.Fixed0, defaultDamage);
            cooldownDays = SettingsUI.LabeledSlider(l, "TSA_WD_Experimental_AT_TurretCooldown".Translate(), cooldownDays, 0.05f, 10f,
                "TSA_WD_Experimental_AT_TurretCooldownTip".Translate(), 0.05f, SliderFormat.Fixed2, defaultCooldownDays);
            range = SettingsUI.LabeledSlider(l, "TSA_WD_Experimental_AT_TurretRange".Translate(), range, 1f, 20f,
                "TSA_WD_Experimental_AT_TurretRangeTip".Translate(), 1f, SliderFormat.Fixed0, defaultRange);
        }
    }
}
