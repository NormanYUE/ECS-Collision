using Unity.Mathematics;

namespace Ember.Collision
{
    /// <summary>
    /// LBVH 构建核心（工具类，静态豁免，纯函数 + 裸指针，Burst 可编译且 <b>CLI 可测</b>）。
    ///
    /// 全部函数以「叶数组 + 节点数组」为输入输出，不依赖 <c>NativeArray</c>，
    /// 因此可以在纯 .NET 下用 <c>stackalloc</c> 直接对拍验证。
    ///
    /// 树形：叶节点占据 <c>[0, leafCapacity)</c>，内部节点自 <c>leafCapacity</c> 起，
    /// 按层自底向上两两合并，构成一棵<b>完美二叉树</b>（叶数补足到 2 的幂）。
    ///
    /// 为什么要补足到 2 的幂：补零后每一层节点数都是偶数，合并规则无需处理
    /// 「奇数个尾节点」的退化情形（退化节点会让「左子树 × 右子树」的 pair 规则
    /// 产生自配对 → 重复 pair）。代价是节点数上界为 <c>2 * nextPow2(n) - 1</c>。
    ///
    /// 空叶（下标 ≥ bodyCount）以 <see cref="BvhNode.MaxLeaf"/> = -1 标记，
    /// 包围盒为 <see cref="Aabb.Empty"/>，合并时用 min/max 自然传播。
    /// </summary>
    public static class BvhBuilder
    {
        /// <summary>遍历结果：命中数与栈溢出标志（溢出即结果被截断，必须告警）。</summary>
        public struct TraversalResult
        {
            /// <summary>命中（或已写入）的叶数量。</summary>
            public int Count;

            /// <summary>是否因遍历栈不足而被截断。</summary>
            public bool Overflow;

            /// <summary>构造。</summary>
            public TraversalResult(int count, bool overflow)
            {
                Count = count;
                Overflow = overflow;
            }
        }

        /// <summary>构造叶节点（含排序位置与包围盒）。</summary>
        public static unsafe void BuildLeaves(
            BvhNode* nodes,
            Aabb* bounds,
            int* order,
            int bodyCount,
            int leafCapacity)
        {
            for (int i = 0; i < bodyCount; i++)
            {
                int body = order[i];
                nodes[i] = new BvhNode
                {
                    Bounds = bounds[body],
                    Left = i,
                    Right = -1,
                    MinLeaf = i,
                    MaxLeaf = i,
                };
            }

            for (int i = bodyCount; i < leafCapacity; i++)
            {
                nodes[i] = new BvhNode
                {
                    Bounds = Aabb.Empty,
                    Left = -1,
                    Right = -1,
                    MinLeaf = int.MaxValue,
                    MaxLeaf = -1,
                };
            }
        }

        /// <summary>
        /// 合并单个父节点（并行 Job 的执行单元，每父节点一次执行）。
        /// 参数是<b>数量</b>而非下标，调用方只需按层给出三个标量。
        /// </summary>
        public static unsafe void MergeParent(
            BvhNode* nodes,
            int levelStart,
            int parentStart,
            int parentIndex)
        {
            int left = levelStart + 2 * parentIndex;
            int right = left + 1;

            ref BvhNode l = ref nodes[left];
            ref BvhNode r = ref nodes[right];

            nodes[parentStart + parentIndex] = new BvhNode
            {
                Bounds = Aabb.Union(l.Bounds, r.Bounds),
                Left = left,
                Right = right,
                MinLeaf = math.min(l.MinLeaf, r.MinLeaf),
                MaxLeaf = math.max(l.MaxLeaf, r.MaxLeaf),
            };
        }

        /// <summary>
        /// 合并一层：把 <paramref name="levelStart"/> 起的 <paramref name="levelCount"/> 个节点
        /// 两两合并为 <c>levelCount / 2</c> 个父节点，写入自 <paramref name="parentStart"/>。
        /// 串行版本，供顶层收尾与单测使用；并行 Job 直接调用 <see cref="MergeParent"/>，
        /// 因此两条路径共用同一份合并逻辑。
        /// </summary>
        public static unsafe void MergeLevel(
            BvhNode* nodes,
            int levelStart,
            int levelCount,
            int parentStart)
        {
            int parentCount = levelCount >> 1;
            for (int j = 0; j < parentCount; j++)
                MergeParent(nodes, levelStart, parentStart, j);
        }

