namespace Ember.Collision
{
    /// <summary>
    /// 并行 LSD 基数排序核心（工具类，静态豁免，裸指针，Burst 可编译且 <b>CLI 可测</b>）。
    ///
    /// 为什么不用 <c>Unity.Collections</c> 的 <c>SortJob</c>：
    /// ① 键盘/索引需成对置换（携带键可避免散布时随机回读键数组）；
    /// ② 需要<b>确定性</b>——LSD 基数排序天然稳定，同一输入必得同一输出，
    ///   而比较排序的并行分段合并需要额外保证 tie 的稳定性；
    /// ③ 避免为排序单独分配 <c>NativeArray</c> 与安全句柄，全部走 World 托管 buffer。
    ///
    /// 布局约定（全部为「桶主序」，使按桶并行的前缀计算连续访存）：
    /// <code>
    /// hist[d * blockCount + b]    分块直方图
    /// offsets[d * blockCount + b] 该 (桶, 分块) 的局部游标 → 加全局桶基址后即写入位置
    /// totals[d]                   先存桶总数，原地前缀和后即为桶的全局起始
    /// </code>
    /// 每趟四步：直方图(P) → 桶内局部前缀(P 按桶) → 桶全局前缀(S) → 散布(P)。
    /// </summary>
    public static class RadixSort32
    {
        /// <summary>每趟处理的位数。</summary>
        public const int RadixBits = 8;

        /// <summary>桶数量。</summary>
        public const int BucketCount = 1 << RadixBits;

        /// <summary>32 位键的最大有效趟数。超过此值的趟没有可读位。</summary>
        public const int MaxPassCount = 32 / RadixBits;

        /// <summary>计算覆盖指定键位数所需的趟数（不超过 <see cref="MaxPassCount"/>）。</summary>
        public static int PassCountForBits(int keyBits)
        {
            int passes = (keyBits + RadixBits - 1) / RadixBits;
            return passes < 1 ? 1 : (passes > MaxPassCount ? MaxPassCount : passes);
        }

        /// <summary>
        /// 取出指定趟的数字。
        /// <paramref name="shift"/> ≥ 32 时返回 0：C# 对 uint 的位移量按 5 位掩码，
        /// <c>&gt;&gt; 32</c> 实际等价于 <c>&gt;&gt; 0</c>，若不拦截会把已排好的低位再次打乱。
        /// 因此「多余的趟」被定义为<b>稳定的空操作</b>，函数对任意趟数都是全函数。
        /// </summary>
        public static int DigitAt(uint key, int shift)
        {
            if (shift >= 32) return 0;
            return (int)((key >> shift) & (BucketCount - 1));
        }

        /// <summary>分块起始下标。</summary>
        public static int BlockStart(int block, int blockSize) => block * blockSize;

        /// <summary>分块结束下标（不含）。</summary>
        public static int BlockEnd(int block, int blockSize, int count)
        {
            int end = (block + 1) * blockSize;
            return end < count ? end : count;
        }

        /// <summary>指定趟对应的位移量。</summary>
        public static int ShiftForPass(int pass) => pass * RadixBits;

        /// <summary>
        /// 步骤 1：清零本分块的直方图并统计各位数出现次数。
        /// </summary>
        public static unsafe void HistogramBlock(
            uint* keys,
            int block,
            int start,
            int end,
            int shift,
            int* histogram,
            int blockCount)
        {
            int bucketBase = block;
            for (int d = 0; d < BucketCount; d++)
                histogram[d * blockCount + bucketBase] = 0;

            for (int i = start; i < end; i++)
            {
                int digit = DigitAt(keys[i], shift);
                histogram[digit * blockCount + bucketBase]++;
            }
        }

        /// <summary>
        /// 步骤 2：按桶汇总各分块（单桶内可连续访存），写出该桶的局部游标与桶总数。
        /// 由调用方以「按桶并行」的方式逐桶执行。
        /// </summary>
        public static unsafe void BucketLocalPrefix(
            int bucket,
            int* histogram,
            int* offsets,
            int* totals,
            int blockCount)
        {
            int baseIndex = bucket * blockCount;
            int running = 0;
            for (int b = 0; b < blockCount; b++)
            {
                offsets[baseIndex + b] = running;
                running += histogram[baseIndex + b];
            }

            totals[bucket] = running;
        }

        /// <summary>步骤 3：把桶总数原地前缀和为桶的全局起始（串行 256 次操作）。</summary>
        public static unsafe void PrefixTotals(int* totals)
        {
            int running = 0;
            for (int d = 0; d < BucketCount; d++)
            {
                int total = totals[d];
                totals[d] = running;
                running += total;
            }
        }

        /// <summary>
        /// 步骤 4：把本分块的元素按位散布到目标数组，并顺带把全局桶基址叠加到局部游标上。
        /// 同一分块内按输入顺序写入同一桶的递增位置 → <b>稳定</b>。
        /// </summary>
        public static unsafe void ScatterBlock(
            uint* keys,
            int* order,
            uint* outKeys,
            int* outOrder,
            int block,
            int start,
            int end,
            int shift,
            int* offsets,
            int* totals,
            int blockCount)
        {
            // 每个 (桶, 分块) 的偏移条目只被本分块访问，故在此就地叠加全局基址是安全的。
            for (int d = 0; d < BucketCount; d++)
            {
                int index = d * blockCount + block;
                offsets[index] += totals[d];
            }

            for (int i = start; i < end; i++)
            {
                int digit = DigitAt(keys[i], shift);
                int slot = digit * blockCount + block;
                int target = offsets[slot]++;
                outKeys[target] = keys[i];
                outOrder[target] = order[i];
            }
        }

        /// <summary>
        /// 单线程完整排序（仅供测试 / 小规模对拍，热路径走 Job 版本）。
        /// </summary>
        public static unsafe void SortSerial(
            uint* keys,
            int* order,
            uint* scratchKeys,
            int* scratchOrder,
            int count,
            int passCount,
            int blockSize,
            int* histogram,
            int* offsets,
            int* totals,
            int blockCount)
        {
            uint* srcKeys = keys;
            int* srcOrder = order;
            uint* dstKeys = scratchKeys;
            int* dstOrder = scratchOrder;

            for (int pass = 0; pass < passCount; pass++)
            {
                int shift = ShiftForPass(pass);

                for (int b = 0; b < blockCount; b++)
                {
                    HistogramBlock(
                        srcKeys, b, BlockStart(b, blockSize), BlockEnd(b, blockSize, count), shift,
                        histogram, blockCount);
                }

                for (int d = 0; d < BucketCount; d++)
                    BucketLocalPrefix(d, histogram, offsets, totals, blockCount);

                PrefixTotals(totals);

                for (int b = 0; b < blockCount; b++)
                {
                    ScatterBlock(
                        srcKeys, srcOrder, dstKeys, dstOrder, b,
                        BlockStart(b, blockSize), BlockEnd(b, blockSize, count), shift,
                        offsets, totals, blockCount);
                }

                // 指针不能做泛型实参，故用显式临时变量交换而非元组解构。
                uint* swapKeys = srcKeys;
                srcKeys = dstKeys;
                dstKeys = swapKeys;

                int* swapOrder = srcOrder;
                srcOrder = dstOrder;
                dstOrder = swapOrder;
            }

            // 奇数趟后结果落在 scratch，拷回主数组以保证调用方拿到确定的输出位置。
            if (srcKeys != keys)
            {
                for (int i = 0; i < count; i++)
                {
                    keys[i] = srcKeys[i];
                    order[i] = srcOrder[i];
                }
            }
        }
    }
}
