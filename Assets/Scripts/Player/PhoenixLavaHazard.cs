using UnityEngine;

namespace EndlessRollball
{
    public sealed class PhoenixLavaHazard : MonoBehaviour
    {
        public enum HazardKind { Pillar, Eruption, BouncingBall }
        [SerializeField] private HazardKind kind;
        [SerializeField] private Transform hazardVisual;
        [SerializeField] private Renderer warningVisual;
        [SerializeField] private Collider hitCollider;
        [SerializeField] private Material sparkMaterial;
        private Rigidbody body;
        private ParticleSystem sparks;
        private MaterialPropertyBlock warningProperties;
        private float encounter, height, surfaceY, centreX, direction;
        private Vector3 start;
        private bool prepared;
        private bool eruptionPlaying;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        public void Prepare()
        {
            if (prepared) return;
            body = GetComponent<Rigidbody>();
            warningProperties = new MaterialPropertyBlock();
            if (kind == HazardKind.Eruption)
            {
                // Created once per pooled encounter; no runtime particle/component churn.
                GameObject plume = new GameObject("Decorative Lava Sparks");
                plume.transform.SetParent(transform, false);
                sparks = plume.AddComponent<ParticleSystem>();
                sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = sparks.main;
                main.playOnAwake = false;
                main.loop = true;
                main.duration = 1.25f;
                main.startLifetime = .8f;
                main.startSpeed = 5f;
                main.startSize = .22f;
                main.startColor = new Color(1f, .32f, .015f, 1f);
                main.gravityModifier = .7f;
                main.maxParticles = 48;
                var emission = sparks.emission;
                emission.rateOverTime = 36f;
                var shape = sparks.shape;
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = 15f;
                shape.radius = .7f;
                plume.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                var collision = sparks.collision;
                collision.enabled = false;
                sparks.GetComponent<ParticleSystemRenderer>().sharedMaterial = sparkMaterial;
            }
            prepared = true;
        }

        public void ResetEncounter(Vector3 worldStart, float encounterTime, float flightHeight,
            float channelCentre, float crossingDirection)
        {
            Prepare();
            start = worldStart;
            encounter = encounterTime;
            height = flightHeight;
            surfaceY = 0f;
            centreX = channelCentre;
            direction = crossingDirection;
            transform.position = start;
            if (body != null) body.position = start;
            eruptionPlaying = false;
            if (sparks != null) sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            hitCollider.enabled = false;
            warningVisual.gameObject.SetActive(false);
            hazardVisual.gameObject.SetActive(kind == HazardKind.Pillar);
            if (kind == HazardKind.Pillar)
            {
                float pillarHeight = height + 3f;
                hazardVisual.localPosition = Vector3.up * (pillarHeight * .5f);
                hazardVisual.localScale = new Vector3(2.2f, pillarHeight, 2.2f);
                BoxCollider box = (BoxCollider)hitCollider;
                box.size = new Vector3(2.2f, pillarHeight, 2.2f);
                box.center = Vector3.up * (pillarHeight * .5f);
            }
            if (kind == HazardKind.BouncingBall)
            {
                // The warning belongs to the fixed course, not the airborne body.
                warningVisual.transform.SetParent(transform.parent, true);
                warningVisual.transform.position = new Vector3(centreX, .04f, start.z);
            }
        }

        public void Tick(float time)
        {
            if (kind == HazardKind.Pillar)
            {
                hitCollider.enabled = true;
                return;
            }
            bool warning = time >= encounter - (kind == HazardKind.Eruption ? 2.4f : 3.6f) && time <= encounter + 1.6f;
            warningVisual.gameObject.SetActive(warning);
            if (warning)
            {
                float pulse = .65f + .35f * Mathf.Sin(time * 12f);
                warningProperties.SetColor(BaseColor, new Color(1f, .75f, .06f, 1f) * pulse);
                warningVisual.SetPropertyBlock(warningProperties);
            }
            if (kind == HazardKind.Eruption)
            {
                float columnHeight = PhoenixLavaMath.EruptionHeight(time, encounter, height + 3f);
                bool active = columnHeight > .15f;
                hazardVisual.gameObject.SetActive(active);
                hitCollider.enabled = active;
                if (active)
                {
                    hazardVisual.localPosition = Vector3.up * (columnHeight * .5f);
                    hazardVisual.localScale = new Vector3(2.2f, columnHeight * .5f, 2.2f);
                    CapsuleCollider capsule = (CapsuleCollider)hitCollider;
                    capsule.radius = Mathf.Min(1f, columnHeight * .5f);
                    capsule.height = columnHeight;
                    capsule.center = Vector3.up * (columnHeight * .5f);
                }
                if (active && !eruptionPlaying) sparks.Play();
                if (!active && eruptionPlaying) sparks.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                eruptionPlaying = active;
            }
            else
            {
                float u = Mathf.Clamp01((time - (encounter - 1.6f)) / 3.2f);
                bool active = time >= encounter - 1.6f && time <= encounter + 1.6f;
                hazardVisual.gameObject.SetActive(active);
                hitCollider.enabled = active;
                Vector3 position = new Vector3(centreX + direction * Mathf.Lerp(-4.5f, 4.5f, u),
                    surfaceY + .65f + PhoenixLavaMath.JumpLift(u, height + 1.8f), start.z);
                body.MovePosition(position);
            }
        }

        private void OnDisable()
        {
            if (hitCollider != null) hitCollider.enabled = false;
            if (warningVisual != null) warningVisual.gameObject.SetActive(false);
            if (sparks != null) sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            eruptionPlaying = false;
        }
    }
}
