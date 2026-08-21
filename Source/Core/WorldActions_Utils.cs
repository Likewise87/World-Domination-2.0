using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using RimWorld.Planet;
using Verse;
using UnityEngine;

namespace TSA_WorldDomination
{
    /// <summary>Shared helpers for world actions: raid strength, settlement tier/init, protection, WD surface scope (see <see cref="IsWdSurfaceTile"/>), distance cache, travel/quest/relationship helpers.</summary>
    public static class WorldActions_Utils
    {
        /// <summary>
        /// True only when we can read the tile’s layer and it is the orbit layer. Layer-aware: resolves
        /// <see cref="PlanetTile.Layer"/> (via the tile's <c>layerId</c>), never the surface-only <c>WorldGrid[int]</c> indexer.
        /// Used to strip <see cref="CompViralSpread"/> — must not be true during early init when <see cref="WorldObject.Tile"/> or layer data is not ready yet.
        /// </summary>
        public static bool IsConfirmedOrbitTile(PlanetTile tile)
        {
            if (Find.World?.grid == null || Find.WorldGrid == null) return false;
            if (!tile.Valid) return false;
            try
            {
                PlanetLayer layer = tile.Layer;
                return layer != null && SpaceMapGuard.IsOrbitLayer(layer);
            }
            catch (Exception ex)
            {
                Log.Warning($"[WD] IsConfirmedOrbitTile: {ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }

        /// <summary>True when the parent’s tile is confirmed orbit (safe to strip comps).</summary>
        public static bool IsConfirmedOrbitWorldObject(WorldObject o) =>
            o != null && IsConfirmedOrbitTile(o.Tile);

        /// <summary>
        /// WD in-scope for simulation: the tile is on the <b>root planet surface</b>, OR layer data is not yet
        /// available (unready grid, invalid tile, null layer) so world-gen and comp init stay permissive and do
        /// not strip every settlement. Non-surface layers (orbit and any other layer) are out of scope.
        /// Layer-aware: resolves <see cref="PlanetTile.Layer"/>, never the surface-only <c>WorldGrid[int]</c> indexer.
        /// </summary>
        public static bool IsWdSurfaceTile(PlanetTile tile)
        {
            if (Find.World?.grid == null || Find.WorldGrid == null) return true; // not ready -> permissive
            if (!tile.Valid) return true; // unknown -> permissive
            try
            {
                PlanetLayer layer = tile.Layer;
                if (layer == null) return true; // unknown -> permissive
                PlanetLayer surface = Find.WorldGrid.Surface;
                return surface != null && ReferenceEquals(layer, surface);
            }
            catch (Exception ex)
            {
                Log.Warning($"[WD] IsWdSurfaceTile: {ex.GetType().Name}: {ex.Message}");
                return true; // permissive on error
            }
        }

        /// <summary>WD simulation only applies to objects on the planet surface (or while layer data is not yet ready).</summary>
        public static bool IsWdSurfaceWorldObject(WorldObject o) =>
            o != null && IsWdSurfaceTile(o.Tile);

        /// <summary>Single scan: world objects that have CompViralSpread, grouped by Faction. Used by Raid_Manager and outpost raid preview.</summary>
        private static readonly Dictionary<Faction, List<WorldObject>> s_compByFactionResult = new Dictionary<Faction, List<WorldObject>>();
        private static readonly Stack<List<WorldObject>> s_compByFactionPool = new Stack<List<WorldObject>>();

        public static Dictionary<Faction, List<WorldObject>> GetWorldObjectsWithCompByFaction()
        {
            foreach (var kvp in s_compByFactionResult)
            {
                kvp.Value.Clear();
                s_compByFactionPool.Push(kvp.Value);
            }
            s_compByFactionResult.Clear();

            var all = Find.WorldObjects.AllWorldObjects;
            for (int i = 0; i < all.Count; i++)
            {
                WorldObject s = all[i];
                if (s.GetComponent<CompViralSpread>() == null) continue;
                if (!IsWdSurfaceWorldObject(s)) continue;
                if (!s_compByFactionResult.TryGetValue(s.Faction, out var list))
                {
                    list = s_compByFactionPool.Count > 0 ? s_compByFactionPool.Pop() : new List<WorldObject>();
                    s_compByFactionResult[s.Faction] = list;
                }
                list.Add(s);
            }
            return s_compByFactionResult;
        }

        private static readonly List<WorldObject> EmptyWorldObjectList = new List<WorldObject>();
        /// <summary>Safe accessor for faction lookup dictionaries — returns empty list for missing keys (same semantics as ILookup).</summary>
        public static List<WorldObject> GetFactionObjects(Dictionary<Faction, List<WorldObject>> lookup, Faction f)
        {
            return (lookup != null && f != null && lookup.TryGetValue(f, out var list)) ? list : EmptyWorldObjectList;
        }

        public static float GetGarrisonRetainFloor(CompViralSpread comp, WorldDominationSettings seth)
        {
            if (comp == null || seth == null) return 0f;
            float max = comp.GetMaxOffensiveStrength();
            if (max <= 0f || float.IsInfinity(max)) return 0f;
            return max * Mathf.Clamp01(seth.garrisonRetainPct);
        }

        /// <summary>
        /// Strength that may leave on a raid/trader: current minus a retain floor of
        /// <see cref="WorldDominationSettings.garrisonRetainPct"/> × max (tier max for settlements, occupant max for outposts).
        /// </summary>
        public static float GetAvailableRaidStrength(CompViralSpread comp, WorldDominationSettings seth)
        {
            if (comp == null) return 0f;
            float retainFloor = GetGarrisonRetainFloor(comp, seth);
            return Mathf.Max(0f, comp.strength - retainFloor);
        }

        /// <summary>
        /// True if paying <paramref name="cost"/> still leaves the min garrison retain floor at home.
        /// Use for road / road-block / spike-trap / fortify expeditions (not decontamination).
        /// </summary>
        public static bool CanAffordExpeditionLeavingGarrison(CompViralSpread comp, float cost, WorldDominationSettings seth = null)
        {
            if (comp == null || cost <= 0f) return false;
            if (ColonyWorldBuildUtility.WaivesExpeditionStrength(comp)) return true;
            seth ??= WorldDominationMod.settings;
            if (seth == null) return comp.strength + 0.01f >= cost;
            return GetAvailableRaidStrength(comp, seth) + 0.01f >= cost;
        }

        /// <summary>Pay expedition strength, or no-op when colony world-build waives the cost.</summary>
        public static bool TryConsumeExpeditionStrength(CompViralSpread comp, float cost, WorldDominationSettings seth = null)
        {
            if (comp == null || cost <= 0f) return false;
            if (ColonyWorldBuildUtility.WaivesExpeditionStrength(comp)) return true;
            if (!CanAffordExpeditionLeavingGarrison(comp, cost, seth)) return false;
            comp.strength = Mathf.Max(0f, comp.strength - cost);
            comp.CheckTierUpdate(false);
            return true;
        }

        public static void RefundExpeditionStrength(CompViralSpread comp, float cost)
        {
            if (comp == null || cost <= 0f) return;
            if (ColonyWorldBuildUtility.WaivesExpeditionStrength(comp)) return;
            comp.AddStrength(cost);
        }

        /// <summary>How many fixed-cost expeditions fit in available (above-garrison) strength.</summary>
        public static int MaxAffordableExpeditionsLeavingGarrison(CompViralSpread comp, float costNeeded, WorldDominationSettings seth = null)
        {
            if (comp == null || costNeeded <= 0.01f) return 0;
            seth ??= WorldDominationMod.settings;
            float available = seth != null ? GetAvailableRaidStrength(comp, seth) : Mathf.Max(0f, comp.strength);
            return Mathf.Max(0, Mathf.FloorToInt((available + 0.01f) / costNeeded));
        }

        public static void EnsureAllSettlementsInitialized()
        {
            var settlements = Find.WorldObjects.Settlements;
            for (int i = 0; i < settlements.Count; i++)
            {
                Settlement s = settlements[i];
                if (s.Faction == null || s.Faction.def.hidden || s.Faction.defeated || IsExcludedFaction(s.Faction)) continue;
                if (!IsWdSurfaceWorldObject(s)) continue;
                var comp = s.GetComponent<CompViralSpread>();
                if (comp == null || !string.IsNullOrEmpty(comp.subType) || comp.IsOutpost) continue;
                ApplyRandomTier(comp);
            }
        }

        /// <summary>Grant the starting raid shield to every player map colony on a new game.</summary>
        public static void ApplyStartingPlayerColonyRaidShields()
        {
            var settlements = Find.WorldObjects.Settlements;
            for (int i = 0; i < settlements.Count; i++)
            {
                Settlement s = settlements[i];
                if (s?.Faction?.IsPlayer != true) continue;
                s.GetComponent<CompViralSpread>()?.EnsureInitialPlayerColonyShield();
            }
        }

        /// <summary>Loaded saves: skip retroactive starting shields for colonies that existed before this fix.</summary>
        public static void MarkExistingPlayerColoniesShieldHandled()
        {
            var settlements = Find.WorldObjects.Settlements;
            for (int i = 0; i < settlements.Count; i++)
            {
                Settlement s = settlements[i];
                if (s?.Faction?.IsPlayer != true) continue;
                s.GetComponent<CompViralSpread>()?.MarkInitialPlayerColonyShieldHandled();
            }
        }

        public static void ApplyRandomTier(CompViralSpread comp)
        {
            var s = WorldDominationMod.settings;

            float totalWeight = s.genWeightT1 + s.genWeightT2 + s.genWeightT3 + s.genWeightT4;
            if (totalWeight <= 0) { comp.SetState(SettlementTier.T1); return; }

            float rand = Rand.Range(0f, totalWeight);

            if (rand < s.genWeightT1)
                comp.SetState(SettlementTier.T1);
            else if (rand < s.genWeightT1 + s.genWeightT2)
                comp.SetState(SettlementTier.T2);
            else if (rand < s.genWeightT1 + s.genWeightT2 + s.genWeightT3)
                comp.SetState(SettlementTier.T3);
            else
                comp.SetState(SettlementTier.T4);
        }

        /// <summary>
        /// True when this faction may own settlements on the planet surface (Odyssey layer contract).
        /// Empty whitelist = unrestricted; non-empty without Surface (or Surface blacklisted) = orbit-only / out of WD scope.
        /// </summary>
        public static bool FactionAllowsSurfaceSettlements(FactionDef def)
        {
            if (def == null) return false;

            PlanetLayerDef surface = PlanetLayerDefOf.Surface;
            if (surface == null) return true; // no layer system → treat as surface-capable

            if (def.layerWhitelist != null && def.layerWhitelist.Count > 0 && !def.layerWhitelist.Contains(surface))
                return false;
            if (def.layerBlacklist != null && def.layerBlacklist.Contains(surface))
                return false;

            return true;
        }

        public static bool FactionAllowsSurfaceSettlements(Faction f) =>
            f?.def != null && FactionAllowsSurfaceSettlements(f.def);

        public static bool IsExcludedFaction(Faction f)
        {
            if (f == null || f.def == null || f.IsPlayer) return true;
            if (f == Faction.OfTradersGuild) return true;
            // Hidden factions (Ancients, Mechanoids, etc.) never participate in WD surface sim.
            if (f.def.hidden) return true;
            // No world-settlement generation weight → never placed by vanilla world gen (default 0).
            if (f.def.settlementGenerationWeight <= 0f) return true;

            if (!FactionAllowsSurfaceSettlements(f.def)) return true;

            string defName = f.def.defName ?? string.Empty;
            if (defName.IndexOf("Insect", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (defName.IndexOf("Hive", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            string label = f.Name?.ToString() ?? string.Empty;
            if (label.IndexOf("insect", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (label.IndexOf("hive", StringComparison.OrdinalIgnoreCase) >= 0) return true;

            return false;
        }

        public static bool IsSettlementProtected(Settlement s)
        {
            if (s == null) return true;
            if (!IsWdSurfaceWorldObject(s)) return true;

            var comp = s.GetComponent<CompViralSpread>();
            if (comp != null)
            {
                if (comp.IsDefenseOnCooldown || comp.IsIncidentOnCooldown) return true;
            }

            if (s.HasMap) return true;

            if (Find.WorldObjects.AnyWorldObjectOfDefAt(WorldObjectDefOf.Caravan, s.Tile)) return true;
            if (Find.WorldObjects.SettlementAt(s.Tile) is Settlement other && other.Faction != null && other.Faction.IsPlayer) return true;

            if (HasActiveQuest(s))
                return true;

            if (s.Faction == null || IsExcludedFaction(s.Faction) || s.Faction.defeated)
                return true;

            return false;
        }

        public static void RefreshMap()
        {
            try
            {
                var layersField = typeof(WorldRenderer).GetField("layers", BindingFlags.Instance | BindingFlags.NonPublic);
                if (layersField != null)
                {
                    var list = layersField.GetValue(Find.World.renderer) as System.Collections.IEnumerable;
                    if (list != null)
                    {
                        foreach (object layer in list) layer.GetType().GetMethod("SetDirty")?.Invoke(layer, null);
                    }
                }
            }
            catch (Exception ex) { Log.Warning($"[WD] {ex.GetType().Name}: {ex.Message}"); }
        }

        /// <summary>
        /// True when a world object must not be used as a WD surface travel / trade target
        /// (orbit or non-surface layer, invalid tile, or <see cref="SpaceMapGuard.IsSpaceLike"/> settlement).
        /// </summary>
        public static bool IsSpace(WorldObject obj)
        {
            if (obj == null) return false;
            if (SpaceMapGuard.IsSpaceLike(obj)) return true;
            return !PlanetSurfaceWorldActions.IsPlanetSurfaceTileForWorldActions(obj.Tile);
        }

        public static int GetDistance(int tileA, int tileB, WorldComponent_SpreadManager manager)
        {
            if (tileA == tileB) return 0;

            int t1 = Math.Min(tileA, tileB);
            int t2 = Math.Max(tileA, tileB);
            long key = ((long)t1 << 32) | (uint)t2;

            if (!manager.distanceCache.TryGetValue(key, out int dist))
            {
                // Prevent unbounded growth on very long sessions / huge worlds.
                const int DistanceCacheMaxEntries = 65536;
                if (manager.distanceCache.Count >= DistanceCacheMaxEntries)
                    manager.distanceCache.Clear();

                dist = Mathf.RoundToInt(Find.WorldGrid.ApproxDistanceInTiles(t1, t2));
                manager.distanceCache[key] = dist;
            }
            return dist;
        }

        public static void RerollAllSettlements()
        {
            var allSettlements = Find.WorldObjects.Settlements;
            var settlements = new List<Settlement>(allSettlements.Count);
            for (int i = 0; i < allSettlements.Count; i++)
            {
                if (allSettlements[i].Faction != null && !allSettlements[i].Faction.IsPlayer)
                    settlements.Add(allSettlements[i]);
            }

            foreach (var s in settlements)
            {
                if (!IsWdSurfaceWorldObject(s)) continue;
                var comp = s.GetComponent<CompViralSpread>();
                if (comp == null) continue;

                comp.strength = 0;
                ApplyRandomTier(comp);
                s.Name = s.Name;
            }

            Find.World.GetComponent<WorldComponent_SpreadManager>()?.Notify_WeightsChanged();

            Messages.Message("TSA_WD_Message_RerollSuccess".Translate(settlements.Count), MessageTypeDefOf.TaskCompletion);
        }

        public static bool HasActiveQuest(Settlement settlement)
        {
            if (settlement == null) return false;
            var quests = Find.QuestManager.QuestsListForReading;
            for (int i = 0; i < quests.Count; i++)
            {
                if (quests[i].State != QuestState.Ongoing) continue;
                bool found = false;
                foreach (var t in quests[i].QuestLookTargets)
                    if (t.WorldObject == settlement) { found = true; break; }
                if (found)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>True if this faction can never leave hostility with the player (vanilla permanent-enemy rules).</summary>
        public static bool IsPermanentEnemyOfPlayer(Faction faction)
        {
            if (faction?.def == null) return true;
            FactionDef def = faction.def;
            if (def.permanentEnemy) return true;
            Faction player = Faction.OfPlayerSilentFail;
            if (player?.def == null) return false;
            if (def.permanentEnemyToEveryoneExcept != null && !def.permanentEnemyToEveryoneExcept.Contains(player.def))
                return true;
            return false;
        }

        /// <summary>Uses <see cref="Faction.RelationWith(Faction, bool)"/> with allowCreate=true so missing rows do not hit vanilla <see cref="Faction.RelationKindWith"/> dummy-relation logging.</summary>
        public static FactionRelationKind SafeRelationKindWith(Faction facA, Faction facB)
        {
            if (facA == null || facB == null) return FactionRelationKind.Neutral;
            if (facA == facB) return FactionRelationKind.Neutral;
            FactionRelation rel = facA.RelationWith(facB, true);
            return rel?.kind ?? FactionRelationKind.Neutral;
        }

        /// <summary>Hostility check using <see cref="Faction.RelationWith(Faction, bool)"/> (allowCreate) instead of vanilla <see cref="Faction.HostileTo"/>, which calls <see cref="Faction.RelationKindWith"/> and spams errors when relation rows are not ready yet (common while loading a save).</summary>
        public static bool SafeHostileTo(Faction faction, Faction other)
        {
            if (faction == null || other == null) return false;
            if (faction == other) return false;
            FactionRelation rel = faction.RelationWith(other, true);
            return rel != null && rel.kind == FactionRelationKind.Hostile;
        }

        public static string GetRelationshipLabel(Faction other)
        {
            if (other == null) return "---";
            if (other.IsPlayer) return "TSA_WD_Faction_Player".Translate().Colorize(Color.cyan);

            Faction player = Faction.OfPlayerSilentFail;
            if (player == null) return "---";

            FactionRelationKind kind = SafeRelationKindWith(other, player);

            string label = kind.GetLabel();

            if (kind == FactionRelationKind.Hostile) return label.Colorize(ColorLibrary.RedReadable);
            if (kind == FactionRelationKind.Ally) return label.Colorize(ColorLibrary.LightGreen);

            return label;
        }
    }
}
