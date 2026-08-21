using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>One placed world spike trap / caltrops on a tile.</summary>
    public class SpikeTrapRecord : IExposable
    {
        public int tileId = -1;
        public Faction builtByFaction;
        /// <summary>NPC Fortify origin settlement (optional; player builds leave null).</summary>
        public Settlement builtBySettlement;
        public SpikeTrapKind kind = SpikeTrapKind.Spike;
        /// <summary>Remaining hit points; full value from settings for <see cref="kind"/>.</summary>
        public float health = WorldDominationSettings.DefSpikeTrapSpikeMaxHealth;

        public void ExposeData()
        {
            Scribe_Values.Look(ref tileId, "tileId", -1);
            Scribe_References.Look(ref builtByFaction, "builtByFaction");
            Scribe_References.Look(ref builtBySettlement, "builtBySettlement");
            Scribe_Values.Look(ref kind, "kind", SpikeTrapKind.Spike);
            float defaultHp = WorldDominationSettings.DefSpikeTrapSpikeMaxHealth;
            Scribe_Values.Look(ref health, "health", defaultHp);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                float max = WorldDominationMod.settings != null
                    ? WorldDominationMod.settings.GetSpikeTrapMaxHealth(kind)
                    : (kind == SpikeTrapKind.Caltrops
                        ? WorldDominationSettings.DefSpikeTrapCaltropsMaxHealth
                        : WorldDominationSettings.DefSpikeTrapSpikeMaxHealth);
                if (health <= 0f || float.IsNaN(health) || float.IsInfinity(health))
                    health = max;
                else if (health > max)
                    health = max;
            }
        }
    }
}
