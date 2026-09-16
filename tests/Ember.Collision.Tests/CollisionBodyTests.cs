using NUnit.Framework;

namespace Ember.Collision.Tests
{
    [TestFixture]
    public class CollisionBodyTests
    {
        [Test]
        public void IsActive_RequiresEnabledAndActiveBits()
        {
            var body = new CollisionBody { Flags = CollisionBody.ActiveBit };
            Assert.That(body.IsActive, Is.False);

            body.SetActive(true);
            Assert.That(body.IsEnabled, Is.True);
            Assert.That(body.IsActive, Is.True);

            body.SetActive(false);
            Assert.That(body.IsEnabled, Is.True);
            Assert.That(body.IsActive, Is.False);
        }
    }
}
