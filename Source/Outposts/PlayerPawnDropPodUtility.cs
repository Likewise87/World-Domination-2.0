using System;
using System.Collections.Generic;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Shared drop-pod cost, fallback, allocation, and UI for player pawn launches
    /// (transfer, smart send, RR, establish, recruit journey).
    /// </summary>
    public static class PlayerPawnDropPodUtility
    {
        /// <summary>
        /// Industrial components per drop-pod slot when the economic setting is on.
        /// Pawn transport: one slot per traveling pawn. Goods / upgrade / trade: one slot per traveler.
        /// Returns 0 when <see cref="WorldDominationSettings.dropPodTransportCostComponents"/> is off.
        /// </summary>
        public static int ComponentCostPerLaunch =>
            WorldDominationMod.settings == null || WorldDominationMod.settings.dropPodTransportCostComponents
                ? 1
                : 0;

        public enum LaunchMode
        {
            Pod,
            Land,
            Abort
        }

        /// <summary>
        /// What to do with launches that run out of components.
        /// Automated sends honour the origin's fallback checkbox; manual actions always preview land,
        /// because their "Still launch?" dialog already lets the player back out instead.
        /// </summary>
        public enum ShortStockFallback
        {
            PerOrigin,
            AlwaysLand
        }

        public struct OriginAllocation
        {
            public WorldObject origin;
            public LaunchMode mode;
        }

        public struct AllocationResult
        {
            public int need;
            public int have;
            public int podCount;
            public int landCount;
            public int abortCount;
            public List<OriginAllocation> rows;
        }

        public static ThingDef ComponentDef => ThingDefOf.ComponentIndustrial;

        public static int CountComponentsAvailable()
        {
            ThingDef def = ComponentDef;
            if (def == null) return 0;
            return ColonyWarehouseStockUtility.CountAvailable(
                ColonyWarehouseStockUtility.GetColonyMap(),
                ColonyWarehouseStockUtility.GetAllWarehouses(),
                def);
        }

        public static bool TryConsumeComponents(int count, out string reason)
        {
            reason = null;
            if (count <= 0) return true;
            ThingDef def = ComponentDef;
            if (def == null)
            {
                reason = "TSA_WD_PawnDropPod_NoComponentDef".Translate();
                return false;
            }
            return ColonyWarehouseStockUtility.TryConsume(
                ColonyWarehouseStockUtility.GetColonyMap(),
                ColonyWarehouseStockUtility.GetAllWarehouses(),
                def,
                count,
                out reason);
        }

        /// <summary>
        /// Land vs drop pod for ad-hoc player actions (transfer, smart send, remote establish).
        /// One global choice, set from the All Player Pawns toolbar. Session only, not scribed.
        /// </summary>
        public static bool AdHocViaDropPod { get; private set; }

        public static void SetAdHocViaDropPod(bool value) => AdHocViaDropPod = value;

        /// <summary>Sets ad-hoc pod mode only when Transport Pods is researched; otherwise shows a reject message.</summary>
        public static bool TrySetAdHocViaDropPod(bool value)
        {
            if (value && !RapidResponseUtility.TransportPodsResearched())
            {
                Messages.Message("TSA_WD_RapidResponse_DropPodsNeedResearch".Translate(), MessageTypeDefOf.RejectInput);
                return false;
            }
            AdHocViaDropPod = value;
            return true;
        }

        /// <summary>Land vs drop pod for automated pawn sends from this origin (prisoner recruits, recruiting redirect).</summary>
        public static bool GetTravelViaDropPod(WorldObject origin)
        {
            if (origin is WorldObject_WD_Outpost outpost)
                return outpost.pawnTravelViaDropPod;
            return CompPlayerDispatchMode.Get(origin)?.pawnTravelViaDropPod ?? false;
        }

        public static void SetTravelViaDropPod(WorldObject origin, bool value)
        {
            if (origin == null) return;
            if (origin is WorldObject_WD_Outpost outpost)
            {
                outpost.pawnTravelViaDropPod = value;
                return;
            }
            CompPlayerDispatchMode colony = CompPlayerDispatchMode.Get(origin);
            if (colony != null)
                colony.pawnTravelViaDropPod = value;
        }

        /// <summary>Sets pod mode only when Transport Pods is researched; otherwise shows a reject message.</summary>
        public static bool TrySetTravelViaDropPod(WorldObject origin, bool value)
        {
            if (value && !RapidResponseUtility.TransportPodsResearched())
            {
                Messages.Message("TSA_WD_RapidResponse_DropPodsNeedResearch".Translate(), MessageTypeDefOf.RejectInput);
                return false;
            }
            SetTravelViaDropPod(origin, value);
            return true;
        }

        public static void SetTravelViaDropPodMany(IEnumerable<WorldObject> origins, bool value)
        {
            if (origins == null) return;
            if (value && !RapidResponseUtility.TransportPodsResearched())
            {
                Messages.Message("TSA_WD_RapidResponse_DropPodsNeedResearch".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }
            foreach (WorldObject o in origins)
                SetTravelViaDropPod(o, value);
        }

        /// <summary>True when all origins share the same travel mode; mixed when values differ.</summary>
        public static bool TryGetSharedTravelMode(IReadOnlyList<WorldObject> origins, out bool value, out bool mixed)
        {
            value = false;
            mixed = false;
            if (origins == null || origins.Count == 0) return false;
            bool first = true;
            for (int i = 0; i < origins.Count; i++)
            {
                WorldObject o = origins[i];
                if (o == null || o.Destroyed) continue;
                bool v = GetTravelViaDropPod(o);
                if (first)
                {
                    value = v;
                    first = false;
                }
                else if (v != value)
                {
                    mixed = true;
                    return true;
                }
            }
            return !first;
        }

        public static bool GetFallbackToLand(WorldObject origin)
        {
            if (origin is WorldObject_WD_Outpost outpost)
                return outpost.dropPodFallbackToLand;
            CompPlayerDispatchMode colony = CompPlayerDispatchMode.Get(origin);
            return colony == null || colony.dropPodFallbackToLand;
        }

        public static void SetFallbackToLand(WorldObject origin, bool value)
        {
            if (origin == null) return;
            if (origin is WorldObject_WD_Outpost outpost)
            {
                outpost.dropPodFallbackToLand = value;
                return;
            }
            CompPlayerDispatchMode colony = CompPlayerDispatchMode.Get(origin);
            if (colony != null)
                colony.dropPodFallbackToLand = value;
        }

        public static void SetFallbackToLandMany(IEnumerable<WorldObject> origins, bool value)
        {
            if (origins == null) return;
            foreach (WorldObject o in origins)
                SetFallbackToLand(o, value);
        }

        /// <summary>True when all origins share the same fallback; mixed when values differ.</summary>
        public static bool TryGetSharedFallback(IReadOnlyList<WorldObject> origins, out bool value, out bool mixed)
        {
            value = true;
            mixed = false;
            if (origins == null || origins.Count == 0) return false;
            bool first = true;
            for (int i = 0; i < origins.Count; i++)
            {
                WorldObject o = origins[i];
                if (o == null || o.Destroyed) continue;
                bool v = GetFallbackToLand(o);
                if (first)
                {
                    value = v;
                    first = false;
                }
                else if (v != value)
                {
                    mixed = true;
                    return true;
                }
            }
            return !first;
        }

        /// <summary>
        /// Tile validity for drop-pod destinations. Range is unlimited (distance is not checked).
        /// </summary>
        public static bool InDropPodRange(PlanetTile from, int toTileId)
        {
            if (toTileId < 0 || !Find.WorldGrid.InBounds(toTileId)) return false;
            return true;
        }

        public static bool InDropPodRange(WorldObject origin, int toTileId)
        {
            if (origin == null) return false;
            return InDropPodRange(origin.Tile, toTileId);
        }

        /// <summary>
        /// Greedy per-slot allocation: each list entry is one drop-pod slot (one pawn, or one goods
        /// traveler). Same origin may repeat. Order is preserved so rows align with the input list.
        /// </summary>
        public static AllocationResult Allocate(
            IReadOnlyList<WorldObject> launchesWantingPod,
            ShortStockFallback fallback = ShortStockFallback.PerOrigin,
            int haveOverride = -1)
        {
            var result = new AllocationResult
            {
                rows = new List<OriginAllocation>(),
                have = haveOverride >= 0 ? haveOverride : CountComponentsAvailable()
            };
            if (launchesWantingPod == null || launchesWantingPod.Count == 0)
                return result;

            result.need = 0;
            int remaining = result.have;
            int costPer = ComponentCostPerLaunch;
            for (int i = 0; i < launchesWantingPod.Count; i++)
            {
                WorldObject origin = launchesWantingPod[i];
                if (origin == null || origin.Destroyed) continue;
                result.need += costPer;

                LaunchMode mode;
                if (costPer <= 0 || remaining >= costPer)
                {
                    mode = LaunchMode.Pod;
                    if (costPer > 0) remaining -= costPer;
                    result.podCount++;
                }
                else if (fallback == ShortStockFallback.AlwaysLand || GetFallbackToLand(origin))
                {
                    mode = LaunchMode.Land;
                    result.landCount++;
                }
                else
                {
                    mode = LaunchMode.Abort;
                    result.abortCount++;
                }
                result.rows.Add(new OriginAllocation { origin = origin, mode = mode });
            }
            return result;
        }

        /// <summary>Append <paramref name="origin"/> once per pawn / traveler slot.</summary>
        public static void AddOriginSlots(List<WorldObject> launches, WorldObject origin, int slotCount)
        {
            if (launches == null || origin == null || slotCount <= 0) return;
            for (int i = 0; i < slotCount; i++)
                launches.Add(origin);
        }

        /// <summary>
        /// Split items by allocation modes starting at <paramref name="rowIndex"/>.
        /// Returns the next row index after these items.
        /// </summary>
        public static int PartitionByModes<T>(
            IReadOnlyList<T> items,
            AllocationResult alloc,
            int rowIndex,
            List<T> pod,
            List<T> land,
            List<T> abort)
        {
            if (items == null) return rowIndex;
            for (int i = 0; i < items.Count; i++)
            {
                LaunchMode mode = (alloc.rows != null && rowIndex + i < alloc.rows.Count)
                    ? alloc.rows[rowIndex + i].mode
                    : LaunchMode.Abort;
                if (mode == LaunchMode.Pod) pod?.Add(items[i]);
                else if (mode == LaunchMode.Land) land?.Add(items[i]);
                else abort?.Add(items[i]);
            }
            return rowIndex + items.Count;
        }

        /// <summary>First matching row for this origin. Prefer <see cref="PartitionByModes{T}"/> when one origin has mixed modes.</summary>
        public static LaunchMode ModeForOrigin(AllocationResult alloc, WorldObject origin)
        {
            if (alloc.rows == null || origin == null) return LaunchMode.Abort;
            for (int i = 0; i < alloc.rows.Count; i++)
            {
                if (alloc.rows[i].origin == origin)
                    return alloc.rows[i].mode;
            }
            return LaunchMode.Abort;
        }

        public static bool AllWouldAbort(AllocationResult alloc) =>
            alloc.need > 0 && alloc.podCount == 0 && alloc.landCount == 0 && alloc.abortCount == alloc.need;

        public static string BuildStillLaunchBody(AllocationResult alloc)
        {
            var sb = new StringBuilder();
            sb.AppendLine("TSA_WD_PawnDropPod_StillLaunchNeedHave".Translate(
                alloc.need.ToString(),
                alloc.have.ToString()));
            if (alloc.podCount > 0)
                sb.AppendLine("TSA_WD_PawnDropPod_StillLaunchPodCount".Translate(alloc.podCount.ToString()));
            if (alloc.landCount > 0)
                sb.AppendLine("TSA_WD_PawnDropPod_StillLaunchLandCount".Translate(alloc.landCount.ToString()));
            if (alloc.abortCount > 0)
                sb.AppendLine("TSA_WD_PawnDropPod_StillLaunchAbortCount".Translate(alloc.abortCount.ToString()));
            sb.Append("TSA_WD_PawnDropPod_StillLaunchAsk".Translate());
            return sb.ToString();
        }

        /// <summary>
        /// Manual short-stock gate. Invokes <paramref name="onProceed"/> with the allocation after Yes,
        /// or immediately when stock covers all launches. Cancels when all would abort.
        /// Always previews land for short launches: this dialog is the player's abort, so the
        /// per-origin fallback checkbox (which governs automated sends) must not change a manual action.
        /// </summary>
        public static void ConfirmIfShortThen(
            IReadOnlyList<WorldObject> originsWantingPod,
            Action<AllocationResult> onProceed,
            Action onCancel = null)
        {
            AllocationResult alloc = Allocate(originsWantingPod, ShortStockFallback.AlwaysLand);
            if (alloc.need <= 0)
            {
                onProceed?.Invoke(alloc);
                return;
            }
            if (AllWouldAbort(alloc))
            {
                Messages.Message("TSA_WD_PawnDropPod_AllWouldAbort".Translate(), MessageTypeDefOf.RejectInput, false);
                onCancel?.Invoke();
                return;
            }
            if (alloc.have >= alloc.need)
            {
                onProceed?.Invoke(alloc);
                return;
            }

            Find.WindowStack.Add(new Dialog_MessageBox(
                BuildStillLaunchBody(alloc),
                "Confirm".Translate(),
                () => onProceed?.Invoke(alloc),
                "GoBack".Translate(),
                onCancel,
                title: "TSA_WD_PawnDropPod_StillLaunchTitle".Translate(),
                buttonADestructive: false));
        }

        /// <summary>
        /// If hostile T4 flak threatens this flight, show a confirmation and run <paramref name="onConfirm"/> only if accepted.
        /// Returns true when a dialog was shown (caller should not launch immediately).
        /// </summary>
        public static bool ConfirmHostileAaThen(int originTile, int destTile, Action onConfirm)
        {
            var flights = new List<(int originTile, int destTile)>(1) { (originTile, destTile) };
            return ConfirmHostileAaThen(flights, onConfirm);
        }

        /// <summary>
        /// Multi-origin / multi-dest: union hostile AA threats across flights. Warn if any flight is threatened.
        /// Returns true when a dialog was shown (caller should not also launch). When false, caller must run
        /// <paramref name="onConfirm"/> itself (same contract as Rapid Response).
        /// </summary>
        public static bool ConfirmHostileAaThen(
            IReadOnlyList<(int originTile, int destTile)> flights,
            Action onConfirm)
        {
            if (onConfirm == null || flights == null || flights.Count == 0)
                return false;

            var threats = new List<Settlement>();
            var seen = new HashSet<int>();
            var scratch = new List<Settlement>();
            for (int i = 0; i < flights.Count; i++)
            {
                int origin = flights[i].originTile;
                int dest = flights[i].destTile;
                if (origin < 0 || dest < 0) continue;
                if (!AntiAirFireUtils.TryGetHostileSettlementAaThreatsForDropPodFlight(origin, dest, scratch)
                    || scratch.Count == 0)
                    continue;
                for (int t = 0; t < scratch.Count; t++)
                {
                    Settlement s = scratch[t];
                    if (s == null || s.Destroyed) continue;
                    if (!seen.Add(s.ID)) continue;
                    threats.Add(s);
                }
            }

            if (threats.Count == 0)
                return false;

            string names = threats[0].LabelCap;
            for (int i = 1; i < threats.Count; i++)
                names += ", " + threats[i].LabelCap;

            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                "TSA_WD_RapidResponse_DropPodsAaWarning".Translate(names),
                onConfirm,
                destructive: true));
            return true;
        }

        public static void NotifyLackingMaterials(Pawn pawn, WorldObject origin)
        {
            string pawnLabel = pawn?.LabelShortCap ?? "?";
            string originLabel = origin?.LabelCap ?? "?";
            Messages.Message(
                "TSA_WD_PawnDropPod_LackingMaterials".Translate(pawnLabel, originLabel),
                MessageTypeDefOf.CautionInput,
                false);
        }

        public static void NotifyLackingMaterialsGroup(IReadOnlyList<Pawn> pawns, WorldObject origin)
        {
            if (pawns == null || pawns.Count == 0)
            {
                NotifyLackingMaterials(null, origin);
                return;
            }
            Pawn first = null;
            for (int i = 0; i < pawns.Count; i++)
            {
                if (pawns[i] != null && !pawns[i].Destroyed)
                {
                    first = pawns[i];
                    break;
                }
            }
            if (pawns.Count == 1 || first == null)
            {
                NotifyLackingMaterials(first, origin);
                return;
            }
            string originLabel = origin?.LabelCap ?? "?";
            Messages.Message(
                "TSA_WD_PawnDropPod_LackingMaterialsGroup".Translate(
                    first.LabelShortCap,
                    (pawns.Count - 1).ToString(),
                    originLabel),
                MessageTypeDefOf.CautionInput,
                false);
        }
        public const float ModeIconSize = 28f;
        public const float FallbackCheckboxSize = 24f;
        public const float ModeFallbackGap = 6f;
        public static float FallbackToolbarWidth => ToolbarWidth(showFallback: true);

        public static float ToolbarWidth(bool showFallback) =>
            showFallback
                ? ModeIconSize + ModeFallbackGap + FallbackCheckboxSize
                : ModeIconSize;

        private static Texture2D ModeIcon(bool viaDropPod) =>
            viaDropPod ? WITab_Outpost_WarehouseAssets.DropPodIcon : WITab_Outpost_WarehouseAssets.LandIcon;

        /// <summary>
        /// Close WD hubs and the inspect/main tab so world targeting is usable, then jump camera.
        /// </summary>
        public static void PrepareWorldMapDestinationPick(WorldObject cameraOrigin = null)
        {
            WdNavWindows.CloseAllNavWindows(escapeMainTab: true);
            WdWindowEsc.ClearTextFocus();
            Find.WorldSelector?.ClearSelection();
            if (cameraOrigin != null && !cameraOrigin.Destroyed)
                CameraJumper.TryJump(cameraOrigin.Tile);
            else
                CameraJumper.TryShowWorld();
        }

        /// <summary>Shared hover tip for land / drop-pod mode (1 component when pod + AA warning).</summary>
        public static string BuildDispatchModeTip(bool viaDropPod, bool researched = true, string disabledPodTip = null)
        {
            if (!researched)
                return "TSA_WD_DispatchMode_NeedsResearch".Translate();
            if (!viaDropPod && !string.IsNullOrEmpty(disabledPodTip))
                return disabledPodTip;
            if (viaDropPod)
            {
                return "TSA_WD_PawnDropPod_ModeDropPodTip".Translate().ToString()
                    + "\n\n"
                    + "TSA_WD_DispatchMode_DropPodAaWarning".Translate();
            }
            return "TSA_WD_PawnDropPod_ModeLandTip".Translate();
        }

        /// <summary>Goods dispatch tip: 1 component per traveler/launch, plus auto-ship free note when pod.</summary>
        public static string BuildGoodsDispatchModeTip(bool viaDropPod, bool researched = true)
        {
            if (!researched)
                return "TSA_WD_DispatchMode_NeedsResearch".Translate();
            if (!viaDropPod)
                return "TSA_WD_DispatchMode_LandDesc".Translate();
            string tip = "TSA_WD_DispatchMode_DropPodDesc".Translate().ToString()
                + "\n\n"
                + "TSA_WD_DispatchMode_DropPodAaWarning".Translate()
                + "\n\n"
                + "TSA_WD_DispatchMode_AutoShipFreeNote".Translate();
            return tip;
        }

        /// <summary>Global ad-hoc travel mode as a warehouse-style land/drop-pod icon. Float menu on click, no label.</summary>
        public static void DrawAdHocModeIcon(Rect rect, bool allowDropPod = true, string disabledPodTip = null)
        {
            bool researched = RapidResponseUtility.TransportPodsResearched();
            Rect iconRect = new Rect(
                rect.x,
                rect.y + (rect.height - ModeIconSize) * 0.5f,
                ModeIconSize,
                ModeIconSize);

            if (WorldDomination_UIUtils.ButtonIconOnly(
                    iconRect, ModeIcon(AdHocViaDropPod), null, WorldOverlayLineMaterials.DarkCyanColor))
                OpenAdHocModeMenu(allowDropPod && researched, researched, disabledPodTip);
            TooltipHandler.TipRegion(
                iconRect,
                BuildModeTip(AdHocViaDropPod, allowDropPod, researched, disabledPodTip));
        }

        /// <summary>Icon-only land/drop-pod control for goods dispatch (warehouse / gear / colony gizmo tips).</summary>
        public static void DrawGoodsDispatchModeIcon(
            Rect rect,
            bool viaDropPod,
            Action setLand,
            Action setPod)
        {
            bool researched = RapidResponseUtility.TransportPodsResearched();
            Rect iconRect = new Rect(
                rect.x,
                rect.y + (rect.height - ModeIconSize) * 0.5f,
                ModeIconSize,
                ModeIconSize);
            if (WorldDomination_UIUtils.ButtonIconOnly(
                    iconRect, ModeIcon(viaDropPod), null, WorldOverlayLineMaterials.DarkCyanColor))
            {
                OpenGoodsDispatchModeMenu(viaDropPod, researched, setLand, setPod);
            }
            TooltipHandler.TipRegion(iconRect, BuildGoodsDispatchModeTip(viaDropPod, researched));
        }

        private static void OpenGoodsDispatchModeMenu(
            bool viaDropPod,
            bool researched,
            Action setLand,
            Action setPod)
        {
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption(
                    "TSA_WD_DispatchMode_Land".Translate(),
                    () =>
                    {
                        if (!viaDropPod) return;
                        setLand?.Invoke();
                        SoundDefOf.Click.PlayOneShotOnCamera();
                    },
                    WITab_Outpost_WarehouseAssets.LandIcon,
                    WorldOverlayLineMaterials.DarkCyanColor)
            };

            var podOpt = new FloatMenuOption(
                "TSA_WD_DispatchMode_DropPod".Translate(),
                () =>
                {
                    if (viaDropPod) return;
                    if (!researched)
                    {
                        Messages.Message("TSA_WD_DispatchMode_NeedsResearch".Translate(), MessageTypeDefOf.RejectInput);
                        return;
                    }
                    setPod?.Invoke();
                    SoundDefOf.Click.PlayOneShotOnCamera();
                },
                WITab_Outpost_WarehouseAssets.DropPodIcon,
                WorldOverlayLineMaterials.DarkCyanColor);
            if (!researched)
            {
                podOpt.Disabled = true;
                podOpt.tooltip = new TipSignal("TSA_WD_DispatchMode_NeedsResearch".Translate());
            }
            options.Add(podOpt);
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static string BuildModeTip(bool viaDropPod, bool allowDropPod, bool researched, string disabledPodTip)
        {
            if (!researched)
                return "TSA_WD_DispatchMode_NeedsResearch".Translate();
            if (!allowDropPod && !string.IsNullOrEmpty(disabledPodTip))
                return disabledPodTip;
            return BuildDispatchModeTip(viaDropPod, researched);
        }

        private static void OpenAdHocModeMenu(bool podEnabled, bool researched, string disabledPodTip)
        {
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption(
                    "TSA_WD_DispatchMode_Land".Translate(),
                    () =>
                    {
                        if (!AdHocViaDropPod) return;
                        SetAdHocViaDropPod(false);
                        SoundDefOf.Click.PlayOneShotOnCamera();
                    },
                    WITab_Outpost_WarehouseAssets.LandIcon,
                    WorldOverlayLineMaterials.DarkCyanColor)
            };

            var podOpt = new FloatMenuOption(
                "TSA_WD_DispatchMode_DropPod".Translate(),
                () =>
                {
                    if (AdHocViaDropPod) return;
                    if (TrySetAdHocViaDropPod(true))
                        SoundDefOf.Click.PlayOneShotOnCamera();
                },
                WITab_Outpost_WarehouseAssets.DropPodIcon,
                WorldOverlayLineMaterials.DarkCyanColor);
            if (!podEnabled)
            {
                podOpt.Disabled = true;
                string tip = !researched
                    ? "TSA_WD_DispatchMode_NeedsResearch".Translate().ToString()
                    : (disabledPodTip ?? "TSA_WD_PawnDropPod_OutOfRange".Translate().ToString());
                podOpt.tooltip = new TipSignal(tip);
            }
            options.Add(podOpt);
            Find.WindowStack.Add(new FloatMenu(options));
        }

        /// <summary>
        /// Travel-mode icon, then if drop pod two lines: Cost + Available (colony + warehouses) with component icons.
        /// Returns height used.
        /// </summary>
        public static float DrawModeAndTotalCostLine(
            Rect rect,
            bool viaDropPod,
            int launchCount,
            bool interactiveModeIcon = false,
            bool allowDropPod = true,
            string disabledPodTip = null)
        {
            const float gap = 8f;
            const float componentIconSize = 20f;
            const float lineGap = 2f;

            bool researched = RapidResponseUtility.TransportPodsResearched();
            Rect modeRect = new Rect(rect.x, rect.y, ModeIconSize, ModeIconSize);
            if (interactiveModeIcon)
            {
                DrawAdHocModeIcon(modeRect, allowDropPod, disabledPodTip);
                viaDropPod = AdHocViaDropPod;
            }
            else
            {
                Texture2D icon = ModeIcon(viaDropPod);
                if (icon != null)
                {
                    Color prev = GUI.color;
                    GUI.color = WorldOverlayLineMaterials.DarkCyanColor;
                    GUI.DrawTexture(modeRect, icon, ScaleMode.ScaleToFit);
                    GUI.color = prev;
                }
                TooltipHandler.TipRegion(modeRect, BuildModeTip(viaDropPod, allowDropPod, researched, disabledPodTip));
            }

            if (!viaDropPod)
                return ModeIconSize;

            Text.Font = GameFont.Small;
            float lineH = Mathf.Max(18f, Text.CalcSize("Ay").y);
            float contentH = Mathf.Max(ModeIconSize, lineH * 2f + lineGap);

            int have = CountComponentsAvailable();
            int need = Mathf.Max(0, launchCount) * ComponentCostPerLaunch;
            bool shortStock = have < need;
            ThingDef component = ComponentDef;

            float x = modeRect.xMax + gap;
            float textMaxW = Mathf.Max(80f, rect.xMax - x - componentIconSize - 8f);
            string costText = "TSA_WD_PawnDropPod_TotalCost".Translate(need.ToString());
            string availText = "TSA_WD_PawnDropPod_StockLine".Translate(have.ToString());

            Color prevColor = GUI.color;
            GUI.color = shortStock ? new Color(1f, 0.75f, 0.35f) : Color.white;
            Text.Anchor = TextAnchor.MiddleLeft;

            float y0 = rect.y;
            Rect costLabelRect = new Rect(x, y0, textMaxW, lineH);
            Widgets.Label(costLabelRect, costText.Truncate(textMaxW));
            if (component != null)
            {
                Rect costIcon = new Rect(
                    x + Mathf.Min(textMaxW, Text.CalcSize(costText).x) + 4f,
                    y0 + (lineH - componentIconSize) * 0.5f,
                    componentIconSize,
                    componentIconSize);
                Widgets.ThingIcon(costIcon, component);
            }

            float y1 = y0 + lineH + lineGap;
            Rect availLabelRect = new Rect(x, y1, textMaxW, lineH);
            Widgets.Label(availLabelRect, availText.Truncate(textMaxW));
            if (component != null)
            {
                Rect availIcon = new Rect(
                    x + Mathf.Min(textMaxW, Text.CalcSize(availText).x) + 4f,
                    y1 + (lineH - componentIconSize) * 0.5f,
                    componentIconSize,
                    componentIconSize);
                Widgets.ThingIcon(availIcon, component);
            }

            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = prevColor;
            Text.Font = GameFont.Small;

            Rect tipRect = new Rect(x, rect.y, Mathf.Max(8f, rect.xMax - x), contentH);
            if (shortStock)
                TooltipHandler.TipRegion(tipRect, "TSA_WD_PawnDropPod_ShortStockNote".Translate());

            return contentH;
        }

        /// <summary>Ad-hoc mode icon plus Total Cost on one line. Returns height used.</summary>
        public static float DrawAdHocModeAndCostStrip(
            Rect rect,
            int launchCount,
            bool allowDropPod = true,
            string disabledPodTip = null)
        {
            return DrawModeAndTotalCostLine(
                rect,
                AdHocViaDropPod,
                launchCount,
                interactiveModeIcon: true,
                allowDropPod: allowDropPod,
                disabledPodTip: disabledPodTip);
        }

        /// <summary>Non-interactive mode icon plus Total Cost (confirm dialogs). Returns height used.</summary>
        public static float DrawModeReadoutWithTotalCost(Rect rect, bool viaDropPod, int launchCount)
        {
            return DrawModeAndTotalCostLine(rect, viaDropPod, launchCount, interactiveModeIcon: false);
        }

        /// <summary>Obsolete multi-line cost stack; prefer <see cref="DrawModeAndTotalCostLine"/>.</summary>
        public static float DrawCostLines(Rect rect, bool viaDropPod, int launchCount)
        {
            return DrawModeAndTotalCostLine(rect, viaDropPod, launchCount, interactiveModeIcon: false);
        }

        /// <summary>Non-interactive mode icon plus its name, for confirm dialogs.</summary>
        public static void DrawModeReadout(Rect rect, bool viaDropPod)
        {
            Rect iconRect = new Rect(
                rect.x,
                rect.y + (rect.height - ModeIconSize) * 0.5f,
                ModeIconSize,
                ModeIconSize);
            Texture2D icon = ModeIcon(viaDropPod);
            if (icon != null)
            {
                Color prev = GUI.color;
                GUI.color = WorldOverlayLineMaterials.DarkCyanColor;
                GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit);
                GUI.color = prev;
            }

            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(
                new Rect(iconRect.xMax + 8f, rect.y, rect.width - ModeIconSize - 8f, rect.height),
                "TSA_WD_PawnDropPod_SmartSendModeLine".Translate(
                    viaDropPod
                        ? "TSA_WD_DispatchMode_DropPod".Translate()
                        : "TSA_WD_DispatchMode_Land".Translate()));
            Text.Anchor = TextAnchor.UpperLeft;
            TooltipHandler.TipRegion(rect, BuildModeTip(viaDropPod, true, true, null));
        }

        /// <summary>
        /// Automated sends from these origins: land/drop-pod mode icon plus an unlabeled
        /// fallback-to-land checkbox (checkbox only when drop pod / mixed). No labels, tooltips only.
        /// </summary>
        public static void DrawOriginModeAndFallback(
            Rect rect,
            IReadOnlyList<WorldObject> origins,
            bool enabled = true,
            string disabledTip = null)
        {
            Color cyan = WorldOverlayLineMaterials.DarkCyanColor;
            bool hasOrigins = TryGetSharedTravelMode(origins, out bool viaPod, out bool modeMixed);
            bool interactive = enabled && hasOrigins;
            bool showFallback = interactive && (viaPod || modeMixed);

            Rect iconRect = new Rect(rect.x, rect.y + (rect.height - ModeIconSize) * 0.5f, ModeIconSize, ModeIconSize);
            Rect checkRect = new Rect(
                iconRect.xMax + ModeFallbackGap,
                rect.y + (rect.height - FallbackCheckboxSize) * 0.5f,
                FallbackCheckboxSize,
                FallbackCheckboxSize);

            if (!interactive)
            {
                string unavailableTip = disabledTip ?? "TSA_WD_PawnDropPod_FallbackSelectOriginsTip".Translate();
                bool wasEnabled = GUI.enabled;
                GUI.enabled = false;
                WorldDomination_UIUtils.ButtonIconOnly(iconRect, ModeIcon(hasOrigins && !modeMixed && viaPod), null, cyan);
                GUI.enabled = wasEnabled;
                TooltipHandler.TipRegion(iconRect, unavailableTip);
                return;
            }

            if (WorldDomination_UIUtils.ButtonIconOnly(iconRect, ModeIcon(!modeMixed && viaPod), null, cyan))
                OpenOriginModeMenu(origins, modeMixed, viaPod);

            TooltipHandler.TipRegion(iconRect, modeMixed
                ? "TSA_WD_PawnDropPod_ModeOriginTipMixed".Translate()
                : BuildDispatchModeTip(viaPod));

            if (showFallback)
                DrawFallbackCheckbox(checkRect, origins);
        }

        public static void DrawOriginModeAndFallbackSingle(
            Rect rect,
            WorldObject origin,
            bool enabled = true,
            string disabledTip = null)
        {
            DrawOriginModeAndFallback(
                rect,
                origin != null ? new List<WorldObject> { origin } : null,
                enabled,
                disabledTip);
        }

        private static void OpenOriginModeMenu(IReadOnlyList<WorldObject> origins, bool modeMixed, bool viaPod)
        {
            bool researched = RapidResponseUtility.TransportPodsResearched();
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption(
                    "TSA_WD_DispatchMode_Land".Translate(),
                    () =>
                    {
                        if (!modeMixed && !viaPod) return;
                        SetTravelViaDropPodMany(origins, false);
                        SoundDefOf.Click.PlayOneShotOnCamera();
                    },
                    WITab_Outpost_WarehouseAssets.LandIcon,
                    WorldOverlayLineMaterials.DarkCyanColor)
            };

            var podOpt = new FloatMenuOption(
                "TSA_WD_DispatchMode_DropPod".Translate(),
                () =>
                {
                    if (!modeMixed && viaPod) return;
                    SetTravelViaDropPodMany(origins, true);
                    SoundDefOf.Click.PlayOneShotOnCamera();
                },
                WITab_Outpost_WarehouseAssets.DropPodIcon,
                WorldOverlayLineMaterials.DarkCyanColor);
            if (!researched)
            {
                podOpt.Disabled = true;
                podOpt.tooltip = new TipSignal("TSA_WD_DispatchMode_NeedsResearch".Translate());
            }
            options.Add(podOpt);
            Find.WindowStack.Add(new FloatMenu(options));
        }

        /// <summary>Unlabeled fallback-to-land checkbox for these origins. Tooltip only.</summary>
        public static void DrawFallbackCheckbox(Rect checkRect, IReadOnlyList<WorldObject> origins)
        {
            if (!TryGetSharedFallback(origins, out bool value, out bool mixed))
            {
                bool dummy = true;
                Widgets.Checkbox(checkRect.position, ref dummy, FallbackCheckboxSize, disabled: true);
                TooltipHandler.TipRegion(checkRect, "TSA_WD_PawnDropPod_FallbackSelectOriginsTip".Translate());
                return;
            }

            if (mixed)
            {
                bool drawn = false;
                Widgets.Checkbox(checkRect.position, ref drawn, FallbackCheckboxSize, paintable: true);
                if (drawn)
                {
                    SetFallbackToLandMany(origins, true);
                    SoundDefOf.Click.PlayOneShotOnCamera();
                }
                TooltipHandler.TipRegion(checkRect, "TSA_WD_PawnDropPod_FallbackTipMixed".Translate());
                return;
            }

            bool before = value;
            Widgets.Checkbox(checkRect.position, ref value, FallbackCheckboxSize, paintable: true);
            if (value != before)
            {
                SetFallbackToLandMany(origins, value);
                SoundDefOf.Click.PlayOneShotOnCamera();
            }
            TooltipHandler.TipRegion(checkRect,
                value
                    ? "TSA_WD_PawnDropPod_FallbackTipOn".Translate()
                    : "TSA_WD_PawnDropPod_FallbackTipOff".Translate());
        }
    }
}
