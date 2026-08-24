using System;
using RimWorld;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Soft dependency on Workshop Gravship Raids (<c>sk.gravshipraids</c>) + Odyssey.
    /// Map arrival uses forced <c>GR_GravshipRaid</c> with WD points/faction (no storyteller re-roll).
    /// </summary>
    public static class GravshipRaidsCompat
    {
        public const string PackageId = "sk.gravshipraids";
        public const string IncidentDefName = "GR_GravshipRaid";
        public const string StrategyDefName = "GR_GravshipAssault";
        public const string ArrivalModeDefName = "GR_GravshipLanding";

        public static bool IsModActive()
        {
            if (ModsConfig.IsActive(PackageId) || ModsConfig.IsActive(PackageId + "_steam"))
                return true;
            var mods = LoadedModManager.RunningModsListForReading;
            for (int i = 0; i < mods.Count; i++)
            {
                string id = mods[i]?.PackageId;
                if (string.Equals(id, PackageId, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(id, PackageId + "_steam", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        public static bool IsAvailable()
            => ModsConfig.OdysseyActive && IsModActive();

        /// <summary>
        /// Run Gravship Raids landing with WD-owned points and faction.
        /// Returns false if defs missing, execute fails, or an exception is thrown (caller should fall back).
        /// </summary>
        public static bool TryExecuteWdGravshipRaid(Map map, Faction faction, float points, string letterLabel = null, string letterText = null)
        {
            if (map == null || faction == null || points < 1f)
                return false;
            if (!IsAvailable())
                return false;

            IncidentDef incident = DefDatabase<IncidentDef>.GetNamedSilentFail(IncidentDefName);
            RaidStrategyDef strategy = DefDatabase<RaidStrategyDef>.GetNamedSilentFail(StrategyDefName);
            PawnsArrivalModeDef arrival = DefDatabase<PawnsArrivalModeDef>.GetNamedSilentFail(ArrivalModeDefName);
            if (incident?.Worker == null || strategy == null || arrival == null)
            {
                Log.Warning("[TSA WD] Gravship Raids defs missing; cannot execute WD gravship raid.");
                return false;
            }

            IncidentParms parms = new IncidentParms
            {
                target = map,
                faction = faction,
                points = points,
                forced = true,
                raidStrategy = strategy,
                raidArrivalMode = arrival
            };
            if (!string.IsNullOrEmpty(letterLabel))
                parms.customLetterLabel = letterLabel;
            if (!string.IsNullOrEmpty(letterText))
                parms.customLetterText = letterText;

            try
            {
                bool ok = incident.Worker.TryExecute(parms);
                if (!ok && Prefs.DevMode)
                {
                    Log.Warning("[TSA WD] GR_GravshipRaid.TryExecute returned false faction="
                        + (faction.Name ?? "?")
                        + " points=" + points.ToString("F0")
                        + " map=" + map);
                }
                else if (ok && Prefs.DevMode)
                {
                    Log.Message("[TSA WD] Gravship raid executed points=" + points.ToString("F0")
                        + " faction=" + (faction.Name ?? "?"));
                }
                return ok;
            }
            catch (Exception ex)
            {
                Log.Warning("[TSA WD] Gravship raid execute failed: " + ex.Message);
                return false;
            }
        }
    }
}
