namespace EndlessRollball
{
    // MIDI-derived notes are emitted only by confirmed contacts, never by elapsed time.
    public sealed class PianoLandingSequence
    {
        private readonly CloudMusicChart chart;
        private int lastProcessed;

        public PianoLandingSequence(CloudMusicChart chart)
        {
            chart.ValidatePiano();
            this.chart = chart;
        }

        public bool Land(int index, double elapsed, out CloudPianoNote[] notes)
        {
            notes = null;
            if (index <= lastProcessed || index < 1 || index >= chart.times.Length - 1 ||
                elapsed + .0001 < chart.times[index]) return false;
            lastProcessed = index;
            if (elapsed >= chart.duration || elapsed >= chart.times[index + 1]) return false;
            notes = chart.chords[index].notes;
            return true;
        }

        public void Miss(int index)
        {
            if (index > lastProcessed) lastProcessed = index;
        }
    }
}
