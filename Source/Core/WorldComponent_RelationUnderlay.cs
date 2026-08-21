using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Event = UnityEngine.Event;
using EventType = UnityEngine.EventType;

namespace TSA_WorldDomination
{
    /// <summary>
    /// RELATION UNDERLAY (easy revert):
    /// 1. Delete this file.
    /// 2. In Toggle_WorldTierOnSettlements.cs remove every block marked RELATION_UNDERLAY
    ///    (static flags, float-menu toggles, ExposeData scribes).
    /// 3. Remove keyed strings TSA_WD_WorldMap_ToggleRelationUnderlays* from EN/ES/ZH.
    ///
    /// Screen-space relation rings (or discs) drawn over settlements and WD travelers (same zoom hide as tier labels).
    /// A/B: flip <see cref="UseHollowRing"/>.
    /// Optional: relations relative to the selected NPC settlement/traveler faction.
    /// In that mode, player colonies / outposts / caravans get grey/green/red discs (star if raid-targeted)
    /// and the cyan player underlay is suppressed.
    /// </summary>
    [StaticConstructorOnStartup]
    public class WorldComponent_RelationUnderlay : WorldComponent
    {
        /// <summary>A/B test: true = donut (hollow center), false = filled soft disc.</summary>
        private const bool UseHollowRing = false;

        /// <summary>Same base cutoff as <see cref="Text_WorldTierOnSettlements"/>.</summary>
        private const float HideAltitudePercent = 0.25f;
        /// <summary>
        /// GUI.color alpha (0 = invisible, 1 = opaque). Lower = more see-through.
        /// Hostile/ally use this; neutral and raid-purple need higher opacity (they wash out more).
        /// </summary>
        private const float ColorAlpha = 0.23f;
        private const float ColorAlphaMuted = 0.46f; // grey + purple: ~2× opaque vs red/green

        /// <summary>Screen pixels at closest useful zoom.</summary>
        private const float DiscSizeClosePx = 52f;
        /// <summary>Screen pixels near the hide threshold (still readable when zoomed out).</summary>
        private const float DiscSizeFarPx = 34f;

        /// <summary>Productivity overlay purple, pushed a bit brighter for screen discs.</summary>
        private static readonly Color RaidTargetPurpleRgb = new Color(0.62f, 0.28f, 0.88f);

        private static readonly Texture2D SoftDiscTex;
        private static readonly Texture2D SoftStarTex;
        private static readonly Color ColorHostile;
        private static readonly Color ColorNeutral;
        private static readonly Color ColorAlly;
        private static readonly Color ColorRaidTargetingPlayer;

        private const int RaidCacheIntervalTicks = 30;

        private readonly Dictionary<int, FactionRelationKind> kindByFactionId = new Dictionary<int, FactionRelationKind>();
        private int lastRelationCacheTick = -99999;
        private int raidCacheTick = -99999;
        private int lastPerspectiveFactionId = int.MinValue;
        private Faction activePerspective;
        /// <summary>WorldObject loadIDs of active WD quest target settlements (road-link + common-enemy).</summary>
        private readonly HashSet<int> questTargetSettlementIds = new HashSet<int>();
        /// <summary>Player colonies / outposts currently targeted by an enemy WD raid (star underlay).</summary>
        private readonly HashSet<int> raidTargetIds = new HashSet<int>();

        static WorldComponent_RelationUnderlay()
        {
            SoftDiscTex = UseHollowRing ? CreateSoftRingTexture(64) : CreateSoftDiscTexture(64);
            SoftStarTex = CreateSoftStarTexture(64);
            ColorHostile = WithAlpha(FactionRelationKind.Hostile.GetColor(), ColorAlpha);
            ColorAlly = WithAlpha(FactionRelationKind.Ally.GetColor(), ColorAlpha);
            // Stronger grey + higher alpha so neutrals do not look washed out.
            ColorNeutral = WithAlpha(new Color(0.72f, 0.72f, 0.72f), ColorAlphaMuted);
            ColorRaidTargetingPlayer = WithAlpha(RaidTargetPurpleRgb, ColorAlphaMuted);
        }

        public WorldComponent_RelationUnderlay(World world) : base(world) { }

