using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Colony trench walk-over: Fight (corridor clash with colony pawns) or Skip.</summary>
    public class Dialog_ColonyTrenchDefenseChoice : Window
    {
        private readonly WorldObject_Traveler traveler;
        private readonly Settlement colony;

        public override Vector2 InitialSize => new Vector2(520f, 280f);

        public Dialog_ColonyTrenchDefenseChoice(
            WorldObject_Traveler traveler,
            Settlement colony,
            WorldComponent_SpreadManager manager)
        {
            this.traveler = traveler;
            this.colony = colony;
            doCloseX = false;
            doCloseButton = false;
            absorbInputAroundWindow = true;
            forcePause = true;
            closeOnAccept = false;
            closeOnCancel = false;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);
            Text.Font = GameFont.Medium;
            listing.Label("TSA_WD_ColonyTrench_Title".Translate());
            Text.Font = GameFont.Small;
            listing.GapLine();
            listing.Label("TSA_WD_ColonyTrench_Text".Translate(
                colony?.LabelCap ?? "Colony",
                traveler?.Faction?.Name ?? "Unknown",
                traveler?.travelerStrength.ToString("F0") ?? "0"));
            listing.Gap(16f);

            Rect row = listing.GetRect(42f);
            float half = (row.width - 6f) / 2f;
            if (Widgets.ButtonText(new Rect(row.x, row.y, half, row.height), "TSA_WD_OutpostDefense_FightManually".Translate()))
            {
                Close();
                TryStartColonyTrenchFight();
            }
            if (Widgets.ButtonText(new Rect(row.x + half + 6f, row.y, half, row.height), "TSA_WD_OutpostDefense_SkipTrench".Translate()))
            {
                WdTrenchInterceptUtility.MarkResolved(traveler, colony);
                Close();
                ResumeTraveler();
            }
            listing.End();
        }

        private void ResumeTraveler()
        {
            if (traveler == null || traveler.Destroyed) return;
            if (TravelerEndpointUtility.IsLiveEndpoint(traveler.targetObject))
                traveler.pather?.StartPath(traveler.targetObject.Tile);
            else if (colony != null && !colony.Destroyed)
                traveler.pather?.StartPath(colony.Tile);
        }

        private void TryStartColonyTrenchFight()
        {
            WdTrenchInterceptUtility.MarkResolved(traveler, colony);
            if (traveler == null || traveler.Destroyed || colony?.Map == null)
            {
                Messages.Message("TSA_WD_ColonyTrench_NoMap".Translate(), MessageTypeDefOf.RejectInput, false);
                ResumeTraveler();
                return;
            }

            var pawns = new List<Pawn>();
            foreach (Pawn p in colony.Map.mapPawns.FreeColonistsSpawned)
            {
                if (p == null || p.Dead || p.Downed) continue;
                if (!p.IsColonistPlayerControlled) continue;
                pawns.Add(p);
            }

            if (pawns.Count == 0)
            {
                Messages.Message("TSA_WD_ColonyTrench_NoPawns".Translate(), MessageTypeDefOf.RejectInput, false);
                ResumeTraveler();
                return;
            }

            // Leave at least one colonist on the map when required.
            while (pawns.Count > 0
                   && !PlayerPawnTransferUtility.ValidateColonyLeavingPawns(colony, pawns, out _))
            {
                pawns.RemoveAt(pawns.Count - 1);
            }
            if (pawns.Count == 0)
            {
                Messages.Message("TSA_WD_ColonyTrench_LeaveBehind".Translate(), MessageTypeDefOf.RejectInput, false);
                ResumeTraveler();
                return;
            }

            // Point traveler at colony so clash loss resumes there.
            traveler.targetObject = colony;

            var extracted = new List<Pawn>(pawns.Count);
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                if (p.Spawned)
                    p.DeSpawn(DestroyMode.Vanish);
                extracted.Add(p);
            }

            WD_CaravanClashUtility.StartTrenchInterceptionEncounterDropPods(extracted, traveler, colony);

            if (traveler != null && !traveler.Destroyed)
            {
                traveler.suppressDestroyedWorldFx = true;
                traveler.Destroy();
            }
        }
    }
}