        /// <summary>
        /// 完美二叉树的根节点下标。叶数补足到 2 的幂后，节点总数为
        /// <c>2 * leafCapacity - 1</c>，即根恒为<b>最后一个节点</b>。
        /// 采用解析式而非「构建后回读」是为了让 pair 计数 Job 无需等待
        /// BVH 收尾 Job 的返回值——主线程在调度期即可确定根下标，
        /// 从而省掉一次流水线同步。
        /// </summary>
        public static int RootIndex(int leafCapacity) => 2 * leafCapacity - 2;

        /// <summary>
        /// 自 <paramref name="levelStart"/> 起把剩余各层全部合并到根，返回根节点下标。
        /// 供顶层（宽度已经很小）串行完成，避免为零星节点额外调度 Job。
        /// </summary>
        public static unsafe int MergeToRoot(BvhNode* nodes, int levelStart, int levelCount, int parentStart)
        {
            while (levelCount > 1)
            {
                int parentCount = levelCount >> 1;
                MergeLevel(nodes, levelStart, levelCount, parentStart);
                levelStart = parentStart;
                levelCount = parentCount;
                parentStart += parentCount;
            }

            return levelStart;
        }

        /// <summary>
        /// 计算每个叶的候选 pair 数：以叶 <paramref name="leafIndex"/> 为 A 侧，
        /// 只向<b>排序位置更大</b>的叶推进（<c>node.MaxLeaf &gt; leafIndex</c> 剪枝），
        /// 因此每个无序 pair 恰好被计数一次。
        ///
        /// 遍历使用调用方提供的线程局部栈。栈深度上界为 <c>2 * 树高</c>；
        /// 返回时把栈写坏（溢出）的叶数作为返回值（&gt; 0 表示需要加大栈深）。
        /// </summary>
        public static unsafe TraversalResult CountHierarchyOverlaps(
            BvhNode* nodes,
            int root,
            int leafIndex,
            Aabb* queryBounds,
            int* stack,
            int stackCapacity,
            int* sortedOrder = null,
            byte* bodyFlags = null,
            byte participationBits = 0)
        {
            // 自身未参与就不产出任何 pair（对手侧在叶分支里判）。
            if (!IsParticipating(bodyFlags, sortedOrder, leafIndex, participationBits))
                return new TraversalResult(0, false);

            int count = 0;
            int sp = 0;
            stack[sp++] = root;

            while (sp > 0)
            {
                int node = stack[--sp];
                ref BvhNode n = ref nodes[node];

                // 子树不含排序位置更大的叶 → 其全部 pair 已在别处处理，整体剪掉。
                // 这一条是「每个 pair 恰好产出一次、无需去重集合」的全部理由。
                if (n.MaxLeaf <= leafIndex) continue;
                if (!Aabb.Overlaps(n.Bounds, *queryBounds)) continue;

                if (n.Right < 0)
                {
                    // 能走到这里说明 n.MaxLeaf > leafIndex，且包围盒相交。
                    // 对手未参与碰撞就不算数（它反正会被窄相丢掉）。
                    if (!IsParticipating(bodyFlags, sortedOrder, n.MaxLeaf, participationBits)) continue;
                    count++;
                    continue;
                }

                if (sp + 2 > stackCapacity)
                    return new TraversalResult(count, true);

                stack[sp++] = n.Left;
                stack[sp++] = n.Right;
            }

            return new TraversalResult(count, false);
        }

        /// <summary>
        /// 候选对手（或自身）是否参与碰撞。
        ///
        /// <paramref name="bodyFlags"/> 为空（未开启过滤）时恒为 true，
        /// 因此未传该参数的调用方（含全部 CLI 单测）行为完全不变。
        /// 判定位与窄相 <c>NarrowphaseMath.IsPairEligible</c> 用的是同一组位：
        /// 只有两侧都参与，pair 才可能被窄相接受；任一侧未参与就必然被丢掉。
        /// </summary>
        private static unsafe bool IsParticipating(
            byte* bodyFlags, int* sortedOrder, int leaf, byte participationBits) =>
            bodyFlags == null || sortedOrder == null
            || (bodyFlags[sortedOrder[leaf]] & participationBits) == participationBits;

