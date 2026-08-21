using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Maps prisoner ThingIDs to a post-recruit destination (player outpost or colony MapParent).
    /// Also stores which outposts Smart Assign may use.</summary>
    public class WorldComponent_PrisonerRecruitSchedule : WorldComponent
    {
        private Dictionary<string, int> destOutpostIdByThingId = new Dictionary<string, int>();
        private HashSet<int> smartAssignExcludedOutpostIds = new HashSet<int>();
        private List<string> tmpKeys;
        private List<int> tmpValues;
        private int lastPruneTick = -99999;
        private const int PruneIntervalTicks = 2500;

        private static WorldComponent_PrisonerRecruitSchedule cached;

        public WorldComponent_PrisonerRecruitSchedule(World world) : base(world)
        {
            cached = this;
        }

        public static WorldComponent_PrisonerRecruitSchedule Get()
        {
            if (cached != null && cached.world == Find.World) return cached;
            cached = Find.World?.GetComponent<WorldComponent_PrisonerRecruitSchedule>();
            return cached;
        }

        public override void WorldComponentTick()
        {
            int now = Find.TickManager.TicksGame;
            if (now - lastPruneTick < PruneIntervalTicks) return;
            lastPruneTick = now;
            PruneInvalid();
        }

        public void SetDest(string thingId, WorldObject_WD_Outpost outpost)
        {
            if (thingId.NullOrEmpty() || outpost == null || outpost.Destroyed) return;
            destOutpostIdByThingId[thingId] = outpost.ID;
        }

        public void SetDestForMany(IEnumerable<string> thingIds, WorldObject_WD_Outpost outpost)
        {
            if (thingIds == null || outpost == null || outpost.Destroyed) return;
            int id = outpost.ID;
            foreach (string tid in thingIds)
            {
                if (tid.NullOrEmpty()) continue;
                destOutpostIdByThingId[tid] = id;
            }
        }

        public void SetDestColony(string thingId, MapParent colony)
        {
            if (thingId.NullOrEmpty() || colony == null || colony.Destroyed) return;
            if (colony.Faction == null || !colony.Faction.IsPlayer || !colony.HasMap) return;
            destOutpostIdByThingId[thingId] = colony.ID;
        }

        public void SetDestColonyForMany(IEnumerable<string> thingIds, MapParent colony)
        {
            if (thingIds == null || colony == null || colony.Destroyed) return;
            if (colony.Faction == null || !colony.Faction.IsPlayer || !colony.HasMap) return;
            int id = colony.ID;
            foreach (string tid in thingIds)
            {
                if (tid.NullOrEmpty()) continue;
                destOutpostIdByThingId[tid] = id;
            }
        }

        public void Clear(string thingId)
        {
            if (thingId.NullOrEmpty()) return;
            destOutpostIdByThingId.Remove(thingId);
        }

        public void ClearMany(IEnumerable<string> thingIds)
        {
            if (thingIds == null) return;
            foreach (string tid in thingIds)
            {
                if (!tid.NullOrEmpty())
                    destOutpostIdByThingId.Remove(tid);
            }
        }

        public bool TryGetDestId(string thingId, out int destId)
        {
            destId = -1;
            if (thingId.NullOrEmpty()) return false;
            return destOutpostIdByThingId.TryGetValue(thingId, out destId);
        }

        /// <summary>Legacy name: returns stored id whether it points at an outpost or colony.</summary>
        public bool TryGetOutpostId(string thingId, out int outpostId) => TryGetDestId(thingId, out outpostId);

        public bool TryGetOutpost(string thingId, out WorldObject_WD_Outpost outpost)
        {
            outpost = null;
            if (!TryGetDestId(thingId, out int id)) return false;
            outpost = ResolvePlayerOutpost(id);
            return outpost != null;
        }

        public bool TryGetColony(string thingId, out MapParent colony)
        {
            colony = null;
            if (!TryGetDestId(thingId, out int id)) return false;
            colony = ResolvePlayerColony(id);
            return colony != null;
        }

        public bool TryGetDestination(string thingId, out WorldObject_WD_Outpost outpost, out MapParent colony)
        {
            outpost = null;
            colony = null;
            if (!TryGetDestId(thingId, out int id)) return false;
            outpost = ResolvePlayerOutpost(id);
            if (outpost != null) return true;
            colony = ResolvePlayerColony(id);
            return colony != null;
        }

        public bool IsSmartAssignExcluded(int outpostId)
        {
            return outpostId >= 0 && smartAssignExcludedOutpostIds.Contains(outpostId);
        }

        public bool IsSmartAssignExcluded(WorldObject_WD_Outpost outpost)
        {
            return outpost != null && IsSmartAssignExcluded(outpost.ID);
        }

        public void SetSmartAssignExcluded(int outpostId, bool excluded)
        {
            if (outpostId < 0) return;
            if (excluded) smartAssignExcludedOutpostIds.Add(outpostId);
            else smartAssignExcludedOutpostIds.Remove(outpostId);
        }

        public void SetSmartAssignExcluded(WorldObject_WD_Outpost outpost, bool excluded)
        {
            if (outpost == null) return;
            SetSmartAssignExcluded(outpost.ID, excluded);
        }

        public void SetSmartAssignExcludedMany(IEnumerable<WorldObject_WD_Outpost> outposts, bool excluded)
        {
            if (outposts == null) return;
            foreach (WorldObject_WD_Outpost o in outposts)
                SetSmartAssignExcluded(o, excluded);
        }

        public static WorldObject_WD_Outpost ResolvePlayerOutpost(int worldObjectId)
        {
            if (worldObjectId < 0) return null;
            WorldObject wo = Find.WorldObjects?.AllWorldObjects?.Find(o => o != null && o.ID == worldObjectId);
            if (wo is not WorldObject_WD_Outpost outpost || outpost.Destroyed) return null;
            if (outpost.Faction == null || !outpost.Faction.IsPlayer) return null;
            return outpost;
        }

        public static MapParent ResolvePlayerColony(int worldObjectId)
        {
            if (worldObjectId < 0) return null;
            WorldObject wo = Find.WorldObjects?.AllWorldObjects?.Find(o => o != null && o.ID == worldObjectId);
            if (wo is not MapParent mp || mp.Destroyed) return null;
            if (mp is WorldObject_WD_Outpost) return null;
            if (mp.Faction == null || !mp.Faction.IsPlayer || !mp.HasMap) return null;
            return mp;
        }

        public static bool IsValidDestinationId(int worldObjectId) =>
            ResolvePlayerOutpost(worldObjectId) != null || ResolvePlayerColony(worldObjectId) != null;

        public void PruneInvalid()
        {
            if (destOutpostIdByThingId.Count > 0)
            {
                var drop = new List<string>();
                foreach (var kv in destOutpostIdByThingId)
                {
                    if (!IsValidDestinationId(kv.Value))
                        drop.Add(kv.Key);
                }
                for (int i = 0; i < drop.Count; i++)
                    destOutpostIdByThingId.Remove(drop[i]);
            }

            if (smartAssignExcludedOutpostIds.Count > 0)
            {
                var dropIds = new List<int>();
                foreach (int id in smartAssignExcludedOutpostIds)
                {
                    if (ResolvePlayerOutpost(id) == null)
                        dropIds.Add(id);
                }
                for (int i = 0; i < dropIds.Count; i++)
                    smartAssignExcludedOutpostIds.Remove(dropIds[i]);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref destOutpostIdByThingId, "destOutpostIdByThingId", LookMode.Value, LookMode.Value, ref tmpKeys, ref tmpValues);
            if (destOutpostIdByThingId == null)
                destOutpostIdByThingId = new Dictionary<string, int>();

            Scribe_Collections.Look(ref smartAssignExcludedOutpostIds, "smartAssignExcludedOutpostIds", LookMode.Value);
            if (smartAssignExcludedOutpostIds == null)
                smartAssignExcludedOutpostIds = new HashSet<int>();
        }
    }
}
