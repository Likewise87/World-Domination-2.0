using System.Collections.Generic;
using System.Linq;
using LudeonTK;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    public static class DebugActions_Travelers
    {
        private const float DebugRaidTravelerStrength = 500f;
        private const float DebugOutpostRaidTravelerStrength = 500f;

        [DebugAction("World Domination", "Force random diplomacy change (test freeze CD)",
            allowedGameStates = AllowedGameStates.PlayingOnWorld)]
        public static void ForceRandomDiplomacyChange()
        {
            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            string result = WorldActions_DiplomacyBuffsNerfs.DebugForceRandomDiplomacyChange(manager);
            Messages.Message(result, MessageTypeDefOf.CautionInput);
        }

        [DebugAction("World Domination", "Destroy all active travelers",
            allowedGameStates = AllowedGameStates.PlayingOnWorld)]
        public static void DestroyAllActiveTravelers()
        {
            List<WorldObject_Traveler> travelers = Find.WorldObjects.AllWorldObjects
                .OfType<WorldObject_Traveler>()
                .Where(t => t.Spawned)
                .ToList();

            foreach (WorldObject_Traveler t in travelers)
            {
                t.Destroy();
            }

            Messages.Message(
                $"WD debug: destroyed {travelers.Count} world traveler(s).",
                MessageTypeDefOf.CautionInput);
        }

        [DebugAction("World Domination", "Destroy WD travelers on tile",
            allowedGameStates = AllowedGameStates.PlayingOnWorld)]
        public static void DestroyTravelersOnTile()
        {
            Messages.Message("WD debug: click a world tile to destroy all WD travelers on it (raids, mortars, caravans, etc.).", MessageTypeDefOf.NeutralEvent);

            Find.WorldTargeter.BeginTargeting(
                (GlobalTargetInfo target) =>
                {
                    if (!TryGetValidSurfaceTile(target, out int tile))
                        return false;

                    int destroyed = DestroyWdTravelersAtTile(tile);
                    Messages.Message(
                        destroyed > 0
                            ? $"WD debug: destroyed {destroyed} traveler(s) on tile {tile}."
                            : $"WD debug: no WD travelers on tile {tile}.",
                        MessageTypeDefOf.CautionInput);
                    return true;
                },
                true,
                null,
                false,
                null,
                null,
                t => TryGetValidSurfaceTile(t, out _));
        }

        [DebugAction("World Domination", "Create Roadblock (multi-place)...",
            allowedGameStates = AllowedGameStates.PlayingOnWorld)]
        public static void CreateRoadblockHere()
        {
            var kindOpts = new List<FloatMenuOption>();
            foreach (RoadBlockKind kind in System.Enum.GetValues(typeof(RoadBlockKind)))
            {
                if (!RoadBlockKindUtil.IsPlaceableFromUi(kind)) continue;
                RoadBlockKind captured = kind;
                kindOpts.Add(new FloatMenuOption(
                    RoadBlockKindUtil.LabelKey(captured).Translate(),
                    () => BeginPlaceRoadBlockTargeting(captured)));
            }
            if (kindOpts.Count == 0)
            {
                Messages.Message("WD debug: no placeable road block kinds.", MessageTypeDefOf.RejectInput);
                return;
            }
            DebugActions_FloatMenus.OpenCentered(kindOpts);
        }

        private static void BeginPlaceRoadBlockTargeting(RoadBlockKind kind)
        {
            string kindLabel = RoadBlockKindUtil.LabelKey(kind).Translate();
            Messages.Message(
                $"WD debug: click tiles to place {kindLabel}. Right-click or Esc to stop.",
                MessageTypeDefOf.NeutralEvent);

            Find.WorldTargeter.BeginTargeting(
                (GlobalTargetInfo target) =>
                {
                    if (!TryGetValidSurfaceTile(target, out int tile))
                        return false;

                    WorldComponent_RoadBlocks blocks = WorldComponent_RoadBlocks.Get();
                    if (blocks == null)
                    {
                        Messages.Message("WD debug: road-block world component missing.", MessageTypeDefOf.RejectInput);
                        return false;
                    }

                    if (!blocks.TryPlace(tile, Faction.OfPlayer, kind))
                    {
                        Messages.Message(
                            $"WD debug: cannot place {kindLabel} on tile {tile}.",
                            MessageTypeDefOf.RejectInput);
                        return false;
                    }

                    Messages.Message($"WD debug: placed {kindLabel} on tile {tile}.", MessageTypeDefOf.PositiveEvent);
                    return false;
                },
                true,
                null,
                false,
                null,
                null,
                t =>
                {
                    if (!TryGetValidSurfaceTile(t, out int tile)) return false;
                    return WorldActions_RoadBlocks.IsValidBuildTile(tile, kind);
                });
        }

        [DebugAction("World Domination", "Create Spike Trap (multi-place)...",
            allowedGameStates = AllowedGameStates.PlayingOnWorld)]
        public static void CreateSpikeTrapHere()
        {
            var factions = new List<Faction>();
            DebugActions_FloatMenus.CollectDebugFactions(factions);
            if (factions.Count == 0)
            {
                Messages.Message("WD debug: no factions available.", MessageTypeDefOf.RejectInput);
                return;
            }

            var factionOpts = new List<FloatMenuOption>();
            for (int i = 0; i < factions.Count; i++)
            {
                Faction f = factions[i];
                Faction captured = f;
                Texture2D icon = captured.def?.FactionIcon;
                factionOpts.Add(new FloatMenuOption(
                    captured.Name,
                    () => OpenSpikeTrapKindMenu(captured),
                    icon,
                    captured.Color));
            }
            DebugActions_FloatMenus.OpenCentered(factionOpts);
        }

        private static void OpenSpikeTrapKindMenu(Faction faction)
        {
            var kindOpts = new List<FloatMenuOption>();
            foreach (SpikeTrapKind kind in System.Enum.GetValues(typeof(SpikeTrapKind)))
            {
                SpikeTrapKind captured = kind;
                kindOpts.Add(new FloatMenuOption(
                    SpikeTrapKindUtil.LabelKey(captured).Translate(),
                    () => BeginPlaceSpikeTrapTargeting(faction, captured)));
            }
            DebugActions_FloatMenus.OpenCentered(kindOpts);
        }

        private static void BeginPlaceSpikeTrapTargeting(Faction faction, SpikeTrapKind kind)
        {
            string kindLabel = SpikeTrapKindUtil.LabelKey(kind).Translate();
            Messages.Message(
                $"WD debug: click tiles to place {kindLabel} for {faction.Name}. Right-click or Esc to stop.",
                MessageTypeDefOf.NeutralEvent);

            Find.WorldTargeter.BeginTargeting(
                (GlobalTargetInfo target) =>
                {
                    if (!TryGetValidSurfaceTile(target, out int tile))
                        return false;

                    WorldComponent_SpikeTraps traps = WorldComponent_SpikeTraps.Get();
                    if (traps == null)
                    {
                        Messages.Message("WD debug: spike-trap world component missing.", MessageTypeDefOf.RejectInput);
                        return false;
                    }

                    if (!traps.TryPlace(tile, faction, kind))
                    {
                        Messages.Message(
                            $"WD debug: cannot place {kindLabel} on tile {tile}.",
                            MessageTypeDefOf.RejectInput);
                        return false;
                    }

                    Messages.Message(
                        $"WD debug: placed {kindLabel} ({faction.Name}) on tile {tile}.",
                        MessageTypeDefOf.PositiveEvent);
                    return false;
                },
                true,
                null,
                false,
                null,
                null,
                t =>
                {
                    if (!TryGetValidSurfaceTile(t, out int tile)) return false;
                    return WorldActions_SpikeTraps.IsValidBuildTile(tile, kind, faction);
                });
        }

        [DebugAction("World Domination", "Create AT Turret (multi-place)...",
            allowedGameStates = AllowedGameStates.PlayingOnWorld)]
        public static void CreateAtTurretHere()
        {
            var factions = new List<Faction>();
            DebugActions_FloatMenus.CollectDebugFactions(factions);
            if (factions.Count == 0)
            {
                Messages.Message("WD debug: no factions available.", MessageTypeDefOf.RejectInput);
                return;
            }

            var factionOpts = new List<FloatMenuOption>();
            for (int i = 0; i < factions.Count; i++)
            {
                Faction f = factions[i];
                Faction captured = f;
                Texture2D icon = captured.def?.FactionIcon;
                factionOpts.Add(new FloatMenuOption(
                    captured.Name,
                    () => OpenAtTurretTierMenu(captured),
                    icon,
                    captured.Color));
            }
            DebugActions_FloatMenus.OpenCentered(factionOpts);
        }

        private static void OpenAtTurretTierMenu(Faction faction)
        {
            var tierOpts = new List<FloatMenuOption>();
            foreach (AtTurretTier tier in System.Enum.GetValues(typeof(AtTurretTier)))
            {
                AtTurretTier captured = tier;
                string label = AtTurretUtility.LabelKey(captured).Translate();
                Texture2D icon = WorldObject_AT_Turret.IconForTier(captured);
                tierOpts.Add(new FloatMenuOption(
                    label,
                    () => BeginPlaceAtTurretTargeting(faction, captured),
                    icon,
                    Color.white));
            }
            DebugActions_FloatMenus.OpenCentered(tierOpts);
        }

        private static void BeginPlaceAtTurretTargeting(Faction faction, AtTurretTier tier)
        {
            string kindLabel = AtTurretUtility.LabelKey(tier).Translate();
            Messages.Message(
                $"WD debug: click tiles to place {kindLabel} for {faction.Name}. Right-click or Esc to stop.",
                MessageTypeDefOf.NeutralEvent);

            Find.WorldTargeter.BeginTargeting(
                (GlobalTargetInfo target) =>
                {
                    if (!TryGetValidSurfaceTile(target, out int tile))
                        return false;

                    if (!TryDebugSpawnAtTurret(tile, faction, tier, out string fail))
                    {
                        Messages.Message(fail ?? "WD debug: turret spawn failed.", MessageTypeDefOf.RejectInput);
                        return false;
                    }

                    Messages.Message(
                        $"WD debug: placed {kindLabel} ({faction.Name}) on tile {tile}.",
                        MessageTypeDefOf.PositiveEvent);
                    return false;
                },
                true,
                null,
                false,
                null,
                null,
                t =>
                {
                    if (!TryGetValidSurfaceTile(t, out int tile)) return false;
                    if (AtTurretUtility.TileHasAtTurret(tile)) return false;
                    return faction.IsPlayer
                        ? AtTurretUtility.IsPlayerBuildableTurretTile(tile)
                        : AtTurretUtility.IsEmptyOffRoadTurretSite(tile);
                });
        }

        private static bool TryDebugSpawnAtTurret(int tile, Faction faction, AtTurretTier tier, out string failReason)
        {
            failReason = null;
            if (faction == null)
            {
                failReason = "WD debug: faction missing.";
                return false;
            }
            if (Find.WorldObjects == null)
            {
                failReason = "WD debug: world objects missing.";
                return false;
            }
            if (AtTurretUtility.TileHasAtTurret(tile))
            {
                failReason = $"WD debug: tile {tile} already has an AT Turret.";
                return false;
            }
            if (faction.IsPlayer)
            {
                if (!AtTurretUtility.IsPlayerBuildableTurretTile(tile))
                {
                    failReason = $"WD debug: tile {tile} not eligible for a player AT Turret.";
                    return false;
                }
            }
            else if (!AtTurretUtility.IsEmptyOffRoadTurretSite(tile))
            {
                failReason = $"WD debug: tile {tile} not eligible for an NPC AT Turret (need empty off-road site).";
                return false;
            }

            var def = DefDatabase<WorldObjectDef>.GetNamedSilentFail(AtTurretUtility.DefName);
            if (def == null)
            {
                failReason = $"WD debug: missing WorldObjectDef {AtTurretUtility.DefName}.";
                return false;
            }

            Settlement builtBy = null;
            if (faction.IsPlayer)
                builtBy = InfluenceUtils.GetPlayerColony();
            else
            {
                foreach (Settlement s in Find.WorldObjects.Settlements)
                {
                    if (s != null && !s.Destroyed && s.Faction == faction)
                    {
                        builtBy = s;
                        break;
                    }
                }
            }

            var turret = (WorldObject_AT_Turret)WorldObjectMaker.MakeWorldObject(def);
            turret.Tile = new PlanetTile(tile, Find.WorldGrid[tile].Layer);
            turret.SetFaction(faction);
            turret.tier = tier;
            turret.builtBySettlement = builtBy;
            turret.builtBySite = builtBy;
            var settings = WorldDominationMod.settings;
            turret.strength = settings != null
                ? settings.GetAtTurretMaxStrength(tier)
                : WorldDominationSettings.GetAtTurretMaxStrengthDefault(tier);
            turret.ApplyCooldown();
            Find.WorldObjects.Add(turret);
            return true;
        }

        [DebugAction("World Domination", "Delete Roadblock, Trap, or AT Turret here",
            allowedGameStates = AllowedGameStates.PlayingOnWorld)]
        public static void DeleteRoadblockHere()
        {
            Messages.Message(
                "WD debug: click a world tile to remove a road block, spike trap, or AT Turret.",
                MessageTypeDefOf.NeutralEvent);

            Find.WorldTargeter.BeginTargeting(
                (GlobalTargetInfo target) =>
                {
                    // Return false so targeting stays active until the player cancels.
                    if (!TryGetValidSurfaceTile(target, out int tile))
                        return false;

                    WorldComponent_RoadBlocks blocks = WorldComponent_RoadBlocks.Get();
                    if (blocks != null && blocks.TryClear(tile))
                    {
                        Messages.Message($"WD debug: removed road block on tile {tile}.", MessageTypeDefOf.PositiveEvent);
                        return false;
                    }

                    WorldComponent_SpikeTraps traps = WorldComponent_SpikeTraps.Get();
                    if (traps != null && traps.TryClear(tile))
                    {
                        Messages.Message($"WD debug: removed spike trap on tile {tile}.", MessageTypeDefOf.PositiveEvent);
                        return false;
                    }

                    WorldObject_AT_Turret turret = AtTurretUtility.FindTurretAt(tile);
                    if (turret != null && !turret.Destroyed)
                    {
                        turret.suppressDestroyedLetter = true;
                        turret.Destroy();
                        Messages.Message($"WD debug: removed AT Turret on tile {tile}.", MessageTypeDefOf.PositiveEvent);
                        return false;
                    }

                    Messages.Message(
                        $"WD debug: no road block, spike trap, or AT Turret on tile {tile}.",
                        MessageTypeDefOf.RejectInput);
                    return false;
                },
                true,
                null,
                false,
                null,
                null,
                t =>
                {
                    if (!TryGetValidSurfaceTile(t, out int tile)) return false;
                    return WorldActions_RoadBlocks.IsValidClearTile(tile)
                        || WorldActions_SpikeTraps.IsValidClearTile(tile)
                        || AtTurretUtility.TileHasAtTurret(tile);
                });
        }


        [DebugAction("World Domination", "Damage roadblock 500",
            actionType = DebugActionType.ToolWorld,
            allowedGameStates = AllowedGameStates.PlayingOnWorld)]
        public static void DamageRoadblock500()
        {
            AdjustRoadblockHealthAtMouse(-500f);
        }

        [DebugAction("World Domination", "Heal roadblock 500",
            actionType = DebugActionType.ToolWorld,
            allowedGameStates = AllowedGameStates.PlayingOnWorld)]
        public static void HealRoadblock500()
        {
            AdjustRoadblockHealthAtMouse(500f);
        }

        private static void AdjustRoadblockHealthAtMouse(float delta)
        {
            int tile = GenWorld.MouseTile();
            if (tile < 0) return;

            WorldComponent_RoadBlocks blocks = WorldComponent_RoadBlocks.Get();
            if (blocks == null)
            {
                Messages.Message("WD debug: road-block world component missing.", MessageTypeDefOf.RejectInput);
                return;
            }

            if (!blocks.TryGet(tile, out _))
            {
                Messages.Message($"WD debug: no road block on tile {tile}.", MessageTypeDefOf.RejectInput);
                return;
            }

            if (!blocks.TryAdjustHealth(tile, delta, out float newHealth))
            {
                Messages.Message($"WD debug: failed to adjust road block on tile {tile}.", MessageTypeDefOf.RejectInput);
                return;
            }

            if (newHealth <= 0f)
            {
                Messages.Message($"WD debug: road block on tile {tile} destroyed.", MessageTypeDefOf.NeutralEvent);
                return;
            }

            string verb = delta < 0f ? "damaged" : "healed";
            Messages.Message(
                $"WD debug: {verb} road block on tile {tile} → HP {newHealth:F0}.",
                MessageTypeDefOf.PositiveEvent);
        }

        private static int DestroyWdTravelersAtTile(int tile)
        {
            List<WorldObject_Traveler> onTile = Find.WorldObjects.AllWorldObjects
                .OfType<WorldObject_Traveler>()
                .Where(t => t.Spawned && t.Tile == tile)
                .ToList();

            for (int i = 0; i < onTile.Count; i++)
                onTile[i].Destroy();

            return onTile.Count;
        }

        /// <summary>
        /// Spawns a hostile <see cref="WorldObject_Traveler"/> using the normal raid caravan def and strength 500.
        /// Uses mission <see cref="TravelerMission.DebugRaidTransit"/> so arrival does not run raid simulation (despawn only).
        /// Interception / mortar defensive fire still treats it as a raider caravan.
        /// Destination is a random tile ~10 grid steps away with a valid land path (single click: start only).
        /// </summary>
        [DebugAction("World Domination", "Spawn DEBUG raid caravan (500 str, ~20 tiles, mortar bait)",
            allowedGameStates = AllowedGameStates.PlayingOnWorld)]
        public static void SpawnDebugRaidCaravanMortarBait()
        {
            Messages.Message("TSA_WD_DebugRaidPickStart".Translate(), MessageTypeDefOf.NeutralEvent);

            Find.WorldTargeter.BeginTargeting(
                (GlobalTargetInfo startTarget) =>
                {
                    if (!TryGetValidSurfaceTile(startTarget, out int startTile))
                        return false;

                    if (!TryFindRandomDestinationAboutTenTilesAway(startTile, out int endTile))
                    {
                        Messages.Message("TSA_WD_DebugRaidNoPath".Translate(), MessageTypeDefOf.RejectInput);
                        return true;
                    }

                    SpawnDebugRaidTraveler(startTile, endTile);
                    return true;
                },
                true,
                null,
                false,
                null,
                null,
                startTarget => TryGetValidSurfaceTile(startTarget, out _));
        }

        /// <summary>
        /// Spawns a real outpost raid traveler a few tiles away from a clicked player outpost.
        /// Unlike DebugRaidTransit, this uses mission Raid and the outpost raid traveler def so arrival runs normal raid logic.
        /// </summary>
        [DebugAction("World Domination", "Raid selected outpost in ~4 tiles (500 str)",
            allowedGameStates = AllowedGameStates.PlayingOnWorld)]
        public static void SpawnDebugRaidAgainstPlayerOutpost()
        {
            Messages.Message("TSA_WD_DebugOutpostRaidPickTarget".Translate(), MessageTypeDefOf.NeutralEvent);

            Find.WorldTargeter.BeginTargeting(
                (GlobalTargetInfo target) =>
                {
                    WorldObject_WD_Outpost outpost = FindPlayerOutpostAt(target.Tile);
                    if (outpost == null)
                    {
                        Messages.Message("TSA_WD_DebugOutpostRaidNoOutpost".Translate(), MessageTypeDefOf.RejectInput);
                        return true;
                    }

                    if (!TryFindRandomStartAboutFourTilesAway(outpost.Tile, out int startTile))
                    {
                        Messages.Message("TSA_WD_DebugOutpostRaidNoStart".Translate(), MessageTypeDefOf.RejectInput);
                        return true;
                    }

                    SpawnDebugOutpostRaidTraveler(startTile, outpost);
                    return true;
                },
                true,
                null,
                false,
                null,
                null,
                target => FindPlayerOutpostAt(target.Tile) != null);
        }

        [DebugAction("World Domination", "Raid selected outpost in ~12 tiles (500 str)",
            allowedGameStates = AllowedGameStates.PlayingOnWorld)]
        public static void SpawnDebugRaidAgainstPlayerOutpostFar()
        {
            Messages.Message("WD debug: Click a player outpost. Spawns a real hostile raid caravan (500 str) about 12 tiles away and sends it to attack that outpost.", MessageTypeDefOf.NeutralEvent);

            Find.WorldTargeter.BeginTargeting(
                (GlobalTargetInfo target) =>
                {
                    WorldObject_WD_Outpost outpost = FindPlayerOutpostAt(target.Tile);
                    if (outpost == null)
                    {
                        Messages.Message("TSA_WD_DebugOutpostRaidNoOutpost".Translate(), MessageTypeDefOf.RejectInput);
                        return true;
                    }

                    if (!TryFindRandomStartInDistanceBand(outpost.Tile, DebugOutpostRaidFarMinDistance, DebugOutpostRaidFarMaxDistance, out int startTile))
                    {
                        Messages.Message("WD debug: Could not find a valid start tile about 12 tiles away with a path to this outpost.", MessageTypeDefOf.RejectInput);
                        return true;
                    }

                    SpawnDebugOutpostRaidTraveler(startTile, outpost);
                    return true;
                },
                true,
                null,
                false,
                null,
                null,
                target => FindPlayerOutpostAt(target.Tile) != null);
        }

        [DebugAction("World Domination", "Unlock Outpost Upgrade Delivery (if stuck)",
            allowedGameStates = AllowedGameStates.PlayingOnWorld)]
        public static void UnlockOutpostUpgradeSelection()
        {
            Messages.Message("WD debug: click a player outpost to clear a stuck pending upgrade delivery.", MessageTypeDefOf.NeutralEvent);

            Find.WorldTargeter.BeginTargeting(
                (GlobalTargetInfo target) =>
                {
                    WorldObject_WD_Outpost outpost = FindPlayerOutpostAt(target.Tile);
                    if (outpost == null)
                    {
                        Messages.Message("WD debug: clicked tile does not contain a player outpost.", MessageTypeDefOf.RejectInput);
                        return true;
                    }

                    string pending = outpost.PendingUpgradeDefName ?? "";
                    int level = outpost.PendingUpgradeLevel;
                    if (outpost.ClearPendingUpgrade())
                    {
                        string upgradeLabel = DefDatabase<OutpostUpgradeDef>.GetNamedSilentFail(pending)?.LabelCap ?? pending;
                        Messages.Message($"WD debug: cleared pending upgrade '{upgradeLabel}' level {level} at {outpost.LabelCap}.", MessageTypeDefOf.PositiveEvent);
                    }
                    else
                    {
                        Messages.Message($"WD debug: {outpost.LabelCap} has no pending upgrade.", MessageTypeDefOf.NeutralEvent);
                    }
                    return true;
                },
                true,
                null!,
                false,
                null!,
                null!,
                target => FindPlayerOutpostAt(target.Tile) != null);
        }

        /// <summary>
        /// Debug: click a tile to spawn conquest ruins (if needed) and open the post-conquest outpost opportunity dialog
        /// (<see cref="Dialog_OutpostOpportunityChoices"/>), same flow as after defeating a settlement.
        /// </summary>
        [DebugAction("World Domination", "Simulate Settlement Conquered here",
            allowedGameStates = AllowedGameStates.PlayingOnWorld)]
        public static void OpenConquestOutpostDialogAtTile()
        {
            Messages.Message("WD debug: click a world tile to simulate a conquered settlement (creates ruins if empty, opens outpost choice dialog).", MessageTypeDefOf.NeutralEvent);

            Find.WorldTargeter.BeginTargeting(
                (GlobalTargetInfo target) =>
                {
                    if (!TryGetValidSurfaceTile(target, out int tile))
                        return false;

                    if (!ConquestOpportunityUtility.IsConquestTileStillAvailable(tile, out string reason))
                    {
                        Messages.Message(reason ?? "WD debug: tile is not available for an outpost.", MessageTypeDefOf.RejectInput);
                        return true;
                    }

                    Faction conqueredFaction = FirstNpcFactionForDebugConquest();
                    if (conqueredFaction == null)
                    {
                        Messages.Message("WD debug: no NPC faction found for conquest context.", MessageTypeDefOf.RejectInput);
                        return true;
                    }

                    DestroyedSettlement ruins = FindRuinsAt(tile);
                    if (ruins == null)
                    {
                        ruins = (DestroyedSettlement)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.DestroyedSettlement);
                        ruins.Tile = tile;
                        ruins.SetFaction(conqueredFaction);
                        Find.WorldObjects.Add(ruins);
                    }
                    else if (ruins.Faction != null)
                    {
                        conqueredFaction = ruins.Faction;
                    }

                    string siteName = ruins.LabelCap ?? "Debug site";
                    var context = new ConquestOpportunityContext(
                        tile,
                        siteName,
                        ruins.ID,
                        SettlementTier.T3,
                        conqueredFaction,
                        FindPlayerCaravanIdAt(tile));

                    ConquestOpportunityUtility.OpenMenu(context);
                    Messages.Message($"WD debug: opened conquest outpost dialog at tile {tile} ({siteName}, T3).", MessageTypeDefOf.PositiveEvent);
                    return true;
                },
                true,
                null,
                false,
                null,
                null,
                t => TryGetValidSurfaceTile(t, out _));
        }

        private const int DebugRaidRandomWalkSteps = 20;
        private const int DebugOutpostRaidRandomWalkSteps = 4;
        private const float DebugOutpostRaidFarMinDistance = 11.5f;
        private const float DebugOutpostRaidFarMaxDistance = 16f;

        /// <summary>Random neighbor-walk of <see cref="DebugRaidRandomWalkSteps"/> edges; then verify vanilla path exists on the start tile's layer.</summary>
        private static bool TryFindRandomDestinationAboutTenTilesAway(int startTileId, out int destTileId)
        {
            destTileId = -1;
            WorldGrid grid = Find.WorldGrid;
            if (grid == null || !grid.InBounds(startTileId))
                return false;

            PlanetLayer layer;
            try
            {
                layer = PlanetSurfaceWorldActions.WdSurfaceLayer;
            }
            catch
            {
                return false;
            }

            if (layer?.Pather == null)
                return false;

            var neighbors = new List<PlanetTile>(8);
            var sameLayerNeighbors = new List<int>(8);

            for (int attempt = 0; attempt < 100; attempt++)
            {
                int cur = startTileId;
                for (int step = 0; step < DebugRaidRandomWalkSteps; step++)
                {
                    neighbors.Clear();
                    grid.GetTileNeighbors(cur, neighbors);
                    sameLayerNeighbors.Clear();
                    for (int i = 0; i < neighbors.Count; i++)
                    {
                        PlanetTile pt = neighbors[i];
                        if (!pt.Valid || !grid.InBounds(pt.tileId))
                            continue;
                        if (pt.Layer != layer)
                            continue;
                        sameLayerNeighbors.Add(pt.tileId);
                    }

                    if (sameLayerNeighbors.Count == 0)
                        break;
                    cur = sameLayerNeighbors[Rand.Range(0, sameLayerNeighbors.Count)];
                }

                if (cur == startTileId)
                    continue;
                if (!PlanetSurfaceWorldActions.IsPlanetSurfaceTileForWorldActions(new PlanetTile(cur, layer)))
                    continue;

                PlanetTile fromPt = new PlanetTile(startTileId, layer);
                PlanetTile toPt = new PlanetTile(cur, layer);
                WorldPath testPath = layer.Pather.FindPath(fromPt, toPt, null);
                if (testPath == null || !testPath.Found)
                {
                    testPath?.ReleaseToPool();
                    continue;
                }

                testPath.ReleaseToPool();
                destTileId = cur;
                return true;
            }

            return false;
        }

        /// <summary>Random neighbor-walk away from the outpost, then verify a valid traveler path back to the target tile.</summary>
        private static bool TryFindRandomStartAboutFourTilesAway(int targetTileId, out int startTileId)
        {
            return TryFindRandomStartTilesAway(targetTileId, DebugOutpostRaidRandomWalkSteps, out startTileId);
        }

        private static bool TryFindRandomStartTilesAway(int targetTileId, int randomWalkSteps, out int startTileId)
        {
            startTileId = -1;
            WorldGrid grid = Find.WorldGrid;
            if (grid == null || !grid.InBounds(targetTileId))
                return false;

            PlanetLayer layer;
            try
            {
                layer = PlanetSurfaceWorldActions.WdSurfaceLayer;
            }
            catch
            {
                return false;
            }

            if (layer?.Pather == null)
                return false;

            var neighbors = new List<PlanetTile>(8);
            var sameLayerNeighbors = new List<int>(8);

            for (int attempt = 0; attempt < 100; attempt++)
            {
                int cur = targetTileId;
                int prev = -1;
                for (int step = 0; step < randomWalkSteps; step++)
                {
                    neighbors.Clear();
                    grid.GetTileNeighbors(cur, neighbors);
                    sameLayerNeighbors.Clear();
                    for (int i = 0; i < neighbors.Count; i++)
                    {
                        PlanetTile pt = neighbors[i];
                        if (!pt.Valid || !grid.InBounds(pt.tileId))
                            continue;
                        if (pt.Layer != layer)
                            continue;
                        if (pt.tileId == targetTileId)
                            continue;
                        if (pt.tileId == prev)
                            continue;
                        sameLayerNeighbors.Add(pt.tileId);
                    }

                    if (sameLayerNeighbors.Count == 0)
                        break;
                    prev = cur;
                    cur = sameLayerNeighbors[Rand.Range(0, sameLayerNeighbors.Count)];
                }

                if (cur == targetTileId)
                    continue;
                if (!PlanetSurfaceWorldActions.IsPlanetSurfaceTileForWorldActions(new PlanetTile(cur, layer)))
                    continue;

                PlanetTile fromPt = new PlanetTile(cur, layer);
                PlanetTile toPt = new PlanetTile(targetTileId, layer);
                WorldPath testPath = layer.Pather.FindPath(fromPt, toPt, null);
                if (testPath == null || !testPath.Found)
                {
                    testPath?.ReleaseToPool();
                    continue;
                }

                testPath.ReleaseToPool();
                startTileId = cur;
                return true;
            }

            return false;
        }

        private static bool TryFindRandomStartInDistanceBand(int targetTileId, float minDistance, float maxDistance, out int startTileId)
        {
            startTileId = -1;
            WorldGrid grid = Find.WorldGrid;
            if (grid == null || !grid.InBounds(targetTileId))
                return false;

            PlanetLayer layer;
            try
            {
                layer = PlanetSurfaceWorldActions.WdSurfaceLayer;
            }
            catch
            {
                return false;
            }

            if (layer?.Pather == null)
                return false;

            var visited = new HashSet<int>();
            var queue = new Queue<int>();
            var candidates = new List<int>();
            var neighbors = new List<PlanetTile>(8);
            float searchLimit = maxDistance + 1f;

            visited.Add(targetTileId);
            queue.Enqueue(targetTileId);

            while (queue.Count > 0)
            {
                int cur = queue.Dequeue();
                float dist = grid.ApproxDistanceInTiles(targetTileId, cur);
                if (dist > searchLimit)
                    continue;

                if (cur != targetTileId
                    && dist >= minDistance
                    && dist <= maxDistance
                    && PlanetSurfaceWorldActions.IsPlanetSurfaceTileForWorldActions(new PlanetTile(cur, layer)))
                    candidates.Add(cur);

                neighbors.Clear();
                grid.GetTileNeighbors(cur, neighbors);
                for (int i = 0; i < neighbors.Count; i++)
                {
                    PlanetTile pt = neighbors[i];
                    if (!pt.Valid || !grid.InBounds(pt.tileId))
                        continue;
                    if (pt.Layer != layer)
                        continue;
                    if (visited.Add(pt.tileId))
                        queue.Enqueue(pt.tileId);
                }
            }

            while (candidates.Count > 0)
            {
                int idx = Rand.Range(0, candidates.Count);
                int candidate = candidates[idx];
                candidates.RemoveAt(idx);

                PlanetTile fromPt = new PlanetTile(candidate, layer);
                PlanetTile toPt = new PlanetTile(targetTileId, layer);
                WorldPath testPath = layer.Pather.FindPath(fromPt, toPt, null);
                if (testPath == null || !testPath.Found)
                {
                    testPath?.ReleaseToPool();
                    continue;
                }

                testPath.ReleaseToPool();
                startTileId = candidate;
                return true;
            }

            return false;
        }

        private static bool TryGetValidSurfaceTile(GlobalTargetInfo target, out int tileId)
        {
            tileId = -1;
            if (!target.IsValid || target.Tile < 0)
                return false;
            if (!PlanetSurfaceWorldActions.IsPlanetSurfaceTileForWorldActions(target.Tile))
                return false;
            tileId = target.Tile;
            return true;
        }

        private static void SpawnDebugRaidTraveler(int startTileId, int endTileId)
        {
            Faction fac = FirstNonPlayerFactionHostileToColony();
            if (fac == null)
            {
                Messages.Message("TSA_WD_DebugRaidNoHostileFaction".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }

            WorldObjectDef def = DefDatabase<WorldObjectDef>.GetNamedSilentFail("TSA_WD_Traveler_Raid");
            if (def == null)
            {
                Log.Error("[TSA WD] Debug: TSA_WD_Traveler_Raid def missing.");
                return;
            }

            var traveler = (WorldObject_Traveler)WorldObjectMaker.MakeWorldObject(def);
            traveler.SetFaction(fac);
            traveler.mission = TravelerMission.DebugRaidTransit;
            traveler.originObject = null;
            traveler.targetObject = null;
            traveler.travelerStrength = DebugRaidTravelerStrength;
            traveler.initialStrength = DebugRaidTravelerStrength;
            traveler.spawnTick = Find.TickManager.TicksGame;
            traveler.Tile = startTileId;

            Find.WorldObjects.Add(traveler);
            traveler.pather.StartPath(PlanetSurfaceWorldActions.PlanetTileForWdTravel(endTileId, traveler));

            Messages.Message(
                "TSA_WD_DebugRaidSpawned".Translate(fac.Name, startTileId, endTileId),
                MessageTypeDefOf.PositiveEvent);

            if (Prefs.DevMode)
                Log.Message($"[TSA WD] DebugRaidTransit spawned faction={fac.Name} strength={DebugRaidTravelerStrength} {startTileId} -> {endTileId}");
        }

        private static void SpawnDebugOutpostRaidTraveler(int startTileId, WorldObject_WD_Outpost targetOutpost)
        {
            Faction fac = FirstNonPlayerFactionHostileToColony();
            if (fac == null)
            {
                Messages.Message("TSA_WD_DebugRaidNoHostileFaction".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }

            WorldObjectDef def = DefDatabase<WorldObjectDef>.GetNamedSilentFail("TSA_WD_Traveler_Outpost_Raid");
            if (def == null)
            {
                Log.Error("[TSA WD] Debug: TSA_WD_Traveler_Outpost_Raid def missing.");
                return;
            }

            var traveler = (WorldObject_Traveler)WorldObjectMaker.MakeWorldObject(def);
            traveler.SetFaction(fac);
            traveler.mission = TravelerMission.Raid;
            traveler.originObject = null;
            traveler.targetObject = targetOutpost;
            traveler.travelerStrength = DebugOutpostRaidTravelerStrength;
            traveler.initialStrength = DebugOutpostRaidTravelerStrength;
            traveler.spawnTick = Find.TickManager.TicksGame;
            traveler.Tile = startTileId;

            Find.WorldObjects.Add(traveler);
            traveler.pather.StartPath(PlanetSurfaceWorldActions.PlanetTileForWdTravel(targetOutpost.Tile, traveler));

            Messages.Message(
                "TSA_WD_DebugOutpostRaidSpawned".Translate(fac.Name, startTileId, targetOutpost.LabelCap),
                MessageTypeDefOf.PositiveEvent);

            if (Prefs.DevMode)
                Log.Message($"[TSA WD] Debug outpost raid spawned faction={fac.Name} strength={DebugOutpostRaidTravelerStrength} {startTileId} -> {targetOutpost.Tile} target={targetOutpost.LabelCap}");
        }

        private static WorldObject_WD_Outpost FindPlayerOutpostAt(int tile)
        {
            if (tile < 0) return null;
            List<WorldObject> all = Find.WorldObjects?.AllWorldObjects;
            if (all == null) return null;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] is WorldObject_WD_Outpost outpost
                    && outpost.Tile == tile
                    && outpost.Faction == Faction.OfPlayer
                    && !outpost.Destroyed)
                    return outpost;
            }
            return null;
        }

        private static DestroyedSettlement FindRuinsAt(int tile)
        {
            if (tile < 0) return null;
            List<WorldObject> all = Find.WorldObjects?.AllWorldObjects;
            if (all == null) return null;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] is DestroyedSettlement ruins && ruins.Tile == tile && !ruins.Destroyed)
                    return ruins;
            }
            return null;
        }

        private static int FindPlayerCaravanIdAt(int tile)
        {
            var caravans = Find.WorldObjects?.Caravans;
            if (caravans == null) return -1;
            for (int i = 0; i < caravans.Count; i++)
            {
                Caravan c = caravans[i];
                if (c != null && !c.Destroyed && c.IsPlayerControlled && c.Tile == tile)
                    return c.ID;
            }
            return -1;
        }

        private static Faction FirstNpcFactionForDebugConquest()
        {
            List<Faction> list = Find.FactionManager?.AllFactionsListForReading;
            if (list == null) return null;
            for (int i = 0; i < list.Count; i++)
            {
                Faction f = list[i];
                if (f == null || f.IsPlayer || f.Hidden) continue;
                return f;
            }
            return null;
        }

        private static Faction FirstNonPlayerFactionHostileToColony()
        {
            List<Faction> list = Find.FactionManager?.AllFactionsListForReading;
            if (list == null) return null;
            for (int i = 0; i < list.Count; i++)
            {
                Faction f = list[i];
                if (f == null || f.IsPlayer) continue;
                if (!WorldActions_Utils.SafeHostileTo(f, Faction.OfPlayer)) continue;
                return f;
            }

            return null;
        }
    }
}