        /// <summary>
        /// 与 <see cref="CountHierarchyOverlaps"/> 同一次遍历，但把命中的叶直接写成
        /// <see cref="CandidatePair"/>（已归一化为 <c>BodyA &lt; BodyB</c>）。
        ///
        /// 刻意<b>不</b>复用遍历栈充当命中缓冲：命中数可能远大于栈深，
        /// 复用会把 DFS 栈挤爆。输出写入 <paramref name="output"/> 的
        /// <paramref name="outputOffset"/> 起，调用方按前缀和保证区间互不重叠。
        /// </summary>
        public static unsafe TraversalResult CollectPairsInto(
            BvhNode* nodes,
            int root,
            int leafIndex,
            Aabb* queryBounds,
            int* stack,
            int stackCapacity,
            int* sortedOrder,
            CandidatePair* output,
            int outputOffset,
            int outputLimit,
            byte* bodyFlags = null,
            byte participationBits = 0)
        {
            // 自身未参与就不产出任何 pair（对手侧在叶分支里判）。
            if (!IsParticipating(bodyFlags, sortedOrder, leafIndex, participationBits))
                return new TraversalResult(0, false);

            int count = 0;
            int sp = 0;
            stack[sp++] = root;
            int self = sortedOrder[leafIndex];

            while (sp > 0)
            {
                int node = stack[--sp];
                ref BvhNode n = ref nodes[node];

                if (n.MaxLeaf <= leafIndex) continue;
                if (!Aabb.Overlaps(n.Bounds, *queryBounds)) continue;

                if (n.Right < 0)
                {
                    if (!IsParticipating(bodyFlags, sortedOrder, n.MaxLeaf, participationBits)) continue;
                    if (count >= outputLimit)
                        return new TraversalResult(count, true);

                    int other = sortedOrder[n.MaxLeaf];
                    output[outputOffset + count] = new CandidatePair
                    {
                        BodyA = self < other ? self : other,
                        BodyB = self < other ? other : self,
                    };
                    count++;
                    continue;
                }

                if (sp + 2 > stackCapacity)
                    return new TraversalResult(count, true);

                stack[sp++] = n.Left;
                stack[sp++] = n.Right;
            }

            return new TraversalResult(count, false);
        }

        /// <summary>
        /// 单查询：收集与 <paramref name="queryBounds"/> 相交的叶（不过滤下标）。
        /// 供射线 / 重叠查询与调试使用，返回写入数量。
        /// </summary>
        public static unsafe TraversalResult QueryOverlap(
            BvhNode* nodes,
            int root,
            Aabb* queryBounds,
            int* stack,
            int stackCapacity,
            int* output,
            int outputLimit)
        {
            int count = 0;
            int sp = 0;
            stack[sp++] = root;

            while (sp > 0)
            {
                int node = stack[--sp];
                ref BvhNode n = ref nodes[node];

                if (!Aabb.Overlaps(n.Bounds, *queryBounds)) continue;

                if (n.Right < 0)
                {
                    if (n.MaxLeaf < 0) continue; // 补位空叶
                    if (count >= outputLimit)
                        return new TraversalResult(count, true);
                    output[count++] = n.MaxLeaf;
                    continue;
                }

                if (sp + 2 > stackCapacity)
                    return new TraversalResult(count, true);

                stack[sp++] = n.Left;
                stack[sp++] = n.Right;
            }

            return new TraversalResult(count, false);
        }

        /// <summary>
        /// 暴力 O(n²) pair 统计（仅供测试 / 调试对拍，热路径禁用）。
        /// </summary>
        public static unsafe int CountBruteForcePairs(Aabb* bounds, int count)
        {
            int pairs = 0;
            for (int i = 0; i < count; i++)
                for (int j = i + 1; j < count; j++)
                    if (Aabb.Overlaps(bounds[i], bounds[j])) pairs++;
            return pairs;
        }

        /// <summary>
        /// 暴力 O(n²) pair 枚举（仅测试用）。输出为升序稠密下标对。
        /// </summary>
        public static unsafe int CollectBruteForcePairs(Aabb* bounds, int count, int* output, int outputLimit)
        {
            int written = 0;
            for (int i = 0; i < count; i++)
            {
                for (int j = i + 1; j < count; j++)
                {
                    if (!Aabb.Overlaps(bounds[i], bounds[j])) continue;
                    if (written + 2 > outputLimit) return written;
                    output[written++] = i;
                    output[written++] = j;
                }
            }

            return written;
        }
    }
}
