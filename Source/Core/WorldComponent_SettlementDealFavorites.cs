using System.Collections.Generic;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Persists starred deal-payment ThingDef defNames for Buy / Gift / Bribe dialogs.
    /// Starred defs are skipped by Assign expensive first / Assign to ask / Assign to min.
    /// </summary>
    public class WorldComponent_SettlementDealFavorites : WorldComponent
    {
        private HashSet<string> starredItemDefNames = new HashSet<string>();

        private static WorldComponent_SettlementDealFavorites? cached;

        public WorldComponent_SettlementDealFavorites(World world) : base(world)
        {
            cached = this;
        }

        public static WorldComponent_SettlementDealFavorites? Get()
        {
            if (cached != null && cached.world == Find.World) return cached;
            cached = Find.World?.GetComponent<WorldComponent_SettlementDealFavorites>();
            return cached;
        }

        public bool IsStarred(string defName)
        {
            return !defName.NullOrEmpty() && starredItemDefNames.Contains(defName);
        }

        public bool IsStarred(ThingDef def)
        {
            return def != null && IsStarred(def.defName);
        }

        public void SetStarred(string defName, bool starred)
        {
            if (defName.NullOrEmpty()) return;
            if (starred) starredItemDefNames.Add(defName);
            else starredItemDefNames.Remove(defName);
        }

        public void SetStarred(ThingDef def, bool starred)
        {
            if (def == null) return;
            SetStarred(def.defName, starred);
        }

        public void Toggle(ThingDef def)
        {
            if (def == null || def.defName.NullOrEmpty()) return;
            if (!starredItemDefNames.Add(def.defName))
                starredItemDefNames.Remove(def.defName);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref starredItemDefNames, "starredItemDefNames", LookMode.Value);
            if (starredItemDefNames == null)
                starredItemDefNames = new HashSet<string>();
        }
    }
}
