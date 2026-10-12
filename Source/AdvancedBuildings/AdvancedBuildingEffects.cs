using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Owner buffs from economic advanced buildings. All sizes read <see cref="EffectPct"/>.</summary>
    public static class AdvancedBuildingEffects
    {
        public static float EffectPct =>
            Mathf.Clamp(WorldDominationSettings.DefAdvancedBuildingEffectPct, 0f, 5f);

        public static string EffectPctLabel => (EffectPct * 100f).ToString("F0");

        public static bool Has(WorldObject owner, AdvancedBuildingEffect effect)
        {
            if (!(owner is Settlement s) || s.Destroyed) return false;
            if (s.Faction == null || s.Faction.IsPlayer) return false;
            return WorldComponent_AdvancedBuildings.Get()?.HasEffect(s, effect) == true;
        }

        public static float Mult(WorldObject owner, AdvancedBuildingEffect effect) =>
            Has(owner, effect) ? 1f + EffectPct : 1f;

        /// <summary>Silo: settlement max offensive strength for <paramref name="tier"/> (base tier max × Silo).</summary>
        public static float GetEffectiveOffensiveMax(CompViralSpread comp, SettlementTier tier)
        {
            float baseMax = CompViralSpread.GetStrengthRange(tier).max;
            if (comp == null || !comp.IsSettlement || comp.IsOutpost) return baseMax;
            return baseMax * Mult(comp.parent, AdvancedBuildingEffect.OffensiveMaxPct);
        }

        /// <summary>Watch Tower: multiplier on NPC settlement attack range.</summary>
        public static float AttackRangeMult(Settlement settlement) =>
            Mult(settlement, AdvancedBuildingEffect.AttackRangePct);

        /// <summary>Hospital: multiplier on passive offensive growth and daily defensive recovery.</summary>
        public static float RegenMult(WorldObject owner) =>
            Mult(owner, AdvancedBuildingEffect.RegenPct);

        /// <summary>Stable: NPC land caravan ticks-per-move from <paramref name="origin"/> (lower = faster).</summary>
        public static int GetNpcLandTicksPerMove(WorldObject origin, int baseTicksPerMove)
        {
            if (baseTicksPerMove <= 0) return baseTicksPerMove;
            if (!Has(origin, AdvancedBuildingEffect.LandSpeedPct)) return baseTicksPerMove;
            return Mathf.Max(1, Mathf.RoundToInt(baseTicksPerMove / (1f + EffectPct)));
        }

        /// <summary>Stable-aware ticks per move for an NPC walking caravan launched from <paramref name="origin"/>.</summary>
        public static int GetNpcLandTicksPerMove(WorldObject origin) =>
            GetNpcLandTicksPerMove(origin, WorldActions_Traveler.GetWalkingTicksPerMove());

        /// <summary>Call at NPC land-caravan spawn (before StartPath): applies the origin's Stable to the traveler's current speed.</summary>
        public static void ApplyNpcLandSpeed(WorldObject_Traveler traveler, WorldObject origin)
        {
            if (traveler == null) return;
            traveler.ticksPerMove = GetNpcLandTicksPerMove(origin, traveler.ticksPerMove);
        }

        /// <summary>"Buildings: Silo, Hospital (2/3)" for NPC settlement inspect; "Buildings: none (0/1)" when empty.</summary>
        public static string BuildingsInspectLine(Settlement s, CompViralSpread comp)
        {
            if (s == null || comp == null || !comp.IsSettlement || comp.IsOutpost) return null;
            var reg = WorldComponent_AdvancedBuildings.Get();
            if (reg == null) return null;
            var owned = reg.OwnedBy(s);
            var names = new System.Text.StringBuilder();
            int count = 0;
            for (int i = 0; i < owned.Count; i++)
            {
                var b = owned[i];
                if (b == null || b.Destroyed) continue;
                if (count > 0) names.Append(", ");
                names.Append(b.LabelCap);
                count++;
            }
            string list = count > 0 ? names.ToString() : "TSA_WD_AdvBuild_Inspect_None".Translate().ToString();
            int slots = WorldActions_AdvancedBuildings.SlotsForTier(comp.tier);
            return "TSA_WD_AdvBuild_Inspect_SettlementBuildings".Translate(list, count.ToString(), slots.ToString()).ToString();
        }
    }
}
