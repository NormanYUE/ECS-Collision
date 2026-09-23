using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;

namespace Ember.Collision.Tests
{
    /// <summary>
    /// LBVH 构建与 pair 生成的正确性对拍。
    /// 核心断言：BVH 自查询得到的 pair 集合必须与暴力 O(n²) 结果<b>完全一致</b>
    /// ——既不能漏（漏检 = 穿模），也不能重（重复 pair = 重复施加冲量 = 能量不守恒）。
    /// </summary>
    [TestFixture]
    public unsafe class BvhBuilderTests
    {
        private const int MaxCount = 512;
        private const int MaxNodes = 4 * MaxCount;
        private const int StackDepth = 512;

        private delegate void TreeBody(
            BvhNode* nodes,
            Aabb* bounds,
            int* order,
            int* scratch,
            int* stack,
            int leafCapacity,
            int root);

        /// <summary>构建树并在其上执行一段指针逻辑（固定缓冲区，避免栈压力）。</summary>
        private static void WithTree(int count, TreeBody body)
        {
            var nodeStore = new BvhNode[MaxNodes];
            var boundsStore = new Aabb[MaxCount];
            var orderStore = new int[MaxCount];
            var scratchStore = new int[MaxCount];
            var stackStore = new int[StackDepth];

            fixed (BvhNode* nodes = nodeStore)
            fixed (Aabb* bounds = boundsStore)
            fixed (int* order = orderStore)
            fixed (int* scratch = scratchStore)
            fixed (int* stack = stackStore)
            {
                int leafCapacity = 1;
                while (leafCapacity < Math.Max(1, count)) leafCapacity <<= 1;
                Assert.That(2 * leafCapacity - 1, Is.LessThanOrEqualTo(MaxNodes),
                    $"节点缓冲区不足：count={count} 需要 {2 * leafCapacity - 1} 个节点");

                for (int i = 0; i < count; i++) order[i] = i;

                BvhBuilder.BuildLeaves(nodes, bounds, order, count, leafCapacity);

                int levelStart = 0;
                int levelCount = leafCapacity;
                int parentStart = leafCapacity;
                while (levelCount > 1)
                {
                    int parentCount = levelCount >> 1;
                    BvhBuilder.MergeLevel(nodes, levelStart, levelCount, parentStart);
                    levelStart = parentStart;
                    levelCount = parentCount;
                    parentStart += parentCount;
                }

                body(nodes, bounds, order, scratch, stack, leafCapacity, levelStart);
            }
        }

        /// <summary>
        /// 用 BVH 自查询枚举全部 pair（走生产路径 <see cref="BvhBuilder.CollectPairsInto"/>），
        /// 返回排序后的 (a,b) 列表（a &lt; b，稠密 body 下标）。
        /// </summary>
        private static List<(int A, int B)> CollectBvhPairs(
            BvhNode* nodes, Aabb* bounds, int* order, int* stack, int count, int root, int leafCapacity,
            byte* bodyFlags = null, byte participationBits = 0)
        {
            var pairs = new List<(int, int)>();
            var hitStore = new CandidatePair[leafCapacity];

            fixed (CandidatePair* hits = hitStore)
            {
                for (int i = 0; i < count; i++)
                {
                    int body = order[i];
                    var result = BvhBuilder.CollectPairsInto(
                        nodes, root, i, &bounds[body], stack, StackDepth, order,
                        hits, 0, leafCapacity,
                        bodyFlags, participationBits);

                    Assert.That(result.Overflow, Is.False, "遍历栈溢出或输出容量不足");

                    for (int h = 0; h < result.Count; h++)
                        pairs.Add((hits[h].BodyA, hits[h].BodyB));
                }
            }

            pairs.Sort((x, y) => x.Item1 != y.Item1 ? x.Item1.CompareTo(y.Item1) : x.Item2.CompareTo(y.Item2));
            return pairs;
        }

        private static List<(int A, int B)> CollectBruteForcePairs(Aabb* bounds, int count)
        {
            var pairs = new List<(int, int)>();
            for (int i = 0; i < count; i++)
                for (int j = i + 1; j < count; j++)
                    if (Aabb.Overlaps(bounds[i], bounds[j])) pairs.Add((i, j));
            return pairs;
        }

