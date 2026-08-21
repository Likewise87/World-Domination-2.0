using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>One placed road block / gate on a world tile.</summary>
    public class RoadBlockRecord : IExposable
    {
        public int tileId = -1;
        public Faction builtByFaction;
        /// <summary>NPC Fortify origin settlement (optional; player builds leave null).</summary>
        public Settlement builtBySettlement;
        public RoadBlockKind kind = RoadBlockKind.Normal;
        /// <summary>Remaining hit points; full value from settings for <see cref="kind"/>.</summary>
        public float health = WorldDominationSettings.DefRoadBlockNormalMaxHealth;

        public void ExposeData()
        {
            Scribe_Values.Look(ref tileId, "tileId", -1);
            Scribe_References.Look(ref builtByFaction, "builtByFaction");
            Scribe_References.Look(ref builtBySettlement, "builtBySettlement");
            // Legacy saves scribed Block=0; enum value 0 is now Normal.
            Scribe_Values.Look(ref kind, "kind", RoadBlockKind.Normal);
            float defaultHp = WorldDominationSettings.DefRoadBlockNormalMaxHealth;
            Scribe_Values.Look(ref health, "health", defaultHp);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                float max = WorldDominationMod.settings != null
                    ? WorldDominationMod.settings.GetRoadBlockMaxHealth(kind)
                    : WorldDominationSettings.DefRoadBlockNormalMaxHealth;
                if (health <= 0f || float.IsNaN(health) || float.IsInfinity(health))
                    health = max;
                else if (health > max)
                    health = max;
            }
        }
    }
}
