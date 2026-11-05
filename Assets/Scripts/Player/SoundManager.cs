using System;
using UnityEngine;
using EndlessRollball.Track;

namespace EndlessRollball
{
    [DisallowMultipleComponent, RequireComponent(typeof(AudioSource))]
    public sealed class SoundManager : MonoBehaviour
    {
        [Header("Landing Piano")]
        [SerializeField] private AudioSource musicSource;
        [SerializeField, Range(0f, 1f)] private float musicVolume = .65f;
        [SerializeField, Range(.05f, .5f)] private float noteReleaseSeconds = .2f;
        [SerializeField, Range(8, 48)] private int pianoVoices = 24;
        private readonly SoundPlaybackClock clock = new SoundPlaybackClock();
        private readonly AudioClip[] samples = new AudioClip[4];
        private static readonly int[] samplePitches = { 48, 60, 72, 84 };
        private PianoLandingSequence notes;
        private Voice[] voices;
        private bool prepared;
        private double pauseAt;
        private CloudMusicChart physicalChart;
        private int lastPhysicalLanding = -1;

        private sealed class Voice
        {
            public AudioSource Source;
            public double ReleaseStart, ReleaseEnd;
            public float Gain;
            public bool Active;
        }

        public bool IsPaused => clock.Paused;
        public bool HasStarted => clock.StartedAt(AudioSettings.dspTime);
        public double ElapsedSeconds => clock.ElapsedAt(AudioSettings.dspTime);
        public bool IsReady
        {
            get
            {
                if (!prepared) return false;
                foreach (AudioClip clip in samples)
                    if (clip == null || clip.loadState != AudioDataLoadState.Loaded) return false;
                return true;
            }
        }
        public string PreparationError
        {
            get
            {
                if (!prepared) return null;
                foreach (AudioClip clip in samples)
                    if (clip == null || clip.loadState == AudioDataLoadState.Failed)
                        return "A piano sample could not be loaded. Reimport Resources/CloudPiano.";
                return null;
            }
        }

        private void Awake()
        {
            if (musicSource == null) musicSource = GetComponent<AudioSource>();
            voices = new Voice[pianoVoices];
            for (int i = 0; i < voices.Length; i++)
            {
                AudioSource source = musicSource;
                if (i > 0)
                {
                    var child = new GameObject("Piano Voice " + (i + 1), typeof(AudioSource));
                    child.transform.SetParent(transform, false);
                    source = child.GetComponent<AudioSource>();
                    source.outputAudioMixerGroup = musicSource.outputAudioMixerGroup;
                }
                source.playOnAwake = false;
                source.spatialBlend = 0f;
                source.dopplerLevel = 0f;
                source.loop = false;
                source.volume = 0f;
                source.Stop();
                voices[i] = new Voice { Source = source };
            }
        }

        public void PreparePiano()
        {
            StopMusic();
            if (prepared) return;
            double preparationStarted = Time.realtimeSinceStartupAsDouble;
            string[] names = { "C3", "C4", "C5", "C6" };
            for (int i = 0; i < names.Length; i++)
            {
                samples[i] = Resources.Load<AudioClip>("CloudPiano/" + names[i]);
                if (samples[i] == null) throw new InvalidOperationException("Missing piano sample " + names[i]);
                if (samples[i].loadState == AudioDataLoadState.Unloaded && !samples[i].LoadAudioData())
                    throw new InvalidOperationException("Could not preload piano sample " + names[i]);
            }
            prepared = true;
            EndlessRollball.Infrastructure.RuntimeLog.Info($"[Loading] Piano preparation: {Time.realtimeSinceStartupAsDouble-preparationStarted:F3}s.", this);
        }

        public void StartCloudEvent(DifficultyMode difficulty, CloudMusicChart chart)
        {
            if (!IsReady) throw new InvalidOperationException("Prepare the piano samples first.");
            if (chart.difficulty != (int)difficulty) throw new ArgumentException("Piano chart difficulty mismatch.");
            StopMusic();
            notes = new PianoLandingSequence(chart);
            clock.Schedule(AudioSettings.dspTime, chart.duration);
            // Only the clock starts. There is no background song, muted track or autoplay.
        }

        public void PlayLandingNotes(int platformIndex)
        {
            if (!clock.Scheduled || clock.Paused || notes == null) return;
            double now = AudioSettings.dspTime;
            if (!notes.Land(platformIndex, clock.ElapsedAt(now), out CloudPianoNote[] chord)) return;
            float chordGain = 1f / Mathf.Sqrt(chord.Length);
            foreach (CloudPianoNote note in chord) PlayNote(note, chordGain, now);
        }

