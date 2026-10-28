#nullable disable
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    public struct PawnRosterSortLevel
    {
        public string Id;
        public bool Ascending;

        public PawnRosterSortLevel(string id, bool ascending)
        {
            Id = id;
            Ascending = ascending;
        }
    }

    /// <summary>
    /// Ordered sort levels (max <see cref="MaxLevels"/>). Empty = the window's default ordering.
    /// Header clicks set a single level; extra levels come from the Advanced Filtering dialog.
    /// Final tiebreak is always Name.
    /// </summary>
    public sealed class PawnRosterSortChain
    {
        public const int MaxLevels = 4;

        private readonly List<PawnRosterSortLevel> levels = new List<PawnRosterSortLevel>(MaxLevels);

        public int Count => levels.Count;
        public PawnRosterSortLevel this[int index] => levels[index];
        public int Revision { get; private set; }
        public bool IsEmpty => levels.Count == 0;
        public bool IsMultiLevel => levels.Count > 1;

        public string PrimaryOr(string fallback) => levels.Count > 0 ? levels[0].Id : fallback;
        public bool PrimaryAscendingOr(bool fallback) => levels.Count > 0 ? levels[0].Ascending : fallback;

        public int IndexOf(string id)
        {
            if (id == null) return -1;
            for (int i = 0; i < levels.Count; i++)
            {
                if (levels[i].Id == id) return i;
            }
            return -1;
        }

        /// <summary>1-based level, or 0 when <paramref name="id"/> is not in the chain.</summary>
        public int LevelOf(string id) => IndexOf(id) + 1;
        public bool IsSorted(string id) => IndexOf(id) >= 0;

        public bool IsAscending(string id)
        {
            int i = IndexOf(id);
            return i < 0 || levels[i].Ascending;
        }

        /// <summary>
        /// For <see cref="PawnRosterHeaderFilter.DrawFilterableHeader"/>'s isSorted argument: also queues
        /// the level badge for that header when the chain has more than one level.
        /// </summary>
        public bool HeaderSorted(string id)
        {
            int level = LevelOf(id);
            PawnRosterHeaderFilter.SetPendingSortLevel(levels.Count > 1 ? level : 0);
            return level > 0;
        }

        /// <summary>Arrow suffix (with level number when multi-level) for headers that draw their own label.</summary>
        public string ArrowFor(string id)
        {
            int idx = IndexOf(id);
            if (idx < 0) return "";
            return PawnRosterHeaderFilter.SortArrowText(levels[idx].Ascending, levels.Count > 1 ? idx + 1 : 0);
        }

        /// <summary>
        /// Header click: flips the primary, or replaces the chain with this column.
        /// Multi-level chains are built in <see cref="Dialog_PawnRosterAdvancedFilter"/> only.
        /// </summary>
        public void ClickFromHeader(string id, bool firstAscending = true)
        {
            if (id.NullOrEmpty()) return;
            if (IndexOf(id) == 0)
            {
                levels[0] = new PawnRosterSortLevel(id, !levels[0].Ascending);
            }
            else
            {
                levels.Clear();
                levels.Add(new PawnRosterSortLevel(id, firstAscending));
            }
            Revision++;
        }

        public void Set(string id, bool ascending)
        {
            levels.Clear();
            if (!id.NullOrEmpty())
                levels.Add(new PawnRosterSortLevel(id, ascending));
            Revision++;
        }

        public bool Add(string id, bool ascending)
        {
            if (id.NullOrEmpty() || levels.Count >= MaxLevels || IndexOf(id) >= 0) return false;
            levels.Add(new PawnRosterSortLevel(id, ascending));
            Revision++;
            return true;
        }

        public void SetLevel(int index, string id, bool ascending)
        {
            if (index < 0 || index >= levels.Count || id.NullOrEmpty()) return;
            int existing = IndexOf(id);
            if (existing >= 0 && existing != index)
                levels.RemoveAt(existing);
            if (index >= levels.Count) index = levels.Count - 1;
            if (index < 0) return;
            levels[index] = new PawnRosterSortLevel(id, ascending);
            Revision++;
        }

        public void RemoveAt(int index)
        {
            if (index < 0 || index >= levels.Count) return;
            levels.RemoveAt(index);
            Revision++;
        }

        public void Move(int index, int delta)
        {
            int target = index + delta;
            if (index < 0 || index >= levels.Count || target < 0 || target >= levels.Count) return;
            PawnRosterSortLevel tmp = levels[index];
            levels[index] = levels[target];
            levels[target] = tmp;
            Revision++;
        }

        /// <summary>Drops levels whose id matches <paramref name="drop"/>. Returns true if anything changed.</summary>
        public bool RemoveWhere(Func<string, bool> drop)
        {
            if (drop == null) return false;
            int removed = 0;
            for (int i = levels.Count - 1; i >= 0; i--)
            {
                if (!drop(levels[i].Id)) continue;
                levels.RemoveAt(i);
                removed++;
            }
            if (removed > 0) Revision++;
            return removed > 0;
        }

        public void Clear()
        {
            if (levels.Count == 0) return;
            levels.Clear();
            Revision++;
        }

        public void CopyFrom(PawnRosterSortChain other)
        {
            levels.Clear();
            if (other != null)
            {
                for (int i = 0; i < other.levels.Count && i < MaxLevels; i++)
                    levels.Add(other.levels[i]);
            }
            Revision++;
        }

        public bool SameAs(PawnRosterSortChain other)
        {
            int otherCount = other?.levels.Count ?? 0;
            if (otherCount != levels.Count) return false;
            for (int i = 0; i < levels.Count; i++)
            {
                if (levels[i].Id != other.levels[i].Id || levels[i].Ascending != other.levels[i].Ascending)
                    return false;
            }
            return true;
        }

        /// <summary>"id|1" (ascending) / "id|0" entries for Scribe.</summary>
        public void EncodeTo(List<string> into)
        {
            into.Clear();
            for (int i = 0; i < levels.Count; i++)
                into.Add(levels[i].Id + "|" + (levels[i].Ascending ? "1" : "0"));
        }

        public static bool TryDecode(string encoded, out PawnRosterSortLevel level)
        {
            level = default;
            if (encoded.NullOrEmpty()) return false;
            int bar = encoded.LastIndexOf('|');
            if (bar <= 0) return false;
            level = new PawnRosterSortLevel(encoded.Substring(0, bar), encoded.Substring(bar + 1) != "0");
            return true;
        }

        /// <summary>
        /// Compares through every level, then by <paramref name="name"/>. <paramref name="compare"/> returns
        /// the ascending comparison for one column id.
        /// </summary>
        public int Compare<T>(T a, T b, Func<T, T, string, int> compare, Func<T, string> name)
        {
            for (int i = 0; i < levels.Count; i++)
            {
                int cmp = levels[i].Id == PawnRosterColumnIds.Name
                    ? string.Compare(name(a) ?? "", name(b) ?? "", StringComparison.OrdinalIgnoreCase)
                    : compare(a, b, levels[i].Id);
                if (cmp != 0)
                    return levels[i].Ascending ? cmp : -cmp;
            }
            return string.Compare(name(a) ?? "", name(b) ?? "", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Stable multi-level sort of <paramref name="rows"/> in place.</summary>
        public void Sort<T>(List<T> rows, Func<T, T, string, int> compare, Func<T, string> name)
        {
            if (rows == null || rows.Count < 2) return;
            var indexed = new List<KeyValuePair<int, T>>(rows.Count);
            for (int i = 0; i < rows.Count; i++)
                indexed.Add(new KeyValuePair<int, T>(i, rows[i]));
            indexed.Sort((x, y) =>
            {
                int cmp = Compare(x.Value, y.Value, compare, name);
                return cmp != 0 ? cmp : x.Key.CompareTo(y.Key);
            });
            for (int i = 0; i < rows.Count; i++)
                rows[i] = indexed[i].Value;
        }
    }
}
