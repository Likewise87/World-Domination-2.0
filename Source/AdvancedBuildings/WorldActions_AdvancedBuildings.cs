using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// "Advanced building projects" daily action (inverse of Fortify: safer towns build more, floor so
    /// threatened towns still participate) plus the artillery rules: T3 30% roll, free first T4 turret,
    /// one-time grant for saves from before turrets. Threatened/recovering bias artillery in the pick pool
    /// but keep economic types eligible. Slots = tier number (T1 1 .. T4 4), one per type.
    /// </summary>
    public static class WorldActions_AdvancedBuildings
    {
        public const float T3ArtilleryRollChance = 0.30f;
        /// <summary>Fully safe / no hostile nearby. Threatened is only ~20% below this.</summary>
        public const float SafetyMultSafe = 1.0f;
        /// <summary>Threatened / recovering / near-frontline floor (~20% below <see cref="SafetyMultSafe"/>).</summary>
        public const float SafetyMultFloor = 0.80f;
        /// <summary>When threatened/recovering, artillery pick-weights × this (~20% bias).</summary>
        public const float ThreatenedArtilleryWeightBias = 1.20f;
        public const string BuilderTravelerDefName = "TSA_WD_Traveler_Outpost_RoadBlock";

        private static Dictionary<WorldObjectDef, AdvancedBuildingDef> byWorldObjectDef;
        private static readonly List<AdvancedBuildingDef> tmpPoolDefs = new List<AdvancedBuildingDef>();
        private static readonly List<float> tmpPoolWeights = new List<float>();
        private static readonly List<int> tmpTiles = new List<int>();
        private static readonly List<PlanetTile> tmpNeighbors = new List<PlanetTile>();
        private static readonly HashSet<int> tmpVisited = new HashSet<int>();
        private static readonly List<int> tmpFrontier = new List<int>();
        private static readonly List<int> tmpNext = new List<int>();

        // ---------- Defs ----------

        public static AdvancedBuildingDef DefForWorldObjectDef(WorldObjectDef woDef)
        {
            if (woDef == null) return null;
            if (byWorldObjectDef == null)
            {
                byWorldObjectDef = new Dictionary<WorldObjectDef, AdvancedBuildingDef>();
                foreach (AdvancedBuildingDef d in DefDatabase<AdvancedBuildingDef>.AllDefsListForReading)
                {
                    if (d.worldObjectDef != null && !byWorldObjectDef.ContainsKey(d.worldObjectDef))
                        byWorldObjectDef[d.worldObjectDef] = d;
                }
            }
            return byWorldObjectDef.TryGetValue(woDef, out var r) ? r : null;
        }

        public static int SlotsForTier(SettlementTier tier) => Mathf.Clamp((int)tier + 1, 1, 4);

        /// <summary>Master toggle for that turret type plus the NPC artillery tech level.</summary>
        public static bool IsArtilleryEnabled(AdvancedBuildingDef def, Faction faction)
        {
            if (def == null || !def.IsArtillery) return false;
            if (!WdMedievalMode.IsAdvancedBuildingAllowed(def)) return false;
            var seth = WorldDominationMod.settings;
            bool on = def.effect == AdvancedBuildingEffect.Mortar
                ? (seth?.enableNpcT4Mortar ?? WorldDominationSettings.DefEnableNpcT4Mortar)
                : (seth?.enableNpcT4AntiAir ?? WorldDominationSettings.DefEnableNpcT4AntiAir);
            if (!on) return false;
            if (faction == null || faction.IsPlayer) return false;
            TechLevel minTech = WdMedievalMode.EffectiveNpcArtilleryMinTech(seth);
            return faction.def == null || faction.def.techLevel >= minTech;
        }

        private static bool IsNpcBuilderSettlement(Settlement s, CompViralSpread comp)
        {
            if (s == null || s.Destroyed || comp == null) return false;
            if (!comp.IsSettlement || comp.IsOutpost) return false;
            if (s.Faction == null || s.Faction.IsPlayer) return false;
            return PlanetSurfaceWorldActions.IsPlanetSurfaceWorldObjectForWorldActions(s);
        }

        // ---------- Tier hooks ----------

        /// <summary>Called on every tier change (CompViralSpread). Rolls the T3 artillery chance and queues the free T4 turret.</summary>
        public static void NotifyTierChanged(CompViralSpread comp, SettlementTier oldTier, SettlementTier newTier)
        {
            if (comp == null || oldTier == newTier) return;
            if (!(comp.parent is Settlement s) || s.Faction == null || s.Faction.IsPlayer) return;
            if (newTier >= SettlementTier.T3)
                EnsureArtilleryRolled(comp);
            if (newTier == SettlementTier.T4 && oldTier != SettlementTier.T4)
            {
                comp.freeT4ArtilleryDone = false;
                WorldComponent_AdvancedBuildings.Get()?.QueueFreeT4Artillery(s);
            }
        }

        /// <summary>T3+ settlements roll once (30%); saves from before the roll existed roll lazily.</summary>
        public static void EnsureArtilleryRolled(CompViralSpread comp)
        {
            if (comp == null || comp.artilleryRoll >= 0) return;
            if (comp.tier < SettlementTier.T3) return;
            comp.artilleryRoll = Rand.Chance(T3ArtilleryRollChance) ? 1 : 0;
        }

        // ---------- In-flight builders ----------

        public static int CountInFlightBuilders(Settlement owner)
        {
            if (owner == null) return 0;
            int n = 0;
            IReadOnlyList<WorldObject_Traveler> live = WorldObject_Traveler.LiveTravelers;
            for (int i = 0; i < live.Count; i++)
            {
                var t = live[i];
                if (t == null || t.Destroyed || !t.IsAdvancedBuilder) continue;
                if (t.originObject == owner) n++;
            }
            return n;
        }

        private static bool IsInFlight(Settlement owner, AdvancedBuildingDef def, out bool anyArtilleryInFlight)
        {
            anyArtilleryInFlight = false;
            bool found = false;
            IReadOnlyList<WorldObject_Traveler> live = WorldObject_Traveler.LiveTravelers;
            for (int i = 0; i < live.Count; i++)
            {
                var t = live[i];
                if (t == null || t.Destroyed || !t.IsAdvancedBuilder || t.originObject != owner) continue;
                var d = DefDatabase<AdvancedBuildingDef>.GetNamedSilentFail(t.advancedBuildDefName);
                if (d == null) continue;
                if (d.IsArtillery) anyArtilleryInFlight = true;
                if (d == def) found = true;
            }
            return found;
        }

        private static bool IsBuilderDestTile(int tileId)
        {
            IReadOnlyList<WorldObject_Traveler> live = WorldObject_Traveler.LiveTravelers;
            for (int i = 0; i < live.Count; i++)
            {
                var t = live[i];
                if (t == null || t.Destroyed || !t.IsAdvancedBuilder || t.pather == null) continue;
                if (t.pather.destTile.Valid && t.pather.destTile.tileId == tileId) return true;
            }
            return false;
        }

        public static int UsedSlots(Settlement owner)
        {
            var reg = WorldComponent_AdvancedBuildings.Get();
            return (reg?.CountOwnedBy(owner) ?? 0) + CountInFlightBuilders(owner);
        }

        public static bool HasFreeSlot(Settlement owner, CompViralSpread comp) =>
            comp != null && UsedSlots(owner) < SlotsForTier(comp.tier);

        // ---------- Pick pool ----------

        /// <summary>
        /// Fills the weighted pool for this settlement. Returns false when nothing is buildable.
        /// T4 without artillery still prioritizes artillery exclusively until the first turret is placed/in flight.
        /// Otherwise economic types are always eligible; <paramref name="preferArtillery"/> only biases artillery weights up.
        /// </summary>
        public static bool BuildPool(Settlement s, CompViralSpread comp, bool preferArtillery,
            List<AdvancedBuildingDef> defs, List<float> weights)
        {
            defs.Clear();
            weights.Clear();
            var reg = WorldComponent_AdvancedBuildings.Get();
            if (reg == null || s == null || comp == null) return false;
            EnsureArtilleryRolled(comp);
            SettlementTier tier = comp.tier;

            IsInFlight(s, null, out bool artInFlight);
            int artOwned = reg.CountArtilleryOwnedBy(s) + (artInFlight ? 1 : 0);

            List<AdvancedBuildingDef> all = DefDatabase<AdvancedBuildingDef>.AllDefsListForReading;
            for (int i = 0; i < all.Count; i++)
            {
                var d = all[i];
                if (!d.IsArtillery) continue;
                if (!IsArtilleryEnabled(d, s.Faction)) continue;
                if (reg.Has(s, d) || IsInFlight(s, d, out _)) continue;
                float w;
                if (tier == SettlementTier.T4)
                    w = artOwned == 0 ? 1f : d.WeightForTier(SettlementTier.T4);
                else if (tier == SettlementTier.T3)
                {
                    if (comp.artilleryRoll != 1 || artOwned > 0) continue;
                    w = d.WeightForTier(SettlementTier.T3);
                }
                else continue;
                if (w <= 0f) continue;
                if (preferArtillery) w *= ThreatenedArtilleryWeightBias;
                defs.Add(d);
                weights.Add(w);
            }

            // First T4 turret is still mandatory before economic projects.
            bool t4NeedsFirst = tier == SettlementTier.T4 && artOwned == 0 && defs.Count > 0;
            if (t4NeedsFirst)
                return defs.Count > 0;

            for (int i = 0; i < all.Count; i++)
            {
                var d = all[i];
                if (d.IsArtillery) continue;
                if (reg.Has(s, d) || IsInFlight(s, d, out _)) continue;
                float w = d.WeightForTier(tier);
                if (w <= 0f) continue;
                defs.Add(d);
                weights.Add(w);
            }
            return defs.Count > 0;
        }

        private static AdvancedBuildingDef PickWeighted(List<AdvancedBuildingDef> defs, List<float> weights)
        {
            float total = 0f;
            for (int i = 0; i < weights.Count; i++) total += weights[i];
            if (total <= 0f) return null;
            float r = Rand.Range(0f, total);
            float c = 0f;
            for (int i = 0; i < defs.Count; i++)
            {
                c += weights[i];
                if (r < c) return defs[i];
            }
            return defs[defs.Count - 1];
        }

        // ---------- Daily weight ----------

        /// <summary>
        /// Safety multiplier (inverse of Fortify): only ~20% spread between threatened and fully safe.
        /// Threatened/recovering → <see cref="SafetyMultFloor"/> (0.8); no hostile / far → <see cref="SafetyMultSafe"/> (1.0);
        /// otherwise lerp floor→safe by how far past attack range the nearest hostile is.
        /// </summary>
        public static float GetSafetyMult(Settlement actor, CompViralSpread comp, bool isRecovering, WorldComponent_SpreadManager manager)
        {
            if (actor == null || comp == null) return 0f;
            if (comp.fortifyThreatenedToday || isRecovering) return SafetyMultFloor;
            var seth = WorldDominationMod.settings;
            WorldObject hostile = comp.fortifyNearestHostile;
            float expandR = Mathf.Max(1f, seth?.expandMaxRadius ?? WorldDominationSettings.DefExpandMaxRad);
            if (hostile == null || hostile.Destroyed)
                return SafetyMultSafe;
            float dist = manager != null
                ? (float)WorldActions_Utils.GetDistance(actor.Tile, hostile.Tile, manager)
                : Find.WorldGrid.ApproxDistanceInTiles(actor.Tile, hostile.Tile);
            if (dist > expandR * 2f)
                return SafetyMultSafe;
            float range = Mathf.Max(1f, SettlementAttackRangeUtil.GetNpcSettlementAttackRangeTiles(actor, seth));
            // 0 at attack range, 1 at 3× attack range (same shape as before, mapped into [floor, safe]).
            float t = Mathf.Clamp01((dist / range - 1f) / 2f);
            return Mathf.Lerp(SafetyMultFloor, SafetyMultSafe, t);
        }

        /// <summary>Weight for the orchestrator. Always scaled by floored safety mult; pool keeps economic + artillery.</summary>
        public static float GetDailyWeight(Settlement actor, CompViralSpread comp, bool isRecovering, WorldComponent_SpreadManager manager)
        {
            var seth = WorldDominationMod.settings;
            float baseW = seth?.weightAdvancedBuildings ?? WorldDominationSettings.DefWeightAdvancedBuildings;
            if (baseW <= 0f) return 0f;
            if (!IsNpcBuilderSettlement(actor, comp)) return 0f;
            if (comp.IsAdvancedBuildOnCooldown) return 0f;
            if (!HasFreeSlot(actor, comp)) return 0f;

            bool preferArtillery = comp.fortifyThreatenedToday || isRecovering;
            if (!BuildPool(actor, comp, preferArtillery, tmpPoolDefs, tmpPoolWeights)) return 0f;
            return baseW * GetSafetyMult(actor, comp, isRecovering, manager);
        }

        // ---------- Attempt ----------

        public static bool Attempt(Settlement actor, CompViralSpread comp, WorldComponent_SpreadManager manager, bool isRecovering)
        {
            var seth = WorldDominationMod.settings;
            if (seth == null || !IsNpcBuilderSettlement(actor, comp)) return false;
            if (comp.IsAdvancedBuildOnCooldown || !HasFreeSlot(actor, comp)) return false;

            bool preferArtillery = comp.fortifyThreatenedToday || isRecovering;
            if (!BuildPool(actor, comp, preferArtillery, tmpPoolDefs, tmpPoolWeights)) return false;

            while (tmpPoolDefs.Count > 0)
            {
                AdvancedBuildingDef def = PickWeighted(tmpPoolDefs, tmpPoolWeights);
                if (def == null) return false;
                if (FindBuildTile(actor, def, out int tile))
                {
                    float cost = Mathf.Max(1f, def.travelerStrength);
                    if (!SpawnBuilder(actor, comp, def, tile, cost)) return false;
                    manager?.AddLog(new SpreadLogEntry(
                        "TSA_WD_AdvBuild_Log_BuilderLaunched".Translate(actor.LabelCap, def.LabelText).ToString(),
                        actor, tile));
                    comp.advancedBuildCooldownTick = Find.TickManager.TicksGame
                        + Mathf.RoundToInt(Mathf.Max(0f, seth.cooldownAdvancedBuildingDays) * GenDate.TicksPerDay);
                    return true;
                }
                int idx = tmpPoolDefs.IndexOf(def);
                tmpPoolDefs.RemoveAt(idx);
                tmpPoolWeights.RemoveAt(idx);
            }
            return false;
        }

        private static bool SpawnBuilder(Settlement origin, CompViralSpread comp, AdvancedBuildingDef def, int destTile, float cost)
        {
            if (!WorldActions_Utils.CanAffordExpeditionLeavingGarrison(comp, cost)) return false;
            WorldObjectDef woDef = DefDatabase<WorldObjectDef>.GetNamedSilentFail(BuilderTravelerDefName)
                ?? DefDatabase<WorldObjectDef>.GetNamedSilentFail("TSA_WD_Traveler_Outpost_RoadBuilder");
            if (woDef == null) return false;
            if (!WorldActions_Utils.TryConsumeExpeditionStrength(comp, cost)) return false;

            var traveler = (WorldObject_Traveler)WorldObjectMaker.MakeWorldObject(woDef);
            traveler.Tile = origin.Tile;
            traveler.SetFaction(origin.Faction);
            traveler.mission = TravelerMission.NpcAtTurret;
            traveler.advancedBuildDefName = def.defName;
            traveler.originObject = origin;
            traveler.travelerStrength = cost;
            traveler.initialStrength = cost;
            AdvancedBuildingEffects.ApplyNpcLandSpeed(traveler, origin);

            Find.WorldObjects.Add(traveler);
            traveler.pather.StartPath(PlanetSurfaceWorldActions.PlanetTileForWdTravel(destTile, origin));
            if (traveler.Destroyed)
            {
                WorldActions_Utils.RefundExpeditionStrength(comp, cost);
                return false;
            }
            return true;
        }

        /// <summary>Builder arrived: place if the owner is alive, the slot/type rules still allow it, and the tile is clear; else refund.</summary>
        public static void ExecuteBuilderArrival(WorldObject_Traveler traveler)
        {
            if (traveler == null) return;
            Settlement origin = traveler.originObject as Settlement;
            if (origin == null || origin.Destroyed || origin.Faction != traveler.Faction) return;
            var comp = origin.GetComponent<CompViralSpread>();
            var def = DefDatabase<AdvancedBuildingDef>.GetNamedSilentFail(traveler.advancedBuildDefName);
            float refund = Mathf.Max(0f, traveler.travelerStrength);
            int tile = traveler.Tile.tileId;

            // This builder still counts as in flight while arriving.
            bool ok = def != null && comp != null
                && CanStillPlace(origin, comp, def, excludeInFlight: 1)
                && IsBuildableTile(tile, origin);
            if (!ok)
            {
                WorldActions_Utils.RefundExpeditionStrength(comp, refund);
                return;
            }

            var b = Place(def, origin, tile);
            if (b == null)
            {
                WorldActions_Utils.RefundExpeditionStrength(comp, refund);
                return;
            }
            Find.World?.GetComponent<WorldComponent_SpreadManager>()?.AddLog(new SpreadLogEntry(
                "TSA_WD_AdvBuild_Log_Built".Translate(origin.LabelCap, def.LabelText).ToString(), origin, tile));
        }

        private static bool CanStillPlace(Settlement owner, CompViralSpread comp, AdvancedBuildingDef def, int excludeInFlight)
        {
            var reg = WorldComponent_AdvancedBuildings.Get();
            if (reg == null) return false;
            if (reg.Has(owner, def)) return false;
            int used = reg.CountOwnedBy(owner) + Mathf.Max(0, CountInFlightBuilders(owner) - excludeInFlight);
            if (used >= SlotsForTier(comp.tier)) return false;
            if (def.IsArtillery)
            {
                if (!IsArtilleryEnabled(def, owner.Faction)) return false;
                int art = reg.CountArtilleryOwnedBy(owner);
                if (comp.tier < SettlementTier.T3) return false;
                if (comp.tier == SettlementTier.T3 && art > 0) return false;
            }
            return true;
        }

        // ---------- Tiles ----------

        /// <summary>Surface land, roads allowed; no settlement/outpost/site, fortification, building, or fortify-blacklist paint.</summary>
        public static bool IsBuildableTile(int tileId, Settlement owner)
        {
            if (tileId < 0) return false;
            if (WorldComponent_WdBridges.IsBridgedWaterTile(tileId)) return false;
            if (!WorldActions_RoadBlocks.IsTileBaseEligibleForRoadBlock(tileId)) return false;
            if (WorldActions_Fortifications.HasFortificationAt(tileId)) return false;
            if (owner != null && WorldComponent_FortifyBlacklist.BlocksNpcFortify(tileId, owner.Faction)) return false;
            PlanetLayer layer = PlanetSurfaceWorldActions.WdSurfaceLayer ?? Find.WorldGrid?.Surface;
            if (layer == null) return false;
            foreach (WorldObject wo in Find.WorldObjects.ObjectsAt(new PlanetTile(tileId, layer)))
            {
                if (wo == null || wo.Destroyed) continue;
                if (wo is WorldObject_Traveler || wo is Caravan) continue;
                return false;
            }
            return true;
        }

        /// <summary>
        /// Pick one exact ring at a time (no mixing closer rings into the pool).
        /// Artillery: r=1, then 2..max. Economic: r=2, then outside fortify belt, then outward, then remaining rings.
        /// </summary>
        public static bool FindBuildTile(Settlement owner, AdvancedBuildingDef def, out int tile)
        {
            tile = -1;
            if (owner == null || def == null) return false;
            WorldGrid grid = Find.WorldGrid;
            if (grid == null) return false;

            var comp = owner.GetComponent<CompViralSpread>();
            SettlementTier tier = comp?.tier ?? SettlementTier.T1;
            int outerFortify = WorldActions_FortifyKit.OuterFortifyRadius(tier);
            int max = Mathf.Max(5, def.maxRadius);
            if (def.IsArtillery)
                max = Mathf.Max(4, def.maxRadius);

            // Build per-ring candidate lists once via BFS.
            var byRing = new List<int>[max + 1];
            for (int r = 0; r <= max; r++)
                byRing[r] = new List<int>(8);

            tmpVisited.Clear();
            tmpFrontier.Clear();
            int origin = owner.Tile.tileId;
            tmpVisited.Add(origin);
            tmpFrontier.Add(origin);
            for (int ring = 1; ring <= max; ring++)
            {
                tmpNext.Clear();
                for (int f = 0; f < tmpFrontier.Count; f++)
                {
                    int from = tmpFrontier[f];
                    tmpNeighbors.Clear();
                    grid.GetTileNeighbors(new PlanetTile(from, owner.Tile.Layer), tmpNeighbors);
                    for (int n = 0; n < tmpNeighbors.Count; n++)
                    {
                        int id = tmpNeighbors[n].tileId;
                        if (!tmpVisited.Add(id)) continue;
                        tmpNext.Add(id);
                        if (IsBuildableTile(id, owner) && !IsBuilderDestTile(id))
                            byRing[ring].Add(id);
                    }
                }
                tmpFrontier.Clear();
                tmpFrontier.AddRange(tmpNext);
                if (tmpFrontier.Count == 0) break;
            }

            if (def.IsArtillery)
            {
                for (int ring = 1; ring <= max; ring++)
                {
                    if (TryPickFromRing(byRing[ring], out tile))
                        return true;
                }
                return false;
            }

            // Economic: prefer r=2, then outside fortify (outer+1..max), skip fortify belt until last resort, then r=1.
            int outside = outerFortify + 1;
            if (TryPickFromRing(byRing[2], out tile))
                return true;
            for (int ring = outside; ring <= max; ring++)
            {
                if (TryPickFromRing(byRing[ring], out tile))
                    return true;
            }
            // Last resort: fortify belt (if not already tried as r=2), then r=1.
            if (outerFortify != 2 && TryPickFromRing(byRing[outerFortify], out tile))
                return true;
            if (TryPickFromRing(byRing[1], out tile))
                return true;
            return false;
        }

        private static bool TryPickFromRing(List<int> ringTiles, out int tile)
        {
            tile = -1;
            if (ringTiles == null || ringTiles.Count == 0) return false;
            tile = ringTiles.RandomElement();
            return true;
        }

        // ---------- Placement ----------

        /// <summary>Spawn the building now. Artillery starts at full strength and on full fire cooldown.</summary>
        public static WorldObject_WD_AdvancedBuilding Place(AdvancedBuildingDef def, Settlement owner, int tileId)
        {
            if (def?.worldObjectDef == null || owner == null || owner.Faction == null) return null;
            if (!IsBuildableTile(tileId, owner)) return null;
            if (!(WorldObjectMaker.MakeWorldObject(def.worldObjectDef) is WorldObject_WD_AdvancedBuilding b)) return null;
            b.Tile = new PlanetTile(tileId, owner.Tile.Layer);
            b.SetFaction(owner.Faction);
            b.InitBuilding(def, owner);
            if (b is WorldObject_WD_ArtilleryTurret art)
            {
                art.InitArtillery();
                art.ApplyCooldown();
            }
            Find.WorldObjects.Add(b);
            return b;
        }

        // ---------- Free T4 artillery / save grant ----------

        public static void QueueBootstrapT4Grants(WorldComponent_AdvancedBuildings reg)
        {
            if (reg == null) return;
            var settlements = Find.WorldObjects?.Settlements;
            if (settlements == null) return;
            for (int i = 0; i < settlements.Count; i++)
            {
                Settlement s = settlements[i];
                var comp = s?.GetComponent<CompViralSpread>();
                if (!IsNpcBuilderSettlement(s, comp)) continue;
                if (comp.tier != SettlementTier.T4 || comp.freeT4ArtilleryDone) continue;
                reg.QueueFreeT4Artillery(s);
            }
        }

        /// <summary>First artillery on reaching T4: free, no builder, 50/50 between enabled types.</summary>
        public static void TryGrantFreeT4Artillery(Settlement s)
        {
            var comp = s?.GetComponent<CompViralSpread>();
            if (!IsNpcBuilderSettlement(s, comp)) return;
            if (comp.tier != SettlementTier.T4 || comp.freeT4ArtilleryDone) return;
            var reg = WorldComponent_AdvancedBuildings.Get();
            if (reg == null) return;

            IsInFlight(s, null, out bool artInFlight);
            if (reg.CountArtilleryOwnedBy(s) > 0 || artInFlight || !HasFreeSlot(s, comp))
            {
                comp.freeT4ArtilleryDone = true;
                return;
            }

            var options = new List<AdvancedBuildingDef>(2);
            if (IsArtilleryEnabled(AdvancedBuildingDefOf.TSA_WD_AdvBuilding_MortarTurret, s.Faction))
                options.Add(AdvancedBuildingDefOf.TSA_WD_AdvBuilding_MortarTurret);
            if (IsArtilleryEnabled(AdvancedBuildingDefOf.TSA_WD_AdvBuilding_AaTurret, s.Faction))
                options.Add(AdvancedBuildingDefOf.TSA_WD_AdvBuilding_AaTurret);
            // Both disabled: leave pending so enabling a toggle later still grants.
            if (options.Count == 0) return;

            comp.freeT4ArtilleryDone = true;
            AdvancedBuildingDef def = options.RandomElement();
            // No tile: the daily action picks artillery first for a T4 without any.
            if (!FindBuildTile(s, def, out int tile)) return;
            var b = Place(def, s, tile);
            if (b == null) return;
            Find.World?.GetComponent<WorldComponent_SpreadManager>()?.AddLog(new SpreadLogEntry(
                "TSA_WD_AdvBuild_Log_FreeArtillery".Translate(s.LabelCap, def.LabelText).ToString(), s, tile));
        }

        /// <summary>
        /// One-time for saves from before turrets: each T4 gets a Mortar Turret if its built-in mortar was active
        /// and an AA Turret if its built-in AA was active. Turrets start on full cooldown.
        /// </summary>
        public static void RunLegacyArtilleryGrant()
        {
            var settlements = Find.WorldObjects?.Settlements;
            if (settlements == null) return;
            var copy = new List<Settlement>(settlements);
            int granted = 0;
            for (int i = 0; i < copy.Count; i++)
            {
                Settlement s = copy[i];
                var comp = s?.GetComponent<CompViralSpread>();
                if (!IsNpcBuilderSettlement(s, comp)) continue;
                if (comp.tier != SettlementTier.T4) continue;
                comp.freeT4ArtilleryDone = true;
                var reg = WorldComponent_AdvancedBuildings.Get();
                if (reg == null) return;

                if (comp.IsSettlementMortarAutoActive)
                    granted += TryGrantLegacy(s, AdvancedBuildingDefOf.TSA_WD_AdvBuilding_MortarTurret, reg) ? 1 : 0;
                if (comp.IsSettlementAntiAirAutoActive)
                    granted += TryGrantLegacy(s, AdvancedBuildingDefOf.TSA_WD_AdvBuilding_AaTurret, reg) ? 1 : 0;
            }
            if (granted > 0)
                Log.Message($"[TSA WD] Advanced buildings: granted {granted} artillery turret(s) to existing T4 settlements.");
        }

        private static bool TryGrantLegacy(Settlement s, AdvancedBuildingDef def, WorldComponent_AdvancedBuildings reg)
        {
            if (def == null || reg.Has(s, def)) return false;
            if (!IsArtilleryEnabled(def, s.Faction)) return false;
            if (!FindBuildTile(s, def, out int tile)) return false;
            return Place(def, s, tile) != null;
        }
    }
}
