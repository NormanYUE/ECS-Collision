using NUnit.Framework;

namespace Ember.Collision.Tests
{
    [TestFixture]
    public class NarrowphaseEligibilityTests
    {
        [Test]
        public void IsPairEligible_MatchingEnabledLayers_ReturnsTrue()
        {
            byte active = (byte)(CollisionBody.EnabledBit | CollisionBody.ActiveBit);
            bool eligible = NarrowphaseMath.IsPairEligible(
                new CollisionFilter(1u << 0, 1u << 1), active,
                new CollisionFilter(1u << 1, 1u << 0), active,
                skipStaticPairs: true);

            Assert.That(eligible, Is.True);
        }

        [Test]
        public void IsPairEligible_OneWayLayerMismatch_ReturnsFalse()
        {
            byte active = (byte)(CollisionBody.EnabledBit | CollisionBody.ActiveBit);
            bool eligible = NarrowphaseMath.IsPairEligible(
                new CollisionFilter(1u << 0, 1u << 1), active,
                new CollisionFilter(1u << 1, 0u), active,
                skipStaticPairs: false);

            Assert.That(eligible, Is.False);
        }

        [Test]
        public void IsPairEligible_DisabledOrBothStatic_ReturnsFalseWhenConfigured()
        {
            var all = new CollisionFilter(1u, ~0u);
            byte activeStatic = (byte)(CollisionBody.EnabledBit | CollisionBody.ActiveBit | CollisionBody.StaticBit);

            Assert.That(NarrowphaseMath.IsPairEligible(
                all, 0,
                all, (byte)(CollisionBody.EnabledBit | CollisionBody.ActiveBit),
                skipStaticPairs: false), Is.False);

            Assert.That(NarrowphaseMath.IsPairEligible(
                all, activeStatic,
                all, activeStatic,
                skipStaticPairs: true), Is.False);

            Assert.That(NarrowphaseMath.IsPairEligible(
                all, activeStatic,
                all, activeStatic,
                skipStaticPairs: false), Is.True);
        }

        [Test]
        public void IsPairEligible_MissingActiveBit_ReturnsFalse()
        {
            var all = new CollisionFilter(1u, ~0u);
            bool eligible = NarrowphaseMath.IsPairEligible(
                all, CollisionBody.EnabledBit,
                all, (byte)(CollisionBody.EnabledBit | CollisionBody.ActiveBit),
                skipStaticPairs: false);

            Assert.That(eligible, Is.False);
        }
    }
}
