using RimWorld;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// One-per-map plus clear standable ground (no edifices / impassable / unsupported terrain).
    /// Used by the outpost delivery spot so InstantBuild (WorkToBuild 0) still rejects walls and water.
    /// </summary>
    public class PlaceWorker_UniqueOnMap : PlaceWorker
    {
        public override AcceptanceReport AllowsPlacing(BuildableDef checkingDef, IntVec3 loc, Rot4 rot, Map map, Thing? thingToIgnore = null, Thing? thing = null)
        {
            if (!(checkingDef is ThingDef def) || map == null)
                return true;

            if (HasAny(map, def) || HasAny(map, def.blueprintDef) || HasAny(map, def.frameDef))
                return new AcceptanceReport("TSA_WD_DeliverySpot_AlreadyExists".Translate());

            TerrainAffordanceDef need = def.terrainAffordanceNeeded;
            CellRect rect = GenAdj.OccupiedRect(loc, rot, def.Size);
            foreach (IntVec3 c in rect)
            {
                if (!c.InBounds(map))
                    return new AcceptanceReport("OutOfBounds".Translate());

                TerrainDef terrain = c.GetTerrain(map);
                if (terrain == null || terrain.passability == Traversability.Impassable)
                    return new AcceptanceReport("TerrainCannotSupport".Translate(checkingDef));

                if (need != null && !terrain.affordances.Contains(need))
                    return new AcceptanceReport("TerrainCannotSupport_TerrainAffordance".Translate(checkingDef, need));

                if (!c.Standable(map))
                    return new AcceptanceReport("SpaceAlreadyOccupied".Translate());

                Building edifice = c.GetEdifice(map);
                if (edifice != null && edifice != thingToIgnore)
                    return new AcceptanceReport("SpaceAlreadyOccupied".Translate());
            }

            return true;
        }

        private static bool HasAny(Map map, ThingDef? def)
        {
            if (def == null) return false;
            var list = map.listerThings.ThingsOfDef(def);
            return list != null && list.Count > 0;
        }
    }
}
