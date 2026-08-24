using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// World bearing for assault-map mortar ingress. Bearing math mirrors
    /// TSA.ForceRaidDirection WorldDirectionResolver (tangent-plane chord projection).
    /// Bands are ~20% of an edge (FRD segmented corners); locked once per volley.
    /// </summary>
    public static class AssaultArtilleryIngressDirection
    {
        public const float IngressSpanFraction = 0.20f;
        private const int BandDepth = 2;

        private enum IngressEdge
        {
            Left,
            Right,
            Top,
            Bottom,
            LeftTop,
            RightTop,
            LeftBottom,
            RightBottom
        }

        private struct Weights8
        {
            public int Left, Right, Top, Bottom, LeftTop, RightTop, LeftBottom, RightBottom;

            public void Zero() =>
                Left = Right = Top = Bottom = LeftTop = RightTop = LeftBottom = RightBottom = 0;

            public int Sum() =>
                Left + Right + Top + Bottom + LeftTop + RightTop + LeftBottom + RightBottom;

            public void NormalizeTo100BiasMax()
            {
                int total = Sum();
                if (total == 100) return;
                int[] a = { Left, Right, Top, Bottom, LeftTop, RightTop, LeftBottom, RightBottom };
                int idx = 0;
                for (int i = 1; i < a.Length; i++)
                    if (a[i] > a[idx]) idx = i;
                a[idx] += 100 - total;
                Left = a[0]; Right = a[1]; Top = a[2]; Bottom = a[3];
                LeftTop = a[4]; RightTop = a[5]; LeftBottom = a[6]; RightBottom = a[7];
            }
        }

        /// <summary>Pick dominant bearing once and build a fixed ~20% edge band for the whole volley.</summary>
        public static CellRect LockIngressBand(Map map, int sourceTile)
        {
            if (map == null) return CellRect.Empty;

            int mapTile = map.Parent is MapParent mp ? mp.Tile : map.Tile;
            if (sourceTile < 0 || !TryComputeIngressWeights8(mapTile, sourceTile, out Weights8 w))
                return CellRect.Empty;

            IngressEdge dir = ChooseDominantDir(w);
            CellRect band = GetSegmentedBandRect(map, dir, BandDepth);
            return band.Area > 0 ? band : CellRect.Empty;
        }

        public static bool TryPickIngressCell(Map map, int sourceTile, out IntVec3 edgeCell)
        {
            edgeCell = IntVec3.Invalid;
            if (map == null) return false;

            CellRect band = LockIngressBand(map, sourceTile);
            if (band.Area > 0)
            {
                edgeCell = band.RandomCell;
                return edgeCell.InBounds(map);
            }

            edgeCell = CellFinder.RandomEdgeCell(map);
            return edgeCell.IsValid;
        }

        public static bool TryPickCellInBand(CellRect band, Map map, out IntVec3 edgeCell)
        {
            edgeCell = IntVec3.Invalid;
            if (map == null || band.Area <= 0) return false;
            edgeCell = band.RandomCell;
            return edgeCell.InBounds(map);
        }

        private static bool TryComputeIngressWeights8(int mapTile, int sourceTile, out Weights8 weights)
        {
            weights = default;
            if (mapTile < 0 || sourceTile < 0) return false;

            if (!TryCompute4Dir(mapTile, sourceTile, out int left, out int right, out int top, out int bottom))
                return false;

            WeaveCorners(left, right, top, bottom, ref weights);
            return true;
        }

        private static bool TryCompute4Dir(
            int mapTile, int sourceTile,
            out int weightLeft, out int weightRight, out int weightTop, out int weightBottom)
        {
            weightLeft = weightRight = weightTop = weightBottom = 0;
            var grid = Find.WorldGrid;
            if (grid == null) return false;

            Vector3 from = grid.GetTileCenter(mapTile).normalized;
            Vector3 to = grid.GetTileCenter(sourceTile).normalized;

            Vector3 up = from;
            Vector3 globalNorth = new Vector3(0f, 1f, 0f);
            Vector3 northT = globalNorth - Vector3.Dot(globalNorth, up) * up;
            if (northT.sqrMagnitude < 1e-8f)
            {
                Vector3 alt = new Vector3(0f, 0f, 1f);
                northT = alt - Vector3.Dot(alt, up) * up;
            }
            northT.Normalize();

            Vector3 eastT = Vector3.Cross(northT, up).normalized;

            Vector3 chord = to - from;
            if (chord.sqrMagnitude < 1e-8f)
            {
                weightLeft = 100;
                return true;
            }

            Vector3 tangentDir = chord - Vector3.Dot(chord, up) * up;
            if (tangentDir.sqrMagnitude < 1e-8f)
            {
                weightLeft = 100;
                return true;
            }
            tangentDir.Normalize();

            float eComp = Vector3.Dot(tangentDir, eastT);
            float nComp = Vector3.Dot(tangentDir, northT);

            float east = Mathf.Max(0f, eComp);
            float west = Mathf.Max(0f, -eComp);
            float north = Mathf.Max(0f, nComp);
            float south = Mathf.Max(0f, -nComp);

            float sum = east + west + north + south;
            if (sum <= 1e-6f)
            {
                weightTop = 100;
                return true;
            }

            east /= sum;
            west /= sum;
            north /= sum;
            south /= sum;

            weightRight = Mathf.RoundToInt(west * 100f);
            weightLeft = Mathf.RoundToInt(east * 100f);
            weightTop = Mathf.RoundToInt(north * 100f);
            weightBottom = Mathf.RoundToInt(south * 100f);

            int total = weightTop + weightBottom + weightRight + weightLeft;
            if (total != 100)
            {
                int[] vals = { weightTop, weightBottom, weightRight, weightLeft };
                int idx = 0;
                for (int i = 1; i < 4; i++)
                    if (vals[i] > vals[idx]) idx = i;
                vals[idx] += 100 - total;
                weightTop = vals[0];
                weightBottom = vals[1];
                weightRight = vals[2];
                weightLeft = vals[3];
            }

            return true;
        }

        private static void WeaveCorners(int l, int r, int t, int b, ref Weights8 outW)
        {
            outW.Zero();
            int positives = (l > 0 ? 1 : 0) + (r > 0 ? 1 : 0) + (t > 0 ? 1 : 0) + (b > 0 ? 1 : 0);
            if (positives <= 1)
            {
                outW.Left = l;
                outW.Right = r;
                outW.Top = t;
                outW.Bottom = b;
                outW.NormalizeTo100BiasMax();
                return;
            }

            const float gamma = 0.60f;
            const float power = 2.0f;
            const float keepWeakFrac = 0.20f;

            void Cornerize(int horiz, int vert, out int diag, out int oHoriz, out int oVert)
            {
                float h = horiz, v = vert;
                bool vertIsStronger = v >= h;
                float hi = vertIsStronger ? v : h;
                float lo = vertIsStronger ? h : v;
                float s = h + v;
                if (s <= 0f)
                {
                    diag = 0;
                    oHoriz = 0;
                    oVert = 0;
                    return;
                }

                float ratio = hi > 0f ? lo / hi : 0f;
                float diagF = lo + gamma * Mathf.Pow(ratio, power) * hi;
                diagF = Mathf.Clamp(diagF, 0f, s);
                float remainderF = s - diagF;
                float weakKeepF = Mathf.Min(keepWeakFrac * lo, remainderF);
                float strongKeepF = remainderF - weakKeepF;

                int iDiag = Mathf.RoundToInt(diagF);
                int iWeak = Mathf.RoundToInt(weakKeepF);
                int iStrong = Mathf.RoundToInt(strongKeepF);
                int delta = (int)s - (iDiag + iWeak + iStrong);
                if (delta != 0)
                {
                    int newDiag = iDiag + delta;
                    if (newDiag < 0)
                    {
                        int need = -newDiag;
                        int takeStrong = Mathf.Min(iStrong, need);
                        iStrong -= takeStrong;
                        need -= takeStrong;
                        iWeak = Mathf.Max(0, iWeak - need);
                        newDiag = 0;
                    }
                    iDiag = newDiag;
                }

                if (vertIsStronger)
                {
                    oHoriz = iWeak;
                    oVert = iStrong;
                }
                else
                {
                    oHoriz = iStrong;
                    oVert = iWeak;
                }
                diag = iDiag;
            }

            if (r > 0 && t > 0)
            {
                Cornerize(r, t, out int d, out int oR, out int oT);
                outW.RightTop = d;
                outW.Right = oR;
                outW.Top = oT;
            }
            else if (l > 0 && t > 0)
            {
                Cornerize(l, t, out int d, out int oL, out int oT);
                outW.LeftTop = d;
                outW.Left = oL;
                outW.Top = oT;
            }
            else if (l > 0 && b > 0)
            {
                Cornerize(l, b, out int d, out int oL, out int oB);
                outW.LeftBottom = d;
                outW.Left = oL;
                outW.Bottom = oB;
            }
            else if (r > 0 && b > 0)
            {
                Cornerize(r, b, out int d, out int oR, out int oB);
                outW.RightBottom = d;
                outW.Right = oR;
                outW.Bottom = oB;
            }
            else
            {
                outW.Left = l;
                outW.Right = r;
                outW.Top = t;
                outW.Bottom = b;
            }

            outW.NormalizeTo100BiasMax();
        }

        private static IngressEdge ChooseDominantDir(Weights8 w)
        {
            int[] weights =
            {
                w.Left, w.Right, w.Top, w.Bottom,
                w.LeftTop, w.RightTop, w.LeftBottom, w.RightBottom
            };
            IngressEdge[] dirs =
            {
                IngressEdge.Left, IngressEdge.Right, IngressEdge.Top, IngressEdge.Bottom,
                IngressEdge.LeftTop, IngressEdge.RightTop, IngressEdge.LeftBottom, IngressEdge.RightBottom
            };

            int best = 0;
            for (int i = 1; i < weights.Length; i++)
            {
                if (weights[i] > weights[best])
                    best = i;
            }
            return dirs[best];
        }

        /// <summary>
        /// FRD-style segmented bands: cardinals = center span%; corners = span% L-arms
        /// (eastmost of south / southmost of east), arm chosen once via Rand.Bool.
        /// </summary>
        private static CellRect GetSegmentedBandRect(Map map, IngressEdge dir, int bandWidth)
        {
            CellRect rect = CellRect.WholeMap(map);
            if (rect.Area <= 0) return CellRect.Empty;

            int depth = Mathf.Clamp(bandWidth, 1, Mathf.Max(rect.Width, rect.Height));
            float span = Mathf.Clamp01(IngressSpanFraction);
            int fullH = rect.Height;
            int fullW = rect.Width;
            int spanH = Mathf.Max(1, Mathf.RoundToInt(fullH * span));
            int spanW = Mathf.Max(1, Mathf.RoundToInt(fullW * span));

            int midZStart = rect.minZ + (fullH - spanH) / 2;
            int midXStart = rect.minX + (fullW - spanW) / 2;

            switch (dir)
            {
                case IngressEdge.Left:
                    return new CellRect(rect.minX, midZStart, depth, spanH);
                case IngressEdge.Right:
                    return new CellRect(rect.maxX - (depth - 1), midZStart, depth, spanH);
                case IngressEdge.Top:
                    return new CellRect(midXStart, rect.maxZ - (depth - 1), spanW, depth);
                case IngressEdge.Bottom:
                    return new CellRect(midXStart, rect.minZ, spanW, depth);

                case IngressEdge.RightTop:
                    if (Rand.Bool)
                        return new CellRect(rect.maxX - (depth - 1), rect.maxZ - (spanH - 1), depth, spanH);
                    return new CellRect(rect.maxX - (spanW - 1), rect.maxZ - (depth - 1), spanW, depth);

                case IngressEdge.RightBottom:
                    if (Rand.Bool)
                        return new CellRect(rect.maxX - (depth - 1), rect.minZ, depth, spanH);
                    return new CellRect(rect.maxX - (spanW - 1), rect.minZ, spanW, depth);

                case IngressEdge.LeftTop:
                    if (Rand.Bool)
                        return new CellRect(rect.minX, rect.maxZ - (spanH - 1), depth, spanH);
                    return new CellRect(rect.minX, rect.maxZ - (depth - 1), spanW, depth);

                case IngressEdge.LeftBottom:
                    if (Rand.Bool)
                        return new CellRect(rect.minX, rect.minZ, depth, spanH);
                    return new CellRect(rect.minX, rect.minZ, spanW, depth);

                default:
                    return CellRect.Empty;
            }
        }
    }
}
