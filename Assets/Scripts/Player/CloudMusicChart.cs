using System;

namespace EndlessRollball
{
    [Serializable]
    public sealed class CloudMusicChart
    {
        public string title, composer;
        public int difficulty;
        public float duration, bpm, forwardSpeed, steeringSpeed, laneSpacing, bounceHeight;
        public float endingTime;
        public float[] times;
        public int[] lanes;
        public CloudPianoChord[] chords;
        public float PlayableEnd => endingTime > 0f ? endingTime : duration;
        public int LastPlayableIndex => NextTarget(PlayableEnd) - 1;

        public void ValidatePiano()
        {
            Validate();
            if (chords == null || chords.Length != times.Length)
                throw new ArgumentException("Piano notes must match the platform chart.");
            for (int i = 0; i < chords.Length; i++)
            {
                CloudPianoNote[] notes = chords[i]?.notes;
                if (notes == null || notes.Length > 4 || (i > 0 && notes.Length == 0))
                    throw new ArgumentException("Each piano platform needs one to four notes.");
                foreach (CloudPianoNote note in notes)
                    if (note == null || note.pitch < 21 || note.pitch > 108 || note.velocity < 1 ||
                        note.velocity > 127 || float.IsNaN(note.duration) || float.IsInfinity(note.duration) ||
                        note.duration <= 0f || note.duration > 4f)
                        throw new ArgumentException("Invalid piano note pitch, velocity or duration.");
            }
        }

        public void Validate()
        {
            if (duration != 45f || forwardSpeed <= 0f || steeringSpeed <= 0f || laneSpacing <= 0f || bounceHeight <= 0f ||
                times == null || lanes == null || times.Length != lanes.Length || times.Length < 3 ||
                times[0] != 0f || lanes[0] != 0 || times[times.Length - 1] < duration + 20f)
                throw new ArgumentException("Invalid or incomplete 45-second cloud music chart.");
            if (PlayableEnd <= 0f || PlayableEnd > duration)
                throw new ArgumentException("Invalid cloud ending time.");
            for (int i = 1; i < times.Length; i++)
            {
                float interval = times[i] - times[i - 1];
                int change = Math.Abs(lanes[i] - lanes[i - 1]);
                if (float.IsNaN(times[i]) || float.IsInfinity(times[i]) || interval < .2f ||
                    lanes[i] < -1 || lanes[i] > 1 || change > 1 ||
                    change * laneSpacing > steeringSpeed * interval * .8f)
                    throw new ArgumentException("Cloud chart contains an unreachable or invalid landing.");
            }
            if (endingTime > 0f && Math.Abs(times[LastPlayableIndex] - endingTime) > .0001f)
                throw new ArgumentException("The ending must be an actual landing platform.");
        }

        public int NextTarget(double time)
        {
            int low = 1, high = times.Length - 1;
            while (low < high)
            {
                int mid = (low + high) / 2;
                if (times[mid] <= time + .000001) low = mid + 1;
                else high = mid;
            }
            return low;
        }
    }

    [Serializable]
    public sealed class CloudPianoChord
    {
        public CloudPianoNote[] notes;
    }

    [Serializable]
    public sealed class CloudPianoNote
    {
        public int pitch, velocity;
        public float duration;
    }
}
