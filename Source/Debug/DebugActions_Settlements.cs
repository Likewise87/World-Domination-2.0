using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    public static class DebugActions_Settlements
    {

        public static void SpawnWdSettlementMultiPlace()
        {
            var tierOpts = new List<FloatMenuOption>();
            foreach (SettlementTier tier in Enum.GetValues(typeof(SettlementTier)))
            {
                SettlementTier captured = tier;
                tierOpts.Add(new FloatMenuOption(
                    captured.ToString(),
                    () => OpenFactionMenuForSpawn(captured)));
            }
            OpenCenteredFloatMenu(tierOpts);
        }

        private static void OpenFactionMenuForSpawn(SettlementTier tier)
        {
            var factions = new List<Faction>();
            DebugActions_FloatMenus.CollectWdParticipantFactions(factions, includeDefeated: true);
            if (factions.Count == 0)
            {
                Messages.Message("WD debug: no placeable NPC factions found.", MessageTypeDefOf.RejectInput);
                return;
            }

            var factionOpts = new List<FloatMenuOption>();
            for (int i = 0; i < factions.Count; i++)
            {
                Faction captured = factions[i];
                Texture2D icon = captured.def?.FactionIcon;
                factionOpts.Add(new FloatMenuOption(
                    DebugActions_FloatMenus.FactionMenuLabel(captured),
                    () => BeginSpawnSettlementTargeting(tier, captured),
                    icon,
                    captured.Color));
            }

            OpenCenteredFloatMenu(factionOpts);
        }

        private static void OpenCenteredFloatMenu(List<FloatMenuOption> options) =>
            DebugActions_FloatMenus.OpenCentered(options);

        private static void BeginSpawnSettlementTargeting(SettlementTier tier, Faction faction)
        {
            if (faction != null && faction.defeated)
                faction.defeated = false;

            Messages.Message(
                $"WD debug: click tiles to place {tier} settlements for {faction.Name}. Right-click or Esc to stop.",
                MessageTypeDefOf.NeutralEvent);

            Find.WorldTargeter.BeginTargeting(
                (GlobalTargetInfo target) =>
                {
                    // Return false so targeting stays active for multi-place (same as roadblock debug).
                    if (!TryGetValidDebugSettlementTile(target, out int tile))
                        return false;

                    if (faction != null && faction.defeated)
                        faction.defeated = false;

                    if (!TrySpawnWdSettlementAt(tile, tier, faction, out string fail))
                    {
                        Messages.Message(fail ?? "WD debug: spawn failed.", MessageTypeDefOf.RejectInput);
                        return false;
                    }

                    Messages.Message(
                        $"WD debug: placed {tier} {faction.Name} settlement on tile {tile}.",
                        MessageTypeDefOf.PositiveEvent);
                    return false;
                },
                true,
                null,
                false,
                null,
                null,
                t => TryGetValidDebugSettlementTile(t, out _));
        }

        public static void BeginTriggerRevolt()
        {
            var factions = new List<Faction>();
            DebugActions_FloatMenus.CollectWdParticipantFactions(factions, includeDefeated: true);
            if (factions.Count == 0)
            {
                Messages.Message("WD debug: no WD participant factions.", MessageTypeDefOf.RejectInput);
                return;
            }

            var opts = new List<FloatMenuOption>();
            for (int i = 0; i < factions.Count; i++)
            {
                Faction captured = factions[i];
                opts.Add(new FloatMenuOption(
                    DebugActions_FloatMenus.FactionMenuLabel(captured),
                    () => BeginTriggerRevoltTargeting(captured),
                    captured.def?.FactionIcon,
                    captured.Color));
            }
            OpenCenteredFloatMenu(opts);
        }

        private static void BeginTriggerRevoltTargeting(Faction rebel)
        {
            Messages.Message(
                $"WD debug: click an NPC settlement to seed a revolt for {rebel.Name}. Right-click or Esc to stop.",
                MessageTypeDefOf.NeutralEvent);

            Find.WorldTargeter.BeginTargeting(
                target =>
                {
                    Settlement seed = SettlementAtTile(target);
                    if (seed == null)
                    {
                        Messages.Message("WD debug: click an NPC settlement.", MessageTypeDefOf.RejectInput);
                        return false;
                    }

                    if (!WorldActions_Revolt.DebugForceRevolt(rebel, seed, out string fail))
                    {
                        Messages.Message(fail ?? "WD debug: revolt failed.", MessageTypeDefOf.RejectInput);
                        return false;
                    }

                    Messages.Message(
                        $"WD debug: revolt triggered for {rebel.Name} (seed {seed.LabelCap}).",
                        MessageTypeDefOf.PositiveEvent);
                    return true;
                },
                true,
                null,
                false,
                null,
                null,
                t => SettlementAtTile(t) != null);
        }

        public static void BeginDefeatFaction()
        {
            Messages.Message(
                "WD debug: click an NPC WD settlement to destroy all towns of that faction. Right-click or Esc to stop.",
                MessageTypeDefOf.NeutralEvent);

            Find.WorldTargeter.BeginTargeting(
                target =>
                {
                    Settlement seed = SettlementAtTile(target);
                    if (seed == null)
                    {
                        Messages.Message("WD debug: click an NPC settlement.", MessageTypeDefOf.RejectInput);
                        return false;
                    }

                    if (!TryDefeatFactionOf(seed, out string msg))
                    {
                        Messages.Message(msg ?? "WD debug: defeat faction failed.", MessageTypeDefOf.RejectInput);
                        return false;
                    }

                    Messages.Message(msg, MessageTypeDefOf.CautionInput);
                    return true;
                },
                true,
                null,
                false,
                null,
                null,
                t => SettlementAtTile(t) != null);
        }

        public static void BeginChangeSettlementOwner()
        {
            Messages.Message(
                "WD debug: click an NPC WD settlement to change its owner. Right-click or Esc to stop.",
                MessageTypeDefOf.NeutralEvent);

            Find.WorldTargeter.BeginTargeting(
                target =>
                {
                    Settlement settlement = SettlementAtTile(target);
                    if (settlement == null)
                    {
                        Messages.Message("WD debug: click an NPC settlement.", MessageTypeDefOf.RejectInput);
                        return false;
                    }

                    if (settlement.Faction == null || settlement.Faction.IsPlayer
                        || !WorldActions_Utils.IsWdParticipant(settlement.Faction))
                    {
                        Messages.Message("WD debug: must be an NPC WD settlement.", MessageTypeDefOf.RejectInput);
                        return false;
                    }

                    OpenChangeOwnerMenu(settlement);
                    return true;
                },
                true,
                null,
                false,
                null,
                null,
                t => SettlementAtTile(t) != null);
        }

        private static void OpenChangeOwnerMenu(Settlement settlement)
        {
            var factions = new List<Faction>();
            DebugActions_FloatMenus.CollectWdParticipantFactions(factions, includeDefeated: true);
            Faction current = settlement.Faction;

            var opts = new List<FloatMenuOption>();
            for (int i = 0; i < factions.Count; i++)
            {
                Faction f = factions[i];
                if (f == null || f == current) continue;
                Faction captured = f;
                opts.Add(new FloatMenuOption(
                    DebugActions_FloatMenus.FactionMenuLabel(captured),
                    () => ApplyChangeSettlementOwner(settlement, captured),
                    captured.def?.FactionIcon,
                    captured.Color));
            }

            if (opts.Count == 0)
            {
                Messages.Message("WD debug: no other WD factions available.", MessageTypeDefOf.RejectInput);
                return;
            }

            OpenCenteredFloatMenu(opts);
        }

        private static void ApplyChangeSettlementOwner(Settlement settlement, Faction newOwner)
        {
            if (settlement == null || settlement.Destroyed || newOwner == null) return;
            if (settlement.HasMap)
            {
                Messages.Message(
                    "WD debug: close the settlement map before changing owner.",
                    MessageTypeDefOf.RejectInput);
                return;
            }
            if (settlement.Faction == null || settlement.Faction.IsPlayer
                || !WorldActions_Utils.IsWdParticipant(settlement.Faction))
            {
                Messages.Message("WD debug: must be an NPC WD settlement.", MessageTypeDefOf.RejectInput);
                return;
            }
            if (!WorldActions_Utils.IsWdParticipant(newOwner))
            {
                Messages.Message("WD debug: new owner must be a WD participant.", MessageTypeDefOf.RejectInput);
                return;
            }
            if (newOwner.defeated)
                newOwner.defeated = false;

            string oldFactionName = settlement.Faction?.Name ?? "?";
            string keepName = settlement.Name ?? settlement.LabelCap;
            int tile = settlement.Tile.tileId;
            var oldComp = settlement.GetComponent<CompViralSpread>();
            SettlementTier tier = oldComp != null ? oldComp.tier : SettlementTier.T1;
            float offensive = oldComp != null ? oldComp.offensiveStrength : 100f;
            float defensive = oldComp != null ? oldComp.defensiveStrength : 0f;
            string subType = oldComp?.subType;
            int artilleryRoll = oldComp != null ? oldComp.artilleryRoll : -1;
            bool freeT4 = oldComp != null && oldComp.freeT4ArtilleryDone;

            var atSnaps = SnapshotOwnedAtTurrets(settlement);
            var advSnaps = SnapshotOwnedAdvBuildings(settlement);
            var blockSnaps = CollectOwnedRoadBlocks(settlement);
            var trapSnaps = CollectOwnedSpikeTraps(settlement);

            // Survive NotifyBuilderLost / ClearBuiltBySettlement (exact stamp + old-faction ring).
            for (int i = 0; i < blockSnaps.Count; i++)
            {
                RoadBlockRecord r = blockSnaps[i];
                if (r == null) continue;
                r.builtByFaction = newOwner;
                r.builtBySettlement = null;
            }
            for (int i = 0; i < trapSnaps.Count; i++)
            {
                SpikeTrapRecord r = trapSnaps[i];
                if (r == null) continue;
                r.builtByFaction = newOwner;
                r.builtBySettlement = null;
            }

            WipeOwnedAtTurrets(settlement);
            WorldComponent_AdvancedBuildings.Get()?.DestroyAllOwnedBy(settlement);

            WorldActions_DesperationRaid.SuppressLossNotify(settlement);
            settlement.Destroy();

            Settlement newS = (Settlement)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);
            newS.SetFaction(newOwner);
            newS.Tile = tile;
            newS.Name = keepName;
            Find.WorldObjects.Add(newS);

            var nc = newS.GetComponent<CompViralSpread>();
            if (nc != null)
                nc.ApplyCopiedSettlementState(tier, offensive, defensive, subType, artilleryRoll, freeT4);

            for (int i = 0; i < blockSnaps.Count; i++)
            {
                RoadBlockRecord r = blockSnaps[i];
                if (r == null) continue;
                r.builtBySettlement = newS;
                r.builtByFaction = newOwner;
            }
            for (int i = 0; i < trapSnaps.Count; i++)
            {
                SpikeTrapRecord r = trapSnaps[i];
                if (r == null) continue;
                r.builtBySettlement = newS;
                r.builtByFaction = newOwner;
            }
            if (blockSnaps.Count > 0)
                WD_WorldLayer_MovementDifficultyOverlay.InvalidateAndDirtyIfActive();

            RespawnAtTurrets(newS, newOwner, atSnaps);
            RespawnAdvBuildings(newS, advSnaps);

            Find.World.GetComponent<WorldComponent_SpreadManager>()?.Notify_WeightsChanged();
            Find.World?.GetComponent<Text_WorldTierOnSettlements>()?.NotifyTierLabelCacheDirty();
            WorldActions_Utils.RefreshMap();
            Messages.Message(
                $"WD debug: replaced {keepName} owner {oldFactionName} → {newOwner.Name} (assets/fortifications transferred).",
                MessageTypeDefOf.PositiveEvent);
        }

        private struct AtTurretTransferSnap
        {
            public int TileId;
            public AtTurretTier Tier;
            public float Strength;
            public int CooldownTick;
            public bool DefenseActive;
            public MissionMask DefenseMask;
            public RaidTargetMask RaidMask;
            public float RangeOverride;
            public int DefaultAimTileId;
        }

        private struct AdvBuildingTransferSnap
        {
            public int TileId;
            public AdvancedBuildingDef Def;
            public int PlacedTick;
            public float ArtStrength;
            public float ArtMaxStrength;
            public int ArtCooldownTick;
            public bool IsArtillery;
        }

        private static List<AtTurretTransferSnap> SnapshotOwnedAtTurrets(Settlement owner)
        {
            var list = new List<AtTurretTransferSnap>();
            IReadOnlyList<WorldObject_AT_Turret> turrets = WorldObject_AT_Turret.LiveTurrets;
            for (int i = 0; i < turrets.Count; i++)
            {
                WorldObject_AT_Turret t = turrets[i];
                if (t == null || t.Destroyed || t.builtBySettlement != owner) continue;
                list.Add(new AtTurretTransferSnap
                {
                    TileId = t.Tile.tileId,
                    Tier = t.tier,
                    Strength = t.strength,
                    CooldownTick = t.cooldownTick,
                    DefenseActive = t.DefenseActive,
                    DefenseMask = t.DefenseMask,
                    RaidMask = t.DefenseRaidTargetMask,
                    RangeOverride = t.RangeOverride,
                    DefaultAimTileId = t.DefaultAimTileId
                });
            }
            return list;
        }

        private static List<AdvBuildingTransferSnap> SnapshotOwnedAdvBuildings(Settlement owner)
        {
            var list = new List<AdvBuildingTransferSnap>();
            List<WorldObject_WD_AdvancedBuilding> owned =
                WorldComponent_AdvancedBuildings.Get()?.OwnedBy(owner);
            if (owned == null) return list;
            for (int i = 0; i < owned.Count; i++)
            {
                WorldObject_WD_AdvancedBuilding b = owned[i];
                if (b == null || b.Destroyed || b.BuildingDef == null) continue;
                var snap = new AdvBuildingTransferSnap
                {
                    TileId = b.Tile.tileId,
                    Def = b.BuildingDef,
                    PlacedTick = b.placedTick,
                    IsArtillery = b is WorldObject_WD_ArtilleryTurret
                };
                if (b is WorldObject_WD_ArtilleryTurret art)
                {
                    snap.ArtStrength = art.strength;
                    snap.ArtMaxStrength = art.maxStrength;
                    snap.ArtCooldownTick = art.cooldownTick;
                }
                list.Add(snap);
            }
            return list;
        }

        private static List<RoadBlockRecord> CollectOwnedRoadBlocks(Settlement owner)
        {
            var list = new List<RoadBlockRecord>();
            IReadOnlyList<RoadBlockRecord> records = WorldComponent_RoadBlocks.Get()?.Records;
            if (records == null) return list;
            for (int i = 0; i < records.Count; i++)
            {
                RoadBlockRecord r = records[i];
                if (r != null && r.builtBySettlement == owner)
                    list.Add(r);
            }
            return list;
        }

        private static List<SpikeTrapRecord> CollectOwnedSpikeTraps(Settlement owner)
        {
            var list = new List<SpikeTrapRecord>();
            IReadOnlyList<SpikeTrapRecord> records = WorldComponent_SpikeTraps.Get()?.Records;
            if (records == null) return list;
            for (int i = 0; i < records.Count; i++)
            {
                SpikeTrapRecord r = records[i];
                if (r != null && r.builtBySettlement == owner)
                    list.Add(r);
            }
            return list;
        }

        private static void WipeOwnedAtTurrets(Settlement owner)
        {
            IReadOnlyList<WorldObject_AT_Turret> turrets = WorldObject_AT_Turret.LiveTurrets;
            var doomed = new List<WorldObject_AT_Turret>();
            for (int i = 0; i < turrets.Count; i++)
            {
                WorldObject_AT_Turret t = turrets[i];
                if (t != null && !t.Destroyed && t.builtBySettlement == owner)
                    doomed.Add(t);
            }
            for (int i = 0; i < doomed.Count; i++)
            {
                WorldObject_AT_Turret t = doomed[i];
                if (t == null || t.Destroyed) continue;
                t.suppressDestroyedLetter = true;
                t.Destroy();
            }
        }

        private static void RespawnAtTurrets(Settlement newOwnerSettlement, Faction newOwner, List<AtTurretTransferSnap> snaps)
        {
            if (snaps == null) return;
            for (int i = 0; i < snaps.Count; i++)
            {
                AtTurretTransferSnap snap = snaps[i];
                WorldObject_AT_Turret spawned = AtTurretUtility.TrySpawn(
                    snap.TileId,
                    newOwner,
                    snap.Tier,
                    newOwnerSettlement,
                    builtBySite: newOwnerSettlement,
                    requirePlayerBuildSite: false,
                    allowRoadTile: true,
                    ignoreSettlementCap: true);
                if (spawned == null)
                {
                    Messages.Message(
                        $"WD debug: failed to respawn AT turret at tile {snap.TileId}.",
                        MessageTypeDefOf.RejectInput);
                    continue;
                }
                spawned.ApplyCopiedTransferState(
                    snap.Strength,
                    snap.CooldownTick,
                    snap.DefenseActive,
                    snap.DefenseMask,
                    snap.RaidMask,
                    snap.RangeOverride,
                    snap.DefaultAimTileId);
            }
        }

        private static void RespawnAdvBuildings(Settlement newOwnerSettlement, List<AdvBuildingTransferSnap> snaps)
        {
            if (snaps == null) return;
            for (int i = 0; i < snaps.Count; i++)
            {
                AdvBuildingTransferSnap snap = snaps[i];
                WorldObject_WD_AdvancedBuilding placed =
                    WorldActions_AdvancedBuildings.Place(snap.Def, newOwnerSettlement, snap.TileId);
                if (placed == null)
                {
                    Messages.Message(
                        $"WD debug: failed to respawn {snap.Def?.defName ?? "building"} at tile {snap.TileId}.",
                        MessageTypeDefOf.RejectInput);
                    continue;
                }
                placed.placedTick = snap.PlacedTick;
                if (snap.IsArtillery && placed is WorldObject_WD_ArtilleryTurret art)
                {
                    art.maxStrength = snap.ArtMaxStrength > 0f ? snap.ArtMaxStrength : art.MaxStrength;
                    art.strength = snap.ArtStrength;
                    art.cooldownTick = snap.ArtCooldownTick;
                }
            }
        }

        private static bool TryDefeatFactionOf(Settlement seed, out string message)
        {
            message = null;
            if (seed == null || seed.Destroyed)
            {
                message = "WD debug: no settlement.";
                return false;
            }

            Faction faction = seed.Faction;
            if (faction == null || faction.IsPlayer)
            {
                message = "WD debug: cannot defeat the player faction.";
                return false;
            }
            if (!WorldActions_Utils.IsWdParticipant(faction))
            {
                message = "WD debug: not a WD participant faction.";
                return false;
            }
            if (faction.defeated)
            {
                message = $"WD debug: {faction.Name} is already defeated.";
                return false;
            }

            List<Settlement> all = Find.WorldObjects?.Settlements;
            if (all == null)
            {
                message = "WD debug: no settlements.";
                return false;
            }

            var doomed = new List<Settlement>();
            int skippedHasMap = 0;
            for (int i = 0; i < all.Count; i++)
            {
                Settlement s = all[i];
                if (s == null || s.Destroyed || s.Faction != faction) continue;
                if (!PlanetSurfaceWorldActions.IsPlanetSurfaceTileForWorldActions(s.Tile)) continue;
                if (s.HasMap)
                {
                    skippedHasMap++;
                    continue;
                }
                doomed.Add(s);
            }

            if (doomed.Count == 0)
            {
                message = skippedHasMap > 0
                    ? $"WD debug: all {faction.Name} towns have open maps ({skippedHasMap}); close them first."
                    : $"WD debug: {faction.Name} has no surface settlements to destroy.";
                return false;
            }

            var adv = WorldComponent_AdvancedBuildings.Get();
            for (int i = 0; i < doomed.Count; i++)
            {
                Settlement s = doomed[i];
                if (s == null || s.Destroyed) continue;
                int tile = s.Tile;
                WorldActions_DesperationRaid.SuppressLossNotify(s);
                AtTurretUtility.DestroyTurretsBuiltBy(s);
                adv?.DestroyAllOwnedBy(s);
                WorldActions_RoadBlocks.ClearIfPresent(tile);
                WorldActions_SpikeTraps.ClearIfPresent(tile);
                s.Destroy();
            }

            bool marked = WorldActions_Utils.TryMarkDefeatedIfNoSettlementsLeft(faction);
            Find.World.GetComponent<WorldComponent_SpreadManager>()?.Notify_WeightsChanged();
            Find.World?.GetComponent<Text_WorldTierOnSettlements>()?.NotifyTierLabelCacheDirty();
            WorldActions_Utils.RefreshMap();

            string skipNote = skippedHasMap > 0 ? $" Skipped {skippedHasMap} open-map town(s)." : "";
            if (marked)
            {
                message = $"WD debug: destroyed {doomed.Count} settlement(s); {faction.Name} marked defeated.{skipNote}";
                return true;
            }

            if (WorldActions_Utils.HasActiveRefoundTraveler(faction))
            {
                message =
                    $"WD debug: destroyed {doomed.Count} settlement(s), but {faction.Name} still has a refound traveler (not marked defeated).{skipNote}";
                return true;
            }

            int left = WorldActions_Utils.CountWdSettlements(faction);
            message =
                $"WD debug: destroyed {doomed.Count} settlement(s); {faction.Name} still has {left} settlement(s) (not marked defeated).{skipNote}";
            return true;
        }

        private static Settlement SettlementAtTile(GlobalTargetInfo target)
        {
            if (!target.IsValid) return null;
            int tile = target.Tile;
            if (tile < 0) return null;
            foreach (WorldObject o in Find.WorldObjects.ObjectsAt(tile))
            {
                if (o is Settlement s && !s.Destroyed && s.Faction != null && !s.Faction.IsPlayer)
                    return s;
            }
            return null;
        }

        private static bool TryGetValidDebugSettlementTile(GlobalTargetInfo target, out int tile) =>
            WD_SettlementEditUtility.TryGetValidSettlementTile(target, out tile, enforceMinDistance: false, out _);

        private static bool TrySpawnWdSettlementAt(int tile, SettlementTier tier, Faction faction, out string failReason) =>
            WD_SettlementEditUtility.TrySpawnWdSettlementAt(tile, tier, faction, out failReason, enforceMinDistance: false);


        public static void UpgradeSettlementTier()
        {
            int tile = GenWorld.MouseTile();
            WorldObject obj = Find.WorldObjects.ObjectsAt(tile).FirstOrDefault(x => x is Settlement || x is WorldObject_WD_Outpost);

            if (obj == null) return;

            var comp = obj.GetComponent<CompViralSpread>();
            if (comp == null) return;

            if (comp.tier == SettlementTier.T1) comp.SetState(SettlementTier.T2);
            else if (comp.tier == SettlementTier.T2) comp.SetState(SettlementTier.T3);
            else if (comp.tier == SettlementTier.T3) comp.SetState(SettlementTier.T4);

            comp.strength = CompViralSpread.GetStrengthRange(comp.tier).min;

            Messages.Message($"Debug: {obj.LabelCap} upgraded to {comp.tier}.", MessageTypeDefOf.CautionInput);
            Find.World.GetComponent<WorldComponent_SpreadManager>()?.Notify_WeightsChanged();
        }


        public static void DowngradeSettlementTier()
        {
            int tile = GenWorld.MouseTile();
            WorldObject obj = Find.WorldObjects.ObjectsAt(tile).FirstOrDefault(x => x is Settlement || x is WorldObject_WD_Outpost);

            if (obj == null) return;

            var comp = obj.GetComponent<CompViralSpread>();
            if (comp == null) return;

            if (comp.tier == SettlementTier.T4) comp.SetState(SettlementTier.T3);
            else if (comp.tier == SettlementTier.T3) comp.SetState(SettlementTier.T2);
            else if (comp.tier == SettlementTier.T2) comp.SetState(SettlementTier.T1);

            comp.strength = CompViralSpread.GetStrengthRange(comp.tier).min;

            Messages.Message($"Debug: {obj.LabelCap} downgraded to {comp.tier}.", MessageTypeDefOf.CautionInput);
            Find.World.GetComponent<WorldComponent_SpreadManager>()?.Notify_WeightsChanged();
        }


        public static void ClearRaidProtectionCooldown()
        {
            int tile = GenWorld.MouseTile();
            WorldObject obj = Find.WorldObjects.ObjectsAt(tile)
                .FirstOrDefault(x => x is Settlement || x is WorldObject_WD_Outpost);
            if (obj == null)
            {
                Messages.Message("Debug: click an outpost, settlement, or colony.", MessageTypeDefOf.RejectInput);
                return;
            }

            var comp = obj.GetComponent<CompViralSpread>();
            if (comp == null)
            {
                Messages.Message($"Debug: {obj.LabelCap} has no CompViralSpread.", MessageTypeDefOf.RejectInput);
                return;
            }

            if (!comp.IsDefenseOnCooldown)
            {
                Messages.Message($"Debug: {obj.LabelCap} has no active raid protection.", MessageTypeDefOf.NeutralEvent);
                return;
            }

            comp.defenseCooldownTick = -1;
            Messages.Message($"Debug: cleared raid protection on {obj.LabelCap}.", MessageTypeDefOf.CautionInput);
        }


        public static void DebugStrengthPlus100()
        {
            int tile = GenWorld.MouseTile();
            WorldObject obj = Find.WorldObjects.ObjectsAt(tile).FirstOrDefault(x => x is Settlement || x is WorldObject_WD_Outpost);
            if (obj == null) return;
            var comp = obj.GetComponent<CompViralSpread>();
            if (comp == null) return;
            comp.AdjustStrengthWithinTier(100f);
            Messages.Message($"Debug: {obj.LabelCap} strength → {comp.strength:F0} (tier {comp.tier}, no tier change).", MessageTypeDefOf.CautionInput);
            Find.World.GetComponent<WorldComponent_SpreadManager>()?.Notify_WeightsChanged();
        }


        public static void DebugStrengthMinus100()
        {
            int tile = GenWorld.MouseTile();
            WorldObject obj = Find.WorldObjects.ObjectsAt(tile).FirstOrDefault(x => x is Settlement || x is WorldObject_WD_Outpost);
            if (obj == null) return;
            var comp = obj.GetComponent<CompViralSpread>();
            if (comp == null) return;
            comp.AdjustStrengthWithinTier(-100f);
            Messages.Message($"Debug: {obj.LabelCap} strength → {comp.strength:F0} (tier {comp.tier}, no tier change).", MessageTypeDefOf.CautionInput);
            Find.World.GetComponent<WorldComponent_SpreadManager>()?.Notify_WeightsChanged();
        }

        /// <summary>
        /// Force this NPC settlement to pick the player colony, then run experimental local-executor handoff
        /// (same faction, closer base launches). Requires experimental weighted target bands.
        /// </summary>

        public static void DebugForceColonyRaidExecutorHandoff()
        {
            int tile = GenWorld.MouseTile();
            if (tile < 0) return;

            Settlement settlement = Find.WorldObjects.ObjectsAt(tile).OfType<Settlement>().FirstOrDefault();
            if (settlement == null)
            {
                Messages.Message("WD debug: click an NPC settlement.", MessageTypeDefOf.RejectInput);
                return;
            }

            if (!WorldActions_Raid.DebugForceColonyRaidForExecutorHandoff(settlement, out string failReason, out bool handedOff))
            {
                Messages.Message(
                    "WD debug colony executor raid failed (" + settlement.LabelCap + "): " + (failReason ?? "unknown"),
                    MessageTypeDefOf.RejectInput);
                return;
            }

            if (handedOff)
            {
                Messages.Message(
                    "WD debug: " + settlement.LabelCap + " picked colony and delegated to a closer ally (see action log).",
                    MessageTypeDefOf.PositiveEvent);
            }
            else
            {
                Messages.Message(
                    "WD debug: " + settlement.LabelCap + " launched at colony with no handoff (no closer eligible same-faction executor).",
                    MessageTypeDefOf.CautionInput);
            }
        }

        /// <summary>Watch-index build/perf inspection for target-of-opportunity/marauding/ambush/Feature D tuning.</summary>

        public static void ForceRebuildSettlementWatchIndex()
        {
            var idx = WorldComponent_SettlementWatchIndex.Get();
            if (idx == null)
            {
                Messages.Message("WD debug: no SettlementWatchIndex on the current world.", MessageTypeDefOf.RejectInput);
                return;
            }
            string summary = idx.DebugForceRebuildAndSummarize();
            Log.Message($"[TSA WD] SettlementWatchIndex debug rebuild: {summary}");
            Messages.Message($"WD debug: watch index rebuilt ({summary})", MessageTypeDefOf.NeutralEvent);
        }
    }
}
