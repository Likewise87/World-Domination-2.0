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
    /// PLAYER UNDERLAY (easy revert):
    /// 1. Delete this file.
    /// 2. In Toggle_WorldTierOnSettlements.cs remove every block marked PLAYER_UNDERLAY
    ///    (static flag, float-menu toggle, ExposeData scribe).
    /// 3. Remove keyed strings TSA_WD_WorldMap_TogglePlayerUnderlays* from EN/ES/ZH.
    ///
    /// Screen-space cyan discs under player settlements, outposts, caravans, and travelers
    /// (same zoom hide as tier labels / relation underlays).
    /// Player colonies / outposts currently targeted by an enemy WD raid use a soft 6-line star
    /// (same cyan tint) instead of the disc.
    /// When relation underlays use "based on selection" with an NPC selected, this underlay hides
    /// so player sites show grey/green/red relation discs (star still used for raid targets).
    /// </summary>
    [StaticConstructorOnStartup]
    public class WorldComponent_PlayerUnderlay : WorldComponent
    {
        /// <summary>Same base cutoff as <see cref="Text_WorldTierOnSettlements"/>.</summary>
        private const float HideAltitudePercent = 0.25f;
        /// <summary>GUI.color alpha (0 = invisible, 1 = opaque). Slightly lighter than relation underlays.</summary>
        private const float ColorAlpha = 0.16f;

        private const float DiscSizeClosePx = 52f;
        private const float DiscSizeFarPx = 34f;
        private const int RaidCacheIntervalTicks = 30;

        private static readonly Texture2D SoftDiscTex;
        private static readonly Texture2D SoftStarTex;
        private static readonly Color ColorPlayer;

        /// <summary>WorldObject loadIDs of player colonies / outposts currently targeted by an enemy WD raid.</summary>
        private readonly HashSet<int> raidTargetIds = new HashSet<int>();
        private int raidCacheTick = -99999;

        static WorldComponent_PlayerUnderlay()
        {
            SoftDiscTex = CreateSoftDiscTexture(64);
            SoftStarTex = CreateSoftStarTexture(64);
            // Same cyan used for outpost / player tinting elsewhere (Unity Color.cyan).
            ColorPlayer = WithAlpha(Color.cyan, ColorAlpha);
        }

        public WorldComponent_PlayerUnderlay(World world) : base(world) { }

        public override void WorldComponentOnGUI()
        {
            base.WorldComponentOnGUI();

            if (!WorldComponent_WDVisualizerToggle.ShowPlayerUnderlays) return;
            // NPC selection perspective: relation underlay paints player sites grey/green/red instead.
            if (WorldComponent_RelationUnderlay.IsNpcSelectionPerspectiveActive()) return;
            if (Current.ProgramState != ProgramState.Playing && Current.ProgramState != ProgramState.Entry) return;
            if (!WorldRendererUtility.WorldRendered) return;
            if (Event.current.type != EventType.Repaint) return;
            if (WD_WorldMapZoomUtil.IsZoomedTooFarOut(HideAltitudePercent)) return;

            if (Faction.OfPlayerSilentFail == null) return;

            WorldObjectsHolder worldObjects = Find.WorldObjects;
            if (worldObjects == null) return;

            float discSize = GetZoomScaledDiscSize();
            float half = discSize * 0.5f;

            RefreshRaidTargetCache();
            DrawSettlements(worldObjects.Settlements, half, discSize);
            DrawCaravans(worldObjects.Caravans, half, discSize);

            IReadOnlyList<WorldObject_WD_Outpost> outposts = WdPlayerOutpostCache.PlayerOutposts;
            for (int i = 0; i < outposts.Count; i++)
                DrawOne(outposts[i], half, discSize);

            if (WorldObject_Traveler.ActiveCount > 0)
            {
                IReadOnlyList<WorldObject_Traveler> travelers = WorldObject_Traveler.LiveTravelers;
                for (int i = 0; i < travelers.Count; i++)
                    DrawOne(travelers[i], half, discSize);
            }
        }

        /// <summary>
        /// Player colonies / outposts currently targeted by an enemy WD raid. Throttled: raid membership
        /// changes on the order of seconds, so refreshing every frame is wasteful. Reads the maintained
        /// live-traveler registry instead of scanning AllWorldObjects.
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
                // Colony (player map settlement) or player WD outpost only — not caravans / AT guns.
                if (target is WorldObject_WD_Outpost
                    || (target is Settlement && target.Faction != null && target.Faction.IsPlayer))
                    raidTargetIds.Add(target.ID);
            }
        }

        private void DrawSettlements(List<Settlement> list, float half, float discSize)
        {
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
            {
                Settlement s = list[i];
                if (s?.Faction == null || !s.Faction.IsPlayer) continue;
                DrawOne(s, half, discSize);
            }
        }

        private void DrawCaravans(List<Caravan> list, float half, float discSize)
        {
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
            {
                Caravan c = list[i];
                if (c == null || !c.IsPlayerControlled) continue;
                DrawOne(c, half, discSize);
            }
        }

        private static float GetZoomScaledDiscSize()
        {
            float alt = Find.WorldCameraDriver?.AltitudePercent ?? 0f;
            float maxAlt = WD_WorldMapZoomUtil.GetMaxAltitudePercent(HideAltitudePercent);
            float t = maxAlt > 0.001f ? Mathf.Clamp01(alt / maxAlt) : 1f;
            return Mathf.Lerp(DiscSizeClosePx, DiscSizeFarPx, t);
        }

        private void DrawOne(WorldObject wo, float half, float discSize)
        {
            if (wo == null || wo.Destroyed || !wo.Spawned) return;
            if (wo.Faction == null || !wo.Faction.IsPlayer) return;
            if (!PlanetSurfaceWorldActions.IsPlanetSurfaceWorldObjectForWorldActions(wo)) return;
            if (!WorldObjectSelectionUtility.VisibleToCameraNow(wo)) return;

            Vector2 screenPos = WorldObjectSelectionUtility.ScreenPos(wo);
            Rect rect = new Rect(screenPos.x - half, screenPos.y - half, discSize, discSize);

            Texture2D tex = SoftDiscTex;
            if ((wo is Settlement || wo is WorldObject_WD_Outpost) && raidTargetIds.Contains(wo.ID))
                tex = SoftStarTex;

            Color prev = GUI.color;
            GUI.color = ColorPlayer;
            GUI.DrawTexture(rect, tex, ScaleMode.StretchToFill, alphaBlend: true);
            GUI.color = prev;
        }

        private static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, a);

        private static Texture2D CreateSoftDiscTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false)
            {
                name = "WD_PlayerUnderlayDisc",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };

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
                    a = a * a * (3f - 2f * a);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            return tex;
        }

        /// <summary>Soft 6-line star for player sites under incoming WD raid (same cyan tint path as discs).</summary>
        private static Texture2D CreateSoftStarTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false)
            {
                name = "WD_PlayerUnderlayStar",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };

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
                        a = a * a * (3f - 2f * a);
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
