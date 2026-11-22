using RimWorld;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Player-home colony map only (not camps). Clear standable ground; many allowed per map.
    /// </summary>
    public class PlaceWorker_WdColonyHomeOnly : PlaceWorker
    {
        public override AcceptanceReport AllowsPlacing(BuildableDef checkingDef, IntVec3 loc, Rot4 rot, Map map, Thing? thingToIgnore = null, Thing? thing = null)
        {
            if (!(checkingDef is ThingDef def) || map == null)
                return true;

            if (!map.IsPlayerHome || map.ParentFaction != Faction.OfPlayer
                || Outpost_EstablishmentRequirements.IsActiveCamp(map.Parent))
                return new AcceptanceReport("TSA_WD_RemotePowerSpot_NeedColonyHome".Translate());

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
    }
}
