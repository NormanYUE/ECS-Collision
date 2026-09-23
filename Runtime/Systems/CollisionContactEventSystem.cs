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
                    PairsPtr = view.CandidatePairPtr,
                    BodyEntitiesPtr = view.BodyEntityPtr,
                    ContactCountsPtr = view.ContactCountPtr,
                    ContactOffsetsPtr = view.ContactOffsetPtr,
                    OutputPtr = view.CurrentContactPairPtr,
                    BodyCount = view.BodyCount,
                    OutputCapacity = view.ContactPairCapacity,
                }.Schedule(view.CandidatePairCount, 64).Complete();

                new ContactPairSortJob
                {
                    RecordsPtr = view.CurrentContactPairPtr,
                    ScratchPtr = view.ContactPairScratchPtr,
                    Count = currentCount,
                }.Schedule().Complete();
            }

            var previous = (ContactPairRecord*)view.PreviousContactPairPtr;
            var current = (ContactPairRecord*)view.CurrentContactPairPtr;
            var events = (ContactEvent*)view.ContactEventPtr;
            int eventCount = ContactEventMath.Merge(
                previous, previousCount,
                current, currentCount,
                events, view.ContactEventCapacity);
            view.CommitContactEvents(currentCount, eventCount);
        }
    }
}
