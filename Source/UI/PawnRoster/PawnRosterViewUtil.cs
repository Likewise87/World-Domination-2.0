#nullable disable
using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Shared helpers for view hosts: sort options, filter descriptions, column auto-enable, row filters.</summary>
    public static class PawnRosterViewUtil
    {
        /// <summary>Catalog columns that are not sortable data columns.</summary>
        private static bool IsSortableCatalogId(string id) =>
            id != PawnRosterColumnIds.Portrait
            && id != PawnRosterColumnIds.Select
            && id != PawnRosterColumnIds.OtherInventory;

        /// <summary>Adds every sortable catalog column of <paramref name="window"/> (visible or hidden).</summary>
        public static void AddCatalogSortOptions(PawnRosterColumnWindow window, List<PawnRosterSortOption> into)
        {
            IReadOnlyList<PawnRosterColumnOption> opts = PawnRosterColumnCatalog.OptionsFor(window);
            for (int i = 0; i < opts.Count; i++)
            {
                PawnRosterColumnOption opt = opts[i];
                if (!IsSortableCatalogId(opt.Id)) continue;
                AddUnique(into, opt.Id, PawnRosterColumnCatalog.ResolveLabel(opt));
            }
        }

        public static void AddUnique(List<PawnRosterSortOption> into, string id, string label)
        {
            for (int i = 0; i < into.Count; i++)
            {
                if (into[i].Id == id) return;
            }
            into.Add(new PawnRosterSortOption(id, label));
        }

        /// <summary>Turns on hidden catalog columns that the sort chain or active filters rely on.</summary>
        public static bool EnsureColumnsForView(IPawnRosterViewHost host)
        {
            if (host == null) return false;
            WorldComponent_PawnRosterColumnPrefs prefs = WorldComponent_PawnRosterColumnPrefs.Get();
            if (prefs == null) return false;
            PawnRosterColumnWindow window = host.RosterWindow;
            PawnRosterViewState view = host.RosterView;
            bool changed = false;
            for (int i = 0; i < view.Sort.Count; i++)
                changed |= EnsureVisible(prefs, window, view.Sort[i].Id);
            string[] keys = PawnRosterFilterKeys.All;
            for (int i = 0; i < keys.Length; i++)
            {
                if (!view.HasFilter(keys[i])) continue;
                changed |= EnsureVisible(prefs, window, PawnRosterFilterKeys.ColumnFor(keys[i]));
            }
            if (PawnRosterTraitFilter.IsActive)
                changed |= EnsureVisible(prefs, window, PawnRosterColumnIds.Traits);
            return changed;
        }

        private static bool EnsureVisible(WorldComponent_PawnRosterColumnPrefs prefs, PawnRosterColumnWindow window, string id)
        {
            if (id.NullOrEmpty() || !PawnRosterColumnCatalog.HasOption(window, id)) return false;
            if (prefs.IsVisible(window, id)) return false;
            prefs.SetVisible(window, id, true);
            return true;
        }

        /// <summary>Generic value text for an active filter.</summary>
        public static string DescribeFilter(PawnRosterViewState view, string key)
        {
            if (view == null || key == null) return "";
            switch (key)
            {
                case PawnRosterFilterKeys.Type:
                {
                    var f = (PlayerPawnTypeFilter)view.GetInt(key);
                    return f == PlayerPawnTypeFilter.All
                        ? ""
                        : PlayerPawnRosterUtility.GetPawnTypeLabel(PlayerPawnRosterUtility.ToSortCategory(f));
                }
                case PawnRosterFilterKeys.Star:
                    return UsesPlayerStarFilter(view.Window)
                        ? PlayerPawnRosterUtility.StarFilterLabel((PlayerPawnStarFilter)view.GetInt(key))
                        : PlayerPawnRosterUtility.OutpostStarFilterLabel((OutpostPawnStarFilter)view.GetInt(key));
                case PawnRosterFilterKeys.New:
                    return PlayerPawnRosterUtility.JoinedFilterLabel((PawnRosterJoinedFilter)view.GetInt(key));
                case PawnRosterFilterKeys.Source:
                    return PrisonerRosterUtility.SourceFilterLabel((PrisonerRosterSourceFilter)view.GetInt(key));
                case PawnRosterFilterKeys.LocationType:
                    return LocationTypeLabel(view.GetString(key));
                case PawnRosterFilterKeys.Xenotype:
                {
                    string v = view.GetString(key);
                    XenotypeDef def = DefDatabase<XenotypeDef>.GetNamedSilentFail(v);
                    return def != null ? def.LabelCap.ToString() : v;
                }
                case PawnRosterFilterKeys.Psycasts:
                {
                    string v = view.GetString(key);
                    if (v == PawnRosterHeaderFilter.PsycastFilterNone) return "NoneBrackets".Translate();
                    AbilityDef def = DefDatabase<AbilityDef>.GetNamedSilentFail(v);
                    return def != null ? def.LabelCap.ToString() : v;
                }
                case PawnRosterFilterKeys.Ideology:
                {
                    string v = view.GetString(key);
                    if (v == PawnRosterHeaderFilter.IdeoFilterNone) return "NoneBrackets".Translate();
                    List<Ideo> ideos = Find.IdeoManager?.IdeosListForReading;
                    if (ideos != null)
                    {
                        for (int i = 0; i < ideos.Count; i++)
                        {
                            if (ideos[i] != null && ideos[i].id.ToString() == v)
                                return ideos[i].name;
                        }
                    }
                    return v;
                }
                default:
                    return view.GetString(key);
            }
        }

        /// <summary>
        /// APP / REM store <see cref="PlayerPawnStarFilter"/> (anywhere + colony variants). Every other window
        /// stores the 0..2 subset, which matches <see cref="OutpostPawnStarFilter"/>.
        /// </summary>
        public static bool UsesPlayerStarFilter(PawnRosterColumnWindow window) =>
            window == PawnRosterColumnWindow.AllPlayerPawns || window == PawnRosterColumnWindow.RemoteEstablish;

        public static string LocationTypeLabel(string v)
        {
            switch (v)
            {
                case PawnRosterHeaderFilter.LocationTypeColony: return "TSA_WD_AllPlayerPawns_LocColony".Translate();
                case PawnRosterHeaderFilter.LocationTypeOutpost: return "TSA_WD_AllPlayerPawns_LocOutpost".Translate();
                case PawnRosterHeaderFilter.LocationTypeCamp: return "TSA_WD_AllPlayerPawns_LocCamp".Translate();
                case PawnRosterHeaderFilter.LocationTypeCaravan: return "TSA_WD_AllPlayerPawns_LocCaravan".Translate();
                case PawnRosterHeaderFilter.LocationTypePhysicalMap: return "TSA_WD_AllPlayerPawns_LocPhysicalMap".Translate();
                default: return v ?? "";
            }
        }

        /// <summary>
        /// Common column filters for surfaces that do not have their own header filters
        /// (WDR / DEP / SMS / CAR): name, type, star, join stamp, bio DLC, traits.
        /// Each filter applies only while its column is visible, like the catalog windows.
        /// </summary>
        public static bool PassesCommonFilters(PawnRosterViewState view, PawnRosterRowData d, bool checkName = true)
        {
            if (view == null || d == null) return true;
            PawnRosterColumnWindow w = view.Window;
            string name = checkName ? view.GetString(PawnRosterFilterKeys.Name) : null;
            if (!name.NullOrEmpty()
                && (d.nameLabel == null || d.nameLabel.IndexOf(name, StringComparison.OrdinalIgnoreCase) < 0))
                return false;
            if (PlayerPawnRosterUtility.ColVisible(w, PawnRosterColumnIds.Type))
            {
                var type = (PlayerPawnTypeFilter)view.GetInt(PawnRosterFilterKeys.Type);
                if (type != PlayerPawnTypeFilter.All && d.category != PlayerPawnRosterUtility.ToSortCategory(type))
                    return false;
            }
            if (PlayerPawnRosterUtility.ColVisible(w, PawnRosterColumnIds.Star))
            {
                int star = view.GetInt(PawnRosterFilterKeys.Star);
                if (star == (int)PlayerPawnStarFilter.StarredAnywhere && !d.isStarred) return false;
                if (star == (int)PlayerPawnStarFilter.NotStarredAnywhere && d.isStarred) return false;
            }
            if (PlayerPawnRosterUtility.ColVisible(w, PawnRosterColumnIds.New)
                && !PlayerPawnRosterUtility.PassesJoinedFilter(
                    d.daysSinceJoin, (PawnRosterJoinedFilter)view.GetInt(PawnRosterFilterKeys.New)))
                return false;
            if (PlayerPawnRosterUtility.ColVisible(w, PawnRosterColumnIds.Xenotype)
                && !PawnRosterTraitFilter.MatchesXenotype(d.pawn, view.GetString(PawnRosterFilterKeys.Xenotype)))
                return false;
            if (PlayerPawnRosterUtility.ColVisible(w, PawnRosterColumnIds.Psycasts)
                && !PawnRosterTraitFilter.MatchesPsycast(d.pawn, view.GetString(PawnRosterFilterKeys.Psycasts)))
                return false;
            if (PlayerPawnRosterUtility.ColVisible(w, PawnRosterColumnIds.Ideology)
                && !PawnRosterTraitFilter.MatchesIdeology(d.pawn, view.GetString(PawnRosterFilterKeys.Ideology)))
                return false;
            if (PawnRosterTraitFilter.FilterApplies(w) && !PawnRosterTraitFilter.Matches(d.pawn))
                return false;
            return true;
        }

        /// <summary>Filter keys handled by <see cref="PassesCommonFilters"/>.</summary>
        public static bool IsCommonFilterKey(string key) =>
            key == PawnRosterFilterKeys.Name
            || key == PawnRosterFilterKeys.Type
            || key == PawnRosterFilterKeys.Star
            || key == PawnRosterFilterKeys.New
            || key == PawnRosterFilterKeys.Xenotype
            || key == PawnRosterFilterKeys.Psycasts
            || key == PawnRosterFilterKeys.Ideology;
    }
}
