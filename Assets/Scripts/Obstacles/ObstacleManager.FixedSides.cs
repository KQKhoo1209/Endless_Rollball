namespace EndlessRollball.Track
{
    public sealed partial class ObstacleManager
    {
        // Signs describe the blocked roadside, not the player's clear passage.
        // Pattern state advances on accepted encounter rows, never on candidate attempts.
        private static readonly int[,] FixedSidePatterns =
        {
            { -1,  1, -1 }, // Left, right, left.
            { -1, -1,  1 }, // Left, left, right.
            {  1,  1, -1 }, // Right, right, left.
            {  1, -1,  1 }, // Right, left, right.
            { -1,  1,  1 }, // Left, right, right.
            {  1, -1, -1 }  // Right, left, left.
        };

        private System.Random fixedSideRandom;
        private int fixedSidePattern, fixedSideOffset = 3;
        private int lastFixedSide, fixedSideStreak;

        private void ResetFixedSidePattern()
        {
            fixedSideRandom = null;
            fixedSidePattern = 0;
            fixedSideOffset = 3;
            lastFixedSide = fixedSideStreak = 0;
        }

        private void BeginFixedSidePattern(int segmentIndex, int zone)
        {
            ResetFixedSidePattern();
            fixedSideRandom = new System.Random(
                unchecked(runSeed ^ segmentIndex * 104729 ^ zone * 15485863));
        }

        private bool CanFollowFixedSides(int pattern)
        {
            // Pattern boundaries should also require a side change.
            if (lastFixedSide != 0 && FixedSidePatterns[pattern, 0] == lastFixedSide)
                return false;
            int previous = lastFixedSide;
            int streak = fixedSideStreak;
            for (int i = 0; i < 3; i++)
            {
                int next = FixedSidePatterns[pattern, i];
                streak = next == previous ? streak + 1 : 1;
                if (streak > 2) return false;
                previous = next;
            }
            return true;
        }

        private int NextFixedSide
        {
            get
            {
                if (fixedSideOffset >= 3)
                {
                    if (fixedSideRandom == null)
                        fixedSideRandom = new System.Random(runSeed);

                    // Most half-road encounters alternate. Occasional paired-side
                    // patterns retain variety without becoming the dominant rhythm.
                    if (fixedSideRandom.Next(100) < 80)
                    {
                        fixedSidePattern = lastFixedSide < 0 ? 3 : lastFixedSide > 0 ? 0 :
                            (fixedSideRandom.Next(2) == 0 ? 0 : 3);
                    }
                    else
                    {
                        int count = FixedSidePatterns.GetLength(0);
                        int eligible = 0;
                        for (int i = 0; i < count; i++)
                            if (i != 0 && i != 3 && CanFollowFixedSides(i)) eligible++;

                        int selected = fixedSideRandom.Next(eligible);
                        for (int i = 0; i < count; i++)
                        {
                            if (i == 0 || i == 3 || !CanFollowFixedSides(i)) continue;
                            if (selected-- != 0) continue;
                            fixedSidePattern = i;
                            break;
                        }
                    }
                    fixedSideOffset = 0;
                }
                return FixedSidePatterns[fixedSidePattern, fixedSideOffset];
            }
        }

        private void CommitFixedSide(int side)
        {
            // Full-width jumps and moving encounters do not consume a side-pattern slot.
            if (side == 0) return;
            fixedSideStreak = side == lastFixedSide ? fixedSideStreak + 1 : 1;
            lastFixedSide = side;
            fixedSideOffset++;
        }

        private static int BlockingSide(float centreX)
        {
            return centreX < -.01f ? -1 : centreX > .01f ? 1 : 0;
        }
    }
}
