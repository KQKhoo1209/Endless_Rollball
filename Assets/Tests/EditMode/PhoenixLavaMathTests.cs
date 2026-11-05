using NUnit.Framework;

namespace EndlessRollball.Tests
{
    public sealed class PhoenixLavaMathTests
    {
        [TestCase(40f,43f,3f)]
        [TestCase(43f,43f,0f)]
        [TestCase(44f,43f,0f)]
        public void EarlyArrivalWaitsInsteadOfCancelling(float elapsed,float earliest,float expected)
        {
            Assert.AreEqual(expected,PhoenixLavaMath.SecondsUntilFlight(elapsed,earliest));
        }

        [TestCase(3f)]
        [TestCase(6f)]
        [TestCase(10f)]
        [TestCase(13f)]
        [TestCase(17f)]
        public void EntryFitsWindowAndLeavesGeneratedLookaheadUntouched(float speed)
        {
            Assert.IsTrue(PhoenixLavaMath.TryEntry(144f, 10, 0f, 0f, speed,
                75f, 82.5f, 90f, 8f, out int firstIndex, out float entry));
            float arrival = (entry - 8f) / speed;
            Assert.That(arrival, Is.InRange(75f, 90f));
            Assert.GreaterOrEqual(firstIndex, 13);
            Assert.GreaterOrEqual(entry, 216f);
        }

        [Test]
        public void FrontierTooLateDoesNotCommitAnUnsupportedSection()
        {
            Assert.IsFalse(PhoenixLavaMath.TryEntry(1000, 40, 0, 0, 3,
                75, 82.5f, 90, 8, out _, out _));
        }

        [Test]
        public void ExpiredWindowAndZeroSpeedAreRejected()
        {
            Assert.IsFalse(PhoenixLavaMath.TryEntry(144, 10, 0, 91, 6,
                75, 82.5f, 90, 8, out _, out _));
            Assert.IsFalse(PhoenixLavaMath.TryEntry(144, 10, 0, 0, 0,
                75, 82.5f, 90, 8, out _, out _));
        }

        [TestCase(1f)]
        [TestCase(2f)]
        [TestCase(4.8f)]
        public void HandoffArcHasContinuousEndpointsAndRequestedApex(float height)
        {
            Assert.AreEqual(0, PhoenixLavaMath.JumpLift(0, height));
            Assert.AreEqual(0, PhoenixLavaMath.JumpLift(1, height));
            Assert.AreEqual(height, PhoenixLavaMath.JumpLift(.5f, height), .0001f);
        }

        [Test]
        public void EruptionOnlyReachesFlightHeightDuringItsDangerWindow()
        {
            Assert.AreEqual(0, PhoenixLavaMath.EruptionHeight(11.59f, 12, 6));
            Assert.AreEqual(6, PhoenixLavaMath.EruptionHeight(12, 12, 6));
            Assert.AreEqual(0, PhoenixLavaMath.EruptionHeight(12.86f, 12, 6));
        }
    }
}
