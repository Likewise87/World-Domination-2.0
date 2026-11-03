using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Player outpost under attack: same analysis layout as <see cref="Dialog_OutpostRaidMath"/>,
    /// with forecast and copy oriented to the defending (player) side.
    /// </summary>
    public class Dialog_OutpostDefenseChoice : Window
    {
        private readonly WorldObject_Traveler traveler;
        private readonly WorldObject_WD_Outpost outpost;
        private readonly WorldComponent_SpreadManager manager;
        private readonly bool isSkirmishFollowUp;
        private readonly bool isTrenchIntercept;

        private float attackerStrength;
        private float defenderStrength;
        private float attackerRatio;
        private RaidOutcomeForecast defenderForecast;

        private List<RaidForceRow> atkForceRows = new List<RaidForceRow>();
        private List<RaidForceRow> defForceRows = new List<RaidForceRow>();
        private List<string> atkDetails = new List<string>();
        private List<string> defDetails = new List<string>();
        private Vector2 scrollAtk;
        private Vector2 scrollDef;

        private string cachedTitleLabel;
        private string cachedArrivalLine;

        public override Vector2 InitialSize => new Vector2(650f, isSkirmishFollowUp || isTrenchIntercept ? 700f : 620f);

        public Dialog_OutpostDefenseChoice(
            WorldObject_Traveler traveler,
            WorldObject_WD_Outpost outpost,
            WorldComponent_SpreadManager manager,
            bool isSkirmishFollowUp = false,
            bool isTrenchIntercept = false)
        {
            this.traveler = traveler;
            this.outpost = outpost;
            this.manager = manager;
            this.isSkirmishFollowUp = isSkirmishFollowUp;
            this.isTrenchIntercept = isTrenchIntercept;
            doCloseX = false;
            doCloseButton = false;
            absorbInputAroundWindow = true;
            forcePause = true;
            closeOnAccept = false;
            closeOnCancel = false;
            CalculateDefenseMath();
        }

        public bool IsForOutpost(WorldObject_WD_Outpost candidate)
            => outpost != null && candidate != null && outpost == candidate;

        public bool IsForTraveler(WorldObject_Traveler candidate)
            => traveler != null && candidate != null && traveler == candidate;

        private void CalculateDefenseMath()
        {
            attackerStrength = Mathf.Max(0f, traveler?.travelerStrength ?? 0f);
            defenderStrength = 0f;
            attackerRatio = 0f;
            defenderForecast = default;
            atkForceRows.Clear();
            defForceRows.Clear();
            atkDetails = new List<string>();
            defDetails = new List<string>();

            var seth = WorldDominationMod.settings;
            cachedTitleLabel = isTrenchIntercept
                ? "TSA_WD_OutpostDefense_TrenchTitle".Translate().ToString()
                : isSkirmishFollowUp
                    ? "TSA_WD_OutpostDefense_SkirmishTitle".Translate().ToString()
                    : "TSA_WD_OutpostDefense_ChoiceTitle".Translate().ToString();

            float departed = traveler != null && traveler.initialStrength > 0f
                ? traveler.initialStrength
                : attackerStrength;
            float efficiency = departed > 0.01f ? Mathf.Clamp01(attackerStrength / departed) : 1f;
            string arrMath = departed.ToString("F0") + " x " + efficiency.ToStringPercent() + " = " + attackerStrength.ToString("F0");
            cachedArrivalLine = "TSA_WD_StrengthAtArrival".Translate() + ": " + arrMath;

            if (traveler == null || outpost == null)
                return;

            var lookup = WorldActions_Utils.GetWorldObjectsWithCompByFaction();
            var defSnap = Raid_MathSnapshot.BuildDefenders(outpost, traveler.originObject, traveler.Faction, lookup, manager, seth);
            defenderStrength = defSnap.Total;
            defDetails = defSnap.BuildDetails(seth);
            defForceRows = defSnap.BuildForceRows(seth);

            BuildAttackerForceRows(seth, attackerStrength);

            attackerRatio = attackerStrength / (defenderStrength > 0f ? defenderStrength : 1f);
            defenderForecast = RaidCasualtyModel.GetForecast(attackerRatio, seth, traveler?.Faction, outpost?.Faction);
        }

        private void BuildAttackerForceRows(WorldDominationSettings seth, float arrivedStrength)
        {
            atkForceRows.Clear();
            atkDetails = new List<string>();

            if (traveler?.contributionFactors != null && traveler.contributionFactors.Count > 0)
            {
                var ordered = new List<WorldObject>();
                WorldObject origin = traveler.originObject;
                if (origin != null && traveler.contributionFactors.ContainsKey(origin))
                    ordered.Add(origin);

                if (traveler.raidAttackerList != null)
                {
                    for (int i = 0; i < traveler.raidAttackerList.Count; i++)
                    {
                        WorldObject wo = traveler.raidAttackerList[i];
                        if (wo == null || ordered.Contains(wo)) continue;
                        if (traveler.contributionFactors.ContainsKey(wo))
                            ordered.Add(wo);
                    }
                }

                foreach (var kv in traveler.contributionFactors)
                {
                    if (kv.Key != null && !ordered.Contains(kv.Key))
                        ordered.Add(kv.Key);
                }

                for (int i = 0; i < ordered.Count; i++)
                {
                    WorldObject wo = ordered[i];
                    if (wo == null) continue;
                    float share = traveler.contributionFactors.TryGetValue(wo, out float factor)
                        ? factor * arrivedStrength
                        : 0f;
                    RaidContribRole role = i == 0 ? RaidContribRole.AttackerPrimary : RaidContribRole.AttackerAlly;
                    RaidForceRow row = RaidForceRow.FromWorldObject(wo, role, share, seth, included: true, canToggle: false);
                    atkForceRows.Add(row);
                    atkDetails.Add(row.ToLegacyDetailLine());
                }
                return;
            }

            if (traveler?.raidAttackerForceRows != null && traveler.raidAttackerForceRows.Count > 0)
            {
                atkForceRows = RaidForceLogRow.ToDisplayRows(traveler.raidAttackerForceRows);
                for (int i = 0; i < atkForceRows.Count; i++)
                {
                    if (atkForceRows[i] != null)
                        atkDetails.Add(atkForceRows[i].ToLegacyDetailLine());
                }
                return;
            }

            if (traveler?.raidAttackerDetails != null && traveler.raidAttackerDetails.Count > 0)
            {
                atkDetails = new List<string>(traveler.raidAttackerDetails);
                return;
            }

            WorldObject fallback = traveler?.originObject;
            if (fallback != null)
            {
                RaidForceRow row = RaidForceRow.FromWorldObject(
                    fallback, RaidContribRole.AttackerPrimary, arrivedStrength, seth, included: true, canToggle: false);
                atkForceRows.Add(row);
                atkDetails.Add(row.ToLegacyDetailLine());
                return;
            }

            var synthetic = new RaidForceRow
            {
                Label = traveler?.Faction?.Name ?? "?",
                Faction = traveler?.Faction,
                Role = RaidContribRole.AttackerPrimary,
                Committed = arrivedStrength,
                DisplayStrength = arrivedStrength,
                Included = true,
                CanToggle = false,
            };
            synthetic.Tooltip = RaidForceRow.BuildTooltip(synthetic);
            atkForceRows.Add(synthetic);
            atkDetails.Add(synthetic.ToLegacyDetailLine());
        }

        public override void DoWindowContents(Rect inRect)
        {
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);

            Text.Font = GameFont.Medium;
            listing.Label(cachedTitleLabel);
            Text.Font = GameFont.Small;
            listing.GapLine();

            if (isSkirmishFollowUp)
                DrawSkirmishLossBanner(listing);

            if (isTrenchIntercept)
            {
                listing.Label("TSA_WD_OutpostDefense_TrenchText".Translate(
                    outpost?.LabelCap ?? "Outpost",
                    traveler?.Faction?.Name ?? "Unknown"));
            }
            else if (isSkirmishFollowUp)
            {
                listing.Label("TSA_WD_OutpostDefense_SkirmishText".Translate(
                    outpost?.LabelCap ?? "Outpost",
                    traveler?.Faction?.Name ?? "Unknown"));
            }
            else
            {
                listing.Label("TSA_WD_OutpostDefense_ChoiceText".Translate(
                    outpost?.LabelCap ?? "Outpost",
                    traveler?.Faction?.Name ?? "Unknown"));
            }
            listing.Gap(8f);

            Rect powers = listing.GetRect(70f);
            RaidUIUtils.DrawRaidPowerBoxes(powers, attackerStrength, defenderStrength, "TSA_WD_Attackers", "TSA_WD_Defender");

            listing.Gap(12f);
            listing.Label(cachedArrivalLine);

            listing.Gap(8f);
            float relStr = defenderStrength > 0f ? (defenderStrength / attackerStrength) : defenderStrength;
            RaidUIUtils.DrawRaidForecast(listing, defenderForecast, attackerRatio, defenderPerspective: true,
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
                ref scrollDef);

            listing.GapLine();

            Rect utilityRow = listing.GetRect(36f);
            if (Widgets.ButtonText(utilityRow, "TSA_WD_OutpostDefense_GoToOutpost".Translate()))
                GoToOutpost();

            listing.Gap(8f);
            if (isTrenchIntercept)
            {
                Rect actionRow = listing.GetRect(42f);
                float third = (actionRow.width - 12f) / 3f;
                Rect autoRect = new Rect(actionRow.x, actionRow.y, third, actionRow.height);
                Rect manualRect = new Rect(actionRow.x + third + 6f, actionRow.y, third, actionRow.height);
                Rect skipRect = new Rect(actionRow.x + 2f * (third + 6f), actionRow.y, third, actionRow.height);
                if (Widgets.ButtonText(autoRect, "TSA_WD_OutpostDefense_AutoResolve".Translate()))
                {
                    WdTrenchInterceptUtility.MarkResolved(traveler, outpost);
                    outpost?.ClearPendingSkirmishDefense();
                    Close();
                    traveler.targetObject = outpost;
                    Raid_Simulated.ResolvePlayerOutpostRaidArrival(traveler, manager, allowSkirmishRetry: false);
                    if (traveler != null && !traveler.Destroyed)
                    {
                        traveler.suppressDestroyedWorldFx = true;
                        traveler.Destroy();
                    }
                }
                if (Widgets.ButtonText(manualRect, "TSA_WD_OutpostDefense_FightManually".Translate()))
                {
                    // Keep the live column through Deploy (same contract as hub Fight). Early Destroy
                    // made CameFromWorldTile return -1 (wrong trench hostile edge) and broke PostClose
                    // cancel. MarkResolved waits until Deploy confirms so MotionGuard still parks here.
                    Close();
                    if (traveler != null && !traveler.Destroyed)
                        traveler.targetObject = outpost;
                    Find.WindowStack.Add(new Dialog_OutpostDefenseDeploy(traveler, outpost, manager, isSkirmishFollowUp, isTrenchCorridor: true));
                }
                if (Widgets.ButtonText(skipRect, "TSA_WD_OutpostDefense_SkipTrench".Translate()))
                {
                    WdTrenchInterceptUtility.MarkResolved(traveler, outpost);
                    Close();
                    ResumeTravelerAfterTrenchSkip();
                }
            }
            else
            {
                Rect actionRow = listing.GetRect(42f);
                float halfButtonWidth = (actionRow.width - 6f) / 2f;
                Rect autoRect = new Rect(actionRow.x, actionRow.y, halfButtonWidth, actionRow.height);
                Rect manualRect = new Rect(actionRow.x + halfButtonWidth + 6f, actionRow.y, halfButtonWidth, actionRow.height);
                if (Widgets.ButtonText(autoRect, "TSA_WD_OutpostDefense_AutoResolve".Translate()))
                {
                    bool allowSkirmish = !isSkirmishFollowUp;
                    outpost?.ClearPendingSkirmishDefense();
                    Close();
                    Raid_Simulated.ResolvePlayerOutpostRaidArrival(traveler, manager, allowSkirmishRetry: allowSkirmish);
                }
                if (Widgets.ButtonText(manualRect, "TSA_WD_OutpostDefense_FightManually".Translate()))
                {
                    Close();
                    Find.WindowStack.Add(new Dialog_OutpostDefenseDeploy(traveler, outpost, manager, isSkirmishFollowUp));
                }
            }

            listing.End();
        }

        private void ResumeTravelerAfterTrenchSkip()
        {
            if (traveler == null || traveler.Destroyed) return;
            WorldObject dest = traveler.targetObject;
            if (TravelerEndpointUtility.IsLiveEndpoint(dest))
                traveler.pather?.StartPath(dest.Tile);
            else if (traveler.pather != null && traveler.pather.destTile.Valid)
                traveler.pather.StartPath(traveler.pather.destTile);
        }

        private void DrawSkirmishLossBanner(Listing_Standard listing)
        {
            float strengthLost = outpost?.PendingSkirmishStrengthLost ?? 0f;
            string text = "TSA_WD_OutpostDefense_SkirmishLossBanner".Translate(strengthLost.ToString("F0")).ToString();
            Text.Font = GameFont.Small;
            float textH = Mathf.Max(24f, Text.CalcHeight(text, listing.ColumnWidth - 12f));
            float boxH = textH + 12f;
            Rect boxRect = listing.GetRect(boxH);
            Widgets.DrawBoxSolid(boxRect, Outpost_Dialog_UI.SkillDrBoxYellow);
            Widgets.DrawBox(boxRect);
            GUI.color = Color.yellow;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(boxRect.ContractedBy(6f), text);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            listing.Gap(6f);
        }

        private void GoToOutpost()
        {
            if (outpost == null || outpost.Destroyed) return;
            windowRect.x = Mathf.Max(20f, UI.screenWidth - windowRect.width - 30f);
            windowRect.y = Mathf.Max(20f, (UI.screenHeight - windowRect.height) * 0.5f);
            CameraJumper.TryJump(outpost);
            Find.WorldSelector.ClearSelection();
            Find.WorldSelector.Select(outpost);
        }
    }
}
