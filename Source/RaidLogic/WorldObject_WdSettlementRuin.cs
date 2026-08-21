using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Temporary scar after a settlement is razed. Blocks establishment / expand / conqueror founding
    /// with the same min-distance radius as settlements and WD outposts until it despawns.
    /// </summary>
    [StaticConstructorOnStartup]
    public class WorldObject_WdSettlementRuin : WorldObject
    {
        private static readonly Material CachedMaterial =
            MaterialPool.MatFrom("World/WorldObjects/TribalSettlement", ShaderDatabase.WorldOverlayTransparentLit, WorldMaterials.WorldObjectRenderQueue);

        private string originalSettlementName;
        private int expireTick = -1;

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

        public void Configure(string settlementName, float lingerDays)
        {
            originalSettlementName = settlementName;
            float days = Mathf.Clamp(lingerDays, 5f, 10f);
            expireTick = Find.TickManager.TicksGame + Mathf.Max(1, Mathf.RoundToInt(days * GenDate.TicksPerDay));
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
            Outpost_EstablishmentRequirements.InvalidateNearbyCountCache();
        }

        public override void Destroy()
        {
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

        /// <summary>Spawn a timed ruin on tile. Replaces nothing; caller must already clear the settlement.</summary>
        public static WorldObject_WdSettlementRuin Spawn(int tile, string settlementName, Faction faction = null)
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
            return ruin;
        }
    }
}
