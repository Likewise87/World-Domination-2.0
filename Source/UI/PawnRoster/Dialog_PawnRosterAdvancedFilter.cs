#nullable disable
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Presets, multi-level sort, and active filters for one roster window. Edits apply live to the host.
    /// Styled like <see cref="Dialog_PawnRosterTraitFilter"/>.
    /// </summary>
    public class Dialog_PawnRosterAdvancedFilter : Window
    {
        private const float TitleH = 30f;
        private const float SubtitleH = 18f;
        private const float RowH = 28f;
        private const float IconBtn = 24f;
        private const float Gap = 6f;
        private const float DirBtnW = 64f;
        private const float LevelNumW = 22f;
        private const int OptionsRefreshFrames = 60;

        /// <summary>Same as the window close X (<c>Widgets.CloseButtonFor</c>): 18px <see cref="TexButton.CloseXSmall"/>, untinted.</summary>
        private const float CloseXSize = 18f;
        /// <summary>Space between the close X and the row's right edge.</summary>
        private const float CloseXRightInset = 6f;
        /// <summary>Space between the content and the vertical scrollbar.</summary>
        private const float ScrollbarGap = 6f;

        private readonly IPawnRosterViewHost host;
        private readonly List<PawnRosterSortOption> sortOptions = new List<PawnRosterSortOption>();
        private readonly List<string> activeKeys = new List<string>();
        private PawnRosterFilterPreset selectedPreset;
        private readonly List<Pawn> population = new List<Pawn>();
        private readonly List<string> addableKeys = new List<string>();
        private int seenTraitRevision = -1;
        private Vector2 scroll;
        private bool presetsExpanded = true;
        private bool sortExpanded = true;
        private bool filtersExpanded = true;
        private int optionsFrame = -9999;

        private static string buttonTip;
        public static string ButtonTip => buttonTip ??= "TSA_WD_AdvFilter_ButtonTip".Translate();

        public override Vector2 InitialSize => new Vector2(520f, 640f);

        public Dialog_PawnRosterAdvancedFilter(IPawnRosterViewHost host)
        {
            this.host = host;
            doCloseX = true;
            closeOnClickedOutside = false;
            absorbInputAroundWindow = false;
            draggable = true;
            forcePause = false;
            preventCameraMotion = false;
        }

        /// <summary>One Advanced Filtering dialog at a time; reopening replaces the previous one.</summary>
        public static void Open(IPawnRosterViewHost host)
        {
            if (host == null) return;
            Find.WindowStack.TryRemove(typeof(Dialog_PawnRosterAdvancedFilter), doCloseSound: false);
            PawnRosterHeaderFilter.CloseDropdown();
            Find.WindowStack.Add(new Dialog_PawnRosterAdvancedFilter(host));
        }

        /// <summary>Tint for the toolbar button: any filter, sort level, or applicable trait filter.</summary>
        public static bool IsViewCustomized(IPawnRosterViewHost host)
        {
            if (host?.RosterView == null) return false;
            PawnRosterViewState v = host.RosterView;
            if (v.HasActiveFilters || !v.SortIsDefault) return true;
            return PawnRosterTraitFilter.IsActive && host.SupportsRosterFilter(PawnRosterFilterKeys.Traits);
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (host == null || !host.RosterHostOpen)
            {
                Close(doCloseSound: false);
                return;
            }
            if (PawnRosterHeaderFilter.TryCloseDropdownOnCancel())
                return;
            if (Time.frameCount - optionsFrame >= OptionsRefreshFrames)
                RefreshSortOptions();
            if (seenTraitRevision != PawnRosterTraitFilter.Revision)
            {
                if (seenTraitRevision >= 0) Changed();
                seenTraitRevision = PawnRosterTraitFilter.Revision;
            }

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, TitleH), "TSA_WD_AdvFilter_Title".Translate());
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(0f, TitleH, inRect.width, SubtitleH), "TSA_WD_AdvFilter_Subtitle".Translate());
            Text.Font = GameFont.Small;

            float top = TitleH + SubtitleH + 6f;
            Rect outRect = new Rect(0f, top, inRect.width, inRect.height - top);
            float viewH = 3 * 34f + 3 * RowH * 2f
                + (sortExpanded ? (host.RosterView.Sort.Count + 2) * (RowH + 4f) : 0f)
                + (filtersExpanded ? (CountActiveLines() + 2) * (RowH + 2f) : 0f)
                + 60f;
            Rect view = new Rect(0f, 0f, outRect.width - 16f - ScrollbarGap, Mathf.Max(outRect.height, viewH));
            Widgets.BeginScrollView(outRect, ref scroll, view);
            var listing = new Listing_Standard();
            listing.Begin(view);

            if (SettingsUI.DrawCollapsibleHeader(listing, "TSA_WD_AdvFilter_Presets".Translate(), ref presetsExpanded,
                    SettingsUI.SectionHeaderColor, "TSA_WD_AdvFilter_PresetsTip".Translate()))
                DrawPresets(listing);

            if (SettingsUI.DrawCollapsibleHeader(listing, "TSA_WD_AdvFilter_SortLevels".Translate(), ref sortExpanded,
                    SettingsUI.SectionHeaderColor, "TSA_WD_AdvFilter_SortLevelsTip".Translate()))
                DrawSortLevels(listing);

            if (SettingsUI.DrawCollapsibleHeader(listing, "TSA_WD_AdvFilter_ActiveFilters".Translate(), ref filtersExpanded,
                    SettingsUI.SectionHeaderColor, "TSA_WD_AdvFilter_ActiveFiltersTip".Translate()))
                DrawActiveFilters(listing);

            listing.End();
            Widgets.EndScrollView();
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            PawnRosterHeaderFilter.DrawDropdownIfOpen();
        }

        private void RefreshSortOptions()
        {
            optionsFrame = Time.frameCount;
            sortOptions.Clear();
            host.CollectRosterSortOptions(sortOptions);
        }

        private void Changed()
        {
            PawnRosterViewUtil.EnsureColumnsForView(host);
            host.OnRosterViewChanged();
        }

        // ---------------- Presets ----------------

        private void DrawPresets(Listing_Standard listing)
        {
            List<PawnRosterFilterPreset> all = PawnRosterFilterPresets.All;
            if (selectedPreset != null && !all.Contains(selectedPreset))
                selectedPreset = null;

            Rect pickRect = listing.GetRect(RowH);
            string pickLabel = selectedPreset != null
                ? selectedPreset.name
                : NoPresetSelectedText;
            TooltipHandler.TipRegion(pickRect, "TSA_WD_AdvFilter_SelectPresetTip".Translate());
            if (WorldDomination_UIUtils.ButtonTextWithIcon(pickRect, null, pickLabel.Truncate(pickRect.width - 12f))
                && all.Count > 0)
            {
                SoundDefOf.Click.PlayOneShotOnCamera();
                OpenPresetMenu(all);
            }

            listing.Gap(4f);
            Rect row = listing.GetRect(RowH);
            float btnW = (row.width - 3f * Gap) / 4f;
            Rect saveRect = new Rect(row.x, row.y, btnW, RowH);
            Rect overwriteRect = new Rect(saveRect.xMax + Gap, row.y, btnW, RowH);
            Rect renameRect = new Rect(overwriteRect.xMax + Gap, row.y, btnW, RowH);
            Rect deleteRect = new Rect(renameRect.xMax + Gap, row.y, btnW, RowH);
            bool has = selectedPreset != null;

            if (DrawButton(saveRect, "TSA_WD_AdvFilter_SaveNew".Translate(), true, "TSA_WD_AdvFilter_SaveNewTip".Translate()))
            {
                Find.WindowStack.Add(new Dialog_PawnRosterPresetName(
                    "TSA_WD_AdvFilter_SaveNewTitle".Translate(), "",
                    n => PawnRosterFilterPresets.Find(n) != null ? NameTakenText : null,
                    n =>
                    {
                        PawnRosterFilterPreset created = PawnRosterFilterPresets.SaveNew(host, n);
                        if (created == null) return;
                        selectedPreset = created;
                        Messages.Message("TSA_WD_AdvFilter_Saved".Translate(created.name), MessageTypeDefOf.SilentInput, false);
                    }));
            }
            if (DrawButton(overwriteRect, "TSA_WD_AdvFilter_Overwrite".Translate(), has, "TSA_WD_AdvFilter_OverwriteTip".Translate()))
            {
                PawnRosterFilterPresets.Overwrite(selectedPreset, host);
                Messages.Message("TSA_WD_AdvFilter_Saved".Translate(selectedPreset.name), MessageTypeDefOf.SilentInput, false);
            }
            if (DrawButton(renameRect, "TSA_WD_AdvFilter_Rename".Translate(), has, "TSA_WD_AdvFilter_RenameTip".Translate()))
            {
                PawnRosterFilterPreset target = selectedPreset;
                Find.WindowStack.Add(new Dialog_PawnRosterPresetName(
                    "TSA_WD_AdvFilter_RenameTitle".Translate(), target.name,
                    n =>
                    {
                        PawnRosterFilterPreset clash = PawnRosterFilterPresets.Find(n);
                        return clash != null && clash != target ? NameTakenText : null;
                    },
                    n => PawnRosterFilterPresets.Rename(target, n)));
            }
            if (DrawButton(deleteRect, "TSA_WD_AdvFilter_Delete".Translate(), has, null))
            {
                PawnRosterFilterPreset doomed = selectedPreset;
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    "TSA_WD_AdvFilter_DeleteConfirm".Translate(doomed.name),
                    () =>
                    {
                        PawnRosterFilterPresets.Delete(doomed);
                        if (selectedPreset == doomed) selectedPreset = null;
                    },
                    destructive: true));
            }
            listing.Gap(8f);
        }

        private static string nameTakenText;
        private static string NameTakenText => nameTakenText ??= "TSA_WD_AdvFilter_NameTaken".Translate();
        private static string noPresetSelectedText;
        private static string NoPresetSelectedText => noPresetSelectedText ??= "TSA_WD_AdvFilter_NoPresetSelected".Translate();

        /// <summary>Picking a preset applies it right away (like every other edit in this dialog).</summary>
        private void OpenPresetMenu(List<PawnRosterFilterPreset> all)
        {
            var options = new List<FloatMenuOption>(all.Count);
            for (int i = 0; i < all.Count; i++)
            {
                PawnRosterFilterPreset p = all[i];
                if (p == null) continue;
                options.Add(new FloatMenuOption(p.name, () =>
                {
                    selectedPreset = p;
                    PawnRosterFilterPresets.Apply(p, host);
                    RefreshSortOptions();
                }));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        // ---------------- Sort levels ----------------

        private void DrawSortLevels(Listing_Standard listing)
        {
            PawnRosterSortChain chain = host.RosterView.Sort;
            if (chain.IsEmpty)
            {
                Rect info = listing.GetRect(24f);
                GUI.color = Color.gray;
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(info, "TSA_WD_AdvFilter_SortDefault".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = GameFont.Small;
                GUI.color = Color.white;
            }

            for (int i = 0; i < chain.Count; i++)
            {
                PawnRosterSortLevel level = chain[i];
                Rect row = listing.GetRect(RowH);
                if (i % 2 == 0) Widgets.DrawHighlight(row);
                float x = row.xMax;

                Rect removeRect = CloseXRect(row);
                x = removeRect.x - Gap - 4f;
                float iconY = row.y + (RowH - IconBtn) * 0.5f;
                Rect downRect = new Rect(x - IconBtn, iconY, IconBtn, IconBtn);
                x = downRect.x - 2f;
                Rect upRect = new Rect(x - IconBtn, iconY, IconBtn, IconBtn);
                x = upRect.x - Gap;
                Rect dirRect = new Rect(x - DirBtnW, row.y, DirBtnW, RowH);
                x = dirRect.x - Gap;
                Rect numRect = new Rect(row.x, row.y, LevelNumW, RowH);
                Rect colRect = new Rect(numRect.xMax + 2f, row.y, x - numRect.xMax - 2f, RowH);

                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(numRect, (i + 1).ToString());
                Text.Anchor = TextAnchor.UpperLeft;

                if (Widgets.ButtonText(colRect, LabelForSortId(level.Id).Truncate(colRect.width - 12f)))
                    OpenColumnMenu(i);

                if (PawnRosterHeaderFilter.DrawSlateChoice(dirRect,
                        level.Ascending
                            ? "TSA_WD_AdvFilter_Asc".Translate().ToString()
                            : "TSA_WD_AdvFilter_Desc".Translate().ToString(),
                        false,
                        "TSA_WD_AdvFilter_DirTip".Translate()))
                {
                    chain.SetLevel(i, level.Id, !level.Ascending);
                    SoundDefOf.Click.PlayOneShotOnCamera();
                    Changed();
                }

                if (DrawIcon(upRect, TexButton.ReorderUp, i > 0, "TSA_WD_AdvFilter_MoveUp".Translate(), null))
                {
                    chain.Move(i, -1);
                    Changed();
                }
                if (DrawIcon(downRect, TexButton.ReorderDown, i < chain.Count - 1, "TSA_WD_AdvFilter_MoveDown".Translate(), null))
                {
                    chain.Move(i, 1);
                    Changed();
                }
                if (DrawCloseX(removeRect, "TSA_WD_AdvFilter_RemoveLevel".Translate()))
                {
                    chain.RemoveAt(i);
                    Changed();
                    break;
                }
                listing.Gap(4f);
            }

            Rect addRow = listing.GetRect(RowH);
            Rect addRect = new Rect(addRow.x, addRow.y, 150f, RowH);
            bool canAdd = chain.Count < PawnRosterSortChain.MaxLevels && FirstUnusedOption() != null;
            if (DrawButton(addRect, "TSA_WD_AdvFilter_AddLevel".Translate(), canAdd, null))
                OpenColumnMenu(-1);
            listing.Gap(8f);
        }

        private string FirstUnusedOption()
        {
            PawnRosterSortChain chain = host.RosterView.Sort;
            for (int i = 0; i < sortOptions.Count; i++)
            {
                if (!chain.IsSorted(sortOptions[i].Id))
                    return sortOptions[i].Id;
            }
            return null;
        }

        private string LabelForSortId(string id)
        {
            for (int i = 0; i < sortOptions.Count; i++)
            {
                if (sortOptions[i].Id == id) return sortOptions[i].Label;
            }
            return id ?? "";
        }

        /// <summary><paramref name="levelIndex"/> -1 adds a new level.</summary>
        private void OpenColumnMenu(int levelIndex)
        {
            RefreshSortOptions();
            PawnRosterSortChain chain = host.RosterView.Sort;
            var options = new List<FloatMenuOption>(sortOptions.Count);
            for (int i = 0; i < sortOptions.Count; i++)
            {
                PawnRosterSortOption opt = sortOptions[i];
                int existing = chain.IndexOf(opt.Id);
                if (existing >= 0 && existing != levelIndex) continue;
                options.Add(new FloatMenuOption(opt.Label, () =>
                {
                    bool asc = PawnRosterColumnRegistry.FirstClickAscending(opt.Id);
                    if (levelIndex < 0)
                        chain.Add(opt.Id, asc);
                    else
                        chain.SetLevel(levelIndex, opt.Id, chain[levelIndex].Ascending);
                    Changed();
                }));
            }
            if (options.Count > 0)
                Find.WindowStack.Add(new FloatMenu(options));
        }

        // ---------------- Active filters ----------------

        private int CountActiveLines()
        {
            host.RosterView.CollectActiveKeys(activeKeys);
            int n = activeKeys.Count;
            if (PawnRosterTraitFilter.IsActive && host.SupportsRosterFilter(PawnRosterFilterKeys.Traits)) n++;
            return n;
        }

        private void DrawActiveFilters(Listing_Standard listing)
        {
            PawnRosterViewState view = host.RosterView;
            view.CollectActiveKeys(activeKeys);
            bool traits = PawnRosterTraitFilter.IsActive && host.SupportsRosterFilter(PawnRosterFilterKeys.Traits);
            if (activeKeys.Count == 0 && !traits)
            {
                Rect info = listing.GetRect(24f);
                GUI.color = Color.gray;
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(info, "TSA_WD_AdvFilter_NoFilters".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = GameFont.Small;
                GUI.color = Color.white;
            }

            int line = 0;
            for (int i = 0; i < activeKeys.Count; i++)
            {
                string key = activeKeys[i];
                string value = host.DescribeRosterFilter(key);
                FilterLineClick click = DrawFilterLine(listing, FilterLabel(key), value, line++, out Rect lineRect);
                if (click == FilterLineClick.Edit)
                    OpenFilterEditor(key, lineRect);
                else if (click == FilterLineClick.Clear)
                {
                    view.ClearFilter(key);
                    Changed();
                    break;
                }
            }
            if (traits)
            {
                FilterLineClick click = DrawFilterLine(listing, FilterLabel(PawnRosterFilterKeys.Traits),
                    PawnRosterTraitFilter.DescribeSelection(), line, out Rect lineRect);
                if (click == FilterLineClick.Edit)
                    OpenFilterEditor(PawnRosterFilterKeys.Traits, lineRect);
                else if (click == FilterLineClick.Clear)
                {
                    PawnRosterTraitFilter.Clear();
                    PawnRosterTraitFilter.InvalidateRosterCaches();
                    Changed();
                }
            }

            listing.Gap(4f);
            Rect clearRow = listing.GetRect(RowH);
            Rect addRect = new Rect(clearRow.x, clearRow.y, 150f, RowH);
            CollectAddableFilterKeys();
            if (DrawButton(addRect, "TSA_WD_AdvFilter_AddFilter".Translate(), addableKeys.Count > 0,
                    "TSA_WD_AdvFilter_AddFilterTip".Translate()))
                OpenAddFilterMenu(addRect);
            Rect clearRect = new Rect(clearRow.xMax - 140f, clearRow.y, 140f, RowH);
            if (DrawButton(clearRect, "TSA_WD_AdvFilter_ClearAll".Translate(), activeKeys.Count > 0 || traits, null))
            {
                view.ClearFilters();
                if (traits)
                {
                    PawnRosterTraitFilter.Clear();
                    PawnRosterTraitFilter.InvalidateRosterCaches();
                }
                Changed();
            }
        }

        private static string FilterLabel(string key)
        {
            string labelKey = PawnRosterFilterKeys.LabelKey(key);
            return labelKey.NullOrEmpty() ? key : labelKey.Translate().ToString();
        }

        private enum FilterLineClick { None, Edit, Clear }

        private static FilterLineClick DrawFilterLine(Listing_Standard listing, string label, string value, int index, out Rect textRect)
        {
            Rect row = listing.GetRect(RowH);
            if (index % 2 == 0) Widgets.DrawHighlight(row);
            Rect x = CloseXRect(row);
            textRect = new Rect(row.x, row.y, x.x - Gap - row.x, RowH);
            if (Mouse.IsOver(textRect)) Widgets.DrawHighlight(textRect);
            Rect labelRect = textRect.ContractedBy(6f, 0f);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            string text = label + ": " + (value.NullOrEmpty() ? "-" : value);
            Widgets.Label(labelRect, text.Truncate(labelRect.width));
            Text.Anchor = TextAnchor.UpperLeft;
            TooltipHandler.TipRegion(textRect, Text.CalcSize(text).x > labelRect.width
                ? text + "\n\n" + EditFilterTip
                : EditFilterTip);
            listing.Gap(2f);
            if (DrawCloseX(x, "TSA_WD_AdvFilter_ClearFilterTip".Translate()))
                return FilterLineClick.Clear;
            if (Widgets.ButtonInvisible(textRect))
            {
                SoundDefOf.Click.PlayOneShotOnCamera();
                return FilterLineClick.Edit;
            }
            return FilterLineClick.None;
        }

        private static string editFilterTip;
        private static string EditFilterTip => editFilterTip ??= "TSA_WD_AdvFilter_EditFilterTip".Translate();

        // ---------------- Filter editors ----------------

        private static readonly string[] FilterKeyOrder =
        {
            PawnRosterFilterKeys.Name, PawnRosterFilterKeys.Type, PawnRosterFilterKeys.Star, PawnRosterFilterKeys.New,
            PawnRosterFilterKeys.LocationType, PawnRosterFilterKeys.LocationName, PawnRosterFilterKeys.Source,
            PawnRosterFilterKeys.Origin, PawnRosterFilterKeys.Destination, PawnRosterFilterKeys.Reason,
            PawnRosterFilterKeys.Traits, PawnRosterFilterKeys.Xenotype, PawnRosterFilterKeys.Psycasts,
            PawnRosterFilterKeys.Ideology
        };

        /// <summary>Supported, not yet active filters whose DLC is loaded.</summary>
        private void CollectAddableFilterKeys()
        {
            addableKeys.Clear();
            PawnRosterViewState view = host.RosterView;
            for (int i = 0; i < FilterKeyOrder.Length; i++)
            {
                string key = FilterKeyOrder[i];
                if (!host.SupportsRosterFilter(key) || !FilterDlcActive(key)) continue;
                bool active = key == PawnRosterFilterKeys.Traits ? PawnRosterTraitFilter.IsActive : view.HasFilter(key);
                if (!active) addableKeys.Add(key);
            }
        }

        private static bool FilterDlcActive(string key)
        {
            switch (key)
            {
                case PawnRosterFilterKeys.Xenotype: return ModsConfig.BiotechActive;
                case PawnRosterFilterKeys.Psycasts: return ModsConfig.RoyaltyActive;
                case PawnRosterFilterKeys.Ideology: return ModsConfig.IdeologyActive;
                default: return true;
            }
        }

        private void OpenAddFilterMenu(Rect anchor)
        {
            var options = new List<FloatMenuOption>(addableKeys.Count);
            for (int i = 0; i < addableKeys.Count; i++)
            {
                string key = addableKeys[i];
                options.Add(new FloatMenuOption(FilterLabel(key), () => OpenFilterEditor(key, anchor)));
            }
            if (options.Count > 0)
                Find.WindowStack.Add(new FloatMenu(options));
        }

        private void SetString(string key, string value)
        {
            host.RosterView.SetString(key, value ?? "");
            Changed();
        }

        private void SetInt(string key, int value)
        {
            host.RosterView.SetInt(key, value);
            Changed();
        }

        /// <summary>Same editors as the table headers: text field, slate choice list, or the trait dialog.</summary>
        private void OpenFilterEditor(string key, Rect anchor)
        {
            PawnRosterViewState view = host.RosterView;
            string title = FilterLabel(key);
            switch (key)
            {
                case PawnRosterFilterKeys.Traits:
                    Find.WindowStack.Add(new Dialog_PawnRosterTraitFilter());
                    return;
                case PawnRosterFilterKeys.Type:
                    PawnRosterHeaderFilter.OpenChoiceDropdown(anchor, title,
                        PawnRosterHeaderFilter.TypeChoices((PlayerPawnTypeFilter)view.GetInt(key),
                            f => SetInt(key, (int)f), CategoriesOf(Population())));
                    return;
                case PawnRosterFilterKeys.Star:
                    if (PawnRosterViewUtil.UsesPlayerStarFilter(host.RosterWindow))
                        PawnRosterHeaderFilter.OpenChoiceDropdown(anchor, title,
                            PawnRosterHeaderFilter.PlayerStarChoices((PlayerPawnStarFilter)view.GetInt(key),
                                f => SetInt(key, (int)f), StarRowsOf(Population())), width: 280f);
                    else
                        PawnRosterHeaderFilter.OpenChoiceDropdown(anchor, title,
                            PawnRosterHeaderFilter.OutpostStarChoices((OutpostPawnStarFilter)view.GetInt(key),
                                f => SetInt(key, (int)f), StarredOf(Population())));
                    return;
                case PawnRosterFilterKeys.New:
                    PawnRosterHeaderFilter.OpenChoiceDropdown(anchor, title,
                        PawnRosterHeaderFilter.JoinedFilterChoices((PawnRosterJoinedFilter)view.GetInt(key),
                            f => SetInt(key, (int)f), JoinDaysOf(Population())), width: 300f);
                    return;
                case PawnRosterFilterKeys.Source:
                    PawnRosterHeaderFilter.OpenChoiceDropdown(anchor, title,
                        PawnRosterHeaderFilter.PrisonerSourceChoices((PrisonerRosterSourceFilter)view.GetInt(key),
                            f => SetInt(key, (int)f)));
                    return;
                case PawnRosterFilterKeys.LocationType:
                    PawnRosterHeaderFilter.OpenChoiceDropdown(anchor, title,
                        PawnRosterHeaderFilter.LocationTypeChoices(view.GetString(key), v => SetString(key, v)));
                    return;
                case PawnRosterFilterKeys.Xenotype:
                {
                    List<Pawn> pop = Population();
                    var keys = new List<string>(pop.Count);
                    for (int i = 0; i < pop.Count; i++) keys.Add(PawnRosterHeaderFilter.XenotypeKey(pop[i]));
                    PawnRosterHeaderFilter.OpenChoiceDropdown(anchor, title,
                        PawnRosterHeaderFilter.XenotypeChoices(view.GetString(key), v => SetString(key, v), keys));
                    return;
                }
                case PawnRosterFilterKeys.Psycasts:
                {
                    List<Pawn> pop = Population();
                    var lists = new List<List<string>>(pop.Count);
                    for (int i = 0; i < pop.Count; i++) lists.Add(PawnRosterHeaderFilter.PsycastKeysOnPawn(pop[i]));
                    PawnRosterHeaderFilter.OpenChoiceDropdown(anchor, title,
                        PawnRosterHeaderFilter.PsycastChoices(view.GetString(key), v => SetString(key, v), lists));
                    return;
                }
                case PawnRosterFilterKeys.Ideology:
                {
                    List<Pawn> pop = Population();
                    var keys = new List<string>(pop.Count);
                    for (int i = 0; i < pop.Count; i++) keys.Add(PawnRosterHeaderFilter.IdeoKey(pop[i]));
                    PawnRosterHeaderFilter.OpenChoiceDropdown(anchor, title,
                        PawnRosterHeaderFilter.IdeoChoices(view.GetString(key), v => SetString(key, v), keys));
                    return;
                }
                default:
                    PawnRosterHeaderFilter.OpenTextDropdown(anchor, title, SearchHintFor(key),
                        () => view.GetString(key),
                        v => SetString(key, v),
                        () =>
                        {
                            view.ClearFilter(key);
                            Changed();
                        });
                    return;
            }
        }

        private static string SearchHintFor(string key) =>
            key == PawnRosterFilterKeys.LocationName || key == PawnRosterFilterKeys.Origin
            || key == PawnRosterFilterKeys.Destination
                ? "TSA_WD_AllPlayerPawns_SearchLocation".Translate()
                : "TSA_WD_AllPlayerPawns_SearchName".Translate();

        /// <summary>Host pawns, gathered once per editor open (not per frame).</summary>
        private List<Pawn> Population()
        {
            population.Clear();
            host.CollectRosterPawns(population);
            return population;
        }

        private static List<PlayerPawnSortCategory> CategoriesOf(List<Pawn> pawns)
        {
            var list = new List<PlayerPawnSortCategory>(pawns.Count);
            for (int i = 0; i < pawns.Count; i++) list.Add(PlayerPawnRosterUtility.ClassifyPawn(pawns[i]));
            return list;
        }

        private static List<StarCountRow> StarRowsOf(List<Pawn> pawns)
        {
            var list = new List<StarCountRow>(pawns.Count);
            for (int i = 0; i < pawns.Count; i++)
            {
                Map map = pawns[i].MapHeld;
                list.Add(new StarCountRow(PawnRosterRowData.Build(pawns[i]).isStarred, map != null && map.IsPlayerHome));
            }
            return list;
        }

        private static List<bool> StarredOf(List<Pawn> pawns)
        {
            var list = new List<bool>(pawns.Count);
            for (int i = 0; i < pawns.Count; i++) list.Add(PawnRosterRowData.Build(pawns[i]).isStarred);
            return list;
        }

        private static List<int> JoinDaysOf(List<Pawn> pawns)
        {
            var list = new List<int>(pawns.Count);
            for (int i = 0; i < pawns.Count; i++) list.Add(PawnRosterRowData.Build(pawns[i]).daysSinceJoin);
            return list;
        }

        // ---------------- Widgets ----------------

        private static bool DrawButton(Rect r, string label, bool enabled, string tip)
        {
            if (!tip.NullOrEmpty()) TooltipHandler.TipRegion(r, tip);
            if (!enabled)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.45f);
                Widgets.ButtonText(r, label, active: false);
                GUI.color = Color.white;
                return false;
            }
            if (!Widgets.ButtonText(r, label)) return false;
            SoundDefOf.Click.PlayOneShotOnCamera();
            return true;
        }

        private static Rect CloseXRect(Rect row) =>
            new Rect(row.xMax - CloseXRightInset - CloseXSize, row.y + (row.height - CloseXSize) * 0.5f, CloseXSize, CloseXSize);

        private static bool DrawCloseX(Rect r, string tip)
        {
            if (!tip.NullOrEmpty()) TooltipHandler.TipRegion(r, tip);
            if (!Widgets.ButtonImage(r, TexButton.CloseXSmall)) return false;
            SoundDefOf.Click.PlayOneShotOnCamera();
            return true;
        }

        private static bool DrawIcon(Rect r, Texture2D tex, bool enabled, string tip, Color? tint)
        {
            if (!tip.NullOrEmpty()) TooltipHandler.TipRegion(r, tip);
            Color prev = GUI.color;
            if (!enabled)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.3f);
                GUI.DrawTexture(r, tex ?? BaseContent.BadTex);
                GUI.color = prev;
                return false;
            }
            bool clicked = tint.HasValue
                ? Widgets.ButtonImage(r, tex ?? BaseContent.BadTex, tint.Value)
                : Widgets.ButtonImage(r, tex ?? BaseContent.BadTex);
            GUI.color = prev;
            if (clicked) SoundDefOf.Click.PlayOneShotOnCamera();
            return clicked;
        }
    }
}
