namespace Ember.Collision
{
    /// <summary>
    /// 分块前缀和（工具类，静态豁免，裸指针，Burst 可编译且 <b>CLI 可测</b>）。
    ///
    /// 用途：把「每叶候选 pair 数」「每 pair 流形数」这类可变长输出转成
    /// 互不重叠的连续写区间。这是「并行写入无需加锁、无需原子操作、
    /// 输出顺序完全确定」的全部依据。
    ///
    /// 三步：块内独占前缀(P) → 块前缀(S) → 叠加块基址(P)。
    /// </summary>
    public static class BlockScan
    {
        /// <summary>块起始下标。</summary>
        public static int BlockStart(int block, int blockSize) => block * blockSize;

        /// <summary>块结束下标（不含）。</summary>
        public static int BlockEnd(int block, int blockSize, int count)
        {
            int end = (block + 1) * blockSize;
            return end < count ? end : count;
        }

        /// <summary>块数量。</summary>
        public static int BlockCount(int count, int blockSize) =>
            count <= 0 ? 1 : (count + blockSize - 1) / blockSize;

        /// <summary>
        /// 步骤 1：块内独占前缀和写入 <paramref name="destination"/>，
        /// 块总和写入 <paramref name="blockTotals"/>[block]。
        /// </summary>
        public static unsafe void ScanBlock(
            int* source,
            int* destination,
            int block,
            int start,
            int end,
            int* blockTotals)
        {
            int running = 0;
            for (int i = start; i < end; i++)
            {
                int value = source[i];
                destination[i] = running;
                running += value;
            }

            blockTotals[block] = running;
        }

        /// <summary>
        /// 步骤 2：块总和独占前缀和（原地），总和写入
        /// <paramref name="blockTotals"/>[blockCount]。串行成本仅与块数相关。
        /// </summary>
        public static unsafe void PrefixBlockTotals(int* blockTotals, int blockCount)
        {
            int running = 0;
            for (int b = 0; b < blockCount; b++)
            {
                int total = blockTotals[b];
                blockTotals[b] = running;
                running += total;
            }

            blockTotals[blockCount] = running;
        }

        /// <summary>步骤 3：把块的全局基址叠加到块内前缀上。</summary>
        public static unsafe void AddBlockBase(
            int* destination,
            int block,
            int start,
            int end,
            int* blockTotals)
        {
            int baseValue = blockTotals[block];
            if (baseValue == 0) return;
            for (int i = start; i < end; i++)
                destination[i] += baseValue;
        }

        /// <summary>
        /// 单线程完整扫描（仅供测试与小规模对拍）。返回元素总数。
        /// </summary>
        public static unsafe int ScanSerial(
            int* source, int* destination, int count, int blockSize, int* blockTotals)
        {
            int blocks = BlockCount(count, blockSize);
            for (int b = 0; b < blocks; b++)
            {
                ScanBlock(source, destination, b, BlockStart(b, blockSize), BlockEnd(b, blockSize, count),
                    blockTotals);
            }

            PrefixBlockTotals(blockTotals, blocks);

            for (int b = 0; b < blocks; b++)
            {
                AddBlockBase(destination, b, BlockStart(b, blockSize), BlockEnd(b, blockSize, count),
                    blockTotals);
            }

            return blockTotals[blocks];
        }
    }
}
