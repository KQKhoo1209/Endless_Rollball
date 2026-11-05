using System;

namespace EndlessRollball
{
    public sealed class CloudSongRotation
    {
        public const int SongsPerDifficulty = 3;
        private readonly Random random;
        private readonly int[,] bags = new int[3, SongsPerDifficulty];
        private readonly int[] positions = { SongsPerDifficulty, SongsPerDifficulty, SongsPerDifficulty };
        private readonly int[] previous = { -1, -1, -1 };

        public CloudSongRotation(int seed) => random = new Random(seed);

        public int Next(int difficulty)
        {
            if (difficulty < 0 || difficulty >= 3) throw new ArgumentOutOfRangeException(nameof(difficulty));
            if (positions[difficulty] == SongsPerDifficulty)
            {
                for (int i = 0; i < SongsPerDifficulty; i++) bags[difficulty, i] = i;
                for (int i = SongsPerDifficulty - 1; i > 0; i--)
                {
                    int other = random.Next(i + 1);
                    int value = bags[difficulty, i];
                    bags[difficulty, i] = bags[difficulty, other];
                    bags[difficulty, other] = value;
                }
                if (bags[difficulty, 0] == previous[difficulty])
                {
                    int other = random.Next(1, SongsPerDifficulty);
                    int value = bags[difficulty, 0];
                    bags[difficulty, 0] = bags[difficulty, other];
                    bags[difficulty, other] = value;
                }
                positions[difficulty] = 0;
            }
            int selected = bags[difficulty, positions[difficulty]++];
            previous[difficulty] = selected;
            return selected;
        }
    }
}