        private static void AssertPairSetsEqual(List<(int A, int B)> actual, List<(int A, int B)> expected, string context)
        {
            var actualSet = new HashSet<(int, int)>(actual);
            var expectedSet = new HashSet<(int, int)>(expected);

            Assert.That(actual.Count, Is.EqualTo(actualSet.Count), $"{context}: BVH 输出存在重复 pair");
            Assert.That(actual.Count, Is.EqualTo(expected.Count),
                $"{context}: pair 数量不一致（BVH {actual.Count} vs 暴力 {expected.Count}）");

            foreach (var pair in expectedSet)
                Assert.That(actualSet.Contains(pair), Is.True, $"{context}: 漏掉 pair {pair}（会导致穿模）");

            foreach (var pair in actualSet)
            {
                Assert.That(pair.Item1, Is.Not.EqualTo(pair.Item2), $"{context}: 出现自配对 {pair}");
                Assert.That(pair.Item1, Is.LessThan(pair.Item2), $"{context}: pair {pair} 未归一化为 A<B");
                Assert.That(expectedSet.Contains(pair), Is.True, $"{context}: 多出 pair {pair}（会导致重复施加冲量）");
            }
        }

        [TestCase(2)]
        [TestCase(17)]
        [TestCase(64)]
        [TestCase(100)]
        public void PairFilter_ExcludesInactiveBodies(int count)
        {
            // 宽相预筛（SkipInactivePairs）：带未参与端点的 pair 一律不产出。
            // 判定必须与窄相 IsPairEligible 一致 —— 用「两端都参与的暴力集合」对拍。
            const byte participation = 0x06; // Enabled | Active
            var positions = new float3[MaxCount];
            var flagsStore = new byte[MaxCount];
            for (int i = 0; i < count; i++)
            {
                // 确定性密集栅格（间距 0.5，半径 1）：保证重叠，测试不会空跑。
                positions[i] = new float3((i % 10) * 0.5f, (i / 10) * 0.5f, 0f);
                // 每 3 个停用 1 个（下标 2/5/8…）；下标 0/1 始终参与，保证 count=2 也有 pair。
                flagsStore[i] = (byte)(i % 3 == 2 ? 0 : participation);
            }

            WithTree(count, (nodes, bounds, order, scratch, stack, leafCapacity, root) =>
            {
                for (int i = 0; i < count; i++)
                    bounds[i] = Aabb.FromCenterExtents(positions[i], new float3(1f));

                BvhBuilder.BuildLeaves(nodes, bounds, order, count, leafCapacity);
                int levelStart = 0, levelCount = leafCapacity, parentStart = leafCapacity;
                while (levelCount > 1)
                {
                    int parentCount = levelCount >> 1;
                    BvhBuilder.MergeLevel(nodes, levelStart, levelCount, parentStart);
                    levelStart = parentStart;
                    levelCount = parentCount;
                    parentStart += parentCount;
                }

                fixed (byte* flags = flagsStore)
                {
                    var filtered = CollectBvhPairs(
                        nodes, bounds, order, stack, count, levelStart, leafCapacity, flags, participation);
                    var unfiltered = CollectBvhPairs(
                        nodes, bounds, order, stack, count, levelStart, leafCapacity);
                    var bruteAll = CollectBruteForcePairs(bounds, count);

                    var expected = new List<(int, int)>();
                    for (int i = 0; i < count; i++)
                    {
                        if ((flagsStore[i] & participation) != participation) continue;
                        for (int j = i + 1; j < count; j++)
                        {
                            if ((flagsStore[j] & participation) != participation) continue;
                            if (Aabb.Overlaps(bounds[i], bounds[j])) expected.Add((i, j));
                        }
                    }

                    AssertPairSetsEqual(filtered, expected,
                        $"count={count} 参与位过滤 (bvhAll={unfiltered.Count} bvhFiltered={filtered.Count} bruteAll={bruteAll.Count} bruteFiltered={expected.Count})");
                    Assert.That(filtered.Count, Is.GreaterThan(0), "样本应至少产出一个 pair，否则测试无意义");

                    // 计数遍历与收集遍历共用同一套过滤，总数必须一致
                    int total = 0;
                    for (int i = 0; i < count; i++)
                    {
                        var r = BvhBuilder.CountHierarchyOverlaps(
                            nodes, levelStart, i, &bounds[order[i]], stack, StackDepth,
                            order, flags, participation);
                        Assert.That(r.Overflow, Is.False);
                        total += r.Count;
                    }

                    Assert.That(total, Is.EqualTo(filtered.Count), "过滤后的计数遍历与收集遍历必须一致");
                }
            });
        }

