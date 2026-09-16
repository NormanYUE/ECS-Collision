using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Ember.Collision
{
    /// <summary>
    /// 把 World 托管 buffer 的裸内存视图包装为 <see cref="NativeArray{T}"/>，
    /// 让 Job 以标准的 <c>NativeArray</c> 语义（含 <c>[ReadOnly]</c>）访问，
    /// 同时保留 Ember 「单例 + BufferHandle，随 World 自动释放」的资源模型。
    ///
    /// 安全句柄使用 <c>GetTempMemoryHandle()</c>——与 Unity 自身对临时视图的处理一致，
    /// 无需创建 / 释放，且仅在 <c>ENABLE_UNITY_COLLECTIONS_CHECKS</c> 下存在。
    /// 纯工具类（静态豁免）。
    ///
    /// 关键不变量：<b>buffer 扩容会搬移全部 range 的地址</b>
    /// （BufferStore 用另分配 + 拷贝实现增长），因此调用方必须在
    /// 「全部扩容完成之后」再取视图，且 Job 执行期间禁止任何扩容。
    /// </summary>
    public static class NativeBufferUtil
    {
        /// <summary>把裸指针 + 长度包装为可写 <see cref="NativeArray{T}"/>。</summary>
        public static unsafe NativeArray<T> AsNativeArray<T>(void* ptr, int length)
            where T : unmanaged
        {
            if (ptr == null || length <= 0)
                return default;

            var array = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<T>(
                ptr, length, Allocator.None);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(
                ref array, AtomicSafetyHandle.GetTempMemoryHandle());
#endif
            return array;
        }

        /// <summary>把 <see cref="BufferSpan{T}"/> 包装为 <see cref="NativeArray{T}"/>。</summary>
        public static unsafe NativeArray<T> AsNativeArray<T>(in BufferSpan<T> span)
            where T : unmanaged
        {
            return AsNativeArray<T>(span.UnsafePtr, span.Length);
        }
    }
}
