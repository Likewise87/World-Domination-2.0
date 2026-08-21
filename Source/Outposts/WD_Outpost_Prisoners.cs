using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    [StaticConstructorOnStartup]
    internal static class TakePrisonersGizmoAssets
    {
        internal static readonly Texture2D IconActive;
        internal static readonly Texture2D IconInactive;

        static TakePrisonersGizmoAssets()
        {
            IconActive = ContentFinder<Texture2D>.Get("UI/Commands/TakePrisoners", false)
                ?? TexCommand.ForbidOff;
            IconInactive = ContentFinder<Texture2D>.Get("UI/Commands/TakePrisoners_Off", false)
                ?? TexCommand.ForbidOff;
        }
    }

    /// <summary>Toggle gizmo that swaps pre-colored on/off icons instead of engine tinting.</summary>
    internal sealed class Command_TakePrisonersToggle : Command_Toggle
    {
        public Texture2D iconOn;
        public Texture2D iconOff;

        public override GizmoResult GizmoOnGUI(Vector2 topLeft, float maxWidth, GizmoRenderParms parms)
        {
            icon = isActive != null && isActive() ? iconOn : iconOff;
            return base.GizmoOnGUI(topLeft, maxWidth, parms);
        }
    }

    public partial class WorldObject_WD_Outpost
    {
        public Gizmo GetTakePrisonersGizmo()
        {
            return new Command_TakePrisonersToggle
            {
                defaultLabel = "TSA_WD_TakePrisoners_Label".Translate(),
                defaultDesc = "TSA_WD_TakePrisoners_Desc".Translate(),
                iconOn = TakePrisonersGizmoAssets.IconActive,
                iconOff = TakePrisonersGizmoAssets.IconInactive,
                isActive = () => TakePrisoners,
                toggleAction = () => TakePrisoners = !TakePrisoners
            };
        }

        /// <summary>
        /// Despawn and store a recruitable humanlike as an outpost prisoner.
        /// Skips unwavering / non-recruitable pawns. Does not add to Occupants.
        /// </summary>
        public bool TryCaptureAsPrisoner(Pawn pawn)
        {
            if (pawn == null || pawn.Destroyed || pawn.Dead) return false;
            if (pawn.RaceProps?.Humanlike != true) return false;
            if (OutpostPawnClassificationUtil.IsMechanoidWorker(pawn)) return false;
            if (VehicleFrameworkOutpostDissolveCompat.IsVehicleFrameworkVehiclePawn(pawn)) return false;
            if (pawn.guest != null && !pawn.guest.Recruitable) return false;

            if (Occupants.Contains(pawn)) return false;
            if (Prisoners.Contains(pawn)) return true;

            pawn.ownership?.UnclaimAll();
            VehicleFrameworkOutpostDissolveCompat.TryEjectPawnFromHostingVehicle(pawn);

            if (pawn.Spawned) pawn.DeSpawn();
            pawn.holdingOwner?.Remove(pawn);
            if (Find.WorldPawns != null && Find.WorldPawns.Contains(pawn))
                Find.WorldPawns.RemovePawn(pawn);

            if (pawn.guest != null)
            {
                pawn.guest.SetGuestStatus(Faction.OfPlayer, GuestStatus.Prisoner);
                PrisonerInteractionModeDef recruit = PrisonerInteractionModeDefOf.AttemptRecruit;
                if (recruit != null)
                    pawn.guest.SetExclusiveInteraction(recruit);
            }

            Prisoners.Add(pawn);
            NotePrisonerMaybeNeedsHealing(pawn);
            NotifyVirtualPawnsChanged();
            Window_Prisoners.InvalidateCache();
            return true;
        }

        /// <summary>Remove captive to the void. No map drop, no goodwill dump.</summary>
        public bool LetGoPrisoner(Pawn pawn)
        {
            if (pawn == null) return false;
            if (!Prisoners.Remove(pawn)) return false;

            WorldComponent_PrisonerRecruitSchedule.Get()?.Clear(pawn.ThingID);
            if (!pawn.Destroyed)
                pawn.Destroy(DestroyMode.Vanish);

            NotifyVirtualPawnsChanged();
            Window_Prisoners.InvalidateCache();
            return true;
        }

        /// <summary>Wipe every held captive (outpost teardown / dissolve / loss).</summary>
        public void DestroyAllPrisoners()
        {
            List<Pawn> list = Prisoners;
            if (list.Count == 0) return;

            var schedule = WorldComponent_PrisonerRecruitSchedule.Get();
            for (int i = list.Count - 1; i >= 0; i--)
            {
                Pawn pawn = list[i];
                list.RemoveAt(i);
                if (pawn == null) continue;
                schedule?.Clear(pawn.ThingID);
                if (!pawn.Destroyed)
                    pawn.Destroy(DestroyMode.Vanish);
            }

            prisonersNeedHealing = false;
            NotifyVirtualPawnsChanged();
            Window_Prisoners.InvalidateCache();
        }

        /// <summary>
        /// Convert an outpost captive into an Occupant on this outpost (or scheduled destination).
        /// </summary>
        public bool TryRecruitPrisonerInPlace(Pawn pawn)
        {
            if (pawn == null || pawn.Destroyed || pawn.Dead) return false;
            if (!Prisoners.Contains(pawn)) return false;
            RecruitPrisonersBatch(new List<Pawn> { pawn });
            return !Prisoners.Contains(pawn);
        }

        /// <summary>
        /// Recruit several ready captives at once. Shared destinations leave in one caravan.
        /// </summary>
        public void RecruitPrisonersBatch(List<Pawn> toRecruit)
        {
            if (toRecruit == null || toRecruit.Count == 0) return;

            var schedule = WorldComponent_PrisonerRecruitSchedule.Get();
            var stay = new List<Pawn>();
            var stayMeta = new List<(Pawn pawn, string thingId, bool hadSchedule, WorldObject_WD_Outpost so, MapParent sc)>();
            var byDest = new Dictionary<int, (PlayerPawnTransferDestination dest, List<Pawn> pawns, string destLabel, List<(string thingId, bool hadSchedule, WorldObject_WD_Outpost so, MapParent sc)> meta)>();

            for (int i = 0; i < toRecruit.Count; i++)
            {
                Pawn pawn = toRecruit[i];
                if (pawn == null || pawn.Destroyed || pawn.Dead) continue;
                if (!Prisoners.Contains(pawn)) continue;

                Prisoners.Remove(pawn);

                WorldObject_WD_Outpost destOutpost = this;
                MapParent destColony = null;
                string thingId = pawn.ThingID;
                WorldObject_WD_Outpost scheduledOutpost = null;
                MapParent scheduledColony = null;
                bool hadSchedule = schedule != null
                    && schedule.TryGetDestination(thingId, out scheduledOutpost, out scheduledColony);
                if (hadSchedule)
                {
                    if (scheduledOutpost != null && !scheduledOutpost.Destroyed)
                        destOutpost = scheduledOutpost;
                    else if (scheduledColony != null && !scheduledColony.Destroyed)
                    {
                        destColony = scheduledColony;
                        destOutpost = null;
                    }
                }

                if (pawn.guest != null)
                    pawn.guest.SetGuestStatus(null);
                if (pawn.Faction != Faction.OfPlayer)
                    pawn.SetFaction(Faction.OfPlayer);

                if (destColony != null)
                {
                    if (!destColony.HasMap)
                    {
                        // Unreachable colony: keep on this outpost.
                        if (!AddPawn(pawn, null))
                        {
                            RestoreAsPrisonerAfterFailedRecruit(pawn, schedule, thingId, hadSchedule, scheduledOutpost, scheduledColony);
                            continue;
                        }
                        if (hadSchedule) schedule.Clear(thingId);
                        Messages.Message(
                            "TSA_WD_Prisoners_RecruitStayed".Translate(pawn.LabelShortCap, LabelCap),
                            this,
                            MessageTypeDefOf.TaskCompletion,
                            false);
                        continue;
                    }

                    int key = unchecked((int)0x40000000) ^ destColony.ID;
                    if (!byDest.TryGetValue(key, out var g))
                    {
                        g = (new PlayerPawnTransferDestination
                        {
                            kind = PlayerPawnTransferDestinationKind.Colony,
                            colony = destColony
                        }, new List<Pawn>(), destColony.LabelCap, new List<(string thingId, bool hadSchedule, WorldObject_WD_Outpost so, MapParent sc)>());
                        byDest[key] = g;
                    }
                    g.pawns.Add(pawn);
                    g.meta.Add((thingId, hadSchedule, scheduledOutpost, scheduledColony));
                    byDest[key] = g;
                    continue;
                }

                WorldObject_WD_Outpost dest = destOutpost ?? this;
                if (dest == this)
                {
                    stay.Add(pawn);
                    // Defer schedule clear until AddPawn succeeds.
                    if (hadSchedule)
                        stayMeta.Add((pawn, thingId, true, scheduledOutpost, scheduledColony));
                    else
                        stayMeta.Add((pawn, thingId, false, null, null));
                    continue;
                }

                int outKey = dest.ID;
                if (!byDest.TryGetValue(outKey, out var og))
                {
                    og = (new PlayerPawnTransferDestination
                    {
                        kind = PlayerPawnTransferDestinationKind.Outpost,
                        outpost = dest
                    }, new List<Pawn>(), dest.LabelCap, new List<(string, bool, WorldObject_WD_Outpost, MapParent)>());
                    byDest[outKey] = og;
                }
                og.pawns.Add(pawn);
                og.meta.Add((thingId, hadSchedule, scheduledOutpost, scheduledColony));
                byDest[outKey] = og;
            }

            for (int i = 0; i < stay.Count; i++)
            {
                Pawn pawn = stay[i];
                var meta = stayMeta[i];
                if (!AddPawn(pawn, null))
                {
                    RestoreAsPrisonerAfterFailedRecruit(pawn, schedule, meta.thingId, meta.hadSchedule, meta.so, meta.sc);
                    continue;
                }
                if (meta.hadSchedule) schedule?.Clear(meta.thingId);
                Messages.Message(
                    "TSA_WD_Prisoners_RecruitStayed".Translate(pawn.LabelShortCap, LabelCap),
                    this,
                    MessageTypeDefOf.TaskCompletion,
                    false);
            }

            foreach (var kv in byDest)
            {
                List<Pawn> group = kv.Value.pawns;
                if (group.Count == 0) continue;

                PlayerPawnTransferDestination dest = kv.Value.dest;
                string destLabel = kv.Value.destLabel;
                var meta = kv.Value.meta;
                if (!PlayerPawnTransferUtility.TrySendUnspawnedPawnsFromTileWithPemmican(
                        group,
                        Tile,
                        dest,
                        Window_Prisoners.RecruitJourneyPemmican,
                        this,
                        showRouteMessages: false))
                {
                    for (int i = 0; i < group.Count; i++)
                    {
                        Pawn pawn = group[i];
                        if (pawn == null || pawn.Destroyed) continue;
                        var m = meta[i];
                        if (!AddPawn(pawn, null))
                            RestoreAsPrisonerAfterFailedRecruit(pawn, schedule, m.thingId, m.hadSchedule, m.so, m.sc);
                        else
                        {
                            if (m.hadSchedule) schedule?.Clear(m.thingId);
                            Messages.Message(
                                "TSA_WD_Prisoners_RecruitStayed".Translate(pawn.LabelShortCap, LabelCap),
                                this,
                                MessageTypeDefOf.TaskCompletion,
                                false);
                        }
                    }
                    continue;
                }

                for (int i = 0; i < meta.Count; i++)
                {
                    if (meta[i].hadSchedule)
                        schedule?.Clear(meta[i].thingId);
                }

                bool notify = WorldDominationMod.settings?.notifyPrisonerRecruitedUnderway
                    ?? WorldDominationSettings.DefNotifyPrisonerRecruitedUnderway;
                if (notify)
                {
                    GlobalTargetInfo look = dest.JumpTarget;
                    for (int i = 0; i < group.Count; i++)
                    {
                        Pawn pawn = group[i];
                        if (pawn == null || pawn.Destroyed) continue;
                        Messages.Message(
                            "TSA_WD_Prisoners_RecruitUnderway".Translate(
                                pawn.LabelShortCap, LabelCap, destLabel),
                            look,
                            MessageTypeDefOf.PositiveEvent,
                            false);
                    }
                }
            }

            NotifyVirtualPawnsChanged();
            Window_Prisoners.InvalidateCache();
        }

        private void RestoreAsPrisonerAfterFailedRecruit(
            Pawn pawn,
            WorldComponent_PrisonerRecruitSchedule schedule,
            string thingId,
            bool hadSchedule,
            WorldObject_WD_Outpost scheduledOutpost,
            MapParent scheduledColony)
        {
            if (pawn == null || pawn.Destroyed) return;
            if (!Prisoners.Contains(pawn))
                Prisoners.Add(pawn);
            if (pawn.guest != null)
            {
                pawn.guest.SetGuestStatus(Faction.OfPlayer, GuestStatus.Prisoner);
                PrisonerInteractionModeDef recruit = PrisonerInteractionModeDefOf.AttemptRecruit;
                if (recruit != null)
                    pawn.guest.SetExclusiveInteraction(recruit);
            }
            if (hadSchedule && schedule != null && !thingId.NullOrEmpty())
            {
                if (scheduledOutpost != null && !scheduledOutpost.Destroyed)
                    schedule.SetDest(thingId, scheduledOutpost);
                else if (scheduledColony != null && !scheduledColony.Destroyed)
                    schedule.SetDestColony(thingId, scheduledColony);
            }
            NotifyVirtualPawnsChanged();
            Window_Prisoners.InvalidateCache();
        }
    }
}