        private static void RunRandomScene(int count, int seed, float spacing, float radius)
        {
            var random = new System.Random(seed);
            var positions = new float3[MaxCount];
            for (int i = 0; i < count; i++)
            {
                positions[i] = new float3(
                    (float)(random.NextDouble() * spacing),
                    (float)(random.NextDouble() * spacing),
                    (float)(random.NextDouble() * spacing));
            }

            WithTree(count, (nodes, bounds, order, scratch, stack, leafCapacity, root) =>
            {
                for (int i = 0; i < count; i++)
                    bounds[i] = Aabb.FromCenterExtents(positions[i], new float3(radius));

                BvhBuilder.BuildLeaves(nodes, bounds, order, count, leafCapacity);
                int levelStart = 0, levelCount = leafCapacity, parentStart = leafCapacity;
                while (levelCount > 1)
                {
                    int parentCount = levelCount >> 1;
                    BvhBuilder.MergeLevel(nodes, levelStart, levelCount, parentStart);
                    levelStart = parentStart;
                    levelCount = parentCount;
                    parentStart += parentCount;
                }

                var actual = CollectBvhPairs(nodes, bounds, order, stack, count, levelStart, leafCapacity);
                var expected = CollectBruteForcePairs(bounds, count);
                AssertPairSetsEqual(actual, expected, $"count={count} spacing={spacing} radius={radius}");
            });
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(5)]
        [TestCase(8)]
        [TestCase(17)]
        [TestCase(33)]
        [TestCase(64)]
        [TestCase(65)]
        [TestCase(100)]
        public void Pairs_MatchBruteForce_VariousCounts(int count)
        {
            // radius=1.0、spacing=10：稀疏，绝大多数 pair 不重叠。
            RunRandomScene(count, 4242 + count, 10f, 1f);
        }

        [TestCase(16)]
        [TestCase(48)]
        [TestCase(129)]
        public void Pairs_MatchBruteForce_DenseOverlaps(int count)
        {
            // radius=6.0、spacing=10：密集重叠，跨子树/跨层边界的 pair 会被大量触发。
            RunRandomScene(count, 999 + count, 10f, 6f);
        }

        [Test]
        public void Pairs_AllIdentical_IsCompleteGraph()
        {
            const int count = 40;
            WithTree(count, (nodes, bounds, order, scratch, stack, leafCapacity, root) =>
            {
                for (int i = 0; i < count; i++)
                    bounds[i] = Aabb.FromCenterExtents(float3.zero, new float3(1f));

                BvhBuilder.BuildLeaves(nodes, bounds, order, count, leafCapacity);
                int levelStart = 0, levelCount = leafCapacity, parentStart = leafCapacity;
                while (levelCount > 1)
                {
                    int parentCount = levelCount >> 1;
                    BvhBuilder.MergeLevel(nodes, levelStart, levelCount, parentStart);
                    levelStart = parentStart;
                    levelCount = parentCount;
                    parentStart += parentCount;
                }

                var actual = CollectBvhPairs(nodes, bounds, order, stack, count, levelStart, leafCapacity);
                var expected = CollectBruteForcePairs(bounds, count);
                AssertPairSetsEqual(actual, expected, "完全重合");
                Assert.That(actual.Count, Is.EqualTo(count * (count - 1) / 2),
                    "完全重合的 n 个碰撞体应恰有 n(n-1)/2 个 pair");
            });
        }

        [Test]
        public void Pairs_NoOverlap_IsEmpty()
        {
            const int count = 64;
            WithTree(count, (nodes, bounds, order, scratch, stack, leafCapacity, root) =>
            {
                for (int i = 0; i < count; i++)
                    bounds[i] = Aabb.FromCenterExtents(new float3(i * 10f, 0f, 0f), new float3(1f));

                BvhBuilder.BuildLeaves(nodes, bounds, order, count, leafCapacity);
                int levelStart = 0, levelCount = leafCapacity, parentStart = leafCapacity;
                while (levelCount > 1)
                {
                    int parentCount = levelCount >> 1;
                    BvhBuilder.MergeLevel(nodes, levelStart, levelCount, parentStart);
                    levelStart = parentStart;
                    levelCount = parentCount;
                    parentStart += parentCount;
                }

                var actual = CollectBvhPairs(nodes, bounds, order, stack, count, levelStart, leafCapacity);
                Assert.That(actual, Is.Empty, "完全分离的场景不应产生任何 pair");
            });
        }

        [Test]
        public void Pairs_UnsortedOrderArray_StillComplete()
        {
            // order[] 不是恒等置换时仍须完整（模拟基数排序输出的非平凡置换）。
            const int count = 50;
            WithTree(count, (nodes, bounds, order, scratch, stack, leafCapacity, root) =>
            {
                for (int i = 0; i < count; i++)
                    bounds[i] = Aabb.FromCenterExtents(new float3(i % 7, i % 5, 0f), new float3(1.2f));

                // 逆序置换
                for (int i = 0; i < count; i++) order[i] = count - 1 - i;

                BvhBuilder.BuildLeaves(nodes, bounds, order, count, leafCapacity);
                int levelStart = 0, levelCount = leafCapacity, parentStart = leafCapacity;
                while (levelCount > 1)
                {
                    int parentCount = levelCount >> 1;
                    BvhBuilder.MergeLevel(nodes, levelStart, levelCount, parentStart);
                    levelStart = parentStart;
                    levelCount = parentCount;
                    parentStart += parentCount;
                }

                var actual = CollectBvhPairs(nodes, bounds, order, stack, count, levelStart, leafCapacity);
                var expected = CollectBruteForcePairs(bounds, count);
                AssertPairSetsEqual(actual, expected, "乱序 order[]");
            });
        }

