using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Player virtual WD scout: trap-style waypoint targeting, asphalt-equivalent muster
    /// (pawn count × 5 skill), then launch along waypoints in click order.
    /// </summary>
    public static class WorldActions_PlayerScout
    {
        /// <summary>One scout-prep pawn equals this much Construction skill for asphalt road muster.</summary>
        public const float ScoutPrepSkillPerPawn = 5f;

        private static Texture2D cachedCancelIcon;
        private static Texture2D cachedCancelTravelerIcon;

        /// <summary>Cancel gizmo while a scout muster project is active (no dispatch gizmo).</summary>
        public static IEnumerable<Gizmo> GetGizmos(WorldObject site)
        {
            if (site == null || site.Destroyed) yield break;
            if (site.Faction == null || !site.Faction.IsPlayer) yield break;
            if (!(site is Settlement) && !(site is WorldObject_WD_Outpost)) yield break;

            var comp = site.GetComponent<CompViralSpread>();
            if (comp == null || !HasActiveScoutProject(comp)) yield break;

            yield return new Command_Action
            {
                defaultLabel = "TSA_WD_Intel_PlayerScout_Cancel".Translate(),
                defaultDesc = "TSA_WD_Intel_PlayerScout_CancelDesc".Translate(),
                icon = cachedCancelIcon ??= ContentFinder<Texture2D>.Get("UI/Designators/Cancel"),
                action = () =>
                {
                    ClearScoutProject(comp);
                    Messages.Message(
                        "TSA_WD_Intel_PlayerScout_Cancelled".Translate(site.LabelCap),
                        MessageTypeDefOf.NeutralEvent);
                }
            };
        }

        /// <summary>
        /// Cancel gizmo on an in-flight player scout traveler: destroy immediately with no strength refund.
        /// </summary>
        public static IEnumerable<Gizmo> GetTravelerGizmos(WorldObject_Traveler traveler)
        {
            if (!IsLivePlayerScout(traveler)) yield break;

            yield return new Command_Action
            {
                defaultLabel = "TSA_WD_Intel_PlayerScout_CancelTraveler".Translate(),
                defaultDesc = "TSA_WD_Intel_PlayerScout_CancelTravelerDesc".Translate(),
                icon = cachedCancelTravelerIcon ??= ContentFinder<Texture2D>.Get("UI/Designators/Cancel", false)
                    ?? TexCommand.ClearPrioritizedWork,
                action = () => TryCancelPlayerScout(traveler)
            };
        }

        public static bool IsLivePlayerScout(WorldObject_Traveler traveler)
        {
            if (traveler == null || traveler.Destroyed) return false;
            if (traveler.mission != TravelerMission.Scout) return false;
            if (traveler.Faction == null || !traveler.Faction.IsPlayer) return false;
            return traveler.playerScoutExpireTick > 0;
        }

        /// <summary>Instantly destroy a live player scout. No strength refund (spent cost stays spent).</summary>
        public static bool TryCancelPlayerScout(WorldObject_Traveler traveler)
        {
            if (!IsLivePlayerScout(traveler)) return false;

            string label = traveler.LabelCap;
            LookTargets look = traveler.originObject != null && !traveler.originObject.Destroyed
                ? (LookTargets)traveler.originObject
                : LookTargets.Invalid;

            traveler.scoutWaypointQueue?.Clear();
            traveler.scoutExtraWaypointsLeft = 0;
            traveler.suppressDestroyedWorldFx = true;
            traveler.playerScoutQuietDismiss = true;
            traveler.pather?.StopDead();

            Messages.Message(
                "TSA_WD_Intel_PlayerScout_TravelerCancelled".Translate(label),
                look,
                MessageTypeDefOf.NeutralEvent,
                false);
            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            manager?.AddLog(new SpreadLogEntry(
                "TSA_WD_Intel_PlayerScout_TravelerCancelled".Translate(label),
                traveler,
                traveler.originObject) { category = SpreadLogCategory.Scout });

            traveler.Destroy();
            return true;
        }

        /// <summary>
        /// Combat / environmental wipe of a live player scout (trap, AT, clash, etc.).
        /// Skipped when <see cref="WorldObject_Traveler.playerScoutQuietDismiss"/> is set.
        /// </summary>
        public static void NotifyPlayerScoutDestroyedIfNeeded(WorldObject_Traveler traveler)
        {
            if (traveler == null) return;
            if (traveler.playerScoutQuietDismiss) return;
            if (traveler.mission != TravelerMission.Scout) return;
            if (traveler.Faction == null || !traveler.Faction.IsPlayer) return;
            if (traveler.playerScoutExpireTick <= 0) return;

            var seth = WorldDominationMod.settings;
            if (seth == null || !seth.notifyPlayerScoutDestroyed) return;
            if (Find.LetterStack == null) return;

            string name = traveler.LabelCap;
            LookTargets look = traveler.originObject != null && !traveler.originObject.Destroyed
                ? (LookTargets)traveler.originObject
                : LookTargets.Invalid;
            Find.LetterStack.ReceiveLetter(
                "TSA_WD_Letter_PlayerScoutDestroyed_Label".Translate(),
                "TSA_WD_Letter_PlayerScoutDestroyed_Text".Translate(name),
                LetterDefOf.NegativeEvent,
                look);
        }

        /// <summary>Colony / outpost Build float-menu row.</summary>
        public static FloatMenuOption MakeBuildMenuOption(WorldObject site, CompViralSpread comp)
        {
            Texture2D icon = ContentFinder<Texture2D>.Get("UI/Commands/View", false)
                ?? ContentFinder<Texture2D>.Get("UI/Commands/Config", false)
                ?? TexButton.Info;
            string label = "TSA_WD_Intel_PlayerScout_Gizmo".Translate();
            string tip = FormatBuildMenuTooltip(site, comp);
            if (site == null || comp == null)
                return new FloatMenuOption(label, () => { }, icon, Color.cyan) { Disabled = true, tooltip = tip };

            if (HasActiveScoutProject(comp))
            {
                string dest = comp.scoutTargetName.NullOrEmpty() ? "…" : comp.scoutTargetName;
                string status = "TSA_WD_Intel_PlayerScout_Status".Translate(
                    (Mathf.Min(1f, comp.scoutProgress) * 100f).ToString("F0"),
                    dest).ToString();
                return new FloatMenuOption(status, () => { }, icon, Color.cyan)
                {
                    Disabled = true,
                    tooltip = tip
                };
            }

            if (IsOtherWorldBuildBusy(comp))
            {
                return new FloatMenuOption("TSA_WD_CancelCurrentProjectFirst".Translate(), () => { }, icon, Color.cyan)
                {
                    Disabled = true,
                    tooltip = tip
                };
            }

            if (!CanStartScoutProject(site, comp, out string reason))
            {
                return new FloatMenuOption(
                    reason.NullOrEmpty() ? label : $"{label}: {reason}",
                    () => { },
                    icon,
                    Color.cyan)
                {
                    Disabled = true,
                    tooltip = tip
                };
            }

            return new FloatMenuOption(
                label,
                WdCascadingFloatMenu.WrapLeaf(() => BeginScoutTargeting(site, comp)),
                icon,
                Color.cyan)
            {
                tooltip = tip
            };
        }

        /// <summary>Float-menu tip: prepare time, strength cost, lifetime. Values are live, not hardcoded Defs.</summary>
        public static string FormatBuildMenuTooltip(WorldObject site, CompViralSpread comp)
        {
            string prepareLine;
            int pawns = site != null ? GetScoutPrepPawnCount(site) : 0;
            float days = GetScoutMusterEstimatedDays(site);
            if (pawns <= 0)
                prepareLine = "TSA_WD_Intel_PlayerScout_PrepareTimeNeedPawns".Translate();
            else if (days <= 0.0001f)
                prepareLine = "TSA_WD_Intel_PlayerScout_PrepareTimeInstant".Translate();
            else
                prepareLine = "TSA_WD_Intel_PlayerScout_PrepareTime".Translate(days.ToString("F1"), pawns);

            string strengthLine;
            if (comp != null && comp.IsPlayerMapSettlement)
                strengthLine = "TSA_WD_Intel_PlayerScout_StrengthCostNone".Translate();
            else
            {
                GetScoutStrengthCostRange(out float min, out float max);
                strengthLine = "TSA_WD_Intel_PlayerScout_StrengthCost".Translate(min.ToString("F0"), max.ToString("F0"));
            }

            string lifeDays = GetMaxLifetimeDays(site).ToString("F1");
            return "TSA_WD_Intel_PlayerScout_GizmoTip".Translate(prepareLine, strengthLine, lifeDays);
        }

        public static void GetScoutStrengthCostRange(out float min, out float max)
        {
            var seth = WorldDominationMod.settings;
            min = seth?.settlementIntelScoutStrengthMin
                ?? WorldDominationSettings.DefSettlementIntelScoutStrengthMin;
            max = seth != null
                ? Mathf.Max(min, seth.settlementIntelScoutStrengthMax)
                : WorldDominationSettings.DefSettlementIntelScoutStrengthMax;
        }

        /// <summary>Estimated muster days at current pawn prep speed (asphalt-equivalent work).</summary>
        public static float GetScoutMusterEstimatedDays(WorldObject site)
        {
            float required = GetScoutMusterRequiredTicks();
            if (required <= 0f) return 0f;
            float speed = GetScoutMusterWorkSpeed(site);
            if (speed <= 0f) return float.PositiveInfinity;
            return required / (speed * GenDate.TicksPerDay);
        }

        public static bool HasActiveScoutProject(CompViralSpread comp) =>
            comp != null && comp.scoutPlannedWaypoints != null && comp.scoutPlannedWaypoints.Count > 0;

        public static bool IsOtherWorldBuildBusy(CompViralSpread comp)
        {
            if (comp == null) return false;
            return comp.roadTargetTile != -1
                || WorldActions_BuildBridge.HasActiveBridgeProject(comp)
                || WorldActions_Trenches.HasActiveTrenchProject(comp)
                || WorldActions_RoadBlocks.HasActiveRoadBlockProject(comp)
                || WorldActions_SpikeTraps.HasActiveSpikeTrapProject(comp)
                || WorldActions_AtTurrets.HasActiveAtTurretProject(comp)
                || WorldActions_Decontamination.HasActiveDecontaminationProject(comp);
        }

        public static void ClearScoutProject(CompViralSpread comp)
        {
            if (comp == null) return;
            comp.scoutPlannedWaypoints?.Clear();
            comp.scoutProgress = 0f;
            comp.scoutTargetName = string.Empty;
            comp.lastScoutProgressTick = -1;
        }

        public static float GetScoutMusterWorkSpeed(WorldObject site)
        {
            int pawns = GetScoutPrepPawnCount(site);
            if (pawns <= 0) return 0f;
            return pawns * ScoutPrepSkillPerPawn;
        }

        public static float GetScoutMusterRequiredTicks() =>
            WorldActions_Roads.GetRoadProgressRequiredTicks(SettlementTier.T3);

        /// <summary>
        /// Effective player-scout max lifetime days for a site: Intel setting + outpost upgrade bonuses.
        /// Colonies use the setting only until a colony upgrade path exists.
        /// </summary>
        public static float GetMaxLifetimeDays(WorldObject site)
        {
            var seth = WorldDominationMod.settings;
            float days = seth?.intelPlayerScoutMaxLifetimeDays
                ?? WorldDominationSettings.DefIntelPlayerScoutMaxLifetimeDays;
            if (site is WorldObject_WD_Outpost outpost)
                days += outpost.GetBuiltUpgradePlayerScoutMaxLifetimeDaysBonus();
            return Mathf.Max(0.1f, days);
        }

        public static int GetScoutPrepPawnCount(WorldObject site)
        {
            if (site is WorldObject_WD_Outpost outpost)
                return Mathf.Max(0, outpost.PawnCount);
            if (site is Settlement settlement && settlement.Map != null)
            {
                var colonists = settlement.Map.mapPawns?.FreeColonists;
                return colonists != null ? colonists.Count : 0;
            }
            return 0;
        }

        public static bool CanStartScoutProject(WorldObject site, CompViralSpread comp, out string reason)
        {
            reason = null;
            if (!WorldComponent_SettlementIntel.IsTracking())
            {
                reason = "TSA_WD_Intel_ViewAsGateOff".Translate();
                return false;
            }
            // Player sites: one live scout or muster at a time (no cooldown).
            if (HasActivePlayerScout(site) || HasActiveScoutProject(comp))
            {
                reason = "TSA_WD_Intel_PlayerScout_AlreadyActive".Translate();
                return false;
            }
            if (!comp.IsPlayerMapSettlement)
            {
                var seth = WorldDominationMod.settings;
                float cost = seth?.settlementIntelScoutStrengthMin
                    ?? WorldDominationSettings.DefSettlementIntelScoutStrengthMin;
                if (comp.strength < cost)
                {
                    reason = "TSA_WD_Intel_PlayerScout_NeedStrength".Translate(cost.ToString("F0"));
                    return false;
                }
            }
            if (GetScoutPrepPawnCount(site) <= 0)
            {
                reason = "TSA_WD_Intel_PlayerScout_NeedPawns".Translate();
                return false;
            }
            return true;
        }

        private static bool HasActivePlayerScout(WorldObject site)
        {
            IReadOnlyList<WorldObject_Traveler> travelers = WorldObject_Traveler.LiveTravelers;
            for (int i = 0; i < travelers.Count; i++)
            {
                WorldObject_Traveler t = travelers[i];
                if (t == null || t.Destroyed) continue;
                if (t.mission != TravelerMission.Scout) continue;
                if (t.originObject == site) return true;
            }
            return false;
        }

        private static void BeginScoutTargeting(WorldObject site, CompViralSpread comp)
        {
            CameraJumper.TryJump(site.Tile);
            var sessionNodes = new List<int>();

            int LastNodeOrNone() => sessionNodes.Count > 0 ? sessionNodes[sessionNodes.Count - 1] : -1;

            Find.WorldTargeter.BeginTargeting(
                target =>
                {
                    if (!TryGetValidScoutTile(site, target, out int tileId))
                        return false;

                    bool shift = Event.current != null && Event.current.shift;
                    int lastNode = LastNodeOrNone();
                    if (lastNode >= 0 && tileId == lastNode)
                        return false;

                    int from = lastNode >= 0 ? lastNode : site.Tile.tileId;
                    if (!CanPath(site, from, tileId))
                    {
                        Messages.Message("TSA_WD_Intel_PlayerScout_SkipUnreachable".Translate(tileId), MessageTypeDefOf.RejectInput, false);
                        return false;
                    }

                    if (shift)
                    {
                        sessionNodes.Add(tileId);
                        Messages.Message(
                            "TSA_WD_RoadWaypointAdded".Translate(sessionNodes.Count),
                            MessageTypeDefOf.TaskCompletion);
                        return false;
                    }

                    var waypoints = new List<int>(sessionNodes.Count + 1);
                    waypoints.AddRange(sessionNodes);
                    waypoints.Add(tileId);

                    if (!CommitScoutProject(site, comp, waypoints))
                        return false;
                    return true;
                },
                true,
                null,
                false,
                () =>
                {
                    DrawWaypointMarkers(sessionNodes);
                    int mouseTile = GenWorld.MouseTile();
                    if (mouseTile >= 0 && mouseTile != site.Tile.tileId)
                    {
                        int anchor = LastNodeOrNone();
                        int from = anchor >= 0 ? anchor : site.Tile.tileId;
                        if (CanPath(site, from, mouseTile))
                            Action_Outpost_BuildRoad.DrawOrangeStar(mouseTile);
                    }
                },
                target =>
                {
                    if (sessionNodes.Count > 0)
                        return "TSA_WD_RoadWaypointLabel".Translate(sessionNodes.Count);
                    return "TSA_WD_Intel_PlayerScout_BuildTip".Translate();
                },
                t => TryGetValidScoutTile(site, t, out _),
                null,
                true);
        }

        private static bool CommitScoutProject(WorldObject site, CompViralSpread comp, List<int> waypoints)
        {
            if (!CanStartScoutProject(site, comp, out string reason))
            {
                Messages.Message(reason, MessageTypeDefOf.RejectInput, false);
                return false;
            }
            if (waypoints == null || waypoints.Count == 0)
            {
                Messages.Message("TSA_WD_Intel_PlayerScout_NeedTiles".Translate(), MessageTypeDefOf.RejectInput, false);
                return false;
            }

            var reachable = new List<int>(waypoints.Count);
            int from = site.Tile.tileId;
            for (int i = 0; i < waypoints.Count; i++)
            {
                int dest = waypoints[i];
                if (!CanPath(site, from, dest))
                {
                    Messages.Message("TSA_WD_Intel_PlayerScout_SkipUnreachable".Translate(dest), MessageTypeDefOf.RejectInput, false);
                    continue;
                }
                reachable.Add(dest);
                from = dest;
            }
            if (reachable.Count == 0)
            {
                Messages.Message("TSA_WD_Intel_PlayerScout_NoReachable".Translate(), MessageTypeDefOf.RejectInput, false);
                return false;
            }

            string targetName = "Tile " + reachable[reachable.Count - 1];
            var worldObject = Find.WorldObjects.ObjectsAt(reachable[reachable.Count - 1])
                .FirstOrDefault(x => x is Settlement || x is WorldObject_WD_Outpost);
            if (worldObject != null)
                targetName = worldObject.LabelCap;

            comp.scoutPlannedWaypoints = new List<int>(reachable);
            comp.scoutProgress = 0f;
            comp.scoutTargetName = targetName;
            comp.lastScoutProgressTick = -1;

            Messages.Message(
                "TSA_WD_Intel_PlayerScout_MusterStarted".Translate(reachable.Count),
                MessageTypeDefOf.PositiveEvent,
                false);
            return true;
        }

        /// <summary>CompTick: accrue asphalt-equivalent muster; launch when ready.</summary>
        public static void TickScoutMuster(CompViralSpread comp, WorldObject parent)
        {
            if (!HasActiveScoutProject(comp) || parent == null || parent.Destroyed) return;

            int nowTick = Find.TickManager.TicksGame;
            if (comp.lastScoutProgressTick < 0)
            {
                comp.lastScoutProgressTick = nowTick;
                return;
            }

            const int interval = 180;
            const int maxCatchUp = 3600;
            if (nowTick - comp.lastScoutProgressTick < interval) return;

            int dt = nowTick - comp.lastScoutProgressTick;
            dt = Mathf.Min(dt, maxCatchUp);
            comp.lastScoutProgressTick = nowTick;

            float workSpeed = GetScoutMusterWorkSpeed(parent);
            float required = GetScoutMusterRequiredTicks();
            WorldActions_Roads.ApplyWorldBuildProgress(ref comp.scoutProgress, workSpeed, required, dt);

            if (comp.scoutProgress < 1f) return;

            if (HasActivePlayerScout(parent))
            {
                comp.scoutProgress = 1f;
                return;
            }

            var waypoints = new List<int>(comp.scoutPlannedWaypoints);
            if (!TryLaunch(parent, comp, waypoints))
            {
                // Keep project at 100% until strength/path recovers or player cancels.
                comp.scoutProgress = 1f;
                return;
            }

            ClearScoutProject(comp);
        }

        private static bool TryGetValidScoutTile(WorldObject site, GlobalTargetInfo target, out int tileId)
        {
            tileId = -1;
            if (!target.IsValid) return false;
            tileId = target.Tile;
            if (tileId < 0) return false;
            if (tileId == site.Tile.tileId) return false;
            PlanetTile pTile = PlanetSurfaceWorldActions.PlanetTileForWdTravel(tileId, site);
            if (!PlanetSurfaceWorldActions.IsPlanetSurfaceTileForWorldActions(pTile)) return false;
            bool water = Find.WorldGrid[tileId].WaterCovered;
            if (water)
            {
                var seth = WorldDominationMod.settings;
                if (!(seth?.allowCaravansTravelOverWater ?? WorldDominationSettings.DefAllowCaravansTravelOverWater))
                    return false;
            }
            else if (Find.World.Impassable(pTile))
                return false;
            return true;
        }

        private static bool TryLaunch(WorldObject site, CompViralSpread comp, List<int> waypoints)
        {
            if (!WorldComponent_SettlementIntel.IsTracking())
            {
                Messages.Message("TSA_WD_Intel_ViewAsGateOff".Translate(), MessageTypeDefOf.RejectInput, false);
                return false;
            }
            if (HasActivePlayerScout(site))
            {
                Messages.Message("TSA_WD_Intel_PlayerScout_AlreadyActive".Translate(), MessageTypeDefOf.RejectInput, false);
                return false;
            }
            if (waypoints == null || waypoints.Count == 0)
            {
                Messages.Message("TSA_WD_Intel_PlayerScout_NeedTiles".Translate(), MessageTypeDefOf.RejectInput, false);
                return false;
            }

            var reachable = new List<int>(waypoints.Count);
            int from = site.Tile.tileId;
            for (int i = 0; i < waypoints.Count; i++)
            {
                int dest = waypoints[i];
                if (!CanPath(site, from, dest))
                {
                    Messages.Message("TSA_WD_Intel_PlayerScout_SkipUnreachable".Translate(dest), MessageTypeDefOf.RejectInput, false);
                    continue;
                }
                reachable.Add(dest);
                from = dest;
            }
            if (reachable.Count == 0)
            {
                Messages.Message("TSA_WD_Intel_PlayerScout_NoReachable".Translate(), MessageTypeDefOf.RejectInput, false);
                return false;
            }

            var seth = WorldDominationMod.settings;
            float costMin = seth?.settlementIntelScoutStrengthMin
                ?? WorldDominationSettings.DefSettlementIntelScoutStrengthMin;
            float strMax = seth != null
                ? Mathf.Max(costMin, seth.settlementIntelScoutStrengthMax)
                : WorldDominationSettings.DefSettlementIntelScoutStrengthMax;

            float strength = Rand.Range(costMin, strMax);
            bool chargeStrength = !comp.IsPlayerMapSettlement;
            if (chargeStrength)
            {
                if (comp.strength < strength)
                    strength = comp.strength;
                if (strength < costMin)
                {
                    Messages.Message(
                        "TSA_WD_Intel_PlayerScout_NeedStrength".Translate(costMin.ToString("F0")),
                        MessageTypeDefOf.RejectInput,
                        false);
                    return false;
                }
            }

            var def = DefDatabase<WorldObjectDef>.GetNamedSilentFail("TSA_WD_Traveler_Scout");
            if (def == null)
            {
                Log.Error("[TSA WD] Missing TSA_WD_Traveler_Scout def for player scout.");
                return false;
            }

            int first = reachable[0];
            reachable.RemoveAt(0);

            var traveler = (WorldObject_Traveler)WorldObjectMaker.MakeWorldObject(def);
            traveler.Tile = site.Tile;
            traveler.SetFaction(site.Faction);
            traveler.mission = TravelerMission.Scout;
            traveler.isNimbleScout = true;
            traveler.scoutExtraWaypointsLeft = reachable.Count;
            traveler.scoutWaypointQueue = reachable.Count > 0 ? new List<int>(reachable) : null;
            traveler.travelerStrength = strength;
            traveler.initialStrength = strength;
            traveler.originObject = site;
            traveler.ticksPerMove = WorldActions_Traveler.GetScoutTicksPerMove();
            float lifeDays = GetMaxLifetimeDays(site);
            traveler.playerScoutExpireTick = Find.TickManager.TicksGame
                + CompViralSpread.CooldownTicksFromDays(lifeDays);
            WorldComponent_SettlementIntel.Get()?.EnsureTravelerTrackId(traveler);

            Find.WorldObjects.Add(traveler);
            traveler.pather.StartPath(PlanetSurfaceWorldActions.PlanetTileForWdTravel(first, site));
            if (traveler.Destroyed)
            {
                Messages.Message("TSA_WD_Intel_PlayerScout_PathFailed".Translate(), MessageTypeDefOf.RejectInput, false);
                return false;
            }

            if (chargeStrength)
                comp.strength = Mathf.Max(0f, comp.strength - strength);
            WorldComponent_SettlementIntel.Get()?.RecordScoutTileSeen(site.Faction, first);
            Messages.Message(
                "TSA_WD_Intel_PlayerScout_Launched".Translate(reachable.Count + 1),
                traveler,
                MessageTypeDefOf.PositiveEvent,
                false);
            WdScoutWorldMapQuestHelper.NotifyPlayerScoutLaunched();
            return true;
        }

        private static bool CanPath(WorldObject site, int fromTileId, int toTileId)
        {
            if (fromTileId < 0 || toTileId < 0 || fromTileId == toTileId) return false;
            PlanetTile to = PlanetSurfaceWorldActions.PlanetTileForWdTravel(toTileId, site);
            if (!PlanetSurfaceWorldActions.IsPlanetSurfaceTileForWorldActions(to)) return false;
            bool water = Find.WorldGrid[toTileId].WaterCovered;
            var seth = WorldDominationMod.settings;
            bool allowWater = seth?.allowCaravansTravelOverWater ?? WorldDominationSettings.DefAllowCaravansTravelOverWater;
            if (water)
            {
                if (!allowWater) return false;
            }
            else if (Find.World.Impassable(to))
                return false;
            PlanetLayer layer = to.Layer
                ?? PlanetSurfaceWorldActions.WdSurfaceLayer
                ?? Find.WorldGrid.Surface;
            var from = new PlanetTile(fromTileId, layer);
            using WorldPath path = layer.Pather.FindPath(from, new PlanetTile(toTileId, layer), null);
            if (path != null && path.Found) return true;
            if (!allowWater) return false;
            return TravelerWaterPathing.TryBuildFallbackPath(from, to, out List<PlanetTile> waterPath)
                && waterPath != null
                && waterPath.Count > 1;
        }

        private static void DrawWaypointMarkers(List<int> waypoints)
        {
            if (waypoints == null || waypoints.Count == 0) return;
            for (int i = 0; i < waypoints.Count; i++)
                Action_Outpost_BuildRoad.DrawOrangeX(waypoints[i]);
        }

        /// <summary>
        /// When a scout caravan is selected, mark every remaining stop with the building-project
        /// orange disc (<see cref="Action_Outpost_BuildRoad.DrawOrangeCircle"/>).
        /// </summary>
        public static void DrawScoutWaypointsIfSelected(WorldObject_Traveler traveler)
        {
            if (traveler == null || traveler.Destroyed) return;
            if (traveler.mission != TravelerMission.Scout) return;
            if (!Find.WorldSelector.IsSelected(traveler)) return;
            if (!PlanetSurfaceWorldActions.IsPlanetSurfaceLayerSelected()) return;

            if (traveler.pather != null && traveler.pather.moving)
            {
                int destId = traveler.pather.destTile.tileId;
                if (destId >= 0)
                    Action_Outpost_BuildRoad.DrawOrangeCircle(destId);
            }

            List<int> queue = traveler.scoutWaypointQueue;
            if (queue == null || queue.Count == 0) return;
            for (int i = 0; i < queue.Count; i++)
            {
                int tileId = queue[i];
                if (tileId >= 0)
                    Action_Outpost_BuildRoad.DrawOrangeCircle(tileId);
            }
        }

        /// <summary>True when a player scout has hit its max lifetime.</summary>
        public static bool TryExpirePlayerScoutIfLifetimeElapsed(WorldObject_Traveler traveler)
        {
            if (traveler == null || traveler.Destroyed) return false;
            if (traveler.mission != TravelerMission.Scout) return false;
            if (traveler.playerScoutExpireTick <= 0) return false;
            if (Find.TickManager.TicksGame < traveler.playerScoutExpireTick) return false;

            traveler.scoutWaypointQueue?.Clear();
            traveler.scoutExtraWaypointsLeft = 0;
            traveler.suppressDestroyedWorldFx = true;
            traveler.playerScoutQuietDismiss = true;

            string expiredText = "TSA_WD_Intel_PlayerScout_LifetimeExpired".Translate(traveler.LabelCap);
            LookTargets look = traveler.originObject != null && !traveler.originObject.Destroyed
                ? (LookTargets)traveler.originObject
                : LookTargets.Invalid;
            var seth = WorldDominationMod.settings;
            if (seth != null && seth.notifyPlayerScoutLifetimeExpired && Find.LetterStack != null)
            {
                Find.LetterStack.ReceiveLetter(
                    "TSA_WD_Letter_PlayerScoutLifetimeExpired_Label".Translate(),
                    "TSA_WD_Letter_PlayerScoutLifetimeExpired_Text".Translate(traveler.LabelCap),
                    LetterDefOf.NeutralEvent,
                    look);
            }
            else
            {
                Messages.Message(expiredText, look, MessageTypeDefOf.NeutralEvent, false);
            }

            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            manager?.AddLog(new SpreadLogEntry(
                expiredText,
                traveler,
                traveler.originObject) { category = SpreadLogCategory.Scout });
            traveler.Destroy();
            return true;
        }

        /// <summary>Continue player scout along queued tiles; returns true if a new path started.</summary>
        public static bool TryContinuePlayerScoutQueue(WorldObject_Traveler traveler)
        {
            if (traveler == null || traveler.Destroyed) return false;
            if (traveler.scoutWaypointQueue == null || traveler.scoutWaypointQueue.Count == 0) return false;

            while (traveler.scoutWaypointQueue.Count > 0)
            {
                int dest = traveler.scoutWaypointQueue[0];
                traveler.scoutWaypointQueue.RemoveAt(0);
                traveler.scoutExtraWaypointsLeft = traveler.scoutWaypointQueue.Count;

                WorldObject origin = traveler.originObject ?? traveler;
                traveler.pather.StartPath(PlanetSurfaceWorldActions.PlanetTileForWdTravel(dest, origin));
                if (traveler.Destroyed)
                {
                    Messages.Message(
                        "TSA_WD_Intel_PlayerScout_SkipUnreachable".Translate(dest),
                        MessageTypeDefOf.RejectInput,
                        false);
                    continue;
                }
                WorldComponent_SettlementIntel.Get()?.RecordScoutTileSeen(traveler.Faction, dest);
                return true;
            }
            return false;
        }
    }
}
