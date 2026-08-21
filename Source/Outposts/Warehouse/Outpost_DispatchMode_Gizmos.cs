using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    [StaticConstructorOnStartup]
    public static class Outpost_DispatchMode_Gizmos
    {
        private static readonly Texture2D LandIcon =
            ContentFinder<Texture2D>.Get("WorldObjects/Caravan_OutpostUpgrade", false) ?? TexCommand.Replant;
        private static readonly Texture2D DropPodIcon =
            ContentFinder<Texture2D>.Get("WorldObjects/DropPod_OutpostUpgrade", false) ?? TexCommand.Replant;
        private static readonly Texture2D AutoDeliverIconOn =
            ContentFinder<Texture2D>.Get("UI/Commands/AutoDeliver", false) ?? TexCommand.Replant;
        private static readonly Texture2D AutoDeliverIconOff =
            ContentFinder<Texture2D>.Get("UI/Commands/AutoDeliver_Off", false) ?? AutoDeliverIconOn;

        public static IEnumerable<Gizmo> GetColonyGizmos(Settlement settlement)
        {
            if (settlement == null || settlement.Faction != Faction.OfPlayer) yield break;
            if (CompPlayerDispatchMode.Get(settlement) == null) yield break;
            yield return MakeDispatchModeGizmo(settlement);
        }

        public static IEnumerable<Gizmo> GetWarehouseGizmos(WorldObject_WD_Outpost warehouse)
        {
            if (warehouse == null || warehouse.Faction != Faction.OfPlayer) yield break;
            CompOutpostWarehouse comp = CompOutpostWarehouse.Get(warehouse);
            if (comp == null) yield break;

            yield return MakeDispatchModeGizmo(warehouse);
            yield return MakeAutoShipGizmo(warehouse, comp);
        }

        private static Command_Action MakeDispatchModeGizmo(WorldObject origin)
        {
            bool viaPod = OutpostDispatchMode.GetViaDropPod(origin);
            bool researched = RapidResponseUtility.TransportPodsResearched();

            string desc;
            if (viaPod)
            {
                desc = "TSA_WD_DispatchMode_DropPodDesc".Translate().ToString()
                    + "\n\n"
                    + "TSA_WD_DispatchMode_DropPodAaWarning".Translate();
            }
            else
            {
                desc = "TSA_WD_DispatchMode_LandDesc".Translate().ToString();
                if (!researched)
                    desc += "\n\n" + "TSA_WD_DispatchMode_NeedsResearch".Translate();
            }

            return new Command_Action
            {
                defaultLabel = viaPod
                    ? "TSA_WD_DispatchMode_DropPod".Translate()
                    : "TSA_WD_DispatchMode_Land".Translate(),
                defaultDesc = desc,
                icon = viaPod ? DropPodIcon : LandIcon,
                defaultIconColor = Color.cyan,
                action = () =>
                {
                    if (viaPod)
                        OutpostDispatchMode.SetViaDropPod(origin, false);
                    else
                        OutpostDispatchMode.TrySetViaDropPod(origin, true);
                }
            };
        }

        private static Command_Toggle MakeAutoShipGizmo(WorldObject_WD_Outpost warehouse, CompOutpostWarehouse comp)
        {
            return new Command_AutoDeliverToggle
            {
                defaultLabel = "TSA_WD_Warehouse_AutoShip".Translate(),
                defaultDesc = "TSA_WD_Warehouse_AutoShipDesc".Translate(),
                iconOn = AutoDeliverIconOn,
                iconOff = AutoDeliverIconOff,
                icon = comp.autoShipEnabled ? AutoDeliverIconOn : AutoDeliverIconOff,
                isActive = () => comp.autoShipEnabled,
                toggleAction = () =>
                {
                    // Ensure a live destination before enabling (falls back to colony if needed).
                    if (!comp.autoShipEnabled && comp.ResolveShipDestination() == null)
                    {
                        Messages.Message("TSA_WD_Warehouse_ShipNeedsDest".Translate(), warehouse, MessageTypeDefOf.RejectInput);
                        return;
                    }
                    comp.autoShipEnabled = !comp.autoShipEnabled;
                }
            };
        }
    }

    /// <summary>Auto-ship toggle that swaps AutoDeliver / AutoDeliver_Off instead of engine tinting.</summary>
    internal sealed class Command_AutoDeliverToggle : Command_Toggle
    {
        public Texture2D iconOn;
        public Texture2D iconOff;

        public override GizmoResult GizmoOnGUI(Vector2 topLeft, float maxWidth, GizmoRenderParms parms)
        {
            icon = isActive != null && isActive() ? iconOn : iconOff;
            return base.GizmoOnGUI(topLeft, maxWidth, parms);
        }
    }
}
