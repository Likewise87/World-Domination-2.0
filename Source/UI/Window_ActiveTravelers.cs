using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using RimWorld;
using RimWorld.Planet;
using Verse.Sound;
namespace TSA_WorldDomination
{
    /// <summary>Lists all active WorldObject_Traveler with actor/target icons, full labels, and jump-to (closes window).</summary>
    public class Window_ActiveTravelers : Window
    {
        private Vector2 scrollPos;
        public override Vector2 InitialSize => new Vector2(UI.screenWidth, UI.screenHeight);

        private struct TravelerRowSnapshot
        {
            public WorldObject_Traveler T;
            public Texture2D MissionIcon;
            public float ArrivalStrength;
            public int ExpansionDestTileId;
            public string ExpansionDestLabel;
            public string TimeSinceLabel;
            public string TotalTravelLabel;
            public string DepartureStrengthLabel;
            public string ArrivalStrengthLabel;
        }

        private readonly List<TravelerRowSnapshot> travelerRows = new List<TravelerRowSnapshot>();
        private int lastUpdateTick = -9999;
        /// <summary>120 ticks ≈ 2s real at 60 t/s (unchanged from before).</summary>
        private const int UpdateIntervalTicks = 120;
        private const float IconSize = 32f;
        private const float RowHeight = 52f;

        private static string cachedDaysLabel;
        private static string cachedJumpLabel;
        private static string cachedTitleLabel;
        private static string cachedNoneLabel;
        private static string cachedHeaderType;
        private static string cachedHeaderActor;
        private static string cachedHeaderTarget;
        private static string cachedHeaderTimeSince;
        private static string cachedHeaderTotalTravel;
        private static string cachedHeaderDepartStr;
        private static string cachedHeaderCurrentStr;
        private static string cachedHeaderArrivalStr;
        private static string cachedHeaderJump;
        private static int cachedTranslateFrame = -1;

        private static void EnsureTranslationsCached()
        {
            int frame = UnityEngine.Time.frameCount;
            if (frame == cachedTranslateFrame) return;
            cachedTranslateFrame = frame;
            cachedDaysLabel = "TSA_WD_Days".Translate();
            cachedJumpLabel = "TSA_WD_ActiveTravelers_Jump".Translate();
            cachedTitleLabel = "TSA_WD_ActiveTravelers_Title".Translate();
            cachedNoneLabel = "TSA_WD_None".Translate();
            cachedHeaderType = "TSA_WD_ActiveTravelers_H_Type".Translate();
            cachedHeaderActor = "TSA_WD_ActiveTravelers_H_Actor".Translate();
            cachedHeaderTarget = "TSA_WD_ActiveTravelers_H_Target".Translate();
            cachedHeaderTimeSince = "TSA_WD_ActiveTravelers_H_TimeSinceDeparture".Translate();
            cachedHeaderTotalTravel = "TSA_WD_Traveller_TotalExpectedTravelTime".Translate();
            cachedHeaderCurrentStr = "TSA_WD_ActiveTravelers_H_CurrentStrength".Translate();
            cachedHeaderDepartStr = "TSA_WD_ActiveTravelers_H_DepartureStrength".Translate();
            cachedHeaderArrivalStr = "TSA_WD_ActiveTravelers_H_ArrivalStrength".Translate();
            cachedHeaderJump = "TSA_WD_ActiveTravelers_H_Jump".Translate();
        }

        public Window_ActiveTravelers()
        {
            doCloseX = true;
            draggable = false;
            absorbInputAroundWindow = true;
            forcePause = false;
            closeOnCancel = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            WdNavWindows.ProcessHotkeys();
            if (!IsOpen) return;
            if (WdWindowEsc.TryCloseOnCancel(this))
                return;

            EnsureTranslationsCached();
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0, 0, inRect.width, 35f), cachedTitleLabel);

            float colType = 72f;
            float colActor = 280f;
            float colTarget = 280f;
            float colSince = 140f;
            float colTotalTravel = 140f;
            float colCurrent = 120f;
            float colDepart = 120f;
            float colArrival = 120f;
            float colJump = 100f;

            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            Rect hRect = new Rect(0, 42f, inRect.width, 28f);
            float curX = 0f;
            DrawHeader(ref curX, colType, cachedHeaderType, hRect);
            DrawHeader(ref curX, colActor, cachedHeaderActor, hRect);
            DrawHeader(ref curX, colTarget, cachedHeaderTarget, hRect);
            DrawHeader(ref curX, colSince, cachedHeaderTimeSince, hRect);
            DrawHeader(ref curX, colTotalTravel, cachedHeaderTotalTravel, hRect);
            DrawHeader(ref curX, colCurrent, cachedHeaderCurrentStr, hRect);
            DrawHeader(ref curX, colDepart, cachedHeaderDepartStr, hRect);
            DrawHeader(ref curX, colArrival, cachedHeaderArrivalStr, hRect);
            DrawHeader(ref curX, colJump, cachedHeaderJump, hRect);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            Widgets.DrawLineHorizontal(0, hRect.yMax, inRect.width);

