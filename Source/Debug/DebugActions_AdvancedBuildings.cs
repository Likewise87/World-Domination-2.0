using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Dev tools for advanced building projects (click an NPC settlement).</summary>
    public static class DebugActions_AdvancedBuildings
    {
        private static Settlement NpcSettlementAtMouse()
        {
            int tile = GenWorld.MouseTile();
            if (tile < 0) return null;
            Settlement s = Find.WorldObjects.ObjectsAt(tile).OfType<Settlement>().FirstOrDefault();
            if (s == null || s.Faction == null || s.Faction.IsPlayer)
            {
                Messages.Message("WD debug: click an NPC settlement (not player).", MessageTypeDefOf.RejectInput);
                return null;
            }
            return s;
        }

        private static void SpawnType(AdvancedBuildingDef def)
        {
            Settlement s = NpcSettlementAtMouse();
            if (s == null || def == null) return;
            if (WorldComponent_AdvancedBuildings.Get()?.Has(s, def) == true)
            {
                Messages.Message($"WD debug: {s.LabelCap} already owns a {def.LabelText}.", MessageTypeDefOf.RejectInput);
                return;
            }
            if (!WorldActions_AdvancedBuildings.FindBuildTile(s, def, out int tile))
            {
                Messages.Message("WD debug: no buildable tile within range.", MessageTypeDefOf.RejectInput);
                return;
            }
            var b = WorldActions_AdvancedBuildings.Place(def, s, tile);
            Messages.Message(
                b != null ? $"WD debug: placed {def.LabelText} for {s.LabelCap} on tile {tile}." : "WD debug: place failed.",
                b != null ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.RejectInput);
        }


        public static void SpawnSilo() => SpawnType(AdvancedBuildingDefOf.TSA_WD_AdvBuilding_Silo);


        public static void SpawnHospital() => SpawnType(AdvancedBuildingDefOf.TSA_WD_AdvBuilding_Hospital);


        public static void SpawnWatchTower() => SpawnType(AdvancedBuildingDefOf.TSA_WD_AdvBuilding_WatchTower);


        public static void SpawnStable() => SpawnType(AdvancedBuildingDefOf.TSA_WD_AdvBuilding_Stable);


        public static void SpawnMortar() => SpawnType(AdvancedBuildingDefOf.TSA_WD_AdvBuilding_MortarTurret);


        public static void SpawnAa() => SpawnType(AdvancedBuildingDefOf.TSA_WD_AdvBuilding_AaTurret);


        public static void ForceArtilleryRoll()
        {
            Settlement s = NpcSettlementAtMouse();
            var comp = s?.GetComponent<CompViralSpread>();
            if (comp == null) return;
            comp.artilleryRoll = 1;
            Messages.Message($"WD debug: {s.LabelCap} artillery roll set to pass.", MessageTypeDefOf.PositiveEvent);
        }


        public static void ForceAttempt()
        {
            Settlement s = NpcSettlementAtMouse();
            var comp = s?.GetComponent<CompViralSpread>();
            if (comp == null) return;
            comp.advancedBuildCooldownTick = -1;
            bool ok = WorldActions_AdvancedBuildings.Attempt(
                s, comp, Find.World?.GetComponent<WorldComponent_SpreadManager>(), isRecovering: false);
            Messages.Message(
                ok ? $"WD debug: {s.LabelCap} launched builders." : $"WD debug: {s.LabelCap} could not start a project (slots, pool, tile, or strength).",
                ok ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.RejectInput);
        }


        public static void RerunLegacyGrant()
        {
            WorldActions_AdvancedBuildings.RunLegacyArtilleryGrant();
            Messages.Message("WD debug: legacy artillery grant ran (see log).", MessageTypeDefOf.NeutralEvent);
        }
    }
}
