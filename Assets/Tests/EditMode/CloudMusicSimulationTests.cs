using System;
using NUnit.Framework;

namespace EndlessRollball.Tests
{
    public sealed class CloudMusicSimulationTests
    {
        private static CloudMusicChart Chart(float interval, float steering)
        {
            int count = (int)Math.Ceiling(70f / interval) + 1;
            var chart = new CloudMusicChart
            {
                duration = 45f, forwardSpeed = 12f, steeringSpeed = steering,
                laneSpacing = 4.5f, bounceHeight = .75f,
                times = new float[count], lanes = new int[count], chords = new CloudPianoChord[count]
            };
            for (int i = 0; i < count; i++)
            {
                chart.times[i] = i * interval;
                chart.lanes[i] = i % 4 == 1 ? -1 : i % 4 == 3 ? 1 : 0;
                chart.chords[i] = new CloudPianoChord { notes = i == 0 ? new CloudPianoNote[0] :
                    new[] { new CloudPianoNote { pitch = 72, velocity = 70, duration = .2f } } };
            }
            return chart;
        }

        [TestCase(1.53846f, 9f)]
        [TestCase(.731707f, 12f)]
        [TestCase(.3125f, 22f)]
        public void ReachableMusicChartBouncesForFull45Seconds(float interval, float steeringSpeed)
        {
            CloudMusicChart chart = Chart(interval, steeringSpeed);
            var run = new CloudMusicSimulation(chart, 1.14f, 1.04f, .45f, 4f);
            const double frame = 1.0 / 60;
            int frames = 0;
            while (!run.Complete && frames++ < 10000)
            {
                double nextTime = Math.Min(45, run.Time + frame);
                float target = chart.lanes[run.Next] * chart.laneSpacing;
                float steer = (target - run.X) / (float)(steeringSpeed * (nextTime - run.Time));
                run.AdvanceTo(nextTime, steer);
            }
            Assert.AreEqual(45d, run.Time);
            Assert.AreEqual(0, run.Recoveries);
            Assert.Greater(run.Landings, 20);
        }

        [Test]
        public void MissesRecoverWithoutSeekingMusicTime()
        {
            var run = new CloudMusicSimulation(Chart(.3125f, 22f), 1.14f, 1.04f, .45f, 4f);
            run.AdvanceTo(12.345, 0f);
            Assert.AreEqual(12.345, run.Time, .000001);
            Assert.Greater(run.Recoveries, 0);
            run.AdvanceTo(100d, 0f);
            Assert.AreEqual(45d, run.Time);
            Assert.IsTrue(run.Complete);
        }

        [Test]
        public void NewRunStartsAtBeginningWithNoOldOutcomes()
        {
            CloudMusicChart chart = Chart(.3125f, 22f);
            var old = new CloudMusicSimulation(chart, 1.14f, 1.04f, .45f, 4f);
            old.AdvanceTo(20d, 0f);
            var restarted = new CloudMusicSimulation(chart, 1.14f, 1.04f, .45f, 4f);
            Assert.AreEqual(0d, restarted.Time);
            Assert.AreEqual(0, restarted.Landings);
            Assert.AreEqual(0, restarted.Recoveries);
            Assert.IsFalse(restarted.Falling);
        }

        [Test]
        public void ImpossibleFastLaneChangeIsRejected()
        {
            Assert.Throws<ArgumentException>(() => Chart(.3125f, 9f).Validate());
        }

        [Test]
        public void SoundSignalsComeFromLandingsAndMissesOnly()
        {
            CloudMusicChart chart = Chart(.3125f, 22f);
            for (int i = 0; i < chart.lanes.Length; i++) chart.lanes[i] = 0;
            var run = new CloudMusicSimulation(chart, 1.14f, 1.04f, .45f, 4f);
            var piano = new PianoLandingSequence(chart);
            CloudPianoNote[] soundedNotes = null;
            int hitSignals = 0, missSignals = 0;
            run.Landed += index => { hitSignals++; piano.Land(index, run.Time, out soundedNotes); };
            run.Missed += index => { missSignals++; piano.Miss(index); soundedNotes = null; };
            Assert.IsNull(soundedNotes);
            run.AdvanceTo(.3125, 0f);
            Assert.AreEqual(1, hitSignals);
            Assert.AreEqual(72, soundedNotes[0].pitch);
            run.AdvanceTo(.3125, 0f);
            Assert.AreEqual(1, hitSignals);
            run.AdvanceTo(.625, 1f);
            Assert.AreEqual(1, missSignals);
            Assert.AreEqual(1, hitSignals);
            Assert.IsNull(soundedNotes);
            run.AdvanceTo(3d, 0f);
            Assert.Greater(run.Recoveries, 0);
            Assert.AreEqual(run.Landings, hitSignals);
        }
    }
}
