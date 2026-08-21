using System;
using System.Collections.Generic;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    public enum PawnRosterTraitFilterMode
    {
        Or = 0,
        And = 1
    }

    public struct PawnRosterTraitDegreeRow
    {
        public string Key;
        public string Label;
        public int Count;
        public float Percent;
    }

    /// <summary>
    /// Session-only trait filter shared by All Player Pawns, Outpost Pawns, and Prisoners.
    /// Applied only while that window's Traits column is visible. Never used inside BuildRoster.
    /// </summary>
    public static class PawnRosterTraitFilter
    {
        public const float ColWidth = 128f;
        public const int SnapshotRefreshTicks = 300;

        private static readonly HashSet<string> selectedKeys = new HashSet<string>();
        private static PawnRosterTraitFilterMode mode = PawnRosterTraitFilterMode.Or;

        private static readonly Dictionary<string, int> cachedCounts = new Dictionary<string, int>();
        private static readonly List<PawnRosterTraitDegreeRow> cachedAllRows = new List<PawnRosterTraitDegreeRow>();
        private static int cachedTotalHumanlikes;
        private static int cachedSnapshotTick = -99999;
        private static int cachedSnapshotPawnCount = -1;

        public static PawnRosterTraitFilterMode Mode
        {
            get => mode;
            set => mode = value;
        }

        public static bool IsActive => selectedKeys.Count > 0;

        public static bool IsSelected(string key) => !key.NullOrEmpty() && selectedKeys.Contains(key);

        public static void SetSelected(string key, bool on)
        {
            if (key.NullOrEmpty()) return;
            if (on) selectedKeys.Add(key);
            else selectedKeys.Remove(key);
        }

        public static void Clear()
        {
            selectedKeys.Clear();
            mode = PawnRosterTraitFilterMode.Or;
        }

        public static bool FilterApplies(PawnRosterColumnWindow window) =>
            IsActive && PlayerPawnRosterUtility.ColVisible(window, PawnRosterColumnIds.Traits);

        public static string KeyFor(TraitDef def, int degree) =>
            (def?.defName ?? "") + "/" + degree.ToString();

        public static bool Matches(Pawn pawn)
        {
            if (selectedKeys.Count == 0) return true;
            if (pawn?.story?.traits == null) return false;

            if (mode == PawnRosterTraitFilterMode.Or)
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
            if (!TryParseKey(key, out TraitDef def, out int degree))
                return false;
            Trait t = pawn.story?.traits?.GetTrait(def);
            return t != null && t.Degree == degree;
        }

        private static bool TryParseKey(string key, out TraitDef def, out int degree)
        {
            def = null;
            degree = 0;
            if (key.NullOrEmpty()) return false;
            int slash = key.LastIndexOf('/');
            if (slash <= 0 || slash >= key.Length - 1) return false;
            def = DefDatabase<TraitDef>.GetNamedSilentFail(key.Substring(0, slash));
            return def != null && int.TryParse(key.Substring(slash + 1), out degree);
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
            Window_AllPlayerPawns.InvalidateCache();
            Window_Prisoners.InvalidateCache();
            WITab_Outpost_Pawns.InvalidateCache();
        }

        public static IReadOnlyList<PawnRosterTraitDegreeRow> GetSnapshotRows(out int totalHumanlikes)
        {
            EnsureSnapshot();
            totalHumanlikes = cachedTotalHumanlikes;
            return cachedAllRows;
        }

        public static void EnsureSnapshot(bool force = false)
        {
            int now = Find.TickManager?.TicksGame ?? 0;
            int pawnCount = CountHumanlikesQuick();
            if (!force
                && cachedAllRows.Count > 0
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
            cachedAllRows.Clear();
            cachedTotalHumanlikes = 0;
            var seen = new HashSet<string>();
            ForEachPlayerHumanlike(p =>
            {
                string id = p.ThingID;
                if (id.NullOrEmpty() || !seen.Add(id)) return;
                cachedTotalHumanlikes++;
                List<Trait> traits = p.story?.traits?.allTraits;
                if (traits == null) return;
                for (int i = 0; i < traits.Count; i++)
                {
                    Trait t = traits[i];
                    if (t?.def == null) continue;
                    string key = KeyFor(t.def, t.Degree);
                    cachedCounts.TryGetValue(key, out int c);
                    cachedCounts[key] = c + 1;
                }
            });

            List<TraitDef> defs = DefDatabase<TraitDef>.AllDefsListForReading;
            for (int i = 0; i < defs.Count; i++)
            {
                TraitDef def = defs[i];
                if (def?.degreeDatas == null) continue;
                for (int d = 0; d < def.degreeDatas.Count; d++)
                {
                    TraitDegreeData data = def.degreeDatas[d];
                    if (data == null) continue;
                    string label = data.LabelCap;
                    if (label.NullOrEmpty()) continue;
                    string key = KeyFor(def, data.degree);
                    cachedCounts.TryGetValue(key, out int count);
                    float pct = cachedTotalHumanlikes > 0 ? (100f * count / cachedTotalHumanlikes) : 0f;
                    cachedAllRows.Add(new PawnRosterTraitDegreeRow
                    {
                        Key = key,
                        Label = label,
                        Count = count,
                        Percent = pct
                    });
                }
            }

            cachedAllRows.Sort((a, b) =>
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
                AddOutpostPawns(op.Occupants, player, fn);
                AddOutpostPawns(op.Prisoners, player, fn);
            }
        }

        private static void AddOutpostPawns(List<Pawn> list, Faction player, Action<Pawn> fn)
        {
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
            {
                Pawn p = list[i];
                if (!IsCountableHumanlike(p, player) && !(p?.RaceProps?.Humanlike == true && !p.Destroyed && !p.Dead))
                    continue;
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

        public static void FormatXenotype(Pawn pawn, out string display, out string tip)
        {
            display = "—";
            tip = "";
            if (!ModsConfig.BiotechActive || pawn?.genes == null) return;
            string label = pawn.genes.XenotypeLabelCap;
            if (label.NullOrEmpty()) return;
            display = label;
            string desc = pawn.genes.Xenotype?.description;
            if (desc.NullOrEmpty() && pawn.genes.UniqueXenotype)
                desc = pawn.genes.xenotypeName;
            tip = desc.NullOrEmpty() ? label : label + "\n" + desc;
        }

        public static void FormatPsycasts(Pawn pawn, out string display, out string tip)
        {
            display = "—";
            tip = "";
            if (!ModsConfig.RoyaltyActive || pawn?.abilities?.abilities == null) return;
            var labels = new List<string>();
            List<Ability> abs = pawn.abilities.abilities;
            for (int i = 0; i < abs.Count; i++)
            {
                Ability a = abs[i];
                if (a?.def == null || !a.def.IsPsycast) continue;
                string label = a.def.LabelCap;
                if (label.NullOrEmpty()) continue;
                labels.Add(label);
            }
            if (labels.Count == 0) return;
            labels.Sort(StringComparer.OrdinalIgnoreCase);

            var tipSb = new StringBuilder();
            for (int i = 0; i < labels.Count; i++)
            {
                if (i > 0) tipSb.AppendLine();
                tipSb.Append(labels[i]);
            }
            tip = tipSb.ToString();
            display = labels.Count <= 3
                ? string.Join("\n", labels)
                : labels[0] + "\n" + labels[1] + "\n…";
        }

        public static int CompareXenotype(Pawn a, Pawn b)
        {
            FormatXenotype(a, out string da, out _);
            FormatXenotype(b, out string db, out _);
            return string.Compare(da, db, StringComparison.OrdinalIgnoreCase);
        }

        public static bool MatchesXenotype(Pawn pawn, string filter)
        {
            if (filter.NullOrEmpty()) return true;
            return pawn?.genes?.Xenotype?.defName == filter;
        }

        public static void ApplyXenotypeToPlayerRows(List<PlayerPawnRosterEntry> rows, string filter)
        {
            if (rows == null || filter.NullOrEmpty()) return;
            rows.RemoveAll(e => !MatchesXenotype(e?.pawn, filter));
        }

        public static void ApplyXenotypeToPrisonerRows(List<PrisonerRosterEntry> rows, string filter)
        {
            if (rows == null || filter.NullOrEmpty()) return;
            rows.RemoveAll(e => !e.isGroupHeader && !MatchesXenotype(e.pawn, filter));
            for (int i = rows.Count - 1; i >= 0; i--)
            {
                if (!rows[i].isGroupHeader) continue;
                bool hasBody = i + 1 < rows.Count && !rows[i + 1].isGroupHeader;
                if (!hasBody)
                    rows.RemoveAt(i);
            }
        }

        public static bool MatchesPsycast(Pawn pawn, string filter)
        {
            if (filter.NullOrEmpty()) return true;
            List<string> keys = PawnRosterHeaderFilter.PsycastKeysOnPawn(pawn);
            if (filter == PawnRosterHeaderFilter.PsycastFilterNone)
                return keys.Count == 0;
            return keys.Contains(filter);
        }

        public static void ApplyPsycastToPlayerRows(List<PlayerPawnRosterEntry> rows, string filter)
        {
            if (rows == null || filter.NullOrEmpty()) return;
            rows.RemoveAll(e => !MatchesPsycast(e?.pawn, filter));
        }

        public static void ApplyPsycastToPrisonerRows(List<PrisonerRosterEntry> rows, string filter)
        {
            if (rows == null || filter.NullOrEmpty()) return;
            rows.RemoveAll(e => !e.isGroupHeader && !MatchesPsycast(e.pawn, filter));
            for (int i = rows.Count - 1; i >= 0; i--)
            {
                if (!rows[i].isGroupHeader) continue;
                bool hasBody = i + 1 < rows.Count && !rows[i + 1].isGroupHeader;
                if (!hasBody)
                    rows.RemoveAt(i);
            }
        }

        public static int ComparePsycasts(Pawn a, Pawn b)
        {
            FormatPsycasts(a, out _, out string ta);
            FormatPsycasts(b, out _, out string tb);
            int cmp = string.Compare(ta, tb, StringComparison.OrdinalIgnoreCase);
            if (cmp != 0) return cmp;
            FormatPsycasts(a, out string da, out _);
            FormatPsycasts(b, out string db, out _);
            return string.Compare(da, db, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Traits header: sort on the label, filter icon glued to the title. Returns true if the filter button was clicked.</summary>
        public static bool DrawTraitsHeader(
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
                "TSA_WD_TraitFilter_ButtonTip".Translate(),
                _ =>
                {
                    PawnRosterHeaderFilter.CloseDropdown();
                    Find.WindowStack.Add(new Dialog_PawnRosterTraitFilter());
                },
                onSort);
        }
    }
}
