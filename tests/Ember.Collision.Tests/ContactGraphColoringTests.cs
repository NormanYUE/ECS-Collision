using NUnit.Framework;

namespace Ember.Collision.Tests
{
    [TestFixture]
    public unsafe class ContactGraphColoringTests
    {
        [Test]
        public void Assign_Chain_ReusesNonConflictingColor()
        {
            CandidatePair* pairs = stackalloc CandidatePair[3];
            pairs[0] = Pair(0, 1);
            pairs[1] = Pair(1, 2);
            pairs[2] = Pair(2, 3);
            int* colors = stackalloc int[3];
            int* order = stackalloc int[3];

            int colorCount = ContactGraphColoring.Assign(pairs, 3, 4, colors, order);

            Assert.That(colorCount, Is.EqualTo(2));
            Assert.That(colors[0], Is.EqualTo(colors[2]));
            Assert.That(colors[0], Is.Not.EqualTo(colors[1]));
        }

        [Test]
        public void Assign_Triangle_AssignsThreeConflictFreeColors()
        {
            CandidatePair* pairs = stackalloc CandidatePair[3];
            pairs[0] = Pair(0, 1);
            pairs[1] = Pair(1, 2);
            pairs[2] = Pair(0, 2);
            int* colors = stackalloc int[3];
            int* order = stackalloc int[3];

            int colorCount = ContactGraphColoring.Assign(pairs, 3, 3, colors, order);

            Assert.That(colorCount, Is.EqualTo(3));
            Assert.That(colors[0], Is.Not.EqualTo(colors[1]));
            Assert.That(colors[1], Is.Not.EqualTo(colors[2]));
            Assert.That(colors[0], Is.Not.EqualTo(colors[2]));
        }

        [Test]
        public void Assign_PermutedInput_UsesCanonicalPairOrder()
        {
            CandidatePair* ordered = stackalloc CandidatePair[3];
            ordered[0] = Pair(0, 1);
            ordered[1] = Pair(1, 2);
            ordered[2] = Pair(2, 3);
            CandidatePair* permuted = stackalloc CandidatePair[3];
            permuted[0] = ordered[2];
            permuted[1] = ordered[0];
            permuted[2] = ordered[1];
            int* firstColors = stackalloc int[3];
            int* secondColors = stackalloc int[3];
            int* firstOrder = stackalloc int[3];
            int* secondOrder = stackalloc int[3];

            ContactGraphColoring.Assign(ordered, 3, 4, firstColors, firstOrder);
            ContactGraphColoring.Assign(permuted, 3, 4, secondColors, secondOrder);

            Assert.That(ColorFor(Pair(0, 1), ordered, firstColors, 3),
                Is.EqualTo(ColorFor(Pair(0, 1), permuted, secondColors, 3)));
            Assert.That(ColorFor(Pair(1, 2), ordered, firstColors, 3),
                Is.EqualTo(ColorFor(Pair(1, 2), permuted, secondColors, 3)));
            Assert.That(ColorFor(Pair(2, 3), ordered, firstColors, 3),
                Is.EqualTo(ColorFor(Pair(2, 3), permuted, secondColors, 3)));
        }

        [Test]
        public void Assign_DuplicateAndInvalidEdges_AreRejected()
        {
            CandidatePair* pairs = stackalloc CandidatePair[5];
            pairs[0] = Pair(0, 1);
            pairs[1] = Pair(0, 1);
            pairs[2] = Pair(2, 2);
            pairs[3] = Pair(-1, 3);
            pairs[4] = Pair(1, 4);
            int* colors = stackalloc int[5];
            int* order = stackalloc int[5];

            int colorCount = ContactGraphColoring.Assign(pairs, 5, 4, colors, order);

            Assert.That(colorCount, Is.EqualTo(1));
            Assert.That(colors[0], Is.EqualTo(0));
            Assert.That(colors[1], Is.EqualTo(-1));
            Assert.That(colors[2], Is.EqualTo(-1));
            Assert.That(colors[3], Is.EqualTo(-1));
            Assert.That(colors[4], Is.EqualTo(-1));
        }

        private static CandidatePair Pair(int a, int b) => new() { BodyA = a, BodyB = b };

        private static int ColorFor(CandidatePair target, CandidatePair* pairs, int* colors, int count)
        {
            ulong key = PairKey.PackIndices(target.BodyA, target.BodyB);
            for (int i = 0; i < count; i++)
                if (PairKey.PackIndices(pairs[i].BodyA, pairs[i].BodyB) == key) return colors[i];
            return -1;
        }
    }
}
