using System.Collections.Generic;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Ally-pull radius toggle (global per site category) + ally breakdown tooltip.</summary>
    [StaticConstructorOnStartup]
    public static class AllyRadiusGizmo
    {
        private static Texture2D cachedIconOn;
        private static Texture2D cachedIconOff;

        public static IEnumerable<Gizmo> Get(WorldObject worldObject)
        {
            if (worldObject == null || worldObject.Destroyed || worldObject.Faction == null)
                yield break;
            if (!PlanetSurfaceWorldActions.IsPlanetSurfaceWorldObjectForWorldActions(worldObject))
                yield break;
            if (!WD_RadiusOverlayPrefs.TryGetCategory(worldObject, out WD_RadiusOverlayCategory category))
                yield break;

            yield return new AllyToggleCommand(worldObject, category);
        }

        private sealed class AllyToggleCommand : Command_RadiusOverlayToggle
        {
            private readonly WorldObject worldObject;

            public AllyToggleCommand(WorldObject worldObject, WD_RadiusOverlayCategory category)
            {
                this.worldObject = worldObject;
                defaultLabel = "TSA_WD_Gizmo_ShowAllyRadius".Translate();
                defaultDesc = "TSA_WD_Gizmo_ShowAllyRadiusDesc".Translate();
                iconOn = cachedIconOn ??= ContentFinder<Texture2D>.Get("UI/Commands/ShowAllyRadius", false) ?? BaseContent.BadTex;
                iconOff = cachedIconOff ??= ContentFinder<Texture2D>.Get("UI/Commands/ShowAllyRadius_Off", false) ?? iconOn;
                isActive = () => WD_RadiusOverlayPrefs.IsActive(category, WD_RadiusOverlayKind.Ally);
                toggleAction = () => WD_RadiusOverlayPrefs.Toggle(category, WD_RadiusOverlayKind.Ally);
            }

            public override string Desc => BuildDesc(worldObject);

            private static string BuildDesc(WorldObject wo)
            {
                var preview = AllyRadiusPreview.Build(wo);
                string body = string.IsNullOrEmpty(preview.tooltip)
                    ? "TSA_WD_AllyPreview_None".Translate().ToString()
                    : preview.tooltip;
                return AllyRadiusUtil.BuildTooltip(wo)
                    + "\n\n"
                    + "TSA_WD_Gizmo_ShowAllyRadiusDesc".Translate().ToString()
                    + "\n\n"
                    + "TSA_WD_AllyPreview_RadiusLine".Translate(AllyRadiusPreview.GetRadius(wo).ToString("F0")).ToString()
                    + "\n"
                    + "TSA_WD_AllyPreview_TotalStrength".Translate(preview.TotalStrength.ToString("F0")).ToString()
                    + "\n\n"
                    + body;
            }
        }
    }
}
