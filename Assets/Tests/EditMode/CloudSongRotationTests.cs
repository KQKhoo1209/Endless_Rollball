using System;
using NUnit.Framework;

namespace EndlessRollball.Tests
{
    public sealed class CloudSongRotationTests
    {
        [Test]
        public void EveryPieceIsPlayedBeforeRepeatingWithoutAdjacentRepeats()
        {
            var rotation = new CloudSongRotation(1234);
            for (int level = 0; level < 3; level++)
            {
                int previous = -1;
                for (int cycle = 0; cycle < 100; cycle++)
                {
                    int mask = 0;
                    for (int i = 0; i < 3; i++)
                    {
                        int song = rotation.Next(level);
                        Assert.AreNotEqual(previous, song);
                        mask |= 1 << song;
                        previous = song;
                    }
                    Assert.AreEqual(7, mask);
                }
            }
        }

        [Test]
        public void SwitchingDifficultyKeepsEachLevelsOwnRotation()
        {
            var rotation = new CloudSongRotation(1234);
            var masks = new int[3];
            for (int song = 0; song < 3; song++)
                for (int level = 0; level < 3; level++) masks[level] |= 1 << rotation.Next(level);
            foreach (int mask in masks) Assert.AreEqual(7, mask);
        }

        [Test]
        public void SeedReproducesSelectionOrder()
        {
            var first = new CloudSongRotation(1234);
            var second = new CloudSongRotation(1234);
            for (int i = 0; i < 100; i++) Assert.AreEqual(first.Next(i % 3), second.Next(i % 3));
        }

        [Test]
        public void UnknownDifficultyIsRejected()
        {
            var rotation = new CloudSongRotation(1234);
            Assert.Throws<ArgumentOutOfRangeException>(() => rotation.Next(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => rotation.Next(3));
        }
    }
}
