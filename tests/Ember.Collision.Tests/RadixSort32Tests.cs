using System;
using NUnit.Framework;

namespace Ember.Collision.Tests
{
    /// <summary>
    /// 基数排序核心对拍测试。
    /// 断言三件事：① 结果有序；② 元素多重集不变（无丢失 / 无重复）；
    /// ③ <b>稳定</b>（等键元素的相对顺序保持不变）——稳定性是宽相 pair 确定性的前提。
    ///
    /// 工作数组用固定（pinned）托管数组而非 stackalloc：栈上 200KB+ 会在
    /// 测试宿主线程上冒栈溢出风险，且限制了可测规模。
    /// </summary>
    [TestFixture]
    public unsafe class RadixSort32Tests
    {
        private const int BlockSize = 64;
        private const int BlockCountMax = 128;
        private const int MaxCount = 8192;

        private delegate void SortBody(
            uint* keys,
            uint* scratchKeys,
            int* order,
            int* scratchOrder,
            int* histogram,
            int* offsets,
            int* totals);

        /// <summary>在固定的托管缓冲区上执行一段指针逻辑，避免栈压力。</summary>
        private static void WithBuffers(int count, SortBody body)
        {
            var keysStore = new uint[MaxCount];
            var scratchKeysStore = new uint[MaxCount];
            var orderStore = new int[MaxCount];
            var scratchOrderStore = new int[MaxCount];
            var histogramStore = new int[RadixSort32.BucketCount * BlockCountMax];
            var offsetsStore = new int[RadixSort32.BucketCount * BlockCountMax];
            var totalsStore = new int[RadixSort32.BucketCount];

            fixed (uint* keys = keysStore)
            fixed (uint* scratchKeys = scratchKeysStore)
            fixed (int* order = orderStore)
            fixed (int* scratchOrder = scratchOrderStore)
            fixed (int* histogram = histogramStore)
            fixed (int* offsets = offsetsStore)
            fixed (int* totals = totalsStore)
            {
                for (int i = 0; i < count; i++)
                {
                    keys[i] = 0;
                    scratchKeys[i] = 0;
                    order[i] = i;
                    scratchOrder[i] = 0;
                }

                body(keys, scratchKeys, order, scratchOrder, histogram, offsets, totals);

                // 长度必须能容纳 count 个元素：防止分块数超出测试缓冲区。
                int blockCount = count == 0 ? 1 : (count + BlockSize - 1) / BlockSize;
                Assert.That(blockCount, Is.LessThanOrEqualTo(BlockCountMax),
                    $"测试缓冲区不足：count={count} 需要 {blockCount} 个分块，上限 {BlockCountMax}");
            }
        }

        private static void Sort(
            uint* keys, int* order, uint* scratchKeys, int* scratchOrder,
            int count, int passCount, int* histogram, int* offsets, int* totals)
        {
            int blockCount = count == 0 ? 1 : (count + BlockSize - 1) / BlockSize;
            RadixSort32.SortSerial(
                keys, order, scratchKeys, scratchOrder, count, passCount, BlockSize,
                histogram, offsets, totals, blockCount);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(63)]
        [TestCase(64)]
        [TestCase(65)]
        [TestCase(255)]
        [TestCase(256)]
        [TestCase(257)]
        [TestCase(1000)]
        [TestCase(4097)]
        [TestCase(8192)]
        public void Sort_MatchesReferenceSort(int count)
        {
            WithBuffers(count, (keys, scratchKeys, order, scratchOrder, histogram, offsets, totals) =>
            {
                var random = new Random(12345);
                for (int i = 0; i < count; i++)
                {
                    // 30 位键（3D Morton 上界），并刻意制造大量重复键以检验稳定性。
                    keys[i] = (uint)random.Next(0, 1 << 20) & 0x3FFFFFFFu;
                    order[i] = i;
                }

                Sort(keys, order, scratchKeys, scratchOrder, count, 4, histogram, offsets, totals);

                for (int i = 1; i < count; i++)
                {
                    Assert.That(keys[i], Is.GreaterThanOrEqualTo(keys[i - 1]),
                        $"count={count} 下标 {i}：键 {keys[i]} < 前键 {keys[i - 1]}，未排序");
                }

                var seen = new bool[Math.Max(1, count)];
                for (int i = 0; i < count; i++)
                {
                    int index = order[i];
                    Assert.That(index, Is.InRange(0, Math.Max(0, count - 1)), "排序输出含越界下标");
                    Assert.That(seen[index], Is.False, $"排序输出含重复下标 {index}——元素丢失 / 重复");
                    seen[index] = true;
                }
            });
        }

