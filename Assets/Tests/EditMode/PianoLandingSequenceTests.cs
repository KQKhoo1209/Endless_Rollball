using System;
using NUnit.Framework;

namespace EndlessRollball.Tests
{
    public sealed class PianoLandingSequenceTests
    {
        private static CloudMusicChart Chart()
        {
            var chart = new CloudMusicChart
            {
                duration = 45, forwardSpeed = 6, steeringSpeed = 9, laneSpacing = 4.5f, bounceHeight = 2,
                times = new[] { 0f, 1f, 2f, 3f, 70f }, lanes = new[] { 0, 0, 0, 0, 0 },
                chords = new CloudPianoChord[5]
            };
            for (int i = 0; i < 5; i++) chart.chords[i] = new CloudPianoChord { notes = i == 0 ?
                new CloudPianoNote[0] : new[] { new CloudPianoNote { pitch = 60 + i, velocity = 70, duration = .8f } } };
            return chart;
        }

        [Test]
        public void LandingReturnsTheAssignedMidiNotes()
        {
            CloudMusicChart chart = Chart();
            var piano = new PianoLandingSequence(chart);
            Assert.IsTrue(piano.Land(1, 1d, out CloudPianoNote[] notes));
            Assert.AreSame(chart.chords[1].notes, notes);
            Assert.AreEqual(61, notes[0].pitch);
            Assert.AreEqual(70, notes[0].velocity);
        }

        [Test]
        public void SpawnEarlyAndDuplicateContactsDoNotPlayNotes()
        {
            var piano = new PianoLandingSequence(Chart());
            Assert.IsFalse(piano.Land(0, 0d, out _));
            Assert.IsFalse(piano.Land(1, .5d, out _));
            Assert.IsTrue(piano.Land(1, 1d, out _));
            Assert.IsFalse(piano.Land(1, 1d, out _));
        }

        [Test]
        public void MissedAndExpiredNotesAreSkippedAndRecoveryCanContinue()
        {
            var piano = new PianoLandingSequence(Chart());
            piano.Miss(1);
            Assert.IsFalse(piano.Land(1, 1d, out _));
            Assert.IsFalse(piano.Land(2, 3d, out _));
            Assert.IsTrue(piano.Land(3, 3d, out CloudPianoNote[] notes));
            Assert.AreEqual(63, notes[0].pitch);
        }

        [Test]
        public void RestartAllowsFirstNoteAgainAndEventEndIsSilent()
        {
            var piano = new PianoLandingSequence(Chart());
            piano.Land(1, 1d, out _);
            Assert.IsFalse(piano.Land(3, 45d, out _));
            var restarted = new PianoLandingSequence(Chart());
            Assert.IsTrue(restarted.Land(1, 1d, out _));
        }

        [Test]
        public void InvalidPitchAndMissingNoteGroupsAreRejected()
        {
            CloudMusicChart chart = Chart();
            chart.chords[1].notes[0].pitch = 200;
            Assert.Throws<ArgumentException>(() => chart.ValidatePiano());
            chart = Chart();
            chart.chords = null;
            Assert.Throws<ArgumentException>(() => chart.ValidatePiano());
        }
    }
}
