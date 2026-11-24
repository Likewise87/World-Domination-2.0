using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Right-side alert when a power plant has colony-facing watts but no wired remote power spot.
    /// Toggled by <see cref="WorldDominationSettings.notifyNoRemotePowerSpot"/> (default on).
    /// </summary>
    public class Alert_WDNoRemotePowerSpot : Alert
    {
        private readonly List<WorldObject> culpritsScratch = new List<WorldObject>(4);
        private string labelCached;

        public Alert_WDNoRemotePowerSpot()
        {
            defaultPriority = AlertPriority.Medium;
        }

        public override string GetLabel() => labelCached ??= "TSA_WD_Alert_NoRemotePowerSpot".Translate();

        public override TaggedString GetExplanation()
        {
            return "TSA_WD_Alert_NoRemotePowerSpotDesc".Translate();
        }

        public override AlertReport GetReport()
        {
            if (Current.ProgramState != ProgramState.Playing) return false;
            if (!OutpostPowerGrid.Enabled) return false;
            if (WorldDominationMod.settings == null || !WorldDominationMod.settings.notifyNoRemotePowerSpot)
                return false;
            if (Outpost_PowerPlant.GetTotalRemotePowerWatts() <= 0f) return false;
            if (Outpost_PowerPlant.HasEligibleRemotePowerSpots()) return false;

            CollectPowerPlantCulprits(culpritsScratch);
            if (culpritsScratch.Count == 0) return false;

            WorldObject first = culpritsScratch[0];
            return first != null && !first.Destroyed ? AlertReport.CulpritIs(first) : AlertReport.Active;
        }

        private static void CollectPowerPlantCulprits(List<WorldObject> into)
        {
            into.Clear();
            IReadOnlyList<WorldObject_WD_Outpost> outposts = WdPlayerOutpostCache.PlayerOutposts;
            for (int i = 0; i < outposts.Count; i++)
            {
                WorldObject_WD_Outpost outpost = outposts[i];
                if (outpost == null || outpost.Destroyed) continue;
                if (!outpost.IsPowerPlantOutpost) continue;
                into.Add(outpost);
            }
        }
    }
}
