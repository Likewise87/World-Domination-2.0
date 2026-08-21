using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    [StaticConstructorOnStartup]
    internal static class AutoAddPawnGizmoAssets
    {
        internal static readonly Texture2D IconActive;
        internal static readonly Texture2D IconInactive;

        static AutoAddPawnGizmoAssets()
        {
            IconActive = ContentFinder<Texture2D>.Get("UI/Commands/AutoAddArrivals", false)
                ?? TexCommand.ForbidOff;
            IconInactive = ContentFinder<Texture2D>.Get("UI/Commands/AutoAddArrivals_Off", false)
                ?? TexCommand.ForbidOff;
        }
    }

    /// <summary>Toggle gizmo that swaps pre-colored on/off icons instead of engine tinting.</summary>
    internal sealed class Command_AutoAddArrivalsToggle : Command_Toggle
    {
        public Texture2D iconOn;
        public Texture2D iconOff;

        public override GizmoResult GizmoOnGUI(Vector2 topLeft, float maxWidth, GizmoRenderParms parms)
        {
            icon = isActive != null && isActive() ? iconOn : iconOff;
            return base.GizmoOnGUI(topLeft, maxWidth, parms);
        }
    }

    public class CompProperties_AutoAddPawn : WorldObjectCompProperties
    {
        public CompProperties_AutoAddPawn() => compClass = typeof(WorldObjectComp_AutoAddPawn);
    }

    /// <summary>When enabled, any player caravan arriving at this outpost's tile is automatically added to the outpost (WD virtual pawns).</summary>
    public class WorldObjectComp_AutoAddPawn : WorldObjectComp
    {
        /// <summary>How often to try auto-add / prune blocks (world comp ticks). Cheap per tick; heavier work only on this interval.</summary>
        private const int AutoAddCheckIntervalTicks = 500;

        public bool autoAddActive = false;

        public override void Initialize(WorldObjectCompProperties props)
        {
            base.Initialize(props);
            // New outposts only; load path overwrites via PostExposeData.
            autoAddActive = WorldDominationMod.settings?.autoAddPawnsOnArrivalDefault
                ?? WorldDominationSettings.DefAutoAddPawnsOnArrivalDefault;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            // Missing key on old saves stays off (previous default), not the current settings toggle.
            Scribe_Values.Look(ref autoAddActive, "autoAddActive", false);
        }

        public override void CompTick()
        {
            base.CompTick();
            if (!autoAddActive || parent is not WorldObject_WD_Outpost outpost) return;
            if (outpost.Faction != Faction.OfPlayer) return;
            // Leave caravans free to Enter the temporary defense map instead of swallowing them.
            if (outpost.ManualDefenseActive) return;

            if ((Find.TickManager.TicksGame + parent.ID) % AutoAddCheckIntervalTicks != 0) return;

            outpost.PruneAutoAddBlocksWherePawnLeftTile();

            Caravan caravan = Find.WorldObjects.PlayerControlledCaravanAt(parent.Tile);
            if (!Outpost_EstablishmentRequirements.CaravanParkedOnTileForAddToOutpost(caravan, parent.Tile, out _))
                return;

            var reading = caravan.PawnsListForReading;
            if (reading == null || reading.Count == 0) return;

            // Snapshot: AddCaravanPawnToOutpost mutates the caravan pawn list (and may destroy the caravan).
            // Indexing `reading[pi]` while the list shrinks can throw ArgumentOutOfRangeException.
            var snapshot = new List<Pawn>(reading.Count);
            for (int si = 0; si < reading.Count; si++)
            {
                Pawn p = reading[si];
                if (p != null) snapshot.Add(p);
            }

            int before = outpost.PawnCount;
            for (int i = 0; i < snapshot.Count; i++)
            {
                if (caravan.Destroyed) break;
                Pawn pawn = snapshot[i];
                if (pawn == null || pawn.Destroyed || pawn.Dead || pawn.RaceProps?.Humanlike != true) continue;
                if (outpost.IsPawnBlockedFromAutoAdd(pawn)) continue;
                if (VirtualPawnSummary.FromPawn(pawn) != null)
                    outpost.AddCaravanPawnToOutpost(pawn, caravan);
            }
            if (outpost.PawnCount > before)
                Messages.Message("TSA_WD_AutoAdd_Message".Translate(outpost.Label), outpost, MessageTypeDefOf.PositiveEvent);
        }

        public Gizmo GetToggleGizmo()
        {
            return new Command_AutoAddArrivalsToggle
            {
                defaultLabel = "TSA_WD_AutoAdd_Label".Translate(),
                defaultDesc = "TSA_WD_AutoAdd_Desc".Translate(),
                iconOn = AutoAddPawnGizmoAssets.IconActive,
                iconOff = AutoAddPawnGizmoAssets.IconInactive,
                isActive = () => autoAddActive,
                toggleAction = () => autoAddActive = !autoAddActive
            };
        }
    }
}
