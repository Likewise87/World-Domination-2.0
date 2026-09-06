using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// GAMEPLAY PIVOT FILE — delete or stop calling EnsureOnMap to disable.
    /// When an NPC settlement attack map has no remaining active hostile humanlikes,
    /// destroys unmanned leftover faction turrets so vanilla reform caravan is unblocked.
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
            var s = WorldDominationMod.settings;
            if (s != null && !s.kcsgSilenceDefeatedTurrets) return;
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

            int killed = KillUnmannedFactionTurrets(map, faction);
            silenced = true;

            WDVerbose.Msg(
                $"Killed {killed} unmanned leftover turrets on {map.Parent?.LabelCap} ({faction.Name}).");
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

        /// <summary>
        /// Destroy auto-turrets and empty mannable nests. Leave currently manned turrets alone.
        /// </summary>
        private static int KillUnmannedFactionTurrets(Map map, Faction faction)
        {
            var buildings = map.listerThings?.ThingsInGroup(ThingRequestGroup.BuildingArtificial);
            if (buildings == null) return 0;

            // Snapshot: destroying mutates the lister mid-loop.
            var toKill = new System.Collections.Generic.List<Thing>();
            for (int i = 0; i < buildings.Count; i++)
            {
                Thing thing = buildings[i];
                if (thing == null || thing.Destroyed) continue;
                if (thing.Faction != faction) continue;
                if (thing is not Building_Turret turret) continue;
                if (IsCurrentlyManned(turret)) continue;
                toKill.Add(turret);
            }

            int killed = 0;
            for (int i = 0; i < toKill.Count; i++)
            {
                Thing turret = toKill[i];
                if (turret == null || turret.Destroyed) continue;
                try
                {
                    turret.Destroy(DestroyMode.KillFinalize);
                    killed++;
                }
                catch (System.Exception ex)
                {
                    WDVerbose.Msg($"Turret silence destroy failed {turret.LabelCap}: {ex.Message}");
                }
            }

            return killed;
        }

        private static bool IsCurrentlyManned(Building_Turret turret)
        {
            CompMannable mannable = turret.GetComp<CompMannable>();
            if (mannable == null) return false;
            Pawn manning = mannable.ManningPawn;
            if (manning == null || manning.Dead || manning.Downed) return false;
            // Fleeing gunners no longer count — turret is destroyed with the rest.
            if (manning.MentalStateDef == MentalStateDefOf.PanicFlee) return false;
            return true;
        }
    }
}
