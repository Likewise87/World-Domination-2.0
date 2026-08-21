using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// CPU-baked contested Voronoi fills (Faction Territories fallback look), used by the
    /// establishment-blocked world overlay for tiles claimed by 2–4 factions.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class WD_ContestedVoronoiMaterials
    {
        public const int MaxFactions = 4;
        /// <summary>Match solid blocked-tile fill alpha so spots are not darker than neighbors.</summary>
        public const float ContestedFillAlpha = 0.50f;
        private const int TexSize = 64;
        private const int RenderQueue = 3581;

        private static readonly Color RuinColor = new Color(0.12f, 0.12f, 0.12f, 1f);
        private static readonly Dictionary<int, Material> matByKey = new Dictionary<int, Material>();
        private static readonly Dictionary<int, Texture2D> texByKey = new Dictionary<int, Texture2D>();
        private static readonly List<int> sortedIdsScratch = new List<int>(MaxFactions);
        private static readonly List<Color> colorsScratch = new List<Color>(MaxFactions);

        /// <summary>
        /// Material for a contested faction set. Pass distinct factions (null = ruins/grey).
        /// Requires at least 2 entries; returns null otherwise.
        /// </summary>
        public static Material GetMaterial(List<Faction> factions)
        {
            if (factions == null || factions.Count < 2) return null;
            if (!UnityData.IsInMainThread) return null;

            sortedIdsScratch.Clear();
            colorsScratch.Clear();
            for (int i = 0; i < factions.Count && sortedIdsScratch.Count < MaxFactions; i++)
            {
                Faction f = factions[i];
                int id = FactionLoadId(f);
                bool seen = false;
                for (int j = 0; j < sortedIdsScratch.Count; j++)
                {
                    if (sortedIdsScratch[j] == id)
                    {
                        seen = true;
                        break;
                    }
                }
                if (seen) continue;
                sortedIdsScratch.Add(id);
                colorsScratch.Add(ColorForFaction(f));
            }

            if (sortedIdsScratch.Count < 2) return null;

            // Stable key: sort ids ascending and fold hashes.
            for (int i = 0; i < sortedIdsScratch.Count - 1; i++)
            {
                for (int j = i + 1; j < sortedIdsScratch.Count; j++)
                {
                    if (sortedIdsScratch[j] < sortedIdsScratch[i])
                    {
                        int tmpId = sortedIdsScratch[i];
                        sortedIdsScratch[i] = sortedIdsScratch[j];
                        sortedIdsScratch[j] = tmpId;
                        Color tmpC = colorsScratch[i];
                        colorsScratch[i] = colorsScratch[j];
                        colorsScratch[j] = tmpC;
                    }
                }
            }

            int key = sortedIdsScratch.Count;
            for (int i = 0; i < sortedIdsScratch.Count; i++)
                key = HashCombine(key, sortedIdsScratch[i]);

            if (matByKey.TryGetValue(key, out Material cached) && cached != null)
                return cached;

            Texture2D tex = GetOrBuildTexture(key, colorsScratch);
            Color tint = Color.white;
            tint.a = ContestedFillAlpha;
            Material mat = MaterialPool.MatFrom(tex, ShaderDatabase.MetaOverlay, tint, RenderQueue);
            matByKey[key] = mat;
            return mat;
        }

        private static Texture2D GetOrBuildTexture(int key, List<Color> colors)
        {
            if (texByKey.TryGetValue(key, out Texture2D existing) && existing != null)
                return existing;

            int n = Mathf.Max(1, colors.Count);
            // Equal site count per faction (FT's random site%N often skews ~70/30).
            int sitesPerFaction = Mathf.Max(6, Mathf.Clamp(n * 6, 12, 36) / n);
            int siteCount = sitesPerFaction * n;
            int seed = HashInt(key);

            // Jittered lattice so cell areas stay roughly balanced (pure random piles sites).
            int gridSide = Mathf.CeilToInt(Mathf.Sqrt(siteCount));
            float cell = TexSize / (float)gridSide;
            int cellPx = Mathf.Max(1, Mathf.FloorToInt(cell));

            int[] siteX = new int[siteCount];
            int[] siteY = new int[siteCount];
            int[] siteFaction = new int[siteCount];
            for (int k = 0; k < siteCount; k++)
            {
                int gx = k % gridSide;
                int gy = k / gridSide;
                seed = HashInt(seed + -1640531527 + k * 1013);
                int jx = (seed & 0x7FFFFFFF) % cellPx;
                seed = HashInt(seed + -2048144789 + k * 2029);
                int jy = (seed & 0x7FFFFFFF) % cellPx;
                siteX[k] = Mathf.Clamp(Mathf.FloorToInt(gx * cell) + jx, 0, TexSize - 1);
                siteY[k] = Mathf.Clamp(Mathf.FloorToInt(gy * cell) + jy, 0, TexSize - 1);
                // Round-robin ownership: same number of seeds per faction.
                siteFaction[k] = k % n;
            }

            var tex = new Texture2D(TexSize, TexSize, TextureFormat.RGBA32, false)
            {
                name = "WD_ContestedVoronoi_" + key,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            Color32[] pixels = new Color32[TexSize * TexSize];
            for (int y = 0; y < TexSize; y++)
            {
                for (int x = 0; x < TexSize; x++)
                {
                    int bestSite = -1;
                    int bestDist = int.MaxValue;
                    for (int s = 0; s < siteCount; s++)
                    {
                        int dx = Mathf.Abs(x - siteX[s]);
                        int dy = Mathf.Abs(y - siteY[s]);
                        dx = Mathf.Min(dx, TexSize - dx);
                        dy = Mathf.Min(dy, TexSize - dy);
                        int d2 = dx * dx + dy * dy;
                        if (d2 < bestDist)
                        {
                            bestDist = d2;
                            bestSite = s;
                        }
                    }

                    // No edge darken: with dense spots most pixels sit near a boundary, so FT's
                    // *0.75 edge pass made the whole contested fill look muddy vs solid neighbors.
                    int colorIndex = bestSite >= 0 ? siteFaction[bestSite] : 0;
                    pixels[y * TexSize + x] = colors[Mathf.Clamp(colorIndex, 0, n - 1)];
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            texByKey[key] = tex;
            return tex;
        }

        private static Color ColorForFaction(Faction faction)
        {
            if (faction == null) return RuinColor;
            Color c = faction.Color;
            c.a = 1f;
            return c;
        }

        private static int FactionLoadId(Faction faction) => faction?.loadID ?? int.MinValue;

        private static int HashInt(int x)
        {
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            return x;
        }

        private static int HashCombine(int a, int b)
        {
            int h = 486187739;
            h = (h * 16777619) ^ a;
            h = (h * 16777619) ^ b;
            return HashInt(h);
        }
    }
}
