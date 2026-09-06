using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// First-join ticks for player-faction pawns (Join Stamp). Calendar days = TicksGame - joinTick.
    /// Survives colony/outpost transfers. First stamp wins; never overwritten on leave/rejoin.
    /// </summary>
    public class WorldComponent_PlayerPawnJoinTimes : WorldComponent
    {
        private Dictionary<string, int> joinTickByThingId = new Dictionary<string, int>();
        private Dictionary<string, int> missingSinceTick = new Dictionary<string, int>();

        private int lastDailyTick = -99999;
        private const int DailyIntervalTicks = GenDate.TicksPerDay;
        private const int OrphanGraceTicks = GenDate.TicksPerDay * 15;

        private static WorldComponent_PlayerPawnJoinTimes cached;
        private static readonly List<Pawn> livePlayerScratch = new List<Pawn>(128);
        private static readonly HashSet<string> liveIdScratch = new HashSet<string>();
        private static readonly List<string> dropScratch = new List<string>();

        public WorldComponent_PlayerPawnJoinTimes(World world) : base(world)
        {
            cached = this;
        }

        public static WorldComponent_PlayerPawnJoinTimes Get()
        {
            if (cached != null && cached.world == Find.World) return cached;
            cached = Find.World?.GetComponent<WorldComponent_PlayerPawnJoinTimes>();
            return cached;
        }

        public override void WorldComponentTick()
        {
            if (Current.ProgramState != ProgramState.Playing) return;
            int now = Find.TickManager.TicksGame;
            if (now - lastDailyTick < DailyIntervalTicks) return;
            lastDailyTick = now;
            BackfillMissing();
            PruneGone();
        }

        /// <summary>Stamp first join only. Skips VF vehicles and empty IDs.</summary>
        public void NoteJoinedPlayerFaction(Pawn pawn)
        {
            if (pawn == null || pawn.Destroyed || pawn.Dead) return;
            if (VehicleFrameworkOutpostDissolveCompat.IsVehicleFrameworkVehiclePawn(pawn)) return;
            if (pawn.Faction?.IsPlayer != true) return;
            string id = pawn.ThingID;
            if (id.NullOrEmpty()) return;
            if (joinTickByThingId.ContainsKey(id)) return;
            joinTickByThingId[id] = Find.TickManager?.TicksGame ?? 0;
            missingSinceTick.Remove(id);
        }

        public bool TryGetJoinTick(Pawn pawn, out int joinTick)
        {
            joinTick = 0;
            if (pawn == null) return false;
            string id = pawn.ThingID;
            if (id.NullOrEmpty()) return false;
            return joinTickByThingId.TryGetValue(id, out joinTick);
        }

        /// <summary>Whole in-game days since join stamp. Returns -1 if unknown (should be rare after backfill).</summary>
        public int GetDaysSinceJoin(Pawn pawn)
        {
            if (pawn == null || pawn.Destroyed) return -1;
            if (VehicleFrameworkOutpostDissolveCompat.IsVehicleFrameworkVehiclePawn(pawn)) return -1;
            EnsureStampFor(pawn);
            if (!TryGetJoinTick(pawn, out int joinTick)) return -1;
            int now = Find.TickManager?.TicksGame ?? 0;
            return Mathf.Max(0, (now - joinTick) / GenDate.TicksPerDay);
        }

        public void RemapThingId(string oldThingId, string newThingId)
        {
            if (oldThingId.NullOrEmpty() || newThingId.NullOrEmpty()) return;
            if (oldThingId == newThingId) return;
            if (!joinTickByThingId.TryGetValue(oldThingId, out int tick)) return;
            joinTickByThingId.Remove(oldThingId);
            missingSinceTick.Remove(oldThingId);
            if (!joinTickByThingId.ContainsKey(newThingId))
                joinTickByThingId[newThingId] = tick;
            missingSinceTick.Remove(newThingId);
        }

        /// <summary>Backfill or return existing days. Safe to call from roster builds.</summary>
        public void EnsureStampFor(Pawn pawn)
        {
            if (pawn == null || pawn.Destroyed || pawn.Dead) return;
            if (VehicleFrameworkOutpostDissolveCompat.IsVehicleFrameworkVehiclePawn(pawn)) return;
            if (pawn.Faction?.IsPlayer != true) return;
            string id = pawn.ThingID;
            if (id.NullOrEmpty()) return;
            if (joinTickByThingId.ContainsKey(id)) return;
            joinTickByThingId[id] = EstimateJoinTick(pawn);
            missingSinceTick.Remove(id);
        }

        private static int EstimateJoinTick(Pawn pawn)
        {
            int now = Find.TickManager?.TicksGame ?? 0;
            float served = 0f;
            if (pawn.records != null)
                served = pawn.records.GetValue(RecordDefOf.TimeAsColonistOrColonyAnimal);
            int servedTicks = Mathf.Max(0, Mathf.RoundToInt(served));
            if (servedTicks > 0)
                return Mathf.Max(0, now - servedTicks);
            // Frozen 0 / unknown: treat as long-term so Join stamp < 5 is not flooded.
            return 0;
        }

        public void BackfillMissing()
        {
            CollectLivePlayerPawns(livePlayerScratch);
            for (int i = 0; i < livePlayerScratch.Count; i++)
                EnsureStampFor(livePlayerScratch[i]);
        }

        public void PruneGone()
        {
            if (joinTickByThingId.Count == 0)
            {
                missingSinceTick.Clear();
                return;
            }

            int now = Find.TickManager?.TicksGame ?? 0;
            liveIdScratch.Clear();
            CollectLivePlayerPawns(livePlayerScratch);
            for (int i = 0; i < livePlayerScratch.Count; i++)
            {
                Pawn p = livePlayerScratch[i];
                if (p?.ThingID != null)
                    liveIdScratch.Add(p.ThingID);
            }

            dropScratch.Clear();
            foreach (var kv in joinTickByThingId)
            {
                string id = kv.Key;
                if (id.NullOrEmpty())
                {
                    dropScratch.Add(id);
                    continue;
                }

                if (liveIdScratch.Contains(id))
                {
                    missingSinceTick.Remove(id);
                    continue;
                }

                Pawn found = FindDeadByThingId(id);
                if (found != null)
                {
                    missingSinceTick.Remove(id);
                    if (found.Dead || found.Destroyed)
                        dropScratch.Add(id);
                    continue;
                }

                if (!missingSinceTick.TryGetValue(id, out int since))
                {
                    missingSinceTick[id] = now;
                    continue;
                }
                if (now - since >= OrphanGraceTicks)
                    dropScratch.Add(id);
            }

            for (int i = 0; i < dropScratch.Count; i++)
            {
                string id = dropScratch[i];
                joinTickByThingId.Remove(id);
                missingSinceTick.Remove(id);
            }
        }

        private static Pawn FindDeadByThingId(string thingId)
        {
            var worldDead = Find.WorldPawns?.AllPawnsDead;
            if (worldDead == null) return null;
            foreach (Pawn pawn in worldDead)
            {
                if (pawn != null && pawn.ThingID == thingId)
                    return pawn;
            }
            return null;
        }

        private static void CollectLivePlayerPawns(List<Pawn> dest)
        {
            dest.Clear();
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
                        if (IsLivePlayerPawn(pawn))
                            dest.Add(pawn);
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
                        AddFromList(dest, caravan.PawnsListForReading);
                    else if (wo is WorldObject_Traveler_RapidResponseDropPod dropPod)
                        AddFromList(dest, dropPod.carriedPawns);
                    else if (wo is WorldObject_WD_Outpost outpost)
                    {
                        AddFromList(dest, outpost.Occupants);
                        AddFromList(dest, outpost.StoredAnimalsAndVehicles);
                        AddFromList(dest, outpost.StoredMechanoids);
                    }
                }
            }

            var worldAlive = Find.WorldPawns?.AllPawnsAlive;
            if (worldAlive != null)
            {
                for (int i = 0; i < worldAlive.Count; i++)
                {
                    Pawn pawn = worldAlive[i];
                    if (IsLivePlayerPawn(pawn) && !dest.Contains(pawn))
                        dest.Add(pawn);
                }
            }
        }

        private static void AddFromList(List<Pawn> dest, List<Pawn> list)
        {
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
            {
                Pawn pawn = list[i];
                if (IsLivePlayerPawn(pawn))
                    dest.Add(pawn);
            }
        }

        private static bool IsLivePlayerPawn(Pawn pawn) =>
            pawn != null
            && !pawn.Destroyed
            && !pawn.Dead
            && pawn.Faction?.IsPlayer == true
            && !VehicleFrameworkOutpostDissolveCompat.IsVehicleFrameworkVehiclePawn(pawn);

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref joinTickByThingId, "joinTickByThingId", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref missingSinceTick, "joinMissingSinceTick", LookMode.Value, LookMode.Value);
            if (joinTickByThingId == null)
                joinTickByThingId = new Dictionary<string, int>();
            if (missingSinceTick == null)
                missingSinceTick = new Dictionary<string, int>();
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                dropScratch.Clear();
                foreach (var kv in joinTickByThingId)
                {
                    if (kv.Key.NullOrEmpty())
                        dropScratch.Add(kv.Key);
                }
                for (int i = 0; i < dropScratch.Count; i++)
                    joinTickByThingId.Remove(dropScratch[i]);
                missingSinceTick.Clear();
                // Backfill once after load so roster is ready before the next daily tick.
                if (Current.ProgramState == ProgramState.Playing)
                    BackfillMissing();
            }
        }
    }
}