        /// <summary>
        /// True when relation underlays are on, "based on selection" is on, and the view uses a
        /// selected non-player faction. Cyan player underlay should hide in that case.
        /// </summary>
        public static bool IsNpcSelectionPerspectiveActive()
        {
            if (!WorldComponent_WDVisualizerToggle.ShowRelationUnderlays) return false;
            if (!WorldComponent_WDVisualizerToggle.RelationUnderlaysBasedOnSelection) return false;
            Faction player = Faction.OfPlayerSilentFail;
            if (player == null) return false;
            Faction perspective = ResolvePerspectiveFaction(player);
            return perspective != null && perspective != player;
        }

        public override void WorldComponentOnGUI()
        {
            base.WorldComponentOnGUI();

            if (!WorldComponent_WDVisualizerToggle.ShowRelationUnderlays) return;
            if (Current.ProgramState != ProgramState.Playing && Current.ProgramState != ProgramState.Entry) return;
            if (!WorldRendererUtility.WorldRendered) return;
            if (Event.current.type != EventType.Repaint) return;
            if (WD_WorldMapZoomUtil.IsZoomedTooFarOut(HideAltitudePercent)) return;

            Faction player = Faction.OfPlayerSilentFail;
            if (player == null) return;

            WorldObjectsHolder worldObjects = Find.WorldObjects;
            if (worldObjects == null) return;

            List<Settlement> settlements = worldObjects.Settlements;
            bool hasSettlements = settlements != null && settlements.Count > 0;
            bool hasTravelers = WorldObject_Traveler.ActiveCount > 0;

            activePerspective = ResolvePerspectiveFaction(player);
            if (activePerspective == null) return;

            bool npcPerspective = activePerspective != player;
            if (!hasSettlements && !hasTravelers && !npcPerspective) return;

            RefreshRelationCacheIfNeeded(activePerspective);
            RefreshQuestTargetCache();
            if (npcPerspective)
                RefreshRaidTargetCache();

            float discSize = GetZoomScaledDiscSize();
            float half = discSize * 0.5f;

            if (hasSettlements)
                DrawForList(settlements, half, discSize);

            if (hasTravelers)
            {
                IReadOnlyList<WorldObject_Traveler> travelers = WorldObject_Traveler.LiveTravelers;
                for (int i = 0; i < travelers.Count; i++)
                    DrawOne(travelers[i], half, discSize);
            }

            // NPC selection perspective: also mark player outposts / caravans (settlements + WD
            // travelers already covered above) with grey/green/red relative to the selection.
            if (npcPerspective)
                DrawPlayerSitesForNpcPerspective(worldObjects, half, discSize);
        }

        /// <summary>
        /// Player by default. With "Relationships based on Selection", use the selected NPC
        /// settlement/traveler faction when exactly one valid object is selected.
        /// </summary>
        private static Faction ResolvePerspectiveFaction(Faction player)
        {
            if (!WorldComponent_WDVisualizerToggle.RelationUnderlaysBasedOnSelection)
                return player;

            WorldSelector selector = Find.WorldSelector;
            if (selector == null) return player;
            List<WorldObject> selected = selector.SelectedObjects;
            if (selected == null || selected.Count != 1) return player;

            WorldObject wo = selected[0];
            if (wo == null || wo.Destroyed) return player;
            if (wo is not Settlement && wo is not WorldObject_Traveler) return player;
            if (!PlanetSurfaceWorldActions.IsPlanetSurfaceWorldObjectForWorldActions(wo)) return player;

            Faction f = wo.Faction;
            if (f == null || f.IsPlayer || f.def == null || f.def.hidden || f.defeated)
                return player;
            return f;
        }

        private void DrawPlayerSitesForNpcPerspective(WorldObjectsHolder worldObjects, float half, float discSize)
        {
            IReadOnlyList<WorldObject_WD_Outpost> outposts = WdPlayerOutpostCache.PlayerOutposts;
            for (int i = 0; i < outposts.Count; i++)
                DrawOne(outposts[i], half, discSize);

            List<Caravan> caravans = worldObjects.Caravans;
            if (caravans == null) return;
            for (int i = 0; i < caravans.Count; i++)
            {
                Caravan c = caravans[i];
                if (c == null || !c.IsPlayerControlled) continue;
                DrawOne(c, half, discSize);
            }
        }

