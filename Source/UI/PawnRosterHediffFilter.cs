using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    public enum PawnRosterHediffFilterMode
    {
        Or = 0,
        And = 1
    }

    public struct PawnRosterHediffRow
    {
        public string Key;
        public string Label;
        public int Count;
        public float Percent;
    }

    /// <summary>
    /// Session-only hediff filter shared by roster tables.
    /// Applied only while that window's Hediffs column is visible. Picker rows are pawn-driven only.
    /// </summary>
    public static class PawnRosterHediffFilter
    {
        public const float ColWidth = 128f;
        public const int SnapshotRefreshTicks = 300;

        private static readonly HashSet<string> selectedKeys = new HashSet<string>();
        private static PawnRosterHediffFilterMode mode = PawnRosterHediffFilterMode.Or;

        private static readonly Dictionary<string, int> cachedCounts = new Dictionary<string, int>();
        private static readonly Dictionary<string, string> cachedLabels = new Dictionary<string, string>();
        private static readonly List<PawnRosterHediffRow> cachedRows = new List<PawnRosterHediffRow>();
        private static int cachedTotalHumanlikes;
        private static int cachedSnapshotTick = -99999;
        private static int cachedSnapshotPawnCount = -1;

        public static PawnRosterHediffFilterMode Mode
        {
            get => mode;
            set
            {
                if (mode == value) return;
                mode = value;
                Revision++;
            }
        }

        public static int Revision { get; private set; }

        public static bool IsActive => selectedKeys.Count > 0;

        public static int SelectedCount => selectedKeys.Count;

        public static bool IsSelected(string key) => !key.NullOrEmpty() && selectedKeys.Contains(key);

        public static void SetSelected(string key, bool on)
        {
            if (key.NullOrEmpty()) return;
            bool changed = on ? selectedKeys.Add(key) : selectedKeys.Remove(key);
            if (changed) Revision++;
        }

        public static void CopySelectedKeys(List<string> into)
        {
            into.Clear();
            foreach (string key in selectedKeys)
                into.Add(key);
        }

        public static string DescribeSelection()
        {
            if (selectedKeys.Count == 0) return "";
            var labels = new List<string>(selectedKeys.Count);
            foreach (string key in selectedKeys)
            {
                HediffDef def = DefDatabase<HediffDef>.GetNamedSilentFail(key);
                labels.Add(def != null ? def.LabelCap.ToString() : key);
            }
            string joiner = mode == PawnRosterHediffFilterMode.And ? " + " : " / ";
            return string.Join(joiner, labels);
        }

        public static void Clear()
        {
            if (selectedKeys.Count == 0 && mode == PawnRosterHediffFilterMode.Or) return;
            selectedKeys.Clear();
            mode = PawnRosterHediffFilterMode.Or;
            Revision++;
        }

        public static bool FilterApplies(PawnRosterColumnWindow window) =>
            IsActive && PlayerPawnRosterUtility.ColVisible(window, PawnRosterColumnIds.Hediffs);

        public static bool Matches(Pawn pawn)
        {
            if (selectedKeys.Count == 0) return true;
            if (pawn?.health?.hediffSet?.hediffs == null) return false;

            if (mode == PawnRosterHediffFilterMode.Or)
            {
                foreach (string key in selectedKeys)
                {
                    if (PawnHasKey(pawn, key))
                        return true;
                }
                return false;
            }

            foreach (string key in selectedKeys)
            {
                if (!PawnHasKey(pawn, key))
                    return false;
            }
            return true;
        }

        private static bool PawnHasKey(Pawn pawn, string key)
        {
            if (key.NullOrEmpty()) return false;
            List<Hediff> hediffs = pawn.health?.hediffSet?.hediffs;
            if (hediffs == null) return false;
            for (int i = 0; i < hediffs.Count; i++)
            {
                Hediff h = hediffs[i];
                if (h?.def == null || !h.Visible) continue;
                if (h.def.defName == key)
                    return true;
            }
            return false;
        }

        public static void ApplyToPlayerRows(List<PlayerPawnRosterEntry> rows, PawnRosterColumnWindow window)
        {
            if (rows == null || !FilterApplies(window)) return;
            rows.RemoveAll(e => e?.pawn == null || !Matches(e.pawn));
        }

        public static void ApplyToPrisonerRows(List<PrisonerRosterEntry> rows)
        {
            if (rows == null || !FilterApplies(PawnRosterColumnWindow.Prisoners)) return;
            rows.RemoveAll(e => !e.isGroupHeader && (e.pawn == null || !Matches(e.pawn)));
            for (int i = rows.Count - 1; i >= 0; i--)
            {
                if (!rows[i].isGroupHeader) continue;
                bool hasBody = i + 1 < rows.Count && !rows[i + 1].isGroupHeader;
                if (!hasBody)
                    rows.RemoveAt(i);
            }
        }

        public static void InvalidateRosterCaches()
        {
            Revision++;
            Window_AllPlayerPawns.InvalidateCache();
            Window_Prisoners.InvalidateCache();
            WITab_Outpost_Pawns.InvalidateCache();
            Window_RemoteEstablishPawns.InvalidateCache();
        }

        public static IReadOnlyList<PawnRosterHediffRow> GetSnapshotRows(out int totalHumanlikes)
        {
            EnsureSnapshot();
            totalHumanlikes = cachedTotalHumanlikes;
            return cachedRows;
        }

        public static void EnsureSnapshot(bool force = false)
        {
            int now = Find.TickManager?.TicksGame ?? 0;
            int pawnCount = CountHumanlikesQuick();
            if (!force
                && cachedRows.Count > 0
                && pawnCount == cachedSnapshotPawnCount
                && now - cachedSnapshotTick < SnapshotRefreshTicks)
                return;

            cachedSnapshotTick = now;
            cachedSnapshotPawnCount = pawnCount;
            RebuildSnapshot();
        }

        private static int CountHumanlikesQuick()
        {
            int n = 0;
            var seen = new HashSet<string>();
            ForEachPlayerHumanlike(p =>
            {
                string id = p.ThingID;
                if (id.NullOrEmpty() || !seen.Add(id)) return;
                n++;
            });
            return n;
        }

        private static void RebuildSnapshot()
        {
            cachedCounts.Clear();
            cachedLabels.Clear();
            cachedRows.Clear();
            cachedTotalHumanlikes = 0;
            var seen = new HashSet<string>();
            ForEachPlayerHumanlike(p =>
            {
                string id = p.ThingID;
                if (id.NullOrEmpty() || !seen.Add(id)) return;
                cachedTotalHumanlikes++;
                List<Hediff> hediffs = p.health?.hediffSet?.hediffs;
                if (hediffs == null) return;
                var seenDefs = new HashSet<string>();
                for (int i = 0; i < hediffs.Count; i++)
                {
                    Hediff h = hediffs[i];
                    if (h?.def == null || !h.Visible) continue;
                    string key = h.def.defName;
                    if (key.NullOrEmpty() || !seenDefs.Add(key)) continue;
                    cachedCounts.TryGetValue(key, out int c);
                    cachedCounts[key] = c + 1;
                    if (!cachedLabels.ContainsKey(key))
                    {
                        string label = h.LabelCap;
                        if (label.NullOrEmpty())
                            label = h.def.LabelCap;
                        cachedLabels[key] = label.NullOrEmpty() ? key : label;
                    }
                }
            });

            foreach (KeyValuePair<string, int> kv in cachedCounts)
            {
                cachedLabels.TryGetValue(kv.Key, out string label);
                if (label.NullOrEmpty())
                {
                    HediffDef def = DefDatabase<HediffDef>.GetNamedSilentFail(kv.Key);
                    label = def != null ? def.LabelCap.ToString() : kv.Key;
                }
                float pct = cachedTotalHumanlikes > 0 ? (100f * kv.Value / cachedTotalHumanlikes) : 0f;
                cachedRows.Add(new PawnRosterHediffRow
                {
                    Key = kv.Key,
                    Label = label,
                    Count = kv.Value,
                    Percent = pct
                });
            }

            cachedRows.Sort((a, b) =>
            {
                int cmp = b.Count.CompareTo(a.Count);
                if (cmp != 0) return cmp;
                return string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase);
            });
        }

        private static void ForEachPlayerHumanlike(Action<Pawn> fn)
        {
            if (fn == null) return;
            Faction player = Faction.OfPlayerSilentFail;

            List<Map> maps = Find.Maps;
            if (maps != null)
            {
                for (int m = 0; m < maps.Count; m++)
                {
                    Map map = maps[m];
                    var all = map?.mapPawns?.AllPawnsSpawned;
                    if (all == null) continue;
                    for (int i = 0; i < all.Count; i++)
                    {
                        Pawn p = all[i];
                        if (!IsCountableHumanlike(p, player)) continue;
                        fn(p);
                    }
                }
            }

            List<Caravan> caravans = Find.WorldObjects?.Caravans;
            if (caravans != null)
            {
                for (int i = 0; i < caravans.Count; i++)
                {
                    Caravan c = caravans[i];
                    if (c?.pawns == null) continue;
                    if (player != null && c.Faction != player) continue;
                    List<Pawn> pawns = c.PawnsListForReading;
                    if (pawns == null) continue;
                    for (int p = 0; p < pawns.Count; p++)
                    {
                        if (!IsCountableHumanlike(pawns[p], player)) continue;
                        fn(pawns[p]);
                    }
                }
            }

            List<WorldObject> worldObjects = Find.WorldObjects?.AllWorldObjects;
            if (worldObjects == null) return;
            for (int i = 0; i < worldObjects.Count; i++)
            {
                if (worldObjects[i] is not WorldObject_WD_Outpost op || op.Destroyed) continue;
                if (player != null && op.Faction != player) continue;
                AddOutpostPawns(op.Occupants, fn);
                AddOutpostPawns(op.Prisoners, fn);
            }
        }

        private static void AddOutpostPawns(List<Pawn> list, Action<Pawn> fn)
        {
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
            {
                Pawn p = list[i];
                if (p == null || p.Destroyed || p.Dead) continue;
                if (p.RaceProps?.Humanlike != true) continue;
                fn(p);
            }
        }

        private static bool IsCountableHumanlike(Pawn p, Faction player)
        {
            if (p == null || p.Destroyed || p.Dead) return false;
            if (p.RaceProps?.Humanlike != true) return false;
            if (p.IsPrisonerOfColony) return true;
            if (player != null && p.Faction == player) return true;
            return false;
        }

        public static bool DrawHediffsHeader(
            ref float curX,
            float y,
            float width,
            float height,
            string label,
            bool isSorted,
            bool sortAscending,
            TextAnchor labelAnchor,
            Action onSort)
        {
            return PawnRosterHeaderFilter.DrawFilterableHeader(
                ref curX, y, width, height,
                label, isSorted, sortAscending, labelAnchor,
                IsActive,
                "TSA_WD_HediffFilter_ButtonTip".Translate(),
                _ =>
                {
                    PawnRosterHeaderFilter.CloseDropdown();
                    Find.WindowStack.Add(new Dialog_PawnRosterHediffFilter());
                },
                onSort);
        }
    }
}
