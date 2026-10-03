using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Temporary map parent for player drop pods shot down by anti-air.</summary>
    public class WorldObject_WD_DropPodCrashSite : MapParent
    {
        /// <summary>Hostile settlement used for reinforcement ally / timer math (not this site).</summary>
        public Settlement reinforcementProxy;

        public override bool ShouldRemoveMapNow(out bool alsoRemoveWorldObject)
        {
            alsoRemoveWorldObject = true;
            if (!HasMap) return true;
            var pawns = Map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                if (p == null || p.Dead || p.Destroyed) continue;
                if (p.Faction != null && p.Faction.IsPlayer)
                    return false;
            }
            // Keep map briefly if hostiles still present so corpses/loot remain reachable via reform.
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                if (p == null || p.Dead || p.Destroyed) continue;
                if (p.Faction != null && !p.Faction.IsPlayer && WorldActions_Utils.SafeHostileTo(p.Faction, Faction.OfPlayer))
                    return false;
            }
            return true;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref reinforcementProxy, "reinforcementProxy");
        }
    }
}