            int ticksNow = Find.TickManager.TicksGame;
            if (ticksNow >= lastUpdateTick + UpdateIntervalTicks)
            {
                var allT = new List<WorldObject_Traveler>();
                foreach (WorldObject wo in Find.WorldObjects.AllWorldObjects)
                {
                    if (wo is WorldObject_Traveler tr) allT.Add(tr);
                }
                var sorted = WorldObject_Traveler.SortTravelersForUi(allT, int.MaxValue);
                WorldDominationSettings seth = WorldDominationMod.settings;
                travelerRows.Clear();
                foreach (WorldObject_Traveler t in sorted)
                {
                    float daysSince = (ticksNow - t.spawnTick) / 60000f;
                    float depStr = t.initialStrength > 0 ? t.initialStrength : t.travelerStrength;
                    float arrStr = ComputeArrivalStrengthForSnapshot(t, seth);
                    TravelerRowSnapshot row = new TravelerRowSnapshot
                    {
                        T = t,
                        MissionIcon = WorldDomination_UIUtils.CachedTravelerMissionIcon(t),
                        ArrivalStrength = arrStr,
                        ExpansionDestTileId = -1,
                        ExpansionDestLabel = null,
                        TimeSinceLabel = $"{daysSince:F1} {cachedDaysLabel}",
                        TotalTravelLabel = t.TryGetTotalExpectedTravelDays(out float totalDays)
                            ? $"{totalDays:F1} {cachedDaysLabel}" : "\u2014",
                        DepartureStrengthLabel = depStr.ToString("F0"),
                        ArrivalStrengthLabel = arrStr > 0 ? arrStr.ToString("F0") : "\u2014"
                    };
                    if (t.mission == TravelerMission.Expansion || UsesPathDestinationTile(t.mission))
                    {
                        if (t.pather != null && t.pather.destTile.tileId >= 0)
                        {
                            row.ExpansionDestTileId = t.pather.destTile.tileId;
                            row.ExpansionDestLabel = WorldTileInfo.GetBiomeLabel(row.ExpansionDestTileId).CapitalizeFirst() + $" ({row.ExpansionDestTileId})";
                        }
                    }
                    travelerRows.Add(row);
                }
                lastUpdateTick = ticksNow;
            }

            Rect scrollOutRect = new Rect(0, 75f, inRect.width, inRect.height - 75f);
            Rect viewRect = new Rect(0, 0, inRect.width - 25f, travelerRows.Count * RowHeight);
            Widgets.BeginScrollView(scrollOutRect, ref scrollPos, viewRect);

            WorldObject_Traveler pendingJump = null;
            for (int i = 0; i < travelerRows.Count; i++)
            {
                TravelerRowSnapshot snap = travelerRows[i];
                WorldObject_Traveler t = snap.T;
                Rect row = new Rect(0, i * RowHeight, viewRect.width, RowHeight);
                if (i % 2 == 0) Widgets.DrawHighlight(row);
                if (Mouse.IsOver(row)) Widgets.DrawLightHighlight(row);

                float rX = 0f;
                Text.Anchor = TextAnchor.MiddleLeft;
                Text.Font = GameFont.Small;

                // Type (with icon) — clickable: jump + close
                Rect typeRect = new Rect(rX, row.y, colType, RowHeight);
                DrawMissionTypeAndLabel(typeRect, t, snap.MissionIcon);
                if (Widgets.ButtonInvisible(typeRect))
                {
                    pendingJump = t;
                    break;
                }
                rX += colType;

                Rect actorRect = new Rect(rX, row.y, colActor, RowHeight);
                Rect targetRect = new Rect(rX + colActor, row.y, colTarget, RowHeight);
                if (t.mission == TravelerMission.Expansion)
                {
                    DrawExpansionActorLikeActionLog(actorRect, t);
                    DrawExpansionDestFromSnapshot(targetRect, snap.ExpansionDestTileId, snap.ExpansionDestLabel);
                }
                else if (UsesPathDestinationTile(t.mission))
                {
                    DrawActorCell(actorRect, t.originObject, t.Faction);
                    DrawExpansionDestFromSnapshot(targetRect, snap.ExpansionDestTileId, snap.ExpansionDestLabel);
                }
                else
                {
                    DrawActorCell(actorRect, t.originObject, t.Faction);
                    DrawTargetCell(targetRect, t.targetObject);
                }
                rX += colActor + colTarget;

                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(new Rect(rX, row.y, colSince, RowHeight), snap.TimeSinceLabel);
                rX += colSince;

                Widgets.Label(new Rect(rX, row.y, colTotalTravel, RowHeight), snap.TotalTravelLabel);
                rX += colTotalTravel;

                // Live field read (not snapshot): attrition updates without rebuilding the list.
                Widgets.Label(new Rect(rX, row.y, colCurrent, RowHeight), t.travelerStrength.ToString("F0"));
                rX += colCurrent;

                Widgets.Label(new Rect(rX, row.y, colDepart, RowHeight), snap.DepartureStrengthLabel);
                rX += colDepart;

                Widgets.Label(new Rect(rX, row.y, colArrival, RowHeight), snap.ArrivalStrengthLabel);
                rX += colArrival;
                Text.Anchor = TextAnchor.MiddleLeft;

                // Jump button — jump + close
                Rect jumpRect = new Rect(rX + 4f, row.y + (RowHeight - 28f) / 2f, colJump - 8f, 28f);
                if (Widgets.ButtonText(jumpRect, cachedJumpLabel))
                {
                    pendingJump = t;
                    break;
                }
            }

