using NUnit.Framework;

namespace EndlessRollball.Tests
{
    public sealed class CloudMusicEndingTests
    {
        private static CloudMusicChart Chart() => new CloudMusicChart
        {
            duration = 45f, endingTime = 3f, forwardSpeed = 6f, steeringSpeed = 9f,
            laneSpacing = 4.5f, bounceHeight = 1f,
            times = new[] { 0f, 1f, 2f, 3f, 46f, 70f }, lanes = new[] { 0, 0, 0, 0, 0, 0 }
        };

        [Test]
        public void PlanningPaddingIsOutsideTheVisibleCourse()
        {
            CloudMusicChart chart = Chart();
            chart.Validate();
            Assert.AreEqual(3, chart.LastPlayableIndex);
            Assert.AreEqual(3f, chart.times[chart.LastPlayableIndex]);
            Assert.Greater(chart.times[chart.LastPlayableIndex + 1], chart.duration);
        }

        [Test]
        public void BallSettlesOnFinalCloudWhileTheEndingRingsOut()
        {
            var run = new CloudMusicSimulation(Chart(), 1.14f, 1.04f, .45f, 4f);
            run.AdvanceTo(3.5, 0f);
            Assert.AreEqual(3, run.Landings);
            Assert.AreEqual(18f, run.Z);
            Assert.AreEqual(0f, run.Height);
            run.AdvanceTo(45d, 1f);
            Assert.AreEqual(3, run.Landings);
            Assert.AreEqual(18f, run.Z);
            Assert.AreEqual(0f, run.X);
            Assert.IsTrue(run.Complete);
        }

        [Test]
        public void MissingFinalCloudRecoversOntoItWithoutExposingPadding()
        {
            var run = new CloudMusicSimulation(Chart(), 1.14f, 1.04f, .45f, 4f);
            run.AdvanceTo(2d, 0f);
            run.AdvanceTo(3d, 1f);
            Assert.IsTrue(run.Falling);
            run.AdvanceTo(5d, 0f);
            Assert.Greater(run.Recoveries, 0);
            Assert.AreEqual(0f, run.Height);
            Assert.AreEqual(18f, run.Z);
        }
    }
}
