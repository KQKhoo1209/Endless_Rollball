using NUnit.Framework;

namespace EndlessRollball.Tests
{
    public sealed class CloudLandingMathTests
    {
        [TestCase(.01f)]
        [TestCase(.02f)]
        [TestCase(.0333333f)]
        public void RepeatedLandingsDoNotFallThroughAtStepBoundary(float step)
        {
            float y = .7f, velocity = 10f;
            int landings = 0;
            for (int frame = 0; frame < (int)(45f / step); frame++)
            {
                if (CloudLandingMath.TryGetLandingTime(y - .2f - .5f, velocity,
                    20f, step, out float crossing))
                {
                    landings++;
                    y = .7f;
                    velocity = 10f;
                    float remainder = step - crossing;
                    y += velocity * remainder - 10f * remainder * remainder;
                    velocity -= 20f * remainder;
                }
                else
                {
                    y += velocity * step - 10f * step * step;
                    velocity -= 20f * step;
                }
                Assert.GreaterOrEqual(y, .699f, "A supported landing must not fall below the platform.");
            }
            Assert.GreaterOrEqual(landings, 44);
        }

        [Test]
        public void DescendingContactAtStartOfStepBounces()
        {
            Assert.IsTrue(CloudLandingMath.TryGetLandingTime(0f, -10f, 20f, .02f, out float crossing));
            Assert.AreEqual(0f, crossing);
        }

        [Test]
        public void AscendingBallDoesNotBounceAgainImmediately()
        {
            Assert.IsFalse(CloudLandingMath.TryGetLandingTime(0f, 10f, 20f, .02f, out _));
        }

        [Test]
        public void BallWellBelowPlatformIsNotCaught()
        {
            Assert.IsFalse(CloudLandingMath.TryGetLandingTime(-.1f, -10f, 20f, .02f, out _));
        }

        [Test]
        public void RoundedPlatformRejectsInvisibleSquareCorner()
        {
            // 2.6 x 2.4 platform, with .16 bevel and .45 top corner radius.
            Assert.IsTrue(CloudLandingMath.IsInsideRoundedPlatform(0f, 0f, 1.14f, 1.04f, .45f));
            Assert.IsTrue(CloudLandingMath.IsInsideRoundedPlatform(1.13f, 0f, 1.14f, 1.04f, .45f));
            Assert.IsFalse(CloudLandingMath.IsInsideRoundedPlatform(1.13f, 1.03f, 1.14f, 1.04f, .45f));
            Assert.IsFalse(CloudLandingMath.IsInsideRoundedPlatform(1.2f, 0f, 1.14f, 1.04f, .45f));
        }
    }
}
