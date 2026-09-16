using NUnit.Framework;

namespace Ember.Collision.Tests
{
    [TestFixture]
    public unsafe class ContactEventMathTests
    {
        [Test]
        public void Merge_CurrentOnlyPair_EmitsEnter()
        {
            ContactPairRecord* current = stackalloc ContactPairRecord[1];
            current[0] = ContactPairRecord.Create(new Entity(2, 1), new Entity(7, 1));
            ContactEvent* events = stackalloc ContactEvent[1];

            int count = ContactEventMath.Merge(null, 0, current, 1, events, 1);

            Assert.That(count, Is.EqualTo(1));
            Assert.That(events[0].Phase, Is.EqualTo(ContactEventPhase.Enter));
            Assert.That(events[0].A, Is.EqualTo(new Entity(2, 1)));
            Assert.That(events[0].B, Is.EqualTo(new Entity(7, 1)));
        }

        [Test]
        public void Merge_UnchangedPair_EmitsStay()
        {
            ContactPairRecord* previous = stackalloc ContactPairRecord[1];
            ContactPairRecord* current = stackalloc ContactPairRecord[1];
            previous[0] = ContactPairRecord.Create(new Entity(2, 1), new Entity(7, 1));
            current[0] = ContactPairRecord.Create(new Entity(2, 1), new Entity(7, 1));
            ContactEvent* events = stackalloc ContactEvent[2];

            int count = ContactEventMath.Merge(previous, 1, current, 1, events, 2);

            Assert.That(count, Is.EqualTo(1));
            Assert.That(events[0].Phase, Is.EqualTo(ContactEventPhase.Stay));
        }

        [Test]
        public void Merge_PreviousOnlyPair_EmitsExit()
        {
            ContactPairRecord* previous = stackalloc ContactPairRecord[1];
            previous[0] = ContactPairRecord.Create(new Entity(2, 1), new Entity(7, 1));
            ContactEvent* events = stackalloc ContactEvent[1];

            int count = ContactEventMath.Merge(previous, 1, null, 0, events, 1);

            Assert.That(count, Is.EqualTo(1));
            Assert.That(events[0].Phase, Is.EqualTo(ContactEventPhase.Exit));
        }

        [Test]
        public void Merge_ReusedEntitySlot_EmitsExitThenEnter()
        {
            ContactPairRecord* previous = stackalloc ContactPairRecord[1];
            ContactPairRecord* current = stackalloc ContactPairRecord[1];
            previous[0] = ContactPairRecord.Create(new Entity(2, 1), new Entity(7, 1));
            current[0] = ContactPairRecord.Create(new Entity(2, 2), new Entity(7, 1));
            ContactEvent* events = stackalloc ContactEvent[2];

            int count = ContactEventMath.Merge(previous, 1, current, 1, events, 2);

            Assert.That(count, Is.EqualTo(2));
            Assert.That(events[0].Phase, Is.EqualTo(ContactEventPhase.Exit));
            Assert.That(events[1].Phase, Is.EqualTo(ContactEventPhase.Enter));
            Assert.That(events[1].A, Is.EqualTo(new Entity(2, 2)));
        }

        [Test]
        public void Sort_UnorderedPairs_OrdersCanonicalKeys()
        {
            ContactPairRecord* records = stackalloc ContactPairRecord[3];
            ContactPairRecord* scratch = stackalloc ContactPairRecord[3];
            records[0] = ContactPairRecord.Create(new Entity(8, 1), new Entity(9, 1));
            records[1] = ContactPairRecord.Create(new Entity(1, 1), new Entity(4, 1));
            records[2] = ContactPairRecord.Create(new Entity(2, 1), new Entity(3, 1));

            ContactEventMath.Sort(records, scratch, 3);

            Assert.That(records[0].Key, Is.LessThan(records[1].Key));
            Assert.That(records[1].Key, Is.LessThan(records[2].Key));
            Assert.That(records[0].A, Is.EqualTo(new Entity(1, 1)));
        }
    }
}