        /// <summary>
        /// Player colonies / outposts currently targeted by an enemy WD raid. Throttled and reads the
        /// maintained live-traveler registry instead of scanning AllWorldObjects every frame.
        /// </summary>
        private void RefreshRaidTargetCache()
        {
            int tick = Current.ProgramState == ProgramState.Playing
                ? (Find.TickManager?.TicksGame ?? 0)
                : Time.frameCount;
            int delta = tick - raidCacheTick;
            if (delta >= 0 && delta < RaidCacheIntervalTicks) return;
            raidCacheTick = tick;

            raidTargetIds.Clear();
            if (WorldObject_Traveler.ActiveCount <= 0) return;
            IReadOnlyList<WorldObject_Traveler> travelers = WorldObject_Traveler.LiveTravelers;
            for (int i = 0; i < travelers.Count; i++)
            {
                WorldObject_Traveler traveler = travelers[i];
                if (traveler == null || traveler.Destroyed || !traveler.ShowTargetingPlayerWarning) continue;
                WorldObject target = traveler.targetObject;
                if (!TravelerEndpointUtility.IsLiveEndpoint(target)) continue;
                if (target is WorldObject_WD_Outpost
                    || (target is Settlement && target.Faction != null && target.Faction.IsPlayer))
                    raidTargetIds.Add(target.ID);
            }
        }

        private static float GetZoomScaledDiscSize()
        {
            float alt = Find.WorldCameraDriver?.AltitudePercent ?? 0f;
            float maxAlt = WD_WorldMapZoomUtil.GetMaxAltitudePercent(HideAltitudePercent);
            float t = maxAlt > 0.001f ? Mathf.Clamp01(alt / maxAlt) : 1f;
            // Zoomed in (t~0) -> larger; approaching hide (t~1) -> smaller but still visible.
            return Mathf.Lerp(DiscSizeClosePx, DiscSizeFarPx, t);
        }

        private void DrawForList<T>(List<T> list, float half, float discSize) where T : WorldObject
        {
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
                DrawOne(list[i], half, discSize);
        }

        private void DrawOne(WorldObject wo, float half, float discSize)
        {
            if (wo == null || wo.Destroyed || !wo.Spawned) return;

            Faction fac = wo.Faction;
            if (fac == null || fac.def == null || fac.def.hidden || fac.defeated)
                return;
            // No disc on the perspective faction itself (player when viewing as player; selected NPC when relative).
            if (fac == activePerspective)
                return;

            if (!PlanetSurfaceWorldActions.IsPlanetSurfaceWorldObjectForWorldActions(wo)) return;
            if (!WorldObjectSelectionUtility.VisibleToCameraNow(wo)) return;

            Color color = ColorForWorldObject(wo, fac);
            Vector2 screenPos = WorldObjectSelectionUtility.ScreenPos(wo);
            Rect rect = new Rect(screenPos.x - half, screenPos.y - half, discSize, discSize);

            Texture2D tex = SoftDiscTex;
            if (wo is Settlement && questTargetSettlementIds.Contains(wo.ID))
                tex = SoftStarTex;
            else if ((wo is Settlement || wo is WorldObject_WD_Outpost) && raidTargetIds.Contains(wo.ID))
                tex = SoftStarTex;

            Color prev = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, tex, ScaleMode.StretchToFill, alphaBlend: true);
            GUI.color = prev;
        }

        private void RefreshQuestTargetCache()
        {
            questTargetSettlementIds.Clear();
            TryAddQuestTarget(WdColonyRoadLinkQuestHelper.FindActiveTrackedPart()?.settlement);
            TryAddQuestTarget(WdCommonEnemySettlementQuestHelper.FindActiveTrackedPart()?.settlement);
        }

        private void TryAddQuestTarget(Settlement settlement)
        {
            if (settlement == null || settlement.Destroyed || !settlement.Spawned) return;
            questTargetSettlementIds.Add(settlement.ID);
        }

        private void RefreshRelationCacheIfNeeded(Faction perspective)
        {
            int now = Find.TickManager?.TicksGame ?? 0;
            int perspectiveId = perspective?.loadID ?? -1;
            if (perspectiveId == lastPerspectiveFactionId
                && now - lastRelationCacheTick < 30
                && kindByFactionId.Count > 0)
                return;

            lastPerspectiveFactionId = perspectiveId;
            lastRelationCacheTick = now;
            kindByFactionId.Clear();
            if (perspective == null) return;

            List<Faction> factions = Find.FactionManager?.AllFactionsListForReading;
            if (factions == null) return;
            for (int i = 0; i < factions.Count; i++)
            {
                Faction f = factions[i];
                if (f == null || f.def == null || f.def.hidden || f.defeated) continue;
                if (f == perspective) continue;
                kindByFactionId[f.loadID] = WorldActions_Utils.SafeRelationKindWith(f, perspective);
            }
        }

