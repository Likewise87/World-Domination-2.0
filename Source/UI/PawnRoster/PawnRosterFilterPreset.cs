#nullable disable
using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Saved filter + sort setup shared by every roster window (mod settings, not per save).
    /// Criteria a window cannot use are ignored when applied there.
    /// </summary>
    public class PawnRosterFilterPreset : IExposable
    {
        public string name = "";
        /// <summary>Canonical filter key to raw value (ints as decimal).</summary>
        public Dictionary<string, string> filters = new Dictionary<string, string>();
        public List<string> traitKeys = new List<string>();
        public PawnRosterTraitFilterMode traitMode = PawnRosterTraitFilterMode.Or;
        /// <summary>Encoded "canonicalId|1" sort levels.</summary>
        public List<string> sortLevels = new List<string>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref name, "name", "");
            Scribe_Collections.Look(ref filters, "filters", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref traitKeys, "traitKeys", LookMode.Value);
            Scribe_Values.Look(ref traitMode, "traitMode", PawnRosterTraitFilterMode.Or);
            Scribe_Collections.Look(ref sortLevels, "sortLevels", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.LoadingVars || Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                name ??= "";
                filters ??= new Dictionary<string, string>();
                traitKeys ??= new List<string>();
                sortLevels ??= new List<string>();
            }
        }
    }

    /// <summary>Capture / apply / rename / delete for <see cref="PawnRosterFilterPreset"/>; every mutation writes settings.</summary>
    public static class PawnRosterFilterPresets
    {
        private static readonly List<PawnRosterFilterPreset> Empty = new List<PawnRosterFilterPreset>();
        private static readonly List<PawnRosterSortOption> optionScratch = new List<PawnRosterSortOption>();

        public static List<PawnRosterFilterPreset> All
        {
            get
            {
                WorldDominationSettings s = WorldDominationMod.settings;
                if (s == null) return Empty;
                s.pawnRosterFilterPresets ??= new List<PawnRosterFilterPreset>();
                return s.pawnRosterFilterPresets;
            }
        }

        public static PawnRosterFilterPreset Find(string name)
        {
            if (name.NullOrEmpty()) return null;
            List<PawnRosterFilterPreset> all = All;
            for (int i = 0; i < all.Count; i++)
            {
                if (string.Equals(all[i]?.name, name, StringComparison.OrdinalIgnoreCase))
                    return all[i];
            }
            return null;
        }

        public static PawnRosterFilterPreset SaveNew(IPawnRosterViewHost host, string name)
        {
            name = name?.Trim();
            if (host == null || name.NullOrEmpty() || Find(name) != null || WorldDominationMod.settings == null)
                return null;
            var preset = new PawnRosterFilterPreset { name = name };
            CaptureInto(preset, host);
            All.Add(preset);
            Persist();
            return preset;
        }

        public static void Overwrite(PawnRosterFilterPreset preset, IPawnRosterViewHost host)
        {
            if (preset == null || host == null) return;
            CaptureInto(preset, host);
            Persist();
        }

        public static bool Rename(PawnRosterFilterPreset preset, string newName)
        {
            newName = newName?.Trim();
            if (preset == null || newName.NullOrEmpty()) return false;
            PawnRosterFilterPreset clash = Find(newName);
            if (clash != null && clash != preset) return false;
            preset.name = newName;
            Persist();
            return true;
        }

        public static void Delete(PawnRosterFilterPreset preset)
        {
            if (preset == null) return;
            if (All.Remove(preset))
                Persist();
        }

        private static void Persist() => WorldDominationMod.settings?.Write();

        private static void CaptureInto(PawnRosterFilterPreset preset, IPawnRosterViewHost host)
        {
            PawnRosterViewState view = host.RosterView;
            view.ExportFilters(preset.filters);
            PawnRosterTraitFilter.CopySelectedKeys(preset.traitKeys);
            preset.traitMode = PawnRosterTraitFilter.Mode;
            preset.sortLevels.Clear();
            for (int i = 0; i < view.Sort.Count; i++)
            {
                PawnRosterSortLevel level = view.Sort[i];
                preset.sortLevels.Add(
                    PawnRosterSortIds.ToCanonical(host.RosterWindow, level.Id) + "|" + (level.Ascending ? "1" : "0"));
            }
        }

        /// <summary>Replaces the host's filters / sort / trait filter with the preset (inapplicable parts skipped).</summary>
        public static void Apply(PawnRosterFilterPreset preset, IPawnRosterViewHost host)
        {
            if (preset == null || host == null) return;
            PawnRosterColumnWindow window = host.RosterWindow;
            PawnRosterViewState view = host.RosterView;
            view.ClearFilters();
            foreach (KeyValuePair<string, string> kv in preset.filters)
            {
                if (!host.SupportsRosterFilter(kv.Key)) continue;
                if (kv.Key == PawnRosterFilterKeys.Star
                    && PawnRosterSortIds.UsesOutpostStarFilter(window)
                    && int.TryParse(kv.Value, out int star)
                    && star > (int)OutpostPawnStarFilter.NotStarred)
                    continue;
                view.ImportFilter(kv.Key, kv.Value);
            }

            optionScratch.Clear();
            host.CollectRosterSortOptions(optionScratch);
            view.Sort.Clear();
            for (int i = 0; i < preset.sortLevels.Count; i++)
            {
                if (!PawnRosterSortChain.TryDecode(preset.sortLevels[i], out PawnRosterSortLevel level)) continue;
                string id = PawnRosterSortIds.FromCanonical(window, level.Id);
                if (!ContainsOption(optionScratch, id)) continue;
                view.Sort.Add(id, level.Ascending);
            }

            if (host.SupportsRosterFilter(PawnRosterFilterKeys.Traits))
            {
                PawnRosterTraitFilter.Clear();
                PawnRosterTraitFilter.Mode = preset.traitMode;
                for (int i = 0; i < preset.traitKeys.Count; i++)
                    PawnRosterTraitFilter.SetSelected(preset.traitKeys[i], true);
            }

            PawnRosterViewUtil.EnsureColumnsForView(host);
            PawnRosterTraitFilter.InvalidateRosterCaches();
            host.OnRosterViewChanged();
        }

        private static bool ContainsOption(List<PawnRosterSortOption> options, string id)
        {
            for (int i = 0; i < options.Count; i++)
            {
                if (options[i].Id == id) return true;
            }
            return false;
        }
    }

    /// <summary>Maps window-local sort ids to the canonical ids stored in presets.</summary>
    public static class PawnRosterSortIds
    {
        private const string FullSkillPrefix = "Skill_";

        public static bool UsesOutpostStarFilter(PawnRosterColumnWindow window) =>
            window == PawnRosterColumnWindow.OutpostPawns || window == PawnRosterColumnWindow.OutpostArmory;

        public static string ToCanonical(PawnRosterColumnWindow window, string id)
        {
            if (id == null) return "";
            if (window == PawnRosterColumnWindow.OutpostPawns && id.StartsWith(FullSkillPrefix))
                return id.Substring(FullSkillPrefix.Length);
            return id;
        }

        public static string FromCanonical(PawnRosterColumnWindow window, string id)
        {
            if (id == null) return "";
            if (window != PawnRosterColumnWindow.OutpostPawns) return id;
            if (id == PawnRosterColumnIds.Shooting || id == PawnRosterColumnIds.Melee || id == PawnRosterColumnIds.Construction)
                return id;
            SkillDef skill = DefDatabase<SkillDef>.GetNamedSilentFail(id);
            return skill != null ? PawnRosterColumnIds.FullSkill(skill) : id;
        }
    }
}
