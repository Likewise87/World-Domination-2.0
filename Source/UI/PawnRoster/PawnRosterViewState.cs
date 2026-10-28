#nullable disable
using System.Collections.Generic;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Canonical filter keys shared by all roster windows and presets.</summary>
    public static class PawnRosterFilterKeys
    {
        public const string Name = "Name";
        /// <summary>int: <see cref="PlayerPawnTypeFilter"/>.</summary>
        public const string Type = "Type";
        /// <summary>int: <see cref="PlayerPawnStarFilter"/> (OUT / ARM use 0-2 only).</summary>
        public const string Star = "Star";
        /// <summary>int: <see cref="PawnRosterJoinedFilter"/>.</summary>
        public const string New = "New";
        public const string LocationType = "LocationType";
        /// <summary>Save-specific label; in another save it simply matches nothing.</summary>
        public const string LocationName = "LocationName";
        /// <summary>int: <see cref="PrisonerRosterSourceFilter"/>.</summary>
        public const string Source = "Source";
        public const string Xenotype = "Xenotype";
        public const string Psycasts = "Psycasts";
        public const string Ideology = "Ideology";
        /// <summary>Smart Send: origin label text.</summary>
        public const string Origin = "Origin";
        /// <summary>Smart Send: destination label text (save-specific).</summary>
        public const string Destination = "Dest";
        /// <summary>Smart Send: reason text.</summary>
        public const string Reason = "Reason";
        /// <summary>Pseudo key for the global trait filter (lives in <see cref="PawnRosterTraitFilter"/>).</summary>
        public const string Traits = "Traits";

        public static readonly string[] All =
        {
            Name, Type, Star, New, LocationType, LocationName, Source, Xenotype, Psycasts, Ideology,
            Origin, Destination, Reason
        };

        public static bool IsIntKey(string key) =>
            key == Type || key == Star || key == New || key == Source;

        /// <summary>Column that must be visible for the filter to apply, or null for always-on columns.</summary>
        public static string ColumnFor(string key)
        {
            switch (key)
            {
                case Type: return PawnRosterColumnIds.Type;
                case Star: return PawnRosterColumnIds.Star;
                case New: return PawnRosterColumnIds.New;
                case Xenotype: return PawnRosterColumnIds.Xenotype;
                case Psycasts: return PawnRosterColumnIds.Psycasts;
                case Ideology: return PawnRosterColumnIds.Ideology;
                case Traits: return PawnRosterColumnIds.Traits;
                default: return null;
            }
        }

        public static string LabelKey(string key)
        {
            switch (key)
            {
                case Name: return "TSA_WD_PawnCol_PawnName";
                case Type: return "TSA_WD_AllPlayerPawns_ColPawnType";
                case Star: return "TSA_WD_AllPlayerPawns_ColStar";
                case New: return "TSA_WD_PawnRoster_ColNew";
                case LocationType: return "TSA_WD_AllPlayerPawns_ColLocationType";
                case LocationName: return "TSA_WD_AllPlayerPawns_ColLocationName";
                case Source: return "TSA_WD_AdvFilter_KeySource";
                case Xenotype: return "TSA_WD_PawnRoster_ColXenotype";
                case Psycasts: return "TSA_WD_PawnRoster_ColPsycasts";
                case Ideology: return "TSA_WD_PawnRoster_ColIdeology";
                case Origin: return "TSA_WD_SmartSend_ColOrigin";
                case Destination: return "TSA_WD_SmartSend_ColDestination";
                case Reason: return "TSA_WD_SmartSend_ColReason";
                case Traits: return "TSA_WD_Prisoners_ColTraits";
                default: return null;
            }
        }
    }

    /// <summary>
    /// Filters + sort chain for one roster window. Dialog filter fields are thin properties over this.
    /// </summary>
    public sealed class PawnRosterViewState
    {
        public readonly PawnRosterColumnWindow Window;
        public readonly PawnRosterSortChain Sort = new PawnRosterSortChain();

        private readonly Dictionary<string, string> strings = new Dictionary<string, string>();
        private readonly Dictionary<string, int> ints = new Dictionary<string, int>();
        private int filterRevision;

        public PawnRosterViewState(PawnRosterColumnWindow window)
        {
            Window = window;
        }

        /// <summary>Bumps on any filter or sort change (cheap dirty check for row caches).</summary>
        public int Revision => filterRevision + Sort.Revision * 7919;

        public string GetString(string key) =>
            key != null && strings.TryGetValue(key, out string v) ? v : "";

        public void SetString(string key, string value)
        {
            if (key == null) return;
            if (value.NullOrEmpty())
            {
                if (strings.Remove(key)) filterRevision++;
                return;
            }
            if (strings.TryGetValue(key, out string old) && old == value) return;
            strings[key] = value;
            filterRevision++;
        }

        public int GetInt(string key) =>
            key != null && ints.TryGetValue(key, out int v) ? v : 0;

        public void SetInt(string key, int value)
        {
            if (key == null) return;
            if (value == 0)
            {
                if (ints.Remove(key)) filterRevision++;
                return;
            }
            if (ints.TryGetValue(key, out int old) && old == value) return;
            ints[key] = value;
            filterRevision++;
        }

        public bool HasFilter(string key) =>
            key != null && (strings.ContainsKey(key) || ints.ContainsKey(key));

        public bool HasActiveFilters => strings.Count > 0 || ints.Count > 0;

        public void ClearFilter(string key)
        {
            if (key == null) return;
            bool removed = strings.Remove(key) | ints.Remove(key);
            if (removed) filterRevision++;
        }

        public void ClearFilters()
        {
            if (strings.Count == 0 && ints.Count == 0) return;
            strings.Clear();
            ints.Clear();
            filterRevision++;
        }

        /// <summary>
        /// Window's opening sort (e.g. Strength high-first). <see cref="Reset"/> restores it and the
        /// Advanced Filtering tint ignores it.
        /// </summary>
        public readonly PawnRosterSortChain DefaultSort = new PawnRosterSortChain();

        public bool SortIsDefault => Sort.SameAs(DefaultSort);

        /// <summary>Sets the default and applies it when nothing is sorted yet.</summary>
        public void SetDefaultSort(string id, bool ascending)
        {
            DefaultSort.Set(id, ascending);
            if (Sort.IsEmpty)
                Sort.CopyFrom(DefaultSort);
        }

        public void Reset()
        {
            ClearFilters();
            if (DefaultSort.IsEmpty)
                Sort.Clear();
            else
                Sort.CopyFrom(DefaultSort);
        }

        /// <summary>Active filter keys in canonical order.</summary>
        public void CollectActiveKeys(List<string> into)
        {
            into.Clear();
            string[] all = PawnRosterFilterKeys.All;
            for (int i = 0; i < all.Length; i++)
            {
                if (HasFilter(all[i]))
                    into.Add(all[i]);
            }
        }

        /// <summary>Raw string form (ints as decimal) for presets.</summary>
        public void ExportFilters(Dictionary<string, string> into)
        {
            into.Clear();
            foreach (KeyValuePair<string, string> kv in strings)
                into[kv.Key] = kv.Value;
            foreach (KeyValuePair<string, int> kv in ints)
                into[kv.Key] = kv.Value.ToString();
        }

        public void ImportFilter(string key, string raw)
        {
            if (PawnRosterFilterKeys.IsIntKey(key))
            {
                if (int.TryParse(raw, out int v))
                    SetInt(key, v);
            }
            else
            {
                SetString(key, raw);
            }
        }
    }

    /// <summary>Holds the static (survives close) view states; other windows get a fresh one per open.</summary>
    public static class PawnRosterViewStateStore
    {
        private static readonly Dictionary<PawnRosterColumnWindow, PawnRosterViewState> persistent =
            new Dictionary<PawnRosterColumnWindow, PawnRosterViewState>();

        public static PawnRosterViewState For(PawnRosterColumnWindow window)
        {
            if (!PawnRosterSurface.ViewStatePersists(window))
                return new PawnRosterViewState(window);
            if (!persistent.TryGetValue(window, out PawnRosterViewState state))
            {
                state = new PawnRosterViewState(window);
                persistent[window] = state;
            }
            return state;
        }
    }

    public struct PawnRosterSortOption
    {
        public string Id;
        public string Label;

        public PawnRosterSortOption(string id, string label)
        {
            Id = id;
            Label = label;
        }
    }

    /// <summary>Implemented by every roster window so the Advanced Filtering dialog can edit its view live.</summary>
    public interface IPawnRosterViewHost
    {
        PawnRosterColumnWindow RosterWindow { get; }
        PawnRosterViewState RosterView { get; }
        /// <summary>False once the window / tab is gone; the dialog then closes itself.</summary>
        bool RosterHostOpen { get; }
        /// <summary>Sortable columns allowed in this window (visible or not), in display order.</summary>
        void CollectRosterSortOptions(List<PawnRosterSortOption> into);
        bool SupportsRosterFilter(string key);
        /// <summary>Human-readable value for an active filter (Active filters list).</summary>
        string DescribeRosterFilter(string key);
        /// <summary>Called after the dialog changed filters, sort, or columns.</summary>
        void OnRosterViewChanged();
        /// <summary>Unfiltered pawns of this table (filter choice lists and counts). Called on demand only.</summary>
        void CollectRosterPawns(List<Pawn> into);
    }
}
