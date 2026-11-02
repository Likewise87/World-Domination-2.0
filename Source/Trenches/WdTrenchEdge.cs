using Verse;

namespace TSA_WorldDomination
{
    /// <summary>One undirected trench segment between neighboring tiles.</summary>
    public class WdTrenchEdge : IExposable
    {
        public int tileA = -1;
        public int tileB = -1;

        public WdTrenchEdge() { }

        public WdTrenchEdge(int a, int b)
        {
            if (a <= b)
            {
                tileA = a;
                tileB = b;
            }
            else
            {
                tileA = b;
                tileB = a;
            }
        }

        public bool Involves(int tile) => tile == tileA || tile == tileB;

        public int Other(int tile) => tile == tileA ? tileB : tileA;

        public void ExposeData()
        {
            Scribe_Values.Look(ref tileA, "tileA", -1);
            Scribe_Values.Look(ref tileB, "tileB", -1);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && tileA > tileB)
            {
                int tmp = tileA;
                tileA = tileB;
                tileB = tmp;
            }
        }

        public override bool Equals(object obj)
        {
            if (obj is not WdTrenchEdge o) return false;
            return tileA == o.tileA && tileB == o.tileB;
        }

        public override int GetHashCode() => (tileA * 397) ^ tileB;
    }
}
