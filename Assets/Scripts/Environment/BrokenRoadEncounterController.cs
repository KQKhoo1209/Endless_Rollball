using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace EndlessRollball.EnvironmentVisuals
{
    public enum BrokenRoadEncounterState
    {
        Armed,
        Exploding,
        GapRevealed,
        Burning
    }

    [DisallowMultipleComponent]
    public sealed class BrokenRoadEncounterController : MonoBehaviour
    {
        public const float GapStartLocalZ = 10.5f;
        public const float MaximumExpectedForwardSpeed = 13f;
        public const float MinimumWarningSeconds = 2f;

        [Header("Trigger")]
        [SerializeField] private BoxCollider approachTrigger;

        [Header("Visual road cover")]
        [SerializeField] private Transform[] coverSlabs = System.Array.Empty<Transform>();
        [SerializeField] private Vector3[] revealOffsets = System.Array.Empty<Vector3>();
        [SerializeField] private Vector3[] revealEulerAngles = System.Array.Empty<Vector3>();
        [SerializeField, Min(0.05f)] private float revealDuration = 0.6f;

        [Header("Particle families")]
        [SerializeField] private ParticleSystem[] warningParticles = System.Array.Empty<ParticleSystem>();
        [SerializeField] private ParticleSystem[] burstParticles = System.Array.Empty<ParticleSystem>();
        [SerializeField] private ParticleSystem[] burningParticles = System.Array.Empty<ParticleSystem>();

        private Vector3[] initialLocalPositions = System.Array.Empty<Vector3>();
        private Quaternion[] initialLocalRotations = System.Array.Empty<Quaternion>();
        private Coroutine revealRoutine;
        private bool initialized;

        public BrokenRoadEncounterState State { get; private set; } = BrokenRoadEncounterState.Armed;
        public bool HasTriggered => State != BrokenRoadEncounterState.Armed;
        public BoxCollider ApproachTrigger => approachTrigger;
        public IReadOnlyList<Transform> CoverSlabs => coverSlabs;
        public IReadOnlyList<ParticleSystem> WarningParticles => warningParticles;
        public IReadOnlyList<ParticleSystem> BurstParticles => burstParticles;
        public IReadOnlyList<ParticleSystem> BurningParticles => burningParticles;
        public float RevealDuration => revealDuration;

        private void Awake()
        {
            EnsureInitialized();
        }

        private void OnEnable()
        {
            EnsureInitialized();
            ResetEncounter(Application.isPlaying);
        }

        private void OnDisable()
        {
            StopRevealRoutine();
            StopAndClear(AllParticles());
            RestoreCoverSlabs();
            State = BrokenRoadEncounterState.Armed;
        }

        private void OnTriggerEnter(Collider other)
        {
            TryTrigger(other);
        }

        public bool TryTrigger(Collider other)
        {
            if (!isActiveAndEnabled || HasTriggered || other == null ||
                other.GetComponentInParent<BallMovement>() == null)
            {
                return false;
            }

            TriggerEncounter();
            return true;
        }

        public void TriggerEncounter()
        {
            if (!isActiveAndEnabled || HasTriggered)
            {
                return;
            }

            StopRevealRoutine();
            State = BrokenRoadEncounterState.Exploding;
            StopAndClear(warningParticles);
            Play(burstParticles);

            if (Application.isPlaying)
            {
                revealRoutine = StartCoroutine(RevealCoverRoutine());
            }
            else
            {
                ApplyCoverProgress(1f);
                HideCoverSlabs();
                State = BrokenRoadEncounterState.Burning;
            }
        }

        public void ResetEncounter(bool playWarningParticles = true)
        {
            EnsureInitialized();
            StopRevealRoutine();
            StopAndClear(AllParticles());
            RestoreCoverSlabs();
            State = BrokenRoadEncounterState.Armed;

            if (playWarningParticles && isActiveAndEnabled)
            {
                Play(warningParticles);
            }
        }

        public float GetWarningSeconds(float gapStartLocalZ = GapStartLocalZ)
        {
            if (approachTrigger == null)
            {
                return 0f;
            }

            float triggerLeadingEdge = approachTrigger.center.z - approachTrigger.size.z * 0.5f;
            return (gapStartLocalZ - triggerLeadingEdge) / MaximumExpectedForwardSpeed;
        }

        public int GetMaximumParticleCount()
        {
            int total = 0;
            foreach (ParticleSystem particles in AllParticles())
            {
                if (particles != null)
                {
                    total += particles.main.maxParticles;
                }
            }

            return total;
        }

        public void ApplyReviewState(BrokenRoadEncounterState reviewState, float simulationTime)
        {
            EnsureInitialized();
            StopRevealRoutine();
            StopAndClear(AllParticles());
            RestoreCoverSlabs();

            switch (reviewState)
            {
                case BrokenRoadEncounterState.Armed:
                    State = BrokenRoadEncounterState.Armed;
                    Simulate(warningParticles, Mathf.Max(0.1f, simulationTime));
                    break;

                case BrokenRoadEncounterState.Exploding:
                    State = BrokenRoadEncounterState.Exploding;
                    ApplyCoverProgress(0.45f);
                    Simulate(burstParticles, Mathf.Max(0.12f, simulationTime));
                    break;

                case BrokenRoadEncounterState.GapRevealed:
                    State = BrokenRoadEncounterState.GapRevealed;
                    HideCoverSlabs();
                    Simulate(burstParticles, Mathf.Max(0.65f, simulationTime));
                    Simulate(burningParticles, 0.2f);
                    break;

                default:
                    State = BrokenRoadEncounterState.Burning;
                    HideCoverSlabs();
                    Simulate(burningParticles, Mathf.Max(1.2f, simulationTime));
                    break;
            }
        }

        public void ConfigureForPrototype(
            BoxCollider configuredTrigger,
            Transform[] configuredCoverSlabs,
            Vector3[] configuredRevealOffsets,
            Vector3[] configuredRevealEulerAngles,
            float configuredRevealDuration,
            ParticleSystem[] configuredWarningParticles,
            ParticleSystem[] configuredBurstParticles,
            ParticleSystem[] configuredBurningParticles)
        {
            approachTrigger = configuredTrigger;
            coverSlabs = configuredCoverSlabs ?? System.Array.Empty<Transform>();
            revealOffsets = configuredRevealOffsets ?? System.Array.Empty<Vector3>();
            revealEulerAngles = configuredRevealEulerAngles ?? System.Array.Empty<Vector3>();
            revealDuration = Mathf.Max(0.05f, configuredRevealDuration);
            warningParticles = configuredWarningParticles ?? System.Array.Empty<ParticleSystem>();
            burstParticles = configuredBurstParticles ?? System.Array.Empty<ParticleSystem>();
            burningParticles = configuredBurningParticles ?? System.Array.Empty<ParticleSystem>();
            initialized = false;
            EnsureInitialized();
            ResetEncounter(false);
        }

        private IEnumerator RevealCoverRoutine()
        {
            float elapsed = 0f;
            while (elapsed < revealDuration)
            {
                elapsed += Time.deltaTime;
                float progress = Mathf.Clamp01(elapsed / revealDuration);
                ApplyCoverProgress(progress);
                yield return null;
            }

            HideCoverSlabs();
            State = BrokenRoadEncounterState.GapRevealed;
            Play(burningParticles);
            yield return null;
            State = BrokenRoadEncounterState.Burning;
            revealRoutine = null;
        }

        private void EnsureInitialized()
        {
            if (initialized && initialLocalPositions.Length == coverSlabs.Length)
            {
                return;
            }

            initialLocalPositions = new Vector3[coverSlabs.Length];
            initialLocalRotations = new Quaternion[coverSlabs.Length];

            for (int index = 0; index < coverSlabs.Length; index++)
            {
                Transform slab = coverSlabs[index];
                if (slab == null)
                {
                    continue;
                }

                initialLocalPositions[index] = slab.localPosition;
                initialLocalRotations[index] = slab.localRotation;
            }

            initialized = true;
        }

        private void RestoreCoverSlabs()
        {
            EnsureInitialized();

            for (int index = 0; index < coverSlabs.Length; index++)
            {
                Transform slab = coverSlabs[index];
                if (slab == null)
                {
                    continue;
                }

                slab.gameObject.SetActive(true);
                slab.localPosition = initialLocalPositions[index];
                slab.localRotation = initialLocalRotations[index];
            }
        }

        private void ApplyCoverProgress(float progress)
        {
            EnsureInitialized();
            float eased = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress));

            for (int index = 0; index < coverSlabs.Length; index++)
            {
                Transform slab = coverSlabs[index];
                if (slab == null)
                {
                    continue;
                }

                Vector3 offset = index < revealOffsets.Length ? revealOffsets[index] : Vector3.down * 3f;
                Vector3 euler = index < revealEulerAngles.Length ? revealEulerAngles[index] : Vector3.zero;
                slab.localPosition = Vector3.Lerp(initialLocalPositions[index], initialLocalPositions[index] + offset, eased);
                slab.localRotation = Quaternion.Slerp(
                    initialLocalRotations[index],
                    initialLocalRotations[index] * Quaternion.Euler(euler),
                    eased);
            }
        }

        private void HideCoverSlabs()
        {
            foreach (Transform slab in coverSlabs)
            {
                if (slab != null)
                {
                    slab.gameObject.SetActive(false);
                }
            }
        }

        private void StopRevealRoutine()
        {
            if (revealRoutine == null)
            {
                return;
            }

            StopCoroutine(revealRoutine);
            revealRoutine = null;
        }

        private IEnumerable<ParticleSystem> AllParticles()
        {
            foreach (ParticleSystem particles in warningParticles)
            {
                yield return particles;
            }

            foreach (ParticleSystem particles in burstParticles)
            {
                yield return particles;
            }

            foreach (ParticleSystem particles in burningParticles)
            {
                yield return particles;
            }
        }

        private static void Play(IEnumerable<ParticleSystem> particleSystems)
        {
            foreach (ParticleSystem particles in particleSystems)
            {
                if (particles != null)
                {
                    particles.Play(true);
                }
            }
        }

        private static void StopAndClear(IEnumerable<ParticleSystem> particleSystems)
        {
            foreach (ParticleSystem particles in particleSystems)
            {
                if (particles != null)
                {
                    particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                }
            }
        }

        private static void Simulate(IEnumerable<ParticleSystem> particleSystems, float time)
        {
            foreach (ParticleSystem particles in particleSystems)
            {
                if (particles == null)
                {
                    continue;
                }

                particles.Simulate(time, true, true, true);
                particles.Pause(true);
            }
        }
    }
}
