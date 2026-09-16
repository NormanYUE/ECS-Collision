using System;
using NUnit.Framework;

namespace Ember.Collision.Tests
{
    /// <summary>
    /// 分块前缀和对拍测试。这是「并行写入互不重叠区间」的全部依据：
    /// 一旦偏移算错，候选 pair / 接触流形会出现<b>互相覆盖</b>——
    /// 表现为随机的错误碰撞结果，且与规模强相关，极难定位。
    /// </summary>
    [TestFixture]
    public unsafe class BlockScanTests
    {
        private const int BlockSize = 8;
        private const int BlockCountMax = 64;
        private const int MaxCount = 400;

        private static int Scan(int* source, int* destination, int count, int* blockTotals) =>
            BlockScan.ScanSerial(source, destination, count, BlockSize, blockTotals);

        // 指针不能做泛型实参（Action<int*,...> 非法），故自定义委托。
        private delegate void ScanBody(int* source, int* dest, int* totals);

        private static void WithScan(int count, ScanBody body)
        {
            var sourceStore = new int[MaxCount];
            var destStore = new int[MaxCount];
            var totalsStore = new int[BlockCountMax + 1];

            fixed (int* source = sourceStore)
            fixed (int* dest = destStore)
            fixed (int* totals = totalsStore)
            {
                int blocks = BlockScan.BlockCount(count, BlockSize);
                Assert.That(blocks, Is.LessThanOrEqualTo(BlockCountMax), "测试缓冲区不足");
                for (int i = 0; i < count; i++) source[i] = 0;
                for (int i = 0; i < count; i++) dest[i] = 0;
                for (int i = 0; i <= blocks; i++) totals[i] = 0;

                body(source, dest, totals);
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(7)]
        [TestCase(8)]
        [TestCase(9)]
        [TestCase(63)]
        [TestCase(64)]
        [TestCase(65)]
        [TestCase(200)]
        public void Scan_ProducesExclusivePrefixSums(int count)
        {
            WithScan(count, (source, dest, totals) =>
            {
                int running = 0;
                for (int i = 0; i < count; i++)
                {
                    source[i] = (i % 5) + 1;
                    running += source[i];
                }

                int total = Scan(source, dest, count, totals);

                int expected = 0;
                for (int i = 0; i < count; i++)
                {
                    Assert.That(dest[i], Is.EqualTo(expected), $"下标 {i} 处前缀和不正确");
                    expected += source[i];
                }

                Assert.That(total, Is.EqualTo(running), "总数必须是全部元素之和");
                Assert.That(totals[BlockScan.BlockCount(count, BlockSize)], Is.EqualTo(running),
                    "块基址数组末位必须存总数（下游据此读取精确容量需求）");
            });
        }

        [Test]
        public void Scan_ZeroCounts_ProducesZeroOffsets()
        {
            const int count = 50;
            WithScan(count, (source, dest, totals) =>
            {
                // 全零输入：偏移必须全为 0（意味着下游所有区间的长度为 0，不会互相覆盖）。
                Scan(source, dest, count, totals);

                for (int i = 0; i < count; i++)
                    Assert.That(dest[i], Is.EqualTo(0), "全零计数时偏移必须为 0");
            });
        }

        [Test]
        public void Scan_RegionsAreDisjointAndContiguous()
        {
            // 核心不变量：第 i 个元素的区间是 [dest[i], dest[i] + source[i])，
            // 这些区间必须恰好无缝铺满 [0, total) —— 不允许重叠、不允许空洞。
            const int count = 137;
            WithScan(count, (source, dest, totals) =>
            {
                var random = new Random(2024);
                for (int i = 0; i < count; i++)
                    source[i] = random.Next(0, 6);

                int total = Scan(source, dest, count, totals);

                var owner = new int[total];
                for (int i = 0; i < total; i++) owner[i] = -1;

                for (int i = 0; i < count; i++)
                {
                    for (int slot = dest[i]; slot < dest[i] + source[i]; slot++)
                    {
                        Assert.That(slot, Is.InRange(0, Math.Max(0, total - 1)),
                            $"元素 {i} 的区间越出 [0, total)");
                        Assert.That(owner[slot], Is.EqualTo(-1),
                            $"槽位 {slot} 被元素 {i} 与 {owner[slot]} 同时占用——区间重叠");
                        owner[slot] = i;
                    }
                }

                for (int i = 0; i < total; i++)
                    Assert.That(owner[i], Is.Not.EqualTo(-1), $"槽位 {i} 无人写入——区间存在空洞");
            });
        }

        [Test]
        public void Scan_ElementBatches_FillSlotsInOrder()
        {
            // 下游依赖「第 i 个元素按偏移顺序写入」这一性质来保证确定性输出。
            const int count = 32;
            WithScan(count, (source, dest, totals) =>
            {
                for (int i = 0; i < count; i++) source[i] = 2;

                Scan(source, dest, count, totals);

                for (int i = 1; i < count; i++)
                    Assert.That(dest[i], Is.EqualTo(dest[i - 1] + source[i - 1]),
                        "等宽计数的偏移必须严格按元素顺序递增");
            });
        }

        [TestCase(0, 1)]
        [TestCase(1, 1)]
        [TestCase(8, 1)]
        [TestCase(9, 2)]
        [TestCase(16, 2)]
        [TestCase(17, 3)]
        public void BlockCount_MatchesCeilingDivision(int count, int expected)
        {
            Assert.That(BlockScan.BlockCount(count, BlockSize), Is.EqualTo(expected));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(8)]
        [TestCase(9)]
        [TestCase(100)]
        public void BlockStartEnd_CoverExactly(int count)
        {
            int blocks = BlockScan.BlockCount(count, BlockSize);
            int covered = 0;
            for (int b = 0; b < blocks; b++)
            {
                int start = BlockScan.BlockStart(b, BlockSize);
                int end = BlockScan.BlockEnd(b, BlockSize, count);
                Assert.That(start, Is.EqualTo(covered), $"块 {b} 起点与上一块终点不连续");
                Assert.That(end, Is.GreaterThanOrEqualTo(start));
                covered = end;
            }

            Assert.That(covered, Is.EqualTo(count), "块划分必须恰好覆盖全部元素");
        }
    }
}
