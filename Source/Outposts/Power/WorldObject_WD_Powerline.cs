using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Built from any player outpost's Build menu. Extends the reach of power plants that supply outposts: an outpost
    /// within the powerline bonus radius of a powerline can draw from every plant that reaches the powerline.
    /// </summary>
    public class WorldObject_WD_Powerline : WorldObject
    {
        private static Texture2D cachedDismantleIcon;

        /// <summary>ID of the outpost that built (and so "supports") this powerline; -1 when unknown. One powerline per outpost.</summary>
        public int builtByOutpostId = -1;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref builtByOutpostId, "builtByOutpostId", -1);
        }

        public override void PostAdd()
        {
            base.PostAdd();
            OutpostPowerGrid.MarkDirty();
        }

        public override void PostRemove()
        {
            base.PostRemove();
            OutpostPowerGrid.MarkDirty();
        }

        public override string GetInspectString()
        {
            string baseStr = base.GetInspectString();
            string line = "TSA_WD_Powerline_Inspect".Translate(OutpostPowerGrid.PowerlineRangeBonusTiles.ToString("F0")).ToString();
            return string.IsNullOrEmpty(baseStr) ? line : baseStr + "\n" + line;
        }

        public override void DrawExtraSelectionOverlays()
        {
            base.DrawExtraSelectionOverlays();
            if (!PlanetSurfaceWorldActions.IsPlanetSurfaceLayerSelected()) return;
            if (!Find.WorldSelector.IsSelected(this)) return;
            float radius = OutpostPowerGrid.PowerlineRangeBonusTiles;
            if (radius <= 0f) return;
            PlanetLayer layer = PlanetSurfaceWorldActions.LayerOf(this);
            WorldMapRadiusVisual.DrawApproxRadiusRing(new PlanetTile(Tile, layer), radius, WorldOverlayLineMaterials.RoadOrange);
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo g in base.GetGizmos())
                yield return g;
            if (Faction != Faction.OfPlayer) yield break;

            yield return new Command_Action
            {
                defaultLabel = "TSA_WD_Powerline_Dismantle".Translate(),
                defaultDesc = "TSA_WD_Powerline_DismantleDesc".Translate(),
                icon = cachedDismantleIcon ??= ContentFinder<Texture2D>.Get("UI/Designators/Cancel", false) ?? TexCommand.ClearPrioritizedWork,
                action = () =>
                {
                    if (Destroyed) return;
                    Destroy();
                    Messages.Message("TSA_WD_Powerline_Dismantled".Translate(), MessageTypeDefOf.NeutralEvent, false);
                }
            };
        }
    }
}