        private Color ColorForWorldObject(WorldObject wo, Faction fac)
        {
            // Enemy raid (or drop-pod raid) headed to a player colony or outpost (always player-centric).
            if (wo is WorldObject_Traveler traveler && traveler.ShowTargetingPlayerWarning)
                return ColorRaidTargetingPlayer;

            if (!kindByFactionId.TryGetValue(fac.loadID, out FactionRelationKind kind))
            {
                Faction perspective = activePerspective ?? Faction.OfPlayerSilentFail;
                kind = WorldActions_Utils.SafeRelationKindWith(fac, perspective);
                kindByFactionId[fac.loadID] = kind;
            }

            return kind switch
            {
                FactionRelationKind.Hostile => ColorHostile,
                FactionRelationKind.Ally => ColorAlly,
                _ => ColorNeutral
            };
        }

        private static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, a);

        private static Texture2D AllocUnderlayTexture(int size, string name)
        {
            return new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        private static float SmoothStep01(float a) => a * a * (3f - 2f * a);

        /// <summary>Filled soft disc (A/B: set <see cref="UseHollowRing"/> false).</summary>
        private static Texture2D CreateSoftDiscTexture(int size)
        {
            var tex = AllocUnderlayTexture(size, "WD_RelationUnderlayDisc");

            float center = (size - 1) * 0.5f;
            float radius = center;
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center;
                    float dy = y - center;
                    float t = Mathf.Sqrt(dx * dx + dy * dy) / radius;
                    float a = 0f;
                    if (t < 0.72f)
                        a = 1f;
                    else if (t < 1f)
                        a = Mathf.Clamp01(1f - (t - 0.72f) / 0.28f);
                    a = SmoothStep01(a);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            return tex;
        }

        /// <summary>Hollow ring / donut with soft inner and outer edges (A/B: set <see cref="UseHollowRing"/> true).</summary>
        private static Texture2D CreateSoftRingTexture(int size)
        {
            var tex = AllocUnderlayTexture(size, "WD_RelationUnderlayRing");

            float center = (size - 1) * 0.5f;
            float radius = center;
            // Normalized radii: hole | fade-in | solid band | fade-out | empty
            const float holeEnd = 0.42f;
            const float solidStart = 0.55f;
            const float solidEnd = 0.78f;
            const float outerEnd = 1f;

            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center;
                    float dy = y - center;
                    float t = Mathf.Sqrt(dx * dx + dy * dy) / radius;
                    float a = 0f;
                    if (t < holeEnd)
                        a = 0f;
                    else if (t < solidStart)
                        a = Mathf.Clamp01((t - holeEnd) / (solidStart - holeEnd));
                    else if (t < solidEnd)
                        a = 1f;
                    else if (t < outerEnd)
                        a = Mathf.Clamp01(1f - (t - solidEnd) / (outerEnd - solidEnd));
                    a = SmoothStep01(a);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            return tex;
        }

        /// <summary>Soft 6-line star for WD quest target settlements (same tint path as discs).</summary>
        private static Texture2D CreateSoftStarTexture(int size)
        {
            var tex = AllocUnderlayTexture(size, "WD_RelationUnderlayStar");

            float center = (size - 1) * 0.5f;
            // 30% thinner than the old X (was size * 0.09).
            float halfThickness = size * 0.063f;
            float armExtent = center * 0.92f;
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center;
                    float dy = y - center;
                    float dist = float.MaxValue;
                    float along = 0f;
                    // Six lines through the center at 30° steps (12 tips).
                    for (int i = 0; i < 6; i++)
                    {
                        float angle = i * (Mathf.PI / 6f);
                        float c = Mathf.Cos(angle);
                        float s = Mathf.Sin(angle);
                        float d = Mathf.Abs(-s * dx + c * dy);
                        if (d < dist)
                        {
                            dist = d;
                            along = Mathf.Abs(c * dx + s * dy);
                        }
                    }
                    float a = 0f;
                    if (along <= armExtent && dist < halfThickness)
                    {
                        float edge = halfThickness * 0.45f;
                        if (dist < halfThickness - edge)
                            a = 1f;
                        else
                            a = Mathf.Clamp01(1f - (dist - (halfThickness - edge)) / edge);
                        float tipFade = armExtent * 0.18f;
                        if (along > armExtent - tipFade)
                            a *= Mathf.Clamp01(1f - (along - (armExtent - tipFade)) / tipFade);
                        a = SmoothStep01(a);
                    }
                    pixels[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            return tex;
        }
    }
}
