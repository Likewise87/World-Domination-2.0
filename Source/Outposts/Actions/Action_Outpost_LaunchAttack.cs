using System;
using System.Collections.Generic;
using UnityEngine;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    [StaticConstructorOnStartup]
    public static class Action_Outpost_LaunchAttack
    {
        private static Texture2D cachedAttackIcon;
        private static WorldObject cachedGizmoOutpost;
        private static Action cachedGizmoAction;

        public static Texture2D AttackIcon => cachedAttackIcon ??= ContentFinder<Texture2D>.Get("UI/Commands/Launch_Raid", false) ?? TexCommand.Attack;

        public static IEnumerable<Gizmo> GetGizmos(WorldObject outpost)
        {
            if (outpost == null || outpost.Faction != Faction.OfPlayer) yield break;
            if (WorldDominationMod.settings != null && !WorldDominationMod.settings.enableOutpostLaunchAttack)
                yield break;

            var comp = outpost.GetComponent<CompViralSpread>();
            var seth = WorldDominationMod.settings;

            bool onCooldown = comp != null && comp.IsRaidOnCooldown;

            if (cachedGizmoOutpost != outpost)
            {
                cachedGizmoOutpost = outpost;
                cachedGizmoAction = () => StartRaidTargeting(outpost);
            }

            Command_Action launchExpedition = new Command_Action
            {
                defaultLabel = "TSA_WD_LaunchExpedition".Translate(),
                icon = AttackIcon,
                action = cachedGizmoAction
            };

            float availableToDeploy = WorldActions_Utils.GetAvailableRaidStrength(comp, seth);

            if (onCooldown)
            {
                float daysLeft = (comp.raidCooldownTick - Find.TickManager.TicksGame) / 60000f;
                launchExpedition.defaultDesc = "TSA_WD_OnCooldown".Translate(daysLeft.ToString("F1"));
                launchExpedition.Disable("TSA_WD_ReasonCooldown".Translate());
            }
            else if (availableToDeploy <= 0)
            {
                float retainFloor = WorldActions_Utils.GetGarrisonRetainFloor(comp, seth);
                launchExpedition.Disable("TSA_WD_ReasonLowStrength".Translate(retainFloor.ToString("F0")));
            }
            else
            {
                launchExpedition.defaultDesc = "TSA_WD_LaunchExpeditionDesc".Translate();
            }

            yield return launchExpedition;
        }

        private static void StartRaidTargeting(WorldObject source)
        {
            StartRaidTargeting(source, Faction.OfPlayer, false);
        }

        public static void StartAlliedRaidTargeting(WorldObject source)
        {
            StartRaidTargeting(source, source?.Faction, true);
        }

        private static void StartRaidTargeting(WorldObject source, Faction attackerFaction, bool alliedOrder)
        {
            if (source == null || attackerFaction == null) return;
            CameraJumper.TryJump(source.Tile);

            var seth = WorldDominationMod.settings;
            var manager = Find.World.GetComponent<WorldComponent_SpreadManager>();

            float range = seth.raidTargetRadius;
            if (source is WorldObject_WD_Outpost playerOutpost && !alliedOrder && attackerFaction == Faction.OfPlayer)
                range *= 1f + OutpostExpertUtility.GetStrategistAttackRangeBonusFraction(playerOutpost);
            if (source.Faction == manager?.expansionistZealFaction && Find.TickManager.TicksGame < manager.expansionistZealExpiryTick)
            {
                range *= seth.zealRaidRangeMult;
            }

            Find.WorldTargeter.BeginTargeting(
                (target) =>
                {
                    WorldObject wo = target.WorldObject;
                    if (IsValidHostileRaidTarget(wo, attackerFaction))
                    {
                        Find.WindowStack.Add(new Dialog_OutpostRaidMath(source, wo, alliedOrder));
                        return true;
                    }
                    return false;
                },
                false, null, false,
                    () =>
                    {
                        if (source != null)
                        {
                            WD_RadiusOverlayMode.DrawOrFill(
                                source,
                                range,
                                OutpostCoverageFillKind.Red,
                                WorldOverlayLineMaterials.RadiusRed);
                        }
                    },
                null,
                (target) =>
                {
                    if (!target.IsValid || target.Tile < 0) return false;
                    if (!PlanetSurfaceWorldActions.IsPlanetSurfaceTileForWorldActions(target.Tile)) return false;
                    float dist = WorldActions_Utils.GetDistance(source.Tile, target.Tile, manager);
                    if (dist <= range && target.HasWorldObject)
                        return IsValidHostileRaidTarget(target.WorldObject, attackerFaction);
                    return false;
                }
            );
        }

        /// <summary>Hostile settlement or hostile AT turret on the planet surface (range is checked by the caller).</summary>
        private static bool IsValidHostileRaidTarget(WorldObject wo, Faction attackerFaction)
        {
            if (wo == null || wo.Destroyed || wo.Faction == null || attackerFaction == null) return false;
            if (!WorldActions_Utils.SafeHostileTo(wo.Faction, attackerFaction)) return false;
            return wo is Settlement || wo is WorldObject_AT_Turret;
        }
    }

    public static class Patch_AlliedSettlementRaidOrderGizmo
    {
        public static IEnumerable<Gizmo> GetGizmos(Settlement settlement)
        {
            if (!CanShowAlliedRaidOrderGizmo(settlement, out string disabledReason))
                yield break;

            Command_Action command = new Command_Action
            {
                defaultLabel = "TSA_WD_AlliedRaidOrder_GizmoLabel".Translate(),
                defaultDesc = "TSA_WD_AlliedRaidOrder_GizmoDesc".Translate(),
                icon = Action_Outpost_LaunchAttack.AttackIcon,
                action = () => Action_Outpost_LaunchAttack.StartAlliedRaidTargeting(settlement)
            };

            if (!disabledReason.NullOrEmpty())
                command.Disable(disabledReason);

            yield return command;
        }

        private static bool CanShowAlliedRaidOrderGizmo(Settlement settlement, out string disabledReason)
        {
            disabledReason = null;
            if (settlement == null || settlement.Destroyed || settlement.Faction == null || settlement.Faction.IsPlayer)
                return false;
            if (!PlanetSurfaceWorldActions.IsPlanetSurfaceWorldObjectForWorldActions(settlement))
                return false;
            if (WorldActions_Utils.SafeRelationKindWith(settlement.Faction, Faction.OfPlayerSilentFail) != FactionRelationKind.Ally)
                return false;

            var comp = settlement.GetComponent<CompViralSpread>();
            if (comp == null) return false;

            var seth = WorldDominationMod.settings;
            if (comp.IsRaidOnCooldown)
            {
                float daysLeft = (comp.raidCooldownTick - Find.TickManager.TicksGame) / 60000f;
                disabledReason = "TSA_WD_OnCooldown".Translate(daysLeft.ToString("F1"));
            }
            else if (WorldActions_Utils.GetAvailableRaidStrength(comp, seth) <= 0)
            {
                disabledReason = "TSA_WD_ReasonLowStrength".Translate(WorldActions_Utils.GetGarrisonRetainFloor(comp, seth).ToString("F0"));
            }

            return true;
        }
    }

    [StaticConstructorOnStartup]
    public class Dialog_OutpostRaidMath : Window
    {
        private const int AlliedRaidOrderGoodwillFloor = 10;

        private WorldObject source;
        private WorldObject target;
        private bool alliedOrder;
        private Faction attackerFaction;
        private bool TargetIsAtTurret => target is WorldObject_AT_Turret;

        private float totalAtkPower;
        private float totalDefPower;
        private float predictedEfficiency = 1.0f;
        private float travelDays;
        private bool pollutionDamageExpected;
        private bool pollutionRouteAltered;

        private List<WorldObject> atkReinforcements;
        private List<WorldObject> defReinforcements;
        private List<string> atkDetails;
        private List<string> defDetails;
        private List<RaidForceRow> atkForceRows = new List<RaidForceRow>();
        private List<RaidForceRow> defForceRows = new List<RaidForceRow>();
        /// <summary>Ally world-object IDs excluded from the raid (default: none excluded).</summary>
        private HashSet<int> excludedAllyIds = new HashSet<int>();

        private float winChance;
        private float effectiveRatio;
        private RaidOutcomeForecast raidForecast;
        private Vector2 scrollAtk;
        private Vector2 scrollDef;

        private string cachedTitleLabel;
        private string cachedTravelLine;
        private string cachedEfficiencyLine;
        private string cachedDepartureLine;
        private string cachedArrivalLine;

        public override Vector2 InitialSize => new Vector2(650f, 680f);

        public Dialog_OutpostRaidMath(WorldObject source, WorldObject target, bool alliedOrder = false)
        {
            this.source = source;
            this.target = target;
            this.alliedOrder = alliedOrder;
            attackerFaction = source?.Faction ?? Faction.OfPlayer;
            forcePause = true;
            doCloseX = true;
            absorbInputAroundWindow = true;
            CalculateMath();
        }

        private void CalculateMath()
        {
            var seth = WorldDominationMod.settings;
            var manager = Find.World.GetComponent<WorldComponent_SpreadManager>();

            var lookup = WorldActions_Utils.GetWorldObjectsWithCompByFaction();

            List<Faction> defenderCoalition;
            if (target is WorldObject_AT_Turret turret)
            {
                defReinforcements = new List<WorldObject>();
                totalDefPower = Mathf.Max(0f, turret.strength);
                defForceRows.Clear();
                RaidForceRow turretRow = RaidForceRow.FromWorldObject(
                    turret, RaidContribRole.DefenderPrimary, totalDefPower, seth, included: true, canToggle: false);
                defForceRows.Add(turretRow);
                defDetails = new List<string> { turretRow.ToLegacyDetailLine() };
                defenderCoalition = new List<Faction>();
                if (turret.Faction != null) defenderCoalition.Add(turret.Faction);
            }
            else
            {
                var defSnap = Raid_MathSnapshot.BuildDefenders(target, source, attackerFaction, lookup, manager, seth);
                defReinforcements = new List<WorldObject>(defSnap.allies.Count);
                foreach (var a in defSnap.allies) defReinforcements.Add(a.obj);
                totalDefPower = defSnap.Total;
                defDetails = defSnap.BuildDetails(seth);
                defForceRows.Clear();
                var primaryDefRow = RaidForceRow.FromDefenderEntry(defSnap.primary, seth);
                if (primaryDefRow != null) defForceRows.Add(primaryDefRow);
                foreach (var a in defSnap.allies)
                {
                    var allyRow = RaidForceRow.FromDefenderEntry(a, seth);
                    if (allyRow != null) defForceRows.Add(allyRow);
                }
                defenderCoalition = defSnap.CoalitionFactions();
            }

            atkReinforcements = Raid_MathSnapshot.GetAttackerAllies(source, target, defenderCoalition, lookup, manager, seth);

            var travelEst = TravelUtils.GetTravelStrengthEstimate(source.Tile, target.Tile, seth, source.Faction, WorldObject_Traveler.DefaultTicksPerMove);
            predictedEfficiency = travelEst.Found ? travelEst.Efficiency : 0f;
            if (travelEst.Found)
                travelDays = travelEst.TravelDays;
            else
                travelDays = WorldActions_Utils.GetDistance(source.Tile, target.Tile, manager) / 45f;

            cachedTitleLabel = (alliedOrder ? "TSA_WD_AlliedRaidOrder_PreviewTitle".Translate(source.LabelCap) : "TSA_WD_RaidAnalysis_OutpostPreview".Translate()) + ": " + target.LabelCap;
            cachedTravelLine = "TSA_WD_TimeToDestination".Translate() + ": " + travelDays.ToString("F1") + " " + "TSA_WD_Days".Translate();
            cachedEfficiencyLine = "TSA_WD_ResultingEfficiencyFactor".Translate() + ": " + predictedEfficiency.ToStringPercent();

            RefreshPollutionRouteBanner();
            RecalculateFromSelection();
        }

        /// <summary>Resum attacker totals from the scanned ally list + include toggles (no world rescan).</summary>
        private void RecalculateFromSelection()
        {
            var seth = WorldDominationMod.settings;
            atkForceRows.Clear();
            atkDetails = new List<string>();
            totalAtkPower = 0f;

            float primaryAvailable = WorldActions_Utils.GetAvailableRaidStrength(source?.GetComponent<CompViralSpread>(), seth);
            var primaryRow = RaidForceRow.FromWorldObject(source, RaidContribRole.AttackerPrimary, primaryAvailable, seth, included: true, canToggle: false);
            atkForceRows.Add(primaryRow);
            atkDetails.Add(primaryRow.ToLegacyDetailLine());
            totalAtkPower += primaryAvailable;

            if (atkReinforcements != null)
            {
                for (int i = 0; i < atkReinforcements.Count; i++)
                {
                    WorldObject ally = atkReinforcements[i];
                    if (ally == null) continue;
                    float available = WorldActions_Utils.GetAvailableRaidStrength(ally.GetComponent<CompViralSpread>(), seth);
                    bool included = !excludedAllyIds.Contains(ally.ID);
                    var row = RaidForceRow.FromWorldObject(ally, RaidContribRole.AttackerAlly, available, seth, included, canToggle: true);
                    atkForceRows.Add(row);
                    atkDetails.Add(row.ToLegacyDetailLine());
                    if (included)
                        totalAtkPower += available;
                }
            }

            float effectiveAtkPower = totalAtkPower * predictedEfficiency;
            float ratio = effectiveAtkPower / (totalDefPower > 0 ? totalDefPower : 1f);
            effectiveRatio = ratio;
            raidForecast = RaidCasualtyModel.GetForecast(ratio, seth);
            winChance = raidForecast.winChance;

            cachedDepartureLine = "TSA_WD_StrengthAtDeparture".Translate() + ": " + totalAtkPower.ToString("F0");
            string arrMath = totalAtkPower.ToString("F0") + " x " + predictedEfficiency.ToStringPercent() + " = " + effectiveAtkPower.ToString("F0");
            cachedArrivalLine = "TSA_WD_StrengthAtArrival".Translate() + ": " + arrMath;

            // Route-alter is path-based (cached from dialog open). Re-check damage expected vs current committed strength.
            if (seth != null && source != null && target != null
                && TravelerPollutionDamage.MissionTakesPollutionDamage(TravelerMission.Raid, source.Faction, seth))
            {
                var poll = PollutionPathMath.EvaluatePreview(
                    PlanetSurfaceWorldActions.PlanetTileForWdTravel(source.Tile, source),
                    PlanetSurfaceWorldActions.PlanetTileForWdTravel(target.Tile, source),
                    totalAtkPower,
                    source.Faction,
                    TravelerMission.Raid,
                    seth,
                    forceRouteCompare: false);
                pollutionDamageExpected = poll.damageExpected;
                // Keep pollutionRouteAltered from RefreshPollutionRouteBanner (dialog open).
            }
            else
                pollutionDamageExpected = false;

            WDVerbose.Msg($"RaidPreview {source.LabelCap}->{target.LabelCap}: atkDepart={totalAtkPower:F0} eff={predictedEfficiency:P0} atkArrive={effectiveAtkPower:F0} | def={totalDefPower:F0} allies={atkReinforcements?.Count ?? 0} excluded={excludedAllyIds.Count} ratio={ratio:F2} win={winChance:P0}");
        }

        private void RefreshPollutionRouteBanner()
        {
            pollutionRouteAltered = false;
            var seth = WorldDominationMod.settings;
            if (seth == null || source == null || target == null) return;
            // Same gates as path cost: only when pollution damage applies to this raid.
            if (!seth.pollutionPathCostEnabled) return;
            if (!TravelerPollutionDamage.MissionTakesPollutionDamage(TravelerMission.Raid, source.Faction, seth)) return;
            pollutionRouteAltered = PollutionPathMath.DetectRouteAltered(
                PlanetSurfaceWorldActions.PlanetTileForWdTravel(source.Tile, source),
                PlanetSurfaceWorldActions.PlanetTileForWdTravel(target.Tile, source),
                source.Faction);
        }

        private void OnToggleAttackerAlly(RaidForceRow row)
        {
            if (row?.WorldObject == null || !row.CanToggle) return;
            int id = row.WorldObject.ID;
            if (!excludedAllyIds.Remove(id))
                excludedAllyIds.Add(id);
            RecalculateFromSelection();
        }

        private bool AreAllAttackerAlliesSelected()
        {
            if (atkReinforcements == null || atkReinforcements.Count == 0) return false;
            for (int i = 0; i < atkReinforcements.Count; i++)
            {
                WorldObject ally = atkReinforcements[i];
                if (ally != null && excludedAllyIds.Contains(ally.ID))
                    return false;
            }
            return true;
        }

        private void OnToggleSelectAllAttackerAllies()
        {
            if (atkReinforcements == null || atkReinforcements.Count == 0) return;
            if (AreAllAttackerAlliesSelected())
            {
                for (int i = 0; i < atkReinforcements.Count; i++)
                {
                    WorldObject ally = atkReinforcements[i];
                    if (ally != null)
                        excludedAllyIds.Add(ally.ID);
                }
            }
            else
            {
                excludedAllyIds.Clear();
            }
            RecalculateFromSelection();
        }

        private List<WorldObject> GetIncludedAttackers()
        {
            var list = new List<WorldObject> { source };
            if (atkReinforcements == null) return list;
            for (int i = 0; i < atkReinforcements.Count; i++)
            {
                WorldObject ally = atkReinforcements[i];
                if (ally != null && !excludedAllyIds.Contains(ally.ID))
                    list.Add(ally);
            }
            return list;
        }

        public override void DoWindowContents(Rect inRect)
        {
            var seth = WorldDominationMod.settings;
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);

            Text.Font = GameFont.Medium;
            listing.Label(cachedTitleLabel);
            Text.Font = GameFont.Small;
            listing.GapLine();

            float effectiveAtk = totalAtkPower * predictedEfficiency;
            Rect powers = listing.GetRect(70f);
            RaidUIUtils.DrawRaidPowerBoxes(powers, effectiveAtk, totalDefPower, "TSA_WD_Attackers", "TSA_WD_Defender");

            listing.Gap(12f);

            Rect mathRect = listing.GetRect(50f);
            Rect leftCol = mathRect.LeftHalf();
            Rect rightCol = mathRect.RightHalf();
            Widgets.Label(leftCol.TopHalf(), cachedTravelLine);
            Widgets.Label(leftCol.BottomHalf(), cachedEfficiencyLine);
            Widgets.Label(rightCol.TopHalf(), cachedDepartureLine);
            Widgets.Label(rightCol.BottomHalf(), cachedArrivalLine);

            listing.Gap(12f);
            PollutionRaidUi.DrawBanners(listing, pollutionDamageExpected, pollutionRouteAltered);

            listing.Gap(12f);
            float relStr = (totalDefPower > 0) ? (effectiveAtk / totalDefPower) : effectiveAtk;
            RaidUIUtils.DrawRaidForecast(listing, raidForecast, effectiveRatio, defenderPerspective: false,
                "TSA_WD_RelativeStrength".Translate(relStr.ToString("F2")));

            listing.Gap(15f);
            listing.Label("TSA_WD_ReinforcementBreakdown".Translate());
            Rect breakdownRect = listing.GetRect(180f);
            RaidUIUtils.DrawRaidForceBreakdownScrolls(
                breakdownRect,
                atkForceRows,
                defForceRows,
                atkDetails,
                defDetails,
                ref scrollAtk,
                ref scrollDef,
                OnToggleAttackerAlly,
                AreAllAttackerAlliesSelected,
                OnToggleSelectAllAttackerAllies);

            listing.GapLine();

            if (alliedOrder)
                DrawAlliedOrderButtons(listing);
            else
                DrawPlayerOutpostExecuteButton(listing);

            if (listing.ButtonText("Cancel".Translate())) Close();

            listing.End();
        }

        private void DrawPlayerOutpostExecuteButton(Listing_Standard listing)
        {
            Rect executeRect = listing.GetRect(35f);
            if (Widgets.ButtonText(executeRect, "TSA_WD_ExecuteRaid".Translate()))
            {
                if (ExecuteRaid(RaidOrderOutcome.PlayerOutpostConquestMenu))
                    Close();
            }
        }

        private void DrawAlliedOrderButtons(Listing_Standard listing)
        {
            WorldDominationSettings seth = WorldDominationMod.settings;
            SettlementTier tier = target.GetComponent<CompViralSpread>()?.tier ?? SettlementTier.T1;
            int claimCost = seth.GetAlliedRaidGoodwillCost(tier, false);
            int awardCost = seth.GetAlliedRaidGoodwillCost(tier, true);
            float minWinChance = Mathf.Clamp01(seth.alliedRaidOrderMinWinChance);
            Faction ally = source?.Faction;
            Faction player = Faction.OfPlayerSilentFail;
            int currentGoodwill = ally?.RelationWith(player, true)?.baseGoodwill ?? 0;

            bool winGateMet = winChance >= minWinChance;
            if (!winGateMet)
            {
                Rect warningRect = listing.GetRect(36f);
                Widgets.Label(warningRect, "TSA_WD_AlliedRaidOrder_WinChanceTooLow".Translate(minWinChance.ToStringPercent()));
            }

            if (TargetIsAtTurret)
            {
                DrawAlliedOrderButton(
                    listing.GetRect(35f),
                    "TSA_WD_AlliedRaidOrder_DestroyTurret".Translate(claimCost, currentGoodwill),
                    claimCost,
                    winGateMet,
                    RaidOrderOutcome.AllyClaimsTarget);
                return;
            }

            DrawAlliedOrderButton(listing.GetRect(35f), "TSA_WD_AlliedRaidOrder_ClaimForAlly".Translate(claimCost, currentGoodwill), claimCost, winGateMet, RaidOrderOutcome.AllyClaimsTarget);
            listing.Gap(4f);
            DrawAlliedOrderButton(listing.GetRect(35f), "TSA_WD_AlliedRaidOrder_AwardToPlayer".Translate(awardCost, currentGoodwill), awardCost, winGateMet, RaidOrderOutcome.AllyAwardsToPlayer);
        }

        private void DrawAlliedOrderButton(Rect rect, string label, int goodwillCost, bool winGateMet, RaidOrderOutcome outcome)
        {
            Faction ally = source?.Faction;
            Faction player = Faction.OfPlayerSilentFail;
            int goodwill = ally?.RelationWith(player, true)?.baseGoodwill ?? 0;
            bool canAfford = CanPayAlliedRaidOrderCost(goodwill, goodwillCost);
            bool enabled = winGateMet && canAfford;
            string disabledReason = !winGateMet
                ? "TSA_WD_AlliedRaidOrder_DisabledLowWinChance".Translate()
                : (!canAfford ? "TSA_WD_AlliedRaidOrder_DisabledGoodwill".Translate(goodwillCost, goodwill, AlliedRaidOrderGoodwillFloor) : null);

            bool oldEnabled = GUI.enabled;
            GUI.enabled = oldEnabled && enabled;
            if (Widgets.ButtonText(rect, label))
            {
                if (ExecuteRaid(outcome))
                    Close();
            }
            GUI.enabled = oldEnabled;
            if (!disabledReason.NullOrEmpty())
                TooltipHandler.TipRegion(rect, disabledReason);
        }

        /// <returns>True when the raid traveler launched (caller should close the dialog).</returns>
        private bool ExecuteRaid(RaidOrderOutcome outcome)
        {
            var seth = WorldDominationMod.settings;
            var sourceComp = source.GetComponent<CompViralSpread>();
            var manager = Find.World.GetComponent<WorldComponent_SpreadManager>();

            if (sourceComp == null || seth == null) return false;
            if (alliedOrder && winChance < Mathf.Clamp01(seth.alliedRaidOrderMinWinChance)) return false;
            int alliedGoodwillPaid = 0;
            if (alliedOrder && !TryPayAlliedRaidOrderCost(outcome, out alliedGoodwillPaid)) return false;

            WorldObjectDef travelerDef = DefDatabase<WorldObjectDef>.GetNamed("TSA_WD_Traveler_Outpost_Raid", false);
            if (travelerDef == null) return false;

            int raidCdUntil = Find.TickManager.TicksGame + Mathf.RoundToInt(seth.cooldownRaidDays * 60000f);

            try
            {
                WorldObject_Traveler_Outpost_Raid traveler = (WorldObject_Traveler_Outpost_Raid)WorldObjectMaker.MakeWorldObject(travelerDef);
                traveler.Tile = source.Tile;
                traveler.SetFaction(source.Faction);
                traveler.mission = TravelerMission.Raid;
                traveler.originObject = source;
                traveler.targetObject = target;
                traveler.raidOrderOutcome = outcome;
                traveler.alliedRaidOrderGoodwillPaid = alliedGoodwillPaid;
                traveler.alliedRaidOrderGoodwillRefunded = false;

                if (traveler.contributionFactors == null) traveler.contributionFactors = new Dictionary<WorldObject, float>();

                List<WorldObject> fullAtkList = GetIncludedAttackers();

                Dictionary<WorldObject, float> contributions = new Dictionary<WorldObject, float>();
                float totalInvestedPower = 0f;

                foreach (WorldObject wo in fullAtkList)
                {
                    var comp = wo.GetComponent<CompViralSpread>();
                    float available = WorldActions_Utils.GetAvailableRaidStrength(comp, seth);
                    contributions[wo] = available;
                    totalInvestedPower += available;
                }

                traveler.travelerStrength = totalInvestedPower;
                traveler.initialStrength = totalInvestedPower;

                var logAtkRows = new List<RaidForceRow>();
                var logAtkDetails = new List<string>();
                for (int i = 0; i < atkForceRows.Count; i++)
                {
                    RaidForceRow row = atkForceRows[i];
                    if (row == null || !row.Included) continue;
                    logAtkRows.Add(row);
                    logAtkDetails.Add(row.ToLegacyDetailLine());
                }

                foreach (var kvp in contributions)
                {
                    if (totalInvestedPower > 0)
                        traveler.contributionFactors.Add(kvp.Key, kvp.Value / totalInvestedPower);
                }

                traveler.raidAttackerList = fullAtkList;
                traveler.raidAttackerDetails = logAtkDetails;
                traveler.raidAttackerForceRows = RaidForceLogRow.FromLiveRows(logAtkRows);
                traveler.raidDefenderForceRows = RaidForceLogRow.FromLiveRows(defForceRows);

                // Path first; deduct strength only after pollution pre-commit passes.
                Find.WorldObjects.Add(traveler);
                traveler.pather.StartPath(PlanetSurfaceWorldActions.PlanetTileForWdTravel(target.Tile, source));

                var pollutionOutcome = RaidPollutionPreCommit.EvaluateAndMaybeCancel(traveler, source, target, manager, seth);
                if (pollutionOutcome.cancelled)
                {
                    if (alliedGoodwillPaid > 0)
                        Raid_Simulated.RefundAlliedRaidOrderGoodwill(traveler);
                    Messages.Message("TSA_WD_Msg_RaidCancelledPollution".Translate(pollutionOutcome.expectedLoss.ToString("F0")), MessageTypeDefOf.RejectInput, false);
                    return false;
                }

                foreach (var kvp in contributions)
                {
                    var comp = kvp.Key.GetComponent<CompViralSpread>();
                    if (comp == null) continue;
                    comp.raidCooldownTick = raidCdUntil;
                    if (kvp.Value > 0)
                    {
                        comp.strength -= kvp.Value;
                        comp.CheckTierUpdate(false);
                    }
                }

                if (!traveler.Destroyed)
                {
                    int reservedUntilTick = Raid_DefenseCooldownReservations.ApplyRaidDefenseCooldownReservation(target);
                    traveler.targetRaidDefenseCooldownReservationTick = reservedUntilTick;
                    if (target is Settlement playerRaidTarget && playerRaidTarget.Faction?.IsPlayer == true && playerRaidTarget.HasMap)
                        traveler.playerColonyRaidCooldownReservationTick = reservedUntilTick;
                }

                string launchMessage = alliedOrder
                    ? "TSA_WD_Log_AlliedRaidLaunchedByPlayer".Translate(source.Label, target.Label)
                    : "TSA_WD_Log_PlayerRaidLaunched".Translate(source.Label, target.Label);
                SpreadLogEntry launchLog = new SpreadLogEntry(launchMessage, source, target);
                launchLog.isRaid = true;
                launchLog.isAttempt = true;
                launchLog.attStr = totalInvestedPower;
                launchLog.defStr = totalDefPower;
                launchLog.winChance = winChance;
                launchLog.ratio = effectiveRatio;

                float pathTicks = traveler.CachedLaunchTotalTravelTicks;
                launchLog.pathTravelTicks = pathTicks;
                if (pathTicks >= 0f && TravelUtils.TryEfficiencyFromPathTravelTicks(pathTicks, seth, source.Faction, out float effFromPath))
                    launchLog.efficiencyFactor = effFromPath;
                else
                {
                    var est = TravelUtils.GetTravelStrengthEstimate(source.Tile, target.Tile, seth, source.Faction, WorldObject_Traveler.DefaultTicksPerMove);
                    launchLog.pathTravelTicks = est.Found ? est.TravelTicks : -1f;
                    launchLog.efficiencyFactor = est.Found ? est.Efficiency : predictedEfficiency;
                }

                launchLog.targetDistance = WorldActions_Utils.GetDistance(source.Tile, target.Tile, manager);
                launchLog.attDetails = logAtkDetails;
                launchLog.defDetails = new List<string>(defDetails);
                launchLog.attForceRows = RaidForceLogRow.FromLiveRows(logAtkRows);
                launchLog.defForceRows = RaidForceLogRow.FromLiveRows(defForceRows);
                RaidPollutionPreCommit.ApplyFlagsToLog(launchLog, pollutionOutcome);

                foreach (var kvp in traveler.contributionFactors)
                {
                    launchLog.contributionDNAKeys.Add(kvp.Key?.LabelCap ?? "Unknown");
                    launchLog.contributionDNAValues.Add(kvp.Value);
                }

                manager.AddLog(launchLog);
                WDVerbose.Msg($"RaidLaunch {source.LabelCap}->{target.LabelCap}: committed={totalInvestedPower:F0} (contributors={fullAtkList.Count}) def={totalDefPower:F0} win={winChance:P0} outcome={outcome}");

                // Player outpost raid (not allied-settlement order): credit common-enemy quest target.
                if (!alliedOrder
                    && source.Faction != null
                    && source.Faction.IsPlayer
                    && target is Settlement playerRaidSettlement)
                {
                    WdCommonEnemySettlementQuestHelper.NotifyPlayerAttributedStrike(playerRaidSettlement);
                }

                string message = alliedOrder
                    ? null
                    : "TSA_WD_RaidExpeditionLaunched".Translate(target.Label);
                if (!message.NullOrEmpty())
                    Messages.Message(message, MessageTypeDefOf.TaskCompletion);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("TSA_WD: Failed to spawn Outpost Raider: " + ex.Message);
                return false;
            }
        }

        private bool TryPayAlliedRaidOrderCost(RaidOrderOutcome outcome, out int paidCost)
        {
            paidCost = 0;
            Faction ally = source?.Faction;
            Faction player = Faction.OfPlayerSilentFail;
            if (ally == null || player == null) return false;
            if (WorldActions_Utils.SafeRelationKindWith(ally, player) != FactionRelationKind.Ally) return false;

            SettlementTier tier = target.GetComponent<CompViralSpread>()?.tier ?? SettlementTier.T1;
            int cost = WorldDominationMod.settings.GetAlliedRaidGoodwillCost(tier, outcome == RaidOrderOutcome.AllyAwardsToPlayer);
            int goodwill = ally.RelationWith(player, true)?.baseGoodwill ?? 0;
            if (!CanPayAlliedRaidOrderCost(goodwill, cost))
            {
                Messages.Message("TSA_WD_AlliedRaidOrder_NotEnoughGoodwill".Translate(cost, goodwill, AlliedRaidOrderGoodwillFloor), source, MessageTypeDefOf.RejectInput);
                return false;
            }
            if (!GoodwillChangeNotifier.TryPayAlliedRaidOrder(ally, target, cost, out _))
                return false;
            paidCost = cost;
            return true;
        }

        private static bool CanPayAlliedRaidOrderCost(int currentGoodwill, int cost)
        {
            return currentGoodwill - Mathf.Max(0, cost) >= AlliedRaidOrderGoodwillFloor;
        }
    }
}
