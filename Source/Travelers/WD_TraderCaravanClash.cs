using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Spawns a vanilla-style trader caravan (pawn group Trader + random faction caravanTraderKinds + stock),
    /// then assigns LordJob_AssaultColony so the hostile traders engage the player immediately.
    /// Keeps Trader pawn-group generation for composition and loot.
    /// </summary>
    public static class WD_TraderCaravanClash
    {
        private const int SpawnRadius = 8;

        /// <summary>
        /// Spawns a hostile vanilla-style trader caravan when the faction has <see cref="FactionDef.caravanTraderKinds"/>.
        /// Returns false if the faction cannot generate Trader pawn groups (caller should run a normal raid instead).
        /// </summary>
        public static bool SpawnTraderClashForces(Map map, WorldObject_Traveler traveler)
        {
            if (map == null || traveler?.Faction == null) return false;

            Faction faction = traveler.Faction;
            if (faction.def.caravanTraderKinds == null || faction.def.caravanTraderKinds.Count == 0)
            {
                Log.Message($"[TSA WD] Trader clash: faction {faction.Name} has no caravanTraderKinds; use raid fallback.");
                return false;
            }
            IntVec3 playerStart = IntVec3.Invalid;
            var spawned = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < spawned.Count; i++)
            {
                if (spawned[i].Faction != null && spawned[i].Faction.IsPlayer)
                { playerStart = spawned[i].Position; break; }
            }
            if (!playerStart.IsValid) playerStart = map.Center;

            if (!CellFinder.TryFindRandomEdgeCellWith(c => c.Standable(map) && (c - playerStart).LengthHorizontal > 30f, map, CellFinder.EdgeRoadChance_Neutral, out IntVec3 entryCell))
                CellFinder.TryFindRandomEdgeCellWith(c => c.Standable(map), map, CellFinder.EdgeRoadChance_Neutral, out entryCell);

            IncidentParms incidentParms = new IncidentParms
            {
                target = map,
                faction = faction,
                forced = true,
                spawnCenter = entryCell,
                points = TraderCaravanUtility.GenerateGuardPoints()
            };

            incidentParms.traderKind = faction.def.caravanTraderKinds.RandomElement();

            PawnGroupMakerParms pgmParms = IncidentParmsUtility.GetDefaultPawnGroupMakerParms(PawnGroupKindDefOf.Trader, incidentParms, true);
            var generated = PawnGroupMakerUtility.GeneratePawns(pgmParms);
            var list = new List<Pawn>();
            foreach (var p in generated) list.Add(p);
            if (list.Count == 0)
            {
                Log.Warning($"[TSA WD] Trader clash: PawnGroupMakerUtility returned no pawns for faction {faction.Name}; use raid fallback.");
                return false;
            }

            foreach (Pawn p in list)
            {
                if (p.needs?.food != null)
                    p.needs.food.CurLevel = p.needs.food.MaxLevel;
                IntVec3 loc = CellFinder.RandomClosewalkCellNear(entryCell, map, SpawnRadius);
                GenSpawn.Spawn(p, loc, map, WipeMode.Vanish);
            }

            // Remove any pre-existing lord on these pawns (e.g. from pawn generation internals).
            HashSet<Lord> lordsToRemove = null;
            for (int i = 0; i < list.Count; i++)
            {
                Lord existing = list[i].GetLord();
                if (existing != null)
                {
                    if (lordsToRemove == null) lordsToRemove = new HashSet<Lord>();
                    lordsToRemove.Add(existing);
                }
            }
            if (lordsToRemove != null)
            {
                foreach (Lord old in lordsToRemove)
                    old.lordManager.RemoveLord(old);
            }

            LordMaker.MakeNewLord(faction, new LordJob_AssaultColony(faction), map, list);
            return true;
        }
    }
}
