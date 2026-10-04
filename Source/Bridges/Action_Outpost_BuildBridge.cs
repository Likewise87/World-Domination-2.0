using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    [StaticConstructorOnStartup]
    public static class Action_Outpost_BuildBridge
    {
        private static Texture2D cachedCancelIcon;
        private static readonly List<int> OverlayDrawScratch = new List<int>(16);
        private static readonly HashSet<int> SessionValid = new HashSet<int>(64);
        private static readonly HashSet<int> SessionReserved = new HashSet<int>(32);
        private static readonly Dictionary<int, List<int>> SessionChains = new Dictionary<int, List<int>>(32);
        private static readonly Dictionary<int, List<int>> SessionDraw = new Dictionary<int, List<int>>(32);
        private static readonly Dictionary<int, int> SessionWork = new Dictionary<int, int>(32);
        private static int sessionBuilderTile = -1;
        private static bool sessionClearing;

        private static Texture2D BuildBridgeIcon => Action_Outpost_BuildRoad.BuildRoadIcon;
        private static Texture2D DeconstructBridgeIcon => Action_Outpost_BuildRoad.RemoveRoadIcon;

        public static IEnumerable<Gizmo> GetGizmos(WorldObject outpost)
        {
            if (outpost == null || outpost.Faction != Faction.OfPlayer) yield break;
            var comp = outpost.GetComponent<CompViralSpread>();
            if (comp == null || !WorldActions_BuildBridge.HasActiveBridgeProject(comp)) yield break;

            bool clearing = WorldActions_BuildBridge.IsDeconstructProject(comp);
            yield return new Command_Action
            {
                defaultLabel = clearing
                    ? "TSA_WD_CancelBridgeDeconstruct".Translate()
                    : "TSA_WD_CancelBridge".Translate(),
                defaultDesc = clearing
                    ? "TSA_WD_CancelBridgeDeconstructDesc".Translate()
                    : "TSA_WD_CancelBridgeDesc".Translate(),
                icon = cachedCancelIcon ??= ContentFinder<Texture2D>.Get("UI/Designators/Cancel"),
                action = delegate
                {
                    WorldActions_BuildBridge.ClearBridgeProject(comp);
                    Messages.Message(
                        clearing
                            ? "TSA_WD_BridgeDeconstructCancelled".Translate(outpost.LabelCap)
                            : "TSA_WD_BridgeCancelled".Translate(outpost.LabelCap),
                        MessageTypeDefOf.NeutralEvent);
                }
            };
        }

        public static FloatMenuOption MakeBuildBridgeMenuOption(WorldObject outpost, CompViralSpread comp)
        {
            Texture2D icon = BuildBridgeIcon;
            if (WorldActions_BuildBridge.HasActiveBridgeProject(comp))
            {
                bool clearing = WorldActions_BuildBridge.IsDeconstructProject(comp);
                string label = comp.GetCachedBridgeMenuStatus();
                return new FloatMenuOption(label, () => { }, clearing ? DeconstructBridgeIcon : icon, Color.white)
                {
                    Disabled = true
                };
            }

            if (HasBlockingWorldBuildProject(comp))
            {
                return new FloatMenuOption("TSA_WD_CancelCurrentProjectFirst".Translate(), () => { }, icon, Color.white)
                {
                    Disabled = true
                };
            }

            float skill = ColonyWorldBuildUtility.GetActorConstructionSkillRaw(outpost);
            int minC = WorldActions_Roads.GetMinConstructionToBuildRoad(WorldActions_BuildBridge.BridgeRoadTier);
            List<OutpostUpgradeCostEntry> minCosts = WorldActions_BuildBridge.GetBridgeMaterialCosts(1);

            var opt = new FloatMenuOption(
                "TSA_WD_BuildBridge".Translate(),
                WdCascadingFloatMenu.WrapLeaf(() => StartBridgeTargeting(outpost, comp, deconstruct: false)),
                icon,
                Color.cyan)
            {
                tooltip = BuildBridgeTooltip(outpost)
            };
            ColonyWorldBuildRequirements.ApplyGate(
                opt,
                skill,
                minC,
                ColonyWorldBuildRequirements.GetRequiredResearchForRoad(WorldActions_BuildBridge.BridgeRoadTier),
                minCosts);
            return opt;
        }

        public static FloatMenuOption MakeDeconstructBridgeMenuOption(WorldObject outpost, CompViralSpread comp)
        {
            Texture2D icon = DeconstructBridgeIcon;
            if (WorldActions_BuildBridge.HasActiveBridgeProject(comp))
            {
                return new FloatMenuOption("TSA_WD_CancelCurrentProjectFirst".Translate(), () => { }, icon, Color.white)
                {
                    Disabled = true
                };
            }

            if (HasBlockingWorldBuildProject(comp))
            {
                return new FloatMenuOption("TSA_WD_CancelCurrentProjectFirst".Translate(), () => { }, icon, Color.white)
                {
                    Disabled = true
                };
            }

            return new FloatMenuOption(
                "TSA_WD_DeconstructBridge".Translate(),
                WdCascadingFloatMenu.WrapLeaf(() => StartBridgeDeconstructTargeting(outpost, comp)),
                icon,
                Color.white)
            {
                tooltip = "TSA_WD_DeconstructBridgeDesc".Translate(WorldComponent_WdBridges.MaxWaterTiles)
            };
        }

        private static bool HasBlockingWorldBuildProject(CompViralSpread comp) =>
            comp.roadTargetTile != -1
            || WorldActions_RoadBlocks.HasActiveRoadBlockProject(comp)
            || WorldActions_SpikeTraps.HasActiveSpikeTrapProject(comp)
            || WorldActions_AtTurrets.HasActiveAtTurretProject(comp)
            || WorldActions_Decontamination.HasActiveDecontaminationProject(comp);

        private static string BuildBridgeTooltip(WorldObject actor)
        {
            float strength = WorldActions_Roads.GetExpeditionStrengthCost(WorldActions_BuildBridge.BridgeRoadTier);
            if (ColonyWorldBuildUtility.IsPlayerColonyBuildActor(actor))
                strength = 0f;
            int stonePerTile = WorldActions_BuildBridge.GetBridgeAnyStoneBlocksPerWaterTile();
            return "TSA_WD_BuildBridgeDesc".Translate(WorldComponent_WdBridges.MaxWaterTiles)
                + "\n\n" + "TSA_WD_BuildBridgeKindTip".Translate(stonePerTile)
                + "\n" + "TSA_WD_BridgeCostTip".Translate(strength.ToString("F0"));
        }

        public static void StartBridgeTargeting(WorldObject source, CompViralSpread comp, bool deconstruct)
        {
            if (deconstruct)
            {
                StartBridgeDeconstructTargeting(source, comp);
                return;
            }

            CameraJumper.TryJump(source.Tile);
            float range = WorldDominationMod.settings != null
                ? WorldDominationMod.settings.maxRoadRange
                : 30f;
            if (source is WorldObject_WD_Outpost wdOutpost)
                range *= 1f + OutpostExpertUtility.GetEngineerConstructionRadiusBonus(wdOutpost);

            int? startBank = null;
            int originId = source.Tile.tileId;
            int hopWindow = Mathf.Min(
                WorldMapRadiusVisual.GetHopDrawRadius(range),
                WorldMapRadiusVisual.MaxVisualHopRadius);

            SessionChains.Clear();
            SessionDraw.Clear();
            SessionWork.Clear();
            sessionBuilderTile = originId;
            sessionClearing = false;
            WorldActions_BuildBridge.CollectReservedBankTiles(SessionReserved, comp);
            WdBridgeGeometry.CollectLandBanks(
                originId, hopWindow, range, SessionReserved, SessionValid, traverseWater: false);
            WD_WorldLayer_BridgeTargetFill.Show(SessionValid);

            string tipStart = "TSA_WD_BridgeTip_PickStartBank".Translate();
            string tipEnd = "TSA_WD_BridgeTip_PickEndBank".Translate(WorldComponent_WdBridges.MaxWaterTiles);

            void RebuildEndBanks(int fromBank)
            {
                SessionDraw.Clear();
                SessionWork.Clear();
                WdBridgeGeometry.CollectFarBanksWithChains(
                    fromBank, SessionReserved, SessionValid, SessionChains);
                WD_WorldLayer_BridgeTargetFill.Show(SessionValid);
            }

            Find.WorldTargeter.BeginTargeting(
                (target) =>
                {
                    if (target.Tile < 0) return false;
                    int tile = target.Tile.tileId;
                    WorldGrid grid = Find.WorldGrid;
                    if (grid == null || !grid.InBounds(tile)) return false;

                    if (!startBank.HasValue)
                    {
                        if (Find.WorldGrid.ApproxDistanceInTiles(source.Tile, target.Tile) > range)
                        {
                            Messages.Message("TSA_WD_RoadWaypointOutOfRange".Translate(), MessageTypeDefOf.RejectInput);
                            return false;
                        }
                        if (!SessionValid.Contains(tile))
                        {
                            WdBridgeGeometry.RejectReason startReason;
                            if (grid[tile].WaterCovered)
                                startReason = WdBridgeGeometry.RejectReason.StartNotLand;
                            else if (SessionReserved.Contains(tile))
                                startReason = WdBridgeGeometry.RejectReason.BankAlreadyUsed;
                            else
                                startReason = WdBridgeGeometry.RejectReason.StartNotAdjacentToWater;
                            Messages.Message(
                                WorldActions_BuildBridge.RejectMessage(startReason),
                                MessageTypeDefOf.RejectInput);
                            return false;
                        }
                        startBank = tile;
                        RebuildEndBanks(tile);
                        Messages.Message("TSA_WD_BridgeStartBankSelected".Translate(), MessageTypeDefOf.TaskCompletion);
                        return false;
                    }

                    if (tile == startBank.Value)
                    {
                        Messages.Message(
                            WorldActions_BuildBridge.RejectMessage(WdBridgeGeometry.RejectReason.SameTile),
                            MessageTypeDefOf.RejectInput);
                        return false;
                    }
                    if (!SessionChains.TryGetValue(tile, out List<int> chain) || chain == null)
                    {
                        Messages.Message(
                            SessionReserved.Contains(tile)
                                ? WorldActions_BuildBridge.RejectMessage(WdBridgeGeometry.RejectReason.BankAlreadyUsed)
                                : "TSA_WD_BridgeReject_OutOfBridgeRange".Translate(),
                            MessageTypeDefOf.RejectInput);
                        return false;
                    }

                    if (WorldActions_BuildBridge.IsSpanProjectLocked(chain, exclude: comp))
                    {
                        Messages.Message(
                            WorldActions_BuildBridge.RejectMessage(WdBridgeGeometry.RejectReason.ProjectLocked),
                            MessageTypeDefOf.RejectInput);
                        return false;
                    }

                    if (!WorldActions_BuildBridge.BeginBridgeProject(comp, chain, deconstruct: false))
                    {
                        Messages.Message(
                            WorldActions_BuildBridge.RejectMessage(WdBridgeGeometry.RejectReason.AlreadyBridged),
                            MessageTypeDefOf.RejectInput);
                        return false;
                    }

                    Messages.Message(
                        "TSA_WD_BridgeProjectSet".Translate(source.LabelCap),
                        MessageTypeDefOf.TaskCompletion);
                    return true;
                },
                true,
                null,
                false,
                () => DrawCachedTargetPreview(startBank),
                (target) => startBank.HasValue ? tipEnd : tipStart,
                (target) =>
                {
                    if (target.Tile < 0) return false;
                    return SessionValid.Contains(target.Tile.tileId);
                });
        }

        /// <summary>One-click deconstruct: pick either bank of an existing bridge.</summary>
        public static void StartBridgeDeconstructTargeting(WorldObject source, CompViralSpread comp)
        {
            CameraJumper.TryJump(source.Tile);
            float range = WorldDominationMod.settings != null
                ? WorldDominationMod.settings.maxRoadRange
                : 30f;
            if (source is WorldObject_WD_Outpost wdOutpost)
                range *= 1f + OutpostExpertUtility.GetEngineerConstructionRadiusBonus(wdOutpost);

            SessionChains.Clear();
            SessionDraw.Clear();
            SessionWork.Clear();
            sessionBuilderTile = source.Tile.tileId;
            sessionClearing = true;
            WorldActions_BuildBridge.CollectDeconstructBanksInRange(source.Tile.tileId, range, SessionValid);
            foreach (int bank in SessionValid)
            {
                if (!WdBridgeGeometry.TryResolveExistingBridgeFromBank(bank, out List<int> chain, out _))
                    continue;
                SessionChains[bank] = chain;
            }
            WD_WorldLayer_BridgeTargetFill.Show(SessionValid);

            string tipDeconstruct = "TSA_WD_BridgeTip_PickBankDeconstruct".Translate();

            Find.WorldTargeter.BeginTargeting(
                (target) =>
                {
                    if (target.Tile < 0) return false;
                    int tile = target.Tile.tileId;
                    WorldGrid grid = Find.WorldGrid;
                    if (grid == null || !grid.InBounds(tile)) return false;

                    if (Find.WorldGrid.ApproxDistanceInTiles(source.Tile, target.Tile) > range)
                    {
                        Messages.Message("TSA_WD_RoadWaypointOutOfRange".Translate(), MessageTypeDefOf.RejectInput);
                        return false;
                    }

                    if (!SessionChains.TryGetValue(tile, out List<int> chain) || chain == null)
                    {
                        Messages.Message(
                            WorldActions_BuildBridge.RejectMessage(WdBridgeGeometry.RejectReason.NotBridged),
                            MessageTypeDefOf.RejectInput);
                        return false;
                    }

                    if (WorldActions_BuildBridge.IsSpanProjectLocked(chain, exclude: comp))
                    {
                        Messages.Message(
                            WorldActions_BuildBridge.RejectMessage(WdBridgeGeometry.RejectReason.ProjectLocked),
                            MessageTypeDefOf.RejectInput);
                        return false;
                    }

                    if (!WorldActions_BuildBridge.BeginBridgeProject(comp, chain, deconstruct: true))
                    {
                        Messages.Message(
                            WorldActions_BuildBridge.RejectMessage(WdBridgeGeometry.RejectReason.NotBridged),
                            MessageTypeDefOf.RejectInput);
                        return false;
                    }

                    Messages.Message(
                        "TSA_WD_BridgeDeconstructProjectSet".Translate(source.LabelCap),
                        MessageTypeDefOf.TaskCompletion);
                    return true;
                },
                true,
                null,
                false,
                () => DrawCachedTargetPreview(startBank: null),
                (target) => tipDeconstruct,
                (target) =>
                {
                    if (target.Tile < 0) return false;
                    return SessionValid.Contains(target.Tile.tileId);
                });
        }

        private static void EnsureHoverVisual(int endTile, bool clearing, int builderTile)
        {
            if (SessionDraw.ContainsKey(endTile)) return;
            if (!SessionChains.TryGetValue(endTile, out List<int> chain) || chain == null) return;
            var draw = new List<int>(8);
            WorldActions_BuildBridge.CollectUnfinishedDrawTiles(chain, clearing, draw);
            SessionDraw[endTile] = draw;
            int work = -1;
            if (WorldActions_BuildBridge.TryGetNextWaterWork(chain, clearing, builderTile, out _, out int w)
                && w >= 0)
                work = w;
            SessionWork[endTile] = work;
        }

        private static void DrawCachedTargetPreview(int? startBank)
        {
            if (startBank.HasValue)
                Action_Outpost_BuildRoad.DrawOrangeStar(startBank.Value);

            int mouseTile = GenWorld.MouseTile();
            if (mouseTile < 0 || !SessionValid.Contains(mouseTile)) return;
            if (startBank.HasValue && mouseTile == startBank.Value) return;

            EnsureHoverVisual(mouseTile, sessionClearing, sessionBuilderTile);
            if (!SessionDraw.TryGetValue(mouseTile, out List<int> draw) || draw == null)
                return;

            if (draw.Count >= 2)
            {
                Action_Outpost_BuildRoad.DrawRoadPathFromCalculatedNodes(
                    draw, Action_Outpost_BuildRoad.RoadLineOrange);
            }
            Action_Outpost_BuildRoad.DrawOrangeStar(mouseTile);
            if (SessionWork.TryGetValue(mouseTile, out int work) && work >= 0)
                Action_Outpost_BuildRoad.DrawOrangeCircle(work);
        }

        public static void DrawBridgeOverlayIfSelected(WorldObject worldObject)
        {
            if (worldObject == null || !Find.WorldSelector.IsSelected(worldObject)) return;
            var comp = worldObject.GetComponent<CompViralSpread>();
            if (comp == null || !WorldActions_BuildBridge.HasActiveBridgeProject(comp)) return;

            bool isPlayerOutpost = worldObject.Faction == Faction.OfPlayer && worldObject is WorldObject_WD_Outpost;
            bool isColonyBuild = ColonyWorldBuildUtility.IsPlayerColonyBuildActor(worldObject);
            if (!isPlayerOutpost && !isColonyBuild) return;

            WorldActions_BuildBridge.CollectUnfinishedDrawTiles(
                comp.bridgeSpanTiles, comp.bridgeIsClearing, OverlayDrawScratch);
            if (OverlayDrawScratch.Count >= 2)
                Action_Outpost_BuildRoad.DrawRoadPathFromCalculatedNodes(
                    OverlayDrawScratch, Action_Outpost_BuildRoad.RoadLineOrange);
            if (comp.cachedWorkTile != -1)
                Action_Outpost_BuildRoad.DrawOrangeCircle(comp.cachedWorkTile);
        }
    }
}