        [Test]
        public void Count_MatchesCollect()
        {
            const int count = 80;
            WithTree(count, (nodes, bounds, order, scratch, stack, leafCapacity, root) =>
            {
                var random = new System.Random(31337);
                for (int i = 0; i < count; i++)
                {
                    var center = new float3(
                        (float)(random.NextDouble() * 8.0), (float)(random.NextDouble() * 8.0), 0f);
                    bounds[i] = Aabb.FromCenterExtents(center, new float3(1.5f));
                }

                BvhBuilder.BuildLeaves(nodes, bounds, order, count, leafCapacity);
                int levelStart = 0, levelCount = leafCapacity, parentStart = leafCapacity;
                while (levelCount > 1)
                {
                    int parentCount = levelCount >> 1;
                    BvhBuilder.MergeLevel(nodes, levelStart, levelCount, parentStart);
                    levelStart = parentStart;
                    levelCount = parentCount;
                    parentStart += parentCount;
                }

                var collected = CollectBvhPairs(nodes, bounds, order, stack, count, levelStart, leafCapacity);
                int totalCounted = 0;
                for (int i = 0; i < count; i++)
                {
                    int body = order[i];
                    var result = BvhBuilder.CountHierarchyOverlaps(
                        nodes, levelStart, i, &bounds[body], stack, StackDepth);
                    Assert.That(result.Overflow, Is.False);
                    totalCounted += result.Count;
                }

                Assert.That(totalCounted, Is.EqualTo(collected.Count),
                    "计数遍历与收集遍历必须给出相同的 pair 总数");
            });
        }

        [Test]
        public void MergeToRoot_ProducesSingleRoot()
        {
            const int count = 37;
            WithTree(count, (nodes, bounds, order, scratch, stack, leafCapacity, root) =>
            {
                for (int i = 0; i < count; i++)
                    bounds[i] = Aabb.FromCenterExtents(new float3(i, 0f, 0f), new float3(1f));

                BvhBuilder.BuildLeaves(nodes, bounds, order, count, leafCapacity);
                int merged = BvhBuilder.MergeToRoot(nodes, 0, leafCapacity, leafCapacity);

                Assert.That(nodes[merged].MaxLeaf, Is.EqualTo(count - 1), "根节点须覆盖全部真实叶");
                Assert.That(nodes[merged].MinLeaf, Is.EqualTo(0));
                Assert.That(nodes[merged].Right, Is.GreaterThanOrEqualTo(0), "根必须是双子内部节点");
            });
        }

        [Test]
        public void Padding_EmptyLeavesCarryNoPairs()
        {
            // count 非 2 的幂时存在补位空叶；它们不得参与任何 pair，
            // 也不得破坏 MinLeaf/MaxLeaf 的上界语义。
            const int count = 9;
            WithTree(count, (nodes, bounds, order, scratch, stack, leafCapacity, root) =>
            {
                Assert.That(leafCapacity, Is.EqualTo(16), "9 个元素应补足到 16 个叶");

                for (int i = 0; i < count; i++)
                    bounds[i] = Aabb.FromCenterExtents(float3.zero, new float3(1f));

                BvhBuilder.BuildLeaves(nodes, bounds, order, count, leafCapacity);

                for (int i = count; i < leafCapacity; i++)
                {
                    Assert.That(nodes[i].MaxLeaf, Is.EqualTo(-1), "补位空叶须以 MaxLeaf=-1 标记");
                    Assert.That(nodes[i].Left, Is.EqualTo(-1));
                }

                int levelStart = 0, levelCount = leafCapacity, parentStart = leafCapacity;
                while (levelCount > 1)
                {
                    int parentCount = levelCount >> 1;
                    BvhBuilder.MergeLevel(nodes, levelStart, levelCount, parentStart);
                    levelStart = parentStart;
                    levelCount = parentCount;
                    parentStart += parentCount;
                }

                Assert.That(nodes[levelStart].MaxLeaf, Is.EqualTo(count - 1),
                    "根节点的 MaxLeaf 必须恰好是最后一个真实叶，补位叶不得抬高上界");

                var actual = CollectBvhPairs(nodes, bounds, order, stack, count, levelStart, leafCapacity);
                var expected = CollectBruteForcePairs(bounds, count);
                AssertPairSetsEqual(actual, expected, "含补位叶");
            });
        }
    }
}
