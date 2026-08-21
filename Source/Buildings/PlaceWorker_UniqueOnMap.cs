using RimWorld;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Rejects placement when the map already has a built instance, blueprint, or frame of the same def. Enforces one-per-map buildings (e.g. the outpost delivery spot).</summary>
    public class PlaceWorker_UniqueOnMap : PlaceWorker
    {
        public override AcceptanceReport AllowsPlacing(BuildableDef checkingDef, IntVec3 loc, Rot4 rot, Map map, Thing? thingToIgnore = null, Thing? thing = null)
        {
            if (!(checkingDef is ThingDef def) || map == null)
                return true;

            if (HasAny(map, def) || HasAny(map, def.blueprintDef) || HasAny(map, def.frameDef))
                return new AcceptanceReport("TSA_WD_DeliverySpot_AlreadyExists".Translate());

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
