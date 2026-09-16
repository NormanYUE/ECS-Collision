using NUnit.Framework;

namespace Ember.Collision.Tests
{
    [TestFixture]
    public class CollisionStateTests
    {
        [Test]
        public void SetFrameContact_TransitionsEnterStayAndExit()
        {
            var state = default(CollisionState);

            state.SetFrameContact(true);
            Assert.That(state.HasContact, Is.True);
            Assert.That(state.HadContact, Is.False);
            Assert.That(state.EnteredContact, Is.True);

            state.SetFrameContact(true);
            Assert.That(state.HasContact, Is.True);
            Assert.That(state.HadContact, Is.True);
            Assert.That(state.EnteredContact, Is.False);

            state.SetFrameContact(false);
            Assert.That(state.HasContact, Is.False);
            Assert.That(state.HadContact, Is.True);
            Assert.That(state.ExitedContact, Is.True);
        }
    }
}