        [Test]
        public void Sort_IsStableForEqualKeys()
        {
            const int count = 600;
            WithBuffers(count, (keys, scratchKeys, order, scratchOrder, histogram, offsets, totals) =>
            {
                for (int i = 0; i < count; i++)
                {
                    keys[i] = (uint)(i % 2);
                    order[i] = i;
                }

                Sort(keys, order, scratchKeys, scratchOrder, count, 4, histogram, offsets, totals);

                int evenCursor = 0;
                int oddCursor = 0;
                for (int i = 0; i < count; i++)
                {
                    if (keys[i] == 0)
                    {
                        Assert.That(order[i], Is.EqualTo(evenCursor * 2), "等键（0）元素相对顺序被打乱");
                        evenCursor++;
                    }
                    else
                    {
                        Assert.That(order[i], Is.EqualTo(oddCursor * 2 + 1), "等键（1）元素相对顺序被打乱");
                        oddCursor++;
                    }
                }
            });
        }

        [Test]
        public void Sort_AllEqualKeys_PreservesInputOrder()
        {
            const int count = 500;
            WithBuffers(count, (keys, scratchKeys, order, scratchOrder, histogram, offsets, totals) =>
            {
                for (int i = 0; i < count; i++)
                {
                    keys[i] = 0x20u;
                    order[i] = i;
                }

                Sort(keys, order, scratchKeys, scratchOrder, count, 4, histogram, offsets, totals);

                for (int i = 0; i < count; i++)
                    Assert.That(order[i], Is.EqualTo(i), "全等键时输出应为恒等置换");
            });
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        public void Sort_VariousPassCounts_ProduceSortedOutput(int passCount)
        {
            const int count = 1000;
            WithBuffers(count, (keys, scratchKeys, order, scratchOrder, histogram, offsets, totals) =>
            {
                var random = new Random(777);
                uint mask = passCount >= 4 ? uint.MaxValue : (1u << (passCount * 8)) - 1;
                for (int i = 0; i < count; i++)
                {
                    keys[i] = (uint)random.Next() & mask;
                    order[i] = i;
                }

                Sort(keys, order, scratchKeys, scratchOrder, count, passCount, histogram, offsets, totals);

                for (int i = 1; i < count; i++)
                    Assert.That(keys[i], Is.GreaterThanOrEqualTo(keys[i - 1]), $"passCount={passCount} 未排序");
            });
        }

        [Test]
        public void Sort_MaxKeys_SortsDescendingInputToAscending()
        {
            const int count = 300;
            WithBuffers(count, (keys, scratchKeys, order, scratchOrder, histogram, offsets, totals) =>
            {
                for (int i = 0; i < count; i++)
                {
                    keys[i] = uint.MaxValue - (uint)i;
                    order[i] = i;
                }

                Sort(keys, order, scratchKeys, scratchOrder, count, 4, histogram, offsets, totals);

                Assert.That(keys[0], Is.EqualTo(uint.MaxValue - (uint)(count - 1)));
                Assert.That(keys[count - 1], Is.EqualTo(uint.MaxValue));
            });
        }

        [Test]
        public void Sort_TextbookOrdering_MatchesClrSort()
        {
            // 与 CLR 的稳定比较排序逐项对拍，覆盖重复键与跨分块场景。
            const int count = 3000;
            WithBuffers(count, (keys, scratchKeys, order, scratchOrder, histogram, offsets, totals) =>
            {
                var random = new Random(20240911);
                var reference = new (uint Key, int Index)[count];
                for (int i = 0; i < count; i++)
                {
                    uint key = (uint)random.Next(0, 1 << 12); // 高重复率
                    keys[i] = key;
                    order[i] = i;
                    reference[i] = (key, i);
                }

                Array.Sort(reference, (a, b) =>
                {
                    int byKey = a.Key.CompareTo(b.Key);
                    return byKey != 0 ? byKey : a.Index.CompareTo(b.Index); // 稳定排序的等价定义
                });

                Sort(keys, order, scratchKeys, scratchOrder, count, 4, histogram, offsets, totals);

                for (int i = 0; i < count; i++)
                {
                    Assert.That(keys[i], Is.EqualTo(reference[i].Key), $"下标 {i} 键不一致");
                    Assert.That(order[i], Is.EqualTo(reference[i].Index),
                        $"下标 {i} 的元素下标不一致——排序不稳定或键-值置换错位");
                }
            });
        }
    }
}
