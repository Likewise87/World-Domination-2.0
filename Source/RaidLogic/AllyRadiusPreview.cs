using System.Collections.Generic;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Peacetime UI preview of who sits inside the unified ally radius.
    /// Same-faction neighbors always join fights; other-faction diplomatic allies are conditional on constellation.
    /// Live raid resolution still uses <see cref="Raid_ReinforcementLogic.GetReinforcements"/> with enemy/exclusions.
    /// </summary>
    public static class AllyRadiusPreview
    {
        public struct Result
        {
            public float sameFactionStrength;
            public float otherFactionStrength;
            public float TotalStrength => sameFactionStrength + otherFactionStrength;
            public string tooltip;
        }

        private static readonly List<WorldObject> scratch = new List<WorldObject>();
        private static readonly StringBuilder sb = new StringBuilder();

        public static float GetRadius(WorldObject primary, WorldDominationSettings seth = null, WorldComponent_SpreadManager manager = null)
        {
            return AllyRadiusUtil.GetEffective(primary, seth, manager);
        }

        /// <summary>Scaled base ally radius without a primary (no Tunnel bonus). Prefer <see cref="GetRadius(WorldObject, WorldDominationSettings, WorldComponent_SpreadManager)"/>.</summary>
        public static float GetRadius(WorldDominationSettings seth = null)
        {
            return AllyRadiusUtil.GetScaledBaseRadius(seth);
        }

        public static Result Build(WorldObject primary, WorldDominationSettings seth = null, WorldComponent_SpreadManager manager = null)
        {
            Result result = default;
            if (primary?.Faction == null) return result;

            seth ??= WorldDominationMod.settings;
            if (seth == null) return result;
            manager ??= Find.World?.GetComponent<WorldComponent_SpreadManager>();
            if (manager == null) return result;

            float radius = GetRadius(primary, seth, manager);
            var lookup = WorldActions_Utils.GetWorldObjectsWithCompByFaction();

            scratch.Clear();
            CollectNeighbors(primary, radius, lookup, manager, scratch);

            sb.Clear();
            sb.AppendLine("TSA_WD_AllyPreview_SameFactionHeader".Translate());
            bool anySame = false;
            bool anyOther = false;

            for (int i = 0; i < scratch.Count; i++)
            {
                WorldObject wo = scratch[i];
                if (wo?.Faction == null || wo.Faction != primary.Faction) continue;
                float str = StrengthOf(wo, seth);
                result.sameFactionStrength += str;
                sb.AppendLine(FormatAllyLine(wo.LabelCap, str));
                anySame = true;
            }
            if (!anySame)
                sb.AppendLine("TSA_WD_AllyPreview_None".Translate());

            sb.AppendLine();
            sb.AppendLine("TSA_WD_AllyPreview_OtherFactionHeader".Translate());
            sb.AppendLine("TSA_WD_AllyPreview_OtherFactionNote".Translate());

            for (int i = 0; i < scratch.Count; i++)
            {
                WorldObject wo = scratch[i];
                if (wo?.Faction == null || wo.Faction == primary.Faction) continue;
                float str = StrengthOf(wo, seth);
                result.otherFactionStrength += str;
                string label = wo.LabelCap + " (" + wo.Faction.Name + ")";
                sb.AppendLine(FormatAllyLine(label, str));
                anyOther = true;
            }
            if (!anyOther)
                sb.AppendLine("TSA_WD_AllyPreview_None".Translate());

            result.tooltip = sb.ToString().TrimEnd();
            return result;
        }

        private static string FormatAllyLine(string label, float str)
        {
            if (str <= 0f)
                return label + ": 0 (" + "TSA_WD_AllyPreview_TooWeak".Translate() + ")";
            return label + ": " + str.ToString("F0");
        }

        private static float StrengthOf(WorldObject wo, WorldDominationSettings seth)
        {
            var comp = wo?.GetComponent<CompViralSpread>();
            if (comp == null) return 0f;
            return WorldActions_Utils.GetAvailableRaidStrength(comp, seth);
        }

        private static void CollectNeighbors(
            WorldObject primary,
            float radius,
            Dictionary<Faction, List<WorldObject>> lookup,
            WorldComponent_SpreadManager manager,
            List<WorldObject> into)
        {
            Faction primaryFaction = primary.Faction;
            if (primaryFaction == null) return;

            foreach (Faction f in Find.FactionManager.AllFactionsListForReading)
            {
                if (f == null || f.defeated || f.def.hidden) continue;

                bool sameFaction = f == primaryFaction;
                bool otherAlly = !sameFaction
                    && WorldActions_Utils.SafeRelationKindWith(f, primaryFaction) == FactionRelationKind.Ally;
                if (!sameFaction && !otherAlly) continue;

                foreach (WorldObject s in WorldActions_Utils.GetFactionObjects(lookup, f))
                {
                    if (s == null || s == primary) continue;
                    if (!PlanetSurfaceWorldActions.IsPlanetSurfaceTileForWorldActions(s.Tile)) continue;
                    if (s.Faction != null && s.Faction.IsPlayer && s is Settlement playerS && playerS.HasMap)
                        continue;
                    if (WorldActions_Utils.GetDistance(primary.Tile, s.Tile, manager) > radius)
                        continue;
                    into.Add(s);
                }
            }
        }
    }
}
