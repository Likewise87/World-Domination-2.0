using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Vanilla <see cref="CaravansBattlefield.CheckWonBattle"/> records
    /// <see cref="TaleDefOf.CaravanAmbushDefeated"/> from
    /// <c>Map.mapPawns.FreeColonists.RandomElementWithFallback()</c>.
    /// Vehicle Framework keeps crew aboard the vehicle, so FreeColonists is empty and the tale NRE's.
    /// Substitute a humanlike player pawn (including VF crew) or skip the tale.
    /// </summary>
    [HarmonyPatch(typeof(TaleRecorder), nameof(TaleRecorder.RecordTale))]
    public static class Patch_CaravansBattlefieldAmbushTale
    {
        private static readonly List<Pawn> aboardScratch = new List<Pawn>();
        private static readonly HashSet<Pawn> aboardSeen = new HashSet<Pawn>();

        public static bool Prefix(TaleDef def, ref object[] args)
        {
            if (def != TaleDefOf.CaravanAmbushDefeated) return true;

            Pawn pawn = args != null && args.Length > 0 ? args[0] as Pawn : null;
            if (IsUsableTalePawn(pawn)) return true;

            pawn = FindAmbushTalePawn();
            if (pawn == null) return false;

            args = new object[] { pawn };
            return true;
        }

        private static Pawn FindAmbushTalePawn()
        {
            Map map = Find.CurrentMap;
            if (map?.Parent != null && map.Parent.def == WorldObjectDefOf.Ambush)
            {
                Pawn fromMap = FindOnMap(map);
                if (fromMap != null) return fromMap;
            }

            var maps = Current.Game?.Maps;
            if (maps != null)
            {
                for (int i = 0; i < maps.Count; i++)
                {
                    Map m = maps[i];
                    if (m?.Parent == null || m.Parent.Destroyed) continue;
                    if (m.Parent.def != WorldObjectDefOf.Ambush) continue;
                    Pawn fromAmbush = FindOnMap(m);
                    if (fromAmbush != null) return fromAmbush;
                }
            }

            return null;
        }

        private static Pawn FindOnMap(Map map)
        {
            if (map?.mapPawns == null) return null;

            Pawn best = FirstUsable(map.mapPawns.FreeColonistsSpawned);
            if (best != null) return best;

            best = FirstUsable(map.mapPawns.FreeColonists);
            if (best != null) return best;

            var spawned = map.mapPawns.AllPawnsSpawned;
            if (spawned != null)
            {
                best = FirstUsable(spawned, requireSpawnedHumanlike: true, includeVehicleCrew: false);
                if (best != null) return best;

                best = FirstVehicleCrew(spawned);
                if (best != null) return best;
            }

            return null;
        }

        private static Pawn FirstVehicleCrew(IReadOnlyList<Pawn> spawned)
        {
            if (spawned == null) return null;
            for (int i = 0; i < spawned.Count; i++)
            {
                Pawn vehicle = spawned[i];
                if (!VehicleFrameworkOutpostDissolveCompat.IsVehicleFrameworkVehiclePawn(vehicle))
                    continue;
                if (vehicle.Faction == null || !vehicle.Faction.IsPlayer) continue;

                aboardScratch.Clear();
                aboardSeen.Clear();
                VehicleFrameworkOutpostDissolveCompat.CollectPawnsAboardVehicleForRoster(
                    vehicle, aboardScratch, aboardSeen);
                Pawn crew = FirstUsable(aboardScratch);
                if (crew != null) return crew;
            }

            return null;
        }

        private static Pawn FirstUsable(IReadOnlyList<Pawn> pawns, bool requireSpawnedHumanlike = false, bool includeVehicleCrew = true)
        {
            if (pawns == null) return null;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                if (!IsUsableTalePawn(p)) continue;
                if (requireSpawnedHumanlike && !p.Spawned) continue;
                if (!includeVehicleCrew && VehicleFrameworkOutpostDissolveCompat.IsVehicleFrameworkVehiclePawn(p))
                    continue;
                return p;
            }

            return null;
        }

        private static bool IsUsableTalePawn(Pawn pawn)
        {
            if (pawn == null || pawn.Destroyed || pawn.Dead) return false;
            if (VehicleFrameworkOutpostDissolveCompat.IsVehicleFrameworkVehiclePawn(pawn))
                return false;
            if (pawn.RaceProps == null || !pawn.RaceProps.Humanlike) return false;
            if (pawn.Faction == null || !pawn.Faction.IsPlayer) return false;
            return true;
        }
    }
}
