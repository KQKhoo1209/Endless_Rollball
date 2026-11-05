using NUnit.Framework;

namespace EndlessRollball.Tests
{
    public sealed class SoundPlaybackClockTests
    {
        [Test]
        public void PlaybackStartsAfterLeadAndEndsAt45Seconds()
        {
            var clock = new SoundPlaybackClock();
            clock.Schedule(100d, 45d);
            Assert.AreEqual(0d, clock.ElapsedAt(100.1));
            Assert.IsFalse(clock.StartedAt(100.1));
            Assert.AreEqual(45d, clock.ElapsedAt(145.2), .000001);
            Assert.AreEqual(45d, clock.ElapsedAt(200d));
        }

        [Test]
        public void PauseAndResumePreserveAudioAndGameplayTime()
        {
            var clock = new SoundPlaybackClock();
            clock.Schedule(100d, 45d);
            clock.Pause(110.2);
            Assert.AreEqual(10d, clock.ElapsedAt(130d), .000001);
            Assert.IsTrue(clock.StartedAt(130d));
            clock.Resume(130.2);
            Assert.AreEqual(10d, clock.ElapsedAt(130.2), .000001);
            Assert.AreEqual(20d, clock.ElapsedAt(140.2), .000001);
            Assert.AreEqual(45d, clock.ElapsedAt(clock.StartDsp + 45), .000001);
        }

        [Test]
        public void PauseBeforeScheduledStartKeepsAStartLeadOnResume()
        {
            var clock = new SoundPlaybackClock();
            clock.Schedule(100d, 45d);
            clock.Pause(100.19);
            Assert.IsFalse(clock.StartedAt(200d));
            clock.Resume(200d);
            Assert.GreaterOrEqual(clock.StartDsp, 200.05);
            Assert.AreEqual(0d, clock.ElapsedAt(200d));
        }

        [Test]
        public void RestartClearsOldPauseAndPlaybackState()
        {
            var clock = new SoundPlaybackClock();
            clock.Schedule(100d, 45d);
            clock.Pause(110d);
            clock.Stop();
            Assert.AreEqual(0d, clock.ElapsedAt(200d));
            Assert.IsFalse(clock.Scheduled);
            clock.Schedule(300d, 45d);
            Assert.IsFalse(clock.Paused);
            Assert.AreEqual(0d, clock.ElapsedAt(300d));
            Assert.AreEqual(300.2d, clock.StartDsp);
        }
    }
}
