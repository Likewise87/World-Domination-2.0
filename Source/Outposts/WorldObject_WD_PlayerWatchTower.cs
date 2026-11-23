using System.Collections.Generic;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Player-built Watch Tower: FoW observer (Awareness 10). AT-style field structure — not an NPC advanced building.
    /// Shared AdvWO_Watchtower expanding art; smashed by hostile walk-over.
    /// </summary>
    [StaticConstructorOnStartup]
    public class WorldObject_WD_PlayerWatchTower : WorldObject
    {
        public const string DefName = "TSA_WD_PlayerWatchTower";
        public const string ExpandingIconPath = "WorldObjects/AdvancedBuildings/AdvWO_Watchtower";

        /// <summary>Outpost or colony that ordered the build (soft ref; does not tear down this tower).</summary>
        public WorldObject builtBySite;

        private static readonly List<WorldObject_WD_PlayerWatchTower> liveTowers = new List<WorldObject_WD_PlayerWatchTower>();
        private Material cachedMaterial;
        private string cachedInspectString;
        private int cachedInspectTick = -999;

        public static IReadOnlyList<WorldObject_WD_PlayerWatchTower> LiveTowers => liveTowers;

        public static void RebuildLiveRegistry()
        {
            liveTowers.Clear();
            WorldObjectsHolder worldObjects = Find.WorldObjects;
            if (worldObjects == null) return;
            List<WorldObject> all = worldObjects.AllWorldObjects;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] is WorldObject_WD_PlayerWatchTower t && !t.Destroyed)
                    liveTowers.Add(t);
            }
        }

        public static WorldObject_WD_PlayerWatchTower FindAt(int tileId)
        {
            if (tileId < 0) return null;
            IReadOnlyList<WorldObject_WD_PlayerWatchTower> list = liveTowers;
            for (int i = 0; i < list.Count; i++)
            {
                WorldObject_WD_PlayerWatchTower t = list[i];
                if (t != null && !t.Destroyed && t.Tile.tileId == tileId)
                    return t;
            }
            return null;
        }

        public override void SpawnSetup()
        {
            base.SpawnSetup();
            if (!liveTowers.Contains(this))
                liveTowers.Add(this);
            WorldComponent_SettlementIntel.Get()?.NotifySiteAppeared(this);
        }

        public override void Destroy()
        {
            if (!Destroyed)
            {
                liveTowers.RemoveAll(t => t == this);
                WorldComponent_SettlementIntel.Get()?.NotifySiteDestroyed(this);
            }
            base.Destroy();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref builtBySite, "builtBySite");
        }

        public override Material Material
        {
            get
            {
                if (cachedMaterial != null) return cachedMaterial;
                string path = Faction?.def?.settlementTexturePath;
                if (string.IsNullOrEmpty(path))
                    path = "World/WorldObjects/Settlements/Settlement";
                cachedMaterial = MaterialPool.MatFrom(
                    path,
                    ShaderDatabase.WorldOverlayTransparentLit,
                    Faction?.Color ?? Color.white,
                    WorldMaterials.WorldObjectRenderQueue);
                return cachedMaterial;
            }
        }

        public override Texture2D ExpandingIcon
        {
            get
            {
                Texture2D tex = ContentFinder<Texture2D>.Get(ExpandingIconPath, false);
                return tex ?? base.ExpandingIcon;
            }
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo g in base.GetGizmos())
                yield return g;
            if (Faction?.IsPlayer != true) yield break;
            foreach (Gizmo g in RadiusHoverGizmos.GetSightFor(this, WD_RadiusOverlayCategory.WdTraveler))
                yield return g;
        }

        public override string GetInspectString()
        {
            int tick = Find.TickManager?.TicksGame ?? 0;
            if (cachedInspectString != null && tick == cachedInspectTick)
                return cachedInspectString;

            var sb = new StringBuilder();
            string bas = base.GetInspectString();
            if (!bas.NullOrEmpty())
                sb.AppendLine(bas);
            float awareness = WorldComponent_SettlementIntel.GetAwarenessRadius(this);
            float los = WorldComponent_SettlementIntel.GetLosRadius(this);
            sb.AppendLine("TSA_WD_PlayerWatchTower_Inspect_Los".Translate(los.ToString("F0"), awareness.ToString("F0")).ToString());
            if (builtBySite != null && !builtBySite.Destroyed)
                sb.AppendLine("TSA_WD_PlayerWatchTower_Inspect_BuiltBy".Translate(builtBySite.LabelCap).ToString());

            cachedInspectString = sb.ToString().TrimEnd();
            cachedInspectTick = tick;
            return cachedInspectString;
        }

        public static WorldObject_WD_PlayerWatchTower TrySpawn(int tileId, Faction faction, WorldObject builtBySite)
        {
            if (faction == null || tileId < 0 || Find.WorldGrid == null || !Find.WorldGrid.InBounds(tileId))
                return null;
            // Base eligibility only. Do not use IsValidBuildTile here: the active project still
            // lists this tile and would lock itself (same pattern as AT turret TrySpawn).
            if (!WorldActions_WatchTowers.IsBaseEligibleBuildTile(tileId))
                return null;

            WorldObjectDef def = DefDatabase<WorldObjectDef>.GetNamedSilentFail(DefName);
            if (def == null)
            {
                Log.Warning($"[TSA WD] Missing WorldObjectDef {DefName}.");
                return null;
            }

            var tower = (WorldObject_WD_PlayerWatchTower)WorldObjectMaker.MakeWorldObject(def);
            tower.Tile = new PlanetTile(tileId, Find.WorldGrid[tileId].Layer);
            tower.SetFaction(faction);
            tower.builtBySite = builtBySite;
            Find.WorldObjects.Add(tower);
            return tower;
        }
    }
}