        private void PlayNote(CloudPianoNote note, float chordGain, double now)
        {
            Voice voice = voices[0];
            foreach (Voice candidate in voices)
            {
                if (!candidate.Active) { voice = candidate; break; }
                if (candidate.ReleaseEnd < voice.ReleaseEnd) voice = candidate;
            }
            int sample = 0;
            for (int i = 1; i < samplePitches.Length; i++)
                if (Math.Abs(note.pitch - samplePitches[i]) < Math.Abs(note.pitch - samplePitches[sample])) sample = i;
            voice.Source.Stop();
            voice.Source.clip = samples[sample];
            voice.Source.pitch = Mathf.Pow(2f, (note.pitch - samplePitches[sample]) / 12f);
            voice.Gain = Mathf.Pow(note.velocity / 127f, 1.4f) * chordGain;
            voice.ReleaseStart = now + note.duration;
            voice.ReleaseEnd = voice.ReleaseStart + noteReleaseSeconds;
            voice.Active = true;
            voice.Source.volume = musicVolume * voice.Gain;
            voice.Source.Play();
        }

        public void SilenceMissedPlatform(int platformIndex)
        {
            if (notes == null) return;
            notes.Miss(platformIndex);
            double now = AudioSettings.dspTime;
            foreach (Voice voice in voices)
            {
                if (!voice.Active) continue;
                // A miss emits no new note; gently release any previous ringing keys.
                voice.Gain = musicVolume > .001f ? voice.Source.volume / musicVolume : 0f;
                voice.ReleaseStart = now;
                voice.ReleaseEnd = Math.Min(voice.ReleaseEnd, now + .06);
            }
        }

        public void StartPhysicalCloudEvent(DifficultyMode difficulty, CloudMusicChart chart)
        {
            // Prepares the existing piano sequence and voice clock.
            // This does not start background music.
            StartCloudEvent(difficulty, chart);

            physicalChart = chart;
            lastPhysicalLanding = -1;
        }

        public void PlayPhysicalLandingNotes(int index)
        {
            if (!IsReady || !clock.Scheduled || clock.Paused ||
                physicalChart == null ||
                index < 0 ||
                index > physicalChart.LastPlayableIndex ||
                index <= lastPhysicalLanding)
                return;

            lastPhysicalLanding = index;

            CloudPianoNote[] chord = physicalChart.chords[index]?.notes;

            // The original starting platform has no notes.
            // Reuse the first chord as its entrance sound.
            if (index == 0 && (chord == null || chord.Length == 0))
                chord = physicalChart.chords[1]?.notes;

            if (chord == null || chord.Length == 0)
                return;

            double now = AudioSettings.dspTime;
            float chordGain = 1f / Mathf.Sqrt(chord.Length);

            foreach (CloudPianoNote note in chord)
                PlayNote(note, chordGain, now);
        }

        public void PauseMusic()
        {
            if (!clock.Scheduled || clock.Paused) return;
            pauseAt = AudioSettings.dspTime;
            foreach (Voice voice in voices) if (voice.Active) voice.Source.Pause();
            clock.Pause(pauseAt);
        }

        public void ResumeMusic()
        {
            if (!clock.Paused) return;
            double now = AudioSettings.dspTime;
            double held = now - pauseAt;
            clock.Resume(now);
            foreach (Voice voice in voices)
            {
                if (!voice.Active) continue;
                voice.ReleaseStart += held;
                voice.ReleaseEnd += held;
                voice.Source.UnPause();
            }
        }

        public void StopMusic()
        {
            if (voices != null)
                foreach (Voice voice in voices)
                {
                    if (voice.Source != null) { voice.Source.Stop(); voice.Source.volume = 0f; }
                    voice.Active = false;
                }
            notes = null;
            clock.Stop();
            physicalChart = null;
            lastPhysicalLanding = -1;
        }

        private void Update()
        {
            if (!clock.Scheduled || clock.Paused) return;
            double now = AudioSettings.dspTime;
            foreach (Voice voice in voices)
            {
                if (!voice.Active) continue;
                if (now >= voice.ReleaseEnd)
                {
                    voice.Source.Stop(); voice.Active = false;
                    continue;
                }
                float release = now <= voice.ReleaseStart ? 1f :
                    (float)((voice.ReleaseEnd - now) / (voice.ReleaseEnd - voice.ReleaseStart));
                voice.Source.volume = musicVolume * voice.Gain * release * release;
            }
        }

        private void OnDisable() => PauseMusic();
        private void OnDestroy() => StopMusic();
    }
}
