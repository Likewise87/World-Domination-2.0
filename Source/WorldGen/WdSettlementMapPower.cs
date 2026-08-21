using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// One-shot post-KCSG pass for NPC settlement attack maps: zero power draw and force PowerOn
    /// so lamps/turrets/etc. work without standing up a real power grid. Fuel turrets get refueled.
    /// </summary>
    public static class WdSettlementMapPower
    {
        private static bool AllowWdSettlementBaseGeneration =>
            WorldDominationMod.settings?.allowWdSettlementBaseGeneration ?? WorldDominationSettings.DefAllowWdSettlementBaseGeneration;

        public static bool ShouldForcePower(Map map)
        {
            if (map?.Parent is not Settlement settlement) return false;
            if (settlement.Faction == null || settlement.Faction.IsPlayer) return false;
            if (!AllowWdSettlementBaseGeneration) return false;
            if (!WorldActions_Utils.IsWdSurfaceTile(settlement.Tile)) return false;
            if (WorksitesExpandedCompat.ShouldSkipWdKcsgInterference(map)) return false;
            if (WorldActions_Utils.HasActiveQuest(settlement)) return false;
            if (settlement.GetComponent<CompViralSpread>() == null) return false;

            string fName = settlement.Faction.Name.ToLowerInvariant();
            string dName = settlement.Faction.def.defName.ToLowerInvariant();
            if (fName.Contains("insect") || dName.Contains("insect") || fName.Contains("hive") || dName.Contains("hive"))
                return false;

            return true;
        }

        public static void ForceSettlementMapPowered(Map map)
        {
            if (!ShouldForcePower(map)) return;

            var buildings = map.listerThings?.ThingsInGroup(ThingRequestGroup.BuildingArtificial);
            if (buildings == null) return;

            int powered = 0;
            int refueled = 0;

            for (int i = 0; i < buildings.Count; i++)
            {
                Thing thing = buildings[i];
                if (thing == null || thing.Destroyed) continue;
                if (thing.IsBrokenDown()) continue;

                CompPowerTrader trader = thing.TryGetComp<CompPowerTrader>();
                if (trader != null)
                {
                    CompFlickable flick = thing.TryGetComp<CompFlickable>();
                    if (flick == null || flick.SwitchIsOn)
                    {
                        // PowerNet only shuts down traders with negative PowerOutput. Zero draw
                        // keeps PowerOn sticky without gens/batteries/net rebuilds.
                        trader.PowerOutput = 0f;
                        if (!trader.PowerOn)
                            trader.PowerOn = true;
                        powered++;
                    }
                }

                if (thing is Building_Turret)
                {
                    CompRefuelable refuel = thing.TryGetComp<CompRefuelable>();
                    if (refuel != null)
                    {
                        float missing = refuel.Props.fuelCapacity - refuel.Fuel;
                        if (missing > 0.01f)
                        {
                            refuel.Refuel(missing);
                            refueled++;
                        }
                    }
                }
            }

            if (Prefs.DevMode)
                Log.Message($"[WorldDomination] Settlement map power bypass for {map.Parent?.LabelCap}: {powered} powered (0W), {refueled} turrets refueled.");
        }
    }
}
