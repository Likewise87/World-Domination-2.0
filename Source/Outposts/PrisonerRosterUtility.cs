using System;
using System.Collections.Generic;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    public enum PrisonerRosterSourceFilter
    {
        All,
        Colony,
        Outpost
    }

    public class PrisonerRosterEntry
    {
        public Pawn pawn;
        public string thingId = "";
        public string nameLabel = "";
        public string interactionLabel = "";
        public PrisonerInteractionModeDef interactionMode;
        public bool recruitable = true;
        public float resistance;
        public string resistanceLabel = "";
        public string resistanceTip = "";
        public List<string> traitLabels = new List<string>();
        public string traitsDisplay = "";
        public string traitsTip = "";
        public int[] skillLevels = Array.Empty<int>();
        public int ageYears;
        public MapParent mapParent;
        public WorldObject_WD_Outpost holdingOutpost;
        public bool isOutpostPrisoner;
        public bool isBeingRecruited;
        public bool isGroupHeader;
        public string groupHeaderLabel = "";
        public string locationLabel = "";
        public Texture2D locationIcon;
        public Color locationIconColor = Color.white;
        public WorldObject locationJumpTarget;
        public int scheduledDestId = -1;
        public string scheduledDestLabel = "";
        public Texture2D scheduledDestIcon;
        public Color scheduledDestIconColor = Color.white;
        /// <summary>True when destination comes from schedule; false when showing home colony/outpost as default.</summary>
        public bool hasExplicitSchedule;
        /// <summary>Index in the holding outpost's prisoner queue (recruit priority). Colony rows leave this at -1.</summary>
        public int prisonerQueueIndex = -1;
        public int prisonerQueueCount;
    }

    public static class PrisonerRosterUtility
    {
        /// <summary>All prisoner thing IDs from the last <see cref="BuildRoster"/> scan (pre filter).</summary>
        private static readonly HashSet<string> IndexedThingIds = new HashSet<string>();

        public const string DefaultSortColumn = "Name";

        public static List<PrisonerRosterEntry> BuildRoster(
            string nameSearchLower,
            string sortColumn,
            bool sortAscending,
            PrisonerRosterSourceFilter sourceFilter)
        {
            var colonyRows = new List<PrisonerRosterEntry>();
            var outpostRows = new List<PrisonerRosterEntry>();
            Faction player = Faction.OfPlayer;
            IndexedThingIds.Clear();
            if (player == null) return new List<PrisonerRosterEntry>();

            var schedule = WorldComponent_PrisonerRecruitSchedule.Get();

            // Always scan both sources so selection prune can keep IDs hidden by source/name filters.
            CollectColonyPrisoners(colonyRows, schedule, player);
            CollectOutpostPrisoners(outpostRows, schedule, player);

            for (int i = 0; i < colonyRows.Count; i++)
            {
                string tid = colonyRows[i].thingId;
                if (!string.IsNullOrEmpty(tid)) IndexedThingIds.Add(tid);
            }
            for (int i = 0; i < outpostRows.Count; i++)
            {
                string tid = outpostRows[i].thingId;
                if (!string.IsNullOrEmpty(tid)) IndexedThingIds.Add(tid);
            }

            if (!string.IsNullOrEmpty(nameSearchLower))
            {
                colonyRows.RemoveAll(e => e.nameLabel == null
                    || !e.nameLabel.ToLowerInvariant().Contains(nameSearchLower));
                outpostRows.RemoveAll(e => e.nameLabel == null
                    || !e.nameLabel.ToLowerInvariant().Contains(nameSearchLower));
            }

            SortRows(colonyRows, sortColumn, sortAscending);
            // Default Name sort keeps outpost queue order so recruit priority stays visible.
            if (sortColumn == DefaultSortColumn && sortAscending)
                SortOutpostRowsByQueue(outpostRows);
            else
                SortRows(outpostRows, sortColumn, sortAscending);

            var rows = new List<PrisonerRosterEntry>(colonyRows.Count + outpostRows.Count + 1);
            if (sourceFilter == PrisonerRosterSourceFilter.All
                && colonyRows.Count > 0
                && outpostRows.Count > 0)
            {
                rows.AddRange(colonyRows);
                rows.Add(new PrisonerRosterEntry
                {
                    isGroupHeader = true,
                    groupHeaderLabel = "TSA_WD_Prisoners_GroupOutpost".Translate()
                });
                rows.AddRange(outpostRows);
            }
            else if (sourceFilter == PrisonerRosterSourceFilter.Outpost)
            {
                rows.AddRange(outpostRows);
            }
            else if (sourceFilter == PrisonerRosterSourceFilter.Colony)
            {
                rows.AddRange(colonyRows);
            }
            else
            {
                rows.AddRange(colonyRows);
                rows.AddRange(outpostRows);
            }

            return rows;
        }

        /// <summary>
        /// Drop selection IDs that no longer exist as prisoners (not merely hidden by filters).
        /// Call after <see cref="BuildRoster"/> so the scan index is current.
        /// </summary>
        public static void PruneSelectionToLastScan(HashSet<string> selectedThingIds)
        {
            if (selectedThingIds == null || selectedThingIds.Count == 0) return;
            var drop = new List<string>();
            foreach (string id in selectedThingIds)
            {
                if (string.IsNullOrEmpty(id) || !IndexedThingIds.Contains(id))
                    drop.Add(id);
            }
            for (int i = 0; i < drop.Count; i++)
                selectedThingIds.Remove(drop[i]);
        }

        private static void CollectColonyPrisoners(
            List<PrisonerRosterEntry> rows,
            WorldComponent_PrisonerRecruitSchedule schedule,
            Faction player)
        {
            var settlements = Find.WorldObjects?.Settlements;
            if (settlements == null) return;

            for (int si = 0; si < settlements.Count; si++)
            {
                if (settlements[si] is not MapParent mp || mp.Faction != player || !mp.HasMap) continue;
                Map map = mp.Map;
                if (map?.mapPawns == null) continue;

                var all = map.mapPawns.AllPawnsSpawned;
                if (all == null) continue;

                for (int i = 0; i < all.Count; i++)
                {
                    Pawn p = all[i];
                    if (p == null || p.Destroyed || p.Dead) continue;
                    if (p.RaceProps?.Humanlike != true) continue;
                    if (!p.IsPrisonerOfColony) continue;

                    string name = p.Name?.ToStringFull ?? p.LabelCap ?? p.Label ?? "?";

                    var entry = CreateBaseEntry(p, name);
                    entry.mapParent = mp;
                    entry.isOutpostPrisoner = false;
                    entry.locationLabel = "TSA_WD_AllPlayerPawns_LocationColony".Translate(mp.LabelCap);
                    entry.locationIcon = mp.ExpandingIcon;
                    entry.locationIconColor = mp.Faction?.Color ?? Color.white;
                    entry.locationJumpTarget = mp;
                    FillDestination(entry, schedule);
                    rows.Add(entry);
                }
            }
        }

        private static void CollectOutpostPrisoners(
            List<PrisonerRosterEntry> rows,
            WorldComponent_PrisonerRecruitSchedule schedule,
            Faction player)
        {
            var worldObjects = Find.WorldObjects?.AllWorldObjects;
            if (worldObjects == null) return;

            for (int i = 0; i < worldObjects.Count; i++)
            {
                if (worldObjects[i] is not WorldObject_WD_Outpost outpost) continue;
                if (outpost.Destroyed || outpost.Faction != player) continue;
                List<Pawn> captives = outpost.Prisoners;
                if (captives == null || captives.Count == 0) continue;

                int queueCount = captives.Count;
                for (int pi = 0; pi < captives.Count; pi++)
                {
                    Pawn p = captives[pi];
                    if (p == null || p.Destroyed || p.Dead) continue;
                    if (p.RaceProps?.Humanlike != true) continue;
                    // Unwavering should never be stored; skip if they somehow are.
                    if (p.guest != null && !p.guest.Recruitable) continue;

                    string name = p.Name?.ToStringFull ?? p.LabelCap ?? p.Label ?? "?";

                    var entry = CreateBaseEntry(p, name);
                    entry.holdingOutpost = outpost;
                    entry.isOutpostPrisoner = true;
                    entry.prisonerQueueIndex = pi;
                    entry.prisonerQueueCount = queueCount;
                    entry.isBeingRecruited = OutpostPrisonerUtility.IsCurrentlyBeingRecruited(outpost, p);
                    entry.interactionLabel = GetOutpostInteractionLabel(p, outpost);
                    entry.locationLabel = outpost.LabelCap;
                    entry.locationIcon = outpost.def?.ExpandingIconTexture;
                    entry.locationIconColor = outpost.Faction?.Color ?? Color.white;
                    entry.locationJumpTarget = outpost;
                    FillOutpostResistanceDisplay(entry, outpost, p);
                    FillDestination(entry, schedule);
                    rows.Add(entry);
                }
            }
        }

        private static void FillOutpostResistanceDisplay(PrisonerRosterEntry entry, WorldObject_WD_Outpost outpost, Pawn pawn)
        {
            if (entry == null || outpost == null) return;
            float daily = OutpostPrisonerUtility.IsCurrentlyBeingRecruited(outpost, pawn)
                ? OutpostPrisonerResistanceScaling.GetDailyDrop(outpost)
                : 0f;
            entry.resistanceLabel = OutpostPrisonerResistanceScaling.FormatRateLabel(entry.resistance, daily);
            entry.resistanceTip = OutpostPrisonerResistanceScaling.BuildTooltip(outpost);
        }

        private static PrisonerRosterEntry CreateBaseEntry(Pawn p, string name)
        {
            var entry = new PrisonerRosterEntry
            {
                pawn = p,
                thingId = p.ThingID,
                nameLabel = name,
                interactionMode = p.guest?.ExclusiveInteractionMode,
                interactionLabel = GetInteractionLabel(p),
                recruitable = p.guest == null || p.guest.Recruitable,
                resistance = p.guest?.resistance ?? 0f,
            };
            entry.resistanceLabel = entry.resistance.ToString("F1");
            entry.skillLevels = BuildSkillLevels(VirtualPawnSummary.FromPawn(p));
            entry.ageYears = p.ageTracker != null ? p.ageTracker.AgeBiologicalYears : 0;
            FillTraits(p, entry);
            return entry;
        }

        private static void FillDestination(PrisonerRosterEntry entry, WorldComponent_PrisonerRecruitSchedule schedule)
        {
            if (schedule != null && schedule.TryGetDestination(entry.thingId, out WorldObject_WD_Outpost outpost, out MapParent colony))
            {
                entry.hasExplicitSchedule = true;
                if (outpost != null)
                {
                    entry.scheduledDestId = outpost.ID;
                    entry.scheduledDestLabel = outpost.LabelCap;
                    entry.scheduledDestIcon = outpost.def?.ExpandingIconTexture;
                    entry.scheduledDestIconColor = outpost.Faction?.Color ?? Color.white;
                    return;
                }
                if (colony != null)
                {
                    entry.scheduledDestId = colony.ID;
                    entry.scheduledDestLabel = PlayerPawnRosterUtility.FormatColonyLabelForDisplay(colony.LabelCap);
                    entry.scheduledDestIcon = Faction.OfPlayer?.def?.FactionIcon;
                    entry.scheduledDestIconColor = Faction.OfPlayer?.Color ?? Color.white;
                    return;
                }
            }

            // Default: show current home (colony map or holding outpost).
            entry.hasExplicitSchedule = false;
            if (entry.isOutpostPrisoner && entry.holdingOutpost != null)
            {
                entry.scheduledDestId = entry.holdingOutpost.ID;
                entry.scheduledDestLabel = entry.holdingOutpost.LabelCap;
                entry.scheduledDestIcon = entry.holdingOutpost.def?.ExpandingIconTexture;
                entry.scheduledDestIconColor = entry.holdingOutpost.Faction?.Color ?? Color.white;
            }
            else if (entry.mapParent != null)
            {
                entry.scheduledDestId = entry.mapParent.ID;
                entry.scheduledDestLabel = PlayerPawnRosterUtility.FormatColonyLabelForDisplay(entry.mapParent.LabelCap);
                entry.scheduledDestIcon = Faction.OfPlayer?.def?.FactionIcon;
                entry.scheduledDestIconColor = Faction.OfPlayer?.Color ?? Color.white;
            }
        }

        public static string GetInteractionLabel(Pawn pawn)
        {
            PrisonerInteractionModeDef mode = pawn?.guest?.ExclusiveInteractionMode;
            if (mode == PrisonerInteractionModeDefOf.AttemptRecruit)
                return "TSA_WD_Prisoners_ModeRecruit".Translate();
            if (mode == PrisonerInteractionModeDefOf.MaintainOnly)
                return "TSA_WD_Prisoners_ModeMaintain".Translate();
            if (mode != null && !mode.label.NullOrEmpty())
                return mode.LabelCap;
            return "?";
        }

        public static string GetOutpostInteractionLabel(Pawn pawn, WorldObject_WD_Outpost outpost)
        {
            if (pawn?.guest?.ExclusiveInteractionMode == PrisonerInteractionModeDefOf.MaintainOnly)
                return "TSA_WD_Prisoners_ModeMaintain".Translate();
            if (OutpostPrisonerUtility.IsCurrentlyBeingRecruited(outpost, pawn))
                return "TSA_WD_Prisoners_OutpostBeingRecruited".Translate();
            return "TSA_WD_Prisoners_ModeRecruit".Translate();
        }

        public static void SetInteractionMode(Pawn pawn, PrisonerInteractionModeDef mode)
        {
            if (pawn?.guest == null || mode == null) return;
            if (mode.hideIfNotRecruitable && !pawn.guest.Recruitable) return;
            pawn.guest.SetExclusiveInteraction(mode);
        }

        public static void FormatTraits(Pawn pawn, out string display, out string tip)
        {
            var labels = new List<string>();
            List<Trait> traits = pawn?.story?.traits?.TraitsSorted;
            if (traits != null)
            {
                for (int i = 0; i < traits.Count; i++)
                {
                    Trait t = traits[i];
                    if (t == null) continue;
                    string label = t.LabelCap;
                    if (label.NullOrEmpty()) continue;
                    labels.Add(label);
                }
            }

            int count = labels.Count;
            if (count == 0)
            {
                display = "-";
                tip = "";
                return;
            }

            var tipSb = new StringBuilder();
            for (int i = 0; i < count; i++)
            {
                if (i > 0) tipSb.AppendLine();
                tipSb.Append(labels[i]);
            }
            tip = tipSb.ToString();

            if (count <= 3)
                display = string.Join("\n", labels);
            else
                display = labels[0] + "\n" + labels[1] + "\n…";
        }

        /// <summary>Draws multiline traits vertically centered in the cell (RimWorld Label Mid anchors do not).</summary>
        public static void DrawTraitsCell(Rect cell, string display, string tip)
        {
            string text = display.NullOrEmpty() ? "—" : display;
            GameFont prevFont = Text.Font;
            TextAnchor prevAnchor = Text.Anchor;
            bool prevWrap = Text.WordWrap;
            Text.Font = GameFont.Tiny;
            Text.WordWrap = true;
            float textH = Mathf.Min(cell.height, Text.CalcHeight(text, cell.width));
            if (textH < 1f) textH = Mathf.Min(cell.height, Text.LineHeight);
            Rect labelRect = new Rect(
                cell.x,
                cell.y + Mathf.Max(0f, (cell.height - textH) * 0.5f),
                cell.width,
                textH);
            Text.Anchor = TextAnchor.UpperCenter;
            Widgets.Label(labelRect, text);
            Text.WordWrap = prevWrap;
            Text.Font = prevFont;
            Text.Anchor = prevAnchor;
            if (!tip.NullOrEmpty())
                TooltipHandler.TipRegion(cell, tip);
        }

        private static void FillTraits(Pawn pawn, PrisonerRosterEntry entry)
        {
            entry.traitLabels.Clear();
            FormatTraits(pawn, out entry.traitsDisplay, out entry.traitsTip);
            List<Trait> traits = pawn?.story?.traits?.TraitsSorted;
            if (traits == null) return;
            for (int i = 0; i < traits.Count; i++)
            {
                Trait t = traits[i];
                if (t == null) continue;
                string label = t.LabelCap;
                if (label.NullOrEmpty()) continue;
                entry.traitLabels.Add(label);
            }
        }

        private static int[] BuildSkillLevels(VirtualPawnSummary summary)
        {
            SkillDef[] cols = PlayerPawnRosterUtility.AllSkillColumns;
            var levels = new int[cols.Length];
            if (summary == null) return levels;
            for (int i = 0; i < cols.Length; i++)
                levels[i] = summary.GetSkill(cols[i]);
            return levels;
        }

        public static void SortRows(List<PrisonerRosterEntry> rows, string sortColumn, bool ascending)
        {
            if (rows == null || rows.Count < 2) return;
            rows.Sort((a, b) =>
            {
                int cmp = Compare(a, b, sortColumn);
                return ascending ? cmp : -cmp;
            });
        }

        /// <summary>Group by outpost label, then recruit queue index (priority).</summary>
        public static void SortOutpostRowsByQueue(List<PrisonerRosterEntry> rows)
        {
            if (rows == null || rows.Count < 2) return;
            rows.Sort((a, b) =>
            {
                int loc = string.Compare(a.locationLabel, b.locationLabel, StringComparison.OrdinalIgnoreCase);
                if (loc != 0) return loc;
                return a.prisonerQueueIndex.CompareTo(b.prisonerQueueIndex);
            });
        }

        private static int Compare(PrisonerRosterEntry a, PrisonerRosterEntry b, string col)
        {
            if (col == "Location" || col == "LocationName")
                return string.Compare(a.locationLabel, b.locationLabel, StringComparison.OrdinalIgnoreCase);
            if (col == "Interaction")
                return string.Compare(a.interactionLabel, b.interactionLabel, StringComparison.OrdinalIgnoreCase);
            if (col == "Resistance")
                return a.resistance.CompareTo(b.resistance);
            if (col == "Traits")
                return string.Compare(a.traitsTip, b.traitsTip, StringComparison.OrdinalIgnoreCase);
            if (col == "Xenotype")
                return PawnRosterTraitFilter.CompareXenotype(a.pawn, b.pawn);
            if (col == "Psycasts")
                return PawnRosterTraitFilter.ComparePsycasts(a.pawn, b.pawn);
            if (col == "Destination")
                return string.Compare(a.scheduledDestLabel, b.scheduledDestLabel, StringComparison.OrdinalIgnoreCase);
            if (col == "Age")
                return a.ageYears.CompareTo(b.ageYears);

            SkillDef[] skills = PlayerPawnRosterUtility.AllSkillColumns;
            for (int i = 0; i < skills.Length; i++)
            {
                if (skills[i].defName != col) continue;
                int av = i < a.skillLevels.Length ? a.skillLevels[i] : 0;
                int bv = i < b.skillLevels.Length ? b.skillLevels[i] : 0;
                return av.CompareTo(bv);
            }

            return string.Compare(a.nameLabel, b.nameLabel, StringComparison.OrdinalIgnoreCase);
        }

        public static List<PrisonerRosterEntry> ResolveSelected(List<PrisonerRosterEntry> list, HashSet<string> selectedThingIds)
        {
            var result = new List<PrisonerRosterEntry>();
            if (list == null || selectedThingIds == null || selectedThingIds.Count == 0) return result;
            for (int i = 0; i < list.Count; i++)
            {
                PrisonerRosterEntry e = list[i];
                if (e.isGroupHeader) continue;
                if (e.thingId != null && selectedThingIds.Contains(e.thingId))
                    result.Add(e);
            }
            return result;
        }

        /// <summary>
        /// Like <see cref="ResolveSelected"/>, but includes selected prisoners hidden by filters.
        /// Use for action buttons, not every-frame UI checks.
        /// </summary>
        public static List<PrisonerRosterEntry> ResolveSelectedIncludingHidden(
            List<PrisonerRosterEntry> visibleRows,
            HashSet<string> selectedThingIds)
        {
            var result = ResolveSelected(visibleRows, selectedThingIds);
            if (selectedThingIds == null || selectedThingIds.Count == 0 || result.Count >= selectedThingIds.Count)
                return result;

            var full = BuildRoster(null, DefaultSortColumn, true, PrisonerRosterSourceFilter.All);
            return ResolveSelected(full, selectedThingIds);
        }

        /// <summary>
        /// For each selected prisoner: walk skills from highest to lowest, assign the closest player outpost
        /// whose relevant skill matches. Returns how many were assigned.
        /// </summary>
        public static int SmartAssignDestinations(List<PrisonerRosterEntry> selected, out int failed)
        {
            failed = 0;
            if (selected == null || selected.Count == 0) return 0;

            var schedule = WorldComponent_PrisonerRecruitSchedule.Get();
            if (schedule == null) { failed = selected.Count; return 0; }

            Dictionary<SkillDef, List<WorldObject_WD_Outpost>> bySkill = SmartAssignOutpostUtility.BuildOutpostsByRelevantSkill();
            if (bySkill.Count == 0) { failed = selected.Count; return 0; }

            int assigned = 0;
            for (int i = 0; i < selected.Count; i++)
            {
                if (TrySmartAssignOne(selected[i], bySkill, schedule))
                    assigned++;
                else
                    failed++;
            }
            return assigned;
        }

        private static bool TrySmartAssignOne(
            PrisonerRosterEntry entry,
            Dictionary<SkillDef, List<WorldObject_WD_Outpost>> bySkill,
            WorldComponent_PrisonerRecruitSchedule schedule)
        {
            Pawn pawn = entry?.pawn;
            if (pawn?.skills?.skills == null || entry.thingId.NullOrEmpty()) return false;

            PlanetTile originTile;
            WorldObject_WD_Outpost currentOutpost = null;
            if (entry.isOutpostPrisoner)
            {
                if (entry.holdingOutpost == null || !entry.holdingOutpost.Tile.Valid) return false;
                originTile = entry.holdingOutpost.Tile;
                currentOutpost = entry.holdingOutpost;
            }
            else
            {
                if (entry.mapParent == null || !entry.mapParent.Tile.Valid) return false;
                originTile = entry.mapParent.Tile;
            }

            if (!SmartAssignOutpostUtility.TryFindSmartAssignOutpost(
                    pawn,
                    originTile,
                    bySkill,
                    currentOutpost: currentOutpost,
                    out WorldObject_WD_Outpost? best)
                || best == null)
            {
                return false;
            }

            schedule.SetDest(entry.thingId, best);
            return true;
        }

        public static string SourceFilterLabel(PrisonerRosterSourceFilter filter) => filter switch
        {
            PrisonerRosterSourceFilter.Colony => "TSA_WD_Prisoners_Filter_Colony".Translate(),
            PrisonerRosterSourceFilter.Outpost => "TSA_WD_Prisoners_Filter_Outpost".Translate(),
            _ => "TSA_WD_Prisoners_Filter_All".Translate()
        };
    }
}
