using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// GAMEPLAY PIVOT FILE — delete or stop calling EnsureOnMap to disable.
    /// When an NPC settlement attack map has no remaining active hostile humanlikes,
    /// powers down leftover faction turrets/mortars so the fight feels won.
    /// Does not change vanilla SettlementDefeatUtility.IsDefeated / conquest hooks.
    /// </summary>
    public class WdSettlementTurretSilence : MapComponent
    {
        private const int CheckIntervalTicks = 60;

        private bool silenced;

        public WdSettlementTurretSilence(Map map) : base(map) { }

        public static void EnsureOnMap(Map map)
        {
            if (map == null) return;
            if (!WdSettlementMapPower.ShouldForcePower(map)) return;
            if (map.GetComponent<WdSettlementTurretSilence>() != null) return;

            map.components.Add(new WdSettlementTurretSilence(map));
        }

        public override void MapComponentTick()
        {
            if (silenced) return;
            if ((Find.TickManager.TicksGame + map.uniqueID) % CheckIntervalTicks != 0) return;

            Faction faction = (map.Parent as Settlement)?.Faction;
            if (faction == null || faction.IsPlayer || faction.defeated) return;

            if (HasActiveHostileHumanlike(map, faction)) return;

            SilenceFactionTurrets(map, faction);
            silenced = true;

            if (Prefs.DevMode)
                Log.Message($"[WorldDomination] Silenced leftover turrets on {map.Parent?.LabelCap} ({faction.Name}).");
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref silenced, "wdSettlementTurretsSilenced", false);
        }

        /// <summary>
        /// Active humanlike threats of the settlement faction (fogged still count; mirrors defeat timing).
        /// Downed / PanicFlee / non-hostile do not block silence.
        /// </summary>
        private static bool HasActiveHostileHumanlike(Map map, Faction faction)
        {
            var pawns = map.mapPawns?.AllPawnsSpawned;
            if (pawns == null) return false;

            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn == null || pawn.Destroyed || pawn.Dead) continue;
                if (pawn.Faction != faction) continue;
                if (!pawn.RaceProps.Humanlike) continue;
                if (pawn.Downed) continue;
                if (pawn.IsPrisoner) continue;
                if (!pawn.HostileTo(Faction.OfPlayer)) continue;
                if (pawn.MentalStateDef == MentalStateDefOf.PanicFlee) continue;
                return true;
            }

            return false;
        }

        private static void SilenceFactionTurrets(Map map, Faction faction)
        {
            var buildings = map.listerThings?.ThingsInGroup(ThingRequestGroup.BuildingArtificial);
            if (buildings == null) return;

            for (int i = 0; i < buildings.Count; i++)
            {
                Thing thing = buildings[i];
                if (thing == null || thing.Destroyed) continue;
                if (thing.Faction != faction) continue;
                if (!(thing is Building_Turret)) continue;

                CompPowerTrader power = thing.TryGetComp<CompPowerTrader>();
                if (power != null)
                    power.PowerOn = false;

                CompRefuelable refuel = thing.TryGetComp<CompRefuelable>();
                if (refuel != null && refuel.HasFuel)
                    refuel.ConsumeFuel(refuel.Fuel);
            }
        }
    }
}
