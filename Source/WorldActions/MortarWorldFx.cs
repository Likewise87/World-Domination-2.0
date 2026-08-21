using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Ephemeral world-map overlays: mortar/AA explosions. Anchors snapshot world positions; screen pos uses UIScale + IMGUI Y like <c>GenWorldUI.WorldToUIPosition</c>.</summary>
    [StaticConstructorOnStartup]
    public static class MortarWorldFx
    {
        public const string ExplosionTexturePath = "WorldObjects/Explosion";
        public const string DropPodExplosionTexturePath = "WorldObjects/DropPodExplosion";
        public const string ArtilleryShellDestroyedTexturePath = "WorldObjects/Artillery_Shell_Destroyed";
        public const string CaravanRaidersDestroyedTexturePath = "WorldObjects/Caravan_Raiders_Destroyed";
        public const string CaravanRapidResponseDestroyedTexturePath = "WorldObjects/Caravan_RapidResponse_Destroyed";
        public const string FlakHitTexturePath = "WorldObjects/FlakHit";
        public const string FlakSmokeTexturePathPrefix = "WorldObjects/FlakSmoke_";
        private const int FlakSmokeVariantCount = 5;

        /// <summary>Above this camera altitude on a normal-sized planet the impact burst is hidden; small worlds raise the cutoff via <see cref="WD_WorldMapZoomUtil"/>.</summary>
        private const float MaxVisibleAltitudePercent = 0.25f;
        private const float DefaultDurationSeconds = 2f;
        private const float DropPodExplosionDurationSeconds = 1f;
        private const float DropPodExplosionSizeScale = 0.8f;
        private const float ArtilleryShellDestroyedDurationSeconds = 1f;
        private const float ArtilleryShellDestroyedSizeScale = 0.8f;
        private const float CaravanDestroyedDurationSeconds = 1f;
        private const float CaravanDestroyedSizeScale = 0.8f;
        private const float FlakSmokeDurationSeconds = 2f;
        private const float FlakSmokeSizeMin = 0.6f;
        private const float FlakSmokeSizeMax = 1f;
        /// <summary>World-space radius used to separate concurrent flak smoke puffs (fraction of average tile size).</summary>
        private const float FlakSmokeSpreadTileFraction = 1.35f;
        private const float FlakSmokeMinSeparationTileFraction = 0.55f;
        private const int FlakSmokeSpreadAttempts = 8;

        private struct Entry
        {
            public int startTick;
            public int expireTick;
            public Vector3 worldPos;
            public Texture2D tex;
            public Color color;
            public float sizeScale;
            public float angle;
            public bool fadeOut;
        }

        private static readonly List<Entry> entries = new List<Entry>(16);
        private static Texture2D explosionTex;
        private static Texture2D dropPodExplosionTex;
        private static Texture2D artilleryShellDestroyedTex;
        private static Texture2D caravanRaidersDestroyedTex;
        private static Texture2D caravanRapidResponseDestroyedTex;
        private static Texture2D flakHitTex;
        private static readonly Texture2D[] flakSmokeVariants = new Texture2D[FlakSmokeVariantCount];

        static MortarWorldFx()
        {
            explosionTex = ContentFinder<Texture2D>.Get(ExplosionTexturePath, false);
            dropPodExplosionTex = ContentFinder<Texture2D>.Get(DropPodExplosionTexturePath, false);
            artilleryShellDestroyedTex = ContentFinder<Texture2D>.Get(ArtilleryShellDestroyedTexturePath, false);
            caravanRaidersDestroyedTex = ContentFinder<Texture2D>.Get(CaravanRaidersDestroyedTexturePath, false);
            caravanRapidResponseDestroyedTex = ContentFinder<Texture2D>.Get(CaravanRapidResponseDestroyedTexturePath, false);
            flakHitTex = ContentFinder<Texture2D>.Get(FlakHitTexturePath, false);
            for (int i = 0; i < FlakSmokeVariantCount; i++)
                flakSmokeVariants[i] = ContentFinder<Texture2D>.Get(FlakSmokeTexturePathPrefix + (i + 1), false);
        }

        private static Texture2D ExplosionTex => explosionTex;
        private static Texture2D DropPodExplosionTex => dropPodExplosionTex ?? explosionTex;
        private static Texture2D ArtilleryShellDestroyedTex => artilleryShellDestroyedTex ?? explosionTex;
        private static Texture2D CaravanRaidersDestroyedTex => caravanRaidersDestroyedTex ?? explosionTex;
        private static Texture2D CaravanRapidResponseDestroyedTex => caravanRapidResponseDestroyedTex ?? explosionTex;
        private static Texture2D FlakHitTex => flakHitTex ?? explosionTex;

        /// <summary>2s burst at <paramref name="impact"/>.<see cref="WorldObject.DrawPos"/> when the shell arrives (hit or miss).</summary>
        public static void NotifyMortarImpactHit(WorldObject impact)
        {
            if (impact == null || impact.Destroyed) return;
            NotifyExplosionAt(impact.DrawPos);
        }

        /// <summary>2s explosion overlay at a fixed world position (e.g. mortar impact / non-drop-pod kill).</summary>
        public static void NotifyExplosionAt(Vector3 worldPos)
            => NotifyExplosionAt(worldPos, ExplosionTex, Color.white, 1f, DefaultDurationSeconds, fadeOut: true);

        /// <summary>1s drop-pod kill overlay (~20% smaller), tinted with the destroyed traveler's faction color.</summary>
        public static void NotifyDropPodExplosionAt(Vector3 worldPos, Color travelerColor)
            => NotifyExplosionAt(worldPos, DropPodExplosionTex, travelerColor, DropPodExplosionSizeScale, DropPodExplosionDurationSeconds, fadeOut: true);

        /// <summary>1s AA kill overlay for artillery/mortar shells, tinted with the shell faction color. Explosion stays for ground impacts only.</summary>
        public static void NotifyArtilleryShellDestroyedAt(Vector3 worldPos, Color shellColor)
            => NotifyExplosionAt(worldPos, ArtilleryShellDestroyedTex, shellColor, ArtilleryShellDestroyedSizeScale, ArtilleryShellDestroyedDurationSeconds, fadeOut: true);

        /// <summary>1s fade for a wiped ground raider caravan (open field or settlement raid), faction-tinted.</summary>
        public static void NotifyRaiderCaravanDestroyedAt(Vector3 worldPos, Color travelerColor)
            => NotifyExplosionAt(worldPos, CaravanRaidersDestroyedTex, travelerColor, CaravanDestroyedSizeScale, CaravanDestroyedDurationSeconds, fadeOut: true);

        /// <summary>1s fade for a wiped Rapid Response ground caravan, faction-tinted.</summary>
        public static void NotifyRapidResponseCaravanDestroyedAt(Vector3 worldPos, Color travelerColor)
            => NotifyExplosionAt(worldPos, CaravanRapidResponseDestroyedTex, travelerColor, CaravanDestroyedSizeScale, CaravanDestroyedDurationSeconds, fadeOut: true);

        /// <summary>
        /// World-map destroyed overlay for ground raider / Rapid Response caravans.
        /// Skips peaceful despawns (abort, raid victory, surviving RR) via <see cref="WorldObject_Traveler.suppressDestroyedWorldFx"/>.
        /// </summary>
        public static void TryNotifyGroundCombatCaravanDestroyed(WorldObject_Traveler traveler)
        {
            if (traveler == null || traveler.suppressDestroyedWorldFx) return;
            Color color = traveler.Faction?.Color ?? Color.white;
            Vector3 pos = traveler.DrawPos;
            if (traveler.mission == TravelerMission.Raid)
            {
                NotifyRaiderCaravanDestroyedAt(pos, color);
                return;
            }
            if (traveler.mission == TravelerMission.RapidResponseIntercept)
                NotifyRapidResponseCaravanDestroyedAt(pos, color);
        }

        /// <summary>2s flak damage overlay (hit but target still flying).</summary>
        public static void NotifyFlakHitAt(Vector3 worldPos)
            => NotifyExplosionAt(worldPos, FlakHitTex, Color.white, 1f, DefaultDurationSeconds, fadeOut: true);

        /// <summary>Fading smoke puff where a flak shell despawns. Uses FlakSmoke_1..4, random size/rotation, nudged away from nearby active smoke.</summary>
        public static void NotifyFlakSmokeAt(Vector3 worldPos)
        {
            Texture2D tex = PickFlakSmokeVariant();
            if (tex == null) return;
            Vector3 pos = SpreadFlakSmokeWorldPos(worldPos);
            float size = Rand.Range(FlakSmokeSizeMin, FlakSmokeSizeMax);
            float angle = Rand.Range(0f, 360f);
            NotifyExplosionAt(pos, tex, Color.white, size, FlakSmokeDurationSeconds, fadeOut: true, angle: angle);
        }

        private static Texture2D PickFlakSmokeVariant()
        {
            int start = Rand.Range(0, FlakSmokeVariantCount);
            for (int i = 0; i < FlakSmokeVariantCount; i++)
            {
                Texture2D tex = flakSmokeVariants[(start + i) % FlakSmokeVariantCount];
                if (tex != null) return tex;
            }
            return null;
        }

        /// <summary>Offset on the planet surface so concurrent flak puffs do not stack on one point.</summary>
        private static Vector3 SpreadFlakSmokeWorldPos(Vector3 center)
        {
            float tile = Mathf.Max(1f, Find.WorldGrid?.AverageTileSize ?? 1f);
            float spread = tile * FlakSmokeSpreadTileFraction;
            float minSep = tile * FlakSmokeMinSeparationTileFraction;
            float minSepSqr = minSep * minSep;

            Vector3 normal = center.sqrMagnitude > 0.0001f ? center.normalized : Vector3.up;
            Vector3 tangent = Vector3.Cross(normal, Vector3.up);
            if (tangent.sqrMagnitude < 0.0001f)
                tangent = Vector3.Cross(normal, Vector3.right);
            tangent.Normalize();
            Vector3 bitangent = Vector3.Cross(normal, tangent);

            Vector3 best = center;
            float bestPenalty = float.MaxValue;
            for (int attempt = 0; attempt < FlakSmokeSpreadAttempts; attempt++)
            {
                float ang = Rand.Range(0f, Mathf.PI * 2f);
                float dist = attempt == 0 ? 0f : Rand.Range(spread * 0.45f, spread);
                Vector3 candidate = center
                    + (tangent * Mathf.Cos(ang) + bitangent * Mathf.Sin(ang)) * dist;
                // Keep roughly on the sphere shell so altitude stays consistent with other world FX.
                float radius = center.magnitude;
                if (radius > 0.01f)
                    candidate = candidate.normalized * radius;

                float penalty = 0f;
                for (int i = 0; i < entries.Count; i++)
                {
                    Entry e = entries[i];
                    if (!e.fadeOut || e.tex == null) continue;
                    float dSqr = (e.worldPos - candidate).sqrMagnitude;
                    if (dSqr < minSepSqr)
                        penalty += (minSepSqr - dSqr);
                }

                if (penalty <= 0.0001f)
                    return candidate;
                if (penalty < bestPenalty)
                {
                    bestPenalty = penalty;
                    best = candidate;
                }
            }
            return best;
        }

        private static void NotifyExplosionAt(Vector3 worldPos, Texture2D tex, Color color, float sizeScale, float durationSeconds, bool fadeOut, float angle = 0f)
        {
            if (tex == null) return;
            int now = Find.TickManager?.TicksGame ?? 0;
            int lifeTicks = GenTicks.SecondsToTicks(Mathf.Max(0.1f, durationSeconds));
            entries.Add(new Entry
            {
                tex = tex,
                worldPos = worldPos,
                color = color,
                sizeScale = sizeScale > 0f ? sizeScale : 1f,
                angle = angle,
                startTick = now,
                expireTick = now + lifeTicks,
                fadeOut = fadeOut
            });
        }

        /// <summary>Draw from <see cref="WorldComponent.WorldComponentOnGUI"/> on Repaint only.</summary>
        public static void DrawWorldMapGuiOverlay()
        {
            if (entries.Count == 0) return;
            if (!WorldRendererUtility.WorldRendered || Find.WorldCamera == null) return;

            int now = Find.TickManager?.TicksGame ?? 0;
            float altitude = Find.WorldCameraDriver?.AltitudePercent ?? 1f;
            // Players reported the burst was visible "from space"; only draw it at closer zoom levels.
            bool zoomedTooFarOut = WD_WorldMapZoomUtil.IsZoomedTooFarOut(MaxVisibleAltitudePercent);
            float zoom = Mathf.Clamp(altitude, 0.05f, 1f);
            float baseSize = Mathf.Lerp(48f, 22f, zoom);

            for (int i = entries.Count - 1; i >= 0; i--)
            {
                var e = entries[i];
                if (now >= e.expireTick)
                {
                    entries.RemoveAt(i);
                    continue;
                }

                // Still prune expired entries above, but skip rendering when zoomed too far out.
                if (zoomedTooFarOut) continue;

                Texture2D tex = e.tex;
                if (tex == null) continue;

                Vector3 worldPos = e.worldPos;
                if (WorldRendererUtility.HiddenBehindTerrainNow(worldPos)) continue;

                Vector3 screen = Find.WorldCamera.WorldToScreenPoint(worldPos);
                if (screen.z <= 0f) continue;
                Vector2 ui = new Vector2(screen.x / Prefs.UIScale, (float)UI.screenHeight - screen.y / Prefs.UIScale);
                float size = baseSize * 1.1f * e.sizeScale;
                float half = size * 0.5f;
                var rect = new Rect(ui.x - half, ui.y - half, size, size);

                Color drawColor = e.color;
                if (e.fadeOut)
                {
                    int life = Mathf.Max(1, e.expireTick - e.startTick);
                    float t = Mathf.Clamp01((float)(now - e.startTick) / life);
                    // Hold briefly, then ease out to transparent.
                    float alpha = t < 0.25f ? 1f : 1f - ((t - 0.25f) / 0.75f);
                    drawColor = new Color(e.color.r, e.color.g, e.color.b, e.color.a * Mathf.Clamp01(alpha));
                }

                Color prev = GUI.color;
                GUI.color = drawColor;
                if (Mathf.Abs(e.angle) > 0.01f)
                {
                    Matrix4x4 matrix = GUI.matrix;
                    UI.RotateAroundPivot(e.angle, rect.center);
                    GUI.DrawTexture(rect, tex, ScaleMode.ScaleToFit, true);
                    GUI.matrix = matrix;
                }
                else
                {
                    GUI.DrawTexture(rect, tex, ScaleMode.ScaleToFit, true);
                }
                GUI.color = prev;
            }
        }
    }
}
