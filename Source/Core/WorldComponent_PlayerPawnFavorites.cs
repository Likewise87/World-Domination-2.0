using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Persists starred (favorite) player pawn ThingIDs for All Player Pawns / outpost Pawns / remote establish.
    /// Stars survive transfers and vehicle boarding. Auto-prune only removes confirmed dead/destroyed pawns,
    /// or IDs that stay completely unresolved for a long grace period (orphans), never brief transit gaps.
    /// </summary>
    public class WorldComponent_PlayerPawnFavorites : WorldComponent
    {
        private HashSet<string> starredThingIds = new HashSet<string>();
        /// <summary>ThingID → tick when first observed missing from all live locations (orphan grace).</summary>
        private Dictionary<string, int> missingSinceTick = new Dictionary<string, int>();

        private int lastPruneTick = -99999;
        private const int PruneIntervalTicks = 2500;
        /// <summary>Only purge unresolved IDs after this long (vehicle/transfer gaps are far shorter).</summary>
        private const int OrphanGraceTicks = 60000 * 15; // 15 in-game days

        private static WorldComponent_PlayerPawnFavorites? cached;
        private static readonly List<Pawn> vehicleAboardScratch = new List<Pawn>(16);
        private static readonly HashSet<Pawn> vehicleSeenScratch = new HashSet<Pawn>();

        public WorldComponent_PlayerPawnFavorites(World world) : base(world)
        {
            cached = this;
        }

        public static WorldComponent_PlayerPawnFavorites? Get()
        {
            if (cached != null && cached.world == Find.World) return cached;
            cached = Find.World?.GetComponent<WorldComponent_PlayerPawnFavorites>();
            return cached;
        }

        public override void WorldComponentTick()
        {
            int now = Find.TickManager.TicksGame;
            if (now - lastPruneTick < PruneIntervalTicks) return;
            lastPruneTick = now;
            PruneGone();
        }

        public bool IsStarred(string thingId)
        {
            return !thingId.NullOrEmpty() && starredThingIds.Contains(thingId);
        }

        public bool IsStarred(Pawn pawn)
        {
            return pawn != null && IsStarred(pawn.ThingID);
        }

        public void SetStarred(string thingId, bool starred)
        {
            if (thingId.NullOrEmpty()) return;
            if (starred)
            {
                starredThingIds.Add(thingId);
                missingSinceTick.Remove(thingId);
            }
            else
            {
                starredThingIds.Remove(thingId);
                missingSinceTick.Remove(thingId);
            }
        }

        public void Toggle(string thingId)
        {
            if (thingId.NullOrEmpty()) return;
            if (!starredThingIds.Add(thingId))
            {
                starredThingIds.Remove(thingId);
                missingSinceTick.Remove(thingId);
            }
            else
                missingSinceTick.Remove(thingId);
        }

        /// <summary>Keep the star when a pawn's ThingID changes (rare).</summary>
        public void RemapThingId(string oldThingId, string newThingId)
        {
            if (oldThingId.NullOrEmpty() || newThingId.NullOrEmpty()) return;
            if (oldThingId == newThingId) return;
            if (!starredThingIds.Remove(oldThingId)) return;
            missingSinceTick.Remove(oldThingId);
            starredThingIds.Add(newThingId);
            missingSinceTick.Remove(newThingId);
        }

        /// <summary>
        /// Drop stars for confirmed dead/destroyed pawns, empty keys, and long-unresolved orphan IDs.
        /// Do not drop merely because a live pawn is briefly off scanned lists (vehicles / transfers).
        /// </summary>
        public void PruneGone()
        {
            if (starredThingIds.Count == 0)
            {
                missingSinceTick.Clear();
                return;
            }

            int now = Find.TickManager?.TicksGame ?? 0;
            var drop = new List<string>();
            foreach (string id in starredThingIds)
            {
                if (id.NullOrEmpty())
                {
                    drop.Add(id);
                    continue;
                }

                Pawn pawn = FindPawnByThingId(id);
                if (pawn != null)
                {
                    missingSinceTick.Remove(id);
                    if (pawn.Dead || pawn.Destroyed)
                        drop.Add(id);
                    continue;
                }

                // Not found live or dead: may be in an unscanned transient holder. Start orphan grace.
                if (!missingSinceTick.TryGetValue(id, out int since))
                {
                    missingSinceTick[id] = now;
                    continue;
                }
                if (now - since >= OrphanGraceTicks)
                    drop.Add(id);
            }

            for (int i = 0; i < drop.Count; i++)
            {
                string id = drop[i];
                starredThingIds.Remove(id);
                missingSinceTick.Remove(id);
            }

            // Drop grace entries for IDs no longer starred.
            if (missingSinceTick.Count == 0) return;
            var staleGrace = new List<string>();
            foreach (var kv in missingSinceTick)
            {
                if (!starredThingIds.Contains(kv.Key))
                    staleGrace.Add(kv.Key);
            }
            for (int i = 0; i < staleGrace.Count; i++)
                missingSinceTick.Remove(staleGrace[i]);
        }

        /// <summary>Live holders first, then world dead pawns (for confirmed-dead prune).</summary>
        private static Pawn FindPawnByThingId(string thingId)
        {
            if (thingId.NullOrEmpty()) return null;

            var maps = Find.Maps;
            if (maps != null)
            {
                for (int i = 0; i < maps.Count; i++)
                {
                    var list = maps[i]?.mapPawns?.AllPawns;
                    if (list == null) continue;
                    for (int p = 0; p < list.Count; p++)
                    {
                        Pawn pawn = list[p];
                        if (pawn != null && pawn.ThingID == thingId)
                            return pawn;
                    }
                }
            }

            var allWo = Find.WorldObjects?.AllWorldObjects;
            if (allWo != null)
            {
                for (int i = 0; i < allWo.Count; i++)
                {
                    WorldObject wo = allWo[i];
                    if (wo is Caravan caravan)
                    {
                        Pawn found = FindInPawnList(caravan.PawnsListForReading, thingId);
                        if (found != null) return found;
                        found = FindAboardVehiclesOnCaravan(caravan, thingId);
                        if (found != null) return found;
                    }
                    else if (wo is WorldObject_Traveler_RapidResponseDropPod dropPod)
                    {
                        Pawn found = FindInPawnList(dropPod.carriedPawns, thingId);
                        if (found != null) return found;
                    }
                    else if (wo is WorldObject_WD_Outpost outpost)
                    {
                        Pawn found = FindInPawnList(outpost.Occupants, thingId);
                        if (found != null) return found;
                        found = FindInPawnList(outpost.Prisoners, thingId);
                        if (found != null) return found;
                        found = FindInPawnList(outpost.StoredAnimalsAndVehicles, thingId);
                        if (found != null) return found;
                        found = FindInPawnList(outpost.StoredMechanoids, thingId);
                        if (found != null) return found;
                        found = FindAboardVehiclesInList(outpost.StoredAnimalsAndVehicles, thingId);
                        if (found != null) return found;
                    }
                }
            }

            var worldAlive = Find.WorldPawns?.AllPawnsAlive;
            if (worldAlive != null)
            {
                for (int i = 0; i < worldAlive.Count; i++)
                {
                    Pawn pawn = worldAlive[i];
                    if (pawn != null && pawn.ThingID == thingId)
                        return pawn;
                }
            }

            var worldDead = Find.WorldPawns?.AllPawnsDead;
            if (worldDead != null)
            {
                foreach (Pawn pawn in worldDead)
                {
                    if (pawn != null && pawn.ThingID == thingId)
                        return pawn;
                }
            }

            return null;
        }

        private static Pawn FindInPawnList(List<Pawn> list, string thingId)
        {
            if (list == null) return null;
            for (int i = 0; i < list.Count; i++)
            {
                Pawn pawn = list[i];
                if (pawn != null && pawn.ThingID == thingId)
                    return pawn;
            }
            return null;
        }

        private static Pawn FindAboardVehiclesOnCaravan(Caravan caravan, string thingId)
        {
            if (caravan?.PawnsListForReading == null) return null;
            Pawn found = FindAboardVehiclesInList(caravan.PawnsListForReading, thingId);
            if (found != null) return found;

            // VehicleCaravan may keep VehiclePawns only on VehiclesListForReading.
            try
            {
                PropertyInfo prop = caravan.GetType().GetProperty("VehiclesListForReading", BindingFlags.Public | BindingFlags.Instance);
                object raw = prop?.GetValue(caravan);
                if (raw is IEnumerable enumerable)
                {
                    foreach (object o in enumerable)
                    {
                        if (o is Pawn vp)
                        {
                            found = FindAboardOneVehicle(vp, thingId);
                            if (found != null) return found;
                        }
                    }
                }
            }
            catch
            {
                // Soft VF compat only.
            }
            return null;
        }

        private static Pawn FindAboardVehiclesInList(List<Pawn> list, string thingId)
        {
            if (list == null) return null;
            for (int i = 0; i < list.Count; i++)
            {
                Pawn pawn = list[i];
                if (pawn == null || !VehicleFrameworkOutpostDissolveCompat.IsVehicleFrameworkVehiclePawn(pawn))
                    continue;
                Pawn found = FindAboardOneVehicle(pawn, thingId);
                if (found != null) return found;
            }
            return null;
        }

        private static Pawn FindAboardOneVehicle(Pawn vehiclePawn, string thingId)
        {
            if (vehiclePawn == null || vehiclePawn.Destroyed) return null;
            vehicleAboardScratch.Clear();
            vehicleSeenScratch.Clear();
            try
            {
                PropertyInfo aboardProp = vehiclePawn.GetType().GetProperty("AllPawnsAboard", BindingFlags.Public | BindingFlags.Instance);
                object raw = aboardProp?.GetValue(vehiclePawn);
                if (raw is IEnumerable enumerable)
                {
                    foreach (object o in enumerable)
                    {
                        if (o is Pawn ab && !ab.Destroyed && vehicleSeenScratch.Add(ab))
                            vehicleAboardScratch.Add(ab);
                    }
                }
            }
            catch
            {
                return null;
            }

            for (int i = 0; i < vehicleAboardScratch.Count; i++)
            {
                if (vehicleAboardScratch[i].ThingID == thingId)
                    return vehicleAboardScratch[i];
            }
            return null;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref starredThingIds, "starredThingIds", LookMode.Value);
            Scribe_Collections.Look(ref missingSinceTick, "starMissingSinceTick", LookMode.Value, LookMode.Value);
            if (starredThingIds == null)
                starredThingIds = new HashSet<string>();
            if (missingSinceTick == null)
                missingSinceTick = new Dictionary<string, int>();
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                starredThingIds.RemoveWhere(string.IsNullOrEmpty);
                // Fresh grace after load so transfer mid-save does not instantly orphan.
                missingSinceTick.Clear();
            }
        }
    }
}
