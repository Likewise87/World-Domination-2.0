using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    public enum BattleMarginTier : byte
    {
        Close = 0,
        Normal = 1,
        Decisive = 2
    }

    public class RaidMarginShares : IExposable
    {
        public float close = 0.33f;
        public float normal = 0.34f;
        public float decisive = 0.33f;

        public void ExposeData()
        {
            Scribe_Values.Look(ref close, "close", 0.33f);
            Scribe_Values.Look(ref normal, "normal", 0.34f);
            Scribe_Values.Look(ref decisive, "decisive", 0.33f);
        }

        public void Normalize()
        {
            float sum = close + normal + decisive;
            if (sum <= 0.0001f)
            {
                close = normal = decisive = 1f / 3f;
                return;
            }
            close /= sum;
            normal /= sum;
            decisive /= sum;
        }

        public RaidMarginShares Copy() => new RaidMarginShares { close = close, normal = normal, decisive = decisive };
    }

    public class RaidCasualtyEntry : IExposable
    {
        public float attLossPct = 0.3f;
        public float defLossPct = 0.3f;

        public void ExposeData()
        {
            Scribe_Values.Look(ref attLossPct, "attLossPct", 0.3f);
            Scribe_Values.Look(ref defLossPct, "defLossPct", 0.3f);
        }

        public RaidCasualtyEntry Copy() => new RaidCasualtyEntry { attLossPct = attLossPct, defLossPct = defLossPct };
    }

    public class RaidSideLossEntry : IExposable
    {
        public float lossPct = 0.3f;

        public void ExposeData()
        {
            Scribe_Values.Look(ref lossPct, "lossPct", 0.3f);
        }

        public RaidSideLossEntry Copy() => new RaidSideLossEntry { lossPct = lossPct };
    }

    public class RaidOutcome : IExposable
    {
        public float threshold = 1.0f;
        public float winChance = 0.5f;
        public float attLossWin = 0.1f;
        public float defLossLoss = 0.2f;
        public float defLossWin = 0.05f;
        public RaidMarginShares attSeverityOnAttWin = new RaidMarginShares();
        public RaidMarginShares attSeverityOnAttLoss = new RaidMarginShares();
        public RaidMarginShares defCoalitionOnAttWin = new RaidMarginShares();
        public RaidMarginShares defCoalitionOnAttLoss = new RaidMarginShares();

        public void ExposeData()
        {
            Scribe_Values.Look(ref threshold, "threshold", 1.0f);
            Scribe_Values.Look(ref winChance, "winChance", 0.5f);
            Scribe_Values.Look(ref attLossWin, "attLossWin", 0.1f);
            Scribe_Values.Look(ref defLossLoss, "defLossLoss", 0.2f);
            Scribe_Values.Look(ref defLossWin, "defLossWin", 0.05f);
            Scribe_Deep.Look(ref attSeverityOnAttWin, "attSeverityOnAttWin");
            Scribe_Deep.Look(ref attSeverityOnAttLoss, "attSeverityOnAttLoss");
            Scribe_Deep.Look(ref defCoalitionOnAttWin, "defCoalitionOnAttWin");
            Scribe_Deep.Look(ref defCoalitionOnAttLoss, "defCoalitionOnAttLoss");
            RaidMarginShares legacyAttWinMargins = null;
            RaidMarginShares legacyDefWinMargins = null;
            Scribe_Deep.Look(ref legacyAttWinMargins, "attWinMargins");
            Scribe_Deep.Look(ref legacyDefWinMargins, "defWinMargins");
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                if (attSeverityOnAttWin == null)
                    attSeverityOnAttWin = legacyAttWinMargins?.Copy() ?? RaidSeverityDefaults.AttSeverityOnAttWinAt(threshold);
                if (attSeverityOnAttLoss == null)
                    attSeverityOnAttLoss = RaidSeverityDefaults.AttSeverityOnAttLossAt(threshold);
                if (defCoalitionOnAttWin == null)
                    defCoalitionOnAttWin = RaidSeverityDefaults.DefCoalitionOnAttWinAt(threshold);
                if (defCoalitionOnAttLoss == null)
                    defCoalitionOnAttLoss = legacyDefWinMargins?.Copy() ?? RaidSeverityDefaults.DefCoalitionOnAttLossAt(threshold);
            }
        }
    }

    public static class RaidSeverityDefaults
    {
        private static float RatioT(float ratio, float min, float max)
        {
            if (ratio <= min) return 0f;
            if (ratio >= max) return 1f;
            return (ratio - min) / (max - min);
        }

        private static RaidMarginShares LerpShares(RaidMarginShares a, RaidMarginShares b, float t)
        {
            return new RaidMarginShares
            {
                close = Mathf.Lerp(a.close, b.close, t),
                normal = Mathf.Lerp(a.normal, b.normal, t),
                decisive = Mathf.Lerp(a.decisive, b.decisive, t)
            };
        }

        private static RaidMarginShares Shares(float heavy, float moderate, float light)
        {
            return new RaidMarginShares { close = heavy, normal = moderate, decisive = light };
        }

        // Battle-margin decisiveness for the attacker-won branch. Tier meaning is consistent:
        // Close = narrow / hard-fought, Decisive = one-sided / crushing.
        // Anchored at the true endpoints of the strength table (0.10x .. 6x) so the full spectrum is used:
        // a 0.10x attacker that somehow wins can only win razor-thin; a 6x attacker curbstomps.
        public static RaidMarginShares DecisivenessAttackerWon(float ratio)
        {
            var upset = Shares(0.98f, 0.02f, 0.00f);    // <= 0.10x: weak attacker somehow wins -> razor thin
            var even = Shares(0.45f, 0.40f, 0.15f);     // 1x: coin flip -> hard-fought
            var expected = Shares(0.00f, 0.05f, 0.95f); // >= 6x: overwhelming -> total curbstomp
            if (ratio <= 0.10f) return upset.Copy();
            if (ratio >= 6f) return expected.Copy();
            if (ratio <= 1f)
            {
                var s = LerpShares(upset, even, RatioT(ratio, 0.10f, 1f));
                s.Normalize();
                return s;
            }
            var result = LerpShares(even, expected, RatioT(ratio, 1f, 6f));
            result.Normalize();
            return result;
        }

        // Battle-margin decisiveness for the attacker-lost branch.
        // A 0.10x attacker that loses is routed; a 6x attacker that (rarely) loses can only lose closely.
        public static RaidMarginShares DecisivenessAttackerLost(float ratio)
        {
            var expected = Shares(0.00f, 0.05f, 0.95f); // <= 0.10x: weak attacker totally routed
            var even = Shares(0.45f, 0.40f, 0.15f);     // 1x: coin flip -> hard-fought
            var upset = Shares(0.98f, 0.02f, 0.00f);    // >= 6x: strong attacker barely loses -> narrow only
            if (ratio <= 0.10f) return expected.Copy();
            if (ratio >= 6f) return upset.Copy();
            if (ratio <= 1f)
            {
                var s = LerpShares(expected, even, RatioT(ratio, 0.10f, 1f));
                s.Normalize();
                return s;
            }
            var result = LerpShares(even, upset, RatioT(ratio, 1f, 6f));
            result.Normalize();
            return result;
        }

        // Defender's margin when the defender LOSES (attacker won). Same shape as the attacker-won curve at
        // the low end (a 0.10x attacker can only win closely, so the defender only loses closely), but with a
        // deliberate bias at the high end: even when completely overpowered, defenders keep a solid chance of a
        // merely-close loss so an overwhelming raid doesn't cripple the whole region in a single blow.
        public static RaidMarginShares DefenderDecisivenessWhenLosing(float ratio)
        {
            var upset = Shares(0.98f, 0.02f, 0.00f);        // <= 0.10x attacker wins -> defender loses razor-thin
            var even = Shares(0.45f, 0.40f, 0.15f);         // 1x -> hard-fought
            var overpowered = Shares(0.35f, 0.15f, 0.50f);  // >= 6x -> mostly crushed, but a solid 35% close (region survives)
            if (ratio <= 0.10f) return upset.Copy();
            if (ratio >= 6f) return overpowered.Copy();
            if (ratio <= 1f)
            {
                var s = LerpShares(upset, even, RatioT(ratio, 0.10f, 1f));
                s.Normalize();
                return s;
            }
            var result = LerpShares(even, overpowered, RatioT(ratio, 1f, 6f));
            result.Normalize();
            return result;
        }

        // Both margin rolls in a branch are independent. The attacker uses the pure decisiveness curves; the
        // defender-when-losing curve carries the anti-crippling bias described above.
        public static RaidMarginShares AttSeverityOnAttWinAt(float ratio) => DecisivenessAttackerWon(ratio);
        public static RaidMarginShares DefCoalitionOnAttWinAt(float ratio) => DefenderDecisivenessWhenLosing(ratio);
        public static RaidMarginShares AttSeverityOnAttLossAt(float ratio) => DecisivenessAttackerLost(ratio);
        public static RaidMarginShares DefCoalitionOnAttLossAt(float ratio) => DecisivenessAttackerLost(ratio);

        // Loss tables are keyed by (side, won?, tier). Tier meaning is consistent everywhere:
        // Close = narrow / hard-fought battle, Decisive = one-sided / crushing battle.
        // Winner: fewer losses the more decisive. Loser: more losses the more decisive (a rout).
        public static RaidSideLossEntry[] DefaultAttWinLoss()
        {
            return new RaidSideLossEntry[]
            {
                new RaidSideLossEntry { lossPct = 0.60f }, // Close: hard-fought win
                new RaidSideLossEntry { lossPct = 0.35f }, // Normal
                new RaidSideLossEntry { lossPct = 0.15f }, // Decisive: curbstomp
            };
        }

        public static RaidSideLossEntry[] DefaultAttLossLoss()
        {
            return new RaidSideLossEntry[]
            {
                new RaidSideLossEntry { lossPct = 0.30f }, // Close: narrow loss / tactical retreat
                new RaidSideLossEntry { lossPct = 0.60f }, // Normal
                new RaidSideLossEntry { lossPct = 0.80f }, // Decisive: routed
            };
        }

        public static RaidSideLossEntry[] DefaultDefWinLoss()
        {
            return new RaidSideLossEntry[]
            {
                new RaidSideLossEntry { lossPct = 0.35f }, // Close: bloody hold
                new RaidSideLossEntry { lossPct = 0.20f }, // Normal
                new RaidSideLossEntry { lossPct = 0.10f }, // Decisive: crushing repulse
            };
        }

        public static RaidSideLossEntry[] DefaultDefLossLoss()
        {
            // Soft on supporting allies: primary is already wiped on attacker win; allies lose only of committed.
            return new RaidSideLossEntry[]
            {
                new RaidSideLossEntry { lossPct = 0.15f }, // Close: light scratch
                new RaidSideLossEntry { lossPct = 0.28f }, // Normal: noticeable but survivable
                new RaidSideLossEntry { lossPct = 0.45f }, // Decisive: hurts, not a near-wipe of the detachment
            };
        }

        public static int TierToIndex(BattleMarginTier tier) => (int)tier;
    }

    public enum WDSettingsPerformancePreset
    {
        Low = 0,
        Medium = 1,
        High = 2
    }

    public enum WDSettingsDifficultyPreset
    {
        Easy = 0,
        Medium = 1,
        Hard = 2
    }

    public class WorldDominationSettings : ModSettings
    {
        // ==========================================
        // SOURCE OF TRUTH: DEFAULT VALUES
        // ==========================================

        public bool initialAllegianceLockDone = false;

        public const WDSettingsPerformancePreset DefPerformancePreset = WDSettingsPerformancePreset.Medium;
        public const WDSettingsDifficultyPreset DefDifficultyPreset = WDSettingsDifficultyPreset.Medium;

        // --- MAIN PAGE ---
        public const float DefTier1Share = 0.20f;
        public const float DefTier2Share = 0.28f;
        public const float DefTier3Share = 0.38f;
        public const float DefTier4Share = 0.60f;

        // SURGICAL: Tiered Action Cap Defaults
        public const int DefCapT1 = 1;
        public const int DefCapT2 = 1;
        public const int DefCapT3 = 2;
        public const int DefCapT4 = 2;

        public const float DefWeightGrow = 240f;
        public const float DefWeightRaid = 200f;
        public const float DefWeightMinorIncident = 80f;
        public const float DefWeightMajorIncident = 16f;
        public const float DefWeightBuildRoad = 48f;
        public const float DefWeightTrader = 48f;
        public const float DefWeightFortify = 64f;
        /// <summary>Settings UI only: whether Develop is folded into the % shown next to usual-action sliders.</summary>
        public const bool DefIncludeDevelopWeightInPercentDisplay = false;

        /// <summary>Default road-building actor cooldown days (settings field still named cooldownGrowDays for save compat).</summary>
        public const float DefCdGrowDays = 0.1f;
        public const float DefCdExpandDays = 14.0f;
        public const float DefCdRaidDays = 0.2f;
        public const float DefCdBeingRaidedDays = 1.0f;
        public const float DefCdIncidentDays = 2.0f;
        public const float DefCdTraderDays = 1.0f;
        public const float DefCdFortifyDays = 4.0f;

        // --- NPC FORTIFY (World Actions) ---
        public const int DefFortifyMinTilesFromSelf = 2;
        public const int DefFortifyMinTilesFromOtherSettlement = 2;
        public const int DefFortifyMaxTilesFromSelf = 8;
        public const int DefFortifyMaxTravelTiles = 30;
        public const int DefFortifyTerritoryLinkMaxTiles = 35;
        public const float DefFortifyFrontierEps = 0.5f;
        public const float DefFortifyTravelerStrength = 50f;
        public const bool DefFortifyClearOnBuilderLoss = false;
        public const bool DefEnableFortifyBlacklist = true;
        public const bool DefFortifyBlacklistApplyToNeutral = true;
        /// <summary>Relative weight: place a road block when Fortify runs (renormalized among valid options).</summary>
        public const float DefFortifyChanceRoadBlock = 0.60f;
        /// <summary>Relative weight: place a spike trap when Fortify runs (roads only).</summary>
        public const float DefFortifyChanceTrap = 0.30f;
        /// <summary>Relative weight: place an AT Turret when Fortify runs (off-road, prefers behind road blocks).</summary>
        public const float DefFortifyChanceTurret = 0.10f;
        /// <summary>T1: chance to launch 2 fortify caravans (else 1). Road block / trap only.</summary>
        public const float DefFortifyMultiT1ChanceOf2 = 0.25f;
        /// <summary>T2: chance to launch 2 fortify caravans (else 1). Road block / trap only.</summary>
        public const float DefFortifyMultiT2ChanceOf2 = 0.50f;
        /// <summary>T3: chance to launch 2 fortify caravans (else 1). Default always 2. Road block / trap only.</summary>
        public const float DefFortifyMultiT3ChanceOf2 = 1.00f;
        /// <summary>T4: chance to launch 3 fortify caravans (else 2). Road block / trap only.</summary>
        public const float DefFortifyMultiT4ChanceOf3 = 0.30f;
        public const int DefAtTurretMaxT1 = 1;
        public const int DefAtTurretMaxT2 = 2;
        public const int DefAtTurretMaxT3 = 3;
        public const int DefAtTurretMaxT4 = 4;
        /// <summary>Player faction total AT Turret cap (all colonies/outposts combined).</summary>
        public const int DefAtTurretPlayerGlobalMax = 50;
        /// <summary>Max AT Turrets attributed to one player colony or WD outpost.</summary>
        public const int DefAtTurretPlayerPerSiteMax = 4;
        /// <summary>When true, new WD outposts start with Auto-Add Arrivals gizmo on.</summary>
        public const bool DefAutoAddPawnsOnArrivalDefault = true;

        // --- GROWTH & EXPANSION PAGE ---
        public const int DefMaxSettlements = 400;
        public const float DefPassiveGrowthT1 = 50f;
        public const float DefPassiveGrowthT2 = 80f;
        public const float DefPassiveGrowthT3 = 110f;
        public const float DefPassiveGrowthT4 = 140f;
        /// <summary>Legacy single growth default; maps to T1 passive for old saves / references.</summary>
        public const float DefBaseGrowth = DefPassiveGrowthT1;
        public const float DefGrowthScaling = 0f;
        public const int DefExpandMinRad = 5;
        public const int DefExpandMaxRad = 12;
        public const int DefMaxRoadRange = 16;
        /// <summary>NPC faction road-building max tile range (<see cref="WorldActions_Roads"/>).</summary>
        public const int DefMaxRoadRangeNpc = 25;
        public const int DefMaxRoadBlockRange = 10;
        // Road blocks — per tier (Light / Normal / Heavy). Legacy single-value defaults map to Normal.
        public const float DefRoadBlockLightFlatPenalty = 1.5f;
        public const float DefRoadBlockNormalFlatPenalty = 2.5f;
        public const float DefRoadBlockHeavyFlatPenalty = 4f;
        public const float DefRoadBlockLightExpeditionStrength = 50f;
        public const float DefRoadBlockNormalExpeditionStrength = 80f;
        public const float DefRoadBlockHeavyExpeditionStrength = 125f;
        public const float DefRoadBlockLightWork = 250f;
        public const float DefRoadBlockNormalWork = 375f;
        public const float DefRoadBlockHeavyWork = 500f;
        public const float DefRoadBlockLightMaxHealth = 1000f;
        public const float DefRoadBlockNormalMaxHealth = 1500f;
        public const float DefRoadBlockHeavyMaxHealth = 2500f;
        /// <summary>Legacy alias for Normal max HP (older call sites / saves).</summary>
        public const float DefRoadBlockMaxHealth = DefRoadBlockNormalMaxHealth;
        public const int DefMaxSpikeTrapRange = 10;
        public const float DefSpikeTrapSpikeWork = 250f;
        public const float DefSpikeTrapCaltropsWork = 375f;
        public const float DefSpikeTrapSpikeExpeditionStrength = 50f;
        public const float DefSpikeTrapCaltropsExpeditionStrength = 80f;
        public const float DefSpikeTrapSpikeDamage = 100f;
        public const float DefSpikeTrapCaltropsDamage = 200f;
        public const float DefSpikeTrapSpikeMaxHealth = 500f;
        public const float DefSpikeTrapCaltropsMaxHealth = 1000f;
        public const int DefSpikeTrapMaxTriggersPerTraveler = 3;
        public const int DefMaxDecontaminationRange = 20;
        public const float DefDecontaminationWork = 350f;
        public const float DefDecontaminationExpeditionStrength = 20f;
        public const float DefDecontaminationPollutionReductionPp = 40f;
        public const float DefFallbackDirtRoadMovement = 0.7f;
        public const float DefFallbackStoneRoadMovement = 0.5f;
        public const float DefFallbackAsphaltRoadMovement = 0.3f;
        // Work defaults 250/375/500 preserve old 1:1.5:2 duration ratios when Work drives ticks.
        public const float DefFallbackDirtRoadWork = 250f;
        public const float DefFallbackStoneRoadWork = 375f;
        public const float DefFallbackAsphaltRoadWork = 500f;
        public const float DefFallbackDirtRoadExpeditionStrength = 50f;
        public const float DefFallbackStoneRoadExpeditionStrength = 80f;
        public const float DefFallbackAsphaltRoadExpeditionStrength = 125f;
        public const int DefFallbackDirtRoadMinConstruction = 5;
        public const int DefFallbackStoneRoadMinConstruction = 15;
        public const int DefFallbackAsphaltRoadMinConstruction = 25;
        public const float DefFallbackDirtRoadWinterReduction = 0.15f;
        public const float DefFallbackStoneRoadWinterReduction = 0.30f;
        public const float DefFallbackAsphaltRoadWinterReduction = 0.50f;
        public const float DefMinorIncSev = 150f;
        public const float DefMajorIncSev = 450f;
        public const int DefLocalMaxT1 = 5;
        public const int DefLocalMaxT2 = 4;
        public const int DefLocalMaxT3 = 3;
        public const int DefLocalMaxT4 = 1;
        public const int DefSameTierNeighborsToUpgradeT1 = 1;
        public const int DefSameTierNeighborsToUpgradeT2 = 1;
        public const int DefSameTierNeighborsToUpgradeT3 = 2;
        public const float DefExpansionSuccessChance = 0.40f;
        public const float DefTier1BaseDefensiveStrength = 100f;
        public const float DefTier2BaseDefensiveStrength = 200f;
        public const float DefTier3BaseDefensiveStrength = 350f;
        public const float DefTier4BaseDefensiveStrength = 500f;
        public const float DefPlayerOutpostBaseDefensiveStrength = 100f;
        /// <summary>Days a player WD outpost cannot be targeted again after a raid is launched or after a failed defense (separate from NPC settlement Defense Shield).</summary>
        public const float DefCooldownPlayerOutpostRaidDays = 5.0f;

        // --- DIPLOMACY PAGE ---
        public const float DefRevoltChance = 0.02f;
        public const float DefDiplomacyChangeChance = 0.03f;
        public const bool DefEnableLeaderHandicap = true;
        public const bool DefEnableUnderdogBuff = true;
        public const bool DefEnableAntiLeaderCoalition = true;
        public const bool DefEnableRandomDiplomacy = true;
        public const bool DefEnableStrongFactionWar = true;
        public const float DefStrongFactionWarChance = 0.10f;
        public const float DefStrongFactionWarTopPct = 0.30f;
        public const bool DefStrongFactionWarRequireMidOrLate = false;
        public const bool DefEnableExpansionistZeal = true;

        public const float DefDurLeaderHandicapDays = 10f;
        public const float DefCdLeaderHandicapDays = 15f;
        public const float DefDurUnderdogBuffDays = 10f;
        public const float DefCdUnderdogBuffDays = 15f;
        public const float DefDurExpansionistZealDays = 10f;
        public const float DefCdExpansionistZealDays = 15f;
        public const float DefDurAntiLeaderCoalitionDays = 15f;
        public const float DefCdAntiLeaderCoalitionDays = 20f;
        public const float DefZealTriggerChance = 0.20f;
        public const float DefLeaderHandicapTriggerChance = 0.35f;
        public const float DefUnderdogBuffTriggerChance = 0.25f;
        public const float DefAntiLeaderCoalitionTriggerChance = 0.25f;
        public const float DefZealRaidRangeMult = 1.5f;
        public const float DefZealAttritionMult = 0.5f;
        public const float DefUnderdogActionShareMult = 2f;
        public const float DefUnderdogIncidentWeightMult = 0.5f;
        public const float DefUnderdogIncidentSeverityMult = 0.5f;
        public const float DefUnderdogGrowthGainMult = 2f;
        public const float DefLeaderIncidentWeightMult = 2f;
        public const float DefLeaderIncidentSeverityMult = 2f;
        public const float DefAlliedRaidOrderMinWinChance = 0.50f;
        public const int DefAlliedRaidClaimCostT1 = 15;
        public const int DefAlliedRaidClaimCostT2 = 25;
        public const int DefAlliedRaidClaimCostT3 = 35;
        public const int DefAlliedRaidClaimCostT4 = 45;

        // --- BUY SETTLEMENT ---
        public const bool DefEnableSettlementBuy = true;
        public const float DefSettlementBuyAskT1 = 5000f;
        public const float DefSettlementBuyAskT2 = 12000f;
        public const float DefSettlementBuyAskT3 = 20000f;
        public const float DefSettlementBuyAskT4 = 30000f;
        public const float DefSettlementBuySilverPerGoodwill = 200f;
        public const float DefSettlementBuyMaxGoodwillShare = 1f;
        public const bool DefNotifySettlementBuyStarted = true;
        public const bool DefNotifySettlementBuyCompleted = true;
        public const bool DefNotifySettlementBuyAborted = true;
        public const bool DefEnableDiplomacyNegotiate = true;
        public const float DefNegotiateAskMinSilver = 8000f;
        public const float DefNegotiateAskMaxSilver = 40000f;
        public const bool DefNotifyDiplomacyNegotiateStarted = false;
        public const bool DefNotifyDiplomacyNegotiateCompleted = true;
        public const bool DefNotifyDiplomacyNegotiateAborted = true;

        // --- FACTION BRIBE ---
        public const bool DefEnableFactionBribe = true;
        public const float DefBribeSettlementSilverPerStrength = 2.0f;
        public const float DefBribeCaravanSilverPerStrengthEarly = 1.5f;
        public const float DefBribeCaravanSilverPerStrengthMid = 2.0f;
        public const float DefBribeCaravanSilverPerStrengthLate = 2.5f;
        public const int DefBribeCeasefireDaysShort = 10;
        public const int DefBribeCeasefireDaysMedium = 20;
        public const int DefBribeCeasefireDaysLong = 30;
        public const float DefBribeCeasefireDiscountMedium = 0.10f;
        public const float DefBribeCeasefireDiscountLong = 0.20f;
        public const float DefBribeRaidAskFloorFraction = 0.5f;
        public const float DefBribeInvestmentFraction = 0.5f;
        public const int DefBribeCaravanInvestmentRadiusTiles = 50;
        public const float DefBribeGoodwillDivisor = 400f;
        public const bool DefNotifyBribeSettlementCompleted = true;
        public const bool DefNotifyBribeSettlementAborted = true;
        public const bool DefNotifyBribeRaidCompleted = true;
        public const bool DefNotifyBribeRaidAborted = true;
        public const bool DefNotifyBribeLostInTransit = true;
        public const bool DefNotifyBribeCeasefireExpired = true;

        public const int DefAlliedRaidAwardCostT1 = 30;
        public const int DefAlliedRaidAwardCostT2 = 50;
        public const int DefAlliedRaidAwardCostT3 = 70;
        public const int DefAlliedRaidAwardCostT4 = 90;
        public const int DefOrderedRoadBaseCostT1 = 5;
        public const int DefOrderedRoadBaseCostT2 = 8;
        public const int DefOrderedRoadBaseCostT3 = 12;
        public const int DefOrderedRoadBaseCostT4 = 15;
        public const float DefOrderedRoadPerSegmentRateT1 = 0.4f;
        public const float DefOrderedRoadPerSegmentRateT2 = 0.7f;
        public const float DefOrderedRoadPerSegmentRateT3 = 1.0f;
        public const int DefOrderedTraderGoodwillCost = 10;
        public const int DefConquestAllyGiftGoodwillT1 = 15;
        public const int DefConquestAllyGiftGoodwillT2 = 28;
        public const int DefConquestAllyGiftGoodwillT3 = 45;
        public const int DefConquestAllyGiftGoodwillT4 = 70;
        /// <summary>Extra WD offensive strength granted to a non-player settlement for each 100 market value sent as a vanilla launch-pod gift.</summary>
        public const float DefLaunchPodGiftStrengthPer100MarketValue = 20f;

        public const bool DefEnableFactionSettlementInvestment = true;
        public const float DefFactionInvestmentStrengthPer100Silver = 20f;
        public const int DefFactionInvestmentRadiusTiles = 50;
        public const float DefFactionInvestmentUpgradeT1ToT2Silver = 1500f;
        public const float DefFactionInvestmentUpgradeT2ToT3Silver = 4000f;
        public const float DefFactionInvestmentUpgradeT3ToT4Silver = 9000f;
        public const float DefFactionInvestmentUpgradeSuccessChance = 0.50f;
        public const float DefFactionInvestmentNotifyMinSilver = 200f;
        public const int DefMaxGoodwill = 200;

        public HashSet<string> lockedAllegiancePairs = new HashSet<string>();

        // --- LEGACY INFLUENCE RADIUS (unused gameplay; keep fields + Scribe for ModConfig) + notification radius ---
        /// <summary>Notification radius (tiles) for Nearby world event letters. Live again; UI 1–500.</summary>
        public const float DefNotificationRadiusTiles = 15f;
        /// <summary>Legacy ModConfig only. Influence Radius cache/APIs removed; field unused.</summary>
        public const float DefInfluenceStartTiles = 5f;
        /// <summary>Legacy ModConfig only. Influence Radius cache/APIs removed; field unused.</summary>
        public const float DefInfluenceWealthPer10k = 2f;
        /// <summary>Legacy ModConfig only. Influence Radius cache/APIs removed; field unused.</summary>
        public const float DefInfluencePerDay = 0.05f;
        /// <summary>Legacy ModConfig only. Influence Radius cache/APIs removed; field unused.</summary>
        public const float DefInfluencePer10kOutpostDefense = 2f;

        // --- LATE-GAME DIFFICULTY SCALING ---
        public const bool DefEnableLateGameScaling = true;
        // Mid-game escalation (earlier, softer). Late supersedes when both thresholds are met.
        public const float DefMidGameShareThreshold = 0.15f;
        public const float DefMidGameOutpostStrengthThreshold = 6000f;
        public const float DefMidGameRaidBiasPct = 0.25f;
        public const float DefMidGameGrowthMult = 1.5f;
        /// <summary>Mid-game: additive attack-range bonus vs early baselines (0.50 = +50%).</summary>
        public const float DefMidGameAttackRangeBonusPct = 0.50f;
        /// <summary>When true and Mid stage is active, ally pull radius uses <see cref="DefMidGameAllyRadiusBonusPct"/>.</summary>
        public const bool DefEnableMidGameAllyRadiusScaling = true;
        /// <summary>Mid-game: additive ally pull radius bonus vs base (0.40 = +40%).</summary>
        public const float DefMidGameAllyRadiusBonusPct = 0.40f;
        public const int DefMidGameExpandTowardPlayerMaxTiles = 4;
        public const float DefMidGameGarrisonBoostPct = 0.15f;
        public const bool DefEnableMidGameT4SettlementMortar = false;
        public const bool DefEnableMidGameT4SettlementAntiAir = false;
        public const bool DefEnableMidGameOutpostIncidents = true;
        public const float DefMidGameOutpostIncidentSeverity = 100f;
        public const float DefMidGameOutpostIncidentDailyChance = 0.0375f;
        public const bool DefEnableGoodwillDrain = true;
        public const int DefGoodwillDrainIntervalDays = 10;
        public const int DefMidGameGoodwillDrainAmount = 4;
        public const int DefLateGameGoodwillDrainAmount = 10;
        public const bool DefEnableOutpostIncidents = true;
        public const float DefOutpostIncidentSeverity = 200f;
        public const float DefOutpostIncidentDailyChance = 0.075f;
        public const bool DefNotifyOutpostIncident = true;
        public const float DefCoalitionRaidPriorityBias = 0.75f;
        /// <summary>Experimental: player colony can Build roads / road blocks / spike traps on the world map.</summary>
        public const bool DefExperimentalColonyWorldBuild = true;
        public const bool DefEnableFirstOutpostQuest = true;
        public const bool DefEnableCommonEnemySettlementQuest = true;
        public const bool DefEnableColonyRoadLinkQuest = true;
        public const bool DefEnableWorldDominationVictoryQuest = true;
        public const bool DefEnableAtTurretTargetPlayerTravelers = true;
        public const bool DefEnableAtTurretTargetPlayerCaravans = true;
        public const bool DefEnableOutpostUpkeep = false;
        public const bool DefGiveFoodOnPrisonerRecruitTransfer = true;
        public const bool DefGiveFoodOnAllPlayerPawnsTransfer = true;
        public const bool DefShowOutpostRequirementsPreviewInWdMenu = false;
        public const int DefUpkeepSilverPerOccupant = 30;
        public const int DefUpkeepIntervalDays = 15;
        /// <summary>Modifier activates when player global strength share (outpost strength / world strength) reaches this fraction (OR with the outpost-strength threshold).</summary>
        public const float DefLateGameShareThreshold = 0.25f;
        // Absolute outpost strength OR-gate for Late (was 8000 before Mid/Late split).
        /// <summary>Modifier activates when total player outpost strength reaches this value (OR with the global-share threshold).</summary>
        public const float DefLateGameOutpostStrengthThreshold = 10000f;
        /// <summary>Raid bias: player-owned targets are weighted (1 + this) more likely within a distance band when Mid/Late is active and the attacker can reach a player target.</summary>
        public const float DefLateGameRaidBiasPct = 0.50f;
        /// <summary>Flat growth multiplier for hostile settlements while Mid or Late is active.</summary>
        public const float DefLateGameGrowthMult = 2.0f;
        /// <summary>Late-game: additive attack-range bonus vs early baselines (1.00 = +100%). Replaces Mid when Late is active.</summary>
        public const float DefLateGameAttackRangeBonusPct = 1.00f;
        /// <summary>When true and Late stage is active, ally pull radius uses <see cref="DefLateGameAllyRadiusBonusPct"/>.</summary>
        public const bool DefEnableLateGameAllyRadiusScaling = true;
        /// <summary>Late-game: additive ally pull radius bonus vs base (1.00 = +100%). Replaces Mid when Late is active.</summary>
        public const float DefLateGameAllyRadiusBonusPct = 1.00f;
        /// <summary>Max tiles a biased expansion hop may travel from its parent settlement while the modifier is active.</summary>
        public const int DefLateGameExpandTowardPlayerMaxTiles = 8;
        /// <summary>While late-game difficulty is active, settlement garrisons are multiplied by (1 + this). Stacks multiplicatively with tier garrison sliders.</summary>
        public const float DefLateGameGarrisonBoostPct = 0.30f;
        /// <summary>When true, NPC tier-4 settlement mortars MAY target the player (your WD travelers + outposts), but only while the late-game modifier is active. The master fire-at-all toggle is <see cref="DefEnableNpcT4Mortar"/>.</summary>
        public const bool DefEnableT4SettlementMortar = true;
        public const float DefCaravanRaidMinStorytellerFrac = 0.75f;
        public const float DefCaravanRaidMaxStorytellerFrac = 2.25f;
        /// <summary>When true, storyteller raid point floor/ceiling follow Early/Mid/Late pairs from escalation stage (if Mid/Late Game is enabled).</summary>
        public const bool DefScaleRaidClampWithEscalation = true;
        public const float DefEarlyRaidClampMinStorytellerFrac = 0.75f;
        public const float DefEarlyRaidClampMaxStorytellerFrac = 1.30f;
        public const float DefMidRaidClampMinStorytellerFrac = 0.90f;
        public const float DefMidRaidClampMaxStorytellerFrac = 1.80f;
        public const float DefLateRaidClampMinStorytellerFrac = 1.00f;
        public const float DefLateRaidClampMaxStorytellerFrac = 2.30f;
        /// <summary>When true, WD colony raids and caravan interception raids use attacker/caravan strength as raid points with no storyteller-band clamp (floor/ceiling sliders hidden).</summary>
        public const bool DefAlwaysUseStrengthAsRaidPoints = false;
        public const bool DefAlwaysUseStrengthAsOutpostDefenseRaidPoints = false;
        /// <summary>Absolute clamps for reinforcements and other non-storyteller WD spawns that still use fixed points.</summary>
        public const int DefMinRaidPoints = 60;
        public const int DefMaxRaidPoints = 10000;

        // --- WORLD RAIDS PAGE ---
        public const bool DefAllowPlayerRaid = true;
        public const bool DefAllowPlayerOutpostRaid = true;
        public const float DefCdPlayerRaidDays = 5.0f;
        /// <summary>Global cap: max WD world raids targeting player (colonies + outposts) in a 1-day window.</summary>
        public const int DefMaxPlayerWdRaidsPerDay = 1;
        /// <summary>Global cap: max WD world raids targeting player in a 4-day window (≥ per-day cap).</summary>
        public const int DefMaxPlayerWdRaidsPer4Days = 2;
        /// <summary>Global cap: max WD world raids targeting player in a 7-day window (≥ per-4-day cap).</summary>
        public const int DefMaxPlayerWdRaidsPer7Days = 3;
        public const float DefRaidTargetRadius = 25f;
        public const float DefTier1AttackRangeBaseline = 12f;
        public const float DefTier2AttackRangeBaseline = 16f;
        public const float DefTier3AttackRangeBaseline = 20f;
        public const float DefTier4AttackRangeBaseline = 25f;
        public const float DefAttackRangeTimeMaxBonusPct = 2.0f;
        public const float DefAttackRangeDaysToMax = 120f;
        /// <summary>Unified attacker/defender ally pull-in radius (tiles). Replaces legacy att/def radii.</summary>
        public const float DefRaidAllyRadius = 6f;
        public const float DefMinRaidRatio = 1.0f;
        public const float DefRazeChance = 0.35f;
        /// <summary>Days a WD raze ruin blocks founding before despawning.</summary>
        public const float DefRuinLingerDays = 7f;
        /// <summary>Experimental: player map/simulated conquest can roll raze instead of the conquest opportunity menu.</summary>
        public const bool DefExperimentalPlayerConquestRaze = false;

        // --- FEATURE A: TARGET-OF-OPPORTUNITY RETARGETING (experimental) ---
        public const bool DefExperimentalTargetOfOpportunity = true;
        /// <summary>Cheap per-event coin flip rolled before any strength math; also the primary performance throttle.</summary>
        public const float DefTargetOfOpportunityEligibilityRollPct = 0.15f;
        /// <summary>Required ratio advantage over the current target's ratio to justify switching.</summary>
        public const float DefTargetOfOpportunityMinRatioAdvantage = 0.25f;
        /// <summary>Per-traveler cap on target-of-opportunity retargets.</summary>
        public const int DefTargetOfOpportunityMaxRetargets = 2;
        /// <summary>Combined cap shared with Feature B's maraud continuations.</summary>
        public const int DefTargetChangesMaxLifetime = 3;
        /// <summary>Anti-dogpile stamp duration (ticks) on an accepted candidate, shared with Feature B.</summary>
        public const int DefTargetOfOpportunityDogpileCooldownTicks = 3500;

        // --- FEATURE B: POST-VICTORY MARAUDING (experimental) ---
        public const bool DefExperimentalContinueAfterConquest = true;
        public const float DefMaraudingChanceToOccurPct = 0.50f;
        public const float DefMaraudingMinSurvivingStrengthAbsolute = 500f;
        public const int DefMaraudingMaxChainedTargets = 3;

        // --- FEATURE C: SETTLEMENT-LAUNCHED AMBUSH (experimental) ---
        public const bool DefExperimentalSettlementAmbush = true;
        public const float DefSettlementAmbushChancePct = 0.50f;
        /// <summary>Must be well above 1.0: a settlement that is not massively stronger is better off waiting at home.</summary>
        public const float DefSettlementAmbushMinStrengthRatio = 1.6f;
        /// <summary>Matches the inner blocked-tile / expansion min radius (5), not Rapid Response range.</summary>
        public const float DefSettlementAmbushWatchRangeTiles = 5f;
        public const float DefSettlementAmbushMaxStrengthRatio = 2.0f;
        public const SettlementTier DefSettlementAmbushMinTier = SettlementTier.T2;
        /// <summary>0 = unlimited. Performance pack overwrites this (Low 4, Medium 8, High 0).</summary>
        public const int DefSettlementAmbushMaxConcurrent = 8;

        /// <summary>Shared A/B/C escape hatch: when true, skips the <see cref="WdEscalation.IsMidOrLate"/> gate so target-of-opportunity, marauding, and settlement ambush can fire from the very start of a game, not just mid/late escalation.</summary>
        public const bool DefOpportunityFeaturesIgnoreEscalationGate = true;

        /// <summary>Experimental: when transferring pawns from an outpost, gate over-budget withdrawals behind Form Caravan / Leave / Mark Lost.</summary>
        public const bool DefExperimentalOutpostWithdrawStrengthBudget = true;
        /// <summary>Experimental: manual defense deploy picker enforces an offensive-strength selection budget.</summary>
        public const bool DefExperimentalOutpostDefenseDeployBudget = true;
        /// <summary>Play WD combat oneshots on the world map (AT / mortar / flak). On by default.</summary>
        public const bool DefEnableWorldMapSounds = true;
        /// <summary>AT Turret max strength / HP at spawn (Light / Medium / Heavy).</summary>
        public const float DefAtTurretLightMaxStrength = 50f;
        public const float DefAtTurretMediumMaxStrength = 100f;
        public const float DefAtTurretHeavyMaxStrength = 150f;
        /// <summary>AT Turret shell strength damage (Medium; Light/Heavy have their own defs).</summary>
        public const float DefAtTurretLightDamage = 75f;
        public const float DefAtTurretDamage = 100f;
        public const float DefAtTurretHeavyDamage = 125f;
        /// <summary>AT Turret reload after a shot (days). Medium uses DefAtTurretCooldownDays.</summary>
        public const float DefAtTurretLightCooldownDays = 0.35f;
        public const float DefAtTurretCooldownDays = 0.5f;
        public const float DefAtTurretHeavyCooldownDays = 0.75f;
        /// <summary>AT Turret fire / magnet range in tiles.</summary>
        public const float DefAtTurretLightRange = 4f;
        public const float DefAtTurretMediumRange = 5f;
        public const float DefAtTurretHeavyRange = 6f;
        /// <summary>AT Turret base hit chance 0–50% of max range (before skill-equivalent flat bonus).</summary>
        public const float DefAtTurretHitChance0To50PctRange = 0.95f;
        /// <summary>AT Turret base hit chance 51–75% of max range.</summary>
        public const float DefAtTurretHitChance51To75PctRange = 0.85f;
        /// <summary>AT Turret base hit chance 76–100% of max range.</summary>
        public const float DefAtTurretHitChance76To100PctRange = 0.70f;
        /// <summary>AT Turret build work (Light / Medium / Heavy). Defaults are twice prior road-parity costs.</summary>
        public const float DefAtTurretLightWork = 750f;
        public const float DefAtTurretMediumWork = 1500f;
        public const float DefAtTurretHeavyWork = 2250f;
        public const int DefAtTurretLightMinConstruction = 15;
        public const int DefAtTurretMediumMinConstruction = 25;
        public const int DefAtTurretHeavyMinConstruction = 35;
        public const float DefAtTurretLightExpeditionStrength = 50f;
        public const float DefAtTurretMediumExpeditionStrength = DefFallbackAsphaltRoadExpeditionStrength;
        public const float DefAtTurretHeavyExpeditionStrength = 175f;
        public const float DefRaidAllyLossMultiplier = 0.40f;
        /// <summary>Legacy ModConfig only. Field-lerp on clash/outpost defense removed; unused.</summary>
        public const float DefMaxRaidDays = 4.0f;
        /// <summary>Legacy ModConfig only. Field-lerp on clash/outpost defense removed; unused.</summary>
        public const float DefMinEfficiency = 0.5f;
        public const float DefStrengthLossPerHour = 0.015f;
        public const float DefMaxTravelPercentageStrengthLoss = 0.75f;
        /// <summary>Master switch: when false, WD travelers never run water-capable pathfinding (land routes only).</summary>
        public const bool DefAllowCaravansTravelOverWater = true;
        /// <summary>When true, WD travelers use water-capable routing only if vanilla world pathing finds no route. When false (default), vanilla and water-capable routes are compared and the faster (by hop difficulty) route is used.</summary>
        public const bool DefOnlyTravelAcrossWaterIfNoOtherWay = true;
        /// <summary>Movement difficulty units for entering a water-covered tile (vanilla mountain/hill-style scale; matches typical mountain hop cost).</summary>
        public const float DefTravelerWaterMovementDifficulty = 4f;
        public const float DefWaterPathLandThresholdDays = 1.5f;
        /// <summary>0 = always crow-flies prep; 1 = always FindPath prep; default 0.3 ≈ every 3rd assess exact. Fraction 0–1 (UI shows %).</summary>
        public const float DefTravelPrepExactPercent = 0.3f;

        public const float DefGarrisonRetainPct = 0.20f;
        public const float DefDropPodRaidChance = 0.40f;
        public const float DefDropPodRaidChanceT3 = 0.25f;
        public const TechLevel DefDropPodRaidMinTechLevel = TechLevel.Neolithic;
        public const float DefDropPodRaidAttritionMult = 6f;
        public const float DefColonySiegeRaidChance = 0.25f;
        public const TechLevel DefNpcT4MortarMinTechLevel = TechLevel.Neolithic;

        // --- SABOTAGE PAGE ---
        public const float DefWeightSabSuccess = 37f;
        public const float DefWeightSabCleanFail = 32f;
        public const float DefWeightSabInjuredFail = 25f;
        public const float DefWeightSabFatalFail = 6f;
        public const float DefSabSkillSuccessWeightBonus = 5.0f;
        public const float DefSabTierSuccessWeightPenalty = 5.0f;
        public const float DefSabHealthImpactWeight = 1.0f;
        public const float DefSabSocialCleanBonus = 0.02f;
        public const float DefSabCombatSurvivalBonus = 0.02f;
        public const float DefSabBaseReduc = 225f;
        public const float DefSabSkillReductionBonus = 20f;
        public const float DefSabCdDays = 5.0f;

        // --- DISINFORMATION PAGE ---
        public const float DefWeightDisSuccess = 40f;
        public const float DefWeightDisCleanFail = 30f;
        public const float DefWeightDisInjuredFail = 25f;
        public const float DefWeightDisFatalFail = 5f;
        public const float DefDisSkillSuccessWeightBonus = 5.0f;
        public const float DefDisTierSuccessWeightPenalty = 5.0f;
        public const float DefDisHealthImpactWeight = 1.0f;
        public const float DefDisSocialCleanBonus = 0.02f;
        public const float DefDisCombatSurvivalBonus = 0.02f;
        public const float DefDisBaseReduc = 150f;
        public const float DefDisSkillReductionBonus = 15f;
        public const float DefDisCdDays = 5.0f;

        // --- WD OUTPOSTS PAGE ---
        /// <summary>Max path distance (tiles) for WD AI trader caravans when choosing a neutral/allied destination. Independent from raid target radius.</summary>
        public const float DefTraderDestinationSearchRadius = 50f;
        public const int DefOutpostMinDistanceTiles = 4;
        public const float DefOutpostBuildCostMultiplier = 1f;
        public const float DefOutpostDeliveryStrengthCost = 50f;
        public const float DefOutpostDeliveryMinStrength = 100f;
        /// <summary>Silver budget per skill point per delivery (hunting/farming/mining). Fixed—does not scale with outpost cycle length. Items = (budget ÷ market value per bundle) × tile efficiency × skill.</summary>
        public const float DefOutpostSilverValuePerSkillPerCycle = 100f;
        /// <summary>Default production cycle: 30 in-game days (one delivery per 30 days).</summary>
        public const int DefOutpostProductionTicksInterval = 1800000; // 30 * 60000
        public const float DefOutpostProductionTimeMultiplier = 1f;
        public const float DefOutpostProductionOutputMultiplier = 1f;
        /// <summary>Default warehouse productivity aura bonus (fraction; 0.15 = +15%).</summary>
        public const float DefWarehouseAuraBonusPct = 0.15f;
        /// <summary>Default warehouse productivity aura radius in world tiles.</summary>
        public const float DefWarehouseAuraRadiusTiles = 12f;
        /// <summary>When true, embassies can award goodwill to temporarily hostile factions (never permanent enemies).</summary>
        public const bool DefEmbassyMayGainGoodwillWithHostiles = true;
        /// <summary>Compatibility toggle for vanilla-style outpost skill math. Off by default so Endless Growth-style skill levels can contribute above 20.</summary>
        public const bool DefClampOutpostSkillsAtLevel20 = false;
        /// <summary>Skill XP per relevant skill per occupant when an outpost production payout succeeds (delivery launched).</summary>
        public const float DefOutpostOccupantSkillXpPerProductionCycle = 5000f;
        /// <summary>No outpost XP for a skill at this level or higher (default 10).</summary>
        public const int DefOutpostOccupantSkillXpMaxLevel = 10;
        /// <summary>Academy default: base skill XP each eligible student receives per day before teacher multiplier.</summary>
        public const float DefAcademyBaseXpPerDay = 2000f;
        /// <summary>Academy default: minimum teacher level required to teach a selected skill.</summary>
        public const int DefAcademyMinTeacherSkill = 8;
        /// <summary>Academy default: student learning cap is teacher level minus this offset.</summary>
        public const int DefAcademyTeachCapOffset = 3;
        /// <summary>Academy default XP mode: false uses vanilla learning pipeline (passions/global learning); true applies flat direct XP.</summary>
        public const bool DefAcademyUseFlatDirectXp = false;
        /// <summary>Default mining baseline per def when no override is saved. Fallback for unknown defs (e.g. modded).</summary>
        public const float DefMiningBaselineFallback = 10f;
        /// <summary>Per-def default mining baseline (units per Mining skill per cycle). Used by mining baseline dialog when key is missing; Reset restores these.</summary>
        public static readonly Dictionary<string, float> DefMiningBaselineByDefName = new Dictionary<string, float>
        {
            { "Steel", 40f }, { "Jade", 5f }, { "Silver", 8f }, { "Gold", 2.5f }, { "Plasteel", 2.5f }, { "Uranium", 6f },
            { "ComponentSpacer", 0.2f }, { "ComponentIndustrial", 1f },
            { "BlocksGranite", 25f }, { "BlocksMarble", 25f }, { "BlocksSandstone", 25f }, { "BlocksLimestone", 25f }, { "BlocksSlate", 25f }
        };
        /// <summary>Default baseline for a def: DefMiningBaselineByDefName if present, else 25 for any Blocks*, else computed mining baseline from <see cref="Outpost_Baselines.GetMiningBaselinePerSkill"/>, else DefMiningBaselineFallback.</summary>
        public static float GetDefaultMiningBaselineForDef(string defName)
        {
            if (string.IsNullOrEmpty(defName)) return DefMiningBaselineFallback;
            if (DefMiningBaselineByDefName.TryGetValue(defName, out float v)) return v;
            if (defName.StartsWith("Blocks", System.StringComparison.Ordinal)) return 25f;
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (def != null) return Outpost_Baselines.GetMiningBaselinePerSkill(def);
            Log.Warning(
                $"{MiningScatterDiscovery.DevLogPrefix} GetDefaultMiningBaselineForDef: no ThingDef for '{defName}'; using DefMiningBaselineFallback ({DefMiningBaselineFallback}).");
            return DefMiningBaselineFallback;
        }

        /// <summary>When true, outpost upgrades require their XML material costs.</summary>
        public const bool DefOutpostUpgradesCostMaterials = true;
        /// <summary>When true, outpost upgrades require their XML research projects.</summary>
        public const bool DefOutpostUpgradesRequireResearch = true;

        /// <summary>When true, player outposts can show the Launch Attack gizmo.</summary>
        public const bool DefEnableOutpostLaunchAttack = true;
        /// <summary>When true, Build menu includes road construction.</summary>
        public const bool DefEnableOutpostBuildRoads = true;
        /// <summary>When true, Build menu includes road-block build/clear.</summary>
        public const bool DefEnableOutpostBuildRoadBlocks = true;
        /// <summary>When true, Build menu includes spike-trap build/clear.</summary>
        public const bool DefEnableOutpostBuildTraps = true;

        // Establishment requirement toggles (all default ON)
        public const bool DefOutpostReqBiome = true;
        public const bool DefOutpostReqFertility = true;
        public const bool DefOutpostReqAnimalAbundance = true;
        public const bool DefOutpostReqFishAbundance = true;
        public const bool DefOutpostReqMiningTerrain = true;
        public const bool DefOutpostReqResearch = true;
        public const bool DefOutpostReqNearbySettlements = true;
        public const bool DefOutpostReqMinPawns = true;
        public const bool DefOutpostReqMinSkill = true;
        public const bool DefOutpostReqCost = true;
        /// <summary>When true, farming / hunting / fishing tile scores are multiplied by (1 - tile pollution).</summary>
        public const bool DefPollutionEcologyPenaltyEnabled = true;

        /// <summary>When true, ground WD travelers take strength damage leaving polluted tiles.</summary>
        public const bool DefTravelerPollutionDamageEnabled = true;
        /// <summary>When true, PirateWaster factions skip WD pollution damage and auto-decontam skip.</summary>
        public const bool DefWasterPollutionImmunityEnabled = true;
        public const bool DefPollutionDamageRaiders = true;
        public const bool DefPollutionDamageExpansion = false;
        public const bool DefPollutionDamageConstruction = false;
        public const bool DefPollutionDamageTraders = false;
        public const bool DefPollutionDamagePlayerTravelers = false;
        /// <summary>Performance: add live pollution cost during WD FindPath (approach A). Low preset off; Med/High on.</summary>
        public const bool DefPollutionPathCostEnabled = true;
        /// <summary>Performance: one heavier repath when pre-commit path would gut strength. High preset only.</summary>
        public const bool DefPollutionPathRepathEnabled = false;
        /// <summary>Performance: cancel raid launch when pollution attrition would gut strength (approach B).</summary>
        public const bool DefPollutionPathPreCommitCancelEnabled = true;
        /// <summary>Pollution fraction below which traveler exit damage is ignored (0..1).</summary>
        public const float DefPollutionDamageIgnoreBelow = 0.06f;
        /// <summary>Strength lost when leaving a tile at the ignore-below threshold.</summary>
        public const float DefPollutionDamageAtThreshold = 6f;
        /// <summary>Strength lost when leaving a fully polluted tile (100%).</summary>
        public const float DefPollutionDamageAtFull = 400f;
        /// <summary>World-tile radius for site pollution average and NPC auto-decontam search (includes center).</summary>
        public const int DefPollutionDamageRadius = 2;
        /// <summary>Nominal offensive strength NPC settlements try to pay for each auto-decontam dispatch.</summary>
        public const float DefNpcSettlementDecontaminationStrengthCost = 10f;

        /// <summary>Player WD outpost: daily defensive regen uses max(this flat, defensive cap × fraction slider).</summary>
        public const float DefOutpostDefensiveRecoveryMinFlatPerDay = 25f;
        /// <summary>Fraction of defensive cap per day toward max (combined with flat minimum).</summary>
        public const float DefOutpostDefensiveRecoveryFractionPerDay = 0.10f;
        /// <summary>Player WD outpost: daily offensive regen uses max(this flat, offensive target cap × fraction) × upgrade multiplier.</summary>
        public const float DefOutpostOffensiveRecoveryMinFlatPerDay = 80f;
        /// <summary>Fraction of offensive target strength per day toward cap (combined with flat minimum).</summary>
        public const float DefOutpostOffensiveRecoveryFractionPerDay = 0.15f;
        /// <summary>Flat injury severity healed per day for mothballed outpost occupants (scaled by hospital upgrades per outpost).</summary>
        public const float DefOutpostOccupantHealSeverityPerDay = 2f;

        public const float DefExpertStrategistMaxBonusPct = 0.50f;
        public const float DefExpertEntertainerMaxBonusPct = 0.25f;
        public const float DefExpertCookMaxBonusPct = 0.25f;
        public const float DefExpertDoctorMaxBonusPct = 0.50f;
        public const float DefExpertEngineerMaxBonusPct = 0.50f;
        public const float DefExpertEngineerConstructionRadiusMaxBonusPct = 0.30f;
        public const float DefExpertRecruiterMaxBonusPct = 0.30f;
        public const int DefExpertReferenceSkillLevel = 20;

        /// <summary>Virtual pawns spawned when founding on conquered settlement ruins (tier 1 settlement).</summary>
        public const int DefConquestFoundingPawnsT1 = 2;
        public const int DefConquestFoundingPawnsT2 = 4;
        public const int DefConquestFoundingPawnsT3 = 9;
        public const int DefConquestFoundingPawnsT4 = 14;
        /// <summary>Generated conquest founders: each relevant outpost skill is raised to at least this level (0–20).</summary>
        public const int DefConquestFoundingMinRelevantSkill = 4;
        /// <summary>When true, defeating an NPC settlement can turn it into ruins and offer establishing a WD outpost when leaving the map.</summary>
        public const bool DefOutpostAfterConquestEnabled = true;

        // Main settings toggles
        public const bool DefShowAdvancedSettings = false;
        // Update popup visibility (default ON)
        public const bool DefShowUpdatePopups = true;
        /// <summary>When true, writes extra <c>[WD Perf]</c> correlation lines to the log (food pulse, pathing, threat, etc.).</summary>
        public const bool DefVerboseLogging = false;
        public const KeyCode DefWorldMapOverlayHoldKey = KeyCode.LeftAlt;

        // --- FOOD LOGISTICS PAGE ---
        public const bool DefFoodLogisticsActive = true;
        public const float DefFoodConsumptionPerPawn = 2.0f;
        public const float DefFoodProductionPerSkill = 1.0f;
        /// <summary>Fixed daily food production granted to every outpost (any type).</summary>
        public const float DefFoodProductionPerOutpostBase = 3.0f;
        /// <summary>Max virtual food storage per outpost (slider).</summary>
        public const float DefMaxFoodPerOutpost = 300f;
        public const int DefMaxLogisticsRange = 25;
        /// <summary>Default and minimum (0–1) virtual food tile multiplier floor for farming/hunting hubs; effective mult = max(raw, floor). Slider cannot go below this.</summary>
        public const float DefVirtualFoodTileMultiplierFloor = 0.80f;

        // --- NOTIFICATIONS PAGE ---
        public const bool DefNotifyNewSettlement = true;
        /// <summary>Letter when an NPC raid conquers a tile and founds a replacement settlement nearby.</summary>
        public const bool DefNotifyNpcConquestSettlement = true;
        public const bool DefNotifySettlementRaided = true;
        public const bool DefNotifySettlementRazed = true;
        public const bool DefNotifyOutpostDestroyed = true;
        public const bool DefNotifyThreatLevel = true;
        public const bool DefNotifyCriticalFood = true;
        public const bool DefNotifyDropPodDeliveryInAaRange = true;
        public const bool DefNotifyOutpostUpkeep = true;
        public const bool DefNotifyConstructionInsufficientStrength = true;
        public const bool DefNotifyOutpostNoProduction = true;
        public const bool DefNotifyOutpostUnusedExperts = true;
        public const bool DefNotifyLateGameActive = true;
        public const bool DefNotifyMidGameActive = true;
        /// <summary>Legacy single toggle; only read for one-time migration to per-event diplomacy notifications.</summary>
        public const bool DefNotifyDiplomaticChange = true;
        public const bool DefNotifyBuffNerf = false;
        public const bool DefNotifyLeaderHandicap = false;
        public const bool DefNotifyUnderdogBuff = false;
        public const bool DefNotifyExpansionistZeal = true;
        public const bool DefNotifyAntiLeaderCoalition = true;
        public const bool DefNotifyRandomDiplomacy = true;
        public const bool DefNotifyTradeAllyDiplomacy = true;
        public const bool DefNotifyStrongFactionWar = true;
        public const int CurrentSettingsDataVersion = 4;
        // SURGICAL: New Defaults for Incoming Raid Notifications
        public const bool DefNotifyIncomingRaidColony = true;
        public const bool DefNotifyIncomingRaidOutpost = true;
        /// <summary>Feature A: letter when a raid's original player-owned target is successfully diverted onto a different, non-player target-of-opportunity candidate.</summary>
        public const bool DefNotifyRaidDivertedFromPlayer = true;
        /// <summary>Letter (neutral) when YOUR mortar outpost fires at a target. On by default.</summary>
        public const bool DefNotifyMortarHit = true;
        public const bool DefNotifyAntiAirHit = true;
        /// <summary>Letter when YOUR AA destroys/misses a hostile mortar shell. Off by default to avoid spam.</summary>
        public const bool DefNotifyPlayerAntiAirVsHostileMortarShell = false;
        /// <summary>Letter (negative) when an enemy T4 settlement mortar fires at one of your WD outposts/caravans/travelers. On by default.</summary>
        public const bool DefNotifyNpcMortarHitPlayer = true;
        /// <summary>Letter (neutral) when an enemy mortar fires at another NPC (settlement/caravan/traveler). Off by default to avoid spam.</summary>
        public const bool DefNotifyNpcMortarHitNpc = false;
        /// <summary>Letter when your AT Turret destroys a target (shell or clash). On by default.</summary>
        public const bool DefNotifyPlayerAtTurretKilledTarget = true;
        /// <summary>Letter when your AT Turret damages a target that survives. Off by default.</summary>
        public const bool DefNotifyPlayerAtTurretDamagedTarget = false;
        /// <summary>Letter when your AT Turret is destroyed in combat. On by default.</summary>
        public const bool DefNotifyPlayerAtTurretDestroyed = true;
        /// <summary>Letter when an enemy AT Turret damages your traveler/caravan. Off by default.</summary>
        public const bool DefNotifyNpcAtTurretDamagedPlayer = false;
        /// <summary>Letter when an enemy AT Turret destroys your traveler/caravan. On by default.</summary>
        public const bool DefNotifyNpcAtTurretKilledPlayer = true;
        public const bool DefNotifyWarehouseGoodsArrived = true;
        public const bool DefNotifyOutpostDeliveryToColonyArrived = true;
        public const bool DefNotifyPlayerCaravanClash = true;
        public const bool DefShowCaravanClashLootDialog = true;
        public const bool DefNotifyRapidResponseCaravanClash = true;
        /// <summary>Yellow letter the first time a player WD traveler takes pollution exit damage. On by default.</summary>
        public const bool DefNotifyTravelerPollutionDamage = true;
        public const bool DefNotifyOutpostPollutionDamage = true;
        public const bool DefNotifyPrisonerRecruitedUnderway = true;
        public const bool DefAlwaysShowOutpostTravelerIconsRegardlessOfZoom = true;
        public const bool DefAlwaysShowSettlementIconsRegardlessOfZoom = true;

        // --- World Generation PAGE ---
        public const float DefGenWeightT1 = 150.0f;
        public const float DefGenWeightT2 = 45.0f;
        public const float DefGenWeightT3 = 4.0f;
        public const float DefGenWeightT4 = 1.0f;
        /// <summary>0 = rarely join existing clusters; 100 = usually try to join when recreating.</summary>
        public const float DefSettlementTerritoryCoherence = 70f;
        /// <summary>0 = pack at min distance; 100 = extra gap of +300% of min distance (4× min total).</summary>
        public const float DefSettlementTerritorySpacing = 40f;
        /// <summary>0 = other factions use Spacing only; 100 = Spacing plus +3× min distance vs other factions.</summary>
        public const float DefSettlementOtherFactionDistance = 40f;
        /// <summary>Max same-faction settlements that may join one recreate cluster via cluster chance.</summary>
        public const int DefSettlementMaxPerCluster = 5;
        /// <summary>Min tiles between distinct same-faction clusters when a cluster is full or join fails. 0 = off.</summary>
        public const int DefSettlementMinDistanceBetweenClusters = 20;
        /// <summary>When true, Recreate Settlements also clears NPC road blocks, spike traps, and AT turrets.</summary>
        public const bool DefWorldSetupDestroyFortificationsOnRecreate = false;

        // --- GARRISON SETTINGS (KCSG) ---
        /// <summary>When true, WD hijacks KCSG base layout generation for player attacks on NPC settlements. Outpost defense maps ignore this.</summary>
        public const bool DefAllowWdSettlementBaseGeneration = true;
        public const float DefKcsgMultTribalT1 = 0.5f;
        public const float DefKcsgMultTribalT2 = 0.75f;
        public const float DefKcsgMultTribalT3 = 1.6f;
        public const float DefKcsgMultTribalT4 = 3.2f;
        public const float DefKcsgMultGenericT1 = 0.4f;
        public const float DefKcsgMultGenericT2 = 0.75f;
        public const float DefKcsgMultGenericT3 = 1.3f;
        public const float DefKcsgMultGenericT4 = 2.5f;
        /// <summary>Minimum fraction of the configured tier garrison multiplier a fully-depleted settlement still fields. Full offensive strength = 100% of the multiplier.</summary>
        public const float DefGarrisonOffensiveStrengthMinScale = 0.30f;
        /// <summary>When true, convert unbuildable cells in the KCSG layout (and optionally blend outward).</summary>
        public const bool DefKcsgAdaptiveTerrainPrep = true;
        /// <summary>Blocked-cell fraction above which flatten runs, unless Always clear rect is on.</summary>
        public const float DefKcsgBlockedFlattenThreshold = 0.25f;
        /// <summary>Experimental: skip the blocked-fraction gate and always flatten the layout rect.</summary>
        public const bool DefExperimentalAlwaysClearKcsgRect = false;
        /// <summary>Experimental: bleed flatten and filth/chunk wipe outward from the layout rect.</summary>
        public const bool DefExperimentalKcsgRectBlend = true;

        // --- GOODWILL & RAID GATE (bottom of Raid Point Multiplier UI) ---
        public const bool DefNoGoodwillFromHostilesOnConquest = true;
        public const bool DefDisableSettlementProximityGoodwill = true;
        public const bool DefBlockStorytellerRaidsOnlyWD = true;
        public const bool DefAllowStorytellerRaidsFromNonWdFactions = true;
        public const bool DefBlockStorytellerTradersOnlyWD = false;

        // --- GOODWILL FROM TRADE (Growth settings) ---
        public const bool DefGoodwillFromTradeEnabled = true;
        public const float DefGoodwillFromTradePer1000Silver = 2f;

        // --- CARAVANS PAGE ---
        public const float DefTraderCaravanCostStrength = 100f;
        public const float DefTraderCaravanSenderRewardStrength = 250f;
        public const float DefTraderCaravanReceiverRewardStrength = 150f;
        public const float DefTraderCaravanGoodwillGain = 4f;
        /// <summary>Per player map colony: minimum days between being picked as a WD trader destination. Starts when the trader is dispatched. 0 = no limit.</summary>
        public const float DefCooldownPlayerColonyTraderDays = 2f;

        public const float DefTraderTierUpgradeChanceT1ToT2 = 0.25f;
        public const float DefTraderTierUpgradeChanceT2ToT3 = 0.15f;
        public const float DefTraderTierUpgradeChanceT3ToT4 = 0.05f;

        /// <summary>Trader-caravan escort strength floors, keyed by sending settlement's tier (interception/combat math only; does not change the flat resource cost deducted at launch).</summary>
        public const float DefTraderEscortFloorT1 = 75f;
        public const float DefTraderEscortFloorT2 = 150f;
        public const float DefTraderEscortFloorT3 = 300f;
        public const float DefTraderEscortFloorT4 = 500f;
        /// <summary>Days after a settlement loses a trader caravan to interception during which its subsequent caravans go out at full tier-max offensive strength instead of the flat floor.</summary>
        public const float DefTraderEscortRecentInterceptWindowDays = 7f;

        // --- MORTAR OUTPOST / INTERCEPTION PAGE ---
        /// <summary>Max mortar strike / defensive engagement range in world tiles.</summary>
        public const float DefMortarRange = 40f;
        /// <summary>Days between mortar shots (shared by manual + defensive auto-fire).</summary>
        public const float DefCooldownMortarDays = 5f;
        /// <summary>Legacy auto-fire tuning (distance-linear miss). Superseded by band hit chances; kept for save migration only.</summary>
        public const float DefMortarBaseMissChanceAtMaxRange = 0.80f;
        /// <summary>Legacy; hit chance now uses <see cref="MortarHitFlatBonusPerBestShootingLevel"/> × best shooter level.</summary>
        public const float DefMortarHitPerSkillPoint = 0.015f;
        /// <summary>Band base hit (0–1) before best-shooter flat bonus; band1 default 0.8 + level 20 (+0.2) = 100% at 50% max range.</summary>
        public const float DefMortarHitChance0To50PctRange = 0.80f;
        /// <summary>At 75% max range, default 0.55 + level 20 = 75% hit.</summary>
        public const float DefMortarHitChance51To75PctRange = 0.55f;
        /// <summary>At 100% max range, default 0.30 + level 20 = 50% hit.</summary>
        public const float DefMortarHitChance76To100PctRange = 0.30f;
        /// <summary>Per level of best garrison shooter: additive hit chance (0.01 = +1 percentage point at all ranges).</summary>
        public const float MortarHitFlatBonusPerBestShootingLevel = 0.01f;
        /// <summary>Per point of summed garrison Shooting: subtract this from the 1.0 cooldown duration multiplier (same unit as upgrade <c>mortarCooldownReduction</c>).</summary>
        public const float MortarCooldownReductionPerCumulativeShootingSkill = 0.02f;
        /// <summary>Minimum cooldown duration multiplier from skill + upgrades combined.</summary>
        public const float MortarCooldownMultiplierFloor = 0.20f;
        /// <summary>Flat offensive strength chip dealt when an NPC tier-4 settlement's mortar hits a hostile caravan/traveler.</summary>
        public const float DefNpcMortarDamage = 150f;
        /// <summary>Equivalent Shooting skill used for NPC settlement mortar miss-chance (since settlements have no pawn skills).</summary>
        public const float DefNpcMortarSkillEquivalent = 10f;
        /// <summary>Master toggle: when true, NPC tier-4 settlements act as defensive mortar interceptors (fire at hostile non-player WD travelers, else nearest hostile settlement).</summary>
        public const bool DefEnableNpcT4Mortar = true;
        /// <summary>NPC tier-4 settlement mortar max range in world tiles (decoupled from the player mortar range).</summary>
        public const float DefNpcMortarRange = DefMortarRange;
        /// <summary>Days between NPC tier-4 settlement mortar shots (decoupled from the player mortar cooldown).</summary>
        public const float DefNpcMortarCooldownDays = DefCooldownMortarDays;
        /// <summary>NPC mortar band base hit (0–1) at 0–50% of max range.</summary>
        public const float DefNpcMortarHitChance0To50PctRange = DefMortarHitChance0To50PctRange;
        /// <summary>NPC mortar band base hit (0–1) at 51–75% of max range.</summary>
        public const float DefNpcMortarHitChance51To75PctRange = DefMortarHitChance51To75PctRange;
        /// <summary>NPC mortar band base hit (0–1) at 76–100% of max range.</summary>
        public const float DefNpcMortarHitChance76To100PctRange = DefMortarHitChance76To100PctRange;
        /// <summary>How often, in ticks, each registered interceptor is scanned for targets. 1800 ticks = 30 seconds.</summary>
        public const int DefInterceptionScanIntervalTicks = 1800;
        /// <summary>Legacy save field; player mortar shell damage is <see cref="DefMortarBaseShellDamage"/> + outpost upgrades.</summary>
        public const float DefMortarDamagePerSkillPoint = 8f;
        /// <summary>Default player mortar shell strength damage before <see cref="OutpostUpgradeDef.mortarShellDamageBonus"/>.</summary>
        public const float DefMortarBaseShellDamage = 300f;
        /// <summary>World ticks per tile for player/hostile mortar shell travelers (lower = faster). Matches <see cref="WorldActions_Traveler.MortarShellTicksPerMove"/>.</summary>
        public const float DefMortarShellTicksPerMove = WorldActions_Traveler.MortarShellTicksPerMove;
        /// <summary>Default AA flak resolver damage before mortar shell upgrade bonuses.</summary>
        public const float DefAntiAirBaseDamage = 800f;
        /// <summary>World ticks per tile for Anti-Air flak shells (lower = faster). Matches <see cref="WorldActions_Traveler.FlakShellTicksPerMove"/>.</summary>
        public const float DefFlakShellTicksPerMove = WorldActions_Traveler.FlakShellTicksPerMove;
        /// <summary>Base AA cooldown in seconds (separate from mortar days).</summary>
        public const float DefCooldownAntiAirSeconds = 120f;
        /// <summary>Minimum AA cooldown in seconds after skill/upgrade reductions.</summary>
        public const float DefAntiAirCooldownFloorSeconds = 20f;
        /// <summary>Player AA engagement range in world tiles (independent of mortar range).</summary>
        public const float DefAntiAirRange = 32f;
        /// <summary>Player Anti-Air band base hit (0–1) at 0–50% of AA max range (pods/aerials only).</summary>
        public const float DefAntiAirHitChance0To50PctRange = DefMortarHitChance0To50PctRange;
        /// <summary>Player Anti-Air band base hit (0–1) at 51–75% of AA max range.</summary>
        public const float DefAntiAirHitChance51To75PctRange = DefMortarHitChance51To75PctRange;
        /// <summary>Player Anti-Air band base hit (0–1) at 76–100% of AA max range.</summary>
        public const float DefAntiAirHitChance76To100PctRange = DefMortarHitChance76To100PctRange;
        /// <summary>Player AA hit chance vs hostile mortar shells (0–1). Flat; ignores AA range bands.</summary>
        public const float DefAntiAirVsMortarHitChance = 0.80f;
        /// <summary>Master toggle: enemy T4 settlements may use anti-air.</summary>
        public const bool DefEnableNpcT4AntiAir = true;
        /// <summary>When true, T4 AA may engage player airborne targets while late-game modifier is active.</summary>
        public const bool DefEnableT4SettlementAntiAir = true;
        public const float DefNpcAntiAirRange = DefAntiAirRange;
        public const float DefNpcAntiAirCooldownSeconds = DefCooldownAntiAirSeconds;
        public const float DefNpcAntiAirDamage = DefAntiAirBaseDamage;
        public const float DefNpcAntiAirSkillEquivalent = DefNpcMortarSkillEquivalent;
        public const float DefNpcAntiAirHitChance0To50PctRange = DefAntiAirHitChance0To50PctRange;
        public const float DefNpcAntiAirHitChance51To75PctRange = DefAntiAirHitChance51To75PctRange;
        public const float DefNpcAntiAirHitChance76To100PctRange = DefAntiAirHitChance76To100PctRange;
        public const float DefNpcAntiAirVsMortarHitChance = DefAntiAirVsMortarHitChance;
        public const bool DefNotifyT4AntiAirHitPlayer = true;
        /// <summary>Enemy AA destroys / misses your mortar shells. Off by default (spammy).</summary>
        public const bool DefNotifyPlayerMortarShellShotDown = false;
        /// <summary>Rapid Response outposts add this percentage to their offensive strength target. 0.30 = +30%.</summary>
        public const float DefRapidResponseOffensiveStrengthBonus = 0.30f;
        /// <summary>Rapid Response outposts add this percentage to offensive recovery speed. 0.30 = +30%.</summary>
        public const float DefRapidResponseOffensiveRecoveryBonus = 0.30f;
        /// <summary>Rapid Response virtual counter-caravans move this much faster than normal WD travelers. 0.75 = 25% fewer ticks per move.</summary>
        public const float DefRapidResponseTicksPerMoveMultiplier = 0.75f;
        /// <summary>Max world-tile range for automatic Rapid Response virtual counter-caravans.</summary>
        public const float DefRapidResponseAutoInterceptRange = 25f;
        /// <summary>Max world-tile range for manual Rapid Response drop-pod pawn dispatch.</summary>
        public const float DefRapidResponseDropPodRange = 35f;
        /// <summary>World ticks per tile for ballistic drop-pod travelers (raids/deliveries; lower = faster). Matches <see cref="WorldActions_Traveler.DropPodTicksPerMove"/>.</summary>
        public const float DefDropPodTicksPerMove = WorldActions_Traveler.DropPodTicksPerMove;

        // ==========================================
        // SETTINGS VARIABLES
        // ==========================================

        public float tier1Share = DefTier1Share;
        public float tier2Share = DefTier2Share;
        public float tier3Share = DefTier3Share;
        public float tier4Share = DefTier4Share;

        // SURGICAL: Cap Variables
        public int tier1MaxActions = DefCapT1;
        public int tier2MaxActions = DefCapT2;
        public int tier3MaxActions = DefCapT3;
        public int tier4MaxActions = DefCapT4;

        public float weightGrow = DefWeightGrow;
        public float weightRaid = DefWeightRaid;
        public float weightMinorIncident = DefWeightMinorIncident;
        public float weightMajorIncident = DefWeightMajorIncident;
        public float weightBuildRoad = DefWeightBuildRoad;
        public float weightTrader = DefWeightTrader;
        public float weightFortify = DefWeightFortify;
        /// <summary>Settings UI only. Does not change in-game action rolls.</summary>
        public bool includeDevelopWeightInPercentDisplay = DefIncludeDevelopWeightInPercentDisplay;
        /// <summary>Road-building actor cooldown days (legacy save name; growth has no cooldown).</summary>
        public float cooldownGrowDays = DefCdGrowDays;
        public float cooldownExpandDays = DefCdExpandDays;
        public float cooldownRaidDays = DefCdRaidDays;
        /// <summary>AI (and similar) defenders: brief protection after losing a WD raid. Not used for player colony re-target spacing.</summary>
        public float cooldownBeingRaidedDays = DefCdBeingRaidedDays;
        public float cooldownIncidentDays = DefCdIncidentDays;
        public float cooldownTraderDays = DefCdTraderDays;
        public float cooldownFortifyDays = DefCdFortifyDays;

        public int fortifyMinTilesFromSelf = DefFortifyMinTilesFromSelf;
        public int fortifyMinTilesFromOtherSettlement = DefFortifyMinTilesFromOtherSettlement;
        public int fortifyMaxTilesFromSelf = DefFortifyMaxTilesFromSelf;
        public int fortifyMaxTravelTiles = DefFortifyMaxTravelTiles;
        public int fortifyTerritoryLinkMaxTiles = DefFortifyTerritoryLinkMaxTiles;
        public float fortifyFrontierEps = DefFortifyFrontierEps;
        public float fortifyTravelerStrength = DefFortifyTravelerStrength;
        public bool fortifyClearOnBuilderLoss = DefFortifyClearOnBuilderLoss;
        /// <summary>Player can mark tiles allies may not fortify.</summary>
        /// <summary>Unlocks no-fortify marks and blocks allies on marked tiles.</summary>
        public bool enableFortifyBlacklist = DefEnableFortifyBlacklist;
        /// <summary>Also block Neutral factions on marked tiles (not only Ally).</summary>
        public bool fortifyBlacklistApplyToNeutral = DefFortifyBlacklistApplyToNeutral;
        public float fortifyChanceRoadBlock = DefFortifyChanceRoadBlock;
        public float fortifyChanceTrap = DefFortifyChanceTrap;
        public float fortifyChanceTurret = DefFortifyChanceTurret;
        public float fortifyMultiT1ChanceOf2 = DefFortifyMultiT1ChanceOf2;
        public float fortifyMultiT2ChanceOf2 = DefFortifyMultiT2ChanceOf2;
        public float fortifyMultiT3ChanceOf2 = DefFortifyMultiT3ChanceOf2;
        public float fortifyMultiT4ChanceOf3 = DefFortifyMultiT4ChanceOf3;
        public int atTurretMaxT1 = DefAtTurretMaxT1;
        public int atTurretMaxT2 = DefAtTurretMaxT2;
        public int atTurretMaxT3 = DefAtTurretMaxT3;
        public int atTurretMaxT4 = DefAtTurretMaxT4;
        public int atTurretPlayerGlobalMax = DefAtTurretPlayerGlobalMax;
        public int atTurretPlayerPerSiteMax = DefAtTurretPlayerPerSiteMax;
        /// <summary>Default for the Auto-Add Arrivals outpost gizmo on newly created WD outposts.</summary>
        public bool autoAddPawnsOnArrivalDefault = DefAutoAddPawnsOnArrivalDefault;

        public int maxSettlements = DefMaxSettlements;
        public float passiveGrowthT1 = DefPassiveGrowthT1;
        public float passiveGrowthT2 = DefPassiveGrowthT2;
        public float passiveGrowthT3 = DefPassiveGrowthT3;
        public float passiveGrowthT4 = DefPassiveGrowthT4;
        /// <summary>Legacy field kept for save compat; unused by passive growth (see <see cref="passiveGrowthT1"/>).</summary>
        public float baseGrowthAmount = DefBaseGrowth;
        /// <summary>Legacy size-scaling intensity; unused (passive growth is flat per tier).</summary>
        public float growthScalingIntensity = DefGrowthScaling;
        public int expandMinRadius = DefExpandMinRad;
        public int expandMaxRadius = DefExpandMaxRad;
        /// <summary>Player outpost road-planning max tile range.</summary>
        public int maxRoadRange = DefMaxRoadRange;
        /// <summary>NPC settlement road-building max tile range.</summary>
        public int maxRoadRangeNpc = DefMaxRoadRangeNpc;
        public int maxRoadBlockRange = DefMaxRoadBlockRange;
        public float roadBlockLightFlatPenalty = DefRoadBlockLightFlatPenalty;
        public float roadBlockNormalFlatPenalty = DefRoadBlockNormalFlatPenalty;
        public float roadBlockHeavyFlatPenalty = DefRoadBlockHeavyFlatPenalty;
        public float roadBlockLightExpeditionStrength = DefRoadBlockLightExpeditionStrength;
        public float roadBlockNormalExpeditionStrength = DefRoadBlockNormalExpeditionStrength;
        public float roadBlockHeavyExpeditionStrength = DefRoadBlockHeavyExpeditionStrength;
        public float roadBlockLightWork = DefRoadBlockLightWork;
        public float roadBlockNormalWork = DefRoadBlockNormalWork;
        public float roadBlockHeavyWork = DefRoadBlockHeavyWork;
        public float roadBlockLightMaxHealth = DefRoadBlockLightMaxHealth;
        public float roadBlockNormalMaxHealth = DefRoadBlockNormalMaxHealth;
        public float roadBlockHeavyMaxHealth = DefRoadBlockHeavyMaxHealth;
        public int maxSpikeTrapRange = DefMaxSpikeTrapRange;
        public float spikeTrapSpikeWork = DefSpikeTrapSpikeWork;
        public float spikeTrapCaltropsWork = DefSpikeTrapCaltropsWork;
        public float spikeTrapSpikeExpeditionStrength = DefSpikeTrapSpikeExpeditionStrength;
        public float spikeTrapCaltropsExpeditionStrength = DefSpikeTrapCaltropsExpeditionStrength;
        public float spikeTrapSpikeDamage = DefSpikeTrapSpikeDamage;
        public float spikeTrapCaltropsDamage = DefSpikeTrapCaltropsDamage;
        public float spikeTrapSpikeMaxHealth = DefSpikeTrapSpikeMaxHealth;
        public float spikeTrapCaltropsMaxHealth = DefSpikeTrapCaltropsMaxHealth;
        public int spikeTrapMaxTriggersPerTraveler = DefSpikeTrapMaxTriggersPerTraveler;
        public int maxDecontaminationRange = DefMaxDecontaminationRange;
        public float decontaminationWork = DefDecontaminationWork;
        public float decontaminationExpeditionStrength = DefDecontaminationExpeditionStrength;
        public float decontaminationPollutionReductionPp = DefDecontaminationPollutionReductionPp;
        public float fallbackDirtRoadMovement = DefFallbackDirtRoadMovement;
        public float fallbackStoneRoadMovement = DefFallbackStoneRoadMovement;
        public float fallbackAsphaltRoadMovement = DefFallbackAsphaltRoadMovement;
        public float fallbackDirtRoadWork = DefFallbackDirtRoadWork;
        public float fallbackStoneRoadWork = DefFallbackStoneRoadWork;
        public float fallbackAsphaltRoadWork = DefFallbackAsphaltRoadWork;
        public float fallbackDirtRoadExpeditionStrength = DefFallbackDirtRoadExpeditionStrength;
        public float fallbackStoneRoadExpeditionStrength = DefFallbackStoneRoadExpeditionStrength;
        public float fallbackAsphaltRoadExpeditionStrength = DefFallbackAsphaltRoadExpeditionStrength;
        public int fallbackDirtRoadMinConstruction = DefFallbackDirtRoadMinConstruction;
        public int fallbackStoneRoadMinConstruction = DefFallbackStoneRoadMinConstruction;
        public int fallbackAsphaltRoadMinConstruction = DefFallbackAsphaltRoadMinConstruction;
        public float fallbackDirtRoadWinterReduction = DefFallbackDirtRoadWinterReduction;
        public float fallbackStoneRoadWinterReduction = DefFallbackStoneRoadWinterReduction;
        public float fallbackAsphaltRoadWinterReduction = DefFallbackAsphaltRoadWinterReduction;
        public float minorIncidentSeverity = DefMinorIncSev;
        public float majorIncidentSeverity = DefMajorIncSev;
        public int localMaxT1 = DefLocalMaxT1;
        public int localMaxT2 = DefLocalMaxT2;
        public int localMaxT3 = DefLocalMaxT3;
        public int localMaxT4 = DefLocalMaxT4;
        public int sameTierNeighborsToUpgradeT1 = DefSameTierNeighborsToUpgradeT1;
        public int sameTierNeighborsToUpgradeT2 = DefSameTierNeighborsToUpgradeT2;
        public int sameTierNeighborsToUpgradeT3 = DefSameTierNeighborsToUpgradeT3;
        public float expansionSuccessChance = DefExpansionSuccessChance;
        public float tier1BaseDefensiveStrength = DefTier1BaseDefensiveStrength;
        public float tier2BaseDefensiveStrength = DefTier2BaseDefensiveStrength;
        public float tier3BaseDefensiveStrength = DefTier3BaseDefensiveStrength;
        public float tier4BaseDefensiveStrength = DefTier4BaseDefensiveStrength;
        public float playerOutpostBaseDefensiveStrength = DefPlayerOutpostBaseDefensiveStrength;

        public float revoltChance = DefRevoltChance;
        public float diplomacyChangeChance = DefDiplomacyChangeChance;
        public bool enableLeaderHandicap = DefEnableLeaderHandicap;
        public bool enableUnderdogBuff = DefEnableUnderdogBuff;
        public bool enableAntiLeaderCoalition = DefEnableAntiLeaderCoalition;
        public bool enableRandomDiplomacy = DefEnableRandomDiplomacy;
        public bool enableStrongFactionWar = DefEnableStrongFactionWar;
        public float strongFactionWarChance = DefStrongFactionWarChance;
        public float strongFactionWarTopPct = DefStrongFactionWarTopPct;
        public bool strongFactionWarRequireMidOrLate = DefStrongFactionWarRequireMidOrLate;
        public bool enableExpansionistZeal = DefEnableExpansionistZeal;

        public float durLeaderHandicapDays = DefDurLeaderHandicapDays;
        public float cdLeaderHandicapDays = DefCdLeaderHandicapDays;
        public float durUnderdogBuffDays = DefDurUnderdogBuffDays;
        public float cdUnderdogBuffDays = DefCdUnderdogBuffDays;
        public float durExpansionistZealDays = DefDurExpansionistZealDays;
        public float cdExpansionistZealDays = DefCdExpansionistZealDays;
        public float durAntiLeaderCoalitionDays = DefDurAntiLeaderCoalitionDays;
        public float cdAntiLeaderCoalitionDays = DefCdAntiLeaderCoalitionDays;
        public float zealTriggerChance = DefZealTriggerChance;
        public float leaderHandicapTriggerChance = DefLeaderHandicapTriggerChance;
        public float underdogBuffTriggerChance = DefUnderdogBuffTriggerChance;
        public float antiLeaderCoalitionTriggerChance = DefAntiLeaderCoalitionTriggerChance;
        public float zealRaidRangeMult = DefZealRaidRangeMult;
        public float zealAttritionMult = DefZealAttritionMult;
        public float underdogActionShareMult = DefUnderdogActionShareMult;
        public float underdogIncidentWeightMult = DefUnderdogIncidentWeightMult;
        public float underdogIncidentSeverityMult = DefUnderdogIncidentSeverityMult;
        public float underdogGrowthGainMult = DefUnderdogGrowthGainMult;
        public float leaderIncidentWeightMult = DefLeaderIncidentWeightMult;
        public float leaderIncidentSeverityMult = DefLeaderIncidentSeverityMult;
        public float alliedRaidOrderMinWinChance = DefAlliedRaidOrderMinWinChance;
        public int alliedRaidClaimCostT1 = DefAlliedRaidClaimCostT1;
        public int alliedRaidClaimCostT2 = DefAlliedRaidClaimCostT2;
        public int alliedRaidClaimCostT3 = DefAlliedRaidClaimCostT3;
        public int alliedRaidClaimCostT4 = DefAlliedRaidClaimCostT4;

        public bool enableSettlementBuy = DefEnableSettlementBuy;
        public float settlementBuyAskT1 = DefSettlementBuyAskT1;
        public float settlementBuyAskT2 = DefSettlementBuyAskT2;
        public float settlementBuyAskT3 = DefSettlementBuyAskT3;
        public float settlementBuyAskT4 = DefSettlementBuyAskT4;
        public float settlementBuySilverPerGoodwill = DefSettlementBuySilverPerGoodwill;
        public float settlementBuyMaxGoodwillShare = DefSettlementBuyMaxGoodwillShare;
        public bool notifySettlementBuyStarted = DefNotifySettlementBuyStarted;
        public bool notifySettlementBuyCompleted = DefNotifySettlementBuyCompleted;
        public bool notifySettlementBuyAborted = DefNotifySettlementBuyAborted;
        public bool enableDiplomacyNegotiate = DefEnableDiplomacyNegotiate;
        public float negotiateAskMinSilver = DefNegotiateAskMinSilver;
        public float negotiateAskMaxSilver = DefNegotiateAskMaxSilver;
        public bool notifyDiplomacyNegotiateStarted = DefNotifyDiplomacyNegotiateStarted;
        public bool notifyDiplomacyNegotiateCompleted = DefNotifyDiplomacyNegotiateCompleted;
        public bool notifyDiplomacyNegotiateAborted = DefNotifyDiplomacyNegotiateAborted;

        public bool enableFactionBribe = DefEnableFactionBribe;
        public float bribeSettlementSilverPerStrength = DefBribeSettlementSilverPerStrength;
        public float bribeCaravanSilverPerStrengthEarly = DefBribeCaravanSilverPerStrengthEarly;
        public float bribeCaravanSilverPerStrengthMid = DefBribeCaravanSilverPerStrengthMid;
        public float bribeCaravanSilverPerStrengthLate = DefBribeCaravanSilverPerStrengthLate;
        public int bribeCeasefireDaysShort = DefBribeCeasefireDaysShort;
        public int bribeCeasefireDaysMedium = DefBribeCeasefireDaysMedium;
        public int bribeCeasefireDaysLong = DefBribeCeasefireDaysLong;
        public float bribeCeasefireDiscountMedium = DefBribeCeasefireDiscountMedium;
        public float bribeCeasefireDiscountLong = DefBribeCeasefireDiscountLong;
        public float bribeRaidAskFloorFraction = DefBribeRaidAskFloorFraction;
        public float bribeInvestmentFraction = DefBribeInvestmentFraction;
        public int bribeCaravanInvestmentRadiusTiles = DefBribeCaravanInvestmentRadiusTiles;
        public float bribeGoodwillDivisor = DefBribeGoodwillDivisor;
        public bool notifyBribeSettlementCompleted = DefNotifyBribeSettlementCompleted;
        public bool notifyBribeSettlementAborted = DefNotifyBribeSettlementAborted;
        public bool notifyBribeRaidCompleted = DefNotifyBribeRaidCompleted;
        public bool notifyBribeRaidAborted = DefNotifyBribeRaidAborted;
        public bool notifyBribeLostInTransit = DefNotifyBribeLostInTransit;
        public bool notifyBribeCeasefireExpired = DefNotifyBribeCeasefireExpired;

        public int alliedRaidAwardCostT1 = DefAlliedRaidAwardCostT1;
        public int alliedRaidAwardCostT2 = DefAlliedRaidAwardCostT2;
        public int alliedRaidAwardCostT3 = DefAlliedRaidAwardCostT3;
        public int alliedRaidAwardCostT4 = DefAlliedRaidAwardCostT4;
        public int orderedRoadBaseCostT1 = DefOrderedRoadBaseCostT1;
        public int orderedRoadBaseCostT2 = DefOrderedRoadBaseCostT2;
        public int orderedRoadBaseCostT3 = DefOrderedRoadBaseCostT3;
        public int orderedRoadBaseCostT4 = DefOrderedRoadBaseCostT4;
        public float orderedRoadPerSegmentT1 = DefOrderedRoadPerSegmentRateT1;
        public float orderedRoadPerSegmentT2 = DefOrderedRoadPerSegmentRateT2;
        public float orderedRoadPerSegmentT3 = DefOrderedRoadPerSegmentRateT3;
        public int orderedTraderGoodwillCost = DefOrderedTraderGoodwillCost;
        public int conquestAllyGiftGoodwillT1 = DefConquestAllyGiftGoodwillT1;
        public int conquestAllyGiftGoodwillT2 = DefConquestAllyGiftGoodwillT2;
        public int conquestAllyGiftGoodwillT3 = DefConquestAllyGiftGoodwillT3;
        public int conquestAllyGiftGoodwillT4 = DefConquestAllyGiftGoodwillT4;

        /// <summary>Live notification radius (tiles) for Nearby world event letters. UI 1–500.</summary>
        public float notificationRadiusTiles = DefNotificationRadiusTiles;
        /// <summary>Legacy ModConfig only. Influence Radius unused.</summary>
        public float influenceStartTiles = DefInfluenceStartTiles;
        /// <summary>Legacy ModConfig only. Influence Radius unused.</summary>
        public float influenceWealthPer10k = DefInfluenceWealthPer10k;
        /// <summary>Legacy ModConfig only. Influence Radius unused.</summary>
        public float influencePerDay = DefInfluencePerDay;
        /// <summary>Legacy ModConfig only. Influence Radius unused.</summary>
        public float influencePer10kOutpostDefense = DefInfluencePer10kOutpostDefense;

        public bool enableLateGameScaling = DefEnableLateGameScaling;
        public bool enableOutpostIncidents = DefEnableOutpostIncidents;
        public float outpostIncidentSeverity = DefOutpostIncidentSeverity;
        public float outpostIncidentDailyChance = DefOutpostIncidentDailyChance;
        public bool notifyOutpostIncident = DefNotifyOutpostIncident;
        public float coalitionRaidPriorityBias = DefCoalitionRaidPriorityBias;
        public float midGameShareThreshold = DefMidGameShareThreshold;
        public float midGameOutpostStrengthThreshold = DefMidGameOutpostStrengthThreshold;
        public float midGameRaidBiasPct = DefMidGameRaidBiasPct;
        public float midGameGrowthMult = DefMidGameGrowthMult;
        public float midGameAttackRangeBonusPct = DefMidGameAttackRangeBonusPct;
        public bool enableMidGameAllyRadiusScaling = DefEnableMidGameAllyRadiusScaling;
        public float midGameAllyRadiusBonusPct = DefMidGameAllyRadiusBonusPct;
        public int midGameExpandTowardPlayerMaxTiles = DefMidGameExpandTowardPlayerMaxTiles;
        public float midGameGarrisonBoostPct = DefMidGameGarrisonBoostPct;
        public bool enableMidGameT4SettlementMortar = DefEnableMidGameT4SettlementMortar;
        public bool enableMidGameT4SettlementAntiAir = DefEnableMidGameT4SettlementAntiAir;
        public bool enableMidGameOutpostIncidents = DefEnableMidGameOutpostIncidents;
        public float midGameOutpostIncidentSeverity = DefMidGameOutpostIncidentSeverity;
        public float midGameOutpostIncidentDailyChance = DefMidGameOutpostIncidentDailyChance;
        public bool enableGoodwillDrain = DefEnableGoodwillDrain;
        public int goodwillDrainIntervalDays = DefGoodwillDrainIntervalDays;
        public int midGameGoodwillDrainAmount = DefMidGameGoodwillDrainAmount;
        public int lateGameGoodwillDrainAmount = DefLateGameGoodwillDrainAmount;
        public float lateGameShareThreshold = DefLateGameShareThreshold;
        public float lateGameOutpostStrengthThreshold = DefLateGameOutpostStrengthThreshold;
        public float lateGameRaidBiasPct = DefLateGameRaidBiasPct;
        public float lateGameGrowthMult = DefLateGameGrowthMult;
        public float lateGameAttackRangeBonusPct = DefLateGameAttackRangeBonusPct;
        public bool enableLateGameAllyRadiusScaling = DefEnableLateGameAllyRadiusScaling;
        public float lateGameAllyRadiusBonusPct = DefLateGameAllyRadiusBonusPct;
        public int lateGameExpandTowardPlayerMaxTiles = DefLateGameExpandTowardPlayerMaxTiles;
        public float lateGameGarrisonBoostPct = DefLateGameGarrisonBoostPct;
        public bool enableT4SettlementMortar = DefEnableT4SettlementMortar;

        public float caravanRaidPointsMinStorytellerFraction = DefCaravanRaidMinStorytellerFrac;
        public float caravanRaidPointsMaxStorytellerFraction = DefCaravanRaidMaxStorytellerFrac;
        public bool scaleRaidClampWithEscalation = DefScaleRaidClampWithEscalation;
        public float earlyRaidClampMinStorytellerFraction = DefEarlyRaidClampMinStorytellerFrac;
        public float earlyRaidClampMaxStorytellerFraction = DefEarlyRaidClampMaxStorytellerFrac;
        public float midRaidClampMinStorytellerFraction = DefMidRaidClampMinStorytellerFrac;
        public float midRaidClampMaxStorytellerFraction = DefMidRaidClampMaxStorytellerFrac;
        public float lateRaidClampMinStorytellerFraction = DefLateRaidClampMinStorytellerFrac;
        public float lateRaidClampMaxStorytellerFraction = DefLateRaidClampMaxStorytellerFrac;
        public bool alwaysUseStrengthAsRaidPoints = DefAlwaysUseStrengthAsRaidPoints;
        public bool alwaysUseStrengthAsOutpostDefenseRaidPoints = DefAlwaysUseStrengthAsOutpostDefenseRaidPoints;
        public float minRaidPoints = DefMinRaidPoints;
        public float maxRaidPoints = DefMaxRaidPoints;

        public bool allowPlayerRaid = DefAllowPlayerRaid;
        public bool allowPlayerOutpostRaid = DefAllowPlayerOutpostRaid;
        /// <summary>Player home settlement: days before another WD world raid can target it again; same value seeds initial colony shield.</summary>
        public float cooldownPlayerRaidDays = DefCdPlayerRaidDays;
        /// <summary>Global max WD world raids against player colonies + outposts per day (1–10).</summary>
        public int maxPlayerWdRaidsPerDay = DefMaxPlayerWdRaidsPerDay;
        /// <summary>Global max WD world raids against player colonies + outposts per 4 days (1–20, ≥ per-day).</summary>
        public int maxPlayerWdRaidsPer4Days = DefMaxPlayerWdRaidsPer4Days;
        /// <summary>Global max WD world raids against player colonies + outposts per 7 days (1–30, ≥ per-4-days).</summary>
        public int maxPlayerWdRaidsPer7Days = DefMaxPlayerWdRaidsPer7Days;
        /// <summary>Flat attack range for player outpost manual raids only; NPC settlements use tier baselines + time bonus.</summary>
        public float raidTargetRadius = DefRaidTargetRadius;
        public float tier1AttackRangeBaseline = DefTier1AttackRangeBaseline;
        public float tier2AttackRangeBaseline = DefTier2AttackRangeBaseline;
        public float tier3AttackRangeBaseline = DefTier3AttackRangeBaseline;
        public float tier4AttackRangeBaseline = DefTier4AttackRangeBaseline;
        /// <summary>Max additive bonus applied to NPC attack range after that settlement ages to days-to-max (0.5 = +50%).</summary>
        public float attackRangeTimeMaxBonusPct = DefAttackRangeTimeMaxBonusPct;
        /// <summary>In-game days of settlement age until NPC attack range reaches full age bonus.</summary>
        public float attackRangeDaysToMax = DefAttackRangeDaysToMax;
        public float raidAllyRadius = DefRaidAllyRadius;
        public float minRaidRatio = DefMinRaidRatio;
        public float razeChance = DefRazeChance;
        public float ruinLingerDays = DefRuinLingerDays;
        public List<RaidOutcome> raidOutcomes = new List<RaidOutcome>();
        // Loss tables keyed by (side, won?): Close/Normal/Decisive per tier.
        public List<RaidSideLossEntry> raidAttLossOnWin = new List<RaidSideLossEntry>();
        public List<RaidSideLossEntry> raidAttLossOnLoss = new List<RaidSideLossEntry>();
        public List<RaidSideLossEntry> raidDefLossOnWin = new List<RaidSideLossEntry>();
        public List<RaidSideLossEntry> raidDefLossOnLoss = new List<RaidSideLossEntry>();
        public float raidAllyLossMultiplier = DefRaidAllyLossMultiplier;
        private List<RaidCasualtyEntry> _pendingLegacyRaidCasualties;
        private List<RaidOutcome> _cachedSortedRaidOutcomes;

        public List<RaidOutcome> GetRaidOutcomesSorted()
        {
            if (_cachedSortedRaidOutcomes == null)
                _cachedSortedRaidOutcomes = raidOutcomes != null ? raidOutcomes.OrderBy(o => o.threshold).ToList() : new List<RaidOutcome>();
            return _cachedSortedRaidOutcomes;
        }

        public void InvalidateRaidOutcomesCache() => _cachedSortedRaidOutcomes = null;

        public float GetAttackRangeBaseline(SettlementTier tier)
        {
            switch (tier)
            {
                case SettlementTier.T4: return tier4AttackRangeBaseline;
                case SettlementTier.T3: return tier3AttackRangeBaseline;
                case SettlementTier.T2: return tier2AttackRangeBaseline;
                default: return tier1AttackRangeBaseline;
            }
        }

        public float GetPassiveGrowthAmount(SettlementTier tier)
        {
            switch (tier)
            {
                case SettlementTier.T4: return Mathf.Max(0f, passiveGrowthT4);
                case SettlementTier.T3: return Mathf.Max(0f, passiveGrowthT3);
                case SettlementTier.T2: return Mathf.Max(0f, passiveGrowthT2);
                default: return Mathf.Max(0f, passiveGrowthT1);
            }
        }

        /// <summary>Legacy ModConfig only. Field-lerp removed; unused.</summary>
        public float maxRaidDays = DefMaxRaidDays;
        /// <summary>Legacy ModConfig only. Field-lerp removed; unused.</summary>
        public float minEfficiency = DefMinEfficiency;
        public float strengthLossPerHour = DefStrengthLossPerHour;
        public float maxTravelPercentageStrengthLoss = DefMaxTravelPercentageStrengthLoss;
        public bool allowCaravansTravelOverWater = DefAllowCaravansTravelOverWater;
        public bool onlyTravelAcrossWaterIfNoOtherWay = DefOnlyTravelAcrossWaterIfNoOtherWay;
        public float travelerWaterMovementDifficulty = DefTravelerWaterMovementDifficulty;
        public float waterPathLandThresholdDays = DefWaterPathLandThresholdDays;
        public float travelPrepExactPercent = DefTravelPrepExactPercent;
        public bool experimentalColonyWorldBuild = DefExperimentalColonyWorldBuild;
        public bool experimentalPlayerConquestRaze = DefExperimentalPlayerConquestRaze;

        public bool experimentalTargetOfOpportunity = DefExperimentalTargetOfOpportunity;
        public float targetOfOpportunityEligibilityRollPct = DefTargetOfOpportunityEligibilityRollPct;
        public float targetOfOpportunityMinRatioAdvantage = DefTargetOfOpportunityMinRatioAdvantage;
        public int targetOfOpportunityMaxRetargets = DefTargetOfOpportunityMaxRetargets;
        public int targetChangesMaxLifetime = DefTargetChangesMaxLifetime;
        public int targetOfOpportunityDogpileCooldownTicks = DefTargetOfOpportunityDogpileCooldownTicks;

        public bool experimentalContinueAfterConquest = DefExperimentalContinueAfterConquest;
        public float maraudingChanceToOccurPct = DefMaraudingChanceToOccurPct;
        public float maraudingMinSurvivingStrengthAbsolute = DefMaraudingMinSurvivingStrengthAbsolute;
        public int maraudingMaxChainedTargets = DefMaraudingMaxChainedTargets;

        public bool experimentalSettlementAmbush = DefExperimentalSettlementAmbush;
        public float settlementAmbushChancePct = DefSettlementAmbushChancePct;
        public float settlementAmbushMinStrengthRatio = DefSettlementAmbushMinStrengthRatio;
        public float settlementAmbushWatchRangeTiles = DefSettlementAmbushWatchRangeTiles;
        public float settlementAmbushMaxStrengthRatio = DefSettlementAmbushMaxStrengthRatio;
        public SettlementTier settlementAmbushMinTier = DefSettlementAmbushMinTier;
        public int settlementAmbushMaxConcurrent = DefSettlementAmbushMaxConcurrent;
        public bool opportunityFeaturesIgnoreEscalationGate = DefOpportunityFeaturesIgnoreEscalationGate;
        public bool experimentalOutpostWithdrawStrengthBudget = DefExperimentalOutpostWithdrawStrengthBudget;
        public bool experimentalOutpostDefenseDeployBudget = DefExperimentalOutpostDefenseDeployBudget;
        public bool enableWorldMapSounds = DefEnableWorldMapSounds;
        public float atTurretLightMaxStrength = DefAtTurretLightMaxStrength;
        public float atTurretMediumMaxStrength = DefAtTurretMediumMaxStrength;
        public float atTurretHeavyMaxStrength = DefAtTurretHeavyMaxStrength;
        public float atTurretLightDamage = DefAtTurretLightDamage;
        public float atTurretDamage = DefAtTurretDamage;
        public float atTurretHeavyDamage = DefAtTurretHeavyDamage;
        public float atTurretLightCooldownDays = DefAtTurretLightCooldownDays;
        public float atTurretCooldownDays = DefAtTurretCooldownDays;
        public float atTurretHeavyCooldownDays = DefAtTurretHeavyCooldownDays;
        public float atTurretLightRange = DefAtTurretLightRange;
        public float atTurretMediumRange = DefAtTurretMediumRange;
        public float atTurretHeavyRange = DefAtTurretHeavyRange;
        public float atTurretHitChance0To50PctRange = DefAtTurretHitChance0To50PctRange;
        public float atTurretHitChance51To75PctRange = DefAtTurretHitChance51To75PctRange;
        public float atTurretHitChance76To100PctRange = DefAtTurretHitChance76To100PctRange;
        public float atTurretLightWork = DefAtTurretLightWork;
        public float atTurretMediumWork = DefAtTurretMediumWork;
        public float atTurretHeavyWork = DefAtTurretHeavyWork;
        public int atTurretLightMinConstruction = DefAtTurretLightMinConstruction;
        public int atTurretMediumMinConstruction = DefAtTurretMediumMinConstruction;
        public int atTurretHeavyMinConstruction = DefAtTurretHeavyMinConstruction;
        public float atTurretLightExpeditionStrength = DefAtTurretLightExpeditionStrength;
        public float atTurretMediumExpeditionStrength = DefAtTurretMediumExpeditionStrength;
        public float atTurretHeavyExpeditionStrength = DefAtTurretHeavyExpeditionStrength;
        public bool enableFirstOutpostQuest = DefEnableFirstOutpostQuest;
        public bool enableCommonEnemySettlementQuest = DefEnableCommonEnemySettlementQuest;
        public bool enableColonyRoadLinkQuest = DefEnableColonyRoadLinkQuest;
        public bool enableWorldDominationVictoryQuest = DefEnableWorldDominationVictoryQuest;
        public bool enableAtTurretTargetPlayerTravelers = DefEnableAtTurretTargetPlayerTravelers;
        public bool enableAtTurretTargetPlayerCaravans = DefEnableAtTurretTargetPlayerCaravans;
        public bool enableOutpostUpkeep = DefEnableOutpostUpkeep;
        public bool giveFoodOnPrisonerRecruitTransfer = DefGiveFoodOnPrisonerRecruitTransfer;
        public bool giveFoodOnAllPlayerPawnsTransfer = DefGiveFoodOnAllPlayerPawnsTransfer;
        public bool showOutpostRequirementsPreviewInWdMenu = DefShowOutpostRequirementsPreviewInWdMenu;
        public int upkeepSilverPerOccupant = DefUpkeepSilverPerOccupant;
        public int upkeepIntervalDays = DefUpkeepIntervalDays;

        public float garrisonRetainPct = DefGarrisonRetainPct;
        public float dropPodRaidChanceT3 = DefDropPodRaidChanceT3;
        public float dropPodRaidChance = DefDropPodRaidChance;
        public TechLevel dropPodRaidMinTechLevel = DefDropPodRaidMinTechLevel;
        public float dropPodRaidAttritionMult = DefDropPodRaidAttritionMult;
        public float colonySiegeRaidChance = DefColonySiegeRaidChance;

        public float weightSabSuccess = DefWeightSabSuccess;
        public float weightSabCleanFail = DefWeightSabCleanFail;
        public float weightSabInjuredFail = DefWeightSabInjuredFail;
        public float weightSabFatalFail = DefWeightSabFatalFail;
        public float sabotageSkillSuccessWeightBonus = DefSabSkillSuccessWeightBonus;
        public float sabotageTierSuccessWeightPenalty = DefSabTierSuccessWeightPenalty;
        public float sabotageHealthImpactWeight = DefSabHealthImpactWeight;
        public float sabotageSocialCleanBonus = DefSabSocialCleanBonus;
        public float sabotageCombatSurvivalBonus = DefSabCombatSurvivalBonus;
        public float sabotageBaseReduction = DefSabBaseReduc;
        public float sabotageSkillReductionBonus = DefSabSkillReductionBonus;
        public float sabotageCooldownDays = DefSabCdDays;

        public float weightDisSuccess = DefWeightDisSuccess;
        public float weightDisCleanFail = DefWeightDisCleanFail;
        public float weightDisInjuredFail = DefWeightDisInjuredFail;
        public float weightDisFatalFail = DefWeightDisFatalFail;
        public float disSkillSuccessWeightBonus = DefDisSkillSuccessWeightBonus;
        public float disTierSuccessWeightPenalty = DefDisTierSuccessWeightPenalty;
        public float disHealthImpactWeight = DefDisHealthImpactWeight;
        public float disSocialCleanBonus = DefDisSocialCleanBonus;
        public float disCombatSurvivalBonus = DefDisCombatSurvivalBonus;
        public float disBaseReduction = DefDisBaseReduc;
        public float disSkillReductionBonus = DefDisSkillReductionBonus;
        public float disCooldownDays = DefDisCdDays;

        public int outpostMinDistanceTiles = DefOutpostMinDistanceTiles;
        /// <summary>Trader caravan destination search radius (tiles); independent from raid target radius.</summary>
        public float traderDestinationSearchRadius = DefTraderDestinationSearchRadius;
        public float outpostBuildCostMultiplier = DefOutpostBuildCostMultiplier;
        public float outpostDeliveryStrengthCost = DefOutpostDeliveryStrengthCost;
        public float outpostDeliveryMinStrength = DefOutpostDeliveryMinStrength;
        public float outpostSilverValuePerSkillPerCycle = DefOutpostSilverValuePerSkillPerCycle;
        public float outpostProductionTimeMultiplier = DefOutpostProductionTimeMultiplier;
        public float outpostProductionOutputMultiplier = DefOutpostProductionOutputMultiplier;
        public float warehouseAuraBonusPct = DefWarehouseAuraBonusPct;
        public float warehouseAuraRadiusTiles = DefWarehouseAuraRadiusTiles;
        public bool embassyMayGainGoodwillWithHostiles = DefEmbassyMayGainGoodwillWithHostiles;
        public bool clampOutpostSkillsAtLevel20 = DefClampOutpostSkillsAtLevel20;
        public bool enableOutpostSkillDiminishingReturns = OutpostSkillScaling.DefEnableDiminishingReturns;
        public float outpostSkillHardCapRaw = OutpostSkillScaling.DefHardCapRaw;
        public float[] outpostSkillBandEnds = (float[])OutpostSkillScaling.DefBandEnds.Clone();
        public float[] outpostSkillBandWeights = (float[])OutpostSkillScaling.DefBandWeights.Clone();
        public float outpostOccupantSkillXpPerProductionCycle = DefOutpostOccupantSkillXpPerProductionCycle;
        public int outpostOccupantSkillXpMaxLevel = DefOutpostOccupantSkillXpMaxLevel;
        public float academyBaseXpPerDay = DefAcademyBaseXpPerDay;
        public int academyMinTeacherSkill = DefAcademyMinTeacherSkill;
        public int academyTeachCapOffset = DefAcademyTeachCapOffset;
        public bool academyUseFlatDirectXp = DefAcademyUseFlatDirectXp;
        public bool outpostUpgradesCostMaterials = DefOutpostUpgradesCostMaterials;
        public bool outpostUpgradesRequireResearch = DefOutpostUpgradesRequireResearch;
        public bool enableOutpostLaunchAttack = DefEnableOutpostLaunchAttack;
        public bool enableOutpostBuildRoads = DefEnableOutpostBuildRoads;
        public bool enableOutpostBuildRoadBlocks = DefEnableOutpostBuildRoadBlocks;
        public bool enableOutpostBuildTraps = DefEnableOutpostBuildTraps;
        // Establishment requirement toggles
        public bool outpostReqBiome = DefOutpostReqBiome;
        public bool outpostReqFertility = DefOutpostReqFertility;
        public bool outpostReqAnimalAbundance = DefOutpostReqAnimalAbundance;
        public bool outpostReqFishAbundance = DefOutpostReqFishAbundance;
        public bool outpostReqMiningTerrain = DefOutpostReqMiningTerrain;
        public bool outpostReqResearch = DefOutpostReqResearch;
        public bool outpostReqNearbySettlements = DefOutpostReqNearbySettlements;
        public bool outpostReqMinPawns = DefOutpostReqMinPawns;
        public bool outpostReqMinSkill = DefOutpostReqMinSkill;
        public bool outpostReqCost = DefOutpostReqCost;
        public bool pollutionEcologyPenaltyEnabled = DefPollutionEcologyPenaltyEnabled;
        public bool travelerPollutionDamageEnabled = DefTravelerPollutionDamageEnabled;
        public bool wasterPollutionImmunityEnabled = DefWasterPollutionImmunityEnabled;
        public bool pollutionDamageRaiders = DefPollutionDamageRaiders;
        public bool pollutionDamageExpansion = DefPollutionDamageExpansion;
        public bool pollutionDamageConstruction = DefPollutionDamageConstruction;
        public bool pollutionDamageTraders = DefPollutionDamageTraders;
        public bool pollutionDamagePlayerTravelers = DefPollutionDamagePlayerTravelers;
        public bool pollutionPathCostEnabled = DefPollutionPathCostEnabled;
        public bool pollutionPathRepathEnabled = DefPollutionPathRepathEnabled;
        public bool pollutionPathPreCommitCancelEnabled = DefPollutionPathPreCommitCancelEnabled;
        public float pollutionDamageIgnoreBelow = DefPollutionDamageIgnoreBelow;
        public float pollutionDamageAtThreshold = DefPollutionDamageAtThreshold;
        public float pollutionDamageAtFull = DefPollutionDamageAtFull;
        public int pollutionDamageRadius = DefPollutionDamageRadius;
        public float npcSettlementDecontaminationStrengthCost = DefNpcSettlementDecontaminationStrengthCost;
        public float outpostDefensiveRecoveryMinFlatPerDay = DefOutpostDefensiveRecoveryMinFlatPerDay;
        public float outpostDefensiveRecoveryFractionPerDay = DefOutpostDefensiveRecoveryFractionPerDay;
        public float outpostOffensiveRecoveryMinFlatPerDay = DefOutpostOffensiveRecoveryMinFlatPerDay;
        public float outpostOffensiveRecoveryFractionPerDay = DefOutpostOffensiveRecoveryFractionPerDay;
        public float outpostOccupantHealSeverityPerDay = DefOutpostOccupantHealSeverityPerDay;
        public float expertStrategistMaxBonusPct = DefExpertStrategistMaxBonusPct;
        public float expertEntertainerMaxBonusPct = DefExpertEntertainerMaxBonusPct;
        public float expertCookMaxBonusPct = DefExpertCookMaxBonusPct;
        public float expertDoctorMaxBonusPct = DefExpertDoctorMaxBonusPct;
        public float expertEngineerMaxBonusPct = DefExpertEngineerMaxBonusPct;
        public float expertEngineerConstructionRadiusMaxBonusPct = DefExpertEngineerConstructionRadiusMaxBonusPct;
        public float expertRecruiterMaxBonusPct = DefExpertRecruiterMaxBonusPct;
        public int expertReferenceSkillLevel = DefExpertReferenceSkillLevel;
        public float cooldownPlayerOutpostRaidDays = DefCooldownPlayerOutpostRaidDays;
        public bool outpostAfterConquestEnabled = DefOutpostAfterConquestEnabled;
        public int conquestFoundingPawnsT1 = DefConquestFoundingPawnsT1;
        public int conquestFoundingPawnsT2 = DefConquestFoundingPawnsT2;
        public int conquestFoundingPawnsT3 = DefConquestFoundingPawnsT3;
        public int conquestFoundingPawnsT4 = DefConquestFoundingPawnsT4;
        public int conquestFoundingMinRelevantSkill = DefConquestFoundingMinRelevantSkill;
        /// <summary>Per-def override: baseline quantity per Mining skill per cycle (ore/stone). If null or key missing, computed baseline is used; mining baseline dialog uses GetDefaultMiningBaselineForDef as slider default.</summary>
        public Dictionary<string, float> miningBaselineMultiplierByDefName;

        public int GetConquestFoundingPawnCount(SettlementTier conqueredSettlementTier)
        {
            return conqueredSettlementTier switch
            {
                SettlementTier.T4 => Mathf.Max(0, conquestFoundingPawnsT4),
                SettlementTier.T3 => Mathf.Max(0, conquestFoundingPawnsT3),
                SettlementTier.T2 => Mathf.Max(0, conquestFoundingPawnsT2),
                _ => Mathf.Max(0, conquestFoundingPawnsT1)
            };
        }

        public int GetConquestFoundingMinRelevantSkillClamped() =>
            Mathf.Clamp(conquestFoundingMinRelevantSkill, 0, 20);

        public int GetEffectiveOutpostSkillLevel(int level)
        {
            int minClamped = Mathf.Max(0, level);
            return clampOutpostSkillsAtLevel20 ? Mathf.Min(minClamped, 20) : minClamped;
        }

        public int GetSameTierNeighborsRequiredForUpgrade(SettlementTier currentTier)
        {
            return currentTier switch
            {
                SettlementTier.T1 => Mathf.Clamp(sameTierNeighborsToUpgradeT1, 0, 5),
                SettlementTier.T2 => Mathf.Clamp(sameTierNeighborsToUpgradeT2, 0, 5),
                SettlementTier.T3 => Mathf.Clamp(sameTierNeighborsToUpgradeT3, 0, 5),
                _ => int.MaxValue
            };
        }

        public int GetAlliedRaidGoodwillCost(SettlementTier targetTier, bool awardToPlayer)
        {
            return targetTier switch
            {
                SettlementTier.T4 => Mathf.Max(0, awardToPlayer ? alliedRaidAwardCostT4 : alliedRaidClaimCostT4),
                SettlementTier.T3 => Mathf.Max(0, awardToPlayer ? alliedRaidAwardCostT3 : alliedRaidClaimCostT3),
                SettlementTier.T2 => Mathf.Max(0, awardToPlayer ? alliedRaidAwardCostT2 : alliedRaidClaimCostT2),
                _ => Mathf.Max(0, awardToPlayer ? alliedRaidAwardCostT1 : alliedRaidClaimCostT1)
            };
        }

        public float GetSettlementBuyAskSilver(SettlementTier tier)
        {
            return tier switch
            {
                SettlementTier.T2 => Mathf.Max(0f, settlementBuyAskT2),
                SettlementTier.T3 => Mathf.Max(0f, settlementBuyAskT3),
                SettlementTier.T4 => Mathf.Max(0f, settlementBuyAskT4),
                _ => Mathf.Max(0f, settlementBuyAskT1)
            };
        }

        public float GetFactionInvestmentUpgradeSilver(SettlementTier fromTier)
        {
            return fromTier switch
            {
                SettlementTier.T1 => Mathf.Max(0f, factionInvestmentUpgradeT1ToT2Silver),
                SettlementTier.T2 => Mathf.Max(0f, factionInvestmentUpgradeT2ToT3Silver),
                SettlementTier.T3 => Mathf.Max(0f, factionInvestmentUpgradeT3ToT4Silver),
                _ => 0f
            };
        }

        public int GetConquestAllyGiftGoodwill(SettlementTier conqueredTier)
        {
            return conqueredTier switch
            {
                SettlementTier.T4 => Mathf.Max(0, conquestAllyGiftGoodwillT4),
                SettlementTier.T3 => Mathf.Max(0, conquestAllyGiftGoodwillT3),
                SettlementTier.T2 => Mathf.Max(0, conquestAllyGiftGoodwillT2),
                _ => Mathf.Max(0, conquestAllyGiftGoodwillT1)
            };
        }

        public float GetOrderedRoadPerSegmentRate(SettlementTier roadTier)
        {
            if (roadTier >= SettlementTier.T3) return Mathf.Max(0f, orderedRoadPerSegmentT3);
            if (roadTier == SettlementTier.T2) return Mathf.Max(0f, orderedRoadPerSegmentT2);
            return Mathf.Max(0f, orderedRoadPerSegmentT1);
        }

        public static int CalcOrderedRoadPayCost(float perSegmentRate, int segmentCount)
        {
            return Mathf.Max(0, Mathf.CeilToInt(perSegmentRate * Mathf.Max(0, segmentCount)));
        }

        public static int CalcOrderedRoadRefund(float perSegmentRate, int remainingSegments)
        {
            return Mathf.Max(0, Mathf.FloorToInt(perSegmentRate * Mathf.Max(0, remainingSegments)));
        }

        public void GetOrderedRoadGoodwillCostBreakdown(
            SettlementTier roadTier,
            int segmentCount,
            out float perSegmentRate,
            out int totalCost)
        {
            perSegmentRate = GetOrderedRoadPerSegmentRate(roadTier);
            totalCost = CalcOrderedRoadPayCost(perSegmentRate, segmentCount);
        }

        public bool foodLogisticsActive = DefFoodLogisticsActive;
        public float foodConsumptionPerPawn = DefFoodConsumptionPerPawn;
        public float foodProductionPerSkill = DefFoodProductionPerSkill;
        public float foodProductionPerOutpostBase = DefFoodProductionPerOutpostBase;
        public float maxFoodPerOutpost = DefMaxFoodPerOutpost;
        public int maxLogisticsRange = DefMaxLogisticsRange;
        public float virtualFoodTileMultiplierFloor = DefVirtualFoodTileMultiplierFloor;

        public bool notifyNewSettlement = DefNotifyNewSettlement;
        public bool notifyNpcConquestSettlement = DefNotifyNpcConquestSettlement;
        public bool notifySettlementRaided = DefNotifySettlementRaided;
        public bool notifySettlementRazed = DefNotifySettlementRazed;
        public bool notifyOutpostDestroyed = DefNotifyOutpostDestroyed;
        public bool notifyThreatLevel = DefNotifyThreatLevel;
        public bool notifyCriticalFood = DefNotifyCriticalFood;
        public bool notifyDropPodDeliveryInAaRange = DefNotifyDropPodDeliveryInAaRange;
        public bool notifyOutpostUpkeep = DefNotifyOutpostUpkeep;
        public bool notifyConstructionInsufficientStrength = DefNotifyConstructionInsufficientStrength;
        public bool notifyOutpostNoProduction = DefNotifyOutpostNoProduction;
        public bool notifyOutpostUnusedExperts = DefNotifyOutpostUnusedExperts;
        public bool notifyLateGameActive = DefNotifyLateGameActive;
        public bool notifyMidGameActive = DefNotifyMidGameActive;
        public bool notifyLeaderHandicap = DefNotifyLeaderHandicap;
        public bool notifyUnderdogBuff = DefNotifyUnderdogBuff;
        public bool notifyExpansionistZeal = DefNotifyExpansionistZeal;
        public bool notifyAntiLeaderCoalition = DefNotifyAntiLeaderCoalition;
        public bool notifyRandomDiplomacy = DefNotifyRandomDiplomacy;
        public bool notifyTradeAllyDiplomacy = DefNotifyTradeAllyDiplomacy;
        public bool notifyStrongFactionWar = DefNotifyStrongFactionWar;
        /// <summary>Legacy; kept forever for save migration and approximate compatibility if downgrading the mod.</summary>
        public bool notifyDiplomaticChange = DefNotifyDiplomaticChange;
        /// <summary>Legacy; kept forever for save migration and approximate compatibility if downgrading the mod.</summary>
        public bool notifyBuffNerf = DefNotifyBuffNerf;
        public int settingsDataVersion;
        // SURGICAL: Notification Variables
        public bool notifyIncomingRaidColony = DefNotifyIncomingRaidColony;
        public bool notifyIncomingRaidOutpost = DefNotifyIncomingRaidOutpost;
        public bool notifyRaidDivertedFromPlayer = DefNotifyRaidDivertedFromPlayer;
        public bool notifyMortarHit = DefNotifyMortarHit;
        public bool notifyAntiAirHit = DefNotifyAntiAirHit;
        public bool notifyPlayerAntiAirVsHostileMortarShell = DefNotifyPlayerAntiAirVsHostileMortarShell;
        public bool notifyNpcMortarHitPlayer = DefNotifyNpcMortarHitPlayer;
        public bool notifyNpcMortarHitNpc = DefNotifyNpcMortarHitNpc;
        public bool notifyPlayerAtTurretKilledTarget = DefNotifyPlayerAtTurretKilledTarget;
        public bool notifyPlayerAtTurretDamagedTarget = DefNotifyPlayerAtTurretDamagedTarget;
        public bool notifyPlayerAtTurretDestroyed = DefNotifyPlayerAtTurretDestroyed;
        public bool notifyNpcAtTurretDamagedPlayer = DefNotifyNpcAtTurretDamagedPlayer;
        public bool notifyNpcAtTurretKilledPlayer = DefNotifyNpcAtTurretKilledPlayer;
        public bool notifyWarehouseGoodsArrived = DefNotifyWarehouseGoodsArrived;
        public bool notifyOutpostDeliveryToColonyArrived = DefNotifyOutpostDeliveryToColonyArrived;
        public bool notifyPlayerCaravanClash = DefNotifyPlayerCaravanClash;
        public bool showCaravanClashLootDialog = DefShowCaravanClashLootDialog;
        public bool notifyRapidResponseCaravanClash = DefNotifyRapidResponseCaravanClash;
        public bool notifyTravelerPollutionDamage = DefNotifyTravelerPollutionDamage;
        public bool notifyOutpostPollutionDamage = DefNotifyOutpostPollutionDamage;
        public bool notifyPrisonerRecruitedUnderway = DefNotifyPrisonerRecruitedUnderway;
        public bool alwaysShowOutpostTravelerIconsRegardlessOfZoom = DefAlwaysShowOutpostTravelerIconsRegardlessOfZoom;
        public bool alwaysShowSettlementIconsRegardlessOfZoom = DefAlwaysShowSettlementIconsRegardlessOfZoom;

        public float genWeightT1 = DefGenWeightT1;
        public float genWeightT2 = DefGenWeightT2;
        public float genWeightT3 = DefGenWeightT3;
        public float genWeightT4 = DefGenWeightT4;
        public float settlementTerritoryCoherence = DefSettlementTerritoryCoherence;
        public float settlementTerritorySpacing = DefSettlementTerritorySpacing;
        public float settlementOtherFactionDistance = DefSettlementOtherFactionDistance;
        public int settlementMaxPerCluster = DefSettlementMaxPerCluster;
        public int settlementMinDistanceBetweenClusters = DefSettlementMinDistanceBetweenClusters;
        public bool worldSetupDestroyFortificationsOnRecreate = DefWorldSetupDestroyFortificationsOnRecreate;

        public bool allowWdSettlementBaseGeneration = DefAllowWdSettlementBaseGeneration;
        public float kcsgMultTribalT1 = DefKcsgMultTribalT1;
        public float kcsgMultTribalT2 = DefKcsgMultTribalT2;
        public float kcsgMultTribalT3 = DefKcsgMultTribalT3;
        public float kcsgMultTribalT4 = DefKcsgMultTribalT4;
        public float kcsgMultGenericT1 = DefKcsgMultGenericT1;
        public float kcsgMultGenericT2 = DefKcsgMultGenericT2;
        public float kcsgMultGenericT3 = DefKcsgMultGenericT3;
        public float kcsgMultGenericT4 = DefKcsgMultGenericT4;
        public float garrisonOffensiveStrengthMinScale = DefGarrisonOffensiveStrengthMinScale;
        public bool kcsgAdaptiveTerrainPrep = DefKcsgAdaptiveTerrainPrep;
        public float kcsgBlockedFlattenThreshold = DefKcsgBlockedFlattenThreshold;
        public bool experimentalAlwaysClearKcsgRect = DefExperimentalAlwaysClearKcsgRect;
        public bool experimentalKcsgRectBlend = DefExperimentalKcsgRectBlend;

        public bool noGoodwillFromHostilesOnConquest = DefNoGoodwillFromHostilesOnConquest;
        public bool disableSettlementProximityGoodwill = DefDisableSettlementProximityGoodwill;
        public bool blockStorytellerRaidsOnlyWD = DefBlockStorytellerRaidsOnlyWD;
        public bool allowStorytellerRaidsFromNonWdFactions = DefAllowStorytellerRaidsFromNonWdFactions;
        public bool blockStorytellerTradersOnlyWD = DefBlockStorytellerTradersOnlyWD;
        public float launchPodGiftStrengthPer100MarketValue = DefLaunchPodGiftStrengthPer100MarketValue;
        public bool enableFactionSettlementInvestment = DefEnableFactionSettlementInvestment;
        public float factionInvestmentStrengthPer100Silver = DefFactionInvestmentStrengthPer100Silver;
        public int factionInvestmentRadiusTiles = DefFactionInvestmentRadiusTiles;
        public float factionInvestmentUpgradeT1ToT2Silver = DefFactionInvestmentUpgradeT1ToT2Silver;
        public float factionInvestmentUpgradeT2ToT3Silver = DefFactionInvestmentUpgradeT2ToT3Silver;
        public float factionInvestmentUpgradeT3ToT4Silver = DefFactionInvestmentUpgradeT3ToT4Silver;
        public float factionInvestmentUpgradeSuccessChance = DefFactionInvestmentUpgradeSuccessChance;

        public bool goodwillFromTradeEnabled = DefGoodwillFromTradeEnabled;
        public float goodwillFromTradePer1000Silver = DefGoodwillFromTradePer1000Silver;
        public int maxGoodwill = DefMaxGoodwill;
        public float traderCaravanCostStrength = DefTraderCaravanCostStrength;
        public float traderCaravanSenderRewardStrength = DefTraderCaravanSenderRewardStrength;
        public float traderCaravanReceiverRewardStrength = DefTraderCaravanReceiverRewardStrength;
        public float traderCaravanGoodwillGain = DefTraderCaravanGoodwillGain;
        public float cooldownPlayerColonyTraderDays = DefCooldownPlayerColonyTraderDays;

        public float traderTierUpgradeChanceT1ToT2 = DefTraderTierUpgradeChanceT1ToT2;
        public float traderTierUpgradeChanceT2ToT3 = DefTraderTierUpgradeChanceT2ToT3;
        public float traderTierUpgradeChanceT3ToT4 = DefTraderTierUpgradeChanceT3ToT4;

        public float traderEscortFloorT1 = DefTraderEscortFloorT1;
        public float traderEscortFloorT2 = DefTraderEscortFloorT2;
        public float traderEscortFloorT3 = DefTraderEscortFloorT3;
        public float traderEscortFloorT4 = DefTraderEscortFloorT4;
        public float traderEscortRecentInterceptWindowDays = DefTraderEscortRecentInterceptWindowDays;

        // --- MORTAR OUTPOST / INTERCEPTION VARIABLES ---
        public float mortarRange = DefMortarRange;
        public float cooldownMortarDays = DefCooldownMortarDays;
        public float mortarBaseMissChanceAtMaxRange = DefMortarBaseMissChanceAtMaxRange;
        public float mortarHitPerSkillPoint = DefMortarHitPerSkillPoint;
        public float mortarHitChance0To50PctRange = DefMortarHitChance0To50PctRange;
        public float mortarHitChance51To75PctRange = DefMortarHitChance51To75PctRange;
        public float mortarHitChance76To100PctRange = DefMortarHitChance76To100PctRange;
        public float npcMortarDamage = DefNpcMortarDamage;
        public float npcMortarSkillEquivalent = DefNpcMortarSkillEquivalent;
        public bool enableNpcT4Mortar = DefEnableNpcT4Mortar;
        public TechLevel npcT4MortarMinTechLevel = DefNpcT4MortarMinTechLevel;
        public float npcMortarRange = DefNpcMortarRange;
        public float npcMortarCooldownDays = DefNpcMortarCooldownDays;
        public float npcMortarHitChance0To50PctRange = DefNpcMortarHitChance0To50PctRange;
        public float npcMortarHitChance51To75PctRange = DefNpcMortarHitChance51To75PctRange;
        public float npcMortarHitChance76To100PctRange = DefNpcMortarHitChance76To100PctRange;
        public int interceptionScanIntervalTicks = DefInterceptionScanIntervalTicks;
        public float mortarDamagePerSkillPoint = DefMortarDamagePerSkillPoint;
        public float mortarBaseShellDamage = DefMortarBaseShellDamage;
        public float mortarShellTicksPerMove = DefMortarShellTicksPerMove;
        public float antiAirBaseDamage = DefAntiAirBaseDamage;
        public float cooldownAntiAirSeconds = DefCooldownAntiAirSeconds;
        public float antiAirCooldownFloorSeconds = DefAntiAirCooldownFloorSeconds;
        public float antiAirRange = DefAntiAirRange;
        public float antiAirHitChance0To50PctRange = DefAntiAirHitChance0To50PctRange;
        public float antiAirHitChance51To75PctRange = DefAntiAirHitChance51To75PctRange;
        public float antiAirHitChance76To100PctRange = DefAntiAirHitChance76To100PctRange;
        public float antiAirVsMortarHitChance = DefAntiAirVsMortarHitChance;
        public float flakShellTicksPerMove = DefFlakShellTicksPerMove;
        public bool enableNpcT4AntiAir = DefEnableNpcT4AntiAir;
        public bool enableT4SettlementAntiAir = DefEnableT4SettlementAntiAir;
        public float npcAntiAirRange = DefNpcAntiAirRange;
        public float npcAntiAirCooldownSeconds = DefNpcAntiAirCooldownSeconds;
        public float npcAntiAirDamage = DefNpcAntiAirDamage;
        public float npcAntiAirSkillEquivalent = DefNpcAntiAirSkillEquivalent;
        public float npcAntiAirHitChance0To50PctRange = DefNpcAntiAirHitChance0To50PctRange;
        public float npcAntiAirHitChance51To75PctRange = DefNpcAntiAirHitChance51To75PctRange;
        public float npcAntiAirHitChance76To100PctRange = DefNpcAntiAirHitChance76To100PctRange;
        public float npcAntiAirVsMortarHitChance = DefNpcAntiAirVsMortarHitChance;
        public bool notifyT4AntiAirHitPlayer = DefNotifyT4AntiAirHitPlayer;
        public bool notifyPlayerMortarShellShotDown = DefNotifyPlayerMortarShellShotDown;
        public float rapidResponseOffensiveStrengthBonus = DefRapidResponseOffensiveStrengthBonus;
        public float rapidResponseOffensiveRecoveryBonus = DefRapidResponseOffensiveRecoveryBonus;
        public float rapidResponseTicksPerMoveMultiplier = DefRapidResponseTicksPerMoveMultiplier;
        public float rapidResponseAutoInterceptRange = DefRapidResponseAutoInterceptRange;
        public float rapidResponseDropPodRange = DefRapidResponseDropPodRange;
        public float dropPodTicksPerMove = DefDropPodTicksPerMove;

        /// <summary>When true, main settings window shows advanced hub rows and in-dialog advanced sections.</summary>
        public bool showAdvancedSettings = false;

        // --- Meta / UX state (last seen WD version for simple update popup) ---
        public bool showUpdatePopups = DefShowUpdatePopups;
        public bool verboseLogging = DefVerboseLogging;
        /// <summary>Hold this key + 1–7 on the world map to toggle WD overlays.</summary>
        public KeyCode worldMapOverlayHoldKey = DefWorldMapOverlayHoldKey;
        public string lastSeenReleaseNotesVersion = string.Empty;

        /// <summary>Setting presets → Performance pack (last applied named preset). Apply only overwrites this pack's fields.</summary>
        public WDSettingsPerformancePreset performancePreset = DefPerformancePreset;
        /// <summary>Setting presets → Difficulty pack (last applied named preset). Apply only overwrites this pack's fields.</summary>
        public WDSettingsDifficultyPreset difficultyPreset = DefDifficultyPreset;

        public float TotalWeight => weightGrow + weightRaid + weightMinorIncident + weightMajorIncident + weightBuildRoad + weightTrader + weightFortify;

        /// <summary>Settings-menu % pool: usual actions, optionally plus Develop (near-cap only in gameplay).</summary>
        public float WeightPercentDisplayPool =>
            weightRaid + weightMinorIncident + weightMajorIncident + weightBuildRoad + weightTrader + weightFortify
            + (includeDevelopWeightInPercentDisplay ? weightGrow : 0f);
        public float TotalSabWeight => weightSabSuccess + weightSabCleanFail + weightSabInjuredFail + weightSabFatalFail;
        public float TotalDisWeight => weightDisSuccess + weightDisCleanFail + weightDisInjuredFail + weightDisFatalFail;
        public float TotalGenWeight => genWeightT1 + genWeightT2 + genWeightT3 + genWeightT4;

        public float GetFallbackRoadMovement(SettlementTier roadTier)
        {
            if (roadTier == SettlementTier.T3 || roadTier == SettlementTier.T4)
                return Mathf.Clamp(fallbackAsphaltRoadMovement, 0.05f, 2f);
            if (roadTier == SettlementTier.T2)
                return Mathf.Clamp(fallbackStoneRoadMovement, 0.05f, 2f);
            return Mathf.Clamp(fallbackDirtRoadMovement, 0.05f, 2f);
        }

        public float GetFallbackRoadWinterReduction(SettlementTier roadTier)
        {
            if (roadTier == SettlementTier.T3 || roadTier == SettlementTier.T4)
                return Mathf.Clamp01(fallbackAsphaltRoadWinterReduction);
            if (roadTier == SettlementTier.T2)
                return Mathf.Clamp01(fallbackStoneRoadWinterReduction);
            return Mathf.Clamp01(fallbackDirtRoadWinterReduction);
        }

        public float GetFallbackRoadWork(SettlementTier roadTier)
        {
            if (roadTier == SettlementTier.T3 || roadTier == SettlementTier.T4)
                return Mathf.Max(1f, fallbackAsphaltRoadWork);
            if (roadTier == SettlementTier.T2)
                return Mathf.Max(1f, fallbackStoneRoadWork);
            return Mathf.Max(1f, fallbackDirtRoadWork);
        }

        public float GetFallbackRoadExpeditionStrength(SettlementTier roadTier)
        {
            if (roadTier == SettlementTier.T3 || roadTier == SettlementTier.T4)
                return Mathf.Max(1f, fallbackAsphaltRoadExpeditionStrength);
            if (roadTier == SettlementTier.T2)
                return Mathf.Max(1f, fallbackStoneRoadExpeditionStrength);
            return Mathf.Max(1f, fallbackDirtRoadExpeditionStrength);
        }

        public int GetFallbackRoadMinConstruction(SettlementTier roadTier)
        {
            if (roadTier == SettlementTier.T3 || roadTier == SettlementTier.T4)
                return Mathf.Max(0, fallbackAsphaltRoadMinConstruction);
            if (roadTier == SettlementTier.T2)
                return Mathf.Max(0, fallbackStoneRoadMinConstruction);
            return Mathf.Max(0, fallbackDirtRoadMinConstruction);
        }

        public float GetRoadBlockWork(RoadBlockKind kind)
        {
            switch (kind)
            {
                case RoadBlockKind.Heavy: return Mathf.Max(1f, roadBlockHeavyWork);
                case RoadBlockKind.Light: return Mathf.Max(1f, roadBlockLightWork);
                default: return Mathf.Max(1f, roadBlockNormalWork);
            }
        }

        public float GetRoadBlockExpeditionStrength(RoadBlockKind kind)
        {
            switch (kind)
            {
                case RoadBlockKind.Heavy: return Mathf.Max(1f, roadBlockHeavyExpeditionStrength);
                case RoadBlockKind.Light: return Mathf.Max(1f, roadBlockLightExpeditionStrength);
                default: return Mathf.Max(1f, roadBlockNormalExpeditionStrength);
            }
        }

        public float GetRoadBlockFlatPenalty(RoadBlockKind kind)
        {
            switch (kind)
            {
                case RoadBlockKind.Heavy: return Mathf.Max(0f, roadBlockHeavyFlatPenalty);
                case RoadBlockKind.Light: return Mathf.Max(0f, roadBlockLightFlatPenalty);
                case RoadBlockKind.Gate: return Mathf.Max(0f, roadBlockNormalFlatPenalty);
                default: return Mathf.Max(0f, roadBlockNormalFlatPenalty);
            }
        }

        public float GetRoadBlockMaxHealth(RoadBlockKind kind)
        {
            switch (kind)
            {
                case RoadBlockKind.Heavy: return Mathf.Max(1f, roadBlockHeavyMaxHealth);
                case RoadBlockKind.Light: return Mathf.Max(1f, roadBlockLightMaxHealth);
                default: return Mathf.Max(1f, roadBlockNormalMaxHealth);
            }
        }

        public float GetSpikeTrapWork(SpikeTrapKind kind)
        {
            return kind == SpikeTrapKind.Caltrops
                ? Mathf.Max(1f, spikeTrapCaltropsWork)
                : Mathf.Max(1f, spikeTrapSpikeWork);
        }

        public float GetSpikeTrapExpeditionStrength(SpikeTrapKind kind)
        {
            return kind == SpikeTrapKind.Caltrops
                ? Mathf.Max(1f, spikeTrapCaltropsExpeditionStrength)
                : Mathf.Max(1f, spikeTrapSpikeExpeditionStrength);
        }

        public float GetSpikeTrapDamage(SpikeTrapKind kind)
        {
            return kind == SpikeTrapKind.Caltrops
                ? Mathf.Max(0f, spikeTrapCaltropsDamage)
                : Mathf.Max(0f, spikeTrapSpikeDamage);
        }

        public float GetSpikeTrapMaxHealth(SpikeTrapKind kind)
        {
            return kind == SpikeTrapKind.Caltrops
                ? Mathf.Max(1f, spikeTrapCaltropsMaxHealth)
                : Mathf.Max(1f, spikeTrapSpikeMaxHealth);
        }

        public float GetAtTurretWork(AtTurretTier tier)
        {
            switch (tier)
            {
                case AtTurretTier.Light: return Mathf.Max(1f, atTurretLightWork);
                case AtTurretTier.Heavy: return Mathf.Max(1f, atTurretHeavyWork);
                default: return Mathf.Max(1f, atTurretMediumWork);
            }
        }

        public int GetAtTurretMinConstruction(AtTurretTier tier)
        {
            switch (tier)
            {
                case AtTurretTier.Light: return Mathf.Max(0, atTurretLightMinConstruction);
                case AtTurretTier.Heavy: return Mathf.Max(0, atTurretHeavyMinConstruction);
                default: return Mathf.Max(0, atTurretMediumMinConstruction);
            }
        }

        public float GetAtTurretExpeditionStrength(AtTurretTier tier)
        {
            switch (tier)
            {
                case AtTurretTier.Light: return Mathf.Max(1f, atTurretLightExpeditionStrength);
                case AtTurretTier.Heavy: return Mathf.Max(1f, atTurretHeavyExpeditionStrength);
                default: return Mathf.Max(1f, atTurretMediumExpeditionStrength);
            }
        }

        public static float GetAtTurretMaxStrengthDefault(AtTurretTier tier)
        {
            switch (tier)
            {
                case AtTurretTier.Light: return DefAtTurretLightMaxStrength;
                case AtTurretTier.Heavy: return DefAtTurretHeavyMaxStrength;
                default: return DefAtTurretMediumMaxStrength;
            }
        }

        public static float GetAtTurretDamageDefault(AtTurretTier tier)
        {
            switch (tier)
            {
                case AtTurretTier.Light: return DefAtTurretLightDamage;
                case AtTurretTier.Heavy: return DefAtTurretHeavyDamage;
                default: return DefAtTurretDamage;
            }
        }

        public float GetAtTurretMaxStrength(AtTurretTier tier)
        {
            switch (tier)
            {
                case AtTurretTier.Light: return Mathf.Max(1f, atTurretLightMaxStrength);
                case AtTurretTier.Heavy: return Mathf.Max(1f, atTurretHeavyMaxStrength);
                default: return Mathf.Max(1f, atTurretMediumMaxStrength);
            }
        }

        public float GetAtTurretDamage(AtTurretTier tier)
        {
            switch (tier)
            {
                case AtTurretTier.Light: return Mathf.Max(1f, atTurretLightDamage);
                case AtTurretTier.Heavy: return Mathf.Max(1f, atTurretHeavyDamage);
                default: return Mathf.Max(1f, atTurretDamage);
            }
        }

        public float GetAtTurretCooldownDays(AtTurretTier tier)
        {
            switch (tier)
            {
                case AtTurretTier.Light: return Mathf.Max(0f, atTurretLightCooldownDays);
                case AtTurretTier.Heavy: return Mathf.Max(0f, atTurretHeavyCooldownDays);
                default: return Mathf.Max(0f, atTurretCooldownDays);
            }
        }

        public float GetAtTurretRange(AtTurretTier tier)
        {
            switch (tier)
            {
                case AtTurretTier.Light: return Mathf.Max(1f, atTurretLightRange);
                case AtTurretTier.Heavy: return Mathf.Max(1f, atTurretHeavyRange);
                default: return Mathf.Max(1f, atTurretMediumRange);
            }
        }

        public float GetDecontaminationWork() => Mathf.Max(1f, decontaminationWork);
        public float GetDecontaminationExpeditionStrength() => Mathf.Max(1f, decontaminationExpeditionStrength);
        public float GetDecontaminationPollutionReduction() =>
            Mathf.Clamp01(Mathf.Max(0f, decontaminationPollutionReductionPp) * 0.01f);

        /// <summary>Flat traveler strength loss for leaving a tile with pollution in 0..1. Returns 0 below ignore threshold.</summary>
        public float GetPollutionExitDamage(float pollution01)
        {
            float p = Mathf.Clamp01(pollution01);
            float threshold = Mathf.Clamp(pollutionDamageIgnoreBelow, 0f, 0.999f);
            if (p < threshold) return 0f;
            float t = (p - threshold) / (1f - threshold);
            float dmg = Mathf.Lerp(pollutionDamageAtThreshold, pollutionDamageAtFull, t);
            return Mathf.Max(0f, dmg);
        }

        /// <summary>Enforce Dirt≤Stone≤Asphalt and Light≤Normal≤Heavy / Spike≤Caltrops cascades.</summary>
        public void ClampRoadBuildingCascades()
        {
            // Roads: work & strength
            if (fallbackStoneRoadWork < fallbackDirtRoadWork)
                fallbackStoneRoadWork = fallbackDirtRoadWork;
            if (fallbackAsphaltRoadWork < fallbackStoneRoadWork)
                fallbackAsphaltRoadWork = fallbackStoneRoadWork;
            if (fallbackStoneRoadExpeditionStrength < fallbackDirtRoadExpeditionStrength)
                fallbackStoneRoadExpeditionStrength = fallbackDirtRoadExpeditionStrength;
            if (fallbackAsphaltRoadExpeditionStrength < fallbackStoneRoadExpeditionStrength)
                fallbackAsphaltRoadExpeditionStrength = fallbackStoneRoadExpeditionStrength;

            // Road blocks: work, strength, penalty, HP
            if (roadBlockNormalWork < roadBlockLightWork)
                roadBlockNormalWork = roadBlockLightWork;
            if (roadBlockHeavyWork < roadBlockNormalWork)
                roadBlockHeavyWork = roadBlockNormalWork;
            if (roadBlockNormalExpeditionStrength < roadBlockLightExpeditionStrength)
                roadBlockNormalExpeditionStrength = roadBlockLightExpeditionStrength;
            if (roadBlockHeavyExpeditionStrength < roadBlockNormalExpeditionStrength)
                roadBlockHeavyExpeditionStrength = roadBlockNormalExpeditionStrength;
            if (roadBlockNormalFlatPenalty < roadBlockLightFlatPenalty)
                roadBlockNormalFlatPenalty = roadBlockLightFlatPenalty;
            if (roadBlockHeavyFlatPenalty < roadBlockNormalFlatPenalty)
                roadBlockHeavyFlatPenalty = roadBlockNormalFlatPenalty;
            if (roadBlockNormalMaxHealth < roadBlockLightMaxHealth)
                roadBlockNormalMaxHealth = roadBlockLightMaxHealth;
            if (roadBlockHeavyMaxHealth < roadBlockNormalMaxHealth)
                roadBlockHeavyMaxHealth = roadBlockNormalMaxHealth;

            // Spike traps
            if (spikeTrapCaltropsWork < spikeTrapSpikeWork)
                spikeTrapCaltropsWork = spikeTrapSpikeWork;
            if (spikeTrapCaltropsExpeditionStrength < spikeTrapSpikeExpeditionStrength)
                spikeTrapCaltropsExpeditionStrength = spikeTrapSpikeExpeditionStrength;
            if (spikeTrapCaltropsDamage < spikeTrapSpikeDamage)
                spikeTrapCaltropsDamage = spikeTrapSpikeDamage;
            if (spikeTrapCaltropsMaxHealth < spikeTrapSpikeMaxHealth)
                spikeTrapCaltropsMaxHealth = spikeTrapSpikeMaxHealth;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref showAdvancedSettings, "showAdvancedSettings", DefShowAdvancedSettings);
            Scribe_Values.Look(ref performancePreset, "performancePreset", DefPerformancePreset);
            Scribe_Values.Look(ref difficultyPreset, "difficultyPreset", DefDifficultyPreset);
            Scribe_Values.Look(ref tier1Share, "tier1Share", DefTier1Share);
            Scribe_Values.Look(ref tier2Share, "tier2Share", DefTier2Share);
            Scribe_Values.Look(ref tier3Share, "tier3Share", DefTier3Share);
            Scribe_Values.Look(ref tier4Share, "tier4Share", DefTier4Share);

            // SURGICAL: Expose Action Caps
            Scribe_Values.Look(ref tier1MaxActions, "tier1MaxActions", DefCapT1);
            Scribe_Values.Look(ref tier2MaxActions, "tier2MaxActions", DefCapT2);
            Scribe_Values.Look(ref tier3MaxActions, "tier3MaxActions", DefCapT3);
            Scribe_Values.Look(ref tier4MaxActions, "tier4MaxActions", DefCapT4);

            Scribe_Values.Look(ref weightGrow, "weightGrow", DefWeightGrow);
            Scribe_Values.Look(ref weightRaid, "weightRaid", DefWeightRaid);
            Scribe_Values.Look(ref weightMinorIncident, "weightMinorIncident", DefWeightMinorIncident);
            Scribe_Values.Look(ref weightMajorIncident, "weightMajorIncident", DefWeightMajorIncident);
            Scribe_Values.Look(ref weightBuildRoad, "weightBuildRoad", DefWeightBuildRoad);
            Scribe_Values.Look(ref weightTrader, "weightTrader", DefWeightTrader);
            Scribe_Values.Look(ref weightFortify, "weightFortify", DefWeightFortify);
            Scribe_Values.Look(ref includeDevelopWeightInPercentDisplay, "includeDevelopWeightInPercentDisplay", DefIncludeDevelopWeightInPercentDisplay);
            Scribe_Values.Look(ref cooldownGrowDays, "cooldownGrowDays", DefCdGrowDays);
            Scribe_Values.Look(ref cooldownExpandDays, "cooldownExpandDays", DefCdExpandDays);
            Scribe_Values.Look(ref cooldownRaidDays, "cooldownRaidDays", DefCdRaidDays);
            Scribe_Values.Look(ref cooldownBeingRaidedDays, "cooldownBeingRaidedDays", DefCdBeingRaidedDays);
            Scribe_Values.Look(ref cooldownIncidentDays, "cooldownIncidentDays", DefCdIncidentDays);
            Scribe_Values.Look(ref cooldownTraderDays, "cooldownTraderDays", DefCdTraderDays);
            Scribe_Values.Look(ref cooldownFortifyDays, "cooldownFortifyDays", DefCdFortifyDays);
            Scribe_Values.Look(ref fortifyMinTilesFromSelf, "fortifyMinTilesFromSelf", DefFortifyMinTilesFromSelf);
            Scribe_Values.Look(ref fortifyMinTilesFromOtherSettlement, "fortifyMinTilesFromOtherSettlement", DefFortifyMinTilesFromOtherSettlement);
            Scribe_Values.Look(ref fortifyMaxTilesFromSelf, "fortifyMaxTilesFromSelf", DefFortifyMaxTilesFromSelf);
            Scribe_Values.Look(ref fortifyMaxTravelTiles, "fortifyMaxTravelTiles", DefFortifyMaxTravelTiles);
            Scribe_Values.Look(ref fortifyTerritoryLinkMaxTiles, "fortifyTerritoryLinkMaxTiles", DefFortifyTerritoryLinkMaxTiles);
            Scribe_Values.Look(ref fortifyFrontierEps, "fortifyFrontierEps", DefFortifyFrontierEps);
            Scribe_Values.Look(ref fortifyTravelerStrength, "fortifyTravelerStrength", DefFortifyTravelerStrength);
            Scribe_Values.Look(ref fortifyClearOnBuilderLoss, "fortifyClearOnBuilderLoss", DefFortifyClearOnBuilderLoss);
            Scribe_Values.Look(ref enableFortifyBlacklist, "enableFortifyBlacklist", DefEnableFortifyBlacklist);
            Scribe_Values.Look(ref fortifyBlacklistApplyToNeutral, "fortifyBlacklistApplyToNeutral", DefFortifyBlacklistApplyToNeutral);
            Scribe_Values.Look(ref fortifyChanceRoadBlock, "fortifyChanceRoadBlock", DefFortifyChanceRoadBlock);
            Scribe_Values.Look(ref fortifyChanceTrap, "fortifyChanceTrap", DefFortifyChanceTrap);
            Scribe_Values.Look(ref fortifyChanceTurret, "fortifyChanceTurret", DefFortifyChanceTurret);
            Scribe_Values.Look(ref fortifyMultiT1ChanceOf2, "fortifyMultiT1ChanceOf2", DefFortifyMultiT1ChanceOf2);
            Scribe_Values.Look(ref fortifyMultiT2ChanceOf2, "fortifyMultiT2ChanceOf2", DefFortifyMultiT2ChanceOf2);
            Scribe_Values.Look(ref fortifyMultiT3ChanceOf2, "fortifyMultiT3ChanceOf2", DefFortifyMultiT3ChanceOf2);
            Scribe_Values.Look(ref fortifyMultiT4ChanceOf3, "fortifyMultiT4ChanceOf3", DefFortifyMultiT4ChanceOf3);
            Scribe_Values.Look(ref atTurretMaxT1, "atTurretMaxT1", DefAtTurretMaxT1);
            Scribe_Values.Look(ref atTurretMaxT2, "atTurretMaxT2", DefAtTurretMaxT2);
            Scribe_Values.Look(ref atTurretMaxT3, "atTurretMaxT3", DefAtTurretMaxT3);
            Scribe_Values.Look(ref atTurretMaxT4, "atTurretMaxT4", DefAtTurretMaxT4);
            Scribe_Values.Look(ref atTurretPlayerGlobalMax, "atTurretPlayerGlobalMax", DefAtTurretPlayerGlobalMax);
            Scribe_Values.Look(ref atTurretPlayerPerSiteMax, "atTurretPlayerPerSiteMax", DefAtTurretPlayerPerSiteMax);
            Scribe_Values.Look(ref autoAddPawnsOnArrivalDefault, "autoAddPawnsOnArrivalDefault", DefAutoAddPawnsOnArrivalDefault);

            Scribe_Values.Look(ref maxSettlements, "maxSettlements", DefMaxSettlements);
            Scribe_Values.Look(ref passiveGrowthT1, "passiveGrowthT1", DefPassiveGrowthT1);
            Scribe_Values.Look(ref passiveGrowthT2, "passiveGrowthT2", DefPassiveGrowthT2);
            Scribe_Values.Look(ref passiveGrowthT3, "passiveGrowthT3", DefPassiveGrowthT3);
            Scribe_Values.Look(ref passiveGrowthT4, "passiveGrowthT4", DefPassiveGrowthT4);
            Scribe_Values.Look(ref baseGrowthAmount, "baseGrowthAmount", DefBaseGrowth);
            Scribe_Values.Look(ref growthScalingIntensity, "growthScalingIntensity", DefGrowthScaling);
            Scribe_Values.Look(ref expandMinRadius, "expandMinRadius", DefExpandMinRad);
            Scribe_Values.Look(ref expandMaxRadius, "expandMaxRadius", DefExpandMaxRad);
            Scribe_Values.Look(ref maxRoadRange, "maxRoadRange", DefMaxRoadRange);
            Scribe_Values.Look(ref maxRoadRangeNpc, "maxRoadRangeNpc", DefMaxRoadRangeNpc);
            Scribe_Values.Look(ref maxRoadBlockRange, "maxRoadBlockRange", DefMaxRoadBlockRange);

            // Per-tier road blocks (migrate legacy single-value keys onto Normal).
            Scribe_Values.Look(ref roadBlockLightFlatPenalty, "roadBlockLightFlatPenalty", DefRoadBlockLightFlatPenalty);
            Scribe_Values.Look(ref roadBlockNormalFlatPenalty, "roadBlockNormalFlatPenalty", DefRoadBlockNormalFlatPenalty);
            Scribe_Values.Look(ref roadBlockHeavyFlatPenalty, "roadBlockHeavyFlatPenalty", DefRoadBlockHeavyFlatPenalty);
            Scribe_Values.Look(ref roadBlockLightExpeditionStrength, "roadBlockLightExpeditionStrength", DefRoadBlockLightExpeditionStrength);
            Scribe_Values.Look(ref roadBlockNormalExpeditionStrength, "roadBlockNormalExpeditionStrength", DefRoadBlockNormalExpeditionStrength);
            Scribe_Values.Look(ref roadBlockHeavyExpeditionStrength, "roadBlockHeavyExpeditionStrength", DefRoadBlockHeavyExpeditionStrength);
            Scribe_Values.Look(ref roadBlockLightWork, "roadBlockLightWork", DefRoadBlockLightWork);
            Scribe_Values.Look(ref roadBlockNormalWork, "roadBlockNormalWork", DefRoadBlockNormalWork);
            Scribe_Values.Look(ref roadBlockHeavyWork, "roadBlockHeavyWork", DefRoadBlockHeavyWork);
            Scribe_Values.Look(ref roadBlockLightMaxHealth, "roadBlockLightMaxHealth", DefRoadBlockLightMaxHealth);
            Scribe_Values.Look(ref roadBlockNormalMaxHealth, "roadBlockNormalMaxHealth", DefRoadBlockNormalMaxHealth);
            Scribe_Values.Look(ref roadBlockHeavyMaxHealth, "roadBlockHeavyMaxHealth", DefRoadBlockHeavyMaxHealth);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                float legacyPenalty = -1f;
                float legacyStrength = -1f;
                float legacyWork = -1f;
                Scribe_Values.Look(ref legacyPenalty, "roadBlockFlatPenalty", -1f);
                Scribe_Values.Look(ref legacyStrength, "roadBlockExpeditionStrength", -1f);
                Scribe_Values.Look(ref legacyWork, "roadBlockWork", -1f);
                if (legacyPenalty >= 0f && Mathf.Approximately(roadBlockNormalFlatPenalty, DefRoadBlockNormalFlatPenalty))
                    roadBlockNormalFlatPenalty = legacyPenalty;
                if (legacyStrength >= 0f && Mathf.Approximately(roadBlockNormalExpeditionStrength, DefRoadBlockNormalExpeditionStrength))
                    roadBlockNormalExpeditionStrength = legacyStrength;
                if (legacyWork >= 0f && Mathf.Approximately(roadBlockNormalWork, DefRoadBlockNormalWork))
                    roadBlockNormalWork = legacyWork;
            }

            Scribe_Values.Look(ref maxSpikeTrapRange, "maxSpikeTrapRange", DefMaxSpikeTrapRange);
            Scribe_Values.Look(ref spikeTrapSpikeWork, "spikeTrapSpikeWork", DefSpikeTrapSpikeWork);
            Scribe_Values.Look(ref spikeTrapCaltropsWork, "spikeTrapCaltropsWork", DefSpikeTrapCaltropsWork);
            Scribe_Values.Look(ref spikeTrapSpikeExpeditionStrength, "spikeTrapSpikeExpeditionStrength", DefSpikeTrapSpikeExpeditionStrength);
            Scribe_Values.Look(ref spikeTrapCaltropsExpeditionStrength, "spikeTrapCaltropsExpeditionStrength", DefSpikeTrapCaltropsExpeditionStrength);
            Scribe_Values.Look(ref spikeTrapSpikeDamage, "spikeTrapSpikeDamage", DefSpikeTrapSpikeDamage);
            Scribe_Values.Look(ref spikeTrapCaltropsDamage, "spikeTrapCaltropsDamage", DefSpikeTrapCaltropsDamage);
            Scribe_Values.Look(ref spikeTrapSpikeMaxHealth, "spikeTrapSpikeMaxHealth", DefSpikeTrapSpikeMaxHealth);
            Scribe_Values.Look(ref spikeTrapCaltropsMaxHealth, "spikeTrapCaltropsMaxHealth", DefSpikeTrapCaltropsMaxHealth);
            Scribe_Values.Look(ref spikeTrapMaxTriggersPerTraveler, "spikeTrapMaxTriggersPerTraveler", DefSpikeTrapMaxTriggersPerTraveler);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                float legacyTrapWork = -1f;
                float legacyTrapStrength = -1f;
                float legacyTrapDamage = -1f;
                Scribe_Values.Look(ref legacyTrapWork, "spikeTrapWork", -1f);
                Scribe_Values.Look(ref legacyTrapStrength, "spikeTrapExpeditionStrength", -1f);
                Scribe_Values.Look(ref legacyTrapDamage, "spikeTrapDamage", -1f);
                if (legacyTrapWork >= 0f && Mathf.Approximately(spikeTrapSpikeWork, DefSpikeTrapSpikeWork))
                    spikeTrapSpikeWork = legacyTrapWork;
                if (legacyTrapStrength >= 0f && Mathf.Approximately(spikeTrapSpikeExpeditionStrength, DefSpikeTrapSpikeExpeditionStrength))
                    spikeTrapSpikeExpeditionStrength = legacyTrapStrength;
                if (legacyTrapDamage >= 0f && Mathf.Approximately(spikeTrapSpikeDamage, DefSpikeTrapSpikeDamage))
                    spikeTrapSpikeDamage = legacyTrapDamage;
            }

            Scribe_Values.Look(ref maxDecontaminationRange, "maxDecontaminationRange", DefMaxDecontaminationRange);
            Scribe_Values.Look(ref decontaminationWork, "decontaminationWork", DefDecontaminationWork);
            Scribe_Values.Look(ref decontaminationExpeditionStrength, "decontaminationExpeditionStrength", DefDecontaminationExpeditionStrength);
            Scribe_Values.Look(ref decontaminationPollutionReductionPp, "decontaminationPollutionReductionPp", DefDecontaminationPollutionReductionPp);

            Scribe_Values.Look(ref fallbackDirtRoadMovement, "fallbackDirtRoadMovement", DefFallbackDirtRoadMovement);
            Scribe_Values.Look(ref fallbackStoneRoadMovement, "fallbackStoneRoadMovement", DefFallbackStoneRoadMovement);
            Scribe_Values.Look(ref fallbackAsphaltRoadMovement, "fallbackAsphaltRoadMovement", DefFallbackAsphaltRoadMovement);
            Scribe_Values.Look(ref fallbackDirtRoadWork, "fallbackDirtRoadWork", DefFallbackDirtRoadWork);
            Scribe_Values.Look(ref fallbackStoneRoadWork, "fallbackStoneRoadWork", DefFallbackStoneRoadWork);
            Scribe_Values.Look(ref fallbackAsphaltRoadWork, "fallbackAsphaltRoadWork", DefFallbackAsphaltRoadWork);
            Scribe_Values.Look(ref fallbackDirtRoadExpeditionStrength, "fallbackDirtRoadExpeditionStrength", DefFallbackDirtRoadExpeditionStrength);
            Scribe_Values.Look(ref fallbackStoneRoadExpeditionStrength, "fallbackStoneRoadExpeditionStrength", DefFallbackStoneRoadExpeditionStrength);
            Scribe_Values.Look(ref fallbackAsphaltRoadExpeditionStrength, "fallbackAsphaltRoadExpeditionStrength", DefFallbackAsphaltRoadExpeditionStrength);
            Scribe_Values.Look(ref fallbackDirtRoadMinConstruction, "fallbackDirtRoadMinConstruction", DefFallbackDirtRoadMinConstruction);
            Scribe_Values.Look(ref fallbackStoneRoadMinConstruction, "fallbackStoneRoadMinConstruction", DefFallbackStoneRoadMinConstruction);
            Scribe_Values.Look(ref fallbackAsphaltRoadMinConstruction, "fallbackAsphaltRoadMinConstruction", DefFallbackAsphaltRoadMinConstruction);
            Scribe_Values.Look(ref fallbackDirtRoadWinterReduction, "fallbackDirtRoadWinterReduction", DefFallbackDirtRoadWinterReduction);
            Scribe_Values.Look(ref fallbackStoneRoadWinterReduction, "fallbackStoneRoadWinterReduction", DefFallbackStoneRoadWinterReduction);
            Scribe_Values.Look(ref fallbackAsphaltRoadWinterReduction, "fallbackAsphaltRoadWinterReduction", DefFallbackAsphaltRoadWinterReduction);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                ClampRoadBuildingCascades();

            Scribe_Values.Look(ref minorIncidentSeverity, "minorIncidentSeverity", DefMinorIncSev);
            Scribe_Values.Look(ref majorIncidentSeverity, "majorIncidentSeverity", DefMajorIncSev);
            Scribe_Values.Look(ref localMaxT1, "localMaxT1", DefLocalMaxT1);
            Scribe_Values.Look(ref localMaxT2, "localMaxT2", DefLocalMaxT2);
            Scribe_Values.Look(ref localMaxT3, "localMaxT3", DefLocalMaxT3);
            Scribe_Values.Look(ref localMaxT4, "localMaxT4", DefLocalMaxT4);
            Scribe_Values.Look(ref sameTierNeighborsToUpgradeT1, "sameTierNeighborsToUpgradeT1", DefSameTierNeighborsToUpgradeT1);
            Scribe_Values.Look(ref sameTierNeighborsToUpgradeT2, "sameTierNeighborsToUpgradeT2", DefSameTierNeighborsToUpgradeT2);
            Scribe_Values.Look(ref sameTierNeighborsToUpgradeT3, "sameTierNeighborsToUpgradeT3", DefSameTierNeighborsToUpgradeT3);
            Scribe_Values.Look(ref expansionSuccessChance, "expansionSuccessChance", DefExpansionSuccessChance);
            Scribe_Values.Look(ref tier1BaseDefensiveStrength, "tier1BaseDefensiveStrength", DefTier1BaseDefensiveStrength);
            Scribe_Values.Look(ref tier2BaseDefensiveStrength, "tier2BaseDefensiveStrength", DefTier2BaseDefensiveStrength);
            Scribe_Values.Look(ref tier3BaseDefensiveStrength, "tier3BaseDefensiveStrength", DefTier3BaseDefensiveStrength);
            Scribe_Values.Look(ref tier4BaseDefensiveStrength, "tier4BaseDefensiveStrength", DefTier4BaseDefensiveStrength);
            Scribe_Values.Look(ref playerOutpostBaseDefensiveStrength, "playerOutpostBaseDefensiveStrength", DefPlayerOutpostBaseDefensiveStrength);

            Scribe_Values.Look(ref revoltChance, "revoltChance", DefRevoltChance);
            Scribe_Values.Look(ref diplomacyChangeChance, "diplomacyChangeChance", DefDiplomacyChangeChance);
            Scribe_Values.Look(ref enableLeaderHandicap, "enableLeaderHandicap", DefEnableLeaderHandicap);
            Scribe_Values.Look(ref enableUnderdogBuff, "enableUnderdogBuff", DefEnableUnderdogBuff);
            Scribe_Values.Look(ref enableAntiLeaderCoalition, "enableAntiLeaderCoalition", DefEnableAntiLeaderCoalition);
            Scribe_Values.Look(ref enableRandomDiplomacy, "enableRandomDiplomacy", DefEnableRandomDiplomacy);
            Scribe_Values.Look(ref enableStrongFactionWar, "enableStrongFactionWar", DefEnableStrongFactionWar);
            Scribe_Values.Look(ref strongFactionWarChance, "strongFactionWarChance", DefStrongFactionWarChance);
            Scribe_Values.Look(ref strongFactionWarTopPct, "strongFactionWarTopPct", DefStrongFactionWarTopPct);
            Scribe_Values.Look(ref strongFactionWarRequireMidOrLate, "strongFactionWarRequireMidOrLate", DefStrongFactionWarRequireMidOrLate);
            Scribe_Values.Look(ref enableExpansionistZeal, "enableExpansionistZeal", DefEnableExpansionistZeal);
            Scribe_Values.Look(ref zealTriggerChance, "zealTriggerChance", DefZealTriggerChance);

            Scribe_Values.Look(ref durLeaderHandicapDays, "durLeaderHandicapDays", DefDurLeaderHandicapDays);
            Scribe_Values.Look(ref cdLeaderHandicapDays, "cdLeaderHandicapDays", DefCdLeaderHandicapDays);
            Scribe_Values.Look(ref durUnderdogBuffDays, "durUnderdogBuffDays", DefDurUnderdogBuffDays);
            Scribe_Values.Look(ref cdUnderdogBuffDays, "cdUnderdogBuffDays", DefCdUnderdogBuffDays);
            Scribe_Values.Look(ref durExpansionistZealDays, "durExpansionistZealDays", DefDurExpansionistZealDays);
            Scribe_Values.Look(ref cdExpansionistZealDays, "cdExpansionistZealDays", DefCdExpansionistZealDays);
            Scribe_Values.Look(ref durAntiLeaderCoalitionDays, "durAntiLeaderCoalitionDays", DefDurAntiLeaderCoalitionDays);
            Scribe_Values.Look(ref cdAntiLeaderCoalitionDays, "cdAntiLeaderCoalitionDays", DefCdAntiLeaderCoalitionDays);
            Scribe_Values.Look(ref leaderHandicapTriggerChance, "leaderHandicapTriggerChance", DefLeaderHandicapTriggerChance);
            Scribe_Values.Look(ref underdogBuffTriggerChance, "underdogBuffTriggerChance", DefUnderdogBuffTriggerChance);
            Scribe_Values.Look(ref antiLeaderCoalitionTriggerChance, "antiLeaderCoalitionTriggerChance", DefAntiLeaderCoalitionTriggerChance);
            Scribe_Values.Look(ref zealRaidRangeMult, "zealRaidRangeMult", DefZealRaidRangeMult);
            Scribe_Values.Look(ref zealAttritionMult, "zealAttritionMult", DefZealAttritionMult);
            Scribe_Values.Look(ref underdogActionShareMult, "underdogActionShareMult", DefUnderdogActionShareMult);
            Scribe_Values.Look(ref underdogIncidentWeightMult, "underdogIncidentWeightMult", DefUnderdogIncidentWeightMult);
            Scribe_Values.Look(ref underdogIncidentSeverityMult, "underdogIncidentSeverityMult", DefUnderdogIncidentSeverityMult);
            Scribe_Values.Look(ref underdogGrowthGainMult, "underdogGrowthGainMult", DefUnderdogGrowthGainMult);
            Scribe_Values.Look(ref leaderIncidentWeightMult, "leaderIncidentWeightMult", DefLeaderIncidentWeightMult);
            Scribe_Values.Look(ref leaderIncidentSeverityMult, "leaderIncidentSeverityMult", DefLeaderIncidentSeverityMult);
            Scribe_Values.Look(ref alliedRaidOrderMinWinChance, "alliedRaidOrderMinWinChance", DefAlliedRaidOrderMinWinChance);
            Scribe_Values.Look(ref alliedRaidClaimCostT1, "alliedRaidClaimCostT1", DefAlliedRaidClaimCostT1);
            Scribe_Values.Look(ref alliedRaidClaimCostT2, "alliedRaidClaimCostT2", DefAlliedRaidClaimCostT2);
            Scribe_Values.Look(ref alliedRaidClaimCostT3, "alliedRaidClaimCostT3", DefAlliedRaidClaimCostT3);
            Scribe_Values.Look(ref alliedRaidClaimCostT4, "alliedRaidClaimCostT4", DefAlliedRaidClaimCostT4);
            Scribe_Values.Look(ref enableSettlementBuy, "enableSettlementBuy", DefEnableSettlementBuy);
            Scribe_Values.Look(ref settlementBuyAskT1, "settlementBuyAskT1", DefSettlementBuyAskT1);
            Scribe_Values.Look(ref settlementBuyAskT2, "settlementBuyAskT2", DefSettlementBuyAskT2);
            Scribe_Values.Look(ref settlementBuyAskT3, "settlementBuyAskT3", DefSettlementBuyAskT3);
            Scribe_Values.Look(ref settlementBuyAskT4, "settlementBuyAskT4", DefSettlementBuyAskT4);
            Scribe_Values.Look(ref settlementBuySilverPerGoodwill, "settlementBuySilverPerGoodwill", DefSettlementBuySilverPerGoodwill);
            Scribe_Values.Look(ref settlementBuyMaxGoodwillShare, "settlementBuyMaxGoodwillShare", DefSettlementBuyMaxGoodwillShare);
            Scribe_Values.Look(ref notifySettlementBuyStarted, "notifySettlementBuyStarted", DefNotifySettlementBuyStarted);
            Scribe_Values.Look(ref notifySettlementBuyCompleted, "notifySettlementBuyCompleted", DefNotifySettlementBuyCompleted);
            Scribe_Values.Look(ref notifySettlementBuyAborted, "notifySettlementBuyAborted", DefNotifySettlementBuyAborted);
            Scribe_Values.Look(ref enableDiplomacyNegotiate, "enableDiplomacyNegotiate", DefEnableDiplomacyNegotiate);
            Scribe_Values.Look(ref negotiateAskMinSilver, "negotiateAskMinSilver", DefNegotiateAskMinSilver);
            Scribe_Values.Look(ref negotiateAskMaxSilver, "negotiateAskMaxSilver", DefNegotiateAskMaxSilver);
            Scribe_Values.Look(ref notifyDiplomacyNegotiateStarted, "notifyDiplomacyNegotiateStarted", DefNotifyDiplomacyNegotiateStarted);
            Scribe_Values.Look(ref notifyDiplomacyNegotiateCompleted, "notifyDiplomacyNegotiateCompleted", DefNotifyDiplomacyNegotiateCompleted);
            Scribe_Values.Look(ref notifyDiplomacyNegotiateAborted, "notifyDiplomacyNegotiateAborted", DefNotifyDiplomacyNegotiateAborted);
            Scribe_Values.Look(ref enableFactionBribe, "enableFactionBribe", DefEnableFactionBribe);
            Scribe_Values.Look(ref bribeSettlementSilverPerStrength, "bribeSettlementSilverPerStrength", DefBribeSettlementSilverPerStrength);
            Scribe_Values.Look(ref bribeCaravanSilverPerStrengthEarly, "bribeCaravanSilverPerStrengthEarly", DefBribeCaravanSilverPerStrengthEarly);
            Scribe_Values.Look(ref bribeCaravanSilverPerStrengthMid, "bribeCaravanSilverPerStrengthMid", DefBribeCaravanSilverPerStrengthMid);
            Scribe_Values.Look(ref bribeCaravanSilverPerStrengthLate, "bribeCaravanSilverPerStrengthLate", DefBribeCaravanSilverPerStrengthLate);
            Scribe_Values.Look(ref bribeCeasefireDaysShort, "bribeCeasefireDaysShort", DefBribeCeasefireDaysShort);
            Scribe_Values.Look(ref bribeCeasefireDaysMedium, "bribeCeasefireDaysMedium", DefBribeCeasefireDaysMedium);
            Scribe_Values.Look(ref bribeCeasefireDaysLong, "bribeCeasefireDaysLong", DefBribeCeasefireDaysLong);
            Scribe_Values.Look(ref bribeCeasefireDiscountMedium, "bribeCeasefireDiscountMedium", DefBribeCeasefireDiscountMedium);
            Scribe_Values.Look(ref bribeCeasefireDiscountLong, "bribeCeasefireDiscountLong", DefBribeCeasefireDiscountLong);
            Scribe_Values.Look(ref bribeRaidAskFloorFraction, "bribeRaidAskFloorFraction", DefBribeRaidAskFloorFraction);
            Scribe_Values.Look(ref bribeInvestmentFraction, "bribeInvestmentFraction", DefBribeInvestmentFraction);
            Scribe_Values.Look(ref bribeCaravanInvestmentRadiusTiles, "bribeCaravanInvestmentRadiusTiles", DefBribeCaravanInvestmentRadiusTiles);
            Scribe_Values.Look(ref bribeGoodwillDivisor, "bribeGoodwillDivisor", DefBribeGoodwillDivisor);
            Scribe_Values.Look(ref notifyBribeSettlementCompleted, "notifyBribeSettlementCompleted", DefNotifyBribeSettlementCompleted);
            Scribe_Values.Look(ref notifyBribeSettlementAborted, "notifyBribeSettlementAborted", DefNotifyBribeSettlementAborted);
            Scribe_Values.Look(ref notifyBribeRaidCompleted, "notifyBribeRaidCompleted", DefNotifyBribeRaidCompleted);
            Scribe_Values.Look(ref notifyBribeRaidAborted, "notifyBribeRaidAborted", DefNotifyBribeRaidAborted);
            Scribe_Values.Look(ref notifyBribeLostInTransit, "notifyBribeLostInTransit", DefNotifyBribeLostInTransit);
            Scribe_Values.Look(ref notifyBribeCeasefireExpired, "notifyBribeCeasefireExpired", DefNotifyBribeCeasefireExpired);
            Scribe_Values.Look(ref alliedRaidAwardCostT1, "alliedRaidAwardCostT1", DefAlliedRaidAwardCostT1);
            Scribe_Values.Look(ref alliedRaidAwardCostT2, "alliedRaidAwardCostT2", DefAlliedRaidAwardCostT2);
            Scribe_Values.Look(ref alliedRaidAwardCostT3, "alliedRaidAwardCostT3", DefAlliedRaidAwardCostT3);
            Scribe_Values.Look(ref alliedRaidAwardCostT4, "alliedRaidAwardCostT4", DefAlliedRaidAwardCostT4);
            Scribe_Values.Look(ref orderedRoadBaseCostT1, "orderedRoadBaseCostT1", DefOrderedRoadBaseCostT1);
            Scribe_Values.Look(ref orderedRoadBaseCostT2, "orderedRoadBaseCostT2", DefOrderedRoadBaseCostT2);
            Scribe_Values.Look(ref orderedRoadBaseCostT3, "orderedRoadBaseCostT3", DefOrderedRoadBaseCostT3);
            Scribe_Values.Look(ref orderedRoadBaseCostT4, "orderedRoadBaseCostT4", DefOrderedRoadBaseCostT4);
            Scribe_Values.Look(ref orderedRoadPerSegmentT1, "orderedRoadPerSegmentT1", DefOrderedRoadPerSegmentRateT1);
            Scribe_Values.Look(ref orderedRoadPerSegmentT2, "orderedRoadPerSegmentT2", DefOrderedRoadPerSegmentRateT2);
            Scribe_Values.Look(ref orderedRoadPerSegmentT3, "orderedRoadPerSegmentT3", DefOrderedRoadPerSegmentRateT3);
            Scribe_Values.Look(ref orderedTraderGoodwillCost, "orderedTraderGoodwillCost", DefOrderedTraderGoodwillCost);
            Scribe_Values.Look(ref conquestAllyGiftGoodwillT1, "conquestAllyGiftGoodwillT1", DefConquestAllyGiftGoodwillT1);
            Scribe_Values.Look(ref conquestAllyGiftGoodwillT2, "conquestAllyGiftGoodwillT2", DefConquestAllyGiftGoodwillT2);
            Scribe_Values.Look(ref conquestAllyGiftGoodwillT3, "conquestAllyGiftGoodwillT3", DefConquestAllyGiftGoodwillT3);
            Scribe_Values.Look(ref conquestAllyGiftGoodwillT4, "conquestAllyGiftGoodwillT4", DefConquestAllyGiftGoodwillT4);

            Scribe_Values.Look(ref notificationRadiusTiles, "notificationRadiusTiles", DefNotificationRadiusTiles);
            Scribe_Values.Look(ref influenceStartTiles, "influenceStartTiles", DefInfluenceStartTiles);
            Scribe_Values.Look(ref influenceWealthPer10k, "influenceWealthPer10k", DefInfluenceWealthPer10k);
            Scribe_Values.Look(ref influencePerDay, "influencePerDay", DefInfluencePerDay);
            Scribe_Values.Look(ref influencePer10kOutpostDefense, "influencePer10kOutpostDefense", DefInfluencePer10kOutpostDefense);
            Scribe_Values.Look(ref enableLateGameScaling, "enableLateGameScaling", DefEnableLateGameScaling);
            Scribe_Values.Look(ref enableOutpostIncidents, "enableOutpostIncidents", DefEnableOutpostIncidents);
            Scribe_Values.Look(ref outpostIncidentSeverity, "outpostIncidentSeverity", DefOutpostIncidentSeverity);
            Scribe_Values.Look(ref outpostIncidentDailyChance, "outpostIncidentDailyChance", DefOutpostIncidentDailyChance);
            Scribe_Values.Look(ref notifyOutpostIncident, "notifyOutpostIncident", DefNotifyOutpostIncident);
            Scribe_Values.Look(ref coalitionRaidPriorityBias, "coalitionRaidPriorityBias", DefCoalitionRaidPriorityBias);
            Scribe_Values.Look(ref midGameShareThreshold, "midGameShareThreshold", DefMidGameShareThreshold);
            Scribe_Values.Look(ref midGameOutpostStrengthThreshold, "midGameOutpostStrengthThreshold", DefMidGameOutpostStrengthThreshold);
            Scribe_Values.Look(ref midGameRaidBiasPct, "midGameRaidBiasPct", DefMidGameRaidBiasPct);
            Scribe_Values.Look(ref midGameGrowthMult, "midGameGrowthMult", DefMidGameGrowthMult);
            Scribe_Values.Look(ref midGameAttackRangeBonusPct, "midGameAttackRangeBonusPct", DefMidGameAttackRangeBonusPct);
            Scribe_Values.Look(ref enableMidGameAllyRadiusScaling, "enableMidGameAllyRadiusScaling", DefEnableMidGameAllyRadiusScaling);
            Scribe_Values.Look(ref midGameAllyRadiusBonusPct, "midGameAllyRadiusBonusPct", DefMidGameAllyRadiusBonusPct);
            Scribe_Values.Look(ref midGameExpandTowardPlayerMaxTiles, "midGameExpandTowardPlayerMaxTiles", DefMidGameExpandTowardPlayerMaxTiles);
            Scribe_Values.Look(ref midGameGarrisonBoostPct, "midGameGarrisonBoostPct", DefMidGameGarrisonBoostPct);
            Scribe_Values.Look(ref enableMidGameT4SettlementMortar, "enableMidGameT4SettlementMortar", DefEnableMidGameT4SettlementMortar);
            Scribe_Values.Look(ref enableMidGameT4SettlementAntiAir, "enableMidGameT4SettlementAntiAir", DefEnableMidGameT4SettlementAntiAir);
            Scribe_Values.Look(ref enableMidGameOutpostIncidents, "enableMidGameOutpostIncidents", DefEnableMidGameOutpostIncidents);
            Scribe_Values.Look(ref midGameOutpostIncidentSeverity, "midGameOutpostIncidentSeverity", DefMidGameOutpostIncidentSeverity);
            Scribe_Values.Look(ref midGameOutpostIncidentDailyChance, "midGameOutpostIncidentDailyChance", DefMidGameOutpostIncidentDailyChance);
            Scribe_Values.Look(ref enableGoodwillDrain, "enableGoodwillDrain", DefEnableGoodwillDrain);
            Scribe_Values.Look(ref goodwillDrainIntervalDays, "goodwillDrainIntervalDays", DefGoodwillDrainIntervalDays);
            Scribe_Values.Look(ref midGameGoodwillDrainAmount, "midGameGoodwillDrainAmount", DefMidGameGoodwillDrainAmount);
            Scribe_Values.Look(ref lateGameGoodwillDrainAmount, "lateGameGoodwillDrainAmount", DefLateGameGoodwillDrainAmount);
            Scribe_Values.Look(ref lateGameShareThreshold, "lateGameShareThreshold", DefLateGameShareThreshold);
            Scribe_Values.Look(ref lateGameOutpostStrengthThreshold, "lateGameOutpostStrengthThreshold", DefLateGameOutpostStrengthThreshold);
            Scribe_Values.Look(ref lateGameRaidBiasPct, "lateGameRaidBiasPct", DefLateGameRaidBiasPct);
            Scribe_Values.Look(ref lateGameGrowthMult, "lateGameGrowthMult", DefLateGameGrowthMult);
            Scribe_Values.Look(ref lateGameAttackRangeBonusPct, "lateGameAttackRangeBonusPct", DefLateGameAttackRangeBonusPct);
            Scribe_Values.Look(ref enableLateGameAllyRadiusScaling, "enableLateGameAllyRadiusScaling", DefEnableLateGameAllyRadiusScaling);
            Scribe_Values.Look(ref lateGameAllyRadiusBonusPct, "lateGameAllyRadiusBonusPct", DefLateGameAllyRadiusBonusPct);
            Scribe_Values.Look(ref lateGameExpandTowardPlayerMaxTiles, "lateGameExpandTowardPlayerMaxTiles", DefLateGameExpandTowardPlayerMaxTiles);
            Scribe_Values.Look(ref lateGameGarrisonBoostPct, "lateGameGarrisonBoostPct", DefLateGameGarrisonBoostPct);
            Scribe_Values.Look(ref enableT4SettlementMortar, "enableT4SettlementMortar", DefEnableT4SettlementMortar);
            // Legacy: notificationRadiusTiles used to migrate into influenceStartTiles. Influence is unused;
            // notificationRadiusTiles is live again for Nearby world event letters. Do not copy into influence.
            Scribe_Values.Look(ref caravanRaidPointsMinStorytellerFraction, "caravanRaidPointsMinStorytellerFraction", DefCaravanRaidMinStorytellerFrac);
            Scribe_Values.Look(ref caravanRaidPointsMaxStorytellerFraction, "caravanRaidPointsMaxStorytellerFraction", DefCaravanRaidMaxStorytellerFrac);
            Scribe_Values.Look(ref scaleRaidClampWithEscalation, "scaleRaidClampWithEscalation", DefScaleRaidClampWithEscalation);
            Scribe_Values.Look(ref earlyRaidClampMinStorytellerFraction, "earlyRaidClampMinStorytellerFraction", DefEarlyRaidClampMinStorytellerFrac);
            Scribe_Values.Look(ref earlyRaidClampMaxStorytellerFraction, "earlyRaidClampMaxStorytellerFraction", DefEarlyRaidClampMaxStorytellerFrac);
            Scribe_Values.Look(ref midRaidClampMinStorytellerFraction, "midRaidClampMinStorytellerFraction", DefMidRaidClampMinStorytellerFrac);
            Scribe_Values.Look(ref midRaidClampMaxStorytellerFraction, "midRaidClampMaxStorytellerFraction", DefMidRaidClampMaxStorytellerFrac);
            Scribe_Values.Look(ref lateRaidClampMinStorytellerFraction, "lateRaidClampMinStorytellerFraction", DefLateRaidClampMinStorytellerFrac);
            Scribe_Values.Look(ref lateRaidClampMaxStorytellerFraction, "lateRaidClampMaxStorytellerFraction", DefLateRaidClampMaxStorytellerFrac);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                NormalizeRaidClampFractions();
            Scribe_Values.Look(ref alwaysUseStrengthAsRaidPoints, "alwaysUseStrengthAsRaidPoints", DefAlwaysUseStrengthAsRaidPoints);
            Scribe_Values.Look(ref alwaysUseStrengthAsOutpostDefenseRaidPoints, "alwaysUseStrengthAsOutpostDefenseRaidPoints", DefAlwaysUseStrengthAsOutpostDefenseRaidPoints);
            Scribe_Values.Look(ref minRaidPoints, "minRaidPoints", DefMinRaidPoints);
            Scribe_Values.Look(ref maxRaidPoints, "maxRaidPoints", DefMaxRaidPoints);

            Scribe_Values.Look(ref allowPlayerRaid, "allowPlayerRaid", DefAllowPlayerRaid);
            Scribe_Values.Look(ref allowPlayerOutpostRaid, "allowPlayerOutpostRaid", DefAllowPlayerOutpostRaid);
            Scribe_Values.Look(ref cooldownPlayerRaidDays, "cooldownPlayerRaidDays", DefCdPlayerRaidDays);
            Scribe_Values.Look(ref maxPlayerWdRaidsPerDay, "maxPlayerWdRaidsPerDay", DefMaxPlayerWdRaidsPerDay);
            Scribe_Values.Look(ref maxPlayerWdRaidsPer4Days, "maxPlayerWdRaidsPer4Days", DefMaxPlayerWdRaidsPer4Days);
            Scribe_Values.Look(ref maxPlayerWdRaidsPer7Days, "maxPlayerWdRaidsPer7Days", DefMaxPlayerWdRaidsPer7Days);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                ClampPlayerWdRaidRateCaps();
            Scribe_Values.Look(ref raidTargetRadius, "raidTargetRadius", DefRaidTargetRadius);
            Scribe_Values.Look(ref tier1AttackRangeBaseline, "tier1AttackRangeBaseline", DefTier1AttackRangeBaseline);
            Scribe_Values.Look(ref tier2AttackRangeBaseline, "tier2AttackRangeBaseline", DefTier2AttackRangeBaseline);
            Scribe_Values.Look(ref tier3AttackRangeBaseline, "tier3AttackRangeBaseline", DefTier3AttackRangeBaseline);
            Scribe_Values.Look(ref tier4AttackRangeBaseline, "tier4AttackRangeBaseline", DefTier4AttackRangeBaseline);
            Scribe_Values.Look(ref attackRangeTimeMaxBonusPct, "attackRangeTimeMaxBonusPct", DefAttackRangeTimeMaxBonusPct);
            Scribe_Values.Look(ref attackRangeDaysToMax, "attackRangeDaysToMax", DefAttackRangeDaysToMax);
            Scribe_Values.Look(ref raidAllyRadius, "raidAllyRadius", DefRaidAllyRadius);
            WD_RadiusOverlayPrefs.ExposeData();
            // Obsolete att/def radii: absorb old save keys so loads stay safe; do not migrate values — new default applies when key absent.
            float obsoleteAttAllyRadius = DefRaidAllyRadius;
            float obsoleteDefAllyRadius = DefRaidAllyRadius;
            Scribe_Values.Look(ref obsoleteAttAllyRadius, "raidAttackerAllyRadius", DefRaidAllyRadius);
            Scribe_Values.Look(ref obsoleteDefAllyRadius, "raidDefenderAllyRadius", DefRaidAllyRadius);
            Scribe_Values.Look(ref minRaidRatio, "minRaidRatio", DefMinRaidRatio);
            Scribe_Values.Look(ref razeChance, "razeChance", DefRazeChance);
            Scribe_Values.Look(ref ruinLingerDays, "ruinLingerDays", DefRuinLingerDays);
            Scribe_Collections.Look(ref raidOutcomes, "raidOutcomes", LookMode.Deep);
            List<RaidCasualtyEntry> legacyRaidCasualties = null;
            Scribe_Collections.Look(ref legacyRaidCasualties, "raidCasualties", LookMode.Deep);
            Scribe_Collections.Look(ref raidAttLossOnWin, "raidAttLossOnWin", LookMode.Deep);
            Scribe_Collections.Look(ref raidAttLossOnLoss, "raidAttLossOnLoss", LookMode.Deep);
            Scribe_Collections.Look(ref raidDefLossOnWin, "raidDefLossOnWin", LookMode.Deep);
            Scribe_Collections.Look(ref raidDefLossOnLoss, "raidDefLossOnLoss", LookMode.Deep);
            Scribe_Values.Look(ref raidAllyLossMultiplier, "raidAllyLossMultiplier", DefRaidAllyLossMultiplier);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                InvalidateRaidOutcomesCache();
                EnsureRaidLossTablesInitialized();
                _pendingLegacyRaidCasualties = legacyRaidCasualties;
            }
            Scribe_Values.Look(ref maxRaidDays, "maxRaidDays", DefMaxRaidDays);
            Scribe_Values.Look(ref minEfficiency, "minEfficiency", DefMinEfficiency);
            Scribe_Values.Look(ref strengthLossPerHour, "strengthLossPerHour", DefStrengthLossPerHour);
            Scribe_Values.Look(ref maxTravelPercentageStrengthLoss, "maxTravelPercentageStrengthLoss", DefMaxTravelPercentageStrengthLoss);
            Scribe_Values.Look(ref allowCaravansTravelOverWater, "allowCaravansTravelOverWater", DefAllowCaravansTravelOverWater);
            Scribe_Values.Look(ref onlyTravelAcrossWaterIfNoOtherWay, "onlyTravelAcrossWaterIfNoOtherWay", DefOnlyTravelAcrossWaterIfNoOtherWay);
            Scribe_Values.Look(ref travelerWaterMovementDifficulty, "travelerWaterMovementDifficulty", DefTravelerWaterMovementDifficulty);
            Scribe_Values.Look(ref waterPathLandThresholdDays, "waterPathLandThresholdDays", DefWaterPathLandThresholdDays);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                float loadedPrep = -1f;
                Scribe_Values.Look(ref loadedPrep, "travelPrepExactPercent", -1f);
                if (loadedPrep >= 0f)
                {
                    // Legacy saves stored 0–100; values above 1 are that scale. Current scale is 0–1.
                    travelPrepExactPercent = loadedPrep > 1f
                        ? Mathf.Clamp(loadedPrep / 100f, 0f, 1f)
                        : Mathf.Clamp(loadedPrep, 0f, 1f);
                }
                else if (Scribe.loader?.curXmlParent != null && Scribe.loader.curXmlParent["approxTravelCostPrep"] != null)
                {
                    // Migrate legacy bool: true (approx) → 0%, false (exact) → 100%.
                    bool approxLegacy = false;
                    Scribe_Values.Look(ref approxLegacy, "approxTravelCostPrep", false);
                    travelPrepExactPercent = approxLegacy ? 0f : 1f;
                }
                else
                    travelPrepExactPercent = DefTravelPrepExactPercent;
            }
            else
                Scribe_Values.Look(ref travelPrepExactPercent, "travelPrepExactPercent", DefTravelPrepExactPercent);

            Scribe_Values.Look(ref experimentalColonyWorldBuild, "experimentalColonyWorldBuild", DefExperimentalColonyWorldBuild);
            Scribe_Values.Look(ref experimentalPlayerConquestRaze, "experimentalPlayerConquestRaze", DefExperimentalPlayerConquestRaze);
            Scribe_Values.Look(ref experimentalTargetOfOpportunity, "experimentalTargetOfOpportunity", DefExperimentalTargetOfOpportunity);
            Scribe_Values.Look(ref targetOfOpportunityEligibilityRollPct, "targetOfOpportunityEligibilityRollPct", DefTargetOfOpportunityEligibilityRollPct);
            Scribe_Values.Look(ref targetOfOpportunityMinRatioAdvantage, "targetOfOpportunityMinRatioAdvantage", DefTargetOfOpportunityMinRatioAdvantage);
            Scribe_Values.Look(ref targetOfOpportunityMaxRetargets, "targetOfOpportunityMaxRetargets", DefTargetOfOpportunityMaxRetargets);
            Scribe_Values.Look(ref targetChangesMaxLifetime, "targetChangesMaxLifetime", DefTargetChangesMaxLifetime);
            Scribe_Values.Look(ref targetOfOpportunityDogpileCooldownTicks, "targetOfOpportunityDogpileCooldownTicks", DefTargetOfOpportunityDogpileCooldownTicks);
            Scribe_Values.Look(ref experimentalContinueAfterConquest, "experimentalContinueAfterConquest", DefExperimentalContinueAfterConquest);
            Scribe_Values.Look(ref maraudingChanceToOccurPct, "maraudingChanceToOccurPct", DefMaraudingChanceToOccurPct);
            Scribe_Values.Look(ref maraudingMinSurvivingStrengthAbsolute, "maraudingMinSurvivingStrengthAbsolute", DefMaraudingMinSurvivingStrengthAbsolute);
            Scribe_Values.Look(ref maraudingMaxChainedTargets, "maraudingMaxChainedTargets", DefMaraudingMaxChainedTargets);
            Scribe_Values.Look(ref experimentalSettlementAmbush, "experimentalSettlementAmbush", DefExperimentalSettlementAmbush);
            Scribe_Values.Look(ref settlementAmbushChancePct, "settlementAmbushChancePct", DefSettlementAmbushChancePct);
            Scribe_Values.Look(ref settlementAmbushMinStrengthRatio, "settlementAmbushMinStrengthRatio", DefSettlementAmbushMinStrengthRatio);
            Scribe_Values.Look(ref settlementAmbushWatchRangeTiles, "settlementAmbushWatchRangeTiles", DefSettlementAmbushWatchRangeTiles);
            Scribe_Values.Look(ref settlementAmbushMaxStrengthRatio, "settlementAmbushMaxStrengthRatio", DefSettlementAmbushMaxStrengthRatio);
            Scribe_Values.Look(ref settlementAmbushMinTier, "settlementAmbushMinTier", DefSettlementAmbushMinTier);
            Scribe_Values.Look(ref settlementAmbushMaxConcurrent, "settlementAmbushMaxConcurrent", DefSettlementAmbushMaxConcurrent);
            // Old Feature C defaults (0.9x / 7 tiles) were too eager. Migrate only if the player never moved the sliders.
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                if (Mathf.Approximately(settlementAmbushMinStrengthRatio, 0.9f))
                    settlementAmbushMinStrengthRatio = DefSettlementAmbushMinStrengthRatio;
                if (Mathf.Approximately(settlementAmbushWatchRangeTiles, 7f))
                    settlementAmbushWatchRangeTiles = DefSettlementAmbushWatchRangeTiles;
            }
            Scribe_Values.Look(ref opportunityFeaturesIgnoreEscalationGate, "opportunityFeaturesIgnoreEscalationGate", DefOpportunityFeaturesIgnoreEscalationGate);
            Scribe_Values.Look(ref experimentalOutpostWithdrawStrengthBudget, "experimentalOutpostWithdrawStrengthBudget", DefExperimentalOutpostWithdrawStrengthBudget);
            Scribe_Values.Look(ref experimentalOutpostDefenseDeployBudget, "experimentalOutpostDefenseDeployBudget", DefExperimentalOutpostDefenseDeployBudget);
            Scribe_Values.Look(ref experimentalAlwaysClearKcsgRect, "experimentalAlwaysClearKcsgRect", DefExperimentalAlwaysClearKcsgRect);
            Scribe_Values.Look(ref experimentalKcsgRectBlend, "experimentalKcsgRectBlend", DefExperimentalKcsgRectBlend);
            Scribe_Values.Look(ref enableWorldMapSounds, "enableWorldMapSounds", DefEnableWorldMapSounds);
            Scribe_Values.Look(ref atTurretLightMaxStrength, "atTurretLightMaxStrength", DefAtTurretLightMaxStrength);
            Scribe_Values.Look(ref atTurretMediumMaxStrength, "atTurretMediumMaxStrength", DefAtTurretMediumMaxStrength);
            Scribe_Values.Look(ref atTurretHeavyMaxStrength, "atTurretHeavyMaxStrength", DefAtTurretHeavyMaxStrength);
            Scribe_Values.Look(ref atTurretLightDamage, "atTurretLightDamage", DefAtTurretLightDamage);
            Scribe_Values.Look(ref atTurretDamage, "atTurretDamage", DefAtTurretDamage);
            Scribe_Values.Look(ref atTurretHeavyDamage, "atTurretHeavyDamage", DefAtTurretHeavyDamage);
            Scribe_Values.Look(ref atTurretLightCooldownDays, "atTurretLightCooldownDays", DefAtTurretLightCooldownDays);
            Scribe_Values.Look(ref atTurretCooldownDays, "atTurretCooldownDays", DefAtTurretCooldownDays);
            Scribe_Values.Look(ref atTurretHeavyCooldownDays, "atTurretHeavyCooldownDays", DefAtTurretHeavyCooldownDays);
            Scribe_Values.Look(ref atTurretLightRange, "atTurretLightRange", DefAtTurretLightRange);
            Scribe_Values.Look(ref atTurretMediumRange, "atTurretMediumRange", DefAtTurretMediumRange);
            Scribe_Values.Look(ref atTurretHeavyRange, "atTurretHeavyRange", DefAtTurretHeavyRange);
            Scribe_Values.Look(ref atTurretHitChance0To50PctRange, "atTurretHitChance0To50PctRange", DefAtTurretHitChance0To50PctRange);
            Scribe_Values.Look(ref atTurretHitChance51To75PctRange, "atTurretHitChance51To75PctRange", DefAtTurretHitChance51To75PctRange);
            Scribe_Values.Look(ref atTurretHitChance76To100PctRange, "atTurretHitChance76To100PctRange", DefAtTurretHitChance76To100PctRange);
            Scribe_Values.Look(ref atTurretLightWork, "atTurretLightWork", DefAtTurretLightWork);
            Scribe_Values.Look(ref atTurretMediumWork, "atTurretMediumWork", DefAtTurretMediumWork);
            Scribe_Values.Look(ref atTurretHeavyWork, "atTurretHeavyWork", DefAtTurretHeavyWork);
            Scribe_Values.Look(ref atTurretLightMinConstruction, "atTurretLightMinConstruction", DefAtTurretLightMinConstruction);
            Scribe_Values.Look(ref atTurretMediumMinConstruction, "atTurretMediumMinConstruction", DefAtTurretMediumMinConstruction);
            Scribe_Values.Look(ref atTurretHeavyMinConstruction, "atTurretHeavyMinConstruction", DefAtTurretHeavyMinConstruction);
            Scribe_Values.Look(ref atTurretLightExpeditionStrength, "atTurretLightExpeditionStrength", DefAtTurretLightExpeditionStrength);
            Scribe_Values.Look(ref atTurretMediumExpeditionStrength, "atTurretMediumExpeditionStrength", DefAtTurretMediumExpeditionStrength);
            Scribe_Values.Look(ref atTurretHeavyExpeditionStrength, "atTurretHeavyExpeditionStrength", DefAtTurretHeavyExpeditionStrength);
            Scribe_Values.Look(ref enableFirstOutpostQuest, "enableFirstOutpostQuest", DefEnableFirstOutpostQuest);
            Scribe_Values.Look(ref enableCommonEnemySettlementQuest, "enableCommonEnemySettlementQuest", DefEnableCommonEnemySettlementQuest);
            Scribe_Values.Look(ref enableColonyRoadLinkQuest, "enableColonyRoadLinkQuest", DefEnableColonyRoadLinkQuest);
            Scribe_Values.Look(ref enableWorldDominationVictoryQuest, "enableWorldDominationVictoryQuest", DefEnableWorldDominationVictoryQuest);
            Scribe_Values.Look(ref enableAtTurretTargetPlayerTravelers, "enableAtTurretTargetPlayerTravelers", DefEnableAtTurretTargetPlayerTravelers);
            Scribe_Values.Look(ref enableAtTurretTargetPlayerCaravans, "enableAtTurretTargetPlayerCaravans", DefEnableAtTurretTargetPlayerCaravans);
            Scribe_Values.Look(ref enableOutpostUpkeep, "enableOutpostUpkeep", DefEnableOutpostUpkeep);
            Scribe_Values.Look(ref giveFoodOnPrisonerRecruitTransfer, "giveFoodOnPrisonerRecruitTransfer", DefGiveFoodOnPrisonerRecruitTransfer);
            Scribe_Values.Look(ref giveFoodOnAllPlayerPawnsTransfer, "giveFoodOnAllPlayerPawnsTransfer", DefGiveFoodOnAllPlayerPawnsTransfer);
            Scribe_Values.Look(ref showOutpostRequirementsPreviewInWdMenu, "showOutpostRequirementsPreviewInWdMenu", DefShowOutpostRequirementsPreviewInWdMenu);
            Scribe_Values.Look(ref upkeepSilverPerOccupant, "upkeepSilverPerOccupant", DefUpkeepSilverPerOccupant);
            Scribe_Values.Look(ref upkeepIntervalDays, "upkeepIntervalDays", DefUpkeepIntervalDays);

            Scribe_Values.Look(ref garrisonRetainPct, "garrisonRetainPct", DefGarrisonRetainPct);
            Scribe_Values.Look(ref dropPodRaidChanceT3, "dropPodRaidChanceT3", DefDropPodRaidChanceT3);
            Scribe_Values.Look(ref dropPodRaidChance, "dropPodRaidChance", DefDropPodRaidChance);
            Scribe_Values.Look(ref dropPodRaidMinTechLevel, "dropPodRaidMinTechLevel", DefDropPodRaidMinTechLevel);
            Scribe_Values.Look(ref dropPodRaidAttritionMult, "dropPodRaidAttritionMult", DefDropPodRaidAttritionMult);
            Scribe_Values.Look(ref colonySiegeRaidChance, "colonySiegeRaidChance", DefColonySiegeRaidChance);

            Scribe_Values.Look(ref weightSabSuccess, "weightSabSuccess", DefWeightSabSuccess);
            Scribe_Values.Look(ref weightSabCleanFail, "weightSabCleanFail", DefWeightSabCleanFail);
            Scribe_Values.Look(ref weightSabInjuredFail, "weightSabInjuredFail", DefWeightSabInjuredFail);
            Scribe_Values.Look(ref weightSabFatalFail, "weightSabFatalFail", DefWeightSabFatalFail);
            Scribe_Values.Look(ref sabotageSkillSuccessWeightBonus, "sabotageSkillSuccessWeightBonus", DefSabSkillSuccessWeightBonus);
            Scribe_Values.Look(ref sabotageTierSuccessWeightPenalty, "sabotageTierSuccessWeightPenalty", DefSabTierSuccessWeightPenalty);
            Scribe_Values.Look(ref sabotageHealthImpactWeight, "sabotageHealthImpactWeight", DefSabHealthImpactWeight);
            Scribe_Values.Look(ref sabotageSocialCleanBonus, "sabotageSocialCleanBonus", DefSabSocialCleanBonus);
            Scribe_Values.Look(ref sabotageCombatSurvivalBonus, "sabotageCombatSurvivalBonus", DefSabCombatSurvivalBonus);
            Scribe_Values.Look(ref sabotageBaseReduction, "sabotageBaseReduction", DefSabBaseReduc);
            Scribe_Values.Look(ref sabotageSkillReductionBonus, "sabotageSkillReductionBonus", DefSabSkillReductionBonus);
            Scribe_Values.Look(ref sabotageCooldownDays, "sabotageCooldownDays", DefSabCdDays);

            Scribe_Values.Look(ref weightDisSuccess, "weightDisSuccess", DefWeightDisSuccess);
            Scribe_Values.Look(ref weightDisCleanFail, "weightDisCleanFail", DefWeightDisCleanFail);
            Scribe_Values.Look(ref weightDisInjuredFail, "weightDisInjuredFail", DefWeightDisInjuredFail);
            Scribe_Values.Look(ref weightDisFatalFail, "weightDisFatalFail", DefWeightDisFatalFail);
            Scribe_Values.Look(ref disSkillSuccessWeightBonus, "disSkillSuccessWeightBonus", DefDisSkillSuccessWeightBonus);
            Scribe_Values.Look(ref disTierSuccessWeightPenalty, "disTierSuccessWeightPenalty", DefDisTierSuccessWeightPenalty);
            Scribe_Values.Look(ref disHealthImpactWeight, "disHealthImpactWeight", DefDisHealthImpactWeight);
            Scribe_Values.Look(ref disSocialCleanBonus, "disSocialCleanBonus", DefDisSocialCleanBonus);
            Scribe_Values.Look(ref disCombatSurvivalBonus, "disCombatSurvivalBonus", DefDisCombatSurvivalBonus);
            Scribe_Values.Look(ref disBaseReduction, "disBaseReduction", DefDisBaseReduc);
            Scribe_Values.Look(ref disSkillReductionBonus, "disSkillReductionBonus", DefDisSkillReductionBonus);
            Scribe_Values.Look(ref disCooldownDays, "disCooldownDays", DefDisCdDays);

            Scribe_Values.Look(ref outpostMinDistanceTiles, "outpostMinDistanceTiles", DefOutpostMinDistanceTiles);
            Scribe_Values.Look(ref traderDestinationSearchRadius, "traderDestinationSearchRadius", DefTraderDestinationSearchRadius);
            Scribe_Values.Look(ref outpostBuildCostMultiplier, "outpostBuildCostMultiplier", DefOutpostBuildCostMultiplier);
            Scribe_Values.Look(ref outpostDeliveryStrengthCost, "outpostDeliveryStrengthCost", DefOutpostDeliveryStrengthCost);
            Scribe_Values.Look(ref outpostDeliveryMinStrength, "outpostDeliveryMinStrength", DefOutpostDeliveryMinStrength);
            Scribe_Values.Look(ref outpostSilverValuePerSkillPerCycle, "outpostSilverValuePerSkillPerCycle", DefOutpostSilverValuePerSkillPerCycle);
            Scribe_Values.Look(ref outpostProductionTimeMultiplier, "outpostProductionTimeMultiplier", DefOutpostProductionTimeMultiplier);
            Scribe_Values.Look(ref outpostProductionOutputMultiplier, "outpostProductionOutputMultiplier", DefOutpostProductionOutputMultiplier);
            Scribe_Values.Look(ref warehouseAuraBonusPct, "warehouseAuraBonusPct", DefWarehouseAuraBonusPct);
            Scribe_Values.Look(ref warehouseAuraRadiusTiles, "warehouseAuraRadiusTiles", DefWarehouseAuraRadiusTiles);
            Scribe_Values.Look(ref embassyMayGainGoodwillWithHostiles, "embassyMayGainGoodwillWithHostiles", DefEmbassyMayGainGoodwillWithHostiles);
            Scribe_Values.Look(ref clampOutpostSkillsAtLevel20, "clampOutpostSkillsAtLevel20", DefClampOutpostSkillsAtLevel20);
            Scribe_Values.Look(ref enableOutpostSkillDiminishingReturns, "enableOutpostSkillDiminishingReturns", OutpostSkillScaling.DefEnableDiminishingReturns);
            Scribe_Values.Look(ref outpostSkillHardCapRaw, "outpostSkillHardCapRaw", OutpostSkillScaling.DefHardCapRaw);
            if (Scribe.mode == LoadSaveMode.Saving || Scribe.mode == LoadSaveMode.LoadingVars)
            {
                OutpostSkillScaling.EnsureArrays(this);
                if (outpostSkillBandEnds != null)
                {
                    for (int i = 0; i < OutpostSkillScaling.BandCount; i++)
                    {
                        float end = outpostSkillBandEnds[i];
                        float weight = outpostSkillBandWeights[i];
                        Scribe_Values.Look(ref end, "outpostSkillBandEnd" + i, OutpostSkillScaling.DefBandEnds[i]);
                        Scribe_Values.Look(ref weight, "outpostSkillBandWeight" + i, OutpostSkillScaling.DefBandWeights[i]);
                        outpostSkillBandEnds[i] = end;
                        outpostSkillBandWeights[i] = weight;
                    }
                }
            }
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                OutpostSkillScaling.NormalizeBands(this);
            Scribe_Values.Look(ref outpostOccupantSkillXpPerProductionCycle, "outpostOccupantSkillXpPerProductionCycle", DefOutpostOccupantSkillXpPerProductionCycle);
            Scribe_Values.Look(ref outpostOccupantSkillXpMaxLevel, "outpostOccupantSkillXpMaxLevel", DefOutpostOccupantSkillXpMaxLevel);
            Scribe_Values.Look(ref academyBaseXpPerDay, "academyBaseXpPerDay", DefAcademyBaseXpPerDay);
            Scribe_Values.Look(ref academyMinTeacherSkill, "academyMinTeacherSkill", DefAcademyMinTeacherSkill);
            Scribe_Values.Look(ref academyTeachCapOffset, "academyTeachCapOffset", DefAcademyTeachCapOffset);
            Scribe_Values.Look(ref academyUseFlatDirectXp, "academyUseFlatDirectXp", DefAcademyUseFlatDirectXp);
            Scribe_Values.Look(ref outpostUpgradesCostMaterials, "outpostUpgradesCostMaterials", DefOutpostUpgradesCostMaterials);
            Scribe_Values.Look(ref outpostUpgradesRequireResearch, "outpostUpgradesRequireResearch", DefOutpostUpgradesRequireResearch);
            Scribe_Values.Look(ref enableOutpostLaunchAttack, "enableOutpostLaunchAttack", DefEnableOutpostLaunchAttack);
            Scribe_Values.Look(ref enableOutpostBuildRoads, "enableOutpostBuildRoads", DefEnableOutpostBuildRoads);
            Scribe_Values.Look(ref enableOutpostBuildRoadBlocks, "enableOutpostBuildRoadBlocks", DefEnableOutpostBuildRoadBlocks);
            Scribe_Values.Look(ref enableOutpostBuildTraps, "enableOutpostBuildTraps", DefEnableOutpostBuildTraps);
            // Establishment requirement toggles
            Scribe_Values.Look(ref outpostReqBiome, "outpostReqBiome", DefOutpostReqBiome);
            Scribe_Values.Look(ref outpostReqFertility, "outpostReqFertility", DefOutpostReqFertility);
            Scribe_Values.Look(ref outpostReqAnimalAbundance, "outpostReqAnimalAbundance", DefOutpostReqAnimalAbundance);
            Scribe_Values.Look(ref outpostReqFishAbundance, "outpostReqFishAbundance", DefOutpostReqFishAbundance);
            Scribe_Values.Look(ref outpostReqMiningTerrain, "outpostReqMiningTerrain", DefOutpostReqMiningTerrain);
            Scribe_Values.Look(ref outpostReqResearch, "outpostReqResearch", DefOutpostReqResearch);
            Scribe_Values.Look(ref outpostReqNearbySettlements, "outpostReqNearbySettlements", DefOutpostReqNearbySettlements);
            Scribe_Values.Look(ref outpostReqMinPawns, "outpostReqMinPawns", DefOutpostReqMinPawns);
            Scribe_Values.Look(ref outpostReqMinSkill, "outpostReqMinSkill", DefOutpostReqMinSkill);
            Scribe_Values.Look(ref outpostReqCost, "outpostReqCost", DefOutpostReqCost);
            Scribe_Values.Look(ref pollutionEcologyPenaltyEnabled, "pollutionEcologyPenaltyEnabled", DefPollutionEcologyPenaltyEnabled);
            Scribe_Values.Look(ref travelerPollutionDamageEnabled, "travelerPollutionDamageEnabled", DefTravelerPollutionDamageEnabled);
            Scribe_Values.Look(ref wasterPollutionImmunityEnabled, "wasterPollutionImmunityEnabled", DefWasterPollutionImmunityEnabled);
            Scribe_Values.Look(ref pollutionDamageRaiders, "pollutionDamageRaiders", DefPollutionDamageRaiders);
            Scribe_Values.Look(ref pollutionDamageExpansion, "pollutionDamageExpansion", DefPollutionDamageExpansion);
            Scribe_Values.Look(ref pollutionDamageConstruction, "pollutionDamageConstruction", DefPollutionDamageConstruction);
            Scribe_Values.Look(ref pollutionDamageTraders, "pollutionDamageTraders", DefPollutionDamageTraders);
            Scribe_Values.Look(ref pollutionDamagePlayerTravelers, "pollutionDamagePlayerTravelers", DefPollutionDamagePlayerTravelers);
            Scribe_Values.Look(ref pollutionPathCostEnabled, "pollutionPathCostEnabled", DefPollutionPathCostEnabled);
            Scribe_Values.Look(ref pollutionPathRepathEnabled, "pollutionPathRepathEnabled", DefPollutionPathRepathEnabled);
            Scribe_Values.Look(ref pollutionPathPreCommitCancelEnabled, "pollutionPathPreCommitCancelEnabled", DefPollutionPathPreCommitCancelEnabled);
            Scribe_Values.Look(ref pollutionDamageIgnoreBelow, "pollutionDamageIgnoreBelow", DefPollutionDamageIgnoreBelow);
            Scribe_Values.Look(ref pollutionDamageAtThreshold, "pollutionDamageAtThreshold", DefPollutionDamageAtThreshold);
            Scribe_Values.Look(ref pollutionDamageAtFull, "pollutionDamageAtFull", DefPollutionDamageAtFull);
            Scribe_Values.Look(ref pollutionDamageRadius, "pollutionDamageRadius", DefPollutionDamageRadius);
            Scribe_Values.Look(ref npcSettlementDecontaminationStrengthCost, "npcSettlementDecontaminationStrengthCost", DefNpcSettlementDecontaminationStrengthCost);
            Scribe_Values.Look(ref outpostDefensiveRecoveryMinFlatPerDay, "outpostDefensiveRecoveryMinFlatPerDay", DefOutpostDefensiveRecoveryMinFlatPerDay);
            Scribe_Values.Look(ref outpostDefensiveRecoveryFractionPerDay, "outpostDefensiveRecoveryFractionPerDay", DefOutpostDefensiveRecoveryFractionPerDay);
            Scribe_Values.Look(ref outpostOffensiveRecoveryMinFlatPerDay, "outpostOffensiveRecoveryMinFlatPerDay", DefOutpostOffensiveRecoveryMinFlatPerDay);
            Scribe_Values.Look(ref outpostOffensiveRecoveryFractionPerDay, "outpostOffensiveRecoveryFractionPerDay", DefOutpostOffensiveRecoveryFractionPerDay);
            Scribe_Values.Look(ref outpostOccupantHealSeverityPerDay, "outpostOccupantHealSeverityPerDay", DefOutpostOccupantHealSeverityPerDay);
            Scribe_Values.Look(ref expertStrategistMaxBonusPct, "expertStrategistMaxBonusPct", DefExpertStrategistMaxBonusPct);
            Scribe_Values.Look(ref expertEntertainerMaxBonusPct, "expertEntertainerMaxBonusPct", DefExpertEntertainerMaxBonusPct);
            Scribe_Values.Look(ref expertCookMaxBonusPct, "expertCookMaxBonusPct", DefExpertCookMaxBonusPct);
            Scribe_Values.Look(ref expertDoctorMaxBonusPct, "expertDoctorMaxBonusPct", DefExpertDoctorMaxBonusPct);
            Scribe_Values.Look(ref expertEngineerMaxBonusPct, "expertEngineerMaxBonusPct", DefExpertEngineerMaxBonusPct);
            Scribe_Values.Look(ref expertEngineerConstructionRadiusMaxBonusPct, "expertEngineerConstructionRadiusMaxBonusPct", DefExpertEngineerConstructionRadiusMaxBonusPct);
            Scribe_Values.Look(ref expertRecruiterMaxBonusPct, "expertRecruiterMaxBonusPct", DefExpertRecruiterMaxBonusPct);
            Scribe_Values.Look(ref expertReferenceSkillLevel, "expertReferenceSkillLevel", DefExpertReferenceSkillLevel);
            Scribe_Values.Look(ref cooldownPlayerOutpostRaidDays, "cooldownPlayerOutpostRaidDays", DefCooldownPlayerOutpostRaidDays);
            Scribe_Values.Look(ref outpostAfterConquestEnabled, "outpostAfterConquestEnabled", DefOutpostAfterConquestEnabled);
            Scribe_Values.Look(ref conquestFoundingPawnsT1, "conquestFoundingPawnsT1", DefConquestFoundingPawnsT1);
            Scribe_Values.Look(ref conquestFoundingPawnsT2, "conquestFoundingPawnsT2", DefConquestFoundingPawnsT2);
            Scribe_Values.Look(ref conquestFoundingPawnsT3, "conquestFoundingPawnsT3", DefConquestFoundingPawnsT3);
            Scribe_Values.Look(ref conquestFoundingPawnsT4, "conquestFoundingPawnsT4", DefConquestFoundingPawnsT4);
            Scribe_Values.Look(ref conquestFoundingMinRelevantSkill, "conquestFoundingMinRelevantSkill", DefConquestFoundingMinRelevantSkill);
            ScribeMiningBaselineMultipliers(ref miningBaselineMultiplierByDefName);

            Scribe_Values.Look(ref foodLogisticsActive, "foodLogisticsActive", DefFoodLogisticsActive);
            Scribe_Values.Look(ref foodConsumptionPerPawn, "foodConsumptionPerPawn", DefFoodConsumptionPerPawn);
            Scribe_Values.Look(ref foodProductionPerSkill, "foodProductionPerSkill", DefFoodProductionPerSkill);
            Scribe_Values.Look(ref foodProductionPerOutpostBase, "foodProductionPerOutpostBase", DefFoodProductionPerOutpostBase);
            Scribe_Values.Look(ref maxFoodPerOutpost, "maxFoodPerOutpost", DefMaxFoodPerOutpost);
            Scribe_Values.Look(ref maxLogisticsRange, "maxLogisticsRange", DefMaxLogisticsRange);
            Scribe_Values.Look(ref virtualFoodTileMultiplierFloor, "virtualFoodTileMultiplierFloor", DefVirtualFoodTileMultiplierFloor);

            Scribe_Values.Look(ref notifyNewSettlement, "notifyNewSettlement", DefNotifyNewSettlement);
            Scribe_Values.Look(ref notifyNpcConquestSettlement, "notifyNpcConquestSettlement", DefNotifyNpcConquestSettlement);
            Scribe_Values.Look(ref notifySettlementRaided, "notifySettlementRaided", DefNotifySettlementRaided);
            Scribe_Values.Look(ref notifySettlementRazed, "notifySettlementRazed", DefNotifySettlementRazed);
            Scribe_Values.Look(ref notifyOutpostDestroyed, "notifyOutpostDestroyed", DefNotifyOutpostDestroyed);
            Scribe_Values.Look(ref notifyThreatLevel, "notifyThreatLevel", DefNotifyThreatLevel);
            Scribe_Values.Look(ref notifyCriticalFood, "notifyCriticalFood", DefNotifyCriticalFood);
            Scribe_Values.Look(ref notifyDropPodDeliveryInAaRange, "notifyDropPodDeliveryInAaRange", DefNotifyDropPodDeliveryInAaRange);
            Scribe_Values.Look(ref notifyOutpostUpkeep, "notifyOutpostUpkeep", DefNotifyOutpostUpkeep);
            Scribe_Values.Look(ref notifyConstructionInsufficientStrength, "notifyConstructionInsufficientStrength", DefNotifyConstructionInsufficientStrength);
            Scribe_Values.Look(ref notifyOutpostNoProduction, "notifyOutpostNoProduction", DefNotifyOutpostNoProduction);
            Scribe_Values.Look(ref notifyOutpostUnusedExperts, "notifyOutpostUnusedExperts", DefNotifyOutpostUnusedExperts);
            Scribe_Values.Look(ref notifyLateGameActive, "notifyLateGameActive", DefNotifyLateGameActive);
            Scribe_Values.Look(ref notifyMidGameActive, "notifyMidGameActive", DefNotifyMidGameActive);
            Scribe_Values.Look(ref notifyLeaderHandicap, "notifyLeaderHandicap", DefNotifyLeaderHandicap);
            Scribe_Values.Look(ref notifyUnderdogBuff, "notifyUnderdogBuff", DefNotifyUnderdogBuff);
            Scribe_Values.Look(ref notifyExpansionistZeal, "notifyExpansionistZeal", DefNotifyExpansionistZeal);
            Scribe_Values.Look(ref notifyAntiLeaderCoalition, "notifyAntiLeaderCoalition", DefNotifyAntiLeaderCoalition);
            Scribe_Values.Look(ref notifyRandomDiplomacy, "notifyRandomDiplomacy", DefNotifyRandomDiplomacy);
            Scribe_Values.Look(ref notifyTradeAllyDiplomacy, "notifyTradeAllyDiplomacy", DefNotifyTradeAllyDiplomacy);
            Scribe_Values.Look(ref notifyStrongFactionWar, "notifyStrongFactionWar", DefNotifyStrongFactionWar);
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                notifyBuffNerf = notifyLeaderHandicap || notifyUnderdogBuff || notifyExpansionistZeal;
                notifyDiplomaticChange = notifyAntiLeaderCoalition || notifyRandomDiplomacy;
            }
            Scribe_Values.Look(ref notifyDiplomaticChange, "notifyDiplomaticChange", DefNotifyDiplomaticChange);
            Scribe_Values.Look(ref notifyBuffNerf, "notifyBuffNerf", DefNotifyBuffNerf);
            Scribe_Values.Look(ref settingsDataVersion, "settingsDataVersion", 0);
            // SURGICAL: Expose Notifications
            Scribe_Values.Look(ref notifyIncomingRaidColony, "notifyIncomingRaidColony", DefNotifyIncomingRaidColony);
            Scribe_Values.Look(ref notifyIncomingRaidOutpost, "notifyIncomingRaidOutpost", DefNotifyIncomingRaidOutpost);
            Scribe_Values.Look(ref notifyRaidDivertedFromPlayer, "notifyRaidDivertedFromPlayer", DefNotifyRaidDivertedFromPlayer);
            Scribe_Values.Look(ref notifyMortarHit, "notifyMortarHit", DefNotifyMortarHit);
            Scribe_Values.Look(ref notifyAntiAirHit, "notifyAntiAirHit", DefNotifyAntiAirHit);
            Scribe_Values.Look(ref notifyPlayerAntiAirVsHostileMortarShell, "notifyPlayerAntiAirVsHostileMortarShell", DefNotifyPlayerAntiAirVsHostileMortarShell);
            Scribe_Values.Look(ref notifyNpcMortarHitPlayer, "notifyNpcMortarHitPlayer", DefNotifyNpcMortarHitPlayer);
            Scribe_Values.Look(ref notifyNpcMortarHitNpc, "notifyNpcMortarHitNpc", DefNotifyNpcMortarHitNpc);
            Scribe_Values.Look(ref notifyPlayerAtTurretKilledTarget, "notifyPlayerAtTurretKilledTarget", DefNotifyPlayerAtTurretKilledTarget);
            Scribe_Values.Look(ref notifyPlayerAtTurretDamagedTarget, "notifyPlayerAtTurretDamagedTarget", DefNotifyPlayerAtTurretDamagedTarget);
            Scribe_Values.Look(ref notifyPlayerAtTurretDestroyed, "notifyPlayerAtTurretDestroyed", DefNotifyPlayerAtTurretDestroyed);
            Scribe_Values.Look(ref notifyNpcAtTurretDamagedPlayer, "notifyNpcAtTurretDamagedPlayer", DefNotifyNpcAtTurretDamagedPlayer);
            Scribe_Values.Look(ref notifyNpcAtTurretKilledPlayer, "notifyNpcAtTurretKilledPlayer", DefNotifyNpcAtTurretKilledPlayer);
            Scribe_Values.Look(ref notifyWarehouseGoodsArrived, "notifyWarehouseGoodsArrived", DefNotifyWarehouseGoodsArrived);
            Scribe_Values.Look(ref notifyOutpostDeliveryToColonyArrived, "notifyOutpostDeliveryToColonyArrived", DefNotifyOutpostDeliveryToColonyArrived);
            Scribe_Values.Look(ref notifyPlayerCaravanClash, "notifyPlayerCaravanClash", DefNotifyPlayerCaravanClash);
            Scribe_Values.Look(ref showCaravanClashLootDialog, "showCaravanClashLootDialog", DefShowCaravanClashLootDialog);
            Scribe_Values.Look(ref notifyRapidResponseCaravanClash, "notifyRapidResponseCaravanClash", DefNotifyRapidResponseCaravanClash);
            Scribe_Values.Look(ref notifyTravelerPollutionDamage, "notifyTravelerPollutionDamage", DefNotifyTravelerPollutionDamage);
            Scribe_Values.Look(ref notifyOutpostPollutionDamage, "notifyOutpostPollutionDamage", DefNotifyOutpostPollutionDamage);
            Scribe_Values.Look(ref notifyPrisonerRecruitedUnderway, "notifyPrisonerRecruitedUnderway", DefNotifyPrisonerRecruitedUnderway);
            Scribe_Values.Look(ref alwaysShowOutpostTravelerIconsRegardlessOfZoom, "alwaysShowOutpostTravelerIconsRegardlessOfZoom", DefAlwaysShowOutpostTravelerIconsRegardlessOfZoom);
            Scribe_Values.Look(ref alwaysShowSettlementIconsRegardlessOfZoom, "alwaysShowSettlementIconsRegardlessOfZoom", DefAlwaysShowSettlementIconsRegardlessOfZoom);

            Scribe_Values.Look(ref genWeightT1, "genWeightT1", DefGenWeightT1);
            Scribe_Values.Look(ref genWeightT2, "genWeightT2", DefGenWeightT2);
            Scribe_Values.Look(ref genWeightT3, "genWeightT3", DefGenWeightT3);
            Scribe_Values.Look(ref genWeightT4, "genWeightT4", DefGenWeightT4);
            Scribe_Values.Look(ref settlementTerritoryCoherence, "settlementTerritoryCoherence", DefSettlementTerritoryCoherence);
            Scribe_Values.Look(ref settlementTerritorySpacing, "settlementTerritorySpacing", DefSettlementTerritorySpacing);
            Scribe_Values.Look(ref settlementOtherFactionDistance, "settlementOtherFactionDistance", DefSettlementOtherFactionDistance);
            Scribe_Values.Look(ref settlementMaxPerCluster, "settlementMaxPerCluster", DefSettlementMaxPerCluster);
            Scribe_Values.Look(ref settlementMinDistanceBetweenClusters, "settlementMinDistanceBetweenClusters", DefSettlementMinDistanceBetweenClusters);
            settlementMaxPerCluster = Mathf.Clamp(settlementMaxPerCluster, 1, 20);
            settlementMinDistanceBetweenClusters = Mathf.Clamp(settlementMinDistanceBetweenClusters, 0, 50);
            Scribe_Values.Look(ref worldSetupDestroyFortificationsOnRecreate, "worldSetupDestroyFortificationsOnRecreate", DefWorldSetupDestroyFortificationsOnRecreate);

            Scribe_Values.Look(ref allowWdSettlementBaseGeneration, "allowWdSettlementBaseGeneration", DefAllowWdSettlementBaseGeneration);
            Scribe_Values.Look(ref kcsgMultTribalT1, "kcsgMultTribalT1", DefKcsgMultTribalT1);
            Scribe_Values.Look(ref kcsgMultTribalT2, "kcsgMultTribalT2", DefKcsgMultTribalT2);
            Scribe_Values.Look(ref kcsgMultTribalT3, "kcsgMultTribalT3", DefKcsgMultTribalT3);
            Scribe_Values.Look(ref kcsgMultTribalT4, "kcsgMultTribalT4", DefKcsgMultTribalT4);
            Scribe_Values.Look(ref kcsgMultGenericT1, "kcsgMultGenericT1", DefKcsgMultGenericT1);
            Scribe_Values.Look(ref kcsgMultGenericT2, "kcsgMultGenericT2", DefKcsgMultGenericT2);
            Scribe_Values.Look(ref kcsgMultGenericT3, "kcsgMultGenericT3", DefKcsgMultGenericT3);
            Scribe_Values.Look(ref kcsgMultGenericT4, "kcsgMultGenericT4", DefKcsgMultGenericT4);
            Scribe_Values.Look(ref garrisonOffensiveStrengthMinScale, "garrisonOffensiveStrengthMinScale", DefGarrisonOffensiveStrengthMinScale);
            Scribe_Values.Look(ref kcsgAdaptiveTerrainPrep, "kcsgAdaptiveTerrainPrep", DefKcsgAdaptiveTerrainPrep);
            Scribe_Values.Look(ref kcsgBlockedFlattenThreshold, "kcsgBlockedFlattenThreshold", DefKcsgBlockedFlattenThreshold);

            Scribe_Values.Look(ref noGoodwillFromHostilesOnConquest, "noGoodwillFromHostilesOnConquest", DefNoGoodwillFromHostilesOnConquest);
            Scribe_Values.Look(ref disableSettlementProximityGoodwill, "disableSettlementProximityGoodwill", DefDisableSettlementProximityGoodwill);
            Scribe_Values.Look(ref blockStorytellerRaidsOnlyWD, "blockStorytellerRaidsOnlyWD", DefBlockStorytellerRaidsOnlyWD);
            Scribe_Values.Look(ref allowStorytellerRaidsFromNonWdFactions, "allowStorytellerRaidsFromNonWdFactions", DefAllowStorytellerRaidsFromNonWdFactions);
            Scribe_Values.Look(ref blockStorytellerTradersOnlyWD, "blockStorytellerTradersOnlyWD", DefBlockStorytellerTradersOnlyWD);
            Scribe_Values.Look(ref launchPodGiftStrengthPer100MarketValue, "launchPodGiftStrengthPer100MarketValue", DefLaunchPodGiftStrengthPer100MarketValue);
            Scribe_Values.Look(ref enableFactionSettlementInvestment, "enableFactionSettlementInvestment", DefEnableFactionSettlementInvestment);
            Scribe_Values.Look(ref factionInvestmentStrengthPer100Silver, "factionInvestmentStrengthPer100Silver", DefFactionInvestmentStrengthPer100Silver);
            Scribe_Values.Look(ref factionInvestmentRadiusTiles, "factionInvestmentRadiusTiles", DefFactionInvestmentRadiusTiles);
            Scribe_Values.Look(ref factionInvestmentUpgradeT1ToT2Silver, "factionInvestmentUpgradeT1ToT2Silver", DefFactionInvestmentUpgradeT1ToT2Silver);
            Scribe_Values.Look(ref factionInvestmentUpgradeT2ToT3Silver, "factionInvestmentUpgradeT2ToT3Silver", DefFactionInvestmentUpgradeT2ToT3Silver);
            Scribe_Values.Look(ref factionInvestmentUpgradeT3ToT4Silver, "factionInvestmentUpgradeT3ToT4Silver", DefFactionInvestmentUpgradeT3ToT4Silver);
            Scribe_Values.Look(ref factionInvestmentUpgradeSuccessChance, "factionInvestmentUpgradeSuccessChance", DefFactionInvestmentUpgradeSuccessChance);
            Scribe_Values.Look(ref goodwillFromTradeEnabled, "goodwillFromTradeEnabled", DefGoodwillFromTradeEnabled);
            Scribe_Values.Look(ref goodwillFromTradePer1000Silver, "goodwillFromTradePer1000Silver", DefGoodwillFromTradePer1000Silver);
            Scribe_Values.Look(ref maxGoodwill, "maxGoodwill", DefMaxGoodwill);
            Scribe_Values.Look(ref traderCaravanCostStrength, "traderCaravanCostStrength", DefTraderCaravanCostStrength);
            Scribe_Values.Look(ref traderCaravanSenderRewardStrength, "traderCaravanSenderRewardStrength", DefTraderCaravanSenderRewardStrength);
            Scribe_Values.Look(ref traderCaravanReceiverRewardStrength, "traderCaravanReceiverRewardStrength", DefTraderCaravanReceiverRewardStrength);
            Scribe_Values.Look(ref traderCaravanGoodwillGain, "traderCaravanGoodwillGain", DefTraderCaravanGoodwillGain);
            Scribe_Values.Look(ref cooldownPlayerColonyTraderDays, "cooldownPlayerColonyTraderDays", DefCooldownPlayerColonyTraderDays);
            Scribe_Values.Look(ref traderTierUpgradeChanceT1ToT2, "traderTierUpgradeChanceT1ToT2", DefTraderTierUpgradeChanceT1ToT2);
            Scribe_Values.Look(ref traderTierUpgradeChanceT2ToT3, "traderTierUpgradeChanceT2ToT3", DefTraderTierUpgradeChanceT2ToT3);
            Scribe_Values.Look(ref traderTierUpgradeChanceT3ToT4, "traderTierUpgradeChanceT3ToT4", DefTraderTierUpgradeChanceT3ToT4);
            Scribe_Values.Look(ref traderEscortFloorT1, "traderEscortFloorT1", DefTraderEscortFloorT1);
            Scribe_Values.Look(ref traderEscortFloorT2, "traderEscortFloorT2", DefTraderEscortFloorT2);
            Scribe_Values.Look(ref traderEscortFloorT3, "traderEscortFloorT3", DefTraderEscortFloorT3);
            Scribe_Values.Look(ref traderEscortFloorT4, "traderEscortFloorT4", DefTraderEscortFloorT4);
            Scribe_Values.Look(ref traderEscortRecentInterceptWindowDays, "traderEscortRecentInterceptWindowDays", DefTraderEscortRecentInterceptWindowDays);

            // Mortar / interception
            Scribe_Values.Look(ref mortarRange, "mortarRange", DefMortarRange);
            Scribe_Values.Look(ref cooldownMortarDays, "cooldownMortarDays", DefCooldownMortarDays);
            Scribe_Values.Look(ref mortarBaseMissChanceAtMaxRange, "mortarBaseMissChanceAtMaxRange", DefMortarBaseMissChanceAtMaxRange);
            Scribe_Values.Look(ref mortarHitPerSkillPoint, "mortarHitPerSkillPoint", DefMortarHitPerSkillPoint);
            Scribe_Values.Look(ref mortarHitChance0To50PctRange, "mortarHitChance0To50PctRange", DefMortarHitChance0To50PctRange);
            Scribe_Values.Look(ref mortarHitChance51To75PctRange, "mortarHitChance51To75PctRange", DefMortarHitChance51To75PctRange);
            Scribe_Values.Look(ref mortarHitChance76To100PctRange, "mortarHitChance76To100PctRange", DefMortarHitChance76To100PctRange);
            Scribe_Values.Look(ref npcMortarDamage, "npcMortarDamage", DefNpcMortarDamage);
            Scribe_Values.Look(ref npcMortarSkillEquivalent, "npcMortarSkillEquivalent", DefNpcMortarSkillEquivalent);
            Scribe_Values.Look(ref enableNpcT4Mortar, "enableNpcT4Mortar", DefEnableNpcT4Mortar);
            Scribe_Values.Look(ref npcT4MortarMinTechLevel, "npcT4MortarMinTechLevel", DefNpcT4MortarMinTechLevel);
            Scribe_Values.Look(ref npcMortarRange, "npcMortarRange", DefNpcMortarRange);
            Scribe_Values.Look(ref npcMortarCooldownDays, "npcMortarCooldownDays", DefNpcMortarCooldownDays);
            Scribe_Values.Look(ref npcMortarHitChance0To50PctRange, "npcMortarHitChance0To50PctRange", DefNpcMortarHitChance0To50PctRange);
            Scribe_Values.Look(ref npcMortarHitChance51To75PctRange, "npcMortarHitChance51To75PctRange", DefNpcMortarHitChance51To75PctRange);
            Scribe_Values.Look(ref npcMortarHitChance76To100PctRange, "npcMortarHitChance76To100PctRange", DefNpcMortarHitChance76To100PctRange);
            Scribe_Values.Look(ref interceptionScanIntervalTicks, "interceptionScanIntervalTicks", DefInterceptionScanIntervalTicks);
            Scribe_Values.Look(ref mortarDamagePerSkillPoint, "mortarDamagePerSkillPoint", DefMortarDamagePerSkillPoint);
            Scribe_Values.Look(ref mortarBaseShellDamage, "mortarBaseShellDamage", DefMortarBaseShellDamage);
            Scribe_Values.Look(ref mortarShellTicksPerMove, "mortarShellTicksPerMove", DefMortarShellTicksPerMove);
            Scribe_Values.Look(ref antiAirBaseDamage, "antiAirBaseDamage", DefAntiAirBaseDamage);
            Scribe_Values.Look(ref cooldownAntiAirSeconds, "cooldownAntiAirSeconds", DefCooldownAntiAirSeconds);
            Scribe_Values.Look(ref antiAirCooldownFloorSeconds, "antiAirCooldownFloorSeconds", DefAntiAirCooldownFloorSeconds);
            Scribe_Values.Look(ref antiAirRange, "antiAirRange", DefAntiAirRange);
            Scribe_Values.Look(ref antiAirHitChance0To50PctRange, "antiAirHitChance0To50PctRange", DefAntiAirHitChance0To50PctRange);
            Scribe_Values.Look(ref antiAirHitChance51To75PctRange, "antiAirHitChance51To75PctRange", DefAntiAirHitChance51To75PctRange);
            Scribe_Values.Look(ref antiAirHitChance76To100PctRange, "antiAirHitChance76To100PctRange", DefAntiAirHitChance76To100PctRange);
            Scribe_Values.Look(ref antiAirVsMortarHitChance, "antiAirVsMortarHitChance", DefAntiAirVsMortarHitChance);
            Scribe_Values.Look(ref flakShellTicksPerMove, "flakShellTicksPerMove", DefFlakShellTicksPerMove);
            Scribe_Values.Look(ref enableNpcT4AntiAir, "enableNpcT4AntiAir", DefEnableNpcT4AntiAir);
            Scribe_Values.Look(ref enableT4SettlementAntiAir, "enableT4SettlementAntiAir", DefEnableT4SettlementAntiAir);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                NormalizeEscalationConstraints();
            Scribe_Values.Look(ref npcAntiAirRange, "npcAntiAirRange", DefNpcAntiAirRange);
            Scribe_Values.Look(ref npcAntiAirCooldownSeconds, "npcAntiAirCooldownSeconds", DefNpcAntiAirCooldownSeconds);
            Scribe_Values.Look(ref npcAntiAirDamage, "npcAntiAirDamage", DefNpcAntiAirDamage);
            Scribe_Values.Look(ref npcAntiAirSkillEquivalent, "npcAntiAirSkillEquivalent", DefNpcAntiAirSkillEquivalent);
            Scribe_Values.Look(ref npcAntiAirHitChance0To50PctRange, "npcAntiAirHitChance0To50PctRange", DefNpcAntiAirHitChance0To50PctRange);
            Scribe_Values.Look(ref npcAntiAirHitChance51To75PctRange, "npcAntiAirHitChance51To75PctRange", DefNpcAntiAirHitChance51To75PctRange);
            Scribe_Values.Look(ref npcAntiAirHitChance76To100PctRange, "npcAntiAirHitChance76To100PctRange", DefNpcAntiAirHitChance76To100PctRange);
            Scribe_Values.Look(ref npcAntiAirVsMortarHitChance, "npcAntiAirVsMortarHitChance", DefNpcAntiAirVsMortarHitChance);
            Scribe_Values.Look(ref notifyT4AntiAirHitPlayer, "notifyT4AntiAirHitPlayer", DefNotifyT4AntiAirHitPlayer);
            Scribe_Values.Look(ref notifyPlayerMortarShellShotDown, "notifyPlayerMortarShellShotDown", DefNotifyPlayerMortarShellShotDown);
            Scribe_Values.Look(ref rapidResponseOffensiveStrengthBonus, "rapidResponseOffensiveStrengthBonus", DefRapidResponseOffensiveStrengthBonus);
            Scribe_Values.Look(ref rapidResponseOffensiveRecoveryBonus, "rapidResponseOffensiveRecoveryBonus", DefRapidResponseOffensiveRecoveryBonus);
            Scribe_Values.Look(ref rapidResponseTicksPerMoveMultiplier, "rapidResponseTicksPerMoveMultiplier", DefRapidResponseTicksPerMoveMultiplier);
            Scribe_Values.Look(ref rapidResponseAutoInterceptRange, "rapidResponseAutoInterceptRange", DefRapidResponseAutoInterceptRange);
            Scribe_Values.Look(ref rapidResponseDropPodRange, "rapidResponseDropPodRange", DefRapidResponseDropPodRange);
            Scribe_Values.Look(ref dropPodTicksPerMove, "dropPodTicksPerMove", DefDropPodTicksPerMove);

            // Meta / UX state
            Scribe_Values.Look(ref showUpdatePopups, "showUpdatePopups", DefShowUpdatePopups);
            Scribe_Values.Look(ref verboseLogging, "verboseLogging", DefVerboseLogging);
            Scribe_Values.Look(ref worldMapOverlayHoldKey, "worldMapOverlayHoldKey", DefWorldMapOverlayHoldKey);
            if (Scribe.mode == LoadSaveMode.LoadingVars
                && (worldMapOverlayHoldKey == KeyCode.None || worldMapOverlayHoldKey == KeyCode.Alpha1
                    || worldMapOverlayHoldKey == KeyCode.Alpha2 || worldMapOverlayHoldKey == KeyCode.Alpha3
                    || worldMapOverlayHoldKey == KeyCode.Alpha4))
                worldMapOverlayHoldKey = DefWorldMapOverlayHoldKey;
            Scribe_Values.Look(ref lastSeenReleaseNotesVersion, "lastSeenReleaseNotesVersion", string.Empty);

            Scribe_Values.Look(ref initialAllegianceLockDone, "initialAllegianceLockDone", false);
            Scribe_Collections.Look(ref lockedAllegiancePairs, "lockedAllegiancePairs", LookMode.Value);

            if (raidOutcomes == null || raidOutcomes.Count == 0) InitializeDefaults();

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                ForceFixedThresholds();

            if (Scribe.mode == LoadSaveMode.LoadingVars && settingsDataVersion < CurrentSettingsDataVersion)
            {
                if (settingsDataVersion < 1)
                    MigrateDiplomacyNotificationSettingsFromLegacy();
                if (settingsDataVersion < 2)
                    MigrateRaidMarginSettingsFromLegacy();
                if (settingsDataVersion < 3)
                    MigrateDecoupledRaidSettingsFromV2();
                if (settingsDataVersion < 4)
                    MigrateRaidLossTablesToWinLossV4();
                settingsDataVersion = CurrentSettingsDataVersion;
            }

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                EnsureRaidLossTablesInitialized();
                EnsureRaidOutcomeSeverities();
            }
        }

        public void EnsureRaidLossTablesInitialized()
        {
            if (raidAttLossOnWin == null || raidAttLossOnWin.Count < 3)
                raidAttLossOnWin = new List<RaidSideLossEntry>(RaidSeverityDefaults.DefaultAttWinLoss());
            if (raidAttLossOnLoss == null || raidAttLossOnLoss.Count < 3)
                raidAttLossOnLoss = new List<RaidSideLossEntry>(RaidSeverityDefaults.DefaultAttLossLoss());
            if (raidDefLossOnWin == null || raidDefLossOnWin.Count < 3)
                raidDefLossOnWin = new List<RaidSideLossEntry>(RaidSeverityDefaults.DefaultDefWinLoss());
            if (raidDefLossOnLoss == null || raidDefLossOnLoss.Count < 3)
                raidDefLossOnLoss = new List<RaidSideLossEntry>(RaidSeverityDefaults.DefaultDefLossLoss());
        }

        public float GetAttCasualtyLoss(BattleMarginTier tier, bool won)
        {
            EnsureRaidLossTablesInitialized();
            List<RaidSideLossEntry> table = won ? raidAttLossOnWin : raidAttLossOnLoss;
            int idx = RaidSeverityDefaults.TierToIndex(tier);
            if (idx >= 0 && idx < table.Count && table[idx] != null)
                return table[idx].lossPct;
            RaidSideLossEntry[] def = won ? RaidSeverityDefaults.DefaultAttWinLoss() : RaidSeverityDefaults.DefaultAttLossLoss();
            return def[Mathf.Clamp(idx, 0, 2)].lossPct;
        }

        public float GetDefCoalitionCasualtyLoss(BattleMarginTier tier, bool won)
        {
            EnsureRaidLossTablesInitialized();
            List<RaidSideLossEntry> table = won ? raidDefLossOnWin : raidDefLossOnLoss;
            int idx = RaidSeverityDefaults.TierToIndex(tier);
            if (idx >= 0 && idx < table.Count && table[idx] != null)
                return table[idx].lossPct;
            RaidSideLossEntry[] def = won ? RaidSeverityDefaults.DefaultDefWinLoss() : RaidSeverityDefaults.DefaultDefLossLoss();
            return def[Mathf.Clamp(idx, 0, 2)].lossPct;
        }

        private void EnsureRaidOutcomeSeverities()
        {
            if (raidOutcomes == null) return;
            foreach (var o in raidOutcomes)
            {
                if (o == null) continue;
                if (o.attSeverityOnAttWin == null)
                    o.attSeverityOnAttWin = RaidSeverityDefaults.AttSeverityOnAttWinAt(o.threshold);
                if (o.attSeverityOnAttLoss == null)
                    o.attSeverityOnAttLoss = RaidSeverityDefaults.AttSeverityOnAttLossAt(o.threshold);
                if (o.defCoalitionOnAttWin == null)
                    o.defCoalitionOnAttWin = RaidSeverityDefaults.DefCoalitionOnAttWinAt(o.threshold);
                if (o.defCoalitionOnAttLoss == null)
                    o.defCoalitionOnAttLoss = RaidSeverityDefaults.DefCoalitionOnAttLossAt(o.threshold);
            }
        }

        private void MigrateRaidMarginSettingsFromLegacy()
        {
            EnsureRaidLossTablesInitialized();
            if (raidOutcomes != null)
            {
                foreach (var o in raidOutcomes)
                {
                    if (o == null) continue;
                    EnsureRaidOutcomeSeverities();
                }
            }
        }

        private void MigrateDecoupledRaidSettingsFromV2()
        {
            // v3 stored two tier-only loss tables (raidAttCasualties/raidDefCoalitionCasualties). The v4 model
            // replaces them with four (side x won?) tables whose tier semantics are inverted for the losing side,
            // so old values cannot be mapped 1:1 and are reseeded from the correct v4 defaults below.
            _pendingLegacyRaidCasualties = null;
            EnsureRaidLossTablesInitialized();

            if (raidAllyLossMultiplier <= 0f)
                raidAllyLossMultiplier = DefRaidAllyLossMultiplier;

            if (raidOutcomes != null)
            {
                foreach (var o in raidOutcomes)
                {
                    if (o == null) continue;
                    if (o.attSeverityOnAttLoss == null)
                        o.attSeverityOnAttLoss = RaidSeverityDefaults.AttSeverityOnAttLossAt(o.threshold);
                    if (o.defCoalitionOnAttWin == null)
                        o.defCoalitionOnAttWin = RaidSeverityDefaults.DefCoalitionOnAttWinAt(o.threshold);
                }
            }
        }

        private void MigrateRaidLossTablesToWinLossV4()
        {
            // Loss-table semantics changed (single tier->loss table split into win/loss variants with a
            // consistent tier meaning). Reseed the four tables from the v4 defaults for any pre-v4 save.
            raidAttLossOnWin = new List<RaidSideLossEntry>(RaidSeverityDefaults.DefaultAttWinLoss());
            raidAttLossOnLoss = new List<RaidSideLossEntry>(RaidSeverityDefaults.DefaultAttLossLoss());
            raidDefLossOnWin = new List<RaidSideLossEntry>(RaidSeverityDefaults.DefaultDefWinLoss());
            raidDefLossOnLoss = new List<RaidSideLossEntry>(RaidSeverityDefaults.DefaultDefLossLoss());
        }

        private void MigrateDiplomacyNotificationSettingsFromLegacy()
        {
            bool legacyBuff = notifyBuffNerf;
            bool legacyDiplo = notifyDiplomaticChange;
            notifyLeaderHandicap = legacyBuff;
            notifyUnderdogBuff = false;
            notifyExpansionistZeal = true;
            notifyAntiLeaderCoalition = legacyDiplo;
            notifyRandomDiplomacy = legacyDiplo;
        }

        private void ForceFixedThresholds()
        {
            float[] thresholds = { 0.00f, 0.10f, 0.25f, 0.50f, 1.00f, 1.50f, 2.00f, 3.00f, 4.00f, 5.00f, 6.00f };
            if (raidOutcomes == null || raidOutcomes.Count == 0)
            {
                ResetRaids();
                return;
            }

            List<RaidOutcome> existing = raidOutcomes
                .Where(o => o != null)
                .OrderBy(o => o.threshold)
                .ToList();

            List<RaidOutcome> fixedOutcomes = new List<RaidOutcome>();
            foreach (float threshold in thresholds)
            {
                RaidOutcome exact = existing.FirstOrDefault(o => Mathf.Abs(o.threshold - threshold) < 0.001f);
                RaidOutcome outcome = exact != null ? CopyRaidOutcome(exact) : InterpolateRaidOutcome(existing, threshold);
                outcome.threshold = threshold;
                fixedOutcomes.Add(outcome);
            }

            raidOutcomes = fixedOutcomes;
            InvalidateRaidOutcomesCache();
        }

        private static RaidOutcome CopyRaidOutcome(RaidOutcome source)
        {
            if (source == null) return new RaidOutcome();
            return new RaidOutcome
            {
                threshold = source.threshold,
                winChance = source.winChance,
                attLossWin = source.attLossWin,
                defLossLoss = source.defLossLoss,
                defLossWin = source.defLossWin,
                attSeverityOnAttWin = source.attSeverityOnAttWin?.Copy() ?? RaidSeverityDefaults.AttSeverityOnAttWinAt(source.threshold),
                attSeverityOnAttLoss = source.attSeverityOnAttLoss?.Copy() ?? RaidSeverityDefaults.AttSeverityOnAttLossAt(source.threshold),
                defCoalitionOnAttWin = source.defCoalitionOnAttWin?.Copy() ?? RaidSeverityDefaults.DefCoalitionOnAttWinAt(source.threshold),
                defCoalitionOnAttLoss = source.defCoalitionOnAttLoss?.Copy() ?? RaidSeverityDefaults.DefCoalitionOnAttLossAt(source.threshold)
            };
        }

        private static RaidOutcome InterpolateRaidOutcome(List<RaidOutcome> existing, float threshold)
        {
            if (existing == null || existing.Count == 0)
                return DefaultRaidOutcomeAt(threshold);

            RaidOutcome lower = existing[0];
            RaidOutcome upper = existing[existing.Count - 1];

            for (int i = 0; i < existing.Count - 1; i++)
            {
                if (threshold >= existing[i].threshold && threshold <= existing[i + 1].threshold)
                {
                    lower = existing[i];
                    upper = existing[i + 1];
                    break;
                }
            }

            if (threshold <= existing[0].threshold)
                lower = upper = existing[0];
            else if (threshold >= existing[existing.Count - 1].threshold)
                lower = upper = existing[existing.Count - 1];

            float span = upper.threshold - lower.threshold;
            float t = span > 0.0001f ? Mathf.Clamp01((threshold - lower.threshold) / span) : 0f;
            RaidOutcome interpolated = new RaidOutcome
            {
                threshold = threshold,
                winChance = Mathf.Lerp(lower.winChance, upper.winChance, t),
                attLossWin = Mathf.Lerp(lower.attLossWin, upper.attLossWin, t),
                defLossLoss = Mathf.Lerp(lower.defLossLoss, upper.defLossLoss, t),
                defLossWin = Mathf.Lerp(lower.defLossWin, upper.defLossWin, t),
                attSeverityOnAttWin = LerpSeverityShares(lower.attSeverityOnAttWin, upper.attSeverityOnAttWin, t, threshold, SeverityShareKind.AttOnWin),
                attSeverityOnAttLoss = LerpSeverityShares(lower.attSeverityOnAttLoss, upper.attSeverityOnAttLoss, t, threshold, SeverityShareKind.AttOnLoss),
                defCoalitionOnAttWin = LerpSeverityShares(lower.defCoalitionOnAttWin, upper.defCoalitionOnAttWin, t, threshold, SeverityShareKind.DefOnWin),
                defCoalitionOnAttLoss = LerpSeverityShares(lower.defCoalitionOnAttLoss, upper.defCoalitionOnAttLoss, t, threshold, SeverityShareKind.DefOnLoss)
            };

            interpolated.winChance = DefaultWinChanceAt(threshold, interpolated.winChance);

            return interpolated;
        }

        private enum SeverityShareKind { AttOnWin, AttOnLoss, DefOnWin, DefOnLoss }

        private static RaidMarginShares LerpSeverityShares(RaidMarginShares lower, RaidMarginShares upper, float t, float threshold, SeverityShareKind kind)
        {
            if (lower == null) lower = DefaultSeveritySharesAt(threshold, kind);
            if (upper == null) upper = lower;
            var result = new RaidMarginShares
            {
                close = Mathf.Lerp(lower.close, upper.close, t),
                normal = Mathf.Lerp(lower.normal, upper.normal, t),
                decisive = Mathf.Lerp(lower.decisive, upper.decisive, t)
            };
            result.Normalize();
            return result;
        }

        private static RaidMarginShares DefaultSeveritySharesAt(float threshold, SeverityShareKind kind)
        {
            switch (kind)
            {
                case SeverityShareKind.AttOnLoss: return RaidSeverityDefaults.AttSeverityOnAttLossAt(threshold);
                case SeverityShareKind.DefOnWin: return RaidSeverityDefaults.DefCoalitionOnAttWinAt(threshold);
                case SeverityShareKind.DefOnLoss: return RaidSeverityDefaults.DefCoalitionOnAttLossAt(threshold);
                default: return RaidSeverityDefaults.AttSeverityOnAttWinAt(threshold);
            }
        }

        private static float DefaultWinChanceAt(float threshold, float fallback = 0.5f)
        {
            if (Mathf.Abs(threshold - 0.00f) < 0.001f) return 0.00f;
            if (Mathf.Abs(threshold - 0.10f) < 0.001f) return 0.03f;
            if (Mathf.Abs(threshold - 0.25f) < 0.001f) return 0.10f;
            if (Mathf.Abs(threshold - 0.50f) < 0.001f) return 0.20f;
            if (Mathf.Abs(threshold - 1.00f) < 0.001f) return 0.42f;
            if (Mathf.Abs(threshold - 1.50f) < 0.001f) return 0.58f;
            if (Mathf.Abs(threshold - 2.00f) < 0.001f) return 0.70f;
            if (Mathf.Abs(threshold - 3.00f) < 0.001f) return 0.88f;
            if (Mathf.Abs(threshold - 4.00f) < 0.001f) return 0.94f;
            if (Mathf.Abs(threshold - 5.00f) < 0.001f) return 0.95f;
            if (Mathf.Abs(threshold - 6.00f) < 0.001f) return 0.99f;
            return fallback;
        }

        private static RaidOutcome DefaultRaidOutcomeAt(float threshold)
        {
            float winChance = DefaultWinChanceAt(threshold);
            if (Mathf.Abs(threshold - 0.10f) < 0.001f)
            {
                return new RaidOutcome
                {
                    threshold = 0.10f, winChance = winChance, attLossWin = 0.92f, defLossLoss = 0.81f, defLossWin = 0.056f,
                    attSeverityOnAttWin = RaidSeverityDefaults.AttSeverityOnAttWinAt(0.10f),
                    attSeverityOnAttLoss = RaidSeverityDefaults.AttSeverityOnAttLossAt(0.10f),
                    defCoalitionOnAttWin = RaidSeverityDefaults.DefCoalitionOnAttWinAt(0.10f),
                    defCoalitionOnAttLoss = RaidSeverityDefaults.DefCoalitionOnAttLossAt(0.10f)
                };
            }
            if (Mathf.Abs(threshold - 0.25f) < 0.001f)
            {
                return new RaidOutcome
                {
                    threshold = 0.25f, winChance = winChance, attLossWin = 0.875f, defLossLoss = 0.825f, defLossWin = 0.11f,
                    attSeverityOnAttWin = RaidSeverityDefaults.AttSeverityOnAttWinAt(0.25f),
                    attSeverityOnAttLoss = RaidSeverityDefaults.AttSeverityOnAttLossAt(0.25f),
                    defCoalitionOnAttWin = RaidSeverityDefaults.DefCoalitionOnAttWinAt(0.25f),
                    defCoalitionOnAttLoss = RaidSeverityDefaults.DefCoalitionOnAttLossAt(0.25f)
                };
            }
            return new RaidOutcome
            {
                threshold = threshold,
                winChance = winChance,
                attSeverityOnAttWin = RaidSeverityDefaults.AttSeverityOnAttWinAt(threshold),
                attSeverityOnAttLoss = RaidSeverityDefaults.AttSeverityOnAttLossAt(threshold),
                defCoalitionOnAttWin = RaidSeverityDefaults.DefCoalitionOnAttWinAt(threshold),
                defCoalitionOnAttLoss = RaidSeverityDefaults.DefCoalitionOnAttLossAt(threshold)
            };
        }

        public void InitializeDefaults()
        {
            ResetDailyActions();
            ResetGrowth();
            ResetOutpost();
            ResetPlayerArtillery();
            ResetDiplomacy();
            ResetThreat();
            ResetLateGame();
            ResetRaids();
            ResetCaravans();
            ResetSabotage();
            ResetDisinformation();
            ResetFoodLogistics();
            ResetExperimental();
            ResetNotifications();
            ResetWorldGen();
            ResetGarrisons();
            ResetT4Mortar();
            performancePreset = DefPerformancePreset;
            difficultyPreset = DefDifficultyPreset;
        }

        /// <summary>
        /// Apply only the Performance pack fields for <paramref name="preset"/> (does not reset other settings or touch Difficulty).
        /// </summary>
        public void ApplyPerformancePreset(WDSettingsPerformancePreset preset)
        {
            performancePreset = preset;
            ApplyPerformancePresetValues(preset);
        }

        /// <summary>
        /// Apply only the Difficulty pack fields for <paramref name="preset"/> (does not reset other settings or touch Performance).
        /// </summary>
        public void ApplyDifficultyPreset(WDSettingsDifficultyPreset preset)
        {
            difficultyPreset = preset;
            ApplyDifficultyPresetValues(preset);
        }

        /// <summary>True when current values match the given Performance preset exactly (within float epsilon).</summary>
        public bool MatchesPerformancePreset(WDSettingsPerformancePreset preset)
        {
            float travelPrep, traderRad;
            int scanTicks, logisticsRange, maxSettlementsCap, ambushMaxConcurrent;
            bool water, t4Mortar, t4Aa, pathCost, pathRepath, pathCancel;
            GetPerformancePresetValues(preset, out travelPrep, out water, out t4Mortar, out t4Aa, out scanTicks, out traderRad, out logisticsRange, out maxSettlementsCap,
                out pathCost, out pathRepath, out pathCancel, out ambushMaxConcurrent);
            return verboseLogging == false
                && onlyTravelAcrossWaterIfNoOtherWay == true
                && Approx(travelPrepExactPercent, travelPrep)
                && allowCaravansTravelOverWater == water
                && enableNpcT4Mortar == t4Mortar
                && enableNpcT4AntiAir == t4Aa
                && interceptionScanIntervalTicks == scanTicks
                && Approx(traderDestinationSearchRadius, traderRad)
                && maxLogisticsRange == logisticsRange
                && maxSettlements == maxSettlementsCap
                && pollutionPathCostEnabled == pathCost
                && pollutionPathRepathEnabled == pathRepath
                && pollutionPathPreCommitCancelEnabled == pathCancel
                && settlementAmbushMaxConcurrent == ambushMaxConcurrent;
        }

        /// <summary>True when current values match the given Difficulty preset exactly (within float epsilon).</summary>
        public bool MatchesDifficultyPreset(WDSettingsDifficultyPreset preset)
        {
            float cdColony, cdOutpost, raidMin, raidMax, wRaid, shareTh, outpostTh, bias, growth, garrison;
            int perDay, per4, per7;
            bool lateGame;
            bool skillDr;
            GetDifficultyPresetValues(preset, out cdColony, out cdOutpost, out perDay, out per4, out per7,
                out raidMin, out raidMax, out wRaid, out lateGame, out shareTh, out outpostTh, out bias, out growth, out garrison,
                out skillDr);
            return Approx(cooldownPlayerRaidDays, cdColony)
                && Approx(cooldownPlayerOutpostRaidDays, cdOutpost)
                && maxPlayerWdRaidsPerDay == perDay
                && maxPlayerWdRaidsPer4Days == per4
                && maxPlayerWdRaidsPer7Days == per7
                && Approx(caravanRaidPointsMinStorytellerFraction, raidMin)
                && Approx(caravanRaidPointsMaxStorytellerFraction, raidMax)
                && Approx(weightRaid, wRaid)
                && enableLateGameScaling == lateGame
                && Approx(lateGameShareThreshold, shareTh)
                && Approx(lateGameOutpostStrengthThreshold, outpostTh)
                && Approx(lateGameRaidBiasPct, bias)
                && Approx(lateGameGrowthMult, growth)
                && Approx(lateGameGarrisonBoostPct, garrison)
                && enableT4SettlementMortar == (preset != WDSettingsDifficultyPreset.Easy)
                && enableT4SettlementAntiAir == (preset != WDSettingsDifficultyPreset.Easy)
                && enableMidGameT4SettlementMortar == (preset == WDSettingsDifficultyPreset.Hard)
                && enableMidGameT4SettlementAntiAir == (preset == WDSettingsDifficultyPreset.Hard)
                && enableMidGameAllyRadiusScaling == (preset != WDSettingsDifficultyPreset.Easy)
                && enableLateGameAllyRadiusScaling == (preset != WDSettingsDifficultyPreset.Easy)
                && Approx(midGameAllyRadiusBonusPct, DefMidGameAllyRadiusBonusPct)
                && Approx(lateGameAllyRadiusBonusPct, DefLateGameAllyRadiusBonusPct)
                && enableOutpostSkillDiminishingReturns == skillDr;
        }

        private static bool Approx(float a, float b) => Mathf.Abs(a - b) < 0.0001f;

        public void ApplyPerformancePresetValues(WDSettingsPerformancePreset preset)
        {
            verboseLogging = false;
            onlyTravelAcrossWaterIfNoOtherWay = true;
            GetPerformancePresetValues(preset,
                out travelPrepExactPercent,
                out allowCaravansTravelOverWater,
                out enableNpcT4Mortar,
                out enableNpcT4AntiAir,
                out interceptionScanIntervalTicks,
                out traderDestinationSearchRadius,
                out maxLogisticsRange,
                out maxSettlements,
                out pollutionPathCostEnabled,
                out pollutionPathRepathEnabled,
                out pollutionPathPreCommitCancelEnabled,
                out settlementAmbushMaxConcurrent);
        }

        private void GetPerformancePresetValues(
            WDSettingsPerformancePreset preset,
            out float travelPrep,
            out bool water,
            out bool t4Mortar,
            out bool t4Aa,
            out int scanTicks,
            out float traderRad,
            out int logisticsRange,
            out int maxSettlementsCap,
            out bool pollutionPathCost,
            out bool pollutionPathRepath,
            out bool pollutionPathCancel,
            out int ambushMaxConcurrent)
        {
            switch (preset)
            {
                case WDSettingsPerformancePreset.Low:
                    travelPrep = 0f;
                    water = false;
                    t4Mortar = false;
                    t4Aa = false;
                    scanTicks = 3600;
                    traderRad = 30f;
                    logisticsRange = 15;
                    maxSettlementsCap = 200;
                    pollutionPathCost = false;
                    pollutionPathRepath = false;
                    pollutionPathCancel = true;
                    ambushMaxConcurrent = 4;
                    break;
                case WDSettingsPerformancePreset.High:
                    travelPrep = 0.80f;
                    water = true;
                    t4Mortar = true;
                    t4Aa = true;
                    scanTicks = 900;
                    traderRad = DefTraderDestinationSearchRadius;
                    logisticsRange = DefMaxLogisticsRange;
                    maxSettlementsCap = 800;
                    pollutionPathCost = true;
                    pollutionPathRepath = true;
                    pollutionPathCancel = true;
                    ambushMaxConcurrent = 0;
                    break;
                default:
                    travelPrep = DefTravelPrepExactPercent;
                    water = DefAllowCaravansTravelOverWater;
                    t4Mortar = DefEnableNpcT4Mortar;
                    t4Aa = DefEnableNpcT4AntiAir;
                    scanTicks = DefInterceptionScanIntervalTicks;
                    traderRad = DefTraderDestinationSearchRadius;
                    logisticsRange = DefMaxLogisticsRange;
                    maxSettlementsCap = DefMaxSettlements;
                    pollutionPathCost = DefPollutionPathCostEnabled;
                    pollutionPathRepath = DefPollutionPathRepathEnabled;
                    pollutionPathCancel = DefPollutionPathPreCommitCancelEnabled;
                    ambushMaxConcurrent = DefSettlementAmbushMaxConcurrent;
                    break;
            }
        }

        public void ApplyDifficultyPresetValues(WDSettingsDifficultyPreset preset)
        {
            GetDifficultyPresetValues(preset,
                out cooldownPlayerRaidDays,
                out cooldownPlayerOutpostRaidDays,
                out maxPlayerWdRaidsPerDay,
                out maxPlayerWdRaidsPer4Days,
                out maxPlayerWdRaidsPer7Days,
                out caravanRaidPointsMinStorytellerFraction,
                out caravanRaidPointsMaxStorytellerFraction,
                out weightRaid,
                out enableLateGameScaling,
                out lateGameShareThreshold,
                out lateGameOutpostStrengthThreshold,
                out lateGameRaidBiasPct,
                out lateGameGrowthMult,
                out lateGameGarrisonBoostPct,
                out bool skillDr);
            // Mid pack always resets to Def* when applying a difficulty preset; Late numbers come from the preset.
            midGameShareThreshold = DefMidGameShareThreshold;
            midGameOutpostStrengthThreshold = DefMidGameOutpostStrengthThreshold;
            midGameRaidBiasPct = DefMidGameRaidBiasPct;
            midGameGrowthMult = DefMidGameGrowthMult;
            midGameAttackRangeBonusPct = DefMidGameAttackRangeBonusPct;
            midGameExpandTowardPlayerMaxTiles = DefMidGameExpandTowardPlayerMaxTiles;
            midGameGarrisonBoostPct = DefMidGameGarrisonBoostPct;
            enableMidGameOutpostIncidents = DefEnableMidGameOutpostIncidents;
            midGameOutpostIncidentSeverity = DefMidGameOutpostIncidentSeverity;
            midGameOutpostIncidentDailyChance = DefMidGameOutpostIncidentDailyChance;
            enableGoodwillDrain = DefEnableGoodwillDrain;
            goodwillDrainIntervalDays = DefGoodwillDrainIntervalDays;
            midGameGoodwillDrainAmount = DefMidGameGoodwillDrainAmount;
            lateGameGoodwillDrainAmount = DefLateGameGoodwillDrainAmount;
            lateGameExpandTowardPlayerMaxTiles = DefLateGameExpandTowardPlayerMaxTiles;
            lateGameAttackRangeBonusPct = DefLateGameAttackRangeBonusPct;
            midGameAllyRadiusBonusPct = DefMidGameAllyRadiusBonusPct;
            lateGameAllyRadiusBonusPct = DefLateGameAllyRadiusBonusPct;
            enableOutpostIncidents = DefEnableOutpostIncidents;
            outpostIncidentSeverity = DefOutpostIncidentSeverity;
            outpostIncidentDailyChance = DefOutpostIncidentDailyChance;
            // Easy: T4 vs player off. Medium: Late on, Mid off. Hard: Mid and Late on.
            bool lateT4VsPlayer = preset != WDSettingsDifficultyPreset.Easy;
            bool midT4VsPlayer = preset == WDSettingsDifficultyPreset.Hard;
            bool allyRadiusScale = preset != WDSettingsDifficultyPreset.Easy;
            enableMidGameT4SettlementMortar = midT4VsPlayer;
            enableMidGameT4SettlementAntiAir = midT4VsPlayer;
            enableT4SettlementMortar = lateT4VsPlayer;
            enableT4SettlementAntiAir = lateT4VsPlayer;
            enableMidGameAllyRadiusScaling = allyRadiusScale;
            enableLateGameAllyRadiusScaling = allyRadiusScale;
            NormalizeEscalationConstraints();
            ClampPlayerWdRaidRateCaps();
            if (skillDr)
                OutpostSkillScaling.ResetToDefaults(this);
            else
                enableOutpostSkillDiminishingReturns = false;
        }

        private void GetDifficultyPresetValues(
            WDSettingsDifficultyPreset preset,
            out float cdColony,
            out float cdOutpost,
            out int perDay,
            out int per4,
            out int per7,
            out float raidMin,
            out float raidMax,
            out float wRaid,
            out bool lateGame,
            out float shareTh,
            out float outpostTh,
            out float bias,
            out float growth,
            out float garrison,
            out bool skillDiminishingReturns)
        {
            switch (preset)
            {
                case WDSettingsDifficultyPreset.Easy:
                    cdColony = 8f;
                    cdOutpost = 8f;
                    perDay = 1;
                    per4 = 1;
                    per7 = 2;
                    raidMin = 0.50f;
                    raidMax = 1.50f;
                    wRaid = 15f;
                    lateGame = false;
                    shareTh = DefLateGameShareThreshold;
                    outpostTh = DefLateGameOutpostStrengthThreshold;
                    bias = DefLateGameRaidBiasPct;
                    growth = DefLateGameGrowthMult;
                    garrison = DefLateGameGarrisonBoostPct;
                    skillDiminishingReturns = false;
                    break;
                case WDSettingsDifficultyPreset.Hard:
                    cdColony = 3f;
                    cdOutpost = 3f;
                    perDay = 2;
                    per4 = 4;
                    per7 = 6;
                    raidMin = 1.00f;
                    raidMax = 3.00f;
                    wRaid = 35f;
                    lateGame = true;
                    shareTh = DefLateGameShareThreshold;
                    outpostTh = DefLateGameOutpostStrengthThreshold;
                    bias = 0.75f;
                    growth = 2.5f;
                    garrison = 0.45f;
                    skillDiminishingReturns = true;
                    break;
                default:
                    cdColony = DefCdPlayerRaidDays;
                    cdOutpost = DefCooldownPlayerOutpostRaidDays;
                    perDay = DefMaxPlayerWdRaidsPerDay;
                    per4 = DefMaxPlayerWdRaidsPer4Days;
                    per7 = DefMaxPlayerWdRaidsPer7Days;
                    raidMin = DefCaravanRaidMinStorytellerFrac;
                    raidMax = DefCaravanRaidMaxStorytellerFrac;
                    wRaid = DefWeightRaid;
                    lateGame = DefEnableLateGameScaling;
                    shareTh = DefLateGameShareThreshold;
                    outpostTh = DefLateGameOutpostStrengthThreshold;
                    bias = DefLateGameRaidBiasPct;
                    growth = DefLateGameGrowthMult;
                    garrison = DefLateGameGarrisonBoostPct;
                    skillDiminishingReturns = true;
                    break;
            }
        }

        /// <summary>Resets the player mortar outpost values (decoupled from NPC T4 mortar tuning, see <see cref="ResetT4Mortar"/>).</summary>
        public void ResetMortarInterception()
        {
            mortarRange = DefMortarRange;
            cooldownMortarDays = DefCooldownMortarDays;
            mortarBaseMissChanceAtMaxRange = DefMortarBaseMissChanceAtMaxRange;
            mortarHitPerSkillPoint = DefMortarHitPerSkillPoint;
            mortarHitChance0To50PctRange = DefMortarHitChance0To50PctRange;
            mortarHitChance51To75PctRange = DefMortarHitChance51To75PctRange;
            mortarHitChance76To100PctRange = DefMortarHitChance76To100PctRange;
            mortarDamagePerSkillPoint = DefMortarDamagePerSkillPoint;
            mortarBaseShellDamage = DefMortarBaseShellDamage;
            mortarShellTicksPerMove = DefMortarShellTicksPerMove;
            antiAirBaseDamage = DefAntiAirBaseDamage;
            cooldownAntiAirSeconds = DefCooldownAntiAirSeconds;
            antiAirCooldownFloorSeconds = DefAntiAirCooldownFloorSeconds;
            antiAirRange = DefAntiAirRange;
            antiAirHitChance0To50PctRange = DefAntiAirHitChance0To50PctRange;
            antiAirHitChance51To75PctRange = DefAntiAirHitChance51To75PctRange;
            antiAirHitChance76To100PctRange = DefAntiAirHitChance76To100PctRange;
            antiAirVsMortarHitChance = DefAntiAirVsMortarHitChance;
            flakShellTicksPerMove = DefFlakShellTicksPerMove;
        }

        public void ResetRapidResponse()
        {
            rapidResponseOffensiveStrengthBonus = DefRapidResponseOffensiveStrengthBonus;
            rapidResponseOffensiveRecoveryBonus = DefRapidResponseOffensiveRecoveryBonus;
            rapidResponseTicksPerMoveMultiplier = DefRapidResponseTicksPerMoveMultiplier;
            rapidResponseAutoInterceptRange = DefRapidResponseAutoInterceptRange;
            rapidResponseDropPodRange = DefRapidResponseDropPodRange;
            dropPodTicksPerMove = DefDropPodTicksPerMove;
        }

        /// <summary>Player mortar, AA, and Rapid Response (Player Artillery settings page).</summary>
        public void ResetPlayerArtillery()
        {
            ResetMortarInterception();
            ResetRapidResponse();
        }

        /// <summary>Resets the enemy tier-4 settlement mortar/AA tuning (decoupled player values + gating toggles + shared scan interval).</summary>
        public void ResetT4Mortar()
        {
            enableNpcT4Mortar = DefEnableNpcT4Mortar;
            enableT4SettlementMortar = DefEnableT4SettlementMortar;
            enableNpcT4AntiAir = DefEnableNpcT4AntiAir;
            enableT4SettlementAntiAir = DefEnableT4SettlementAntiAir;
            notifyT4AntiAirHitPlayer = DefNotifyT4AntiAirHitPlayer;
            notifyPlayerMortarShellShotDown = DefNotifyPlayerMortarShellShotDown;
            npcT4MortarMinTechLevel = DefNpcT4MortarMinTechLevel;
            npcMortarRange = DefNpcMortarRange;
            npcMortarCooldownDays = DefNpcMortarCooldownDays;
            npcMortarDamage = DefNpcMortarDamage;
            npcMortarSkillEquivalent = DefNpcMortarSkillEquivalent;
            npcMortarHitChance0To50PctRange = DefNpcMortarHitChance0To50PctRange;
            npcMortarHitChance51To75PctRange = DefNpcMortarHitChance51To75PctRange;
            npcMortarHitChance76To100PctRange = DefNpcMortarHitChance76To100PctRange;
            npcAntiAirRange = DefNpcAntiAirRange;
            npcAntiAirCooldownSeconds = DefNpcAntiAirCooldownSeconds;
            npcAntiAirDamage = DefNpcAntiAirDamage;
            npcAntiAirSkillEquivalent = DefNpcAntiAirSkillEquivalent;
            npcAntiAirHitChance0To50PctRange = DefNpcAntiAirHitChance0To50PctRange;
            npcAntiAirHitChance51To75PctRange = DefNpcAntiAirHitChance51To75PctRange;
            npcAntiAirHitChance76To100PctRange = DefNpcAntiAirHitChance76To100PctRange;
            npcAntiAirVsMortarHitChance = DefNpcAntiAirVsMortarHitChance;
            interceptionScanIntervalTicks = DefInterceptionScanIntervalTicks;
        }

        public void ResetDailyActions()
        {
            tier1Share = DefTier1Share;
            tier2Share = DefTier2Share;
            tier3Share = DefTier3Share;
            tier4Share = DefTier4Share;

            // SURGICAL: Reset Caps
            tier1MaxActions = DefCapT1;
            tier2MaxActions = DefCapT2;
            tier3MaxActions = DefCapT3;
            tier4MaxActions = DefCapT4;

            weightGrow = DefWeightGrow;
            weightRaid = DefWeightRaid;
            weightMinorIncident = DefWeightMinorIncident;
            weightMajorIncident = DefWeightMajorIncident;
            weightBuildRoad = DefWeightBuildRoad;
            weightTrader = DefWeightTrader;
            weightFortify = DefWeightFortify;
            includeDevelopWeightInPercentDisplay = DefIncludeDevelopWeightInPercentDisplay;
            cooldownGrowDays = DefCdGrowDays;
            cooldownExpandDays = DefCdExpandDays;
            cooldownRaidDays = DefCdRaidDays;
            cooldownBeingRaidedDays = DefCdBeingRaidedDays;
            cooldownIncidentDays = DefCdIncidentDays;
            cooldownTraderDays = DefCdTraderDays;
            cooldownFortifyDays = DefCdFortifyDays;

            fortifyMinTilesFromSelf = DefFortifyMinTilesFromSelf;
            fortifyMinTilesFromOtherSettlement = DefFortifyMinTilesFromOtherSettlement;
            fortifyMaxTilesFromSelf = DefFortifyMaxTilesFromSelf;
            fortifyMaxTravelTiles = DefFortifyMaxTravelTiles;
            fortifyTerritoryLinkMaxTiles = DefFortifyTerritoryLinkMaxTiles;
            fortifyFrontierEps = DefFortifyFrontierEps;
            fortifyTravelerStrength = DefFortifyTravelerStrength;
            fortifyClearOnBuilderLoss = DefFortifyClearOnBuilderLoss;
            enableFortifyBlacklist = DefEnableFortifyBlacklist;
            fortifyBlacklistApplyToNeutral = DefFortifyBlacklistApplyToNeutral;
            fortifyChanceRoadBlock = DefFortifyChanceRoadBlock;
            fortifyChanceTrap = DefFortifyChanceTrap;
            fortifyChanceTurret = DefFortifyChanceTurret;
            fortifyMultiT1ChanceOf2 = DefFortifyMultiT1ChanceOf2;
            fortifyMultiT2ChanceOf2 = DefFortifyMultiT2ChanceOf2;
            fortifyMultiT3ChanceOf2 = DefFortifyMultiT3ChanceOf2;
            fortifyMultiT4ChanceOf3 = DefFortifyMultiT4ChanceOf3;
            atTurretMaxT1 = DefAtTurretMaxT1;
            atTurretMaxT2 = DefAtTurretMaxT2;
            atTurretMaxT3 = DefAtTurretMaxT3;
            atTurretMaxT4 = DefAtTurretMaxT4;
            // Player AT caps live on Experimental reset (not World Actions fortify NPC caps).
        }

        public void ResetGrowth()
        {
            maxSettlements = DefMaxSettlements;
            passiveGrowthT1 = DefPassiveGrowthT1;
            passiveGrowthT2 = DefPassiveGrowthT2;
            passiveGrowthT3 = DefPassiveGrowthT3;
            passiveGrowthT4 = DefPassiveGrowthT4;
            baseGrowthAmount = DefBaseGrowth;
            growthScalingIntensity = DefGrowthScaling;
            expandMinRadius = DefExpandMinRad;
            expandMaxRadius = DefExpandMaxRad;
            maxRoadRange = DefMaxRoadRange;
            maxRoadRangeNpc = DefMaxRoadRangeNpc;
            maxRoadBlockRange = DefMaxRoadBlockRange;
            roadBlockLightFlatPenalty = DefRoadBlockLightFlatPenalty;
            roadBlockNormalFlatPenalty = DefRoadBlockNormalFlatPenalty;
            roadBlockHeavyFlatPenalty = DefRoadBlockHeavyFlatPenalty;
            roadBlockLightExpeditionStrength = DefRoadBlockLightExpeditionStrength;
            roadBlockNormalExpeditionStrength = DefRoadBlockNormalExpeditionStrength;
            roadBlockHeavyExpeditionStrength = DefRoadBlockHeavyExpeditionStrength;
            roadBlockLightWork = DefRoadBlockLightWork;
            roadBlockNormalWork = DefRoadBlockNormalWork;
            roadBlockHeavyWork = DefRoadBlockHeavyWork;
            roadBlockLightMaxHealth = DefRoadBlockLightMaxHealth;
            roadBlockNormalMaxHealth = DefRoadBlockNormalMaxHealth;
            roadBlockHeavyMaxHealth = DefRoadBlockHeavyMaxHealth;
            maxSpikeTrapRange = DefMaxSpikeTrapRange;
            spikeTrapSpikeWork = DefSpikeTrapSpikeWork;
            spikeTrapCaltropsWork = DefSpikeTrapCaltropsWork;
            spikeTrapSpikeExpeditionStrength = DefSpikeTrapSpikeExpeditionStrength;
            spikeTrapCaltropsExpeditionStrength = DefSpikeTrapCaltropsExpeditionStrength;
            spikeTrapSpikeDamage = DefSpikeTrapSpikeDamage;
            spikeTrapCaltropsDamage = DefSpikeTrapCaltropsDamage;
            spikeTrapSpikeMaxHealth = DefSpikeTrapSpikeMaxHealth;
            spikeTrapCaltropsMaxHealth = DefSpikeTrapCaltropsMaxHealth;
            spikeTrapMaxTriggersPerTraveler = DefSpikeTrapMaxTriggersPerTraveler;
            maxDecontaminationRange = DefMaxDecontaminationRange;
            decontaminationWork = DefDecontaminationWork;
            decontaminationExpeditionStrength = DefDecontaminationExpeditionStrength;
            decontaminationPollutionReductionPp = DefDecontaminationPollutionReductionPp;
            ResetRoadBuildingFallback();
            minorIncidentSeverity = DefMinorIncSev;
            majorIncidentSeverity = DefMajorIncSev;
            localMaxT1 = DefLocalMaxT1;
            localMaxT2 = DefLocalMaxT2;
            localMaxT3 = DefLocalMaxT3;
            localMaxT4 = DefLocalMaxT4;
            sameTierNeighborsToUpgradeT1 = DefSameTierNeighborsToUpgradeT1;
            sameTierNeighborsToUpgradeT2 = DefSameTierNeighborsToUpgradeT2;
            sameTierNeighborsToUpgradeT3 = DefSameTierNeighborsToUpgradeT3;
            expansionSuccessChance = DefExpansionSuccessChance;
            tier1BaseDefensiveStrength = DefTier1BaseDefensiveStrength;
            tier2BaseDefensiveStrength = DefTier2BaseDefensiveStrength;
            tier3BaseDefensiveStrength = DefTier3BaseDefensiveStrength;
            tier4BaseDefensiveStrength = DefTier4BaseDefensiveStrength;
            maxGoodwill = DefMaxGoodwill;
            traderCaravanCostStrength = DefTraderCaravanCostStrength;
            traderCaravanSenderRewardStrength = DefTraderCaravanSenderRewardStrength;
            traderCaravanReceiverRewardStrength = DefTraderCaravanReceiverRewardStrength;
            traderCaravanGoodwillGain = DefTraderCaravanGoodwillGain;
            cooldownPlayerColonyTraderDays = DefCooldownPlayerColonyTraderDays;
            traderTierUpgradeChanceT1ToT2 = DefTraderTierUpgradeChanceT1ToT2;
            traderTierUpgradeChanceT2ToT3 = DefTraderTierUpgradeChanceT2ToT3;
            traderTierUpgradeChanceT3ToT4 = DefTraderTierUpgradeChanceT3ToT4;
        }

        public void ResetRoadBuildingFallback()
        {
            fallbackDirtRoadMovement = DefFallbackDirtRoadMovement;
            fallbackStoneRoadMovement = DefFallbackStoneRoadMovement;
            fallbackAsphaltRoadMovement = DefFallbackAsphaltRoadMovement;
            fallbackDirtRoadWork = DefFallbackDirtRoadWork;
            fallbackStoneRoadWork = DefFallbackStoneRoadWork;
            fallbackAsphaltRoadWork = DefFallbackAsphaltRoadWork;
            fallbackDirtRoadExpeditionStrength = DefFallbackDirtRoadExpeditionStrength;
            fallbackStoneRoadExpeditionStrength = DefFallbackStoneRoadExpeditionStrength;
            fallbackAsphaltRoadExpeditionStrength = DefFallbackAsphaltRoadExpeditionStrength;
            fallbackDirtRoadMinConstruction = DefFallbackDirtRoadMinConstruction;
            fallbackStoneRoadMinConstruction = DefFallbackStoneRoadMinConstruction;
            fallbackAsphaltRoadMinConstruction = DefFallbackAsphaltRoadMinConstruction;
            fallbackDirtRoadWinterReduction = DefFallbackDirtRoadWinterReduction;
            fallbackStoneRoadWinterReduction = DefFallbackStoneRoadWinterReduction;
            fallbackAsphaltRoadWinterReduction = DefFallbackAsphaltRoadWinterReduction;
            maxRoadRange = DefMaxRoadRange;
            maxRoadRangeNpc = DefMaxRoadRangeNpc;
            maxRoadBlockRange = DefMaxRoadBlockRange;
            roadBlockLightFlatPenalty = DefRoadBlockLightFlatPenalty;
            roadBlockNormalFlatPenalty = DefRoadBlockNormalFlatPenalty;
            roadBlockHeavyFlatPenalty = DefRoadBlockHeavyFlatPenalty;
            roadBlockLightExpeditionStrength = DefRoadBlockLightExpeditionStrength;
            roadBlockNormalExpeditionStrength = DefRoadBlockNormalExpeditionStrength;
            roadBlockHeavyExpeditionStrength = DefRoadBlockHeavyExpeditionStrength;
            roadBlockLightWork = DefRoadBlockLightWork;
            roadBlockNormalWork = DefRoadBlockNormalWork;
            roadBlockHeavyWork = DefRoadBlockHeavyWork;
            roadBlockLightMaxHealth = DefRoadBlockLightMaxHealth;
            roadBlockNormalMaxHealth = DefRoadBlockNormalMaxHealth;
            roadBlockHeavyMaxHealth = DefRoadBlockHeavyMaxHealth;
            maxSpikeTrapRange = DefMaxSpikeTrapRange;
            spikeTrapSpikeWork = DefSpikeTrapSpikeWork;
            spikeTrapCaltropsWork = DefSpikeTrapCaltropsWork;
            spikeTrapSpikeExpeditionStrength = DefSpikeTrapSpikeExpeditionStrength;
            spikeTrapCaltropsExpeditionStrength = DefSpikeTrapCaltropsExpeditionStrength;
            spikeTrapSpikeDamage = DefSpikeTrapSpikeDamage;
            spikeTrapCaltropsDamage = DefSpikeTrapCaltropsDamage;
            spikeTrapSpikeMaxHealth = DefSpikeTrapSpikeMaxHealth;
            spikeTrapCaltropsMaxHealth = DefSpikeTrapCaltropsMaxHealth;
            spikeTrapMaxTriggersPerTraveler = DefSpikeTrapMaxTriggersPerTraveler;
            atTurretLightWork = DefAtTurretLightWork;
            atTurretMediumWork = DefAtTurretMediumWork;
            atTurretHeavyWork = DefAtTurretHeavyWork;
            atTurretLightMinConstruction = DefAtTurretLightMinConstruction;
            atTurretMediumMinConstruction = DefAtTurretMediumMinConstruction;
            atTurretHeavyMinConstruction = DefAtTurretHeavyMinConstruction;
            atTurretLightExpeditionStrength = DefAtTurretLightExpeditionStrength;
            atTurretMediumExpeditionStrength = DefAtTurretMediumExpeditionStrength;
            atTurretHeavyExpeditionStrength = DefAtTurretHeavyExpeditionStrength;
            atTurretPlayerGlobalMax = DefAtTurretPlayerGlobalMax;
            atTurretPlayerPerSiteMax = DefAtTurretPlayerPerSiteMax;
            atTurretLightMaxStrength = DefAtTurretLightMaxStrength;
            atTurretMediumMaxStrength = DefAtTurretMediumMaxStrength;
            atTurretHeavyMaxStrength = DefAtTurretHeavyMaxStrength;
            atTurretLightDamage = DefAtTurretLightDamage;
            atTurretDamage = DefAtTurretDamage;
            atTurretHeavyDamage = DefAtTurretHeavyDamage;
            atTurretLightCooldownDays = DefAtTurretLightCooldownDays;
            atTurretCooldownDays = DefAtTurretCooldownDays;
            atTurretHeavyCooldownDays = DefAtTurretHeavyCooldownDays;
            atTurretLightRange = DefAtTurretLightRange;
            atTurretMediumRange = DefAtTurretMediumRange;
            atTurretHeavyRange = DefAtTurretHeavyRange;
            atTurretHitChance0To50PctRange = DefAtTurretHitChance0To50PctRange;
            atTurretHitChance51To75PctRange = DefAtTurretHitChance51To75PctRange;
            atTurretHitChance76To100PctRange = DefAtTurretHitChance76To100PctRange;
            maxDecontaminationRange = DefMaxDecontaminationRange;
            decontaminationWork = DefDecontaminationWork;
            decontaminationExpeditionStrength = DefDecontaminationExpeditionStrength;
            decontaminationPollutionReductionPp = DefDecontaminationPollutionReductionPp;
        }

        public void ResetOutpost()
        {
            outpostMinDistanceTiles = DefOutpostMinDistanceTiles;
            raidTargetRadius = DefRaidTargetRadius;
            playerOutpostBaseDefensiveStrength = DefPlayerOutpostBaseDefensiveStrength;
            outpostBuildCostMultiplier = DefOutpostBuildCostMultiplier;
            outpostDeliveryStrengthCost = DefOutpostDeliveryStrengthCost;
            outpostDeliveryMinStrength = DefOutpostDeliveryMinStrength;
            outpostSilverValuePerSkillPerCycle = DefOutpostSilverValuePerSkillPerCycle;
            outpostProductionTimeMultiplier = DefOutpostProductionTimeMultiplier;
            outpostProductionOutputMultiplier = DefOutpostProductionOutputMultiplier;
            warehouseAuraBonusPct = DefWarehouseAuraBonusPct;
            warehouseAuraRadiusTiles = DefWarehouseAuraRadiusTiles;
            embassyMayGainGoodwillWithHostiles = DefEmbassyMayGainGoodwillWithHostiles;
            clampOutpostSkillsAtLevel20 = DefClampOutpostSkillsAtLevel20;
            OutpostSkillScaling.ResetToDefaults(this);
            outpostOccupantSkillXpPerProductionCycle = DefOutpostOccupantSkillXpPerProductionCycle;
            outpostOccupantSkillXpMaxLevel = DefOutpostOccupantSkillXpMaxLevel;
            academyBaseXpPerDay = DefAcademyBaseXpPerDay;
            academyMinTeacherSkill = DefAcademyMinTeacherSkill;
            academyTeachCapOffset = DefAcademyTeachCapOffset;
            academyUseFlatDirectXp = DefAcademyUseFlatDirectXp;
            outpostUpgradesCostMaterials = DefOutpostUpgradesCostMaterials;
            outpostUpgradesRequireResearch = DefOutpostUpgradesRequireResearch;
            enableOutpostLaunchAttack = DefEnableOutpostLaunchAttack;
            enableOutpostBuildRoads = DefEnableOutpostBuildRoads;
            enableOutpostBuildRoadBlocks = DefEnableOutpostBuildRoadBlocks;
            enableOutpostBuildTraps = DefEnableOutpostBuildTraps;
            outpostReqBiome = DefOutpostReqBiome;
            outpostReqFertility = DefOutpostReqFertility;
            outpostReqAnimalAbundance = DefOutpostReqAnimalAbundance;
            outpostReqFishAbundance = DefOutpostReqFishAbundance;
            outpostReqMiningTerrain = DefOutpostReqMiningTerrain;
            outpostReqResearch = DefOutpostReqResearch;
            outpostReqNearbySettlements = DefOutpostReqNearbySettlements;
            outpostReqMinPawns = DefOutpostReqMinPawns;
            outpostReqMinSkill = DefOutpostReqMinSkill;
            outpostReqCost = DefOutpostReqCost;
            pollutionEcologyPenaltyEnabled = DefPollutionEcologyPenaltyEnabled;
            outpostDefensiveRecoveryMinFlatPerDay = DefOutpostDefensiveRecoveryMinFlatPerDay;
            outpostDefensiveRecoveryFractionPerDay = DefOutpostDefensiveRecoveryFractionPerDay;
            outpostOffensiveRecoveryMinFlatPerDay = DefOutpostOffensiveRecoveryMinFlatPerDay;
            outpostOffensiveRecoveryFractionPerDay = DefOutpostOffensiveRecoveryFractionPerDay;
            outpostOccupantHealSeverityPerDay = DefOutpostOccupantHealSeverityPerDay;
            expertStrategistMaxBonusPct = DefExpertStrategistMaxBonusPct;
            expertEntertainerMaxBonusPct = DefExpertEntertainerMaxBonusPct;
            expertCookMaxBonusPct = DefExpertCookMaxBonusPct;
            expertDoctorMaxBonusPct = DefExpertDoctorMaxBonusPct;
            expertEngineerMaxBonusPct = DefExpertEngineerMaxBonusPct;
            expertEngineerConstructionRadiusMaxBonusPct = DefExpertEngineerConstructionRadiusMaxBonusPct;
            expertRecruiterMaxBonusPct = DefExpertRecruiterMaxBonusPct;
            expertReferenceSkillLevel = DefExpertReferenceSkillLevel;
            cooldownPlayerOutpostRaidDays = DefCooldownPlayerOutpostRaidDays;
            outpostAfterConquestEnabled = DefOutpostAfterConquestEnabled;
            conquestFoundingPawnsT1 = DefConquestFoundingPawnsT1;
            conquestFoundingPawnsT2 = DefConquestFoundingPawnsT2;
            conquestFoundingPawnsT3 = DefConquestFoundingPawnsT3;
            conquestFoundingPawnsT4 = DefConquestFoundingPawnsT4;
            conquestFoundingMinRelevantSkill = DefConquestFoundingMinRelevantSkill;
            ResetMiningBaselines();
        }

        /// <summary>Resets only mining baseline overrides (all sliders back to DefMiningBaselineByDefName / computed baseline).</summary>
        public void ResetMiningBaselines()
        {
            miningBaselineMultiplierByDefName = null;
        }

        private static void ScribeMiningBaselineMultipliers(ref Dictionary<string, float> dict)
        {
            List<string> keys = dict != null ? new List<string>(dict.Keys) : null;
            List<float> values = dict != null ? new List<float>(dict.Values) : null;
            Scribe_Collections.Look(ref keys, "miningBaselineKeys", LookMode.Value);
            Scribe_Collections.Look(ref values, "miningBaselineValues", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.LoadingVars && keys != null && values != null && keys.Count == values.Count)
            {
                dict = new Dictionary<string, float>();
                for (int i = 0; i < keys.Count; i++)
                    dict[keys[i]] = values[i];
            }
        }

        public void ResetDiplomacy()
        {
            enableLeaderHandicap = DefEnableLeaderHandicap;
            enableUnderdogBuff = DefEnableUnderdogBuff;
            enableAntiLeaderCoalition = DefEnableAntiLeaderCoalition;
            enableRandomDiplomacy = DefEnableRandomDiplomacy;
            enableStrongFactionWar = DefEnableStrongFactionWar;
            strongFactionWarChance = DefStrongFactionWarChance;
            strongFactionWarTopPct = DefStrongFactionWarTopPct;
            strongFactionWarRequireMidOrLate = DefStrongFactionWarRequireMidOrLate;
            enableExpansionistZeal = DefEnableExpansionistZeal;
            diplomacyChangeChance = DefDiplomacyChangeChance;
            revoltChance = DefRevoltChance;
            zealTriggerChance = DefZealTriggerChance;
            leaderHandicapTriggerChance = DefLeaderHandicapTriggerChance;
            underdogBuffTriggerChance = DefUnderdogBuffTriggerChance;
            antiLeaderCoalitionTriggerChance = DefAntiLeaderCoalitionTriggerChance;
            zealRaidRangeMult = DefZealRaidRangeMult;
            zealAttritionMult = DefZealAttritionMult;
            underdogActionShareMult = DefUnderdogActionShareMult;
            underdogIncidentWeightMult = DefUnderdogIncidentWeightMult;
            underdogIncidentSeverityMult = DefUnderdogIncidentSeverityMult;
            underdogGrowthGainMult = DefUnderdogGrowthGainMult;
            leaderIncidentWeightMult = DefLeaderIncidentWeightMult;
            leaderIncidentSeverityMult = DefLeaderIncidentSeverityMult;
            alliedRaidOrderMinWinChance = DefAlliedRaidOrderMinWinChance;
            alliedRaidClaimCostT1 = DefAlliedRaidClaimCostT1;
            alliedRaidClaimCostT2 = DefAlliedRaidClaimCostT2;
            alliedRaidClaimCostT3 = DefAlliedRaidClaimCostT3;
            alliedRaidClaimCostT4 = DefAlliedRaidClaimCostT4;
            enableSettlementBuy = DefEnableSettlementBuy;
            settlementBuyAskT1 = DefSettlementBuyAskT1;
            settlementBuyAskT2 = DefSettlementBuyAskT2;
            settlementBuyAskT3 = DefSettlementBuyAskT3;
            settlementBuyAskT4 = DefSettlementBuyAskT4;
            settlementBuySilverPerGoodwill = DefSettlementBuySilverPerGoodwill;
            settlementBuyMaxGoodwillShare = DefSettlementBuyMaxGoodwillShare;
            enableDiplomacyNegotiate = DefEnableDiplomacyNegotiate;
            negotiateAskMinSilver = DefNegotiateAskMinSilver;
            negotiateAskMaxSilver = DefNegotiateAskMaxSilver;
            enableFactionBribe = DefEnableFactionBribe;
            bribeSettlementSilverPerStrength = DefBribeSettlementSilverPerStrength;
            bribeCaravanSilverPerStrengthEarly = DefBribeCaravanSilverPerStrengthEarly;
            bribeCaravanSilverPerStrengthMid = DefBribeCaravanSilverPerStrengthMid;
            bribeCaravanSilverPerStrengthLate = DefBribeCaravanSilverPerStrengthLate;
            bribeCeasefireDaysShort = DefBribeCeasefireDaysShort;
            bribeCeasefireDaysMedium = DefBribeCeasefireDaysMedium;
            bribeCeasefireDaysLong = DefBribeCeasefireDaysLong;
            bribeCeasefireDiscountMedium = DefBribeCeasefireDiscountMedium;
            bribeCeasefireDiscountLong = DefBribeCeasefireDiscountLong;
            bribeRaidAskFloorFraction = DefBribeRaidAskFloorFraction;
            bribeInvestmentFraction = DefBribeInvestmentFraction;
            bribeCaravanInvestmentRadiusTiles = DefBribeCaravanInvestmentRadiusTiles;
            bribeGoodwillDivisor = DefBribeGoodwillDivisor;
            alliedRaidAwardCostT1 = DefAlliedRaidAwardCostT1;
            alliedRaidAwardCostT2 = DefAlliedRaidAwardCostT2;
            alliedRaidAwardCostT3 = DefAlliedRaidAwardCostT3;
            alliedRaidAwardCostT4 = DefAlliedRaidAwardCostT4;
            orderedRoadBaseCostT1 = DefOrderedRoadBaseCostT1;
            orderedRoadBaseCostT2 = DefOrderedRoadBaseCostT2;
            orderedRoadBaseCostT3 = DefOrderedRoadBaseCostT3;
            orderedRoadBaseCostT4 = DefOrderedRoadBaseCostT4;
            orderedRoadPerSegmentT1 = DefOrderedRoadPerSegmentRateT1;
            orderedRoadPerSegmentT2 = DefOrderedRoadPerSegmentRateT2;
            orderedRoadPerSegmentT3 = DefOrderedRoadPerSegmentRateT3;
            orderedTraderGoodwillCost = DefOrderedTraderGoodwillCost;
            conquestAllyGiftGoodwillT1 = DefConquestAllyGiftGoodwillT1;
            conquestAllyGiftGoodwillT2 = DefConquestAllyGiftGoodwillT2;
            conquestAllyGiftGoodwillT3 = DefConquestAllyGiftGoodwillT3;
            conquestAllyGiftGoodwillT4 = DefConquestAllyGiftGoodwillT4;
            launchPodGiftStrengthPer100MarketValue = DefLaunchPodGiftStrengthPer100MarketValue;
            enableFactionSettlementInvestment = DefEnableFactionSettlementInvestment;
            factionInvestmentStrengthPer100Silver = DefFactionInvestmentStrengthPer100Silver;
            factionInvestmentRadiusTiles = DefFactionInvestmentRadiusTiles;
            factionInvestmentUpgradeT1ToT2Silver = DefFactionInvestmentUpgradeT1ToT2Silver;
            factionInvestmentUpgradeT2ToT3Silver = DefFactionInvestmentUpgradeT2ToT3Silver;
            factionInvestmentUpgradeT3ToT4Silver = DefFactionInvestmentUpgradeT3ToT4Silver;
            factionInvestmentUpgradeSuccessChance = DefFactionInvestmentUpgradeSuccessChance;
            maxGoodwill = DefMaxGoodwill;

            durLeaderHandicapDays = DefDurLeaderHandicapDays;
            cdLeaderHandicapDays = DefCdLeaderHandicapDays;
            durUnderdogBuffDays = DefDurUnderdogBuffDays;
            cdUnderdogBuffDays = DefCdUnderdogBuffDays;
            durExpansionistZealDays = DefDurExpansionistZealDays;
            cdExpansionistZealDays = DefCdExpansionistZealDays;
            durAntiLeaderCoalitionDays = DefDurAntiLeaderCoalitionDays;
            cdAntiLeaderCoalitionDays = DefCdAntiLeaderCoalitionDays;
        }

        public void ResetGarrisons()
        {
            allowWdSettlementBaseGeneration = DefAllowWdSettlementBaseGeneration;
            kcsgMultTribalT1 = DefKcsgMultTribalT1;
            kcsgMultTribalT2 = DefKcsgMultTribalT2;
            kcsgMultTribalT3 = DefKcsgMultTribalT3;
            kcsgMultTribalT4 = DefKcsgMultTribalT4;
            kcsgMultGenericT1 = DefKcsgMultGenericT1;
            kcsgMultGenericT2 = DefKcsgMultGenericT2;
            kcsgMultGenericT3 = DefKcsgMultGenericT3;
            kcsgMultGenericT4 = DefKcsgMultGenericT4;
            garrisonOffensiveStrengthMinScale = DefGarrisonOffensiveStrengthMinScale;
        }
        /// <summary>Keeps 7-day ≥ 4-day ≥ 1-day caps and all in allowed ranges.</summary>
        public void ClampPlayerWdRaidRateCaps()
        {
            maxPlayerWdRaidsPerDay = Mathf.Clamp(maxPlayerWdRaidsPerDay, 1, 10);
            maxPlayerWdRaidsPer4Days = Mathf.Clamp(maxPlayerWdRaidsPer4Days, 1, 20);
            maxPlayerWdRaidsPer7Days = Mathf.Clamp(maxPlayerWdRaidsPer7Days, 1, 30);
            if (maxPlayerWdRaidsPer4Days < maxPlayerWdRaidsPerDay)
                maxPlayerWdRaidsPer4Days = maxPlayerWdRaidsPerDay;
            if (maxPlayerWdRaidsPer7Days < maxPlayerWdRaidsPer4Days)
                maxPlayerWdRaidsPer7Days = maxPlayerWdRaidsPer4Days;
        }

        public void ResetThreat()
        {
            allowPlayerRaid = DefAllowPlayerRaid;
            allowPlayerOutpostRaid = DefAllowPlayerOutpostRaid;
            cooldownPlayerRaidDays = DefCdPlayerRaidDays;
            maxPlayerWdRaidsPerDay = DefMaxPlayerWdRaidsPerDay;
            maxPlayerWdRaidsPer4Days = DefMaxPlayerWdRaidsPer4Days;
            maxPlayerWdRaidsPer7Days = DefMaxPlayerWdRaidsPer7Days;
            noGoodwillFromHostilesOnConquest = DefNoGoodwillFromHostilesOnConquest;
            disableSettlementProximityGoodwill = DefDisableSettlementProximityGoodwill;
            blockStorytellerRaidsOnlyWD = DefBlockStorytellerRaidsOnlyWD;
            allowStorytellerRaidsFromNonWdFactions = DefAllowStorytellerRaidsFromNonWdFactions;
            blockStorytellerTradersOnlyWD = DefBlockStorytellerTradersOnlyWD;
            notificationRadiusTiles = DefNotificationRadiusTiles;
            influenceStartTiles = DefInfluenceStartTiles;
            influenceWealthPer10k = DefInfluenceWealthPer10k;
            influencePerDay = DefInfluencePerDay;
            influencePer10kOutpostDefense = DefInfluencePer10kOutpostDefense;
            caravanRaidPointsMinStorytellerFraction = DefCaravanRaidMinStorytellerFrac;
            caravanRaidPointsMaxStorytellerFraction = DefCaravanRaidMaxStorytellerFrac;
            scaleRaidClampWithEscalation = DefScaleRaidClampWithEscalation;
            earlyRaidClampMinStorytellerFraction = DefEarlyRaidClampMinStorytellerFrac;
            earlyRaidClampMaxStorytellerFraction = DefEarlyRaidClampMaxStorytellerFrac;
            midRaidClampMinStorytellerFraction = DefMidRaidClampMinStorytellerFrac;
            midRaidClampMaxStorytellerFraction = DefMidRaidClampMaxStorytellerFrac;
            lateRaidClampMinStorytellerFraction = DefLateRaidClampMinStorytellerFrac;
            lateRaidClampMaxStorytellerFraction = DefLateRaidClampMaxStorytellerFrac;
            alwaysUseStrengthAsRaidPoints = DefAlwaysUseStrengthAsRaidPoints;
            alwaysUseStrengthAsOutpostDefenseRaidPoints = DefAlwaysUseStrengthAsOutpostDefenseRaidPoints;
            minRaidPoints = DefMinRaidPoints;
            maxRaidPoints = DefMaxRaidPoints;
            NormalizeRaidClampFractions();
        }

        /// <summary>Clamp each storyteller-fraction pair into slider ranges and ensure min ≤ max.</summary>
        public void NormalizeRaidClampFractions()
        {
            NormalizeRaidClampPair(ref caravanRaidPointsMinStorytellerFraction, ref caravanRaidPointsMaxStorytellerFraction);
            NormalizeRaidClampPair(ref earlyRaidClampMinStorytellerFraction, ref earlyRaidClampMaxStorytellerFraction);
            NormalizeRaidClampPair(ref midRaidClampMinStorytellerFraction, ref midRaidClampMaxStorytellerFraction);
            NormalizeRaidClampPair(ref lateRaidClampMinStorytellerFraction, ref lateRaidClampMaxStorytellerFraction);
        }

        private static void NormalizeRaidClampPair(ref float minFrac, ref float maxFrac)
        {
            minFrac = Mathf.Clamp(minFrac, 0.05f, 2f);
            maxFrac = Mathf.Clamp(maxFrac, 0.5f, 50f);
            if (minFrac > maxFrac)
                maxFrac = minFrac;
        }

        public void ResetLateGame()
        {
            enableLateGameScaling = DefEnableLateGameScaling;
            enableOutpostIncidents = DefEnableOutpostIncidents;
            outpostIncidentSeverity = DefOutpostIncidentSeverity;
            outpostIncidentDailyChance = DefOutpostIncidentDailyChance;
            notifyOutpostIncident = DefNotifyOutpostIncident;
            midGameShareThreshold = DefMidGameShareThreshold;
            midGameOutpostStrengthThreshold = DefMidGameOutpostStrengthThreshold;
            midGameRaidBiasPct = DefMidGameRaidBiasPct;
            midGameGrowthMult = DefMidGameGrowthMult;
            midGameAttackRangeBonusPct = DefMidGameAttackRangeBonusPct;
            enableMidGameAllyRadiusScaling = DefEnableMidGameAllyRadiusScaling;
            midGameAllyRadiusBonusPct = DefMidGameAllyRadiusBonusPct;
            midGameExpandTowardPlayerMaxTiles = DefMidGameExpandTowardPlayerMaxTiles;
            midGameGarrisonBoostPct = DefMidGameGarrisonBoostPct;
            enableMidGameOutpostIncidents = DefEnableMidGameOutpostIncidents;
            midGameOutpostIncidentSeverity = DefMidGameOutpostIncidentSeverity;
            midGameOutpostIncidentDailyChance = DefMidGameOutpostIncidentDailyChance;
            enableGoodwillDrain = DefEnableGoodwillDrain;
            goodwillDrainIntervalDays = DefGoodwillDrainIntervalDays;
            midGameGoodwillDrainAmount = DefMidGameGoodwillDrainAmount;
            lateGameGoodwillDrainAmount = DefLateGameGoodwillDrainAmount;
            lateGameShareThreshold = DefLateGameShareThreshold;
            lateGameOutpostStrengthThreshold = DefLateGameOutpostStrengthThreshold;
            lateGameRaidBiasPct = DefLateGameRaidBiasPct;
            lateGameGrowthMult = DefLateGameGrowthMult;
            lateGameAttackRangeBonusPct = DefLateGameAttackRangeBonusPct;
            enableLateGameAllyRadiusScaling = DefEnableLateGameAllyRadiusScaling;
            lateGameAllyRadiusBonusPct = DefLateGameAllyRadiusBonusPct;
            lateGameExpandTowardPlayerMaxTiles = DefLateGameExpandTowardPlayerMaxTiles;
            lateGameGarrisonBoostPct = DefLateGameGarrisonBoostPct;
            enableMidGameT4SettlementMortar = DefEnableMidGameT4SettlementMortar;
            enableMidGameT4SettlementAntiAir = DefEnableMidGameT4SettlementAntiAir;
            enableT4SettlementMortar = DefEnableT4SettlementMortar;
            enableT4SettlementAntiAir = DefEnableT4SettlementAntiAir;
            bribeSettlementSilverPerStrength = DefBribeSettlementSilverPerStrength;
            bribeCaravanSilverPerStrengthEarly = DefBribeCaravanSilverPerStrengthEarly;
            bribeCaravanSilverPerStrengthMid = DefBribeCaravanSilverPerStrengthMid;
            bribeCaravanSilverPerStrengthLate = DefBribeCaravanSilverPerStrengthLate;
            NormalizeEscalationConstraints();
        }

        /// <summary>Keep Mid share/strength ≤ Late, and Mid T4 flags imply Late T4 flags.</summary>
        public void NormalizeEscalationConstraints()
        {
            NormalizeEscalationThresholds();
            NormalizeEscalationT4Flags();
        }

        /// <summary>Fixed slider ranges; clamp stored Mid thresholds so they never exceed Late.</summary>
        public void NormalizeEscalationThresholds()
        {
            if (midGameShareThreshold > lateGameShareThreshold)
                midGameShareThreshold = lateGameShareThreshold;
            if (midGameOutpostStrengthThreshold > lateGameOutpostStrengthThreshold)
                midGameOutpostStrengthThreshold = lateGameOutpostStrengthThreshold;
        }

        /// <summary>If Mid T4 vs player is on, Late must stay on. Turning Late off also turns Mid off.</summary>
        public void NormalizeEscalationT4Flags()
        {
            if (enableMidGameT4SettlementMortar)
                enableT4SettlementMortar = true;
            else if (!enableT4SettlementMortar)
                enableMidGameT4SettlementMortar = false;

            if (enableMidGameT4SettlementAntiAir)
                enableT4SettlementAntiAir = true;
            else if (!enableT4SettlementAntiAir)
                enableMidGameT4SettlementAntiAir = false;
        }

        public void ResetRaids()
        {
            allowPlayerRaid = DefAllowPlayerRaid;
            allowPlayerOutpostRaid = DefAllowPlayerOutpostRaid;
            cooldownPlayerRaidDays = DefCdPlayerRaidDays;
            maxPlayerWdRaidsPerDay = DefMaxPlayerWdRaidsPerDay;
            maxPlayerWdRaidsPer4Days = DefMaxPlayerWdRaidsPer4Days;
            maxPlayerWdRaidsPer7Days = DefMaxPlayerWdRaidsPer7Days;
            raidTargetRadius = DefRaidTargetRadius;
            tier1AttackRangeBaseline = DefTier1AttackRangeBaseline;
            tier2AttackRangeBaseline = DefTier2AttackRangeBaseline;
            tier3AttackRangeBaseline = DefTier3AttackRangeBaseline;
            tier4AttackRangeBaseline = DefTier4AttackRangeBaseline;
            attackRangeTimeMaxBonusPct = DefAttackRangeTimeMaxBonusPct;
            attackRangeDaysToMax = DefAttackRangeDaysToMax;
            raidAllyRadius = DefRaidAllyRadius;
            WD_RadiusOverlayPrefs.ResetToDefaults();
            minRaidRatio = DefMinRaidRatio;
            razeChance = DefRazeChance;
            ruinLingerDays = DefRuinLingerDays;
            maxRaidDays = DefMaxRaidDays;
            minEfficiency = DefMinEfficiency;
            strengthLossPerHour = DefStrengthLossPerHour;
            maxTravelPercentageStrengthLoss = DefMaxTravelPercentageStrengthLoss;
            travelPrepExactPercent = DefTravelPrepExactPercent;

            garrisonRetainPct = DefGarrisonRetainPct;
            dropPodRaidChanceT3 = DefDropPodRaidChanceT3;
            dropPodRaidChance = DefDropPodRaidChance;
            dropPodRaidMinTechLevel = DefDropPodRaidMinTechLevel;
            dropPodRaidAttritionMult = DefDropPodRaidAttritionMult;
            colonySiegeRaidChance = DefColonySiegeRaidChance;
            coalitionRaidPriorityBias = DefCoalitionRaidPriorityBias;

            raidOutcomes = new List<RaidOutcome>
            {
                MakeDefaultRaidOutcome(0.00f, 0.00f, 0.80f, 0.40f, 0.02f),
                MakeDefaultRaidOutcome(0.10f, 0.03f, 0.80f, 0.5f, 0.05f),
                MakeDefaultRaidOutcome(0.25f, 0.10f, 0.80f, 0.50f, 0.10f),
                MakeDefaultRaidOutcome(0.50f, 0.20f, 0.80f, 0.60f, 0.20f),
                MakeDefaultRaidOutcome(1.00f, 0.42f, 0.60f, 0.60f, 0.40f),
                MakeDefaultRaidOutcome(1.50f, 0.58f, 0.50f, 0.60f, 0.45f),
                MakeDefaultRaidOutcome(2.00f, 0.70f, 0.45f, 0.70f, 0.50f),
                MakeDefaultRaidOutcome(3.00f, 0.88f, 0.25f, 0.70f, 0.60f),
                MakeDefaultRaidOutcome(4.00f, 0.94f, 0.15f, 0.70f, 0.60f),
                MakeDefaultRaidOutcome(5.00f, 0.95f, 0.12f, 0.70f, 0.60f),
                MakeDefaultRaidOutcome(6.00f, 0.99f, 0.10f, 0.70f, 0.60f)
            };
            raidAttLossOnWin = new List<RaidSideLossEntry>(RaidSeverityDefaults.DefaultAttWinLoss());
            raidAttLossOnLoss = new List<RaidSideLossEntry>(RaidSeverityDefaults.DefaultAttLossLoss());
            raidDefLossOnWin = new List<RaidSideLossEntry>(RaidSeverityDefaults.DefaultDefWinLoss());
            raidDefLossOnLoss = new List<RaidSideLossEntry>(RaidSeverityDefaults.DefaultDefLossLoss());
            raidAllyLossMultiplier = DefRaidAllyLossMultiplier;
            InvalidateRaidOutcomesCache();
        }

        private static RaidOutcome MakeDefaultRaidOutcome(float threshold, float winChance, float attLossWin, float defLossLoss, float defLossWin)
        {
            return new RaidOutcome
            {
                threshold = threshold,
                winChance = winChance,
                attLossWin = attLossWin,
                defLossLoss = defLossLoss,
                defLossWin = defLossWin,
                attSeverityOnAttWin = RaidSeverityDefaults.AttSeverityOnAttWinAt(threshold),
                attSeverityOnAttLoss = RaidSeverityDefaults.AttSeverityOnAttLossAt(threshold),
                defCoalitionOnAttWin = RaidSeverityDefaults.DefCoalitionOnAttWinAt(threshold),
                defCoalitionOnAttLoss = RaidSeverityDefaults.DefCoalitionOnAttLossAt(threshold)
            };
        }

        public void ResetCaravans()
        {
            strengthLossPerHour = DefStrengthLossPerHour;
            maxTravelPercentageStrengthLoss = DefMaxTravelPercentageStrengthLoss;
            allowCaravansTravelOverWater = DefAllowCaravansTravelOverWater;
            onlyTravelAcrossWaterIfNoOtherWay = DefOnlyTravelAcrossWaterIfNoOtherWay;
            travelerWaterMovementDifficulty = DefTravelerWaterMovementDifficulty;
            waterPathLandThresholdDays = DefWaterPathLandThresholdDays;
            outpostDeliveryStrengthCost = DefOutpostDeliveryStrengthCost;
            outpostDeliveryMinStrength = DefOutpostDeliveryMinStrength;
            maxGoodwill = DefMaxGoodwill;
            traderCaravanCostStrength = DefTraderCaravanCostStrength;
            traderCaravanSenderRewardStrength = DefTraderCaravanSenderRewardStrength;
            traderCaravanReceiverRewardStrength = DefTraderCaravanReceiverRewardStrength;
            traderCaravanGoodwillGain = DefTraderCaravanGoodwillGain;
            cooldownPlayerColonyTraderDays = DefCooldownPlayerColonyTraderDays;
            blockStorytellerTradersOnlyWD = DefBlockStorytellerTradersOnlyWD;
            traderTierUpgradeChanceT1ToT2 = DefTraderTierUpgradeChanceT1ToT2;
            traderTierUpgradeChanceT2ToT3 = DefTraderTierUpgradeChanceT2ToT3;
            traderTierUpgradeChanceT3ToT4 = DefTraderTierUpgradeChanceT3ToT4;
            traderEscortFloorT1 = DefTraderEscortFloorT1;
            traderEscortFloorT2 = DefTraderEscortFloorT2;
            traderEscortFloorT3 = DefTraderEscortFloorT3;
            traderEscortFloorT4 = DefTraderEscortFloorT4;
            traderEscortRecentInterceptWindowDays = DefTraderEscortRecentInterceptWindowDays;
            traderDestinationSearchRadius = DefTraderDestinationSearchRadius;
            goodwillFromTradeEnabled = DefGoodwillFromTradeEnabled;
            goodwillFromTradePer1000Silver = DefGoodwillFromTradePer1000Silver;
        }

        public void ResetSabotage()
        {
            weightSabSuccess = DefWeightSabSuccess;
            weightSabCleanFail = DefWeightSabCleanFail;
            weightSabInjuredFail = DefWeightSabInjuredFail;
            weightSabFatalFail = DefWeightSabFatalFail;
            sabotageSkillSuccessWeightBonus = DefSabSkillSuccessWeightBonus;
            sabotageTierSuccessWeightPenalty = DefSabTierSuccessWeightPenalty;
            sabotageHealthImpactWeight = DefSabHealthImpactWeight;
            sabotageSocialCleanBonus = DefSabSocialCleanBonus;
            sabotageCombatSurvivalBonus = DefSabCombatSurvivalBonus;
            sabotageBaseReduction = DefSabBaseReduc;
            sabotageSkillReductionBonus = DefSabSkillReductionBonus;
            sabotageCooldownDays = DefSabCdDays;
        }

        public void ResetDisinformation()
        {
            weightDisSuccess = DefWeightDisSuccess;
            weightDisCleanFail = DefWeightDisCleanFail;
            weightDisInjuredFail = DefWeightDisInjuredFail;
            weightDisFatalFail = DefWeightDisFatalFail;
            disSkillSuccessWeightBonus = DefDisSkillSuccessWeightBonus;
            disTierSuccessWeightPenalty = DefDisTierSuccessWeightPenalty;
            disHealthImpactWeight = DefDisHealthImpactWeight;
            disSocialCleanBonus = DefDisSocialCleanBonus;
            disCombatSurvivalBonus = DefDisCombatSurvivalBonus;
            disBaseReduction = DefDisBaseReduc;
            disSkillReductionBonus = DefDisSkillReductionBonus;
            disCooldownDays = DefDisCdDays;
        }

        public void ResetFoodLogistics()
        {
            foodLogisticsActive = DefFoodLogisticsActive;
            foodConsumptionPerPawn = DefFoodConsumptionPerPawn;
            foodProductionPerSkill = DefFoodProductionPerSkill;
            foodProductionPerOutpostBase = DefFoodProductionPerOutpostBase;
            maxFoodPerOutpost = DefMaxFoodPerOutpost;
            maxLogisticsRange = DefMaxLogisticsRange;
            virtualFoodTileMultiplierFloor = DefVirtualFoodTileMultiplierFloor;
        }

        public void ResetExperimental()
        {
            experimentalColonyWorldBuild = DefExperimentalColonyWorldBuild;
            experimentalPlayerConquestRaze = DefExperimentalPlayerConquestRaze;
            experimentalTargetOfOpportunity = DefExperimentalTargetOfOpportunity;
            targetOfOpportunityEligibilityRollPct = DefTargetOfOpportunityEligibilityRollPct;
            targetOfOpportunityMinRatioAdvantage = DefTargetOfOpportunityMinRatioAdvantage;
            targetOfOpportunityMaxRetargets = DefTargetOfOpportunityMaxRetargets;
            targetChangesMaxLifetime = DefTargetChangesMaxLifetime;
            targetOfOpportunityDogpileCooldownTicks = DefTargetOfOpportunityDogpileCooldownTicks;
            experimentalContinueAfterConquest = DefExperimentalContinueAfterConquest;
            maraudingChanceToOccurPct = DefMaraudingChanceToOccurPct;
            maraudingMinSurvivingStrengthAbsolute = DefMaraudingMinSurvivingStrengthAbsolute;
            maraudingMaxChainedTargets = DefMaraudingMaxChainedTargets;
            experimentalSettlementAmbush = DefExperimentalSettlementAmbush;
            settlementAmbushChancePct = DefSettlementAmbushChancePct;
            settlementAmbushMinStrengthRatio = DefSettlementAmbushMinStrengthRatio;
            settlementAmbushWatchRangeTiles = DefSettlementAmbushWatchRangeTiles;
            settlementAmbushMaxStrengthRatio = DefSettlementAmbushMaxStrengthRatio;
            settlementAmbushMinTier = DefSettlementAmbushMinTier;
            settlementAmbushMaxConcurrent = DefSettlementAmbushMaxConcurrent;
            opportunityFeaturesIgnoreEscalationGate = DefOpportunityFeaturesIgnoreEscalationGate;
            experimentalOutpostWithdrawStrengthBudget = DefExperimentalOutpostWithdrawStrengthBudget;
            experimentalOutpostDefenseDeployBudget = DefExperimentalOutpostDefenseDeployBudget;
            kcsgAdaptiveTerrainPrep = DefKcsgAdaptiveTerrainPrep;
            kcsgBlockedFlattenThreshold = DefKcsgBlockedFlattenThreshold;
            experimentalAlwaysClearKcsgRect = DefExperimentalAlwaysClearKcsgRect;
            experimentalKcsgRectBlend = DefExperimentalKcsgRectBlend;
            enableWorldMapSounds = DefEnableWorldMapSounds;
            enableFirstOutpostQuest = DefEnableFirstOutpostQuest;
            enableCommonEnemySettlementQuest = DefEnableCommonEnemySettlementQuest;
            enableColonyRoadLinkQuest = DefEnableColonyRoadLinkQuest;
            enableWorldDominationVictoryQuest = DefEnableWorldDominationVictoryQuest;
            enableAtTurretTargetPlayerTravelers = DefEnableAtTurretTargetPlayerTravelers;
            enableAtTurretTargetPlayerCaravans = DefEnableAtTurretTargetPlayerCaravans;
            enableOutpostUpkeep = DefEnableOutpostUpkeep;
            giveFoodOnPrisonerRecruitTransfer = DefGiveFoodOnPrisonerRecruitTransfer;
            giveFoodOnAllPlayerPawnsTransfer = DefGiveFoodOnAllPlayerPawnsTransfer;
            showOutpostRequirementsPreviewInWdMenu = DefShowOutpostRequirementsPreviewInWdMenu;
            upkeepSilverPerOccupant = DefUpkeepSilverPerOccupant;
            upkeepIntervalDays = DefUpkeepIntervalDays;
            travelerPollutionDamageEnabled = DefTravelerPollutionDamageEnabled;
            wasterPollutionImmunityEnabled = DefWasterPollutionImmunityEnabled;
            pollutionDamageRaiders = DefPollutionDamageRaiders;
            pollutionDamageExpansion = DefPollutionDamageExpansion;
            pollutionDamageConstruction = DefPollutionDamageConstruction;
            pollutionDamageTraders = DefPollutionDamageTraders;
            pollutionDamagePlayerTravelers = DefPollutionDamagePlayerTravelers;
            pollutionPathCostEnabled = DefPollutionPathCostEnabled;
            pollutionPathRepathEnabled = DefPollutionPathRepathEnabled;
            pollutionPathPreCommitCancelEnabled = DefPollutionPathPreCommitCancelEnabled;
            pollutionDamageIgnoreBelow = DefPollutionDamageIgnoreBelow;
            pollutionDamageAtThreshold = DefPollutionDamageAtThreshold;
            pollutionDamageAtFull = DefPollutionDamageAtFull;
            pollutionDamageRadius = DefPollutionDamageRadius;
            npcSettlementDecontaminationStrengthCost = DefNpcSettlementDecontaminationStrengthCost;
            alwaysShowOutpostTravelerIconsRegardlessOfZoom = DefAlwaysShowOutpostTravelerIconsRegardlessOfZoom;
            alwaysShowSettlementIconsRegardlessOfZoom = DefAlwaysShowSettlementIconsRegardlessOfZoom;
            worldMapOverlayHoldKey = DefWorldMapOverlayHoldKey;
            autoAddPawnsOnArrivalDefault = DefAutoAddPawnsOnArrivalDefault;
            Patch_WdWorldObjectNoExpandingIcon.NotifyIconModeChanged();
            WorldComponent_SettlementWatchIndex.Get()?.Invalidate();
        }

        public void ResetNotifications()
        {
            notificationRadiusTiles = DefNotificationRadiusTiles;
            notifyNewSettlement = DefNotifyNewSettlement;
            notifyNpcConquestSettlement = DefNotifyNpcConquestSettlement;
            notifySettlementRaided = DefNotifySettlementRaided;
            notifySettlementRazed = DefNotifySettlementRazed;
            notifyOutpostDestroyed = DefNotifyOutpostDestroyed;
            notifyThreatLevel = DefNotifyThreatLevel;
            notifyCriticalFood = DefNotifyCriticalFood;
            notifyDropPodDeliveryInAaRange = DefNotifyDropPodDeliveryInAaRange;
            notifyOutpostUpkeep = DefNotifyOutpostUpkeep;
            notifyConstructionInsufficientStrength = DefNotifyConstructionInsufficientStrength;
            notifyOutpostNoProduction = DefNotifyOutpostNoProduction;
            notifyOutpostUnusedExperts = DefNotifyOutpostUnusedExperts;
            notifyLateGameActive = DefNotifyLateGameActive;
            notifyMidGameActive = DefNotifyMidGameActive;
            notifyLeaderHandicap = DefNotifyLeaderHandicap;
            notifyUnderdogBuff = DefNotifyUnderdogBuff;
            notifyExpansionistZeal = DefNotifyExpansionistZeal;
            notifyAntiLeaderCoalition = DefNotifyAntiLeaderCoalition;
            notifyRandomDiplomacy = DefNotifyRandomDiplomacy;
            notifyTradeAllyDiplomacy = DefNotifyTradeAllyDiplomacy;
            notifyStrongFactionWar = DefNotifyStrongFactionWar;
            notifyDiplomaticChange = DefNotifyDiplomaticChange;
            notifyBuffNerf = DefNotifyBuffNerf;
            settingsDataVersion = CurrentSettingsDataVersion;
            // SURGICAL: Reset New Bools
            notifyIncomingRaidColony = DefNotifyIncomingRaidColony;
            notifyIncomingRaidOutpost = DefNotifyIncomingRaidOutpost;
            notifyRaidDivertedFromPlayer = DefNotifyRaidDivertedFromPlayer;
            notifyMortarHit = DefNotifyMortarHit;
            notifyAntiAirHit = DefNotifyAntiAirHit;
            notifyPlayerAntiAirVsHostileMortarShell = DefNotifyPlayerAntiAirVsHostileMortarShell;
            notifyT4AntiAirHitPlayer = DefNotifyT4AntiAirHitPlayer;
            notifyPlayerMortarShellShotDown = DefNotifyPlayerMortarShellShotDown;
            notifyNpcMortarHitPlayer = DefNotifyNpcMortarHitPlayer;
            notifyNpcMortarHitNpc = DefNotifyNpcMortarHitNpc;
            notifyPlayerAtTurretKilledTarget = DefNotifyPlayerAtTurretKilledTarget;
            notifyPlayerAtTurretDamagedTarget = DefNotifyPlayerAtTurretDamagedTarget;
            notifyPlayerAtTurretDestroyed = DefNotifyPlayerAtTurretDestroyed;
            notifyNpcAtTurretDamagedPlayer = DefNotifyNpcAtTurretDamagedPlayer;
            notifyNpcAtTurretKilledPlayer = DefNotifyNpcAtTurretKilledPlayer;
            notifyWarehouseGoodsArrived = DefNotifyWarehouseGoodsArrived;
            notifySettlementBuyStarted = DefNotifySettlementBuyStarted;
            notifySettlementBuyCompleted = DefNotifySettlementBuyCompleted;
            notifySettlementBuyAborted = DefNotifySettlementBuyAborted;
            notifyDiplomacyNegotiateStarted = DefNotifyDiplomacyNegotiateStarted;
            notifyDiplomacyNegotiateCompleted = DefNotifyDiplomacyNegotiateCompleted;
            notifyDiplomacyNegotiateAborted = DefNotifyDiplomacyNegotiateAborted;
            notifyBribeSettlementCompleted = DefNotifyBribeSettlementCompleted;
            notifyBribeSettlementAborted = DefNotifyBribeSettlementAborted;
            notifyBribeRaidCompleted = DefNotifyBribeRaidCompleted;
            notifyBribeRaidAborted = DefNotifyBribeRaidAborted;
            notifyBribeLostInTransit = DefNotifyBribeLostInTransit;
            notifyBribeCeasefireExpired = DefNotifyBribeCeasefireExpired;
            notifyOutpostDeliveryToColonyArrived = DefNotifyOutpostDeliveryToColonyArrived;
            notifyPlayerCaravanClash = DefNotifyPlayerCaravanClash;
            showCaravanClashLootDialog = DefShowCaravanClashLootDialog;
            notifyRapidResponseCaravanClash = DefNotifyRapidResponseCaravanClash;
            notifyTravelerPollutionDamage = DefNotifyTravelerPollutionDamage;
            notifyOutpostPollutionDamage = DefNotifyOutpostPollutionDamage;
            notifyPrisonerRecruitedUnderway = DefNotifyPrisonerRecruitedUnderway;
        }

        public void ResetWorldGen()
        {
            genWeightT1 = DefGenWeightT1;
            genWeightT2 = DefGenWeightT2;
            genWeightT3 = DefGenWeightT3;
            genWeightT4 = DefGenWeightT4;
            settlementTerritoryCoherence = DefSettlementTerritoryCoherence;
            settlementTerritorySpacing = DefSettlementTerritorySpacing;
            settlementOtherFactionDistance = DefSettlementOtherFactionDistance;
            settlementMaxPerCluster = DefSettlementMaxPerCluster;
            settlementMinDistanceBetweenClusters = DefSettlementMinDistanceBetweenClusters;
            worldSetupDestroyFortificationsOnRecreate = DefWorldSetupDestroyFortificationsOnRecreate;
        }

        public bool IsPairLocked(Faction a, Faction b)
        {
            if (a == null || b == null) return false;
            string key = GetFactionPairKey(a, b);
            return lockedAllegiancePairs.Contains(key);
        }

        public string GetFactionPairKey(Faction a, Faction b)
        {
            return a.loadID < b.loadID ? $"{a.loadID}_{b.loadID}" : $"{b.loadID}_{a.loadID}";
        }

        public void ResetAllegianceLocks(bool lockAllHostiles = false)
        {
            lockedAllegiancePairs = new HashSet<string>();
            if (Find.FactionManager == null) return;

            // Include the player so Perm. Hostile can lock player×permanent-enemy pairs too.
            var factions = Find.FactionManager.AllFactionsVisible
                .Where(f => f != null && (f.IsPlayer || !WorldActions_Utils.IsExcludedFaction(f)))
                .ToList();

            foreach (var f in factions)
            {
                bool fPerm = IsAllegianceLockPermanentHostile(f);
                bool fHive = IsAllegianceLockHive(f);

                foreach (var other in factions)
                {
                    if (f == other) continue;

                    bool otherPerm = IsAllegianceLockPermanentHostile(other);
                    bool otherHive = IsAllegianceLockHive(other);

                    if (fHive || otherHive)
                    {
                        lockedAllegiancePairs.Add(GetFactionPairKey(f, other));
                        continue;
                    }

                    if (lockAllHostiles)
                    {
                        // Lock every pair that includes a permanently hostile faction.
                        if (fPerm || otherPerm)
                            lockedAllegiancePairs.Add(GetFactionPairKey(f, other));
                    }
                    else if (fPerm && otherPerm && WorldActions_Utils.SafeHostileTo(f, other))
                    {
                        lockedAllegiancePairs.Add(GetFactionPairKey(f, other));
                    }
                }
            }
        }

        /// <summary>Factions that should never get WD diplomacy rolls when using Perm. Hostile lock.</summary>
        public static bool IsAllegianceLockPermanentHostile(Faction f)
        {
            if (f == null || f.IsPlayer || f.def == null) return false;
            if (f.def.permanentEnemy) return true;
            if (f.def.permanentEnemyToEveryoneExceptPlayer) return true;
            if (WorldActions_Utils.IsPermanentEnemyOfPlayer(f)) return true;
            return IsAllegianceLockHive(f);
        }

        private static bool IsAllegianceLockHive(Faction f)
        {
            if (f?.def == null) return false;
            string defName = f.def.defName ?? string.Empty;
            string name = f.Name ?? string.Empty;
            return defName.IndexOf("Insect", System.StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Hive", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public void EnsureInitialLaunchDefaults()
        {
            if (!initialAllegianceLockDone)
            {
                ResetAllegianceLocks();
                initialAllegianceLockDone = true;
            }
        }
    }
}