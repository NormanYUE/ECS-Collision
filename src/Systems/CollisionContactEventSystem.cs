using Ember;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace Ember.Collision
{
    /// <summary>把 P2 的完整接触 membership 转为 frame-local pair 事件流。</summary>
    public sealed class CollisionContactEventSystem : SystemBase
    {
        protected override void DeclareAccess(AccessBuilder access) => access.Write<CollisionWorld>();

        protected override unsafe void OnTick(SystemContext ctx)
        {
            if (!ctx.World.TryGetCollisionWorld(out CollisionWorldView view)) return;

            int currentCount = view.DetectedContactCount;
            int previousCount = view.PreviousContactPairCount;
            view.EnsureContactEventCapacity(currentCount, previousCount);

            if (currentCount > 0)
            {
                new ContactPairGatherJob
                {
                    Pairs = view.CandidatePairArray,
                    BodyEntities = view.BodyEntityArray,
                    ContactCounts = view.ContactCountArray,
                    ContactOffsets = view.ContactOffsetArray,
                    Output = view.CurrentContactPairArray,
                    BodyCount = view.BodyCount,
                }.Schedule(view.CandidatePairCount, 64).Complete();

                new ContactPairSortJob
                {
                    Records = view.CurrentContactPairArray,
                    Scratch = view.ContactPairScratchArray,
                    Count = currentCount,
                }.Schedule().Complete();
            }

            NativeArray<ContactPairRecord> previous = view.PreviousContactPairArray;
            NativeArray<ContactPairRecord> current = view.CurrentContactPairArray;
            NativeArray<ContactEvent> events = view.ContactEventArray;
            int eventCount = ContactEventMath.Merge(
                (ContactPairRecord*)previous.GetUnsafeReadOnlyPtr(), previousCount,
                (ContactPairRecord*)current.GetUnsafeReadOnlyPtr(), currentCount,
                (ContactEvent*)events.GetUnsafePtr(), events.Length);
            view.CommitContactEvents(currentCount, eventCount);
        }
    }
}
