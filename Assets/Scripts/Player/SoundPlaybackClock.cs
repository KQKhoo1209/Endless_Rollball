using System;

namespace EndlessRollball
{
    // A single clock for scheduled audio and gameplay, including pauses before start.
    public sealed class SoundPlaybackClock
    {
        public double StartDsp { get; private set; }
        public double Duration { get; private set; }
        public bool Scheduled { get; private set; }
        public bool Paused { get; private set; }
        private double pauseDsp;

        public void Schedule(double now, double duration)
        {
            StartDsp = now + .2;
            Duration = duration;
            Scheduled = true;
            Paused = false;
        }

        public double ElapsedAt(double now) => !Scheduled ? 0d :
            Math.Max(0d, Math.Min(Duration, (Paused ? pauseDsp : now) - StartDsp));

        public bool StartedAt(double now) => Scheduled && (Paused ? pauseDsp : now) >= StartDsp;

        public void Pause(double now)
        {
            if (!Scheduled || Paused) return;
            pauseDsp = now;
            Paused = true;
        }

        public void Resume(double now)
        {
            if (!Paused) return;
            bool started = pauseDsp >= StartDsp;
            StartDsp += now - pauseDsp;
            if (!started) StartDsp = Math.Max(StartDsp, now + .05);
            Paused = false;
        }

        public void Stop()
        {
            Scheduled = Paused = false;
        }
    }
}
