using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Selection gizmo: withdraw everyone (same path as selecting all pawns and Transfer with Exit here).
    /// </summary>
    [StaticConstructorOnStartup]
    public static class Action_Outpost_Abandon
    {
        public static IEnumerable<Gizmo> GetGizmos(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null || outpost.Faction != Faction.OfPlayer) yield break;

            var cmd = new Command_Action
            {
                defaultLabel = "TSA_WD_AbandonOutpost".Translate(),
                defaultDesc = "TSA_WD_AbandonOutpostTip".Translate(),
                icon = TexButton.Delete,
                defaultIconColor = Color.red,
                action = () => OpenAbandonTransfer(outpost)
            };

            if (outpost.ManualDefenseActive)
            {
                cmd.Disable("TSA_WD_OutpostDefense_FrozenDuringManualDefense".Translate());
            }
            else if (!HasAnyMovableRoster(outpost))
            {
                cmd.Disable("TSA_WD_AbandonOutpost_NoRoster".Translate());
            }

            yield return cmd;
        }

        private static bool HasAnyMovableRoster(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null) return false;
            if (CountList(outpost.Occupants) > 0) return true;
            if (CountList(outpost.Prisoners) > 0) return true;
            if (CountList(outpost.StoredAnimalsAndVehicles) > 0) return true;
            if (CountList(outpost.StoredMechanoids) > 0) return true;
            if (outpost.StoredPassengerShuttles != null && outpost.StoredPassengerShuttles.Count > 0) return true;
            return false;
        }

        private static int CountList(List<Pawn> list)
        {
            if (list == null || list.Count == 0) return 0;
            int n = 0;
            for (int i = 0; i < list.Count; i++)
            {
                Pawn p = list[i];
                if (p != null && !p.Destroyed && !p.Dead) n++;
            }
            return n;
        }

        private static void OpenAbandonTransfer(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null || outpost.Destroyed) return;
            if (outpost.ManualDefenseActive)
            {
                Messages.Message("TSA_WD_OutpostDefense_FrozenDuringManualDefense".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            var ids = CollectAllRosterThingIds(outpost);
            if (ids.Count == 0)
            {
                Messages.Message("TSA_WD_AbandonOutpost_NoRoster".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            var selected = PlayerPawnRosterUtility.BuildTransferEntriesForOutpost(outpost, ids);
            if (selected == null || selected.Count == 0)
            {
                Messages.Message("TSA_WD_AbandonOutpost_NoRoster".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            PlayerPawnDropPodUtility.SetAdHocViaDropPod(
                PlayerPawnDropPodUtility.GetTravelViaDropPod(outpost));
            Find.WindowStack.Add(new Dialog_MovePawnToLocation(selected, null, offerExitHere: true));
            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        private static HashSet<string> CollectAllRosterThingIds(WorldObject_WD_Outpost outpost)
        {
            var ids = new HashSet<string>();
            AddPawnIds(ids, outpost.Occupants);
            AddPawnIds(ids, outpost.Prisoners);
            AddPawnIds(ids, outpost.StoredAnimalsAndVehicles);
            AddPawnIds(ids, outpost.StoredMechanoids);
            var shuttles = outpost.StoredPassengerShuttles;
            if (shuttles != null)
            {
                for (int i = 0; i < shuttles.Count; i++)
                {
                    Thing t = shuttles[i];
                    if (t?.ThingID != null && !t.Destroyed)
                        ids.Add(t.ThingID);
                }
            }
            return ids;
        }

        private static void AddPawnIds(HashSet<string> ids, List<Pawn> list)
        {
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
            {
                Pawn p = list[i];
                if (p?.ThingID != null && !p.Destroyed && !p.Dead)
                    ids.Add(p.ThingID);
            }
        }
    }
}