            Widgets.EndScrollView();
            Text.Anchor = TextAnchor.UpperLeft;

            if (pendingJump != null)
                JumpToTravelerAndClose(pendingJump);
        }

        private void JumpToTravelerAndClose(WorldObject_Traveler t)
        {
            CameraJumper.TryJump(t);
            Find.WorldSelector.ClearSelection();
            Find.WorldSelector.Select(t);
            SoundDefOf.Click.PlayOneShotOnCamera();
            if (Find.MainTabsRoot.OpenTab != null) Find.MainTabsRoot.EscapeCurrentTab();
            Close();
        }

        private void DrawMissionTypeAndLabel(Rect rect, WorldObject_Traveler t, Texture2D missionIcon)
        {
            Rect iconRect = new Rect(rect.x + (rect.width - IconSize) * 0.5f, rect.y + (rect.height - IconSize) / 2f, IconSize, IconSize);
            if (missionIcon != null)
            {
                GUI.color = t.Faction?.Color ?? Color.white;
                GUI.DrawTexture(iconRect, missionIcon, ScaleMode.ScaleToFit);
                GUI.color = Color.white;
            }
            TooltipHandler.TipRegion(rect, GetMissionLabel(t.mission));
        }

        private static string GetMissionLabel(TravelerMission mission) =>
            WorldObject_Traveler.GetMissionTypeLabel(mission);

        private static bool UsesPathDestinationTile(TravelerMission mission) =>
            mission == TravelerMission.RoadBuilding
            || mission == TravelerMission.RoadBlock
            || mission == TravelerMission.SpikeTrap
            || mission == TravelerMission.Decontamination
            || mission == TravelerMission.NpcFortify
            || mission == TravelerMission.NpcAtTurret
            || mission == TravelerMission.AtTurret;

        private static string FormatWorldObjectLabelLikeActionLog(WorldObject obj) =>
            WorldDomination_UIUtils.FormatWorldObjectLabelLikeActionLog(obj);

        private void DrawExpansionActorLikeActionLog(Rect rect, WorldObject_Traveler t)
        {
            WorldObject src = t.originObject;
            if (!TravelerEndpointUtility.IsLiveEndpoint(src))
            {
                TextAnchor prev = Text.Anchor;
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(rect, "---");
                Text.Anchor = prev;
                return;
            }
            Rect iconRect = new Rect(rect.x + 4f, rect.y + (rect.height - IconSize) / 2f, IconSize, IconSize);
            WorldDomination_UIUtils.DrawFactionIconWithColor(iconRect, new GlobalTargetInfo(src));
            string label = FormatWorldObjectLabelLikeActionLog(src);
            DrawJumpLabelActiveTravelers(new Rect(iconRect.xMax + 6f, rect.y, rect.width - (IconSize + 10f), rect.height), label, new GlobalTargetInfo(src));
        }

        private void DrawExpansionDestFromSnapshot(Rect rect, int destTileId, string label)
        {
            if (destTileId < 0 || string.IsNullOrEmpty(label))
            {
                TextAnchor prev = Text.Anchor;
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(rect, "---");
                Text.Anchor = prev;
                return;
            }
            GlobalTargetInfo gti = new GlobalTargetInfo(destTileId);
            Rect iconRect = new Rect(rect.x + 4f, rect.y + (rect.height - IconSize) / 2f, IconSize, IconSize);
            bool drewFaction = WorldDomination_UIUtils.TryDrawFactionIconForTarget(iconRect, gti, out _);
            if (!drewFaction)
            {
                Texture2D ph = WorldDomination_UIUtils.UnknownWorldTargetPlaceholderIcon;
                if (ph != null)
                    GUI.DrawTexture(iconRect, ph, ScaleMode.ScaleToFit);
            }
            DrawJumpLabelActiveTravelers(new Rect(iconRect.xMax + 6f, rect.y, rect.width - (IconSize + 10f), rect.height), label, gti);
        }

        private void DrawJumpLabelActiveTravelers(Rect rect, string label, GlobalTargetInfo target)
        {
            TextAnchor anchorBefore = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleLeft;
            if (target.IsValid)
            {
                Widgets.DrawHighlightIfMouseover(rect);
                if (Widgets.ButtonInvisible(rect))
                {
                    CameraJumper.TryJumpAndSelect(target);
                    SoundDefOf.Click.PlayOneShotOnCamera();
                    if (Find.MainTabsRoot.OpenTab != null) Find.MainTabsRoot.EscapeCurrentTab();
                    Close();
                }
            }
            Widgets.Label(rect, label.Truncate(rect.width));
            Text.Anchor = anchorBefore;
        }

        private void DrawActorCell(Rect rect, WorldObject origin, Faction travelerFaction)
        {
            if (TravelerEndpointUtility.IsLiveEndpoint(origin))
            {
                Rect iconRect = new Rect(rect.x + 4f, rect.y + (rect.height - IconSize) / 2f, IconSize, IconSize);
                DrawWorldObjectIcon(iconRect, origin);
                string label = FormatWorldObjectLabelLikeActionLog(origin);
                Rect labelRect = new Rect(iconRect.xMax + 6f, rect.y, rect.width - (IconSize + 10f), rect.height);
                DrawJumpLabelActiveTravelers(labelRect, label, new GlobalTargetInfo(origin));
            }
            else
            {
                TextAnchor prev = Text.Anchor;
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(rect, cachedNoneLabel);
                Text.Anchor = prev;
            }
        }

        private void DrawTargetCell(Rect rect, WorldObject target)
        {
            if (TravelerEndpointUtility.IsLiveEndpoint(target))
            {
                Rect iconRect = new Rect(rect.x + 4f, rect.y + (rect.height - IconSize) / 2f, IconSize, IconSize);
                DrawWorldObjectIcon(iconRect, target);
                string label = FormatWorldObjectLabelLikeActionLog(target);
                Rect labelRect = new Rect(iconRect.xMax + 6f, rect.y, rect.width - (IconSize + 10f), rect.height);
                DrawJumpLabelActiveTravelers(labelRect, label, new GlobalTargetInfo(target));
            }
            else
            {
                TextAnchor prev = Text.Anchor;
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(rect, cachedNoneLabel);
                Text.Anchor = prev;
            }
        }

        private static void DrawWorldObjectIcon(Rect rect, WorldObject wo)
        {
            if (wo == null) return;
            Faction f = wo.Faction;
            Texture2D tex = null;
            if (wo is WorldObject_WD_Outpost op && op.def != null)
                tex = op.def.ExpandingIconTexture;
            if (tex == null && f?.def?.FactionIcon != null)
                tex = f.def.FactionIcon;
            if (tex != null)
            {
                GUI.color = f != null ? f.Color : Color.white;
                GUI.DrawTexture(rect, tex, ScaleMode.ScaleToFit);
                GUI.color = Color.white;
            }
        }

        private void DrawHeader(ref float curX, float width, string label, Rect hRect)
        {
            Rect r = new Rect(curX, hRect.y, width, hRect.height);
            if (Mouse.IsOver(r)) Widgets.DrawHighlight(r);
            TextAnchor prev = Text.Anchor;
            Text.Anchor = TextAnchor.LowerCenter;
            Widgets.Label(r, label);
            Text.Anchor = prev;
            curX += width;
        }

        /// <summary>
        /// Arrival Strength is the "pre-raid analysis" projection locked in once by
        /// <see cref="WD_PathFollower.StartPath"/>: initialStrength × efficiency(launch-time path).
        /// It must not drift with current tile or current (decayed) strength — that's why there
        /// is no live recomputation fallback here. Zero means "not launched yet / unknown".
        /// </summary>
        private static float ComputeArrivalStrengthForSnapshot(WorldObject_Traveler t, WorldDominationSettings seth)
        {
            return t.projectedArrivalStrength > 0f ? t.projectedArrivalStrength : 0f;
        }
    }
}
