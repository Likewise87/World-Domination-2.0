using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Live registry of <see cref="WorldObject_WD_AdvancedBuilding"/> (<see cref="all"/> + tile index).
    /// Ownership for slots/types/effects/wipe is always derived from live <c>builtBySettlement</c> on
    /// <see cref="all"/> (AT-turret style) — never a denormalized owner map that can go stale on load.
    /// Rebuilt from world objects on FinalizeInit; deferred + daily heal collapses same-type duplicates.
    /// Also owns deferred work that must never run during load (free T4 turret, legacy grant, orphan sweep).
    /// </summary>
    public class WorldComponent_AdvancedBuildings : WorldComponent
    {
        private static WorldComponent_AdvancedBuildings cached;
        private static World cachedWorld;

        private readonly List<WorldObject_WD_AdvancedBuilding> all = new List<WorldObject_WD_AdvancedBuilding>();
        private readonly Dictionary<int, WorldObject_WD_AdvancedBuilding> byTile = new Dictionary<int, WorldObject_WD_AdvancedBuilding>();
        private static readonly List<WorldObject_WD_AdvancedBuilding> EmptyList = new List<WorldObject_WD_AdvancedBuilding>();

        /// <summary>Bumped on any add/remove; UI caches key on this.</summary>
        public int Version { get; private set; }

        /// <summary>Old saves: built-in T4 mortar/AA becomes a free turret once. False only on saves from before turrets.</summary>
        private bool legacyArtilleryGrantDone;
        private bool legacyGrantPending;
        private bool bootstrapSweepPending;
        private bool indexHealPending;
        private readonly List<Settlement> pendingFreeT4 = new List<Settlement>();
        private int lastOrphanSweepTick = -1;

        public WorldComponent_AdvancedBuildings(World world) : base(world) { }

        public static WorldComponent_AdvancedBuildings Get()
        {
            World w = Find.World;
            if (w == null) return null;
            if (cached != null && cachedWorld == w) return cached;
            cached = w.GetComponent<WorldComponent_AdvancedBuildings>();
            cachedWorld = w;
            return cached;
        }

        public IReadOnlyList<WorldObject_WD_AdvancedBuilding> All => all;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref legacyArtilleryGrantDone, "legacyArtilleryGrantDone", false);
        }

        public override void FinalizeInit(bool fromLoad)
        {
            base.FinalizeInit(fromLoad);
            cached = this;
            cachedWorld = world;
            RebuildIndex();
            pendingFreeT4.Clear();
            indexHealPending = fromLoad;
            if (!fromLoad)
            {
                legacyArtilleryGrantDone = true;
                bootstrapSweepPending = true;
            }
            else if (!legacyArtilleryGrantDone)
            {
                legacyGrantPending = true;
            }
        }

        public void RebuildIndex()
        {
            all.Clear();
            byTile.Clear();
            List<WorldObject> objs = Find.WorldObjects?.AllWorldObjects;
            if (objs != null)
            {
                for (int i = 0; i < objs.Count; i++)
                {
                    if (objs[i] is WorldObject_WD_AdvancedBuilding b && !b.Destroyed)
                        AddInternal(b);
                }
            }
            Version++;
        }

        public void Register(WorldObject_WD_AdvancedBuilding b)
        {
            if (b == null || b.Destroyed) return;
            if (!all.Contains(b))
                all.Add(b);
            RefreshTileIndex(b);
            NotifyChanged();
        }

        public void Unregister(WorldObject_WD_AdvancedBuilding b)
        {
            if (b == null) return;
            if (!all.Remove(b)) return;
            int tile = b.Tile.tileId;
            if (byTile.TryGetValue(tile, out var atTile) && atTile == b)
                byTile.Remove(tile);
            NotifyChanged();
        }

        private void AddInternal(WorldObject_WD_AdvancedBuilding b)
        {
            all.Add(b);
            RefreshTileIndex(b);
        }

        private void RefreshTileIndex(WorldObject_WD_AdvancedBuilding b)
        {
            if (b == null) return;
            int tile = b.Tile.tileId;
            if (tile >= 0)
                byTile[tile] = b;
        }

        private void NotifyChanged()
        {
            Version++;
            WorldComponent_SettlementWatchIndex.Get()?.Invalidate();
            WD_RadiusOverlayPrefs.InvalidateResolveCache();
            ReinforcementNeighborCache.BumpGeneration();
        }

        public WorldObject_WD_AdvancedBuilding At(int tileId)
        {
            if (tileId < 0) return null;
            if (byTile.TryGetValue(tileId, out var b) && b != null && !b.Destroyed) return b;
            return null;
        }

        public bool HasAt(int tileId) => At(tileId) != null;

        /// <summary>
        /// Live ownership: walk <see cref="all"/> by current <c>builtBySettlement</c>.
        /// Call sites are daily / inspect / ambush / selection — not every GUI frame.
        /// </summary>
        public List<WorldObject_WD_AdvancedBuilding> OwnedBy(Settlement owner)
        {
            if (owner == null) return EmptyList;
            var list = new List<WorldObject_WD_AdvancedBuilding>(4);
            for (int i = 0; i < all.Count; i++)
            {
                var b = all[i];
                if (b == null || b.Destroyed) continue;
                if (b.builtBySettlement == owner)
                    list.Add(b);
            }
            return list;
        }

        public int CountOwnedBy(Settlement owner)
        {
            if (owner == null) return 0;
            int n = 0;
            for (int i = 0; i < all.Count; i++)
            {
                var b = all[i];
                if (b != null && !b.Destroyed && b.builtBySettlement == owner)
                    n++;
            }
            return n;
        }

        public WorldObject_WD_AdvancedBuilding FindOwned(Settlement owner, AdvancedBuildingDef def)
        {
            if (owner == null || def == null) return null;
            for (int i = 0; i < all.Count; i++)
            {
                var b = all[i];
                if (b == null || b.Destroyed) continue;
                if (b.builtBySettlement == owner && b.BuildingDef == def)
                    return b;
            }
            return null;
        }

        public bool Has(Settlement owner, AdvancedBuildingDef def) => FindOwned(owner, def) != null;

        public bool HasEffect(Settlement owner, AdvancedBuildingEffect effect)
        {
            if (owner == null) return false;
            for (int i = 0; i < all.Count; i++)
            {
                var b = all[i];
                if (b == null || b.Destroyed) continue;
                if (b.builtBySettlement != owner) continue;
                if (b.BuildingDef != null && b.BuildingDef.effect == effect)
                    return true;
            }
            return false;
        }

        public int CountArtilleryOwnedBy(Settlement owner)
        {
            if (owner == null) return 0;
            int n = 0;
            for (int i = 0; i < all.Count; i++)
            {
                var b = all[i];
                if (b == null || b.Destroyed) continue;
                if (b.builtBySettlement != owner) continue;
                if (b.BuildingDef != null && b.BuildingDef.IsArtillery) n++;
            }
            return n;
        }

        /// <summary>True when the owner has any AT turret or advanced building (ratios / self-defense ratios).</summary>
        public bool OwnerHasAssets(Settlement owner)
        {
            if (owner == null) return false;
            if (CountOwnedBy(owner) > 0) return true;
            IReadOnlyList<WorldObject_AT_Turret> turrets = WorldObject_AT_Turret.LiveTurrets;
            for (int i = 0; i < turrets.Count; i++)
            {
                var t = turrets[i];
                if (t != null && !t.Destroyed && t.builtBySettlement == owner) return true;
            }
            return false;
        }

        /// <summary>True when the owner has a Stable (Feature C extended ambush watch radius).</summary>
        public bool OwnerHasStable(Settlement owner)
        {
            if (owner == null) return false;
            for (int i = 0; i < all.Count; i++)
            {
                var b = all[i];
                if (b == null || b.Destroyed) continue;
                if (b.builtBySettlement != owner) continue;
                AdvancedBuildingDef def = b.BuildingDef;
                if (def != null && def.effect == AdvancedBuildingEffect.LandSpeedPct)
                    return true;
            }
            return false;
        }

        /// <summary>Remove every building owned by <paramref name="owner"/> (owner razed or captured).</summary>
        public void DestroyAllOwnedBy(Settlement owner)
        {
            if (owner == null) return;
            var list = OwnedBy(owner);
            if (list.Count == 0) return;
            for (int i = 0; i < list.Count; i++)
            {
                var b = list[i];
                if (b == null || b.Destroyed) continue;
                b.suppressDestroyedLetter = true;
                b.Destroy();
            }
        }

        /// <summary>Queue the free first T4 turret (runs next tick, never during load).</summary>
        public void QueueFreeT4Artillery(Settlement s)
        {
            if (s == null || pendingFreeT4.Contains(s)) return;
            pendingFreeT4.Add(s);
        }

        public override void WorldComponentTick()
        {
            if (Find.TickManager == null) return;
            int now = Find.TickManager.TicksGame;

            if (indexHealPending)
            {
                indexHealPending = false;
                RebuildIndex();
                DedupeOwnedTypes();
            }

            if (legacyGrantPending)
            {
                legacyGrantPending = false;
                legacyArtilleryGrantDone = true;
                WorldActions_AdvancedBuildings.RunLegacyArtilleryGrant();
            }

            if (bootstrapSweepPending)
            {
                bootstrapSweepPending = false;
                WorldActions_AdvancedBuildings.QueueBootstrapT4Grants(this);
            }

            if (pendingFreeT4.Count > 0)
            {
                var batch = new List<Settlement>(pendingFreeT4);
                pendingFreeT4.Clear();
                for (int i = 0; i < batch.Count; i++)
                    WorldActions_AdvancedBuildings.TryGrantFreeT4Artillery(batch[i]);
            }

            if (lastOrphanSweepTick < 0)
                lastOrphanSweepTick = now;
            if (now - lastOrphanSweepTick >= GenDate.TicksPerDay)
            {
                lastOrphanSweepTick = now;
                RebuildIndex();
                RemoveOrphans();
                DedupeOwnedTypes();
                // Catches T4 foundings that skip the tier-change hook (conquest, pack-up, revolt, edits).
                WorldActions_AdvancedBuildings.QueueBootstrapT4Grants(this);
            }
        }

        private void RemoveOrphans()
        {
            if (all.Count == 0) return;
            List<WorldObject_WD_AdvancedBuilding> doomed = null;
            for (int i = 0; i < all.Count; i++)
            {
                var b = all[i];
                if (b == null || b.Destroyed) continue;
                if (!b.IsOrphan) continue;
                doomed ??= new List<WorldObject_WD_AdvancedBuilding>();
                doomed.Add(b);
            }
            if (doomed == null) return;
            for (int i = 0; i < doomed.Count; i++)
            {
                doomed[i].suppressDestroyedLetter = true;
                doomed[i].Destroy();
            }
        }

        /// <summary>
        /// Same owner + same <see cref="AdvancedBuildingDef"/>: keep earliest <c>placedTick</c>
        /// (tie-break lower world-object ID), destroy extras. Never call during load.
        /// </summary>
        private void DedupeOwnedTypes()
        {
            if (all.Count < 2) return;
            // key: owner.ID ^ def shortHash in high bits via composite string avoided — use nested dict
            var bestByOwnerDef = new Dictionary<int, Dictionary<AdvancedBuildingDef, WorldObject_WD_AdvancedBuilding>>();
            List<WorldObject_WD_AdvancedBuilding> doomed = null;

            for (int i = 0; i < all.Count; i++)
            {
                var b = all[i];
                if (b == null || b.Destroyed) continue;
                Settlement owner = b.builtBySettlement;
                AdvancedBuildingDef def = b.BuildingDef;
                if (owner == null || def == null) continue;

                if (!bestByOwnerDef.TryGetValue(owner.ID, out var byDef))
                {
                    byDef = new Dictionary<AdvancedBuildingDef, WorldObject_WD_AdvancedBuilding>();
                    bestByOwnerDef[owner.ID] = byDef;
                }

                if (!byDef.TryGetValue(def, out var keep) || keep == null || keep.Destroyed)
                {
                    byDef[def] = b;
                    continue;
                }

                WorldObject_WD_AdvancedBuilding winner = PreferOlderBuilding(keep, b);
                WorldObject_WD_AdvancedBuilding loser = winner == keep ? b : keep;
                byDef[def] = winner;
                doomed ??= new List<WorldObject_WD_AdvancedBuilding>();
                if (!doomed.Contains(loser))
                    doomed.Add(loser);
            }

            if (doomed == null) return;
            for (int i = 0; i < doomed.Count; i++)
            {
                var b = doomed[i];
                if (b == null || b.Destroyed) continue;
                b.suppressDestroyedLetter = true;
                b.Destroy();
            }
        }

        private static WorldObject_WD_AdvancedBuilding PreferOlderBuilding(
            WorldObject_WD_AdvancedBuilding a,
            WorldObject_WD_AdvancedBuilding b)
        {
            int ta = a.placedTick;
            int tb = b.placedTick;
            if (ta >= 0 && tb >= 0)
            {
                if (ta < tb) return a;
                if (tb < ta) return b;
            }
            else if (ta >= 0) return a;
            else if (tb >= 0) return b;
            return a.ID <= b.ID ? a : b;
        }
    }
}
