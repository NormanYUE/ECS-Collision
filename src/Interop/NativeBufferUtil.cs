using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Ember.Collision
{
    /// <summary>
    /// World 托管 buffer 与 Job 之间的互操作助手。
    ///
    /// <b>为什么传裸指针而不是 <see cref="NativeArray{T}"/>：</b>
    /// <see cref="NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray{T}"/> 造出的数组
    /// 其 <c>AtomicSafetyHandle</c> 是 <c>default</c>，Job 调度期的容器校验会直接拒绝
    /// （报 <c>has not been assigned or constructed. All containers must be valid when
    /// scheduling a job.</c>）。补句柄的两条路都不通：
    /// <list type="bullet">
    /// <item><c>GetTempMemoryHandle()</c> 返回的是<b>当前临时内存作用域</b>的句柄
    /// （见其配套 API <c>IsTempMemoryHandle</c> 的语义），生命周期由 native 侧的作用域决定，
    /// 不是「本帧有效」的保证；</item>
    /// <item><c>AtomicSafetyHandle.Create()</c> 必须配对 <c>Release()</c>，而本模块的资源模型是
    /// 「buffer 随 <c>World.Dispose</c> 自动释放、无需 Dispose」（见 <see cref="CollisionWorld"/> 注释），
    /// 引入句柄就得再加一套生命周期钩子。</item>
    /// </list>
    /// 因此统一按 <c>Ember.Framework</c> chunk job 的既有做法：把裸指针放进
    /// <c>[NativeDisableUnsafePtrRestriction] public long</c> 字段，Job 内部自行转型。
    /// 同一 <see cref="CollisionWorldView"/> 里的 <c>VertexPointer</c> 原本就是这个形态。
    ///
    /// 纯工具类（静态豁免）。
    ///
    /// 关键不变量：<b>buffer 扩容会搬移全部 range 的地址</b>
    /// （BufferStore 用另分配 + 拷贝实现增长），因此调用方必须在
    /// 「全部扩容完成之后」再取视图，且 Job 执行期间禁止任何扩容。
    /// </summary>
    public static class NativeBufferUtil
    {
        /// <summary>把 <see cref="BufferSpan{T}"/> 取为 Job 可用的裸指针；空 span 返回 0。</summary>
        public static unsafe long AsPointer<T>(in BufferSpan<T> span)
            where T : unmanaged
        {
            return span.Length > 0 ? (long)span.UnsafePtr : 0L;
        }

        /// <summary>
        /// <b>不要使用。</b>保留仅为避免破坏已编译的消费方。
        ///
        /// 返回的数组没有有效的 <c>AtomicSafetyHandle</c>：作为 Job 的容器字段会在调度时被拒绝，
        /// 在主线程索引则可能解引用空句柄节点。改用 <see cref="AsPointer{T}"/>。
        /// </summary>
        [Obsolete("AsNativeArray 返回的数组没有有效安全句柄，作为 Job 字段会在调度时被拒绝。改用 NativeBufferUtil.AsPointer。", false)]
        public static unsafe NativeArray<T> AsNativeArray<T>(void* ptr, int length)
            where T : unmanaged
        {
            if (ptr == null || length <= 0)
                return default;

            return NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<T>(
                ptr, length, Allocator.None);
        }

        /// <summary><b>不要使用。</b>见 <see cref="AsNativeArray{T}(void*, int)"/>。</summary>
        [Obsolete("AsNativeArray 返回的数组没有有效安全句柄，作为 Job 字段会在调度时被拒绝。改用 NativeBufferUtil.AsPointer。", false)]
        public static unsafe NativeArray<T> AsNativeArray<T>(in BufferSpan<T> span)
            where T : unmanaged
        {
            return AsNativeArray<T>(span.UnsafePtr, span.Length);
        }
    }
}
