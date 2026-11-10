using UnityEngine;

namespace EndlessRollball
{
    public sealed class PhoenixLavaHazard : MonoBehaviour
    {
        public enum HazardKind { Pillar, Eruption, BouncingBall }
        [SerializeField] private HazardKind kind;
        [SerializeField] private Transform hazardVisual;
        // Retained so older prefab instances also have their warning disabled.
        [SerializeField, HideInInspector] private Renderer warningVisual;
        [SerializeField] private Collider hitCollider;
        [SerializeField] private Material sparkMaterial;
        [SerializeField, Min(0f)] private float bubbleLeadSeconds = 2f;
        private LavaSurfaceEffect surfaceEffect;
        private bool preparationStarted;
        private Rigidbody body;
        private ParticleSystem sparks;
        private ParticleSystem trail;
        private ParticleSystem splash;
        private float encounter, height, surfaceY, centreX, direction;
        private Vector3 start;
        private bool prepared;
        private bool eruptionPlaying;
        private bool ballActive;
        private DungeonObstacleAudio obstacleAudio;

        public void Prepare()
        {
            if (prepared) return;
            body = GetComponent<Rigidbody>();
            obstacleAudio = GetComponent<DungeonObstacleAudio>();
            if (kind != HazardKind.Pillar)
                surfaceEffect = LavaSurfaceEffect.Create(transform);
            if (warningVisual != null)
                warningVisual.gameObject.SetActive(false);
            sparks = FindParticles("Eruption Particles");
            trail = FindParticles("Ember Trail");
            splash = FindParticles("Lava Splash");
            if (sparks != null && sparkMaterial != null)
            {
                var renderer = sparks.GetComponent<ParticleSystemRenderer>();
                if (renderer.sharedMaterial == null)
                    renderer.sharedMaterial = sparkMaterial;
            }
            prepared = true;
        }

        private ParticleSystem FindParticles(string name)
        {
            Transform child = transform.Find(name);
            return child != null ? child.GetComponent<ParticleSystem>() : null;
        }

        public void ResetEncounter(Vector3 worldStart, float encounterTime, float flightHeight,
            float channelCentre, float crossingDirection)
        {
            Prepare();
            obstacleAudio?.ResetAudio();
            start = worldStart;
            encounter = encounterTime;
            height = flightHeight;
            surfaceY = worldStart.y;
            centreX = channelCentre;
            direction = crossingDirection;
            transform.position = start;
            if (body != null) body.position = start;
            eruptionPlaying = ballActive = false;
            preparationStarted = false;
            Vector3 surfacePosition = start;
            surfacePosition.y = surfaceY + .08f;
            surfaceEffect?.Reset(transform.parent, surfacePosition);
            StopParticles(sparks);
            StopParticles(trail);
            StopParticles(splash);
            if (splash != null)
                splash.transform.SetParent(transform.parent, true);
            hitCollider.enabled = false;
            hazardVisual.localRotation = Quaternion.identity;
            hazardVisual.gameObject.SetActive(false);
        }

        public void Tick(float time)
        {
            if (kind == HazardKind.Pillar)
            {
                hitCollider.enabled = false;
                return;
            }
            float launchTime = encounter - (kind == HazardKind.Eruption ? .35f : 1.6f);
            if (!preparationStarted && time >= launchTime - bubbleLeadSeconds && time < launchTime)
            {
                Vector3 emergence = kind == HazardKind.Eruption ? start :
                    new Vector3(centreX - direction * 4.5f, surfaceY, start.z);
                emergence.y = surfaceY + .08f;
                surfaceEffect?.BeginPrepare(emergence);
                obstacleAudio?.BeginWarning();
                preparationStarted = true;
            }
            if (kind == HazardKind.Eruption)
            {
                TickEruption(time);
                return;
            }

            float u = Mathf.Clamp01((time - (encounter - 1.6f)) / 3.2f);
            bool active = time >= encounter - 1.6f && time <= encounter + 1.6f;
            hazardVisual.gameObject.SetActive(active);
            hitCollider.enabled = active;
            Vector3 position = new Vector3(
                centreX + direction * Mathf.Lerp(-4.5f, 4.5f, u),
                surfaceY + .65f + PhoenixLavaMath.JumpLift(u, height + 1.8f), start.z);
            body.MovePosition(position);
            if (active && !ballActive)
            {
                obstacleAudio?.BeginMotion();
                surfaceEffect?.EndPrepare();
                if (trail != null) trail.Play();
            }
            else if (!active && ballActive)
            {
                if (trail != null) trail.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                PlaySplash(position);
            }
            ballActive = active;
        }

        private void TickEruption(float time)
        {
            // Molten mass and droplets rise vertically, then return under gravity.
            float gravity = Mathf.Max(.1f, -Physics.gravity.y);
            float riseHeight = Mathf.Max(2f, height + 3f - surfaceY);
            float launchSpeed = Mathf.Sqrt(2f * gravity * riseHeight);
            float flightSeconds = 2f * launchSpeed / gravity;
            float age = time - (encounter - .35f);
            bool active = age >= 0f && age <= flightSeconds;
            hazardVisual.gameObject.SetActive(active);
            hitCollider.enabled = active;
            if (active)
            {
                float rise = Mathf.Max(0f, launchSpeed * age - .5f * gravity * age * age);
                float stretch = Mathf.Abs(launchSpeed - gravity * age) / launchSpeed;
                float massHeight = 1.8f + .5f * stretch;
                float centreY = .9f + rise;
                hazardVisual.localPosition = Vector3.up * centreY;
                hazardVisual.localScale = new Vector3(1.8f, massHeight, 1.8f);
                var capsule = (CapsuleCollider)hitCollider;
                capsule.radius = .9f;
                capsule.height = massHeight;
                capsule.center = Vector3.up * centreY;
            }
            if (active && !eruptionPlaying)
            {
                obstacleAudio?.BeginMotion();
                surfaceEffect?.EndPrepare();
                if (sparks != null) sparks.Play();
            }
            if (!active && eruptionPlaying)
            {
                if (sparks != null)
                    sparks.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                PlaySplash(start);
            }
            eruptionPlaying = active;
        }

        private void PlaySplash(Vector3 position)
        {
            obstacleAudio?.Land();
            position.y = surfaceY + .08f;
            surfaceEffect?.Impact(position);
            if (splash == null) return;
            splash.transform.position = position;
            splash.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            splash.Play();
        }

        private static void StopParticles(ParticleSystem particles)
        {
            if (particles != null)
                particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        private void OnDestroy() => surfaceEffect?.Dispose();

        private void OnDisable()
        {
            obstacleAudio?.StopAudio();
            surfaceEffect?.Clear();
            if (hitCollider != null) hitCollider.enabled = false;
            if (warningVisual != null) warningVisual.gameObject.SetActive(false);
            StopParticles(sparks);
            StopParticles(trail);
            StopParticles(splash);
            eruptionPlaying = ballActive = false;
        }
    }
}
