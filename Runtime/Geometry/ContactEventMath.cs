namespace Ember.Collision
{
    /// <summary>排序后 contact pair 流的无分配归并。</summary>
    public static unsafe class ContactEventMath
    {
        /// <summary>稳定排序 pair 记录。八趟 8-bit LSD 后结果回到 <paramref name="records"/>。</summary>
        public static void Sort(ContactPairRecord* records, ContactPairRecord* scratch, int count)
        {
            if (count <= 1) return;

            for (int pass = 0; pass < 8; pass++)
            {
                int* buckets = stackalloc int[256];
                for (int bucket = 0; bucket < 256; bucket++) buckets[bucket] = 0;

                ContactPairRecord* source = (pass & 1) == 0 ? records : scratch;
                ContactPairRecord* destination = (pass & 1) == 0 ? scratch : records;
                int shift = pass * 8;
                for (int i = 0; i < count; i++)
                    buckets[(int)((source[i].Key >> shift) & 0xffUL)]++;

                int offset = 0;
                for (int bucket = 0; bucket < 256; bucket++)
                {
                    int bucketCount = buckets[bucket];
                    buckets[bucket] = offset;
                    offset += bucketCount;
                }

                for (int i = 0; i < count; i++)
                {
                    ContactPairRecord record = source[i];
                    int bucket = (int)((record.Key >> shift) & 0xffUL);
                    destination[buckets[bucket]++] = record;
                }
            }
        }

        /// <summary>
        /// 将按 <see cref="ContactPairRecord.Key"/> 升序的前后帧 pair 归并为事件。
        /// 同槽位但版本变化视为旧 pair 退出、新 pair 进入。
        /// </summary>
        public static int Merge(
            ContactPairRecord* previous,
            int previousCount,
            ContactPairRecord* current,
            int currentCount,
            ContactEvent* output,
            int outputLimit)
        {
            int previousIndex = 0;
            int currentIndex = 0;
            int written = 0;

            while (previousIndex < previousCount || currentIndex < currentCount)
            {
                if (written >= outputLimit) return written;

                if (previousIndex >= previousCount)
                {
                    Write(ref output[written++], current[currentIndex++], ContactEventPhase.Enter);
                    continue;
                }

                if (currentIndex >= currentCount)
                {
                    Write(ref output[written++], previous[previousIndex++], ContactEventPhase.Exit);
                    continue;
                }

                ContactPairRecord oldPair = previous[previousIndex];
                ContactPairRecord newPair = current[currentIndex];
                if (oldPair.Key < newPair.Key)
                {
                    Write(ref output[written++], oldPair, ContactEventPhase.Exit);
                    previousIndex++;
                    continue;
                }

                if (newPair.Key < oldPair.Key)
                {
                    Write(ref output[written++], newPair, ContactEventPhase.Enter);
                    currentIndex++;
                    continue;
                }

                previousIndex++;
                currentIndex++;
                if (SamePair(oldPair, newPair))
                {
                    Write(ref output[written++], newPair, ContactEventPhase.Stay);
                    continue;
                }

                if (written >= outputLimit) return written;
                Write(ref output[written++], oldPair, ContactEventPhase.Exit);
                if (written >= outputLimit) return written;
                Write(ref output[written++], newPair, ContactEventPhase.Enter);
            }

            return written;
        }

        private static bool SamePair(in ContactPairRecord a, in ContactPairRecord b) =>
            PairKey.SameSlot(a.A, b.A) && PairKey.SameSlot(a.B, b.B);

        private static void Write(ref ContactEvent output, in ContactPairRecord pair, ContactEventPhase phase)
        {
            output = new ContactEvent
            {
                A = pair.A,
                B = pair.B,
                Phase = phase,
            };
        }
    }
}
