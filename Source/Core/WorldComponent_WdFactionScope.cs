using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Per-save Configure Scope (WD Actions / MapGen / Storyteller) and allegiance pair locks.
    /// ModSettings fields of the same names are legacy Look-only and must not be used for gameplay.
    /// </summary>
    public class WorldComponent_WdFactionScope : WorldComponent
    {
        private HashSet<string> manualExcludeFromWdActions = new HashSet<string>();
        private HashSet<string> manualExcludeFromWdBaseGen = new HashSet<string>();
        private HashSet<string> manualIncludeInWd = new HashSet<string>();
        private HashSet<string> manualAllowStorytellerRaids = new HashSet<string>();
        private HashSet<string> manualBlockStorytellerRaids = new HashSet<string>();
        private HashSet<string> lockedAllegiancePairs = new HashSet<string>();
        private WdStorytellerBlockedRaidMode storytellerBlockedRaidMode =
            WorldDominationSettings.DefStorytellerBlockedRaidMode;
        private bool initialAllegianceLockDone;

        private static WorldComponent_WdFactionScope cached;

        public WorldComponent_WdFactionScope(World world) : base(world)
        {
            cached = this;
        }

        /// <summary>World-keyed singleton for the loaded save; null when no world.</summary>
        public static WorldComponent_WdFactionScope Get()
        {
            if (cached != null && cached.world == Find.World) return cached;
            cached = Find.World?.GetComponent<WorldComponent_WdFactionScope>();
            return cached;
        }

        public WdStorytellerBlockedRaidMode StorytellerBlockedRaidMode
        {
            get => storytellerBlockedRaidMode;
            set => storytellerBlockedRaidMode = value;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref lockedAllegiancePairs, "lockedAllegiancePairs", LookMode.Value);
            Scribe_Collections.Look(ref manualExcludeFromWdActions, "manualExcludeFromWdActions", LookMode.Value);
            Scribe_Collections.Look(ref manualExcludeFromWdBaseGen, "manualExcludeFromWdBaseGen", LookMode.Value);
            Scribe_Collections.Look(ref manualIncludeInWd, "manualIncludeInWd", LookMode.Value);
            Scribe_Collections.Look(ref manualAllowStorytellerRaids, "manualAllowStorytellerRaids", LookMode.Value);
            Scribe_Collections.Look(ref manualBlockStorytellerRaids, "manualBlockStorytellerRaids", LookMode.Value);
            Scribe_Values.Look(ref storytellerBlockedRaidMode, "storytellerBlockedRaidMode",
                WorldDominationSettings.DefStorytellerBlockedRaidMode);
            Scribe_Values.Look(ref initialAllegianceLockDone, "initialAllegianceLockDone", false);
            if (lockedAllegiancePairs == null) lockedAllegiancePairs = new HashSet<string>();
            if (manualExcludeFromWdActions == null) manualExcludeFromWdActions = new HashSet<string>();
            if (manualExcludeFromWdBaseGen == null) manualExcludeFromWdBaseGen = new HashSet<string>();
            if (manualIncludeInWd == null) manualIncludeInWd = new HashSet<string>();
            if (manualAllowStorytellerRaids == null) manualAllowStorytellerRaids = new HashSet<string>();
            if (manualBlockStorytellerRaids == null) manualBlockStorytellerRaids = new HashSet<string>();
        }

        public bool IsManualWdExclude(string defName) =>
            !string.IsNullOrEmpty(defName) && manualExcludeFromWdActions.Contains(defName);

        public void SetManualWdExclude(string defName, bool exclude)
        {
            if (string.IsNullOrEmpty(defName)) return;
            if (exclude) manualExcludeFromWdActions.Add(defName);
            else manualExcludeFromWdActions.Remove(defName);
        }

        public bool IsManualWdBaseGenExclude(string defName) =>
            !string.IsNullOrEmpty(defName) && manualExcludeFromWdBaseGen.Contains(defName);

        public void SetManualWdBaseGenExclude(string defName, bool exclude)
        {
            if (string.IsNullOrEmpty(defName)) return;
            if (exclude) manualExcludeFromWdBaseGen.Add(defName);
            else manualExcludeFromWdBaseGen.Remove(defName);
        }

        public bool IsManualIncludeInWd(string defName) =>
            !string.IsNullOrEmpty(defName) && manualIncludeInWd.Contains(defName);

        public void SetManualIncludeInWd(string defName, bool include)
        {
            if (string.IsNullOrEmpty(defName)) return;
            if (include) manualIncludeInWd.Add(defName);
            else manualIncludeInWd.Remove(defName);
        }

        public void ResetManualFactionParticipation()
        {
            manualExcludeFromWdActions = new HashSet<string>();
            manualExcludeFromWdBaseGen = new HashSet<string>();
            manualIncludeInWd = new HashSet<string>();
            manualAllowStorytellerRaids = new HashSet<string>();
            manualBlockStorytellerRaids = new HashSet<string>();
            storytellerBlockedRaidMode = WorldDominationSettings.DefStorytellerBlockedRaidMode;
        }

        public bool GetStorytellerRaidsAllowed(string defName, bool wdParticipantEffective)
        {
            if (!string.IsNullOrEmpty(defName) && manualBlockStorytellerRaids.Contains(defName))
                return false;
            if (!string.IsNullOrEmpty(defName) && manualAllowStorytellerRaids.Contains(defName))
                return true;
            return !wdParticipantEffective;
        }

        public void SetManualStorytellerRaidsAllow(string defName, bool allow, bool wdParticipantEffective)
        {
            if (string.IsNullOrEmpty(defName)) return;
            bool defaultAllow = !wdParticipantEffective;
            if (allow == defaultAllow)
            {
                manualAllowStorytellerRaids.Remove(defName);
                manualBlockStorytellerRaids.Remove(defName);
                return;
            }

            if (allow)
            {
                manualAllowStorytellerRaids.Add(defName);
                manualBlockStorytellerRaids.Remove(defName);
            }
            else
            {
                manualBlockStorytellerRaids.Add(defName);
                manualAllowStorytellerRaids.Remove(defName);
            }
        }

        public void ClearManualStorytellerOverride(string defName)
        {
            if (string.IsNullOrEmpty(defName)) return;
            manualAllowStorytellerRaids.Remove(defName);
            manualBlockStorytellerRaids.Remove(defName);
        }

        public bool IsPairLocked(Faction a, Faction b)
        {
            if (a == null || b == null) return false;
            return lockedAllegiancePairs.Contains(GetFactionPairKey(a, b));
        }

        public bool IsPairKeyLocked(string key) =>
            !string.IsNullOrEmpty(key) && lockedAllegiancePairs.Contains(key);

        public void SetPairKeyLocked(string key, bool locked)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (locked) lockedAllegiancePairs.Add(key);
            else lockedAllegiancePairs.Remove(key);
        }

        public void ClearAllegianceLocks()
        {
            lockedAllegiancePairs.Clear();
        }

        public string GetFactionPairKey(Faction a, Faction b)
        {
            return a.loadID < b.loadID ? $"{a.loadID}_{b.loadID}" : $"{b.loadID}_{a.loadID}";
        }

        public void ResetAllegianceLocks(bool lockAllHostiles = false)
        {
            lockedAllegiancePairs = new HashSet<string>();
            if (Find.FactionManager == null) return;

            // Include the player so Perm. Hostile can lock player×permanent-enemy pairs too.
            var factions = Find.FactionManager.AllFactionsVisible
                .Where(f => f != null && (f.IsPlayer || !WorldActions_Utils.IsAutoExcludedFromWd(f)))
                .ToList();

            foreach (var f in factions)
            {
                bool fPerm = WorldDominationSettings.IsAllegianceLockPermanentHostile(f);
                bool fHive = IsAllegianceLockHive(f);

                foreach (var other in factions)
                {
                    if (f == other) continue;

                    bool otherPerm = WorldDominationSettings.IsAllegianceLockPermanentHostile(other);
                    bool otherHive = IsAllegianceLockHive(other);

                    if (fHive || otherHive)
                    {
                        lockedAllegiancePairs.Add(GetFactionPairKey(f, other));
                        continue;
                    }

                    if (lockAllHostiles)
                    {
                        if (fPerm || otherPerm)
                            lockedAllegiancePairs.Add(GetFactionPairKey(f, other));
                    }
                    else if (fPerm && otherPerm && WorldActions_Utils.SafeHostileTo(f, other))
                    {
                        lockedAllegiancePairs.Add(GetFactionPairKey(f, other));
                    }
                }
            }
        }

        private static bool IsAllegianceLockHive(Faction f) =>
            f != null && !f.IsPlayer && f.def != null && !f.def.humanlikeFaction;

        public void EnsureInitialLaunchDefaults()
        {
            if (initialAllegianceLockDone) return;
            ResetAllegianceLocks();
            initialAllegianceLockDone = true;
        }
    }
}
