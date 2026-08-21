using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    [StaticConstructorOnStartup]
    public static class WD_Outpost_RapidResponse
    {
        private static Texture2D cachedConfigureIcon;
        private static Texture2D cachedCancelInterceptIcon;

        public static IEnumerable<Gizmo> GetGizmos(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null) yield break;
            if (outpost.Faction != Faction.OfPlayer) yield break;
            if (!outpost.IsRapidResponseOutpost) yield break;

            yield return new Command_Action
            {
                defaultLabel = "TSA_WD_RapidResponse_ConfigureDefense".Translate(),
                defaultDesc = "TSA_WD_RapidResponse_ConfigureDefenseDesc".Translate(
                    RapidResponseUtility.GetRangeTiles(outpost).ToString("F0"),
                    RapidResponseUtility.GetDeployableStrength(outpost, outpost.GetComponent<CompViralSpread>()).ToString("F0")),
                icon = cachedConfigureIcon ??= ContentFinder<Texture2D>.Get("UI/Commands/RapidResponseRadius", false) ?? TexCommand.Attack,
                action = () => Dialog_OutpostRangeAdjust.Open(outpost, OutpostRangeAdjustMode.RapidResponse),
                onHover = () =>
                {
                    if (outpost == null || outpost.Destroyed) return;
                    float range = RapidResponseUtility.GetRangeTiles(outpost);
                    WD_RadiusOverlayMode.DrawOrFill(
                        outpost,
                        range,
                        OutpostCoverageFillKind.Purple,
                        WorldOverlayLineMaterials.RecruitTradingRadiusRing);
                }
            };
        }

        /// <summary>Cancel gizmo on an in-flight rapid-response intercept caravan: destroy and refund strength immediately.</summary>
        public static IEnumerable<Gizmo> GetTravelerGizmos(WorldObject_Traveler traveler)
        {
            if (traveler == null || traveler.Destroyed) yield break;
            if (traveler.mission != TravelerMission.RapidResponseIntercept) yield break;
            if (traveler.Faction != Faction.OfPlayer) yield break;

            yield return new Command_Action
            {
                defaultLabel = "TSA_WD_RapidResponse_CancelIntercept".Translate(),
                defaultDesc = "TSA_WD_RapidResponse_CancelInterceptDesc".Translate(),
                icon = cachedCancelInterceptIcon ??= ContentFinder<Texture2D>.Get("UI/Designators/Cancel", false) ?? TexCommand.ClearPrioritizedWork,
                action = () =>
                {
                    if (traveler == null || traveler.Destroyed) return;
                    string originLabel = traveler.originObject?.LabelCap ?? traveler.LabelCap;
                    float refund = traveler.travelerStrength;
                    TravelerEndpointUtility.AbortTraveler(
                        traveler,
                        "TSA_WD_Log_RapidResponseCancelled".Translate(originLabel, refund.ToString("F0")));
                    Messages.Message(
                        "TSA_WD_RapidResponse_InterceptCancelled".Translate(originLabel, refund.ToString("F0")),
                        MessageTypeDefOf.NeutralEvent);
                }
            };
        }

        public static string GetInspectStatusLine(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null || !outpost.IsRapidResponseOutpost) return "";
            if (!outpost.RapidResponseActive)
                return "TSA_WD_RapidResponse_InspectOff".Translate().ToString();
            MissionMask m = outpost.RapidResponseMask;
            if (m == MissionMask.All)
                return "TSA_WD_RapidResponse_InspectAllCaravans".Translate().ToString();
            if (m == MissionMask.Raider)
                return "TSA_WD_RapidResponse_InspectRaiderCaravans".Translate().ToString();
            if (m == MissionMask.Trader)
                return "TSA_WD_RapidResponse_InspectTraderCaravans".Translate().ToString();
            if (m == MissionMask.Expansion)
                return "TSA_WD_RapidResponse_InspectExpansionCaravans".Translate().ToString();
            if (m == MissionMask.Road)
                return "TSA_WD_RapidResponse_InspectRoadCaravans".Translate().ToString();
            if (m == MissionMask.Fortify)
                return "TSA_WD_RapidResponse_InspectFortifyCaravans".Translate().ToString();
            return "TSA_WD_RapidResponse_InspectGroupCaravans".Translate(MortarFireUtils.MissionMaskLabel(m)).ToString();
        }

        /// <summary>New rapid-response outposts default to raider intercept (matches gizmo menu).</summary>
        public static void ApplyEstablishmentDefaults(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null || !outpost.IsRapidResponseOutpost) return;
            outpost.SetRapidResponseMask(MissionMask.Raider);
            outpost.SetRapidResponseRaidTargetMask(RaidTargetMask.Player | RaidTargetMask.Allies);
            outpost.SetRapidResponseMinStrengthRatio(0.9f);
            outpost.SetRapidResponseMaxStrengthRatio(RapidResponseUtility.DefaultMaxStrengthRatio);
            outpost.SetRapidResponseActive(true);
        }
    }

    public static class RapidResponseUtility
    {
        public static float GetConfiguredMaxRangeTiles()
        {
            return Mathf.Max(1f, WorldDominationMod.settings?.rapidResponseAutoInterceptRange ?? WorldDominationSettings.DefRapidResponseAutoInterceptRange);
        }

        /// <summary>Effective auto-intercept range (respects per-outpost shrink override).</summary>
        public static float GetRangeTiles(WorldObject_WD_Outpost outpost)
        {
            float max = GetConfiguredMaxRangeTiles();
            if (outpost == null) return max;
            float ov = outpost.RapidResponseRangeOverride;
            if (ov < 0f) return max;
            float min = Mathf.Min(Dialog_OutpostRangeAdjust.MinTiles, max);
            return Mathf.Clamp(ov, min, max);
        }

        public static float GetDropPodRangeTiles()
        {
            return Mathf.Max(1f, WorldDominationMod.settings?.rapidResponseDropPodRange ?? WorldDominationSettings.DefRapidResponseDropPodRange);
        }

        /// <summary>Vanilla transport pod research (<c>TransportPod</c>).</summary>
        public static bool TransportPodsResearched()
        {
            ResearchProjectDef def = DefDatabase<ResearchProjectDef>.GetNamedSilentFail("TransportPod");
            return def != null && def.IsFinished;
        }

        public static float GetDeployableStrength(WorldObject_WD_Outpost outpost, CompViralSpread comp)
        {
            if (outpost == null || comp == null) return 0f;
            WorldDominationSettings seth = WorldDominationMod.settings;
            if (seth == null) return 0f;
            return WorldActions_Utils.GetAvailableRaidStrength(comp, seth);
        }

        public const float DefaultMaxStrengthRatio = 2.0f;
        public const float MinMaxStrengthRatio = 0.5f;
        public const float MaxMaxStrengthRatio = 3f;

        /// <summary>After the min-ratio gate: send at most maxRatio × target strength, keep the rest home.</summary>
        public static float CapSentStrength(float available, float targetStrength, float maxRatio)
        {
            if (available <= 0f) return 0f;
            if (maxRatio <= 0f || targetStrength <= 0f) return available;
            return Mathf.Min(available, targetStrength * maxRatio);
        }

        public static int GetTicksPerMove()
        {
            float mult = WorldDominationMod.settings?.rapidResponseTicksPerMoveMultiplier
                ?? WorldDominationSettings.DefRapidResponseTicksPerMoveMultiplier;
            // Extra ~10% chase edge so interceptors (player RR and settlement ambush) can catch targets
            // that move at a similar base speed, especially real caravans that only re-aim at the current tile.
            mult *= 0.90f;
            return Mathf.Max(60, Mathf.RoundToInt(WorldObject_Traveler.DefaultTicksPerMove * Mathf.Clamp(mult, 0.1f, 2f)));
        }

        /// <summary>
        /// Raiders (and other MissionMask.Raider travelers) are only auto-intercepted when their destination
        /// is the player or a formal ally of the player. Non-raider missions are unrestricted here.
        /// </summary>
        public static bool IsEligibleAutoInterceptTarget(WorldObject_Traveler traveler)
        {
            if (traveler == null || traveler.Destroyed) return false;
            if (InterceptionMissionMaskUtils.MaskFor(traveler.mission) != MissionMask.Raider)
                return true;
            return IsRaidTargetingPlayerOrAlly(traveler);
        }

        public static bool IsEligibleAutoInterceptTarget(WorldObject_Traveler traveler, RaidTargetMask mask)
        {
            if (traveler == null || traveler.Destroyed) return false;
            if (InterceptionMissionMaskUtils.MaskFor(traveler.mission) != MissionMask.Raider)
                return true;
            if (mask == RaidTargetMask.None) return false;

            WorldObject dest = traveler.targetObject;
            if (!TravelerEndpointUtility.IsLiveEndpoint(dest)) return false;
            Faction destFaction = dest.Faction;
            if (destFaction == null) return false;

            if (destFaction.IsPlayer)
                return (mask & RaidTargetMask.Player) != 0;

            Faction player = Faction.OfPlayerSilentFail;
            if (player != null && WorldActions_Utils.SafeRelationKindWith(destFaction, player) == FactionRelationKind.Ally)
                return (mask & RaidTargetMask.Allies) != 0;

            return (mask & RaidTargetMask.OtherNpcs) != 0;
        }

        public static bool IsRaidTargetingPlayer(WorldObject_Traveler traveler)
        {
            if (traveler == null) return false;
            WorldObject dest = traveler.targetObject;
            if (!TravelerEndpointUtility.IsLiveEndpoint(dest)) return false;
            Faction destFaction = dest.Faction;
            return destFaction != null && destFaction.IsPlayer;
        }

        public static bool IsRaidTargetingPlayerOrAlly(WorldObject_Traveler traveler)
        {
            if (traveler == null) return false;
            WorldObject dest = traveler.targetObject;
            if (!TravelerEndpointUtility.IsLiveEndpoint(dest)) return false;
            Faction destFaction = dest.Faction;
            if (destFaction == null) return false;
            if (destFaction.IsPlayer) return true;
            Faction player = Faction.OfPlayerSilentFail;
            if (player == null) return false;
            return WorldActions_Utils.SafeRelationKindWith(destFaction, player) == FactionRelationKind.Ally;
        }

        public static bool DispatchVirtualIntercept(WorldObject_WD_Outpost origin, WorldObject_Traveler target)
        {
            if (origin == null || origin.Destroyed || target == null || target.Destroyed) return false;
            if (!origin.IsRapidResponseOutpost || origin.Faction == null || target.Faction == null) return false;
            if (!WorldActions_Utils.SafeHostileTo(origin.Faction, target.Faction)) return false;
            if (!IsEligibleAutoInterceptTarget(target, origin.RapidResponseRaidTargetMask)) return false;
            if (HasActiveInterceptFrom(origin, target)) return false;

            CompViralSpread comp = origin.GetComponent<CompViralSpread>();
            float strength = GetDeployableStrength(origin, comp);
            if (strength <= 0f) return false;

            float minRatio = origin.RapidResponseMinStrengthRatio;
            if (minRatio > 0f)
            {
                float targetStrength = target.travelerStrength;
                if (targetStrength > 0f && strength / targetStrength < minRatio)
                    return false;
            }

            strength = RapidResponseUtility.CapSentStrength(strength, target.travelerStrength, origin.RapidResponseMaxStrengthRatio);
            if (strength <= 0f) return false;

            comp.strength -= strength;
            comp.CheckTierUpdate(false);
            WorldObject_Traveler response = WorldActions_Traveler.SpawnRapidResponseInterceptTraveler(origin, target, strength);
            if (response == null)
            {
                comp.AddStrengthNoTierUpgrade(strength);
                return false;
            }

            // Same-tick scans: register the pair immediately so this outpost cannot stack another launch.
            WorldComponent_InterceptionScheduler.Current
                ?.NotifyRapidResponseDispatched(origin, target);

            Find.World?.GetComponent<WorldComponent_SpreadManager>()?.AddLog(new SpreadLogEntry(
                "TSA_WD_Log_RapidResponseLaunched".Translate(origin.LabelCap, target.LabelCap, strength.ToString("F0")),
                origin,
                target));
            return true;
        }

        /// <summary>True if this outpost already has a Rapid Response intercept chasing <paramref name="target"/>.</summary>
        public static bool HasActiveInterceptFrom(WorldObject_WD_Outpost origin, WorldObject_Traveler target)
        {
            var scheduler = WorldComponent_InterceptionScheduler.Current;
            if (scheduler != null)
                return scheduler.HasActiveRapidResponseFrom(origin, target);
            return false;
        }

        public static Map MapAtTile(int tile)
        {
            if (tile < 0 || Current.Game?.Maps == null) return null;
            List<Map> maps = Current.Game.Maps;
            for (int i = 0; i < maps.Count; i++)
            {
                Map map = maps[i];
                if (map != null && map.Tile == tile)
                    return map;
            }
            return null;
        }

        public static bool IsCaravanClashMap(Map map)
        {
            return map != null && map.GetComponent<WD_MapComponent_CaravanClash>() != null;
        }

        public static void DropPawnsViaDropPods(IReadOnlyList<Pawn> pawns, Map map)
        {
            if (pawns == null || pawns.Count == 0 || map == null) return;
            IntVec3 dropCell = FindDropCell(map);
            var things = new List<Thing>(pawns.Count);
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn == null || pawn.Destroyed || pawn.Dead) continue;
                things.Add(pawn);
            }
            if (things.Count == 0) return;
            DropPodUtility.DropThingsNear(dropCell, map, things);
            CameraJumper.TryJump(new GlobalTargetInfo(dropCell, map));
        }

        private static IntVec3 FindDropCell(Map map)
        {
            IntVec3 cell;
            if (CellFinderLoose.TryGetRandomCellWith(c => c.InBounds(map) && c.Standable(map) && !c.Fogged(map), map, 1000, out cell))
                return cell;
            return DropCellFinder.TradeDropSpot(map);
        }
    }
}
