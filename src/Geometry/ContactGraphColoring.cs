namespace Ember.Collision
{
    /// <summary>确定性的接触图贪心着色基础设施，不执行任何物理求解。</summary>
    public static unsafe class ContactGraphColoring
    {
        /// <summary>
        /// 按 canonical pair key 排序后分配最小可用颜色。
        /// 同色 edge 保证不共享 body；返回实际使用的颜色数。
        /// </summary>
        public static int Assign(
            CandidatePair* pairs,
            int pairCount,
            int bodyCount,
            int* colors,
            int* order)
        {
            if (pairCount <= 0) return 0;

            for (int i = 0; i < pairCount; i++)
            {
                colors[i] = -1;
                order[i] = i;
            }

            // Insertion sort is deterministic and allocation-free. Coloring is a
            // future optimization path, so correctness outranks this cold setup cost.
            for (int i = 1; i < pairCount; i++)
            {
                int value = order[i];
                ulong key = Key(pairs[value]);
                int j = i - 1;
                while (j >= 0 && Key(pairs[order[j]]) > key)
                {
                    order[j + 1] = order[j];
                    j--;
                }

                order[j + 1] = value;
            }

            int colorCount = 0;
            bool hasPreviousKey = false;
            ulong previousKey = 0;
            for (int sortedIndex = 0; sortedIndex < pairCount; sortedIndex++)
            {
                int edgeIndex = order[sortedIndex];
                CandidatePair edge = pairs[edgeIndex];
                if (!IsValid(edge, bodyCount)) continue;

                ulong key = Key(edge);
                if (hasPreviousKey && key == previousKey) continue;
                hasPreviousKey = true;
                previousKey = key;

                int color = 0;
                while (HasConflict(pairs, colors, order, sortedIndex, edge, color)) color++;
                colors[edgeIndex] = color;
                if (color >= colorCount) colorCount = color + 1;
            }

            return colorCount;
        }

        private static bool HasConflict(
            CandidatePair* pairs,
            int* colors,
            int* order,
            int sortedIndex,
            CandidatePair edge,
            int color)
        {
            for (int i = 0; i < sortedIndex; i++)
            {
                int otherIndex = order[i];
                if (colors[otherIndex] != color) continue;
                CandidatePair other = pairs[otherIndex];
                if (SharesBody(edge, other)) return true;
            }

            return false;
        }

        private static bool IsValid(CandidatePair edge, int bodyCount) =>
            edge.BodyA >= 0 && edge.BodyA < edge.BodyB && edge.BodyB < bodyCount;

        private static bool SharesBody(CandidatePair a, CandidatePair b) =>
            a.BodyA == b.BodyA || a.BodyA == b.BodyB
            || a.BodyB == b.BodyA || a.BodyB == b.BodyB;

        private static ulong Key(CandidatePair edge) => PairKey.PackIndices(edge.BodyA, edge.BodyB);
    }
}
