using System.Collections.Generic;
using UnityEngine;
using Verse;
using RimWorld;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Portrait lookup for roster / outpost pawn lists.
    /// Do not cache <see cref="PortraitsCache.Get"/> results in a Dictionary: those textures are pooled
    /// RenderTextures and get reused for other pawns, which mixes faces across rows.
    /// </summary>
    public static class PawnPortraitUIUtils
    {
        private static readonly HashSet<string> FailedPortraitKeys = new HashSet<string>();

        public static string BuildCacheKey(Pawn pawn, VirtualPawnSummary? summary = null)
        {
            if (pawn == null) return "";
            string id = pawn.GetUniqueLoadID() ?? pawn.ThingID ?? "";
            if (summary == null) return "pawn|" + id;
            return "pawn|" + id + "|" + (summary.name ?? "") + "|" + Mathf.FloorToInt(summary.biologicalAgeYears)
                + "|" + summary.shooting + "|" + summary.melee + "|" + summary.construction;
        }

        /// <summary>
        /// Prefer <see cref="GetPortrait(Pawn, Vector2)"/>. The cache parameters are ignored (kept for call-site compatibility).
        /// </summary>
        public static Texture? GetPortrait(
            Pawn pawn,
            string cacheKey,
            Vector2 size,
            System.Collections.Generic.Dictionary<string, Texture> cache,
            int cacheMax)
        {
            _ = cache;
            _ = cacheMax;
            return GetPortrait(pawn, size, cacheKey);
        }

        public static Texture? GetPortrait(Pawn pawn, Vector2 size, string? failKey = null)
        {
            if (pawn == null || pawn.Destroyed) return null;
            if (pawn.RaceProps?.Humanlike != true)
                return pawn.def?.uiIcon ?? pawn.kindDef?.race?.uiIcon;

            if (string.IsNullOrEmpty(failKey))
                failKey = BuildCacheKey(pawn, VirtualPawnSummary.FromPawn(pawn));

            if (FailedPortraitKeys.Contains(failKey))
                return GetFallbackIcon(pawn);

            EnsurePawnPortraitGraphics(pawn);

            Texture? tex = TryPortraitsCache(pawn, size, renderHeadgear: true, renderClothes: true);
            if (tex == null)
                tex = TryPortraitsCache(pawn, size, renderHeadgear: false, renderClothes: false);

            if (tex != null)
                return tex;

            FailedPortraitKeys.Add(failKey);
            return GetFallbackIcon(pawn);
        }

        private static void EnsurePawnPortraitGraphics(Pawn pawn)
        {
            try
            {
                pawn.Drawer?.renderer?.EnsureGraphicsInitialized();
            }
            catch
            {
                // Portrait will fall back to ui icon.
            }
        }

        private static Texture? TryPortraitsCache(Pawn pawn, Vector2 size, bool renderHeadgear, bool renderClothes)
        {
            try
            {
                return PortraitsCache.Get(pawn, size, Rot4.South, Vector3.zero, 1f,
                    supersample: true, compensateForUIScale: true,
                    renderHeadgear: renderHeadgear, renderClothes: renderClothes,
                    null, null, false, null);
            }
            catch
            {
                return null;
            }
        }

        private static Texture? GetFallbackIcon(Pawn pawn)
        {
            if (pawn == null) return null;
            return pawn.def?.uiIcon ?? pawn.kindDef?.race?.uiIcon;
        }

        /// <summary>Clear failed-key blacklist (e.g. after graphics become available).</summary>
        public static void ClearFailedKeys() => FailedPortraitKeys.Clear();
    }
}
