using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Path B type-change: remap world-object IDs and retarget live references from old outpost to new.</summary>
    public static class OutpostReplaceRetargetUtility
    {
        public static int RemapId(int oldId, int newId)
        {
            if (oldId < 0 || newId < 0 || oldId == newId) return 0;
            int changed = 0;
            var worldObjects = Find.WorldObjects?.AllWorldObjects;
            if (worldObjects != null)
            {
                for (int i = 0; i < worldObjects.Count; i++)
                {
                    if (worldObjects[i] is WorldObject_WD_Outpost op && !op.Destroyed)
                    {
                        if (op.itemDeliveryTargetWorldObjectId == oldId)
                        {
                            op.itemDeliveryTargetWorldObjectId = newId;
                            changed++;
                        }
                        var wh = CompOutpostWarehouse.Get(op);
                        if (wh != null && wh.shipDestinationWorldObjectId == oldId)
                        {
                            wh.shipDestinationWorldObjectId = newId;
                            changed++;
                        }
                    }
                }
            }

            var schedule = Find.World?.GetComponent<WorldComponent_PrisonerRecruitSchedule>();
            if (schedule != null)
                changed += schedule.RemapOutpostId(oldId, newId);

            return changed;
        }

        public static int RetargetLiveRefs(WorldObject_WD_Outpost oldOutpost, WorldObject_WD_Outpost newOutpost)
        {
            if (oldOutpost == null || newOutpost == null) return 0;
            int changed = 0;
            var live = WorldObject_Traveler.LiveTravelers;
            if (live != null)
            {
                for (int i = 0; i < live.Count; i++)
                {
                    var t = live[i];
                    if (t == null || t.Destroyed) continue;
                    if (ReferenceEquals(t.originObject, oldOutpost))
                    {
                        t.originObject = newOutpost;
                        changed++;
                    }
                    if (ReferenceEquals(t.targetObject, oldOutpost))
                    {
                        t.targetObject = newOutpost;
                        changed++;
                    }
                    if (ReferenceEquals(t.preTurretDetourTarget, oldOutpost))
                    {
                        t.preTurretDetourTarget = newOutpost;
                        changed++;
                    }
                    if (t.contributionFactors != null && t.contributionFactors.Count > 0
                        && t.contributionFactors.TryGetValue(oldOutpost, out float contrib))
                    {
                        t.contributionFactors.Remove(oldOutpost);
                        t.contributionFactors[newOutpost] = contrib;
                        changed++;
                    }
                    if (t.raidAttackerList != null)
                    {
                        for (int a = 0; a < t.raidAttackerList.Count; a++)
                        {
                            if (ReferenceEquals(t.raidAttackerList[a], oldOutpost))
                            {
                                t.raidAttackerList[a] = newOutpost;
                                changed++;
                            }
                        }
                    }
                }
            }

            var worldObjects = Find.WorldObjects?.AllWorldObjects;
            if (worldObjects != null)
            {
                for (int i = 0; i < worldObjects.Count; i++)
                {
                    if (worldObjects[i] is WorldObject_AT_Turret turret
                        && !turret.Destroyed
                        && ReferenceEquals(turret.builtBySite, oldOutpost))
                    {
                        turret.builtBySite = newOutpost;
                        changed++;
                    }
                }
            }

            if (Find.Maps != null)
            {
                for (int m = 0; m < Find.Maps.Count; m++)
                {
                    Map map = Find.Maps[m];
                    if (map == null) continue;
                    var defense = map.GetComponent<WD_MapComponent_OutpostDefense>();
                    if (defense != null)
                        changed += defense.RetargetOutpostIfMatches(oldOutpost, newOutpost);
                    var clash = map.GetComponent<WD_MapComponent_CaravanClash>();
                    if (clash != null)
                        changed += clash.RetargetOutpostIfMatches(oldOutpost, newOutpost);
                }
            }

            return changed;
        }
    }
}
