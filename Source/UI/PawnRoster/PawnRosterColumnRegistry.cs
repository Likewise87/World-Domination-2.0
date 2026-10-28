#nullable disable
using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace TSA_WorldDomination
{
    public enum PawnRosterColumnKind
    {
        Type,
        Star,
        New,
        Age,
        Health,
        Hurt,
        Traits,
        Xenotype,
        Psycasts,
        Ideology,
        Gear,
        Mass,
        Bulk,
        Strength,
        Skill
    }

    /// <summary>One common roster column: label, width, gate, and how to draw / compare it from <see cref="PawnRosterRowData"/>.</summary>
    public sealed class PawnRosterColumnDef
    {
        public readonly string Id;
        public readonly string LabelKey;
        public readonly string TipKey;
        public readonly float Width;
        public readonly PawnRosterColumnKind Kind;
        public readonly SkillDef Skill;
        public readonly int SkillIndex;
        public readonly PawnRosterCapability Requires;

        private string label;
        private string tip;

        public PawnRosterColumnDef(
            string id,
            string labelKey,
            string tipKey,
            float width,
            PawnRosterColumnKind kind,
            PawnRosterCapability requires = PawnRosterCapability.None,
            SkillDef skill = null,
            int skillIndex = -1)
        {
            Id = id;
            LabelKey = labelKey;
            TipKey = tipKey;
            Width = width;
            Kind = kind;
            Requires = requires;
            Skill = skill;
            SkillIndex = skillIndex;
        }

        public string Label => label ??= !LabelKey.NullOrEmpty()
            ? LabelKey.Translate().ToString()
            : Skill?.LabelCap.ToString() ?? Id;

        public string Tip => tip ??= !TipKey.NullOrEmpty() ? TipKey.Translate().ToString() : "";

        /// <summary>DLC / compat gate (Xenotype = Biotech, Psycasts = Royalty, Ideology, Bulk = CE).</summary>
        public bool Available
        {
            get
            {
                switch (Kind)
                {
                    case PawnRosterColumnKind.Xenotype: return ModsConfig.BiotechActive;
                    case PawnRosterColumnKind.Psycasts: return ModsConfig.RoyaltyActive;
                    case PawnRosterColumnKind.Ideology: return ModsConfig.IdeologyActive;
                    case PawnRosterColumnKind.Bulk: return OutpostCeAmmoCompat.IsCeActive;
                    default: return true;
                }
            }
        }
    }

    /// <summary>
    /// Single source of truth for common roster columns. Each window keeps drawing its own (native)
    /// columns; registry "extras" (allowed here, not native) are appended after them via
    /// <see cref="ExtrasWidth"/> / <see cref="DrawExtraHeaders"/> / <see cref="DrawExtraCells"/>.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class PawnRosterColumnRegistry
    {
        private static List<PawnRosterColumnDef> common;
        private static Dictionary<string, PawnRosterColumnDef> byId;
        private static readonly Dictionary<PawnRosterColumnWindow, List<PawnRosterColumnDef>> extrasByWindow =
            new Dictionary<PawnRosterColumnWindow, List<PawnRosterColumnDef>>();

        private static readonly Color StarOn = new Color(1f, 0.85f, 0.2f);
        private static readonly Color StarOff = new Color(0.55f, 0.55f, 0.55f, 0.7f);
        private static readonly Color JoinRecent = new Color(0.45f, 0.85f, 0.55f);

        public static IReadOnlyList<PawnRosterColumnDef> Common
        {
            get
            {
                EnsureBuilt();
                return common;
            }
        }

        public static PawnRosterColumnDef Get(string id)
        {
            EnsureBuilt();
            return id != null && byId.TryGetValue(id, out PawnRosterColumnDef def) ? def : null;
        }

        public static bool IsAllowed(PawnRosterColumnWindow window, PawnRosterColumnDef def) =>
            def != null && def.Available && PawnRosterSurface.Has(window, def.Requires);

        public static IReadOnlyList<PawnRosterColumnDef> ExtrasFor(PawnRosterColumnWindow window)
        {
            EnsureBuilt();
            if (extrasByWindow.TryGetValue(window, out List<PawnRosterColumnDef> list))
                return list;
            list = new List<PawnRosterColumnDef>();
            for (int i = 0; i < common.Count; i++)
            {
                PawnRosterColumnDef def = common[i];
                if (!IsAllowed(window, def) || PawnRosterSurface.IsNative(window, def.Id)) continue;
                list.Add(def);
            }
            extrasByWindow[window] = list;
            return list;
        }

        /// <summary>True when <paramref name="id"/> is a registry column drawn as an extra in this window.</summary>
        public static bool IsExtra(PawnRosterColumnWindow window, string id)
        {
            IReadOnlyList<PawnRosterColumnDef> extras = ExtrasFor(window);
            for (int i = 0; i < extras.Count; i++)
            {
                if (extras[i].Id == id) return true;
            }
            return false;
        }

        public static bool AnyVisibleExtra(PawnRosterColumnWindow window)
        {
            IReadOnlyList<PawnRosterColumnDef> extras = ExtrasFor(window);
            for (int i = 0; i < extras.Count; i++)
            {
                if (PlayerPawnRosterUtility.ColVisible(window, extras[i].Id)) return true;
            }
            return false;
        }

        /// <summary>True if any visible extra needs three-line rows (Traits / Psycasts).</summary>
        public static bool AnyVisibleTallExtra(PawnRosterColumnWindow window)
        {
            IReadOnlyList<PawnRosterColumnDef> extras = ExtrasFor(window);
            for (int i = 0; i < extras.Count; i++)
            {
                PawnRosterColumnKind k = extras[i].Kind;
                if ((k == PawnRosterColumnKind.Traits || k == PawnRosterColumnKind.Psycasts)
                    && PlayerPawnRosterUtility.ColVisible(window, extras[i].Id))
                    return true;
            }
            return false;
        }

        public static float ExtrasWidth(PawnRosterColumnWindow window)
        {
            float w = 0f;
            IReadOnlyList<PawnRosterColumnDef> extras = ExtrasFor(window);
            for (int i = 0; i < extras.Count; i++)
            {
                if (PlayerPawnRosterUtility.ColVisible(window, extras[i].Id))
                    w += extras[i].Width;
            }
            return w;
        }

        /// <summary>Sortable headers for visible extras. <paramref name="onSort"/> receives the column id.</summary>
        public static void DrawExtraHeaders(
            ref float curX,
            float y,
            float height,
            PawnRosterColumnWindow window,
            PawnRosterSortChain sort,
            Action<string> onSort)
        {
            IReadOnlyList<PawnRosterColumnDef> extras = ExtrasFor(window);
            for (int i = 0; i < extras.Count; i++)
            {
                PawnRosterColumnDef def = extras[i];
                if (!PlayerPawnRosterUtility.ColVisible(window, def.Id)) continue;
                Rect hdr = new Rect(curX, y, def.Width, height);
                bool sorted = sort != null && sort.HeaderSorted(def.Id);
                bool asc = sort == null || sort.IsAscending(def.Id);
                PawnRosterHeaderFilter.DrawFilterableHeader(
                    ref curX, y, def.Width, height,
                    def.Label, sorted, asc,
                    TextAnchor.MiddleCenter,
                    false, null, null, null);
                if (!def.Tip.NullOrEmpty())
                    TooltipHandler.TipRegion(hdr, def.Tip);
                if (onSort == null) continue;
                if (Mouse.IsOver(hdr)) Widgets.DrawHighlight(hdr);
                if (Widgets.ButtonInvisible(hdr))
                    onSort(def.Id);
            }
        }

        /// <summary>
        /// Cells for visible extras. Returns true when a cell changed row state (star toggle) so the
        /// caller can rebuild.
        /// </summary>
        public static bool DrawExtraCells(
            ref float curX,
            float y,
            float rowHeight,
            PawnRosterColumnWindow window,
            PawnRosterRowData data)
        {
            bool changed = false;
            IReadOnlyList<PawnRosterColumnDef> extras = ExtrasFor(window);
            for (int i = 0; i < extras.Count; i++)
            {
                PawnRosterColumnDef def = extras[i];
                if (!PlayerPawnRosterUtility.ColVisible(window, def.Id)) continue;
                Rect cell = new Rect(curX, y, def.Width, rowHeight);
                if (data?.pawn != null && DrawCell(cell, def, data, window))
                    changed = true;
                curX += def.Width;
            }
            return changed;
        }

        public static bool DrawCell(Rect cell, PawnRosterColumnDef def, PawnRosterRowData d, PawnRosterColumnWindow window)
        {
            GameFont prevFont = Text.Font;
            TextAnchor prevAnchor = Text.Anchor;
            Color prevColor = GUI.color;
            bool changed = false;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            switch (def.Kind)
            {
                case PawnRosterColumnKind.Type:
                    Widgets.Label(cell, d.typeLabel.Truncate(cell.width - 4f));
                    break;
                case PawnRosterColumnKind.Star:
                    Text.Font = GameFont.Medium;
                    GUI.color = d.isStarred ? StarOn : StarOff;
                    Widgets.Label(cell, d.isStarred ? "★" : "☆");
                    GUI.color = prevColor;
                    if (!d.thingId.NullOrEmpty() && Widgets.ButtonInvisible(cell))
                    {
                        WorldComponent_PlayerPawnFavorites.Get()?.Toggle(d.thingId);
                        d.isStarred = !d.isStarred;
                        SoundDefOf.Click.PlayOneShotOnCamera();
                        changed = true;
                    }
                    break;
                case PawnRosterColumnKind.New:
                    if (d.daysSinceJoin >= 0)
                    {
                        if (PlayerPawnRosterUtility.IsJoinStampRecent(d.daysSinceJoin))
                            GUI.color = JoinRecent;
                        Widgets.Label(cell, d.daysSinceJoinLabel);
                    }
                    break;
                case PawnRosterColumnKind.Age:
                    Widgets.Label(cell, d.sparseSkills && d.ageYears <= 0 ? "-" : d.ageLabel);
                    break;
                case PawnRosterColumnKind.Health:
                    Widgets.Label(cell, d.healthLabel);
                    break;
                case PawnRosterColumnKind.Hurt:
                    if (d.needsHealing)
                        OutpostHealthUIUtils.DrawBleedingIconCentered(cell, cell.height);
                    break;
                case PawnRosterColumnKind.Traits:
                    PrisonerRosterUtility.DrawTraitsCell(cell.ContractedBy(2f), d.traitsDisplay, d.traitsTip);
                    break;
                case PawnRosterColumnKind.Xenotype:
                    PrisonerRosterUtility.DrawTraitsCell(cell.ContractedBy(2f), d.xenotypeDisplay, d.xenotypeTip);
                    break;
                case PawnRosterColumnKind.Psycasts:
                    PrisonerRosterUtility.DrawTraitsCell(cell.ContractedBy(2f), d.psycastsDisplay, d.psycastsTip);
                    break;
                case PawnRosterColumnKind.Ideology:
                    PrisonerRosterUtility.DrawTraitsCell(cell.ContractedBy(2f), d.ideologyDisplay, d.ideologyTip);
                    break;
                case PawnRosterColumnKind.Gear:
                    OutpostStrengthBudgetUi.DrawEquippedItemIcons(new Rect(cell.x + 2f, cell.y, cell.width - 4f, cell.height), d.pawn);
                    break;
                case PawnRosterColumnKind.Mass:
                    GUI.color = PawnRosterRowData.CarryLoadTextColor(d.massCur, d.massCap);
                    Widgets.Label(cell, d.massLabel);
                    break;
                case PawnRosterColumnKind.Bulk:
                    if (d.hasBulk)
                        GUI.color = PawnRosterRowData.CarryLoadTextColor(d.bulkCur, d.bulkCap);
                    Widgets.Label(cell, d.bulkLabel);
                    break;
                case PawnRosterColumnKind.Strength:
                    Widgets.Label(cell, d.strengthLabel);
                    break;
                case PawnRosterColumnKind.Skill:
                    if (d.sparseSkills)
                    {
                        GUI.color = Color.gray;
                        Widgets.Label(cell, "-");
                    }
                    else
                    {
                        int level = d.SkillLevel(def.SkillIndex);
                        bool isBest = d.bestSkillLevel > 0 && level == d.bestSkillLevel;
                        PlayerPawnRosterUtility.DrawSkillLevelWithPassion(cell, d.pawn, def.Skill, level, isBest, window);
                    }
                    break;
            }
            GUI.color = prevColor;
            Text.Font = prevFont;
            Text.Anchor = prevAnchor;
            return changed;
        }

        /// <summary>Ascending comparison for registry column <paramref name="id"/>; 0 when unknown.</summary>
        public static int Compare(string id, PawnRosterRowData a, PawnRosterRowData b)
        {
            PawnRosterColumnDef def = Get(id);
            if (def == null) return 0;
            if (a == null || b == null)
                return (a == null ? 1 : 0).CompareTo(b == null ? 1 : 0);
            switch (def.Kind)
            {
                case PawnRosterColumnKind.Type:
                {
                    int cmp = ((int)a.category).CompareTo((int)b.category);
                    return cmp != 0 ? cmp : string.Compare(a.typeLabel, b.typeLabel, StringComparison.OrdinalIgnoreCase);
                }
                case PawnRosterColumnKind.Star: return a.isStarred.CompareTo(b.isStarred);
                case PawnRosterColumnKind.New: return a.daysSinceJoin.CompareTo(b.daysSinceJoin);
                case PawnRosterColumnKind.Age: return a.ageYears.CompareTo(b.ageYears);
                case PawnRosterColumnKind.Health: return a.healthPercent.CompareTo(b.healthPercent);
                case PawnRosterColumnKind.Hurt: return a.needsHealing.CompareTo(b.needsHealing);
                case PawnRosterColumnKind.Traits:
                    return string.Compare(a.traitsTip, b.traitsTip, StringComparison.OrdinalIgnoreCase);
                case PawnRosterColumnKind.Xenotype:
                    return string.Compare(a.xenotypeDisplay, b.xenotypeDisplay, StringComparison.OrdinalIgnoreCase);
                case PawnRosterColumnKind.Psycasts:
                    return string.Compare(a.psycastsTip, b.psycastsTip, StringComparison.OrdinalIgnoreCase);
                case PawnRosterColumnKind.Ideology:
                    return string.Compare(a.ideologyDisplay, b.ideologyDisplay, StringComparison.OrdinalIgnoreCase);
                case PawnRosterColumnKind.Gear: return a.gearCount.CompareTo(b.gearCount);
                case PawnRosterColumnKind.Mass: return a.massCur.CompareTo(b.massCur);
                case PawnRosterColumnKind.Bulk: return a.bulkCur.CompareTo(b.bulkCur);
                case PawnRosterColumnKind.Strength: return a.strength.CompareTo(b.strength);
                case PawnRosterColumnKind.Skill:
                {
                    int av = a.sparseSkills ? -1 : a.SkillLevel(def.SkillIndex);
                    int bv = b.sparseSkills ? -1 : b.SkillLevel(def.SkillIndex);
                    return av.CompareTo(bv);
                }
                default: return 0;
            }
        }

        /// <summary>Skill columns default to descending on first click (highest first), like the existing grids.</summary>
        public static bool FirstClickAscending(string id)
        {
            PawnRosterColumnDef def = Get(id);
            if (def == null) return true;
            switch (def.Kind)
            {
                case PawnRosterColumnKind.Skill:
                case PawnRosterColumnKind.Strength:
                case PawnRosterColumnKind.Health:
                case PawnRosterColumnKind.Gear:
                    return false;
                default:
                    return true;
            }
        }

        private static void EnsureBuilt()
        {
            if (common != null) return;
            common = new List<PawnRosterColumnDef>
            {
                new PawnRosterColumnDef(PawnRosterColumnIds.Type, "TSA_WD_AllPlayerPawns_ColPawnType", null, 96f,
                    PawnRosterColumnKind.Type),
                new PawnRosterColumnDef(PawnRosterColumnIds.Star, "TSA_WD_AllPlayerPawns_ColStar",
                    "TSA_WD_AllPlayerPawns_StarTip", 56f, PawnRosterColumnKind.Star),
                new PawnRosterColumnDef(PawnRosterColumnIds.New, "TSA_WD_PawnRoster_ColNew",
                    "TSA_WD_PawnRoster_ColNewTip", 56f, PawnRosterColumnKind.New, PawnRosterCapability.PlayerOwned),
                new PawnRosterColumnDef(PawnRosterColumnIds.Age, "TSA_WD_PawnRoster_ColAge",
                    "TSA_WD_PawnRoster_ColAgeTip", 44f, PawnRosterColumnKind.Age),
                new PawnRosterColumnDef(PawnRosterColumnIds.Health, "TSA_WD_PawnRoster_ColHealth",
                    "TSA_WD_PawnRoster_ColHealthTip", 56f, PawnRosterColumnKind.Health),
                new PawnRosterColumnDef(PawnRosterColumnIds.Hurt, "TSA_WD_PawnRoster_ColHurt",
                    "TSA_WD_PawnCol_HurtTip", 48f, PawnRosterColumnKind.Hurt),
                new PawnRosterColumnDef(PawnRosterColumnIds.Traits, "TSA_WD_Prisoners_ColTraits", null,
                    PawnRosterTraitFilter.ColWidth, PawnRosterColumnKind.Traits),
                new PawnRosterColumnDef(PawnRosterColumnIds.Xenotype, "TSA_WD_PawnRoster_ColXenotype", null, 100f,
                    PawnRosterColumnKind.Xenotype),
                new PawnRosterColumnDef(PawnRosterColumnIds.Psycasts, "TSA_WD_PawnRoster_ColPsycasts", null, 110f,
                    PawnRosterColumnKind.Psycasts),
                new PawnRosterColumnDef(PawnRosterColumnIds.Ideology, "TSA_WD_PawnRoster_ColIdeology", null, 110f,
                    PawnRosterColumnKind.Ideology),
                new PawnRosterColumnDef(PawnRosterColumnIds.Gear, "TSA_WD_PawnRoster_ColGear",
                    "TSA_WD_PawnRoster_ColGearTip", 160f, PawnRosterColumnKind.Gear),
                new PawnRosterColumnDef(PawnRosterColumnIds.Mass, "TSA_WD_Armory_ColMass", null, 64f,
                    PawnRosterColumnKind.Mass),
                new PawnRosterColumnDef(PawnRosterColumnIds.Bulk, "TSA_WD_Armory_ColBulk", null, 64f,
                    PawnRosterColumnKind.Bulk),
                new PawnRosterColumnDef(PawnRosterColumnIds.Strength, "TSA_WD_PawnRoster_ColStrength", null, 72f,
                    PawnRosterColumnKind.Strength, PawnRosterCapability.PlayerOwned),
            };
            SkillDef[] skills = PlayerPawnRosterUtility.AllSkillColumns;
            for (int i = 0; i < skills.Length; i++)
            {
                if (skills[i] == null) continue;
                common.Add(new PawnRosterColumnDef(PawnRosterColumnIds.Skill(skills[i]), null, null, 56f,
                    PawnRosterColumnKind.Skill, PawnRosterCapability.None, skills[i], i));
            }
            byId = new Dictionary<string, PawnRosterColumnDef>();
            for (int i = 0; i < common.Count; i++)
                byId[common[i].Id] = common[i];
        }
    }
}
