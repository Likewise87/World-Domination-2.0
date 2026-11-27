using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Temporary scar after a settlement is razed. Blocks establishment / expand / conqueror founding
    /// with the same min-distance radius as settlements and WD outposts until it despawns.
    /// FoW target: hide when undiscovered, dim outside LOS (same site pattern as settlements).
    /// </summary>
    [StaticConstructorOnStartup]
    public class WorldObject_WdSettlementRuin : WorldObject
    {
        private static readonly Material CachedMaterial =
            MaterialPool.MatFrom("World/WorldObjects/TribalSettlement", ShaderDatabase.WorldOverlayTransparentLit, WorldMaterials.WorldObjectRenderQueue);

        private static readonly List<WorldObject_WdSettlementRuin> liveRuins = new List<WorldObject_WdSettlementRuin>();
        private static readonly List<Faction> tmpKnowers = new List<Faction>(8);

        private string originalSettlementName;
        private int expireTick = -1;

        public static IReadOnlyList<WorldObject_WdSettlementRuin> LiveRuins => liveRuins;

        public string OriginalSettlementName
        {
            get => originalSettlementName;
            set => originalSettlementName = value;
        }

        public int ExpireTick => expireTick;

        public override Material Material => CachedMaterial;

        public override string Label
        {
            get
            {
                if (!originalSettlementName.NullOrEmpty())
                    return "TSA_WD_SettlementRuin_LabelNamed".Translate(originalSettlementName);
                return "TSA_WD_SettlementRuin_Label".Translate();
            }
        }

        public static void RebuildLiveRegistry()
        {
            liveRuins.Clear();
            WorldObjectsHolder worldObjects = Find.WorldObjects;
            if (worldObjects == null) return;
            List<WorldObject> all = worldObjects.AllWorldObjects;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] is WorldObject_WdSettlementRuin r && !r.Destroyed)
                    liveRuins.Add(r);
            }
        }

        public void Configure(string settlementName, float lingerDays)
        {
            originalSettlementName = settlementName;
            // Match World Raids slider (1–100 whole days). Do not hard-cap at 10.
            int days = Mathf.Clamp(Mathf.RoundToInt(lingerDays), 1, 100);
            expireTick = Find.TickManager.TicksGame + Mathf.Max(1, days * GenDate.TicksPerDay);
        }

        public float DaysRemaining
        {
            get
            {
                if (expireTick < 0 || Find.TickManager == null) return 0f;
                return Mathf.Max(0f, (expireTick - Find.TickManager.TicksGame) / (float)GenDate.TicksPerDay);
            }
        }

        protected override void Tick()
        {
            base.Tick();
            if (expireTick < 0) return;
            if (Find.TickManager.TicksGame < expireTick) return;
            Destroy();
        }

        public override void SpawnSetup()
        {
            base.SpawnSetup();
            if (!liveRuins.Contains(this))
                liveRuins.Add(this);
            Outpost_EstablishmentRequirements.InvalidateNearbyCountCache();
            // Load path + new spawn: observer LOS + explored-tile safety net for the player.
            ApplyFowDiscoveryAfterSpawn(priorKnowers: null);
        }

        public override void Destroy()
        {
            if (!Destroyed)
            {
                liveRuins.RemoveAll(r => r == this);
                WorldComponent_SettlementIntel.Get()?.NotifySiteDestroyed(this);
            }
            base.Destroy();
            Outpost_EstablishmentRequirements.InvalidateNearbyCountCache();
        }

        public override string GetInspectString()
        {
            string baseStr = base.GetInspectString();
            string clearLine = "TSA_WD_SettlementRuin_InspectClearIn".Translate(DaysRemaining.ToString("F1"));
            if (baseStr.NullOrEmpty()) return clearLine;
            return baseStr + "\n" + clearLine;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref originalSettlementName, "originalSettlementName");
            Scribe_Values.Look(ref expireTick, "expireTick", -1);
        }

        /// <summary>Capture knowers while <paramref name="site"/> still exists (before Destroy).</summary>
        public static void CapturePriorKnowers(WorldObject site, List<Faction> into)
        {
            WorldComponent_SettlementIntel.Get()?.CollectFactionsThatKnow(site, into);
        }

        /// <summary>
        /// Spawn a timed ruin on tile. Replaces nothing; caller must already clear the settlement.
        /// Pass <paramref name="priorKnowers"/> captured before Destroy so discovery continues.
        /// </summary>
        public static WorldObject_WdSettlementRuin Spawn(
            int tile,
            string settlementName,
            Faction faction = null,
            List<Faction> priorKnowers = null)
        {
            if (tile < 0 || Find.WorldObjects == null) return null;
            var def = DefDatabase<WorldObjectDef>.GetNamedSilentFail("TSA_WD_SettlementRuin");
            if (def == null)
            {
                Log.Error("[TSA World Domination] Missing WorldObjectDef TSA_WD_SettlementRuin.");
                return null;
            }

            // Avoid stacking ruins on the same tile.
            var all = Find.WorldObjects.AllWorldObjects;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] is WorldObject_WdSettlementRuin existing && !existing.Destroyed && existing.Tile == tile)
                {
                    existing.Configure(settlementName ?? existing.originalSettlementName,
                        WorldDominationMod.settings?.ruinLingerDays ?? WorldDominationSettings.DefRuinLingerDays);
                    if (faction != null) existing.SetFaction(faction);
                    existing.ApplyPriorKnowersOnly(priorKnowers);
                    return existing;
                }
            }

            var ruin = (WorldObject_WdSettlementRuin)WorldObjectMaker.MakeWorldObject(def);
            ruin.Tile = tile;
            if (faction != null) ruin.SetFaction(faction);
            ruin.Configure(
                settlementName,
                WorldDominationMod.settings?.ruinLingerDays ?? WorldDominationSettings.DefRuinLingerDays);
            Find.WorldObjects.Add(ruin);
            // SpawnSetup already ran observer/explored safety net; transfer razed-site knowledge here.
            ruin.ApplyPriorKnowersOnly(priorKnowers);
            return ruin;
        }

        /// <summary>
        /// Capture knowers while site lives, Destroy, then Spawn with transfer.
        /// Use when no extra pre-Destroy work is needed; otherwise Capture + Destroy + Spawn manually.
        /// </summary>
        public static WorldObject_WdSettlementRuin DestroySiteAndSpawnRuin(
            WorldObject dyingSite,
            string settlementName,
            Faction ruinFaction = null)
        {
            if (dyingSite == null || dyingSite.Destroyed) return null;
            tmpKnowers.Clear();
            WorldComponent_SettlementIntel.Get()?.CollectFactionsThatKnow(dyingSite, tmpKnowers);
            int tile = dyingSite.Tile.tileId;
            string name = settlementName;
            if (name.NullOrEmpty())
                name = dyingSite.LabelCap;
            Faction faction = ruinFaction ?? dyingSite.Faction;
            dyingSite.Destroy();
            return Spawn(tile, name, faction, tmpKnowers);
        }

        private void ApplyPriorKnowersOnly(List<Faction> priorKnowers)
        {
            if (priorKnowers == null || priorKnowers.Count == 0) return;
            var intel = WorldComponent_SettlementIntel.Get();
            if (intel == null) return;
            for (int i = 0; i < priorKnowers.Count; i++)
                intel.Discover(priorKnowers[i], this);
            intel.ForceRebuildFowDrawCacheNow();
        }

        private void ApplyFowDiscoveryAfterSpawn(List<Faction> priorKnowers)
        {
            var intel = WorldComponent_SettlementIntel.Get();
            if (intel == null) return;

            if (priorKnowers != null)
            {
                for (int i = 0; i < priorKnowers.Count; i++)
                    intel.Discover(priorKnowers[i], this);
            }

            // Safety net: player already explored this tile (legacy saves / missed transfer).
            Faction player = Faction.OfPlayerSilentFail;
            if (player != null
                && intel.TileFow != null
                && intel.TileFow.IsTileExploredForView(Tile.tileId))
            {
                intel.Discover(player, this);
            }

            intel.NotifySiteAppearedAfterInit(this);
        }
    }
}
