using System;
using System.Collections.Generic;
using UnityEngine;

namespace EndlessRollball.Track
{
    // An encounter owns its spawned vehicle and exposes only that vehicle's hazards.
    public abstract class CityVehicleEncounter : MonoBehaviour
    {
        [SerializeField] private Transform vehicleRoot;
        private GameObject vehicle;
        private Rigidbody body;
        private Transform player;
        private Vector3 initialPosition;
        private float greatestProgress;
        private bool prepared;
        private bool running;
        private TrackGenerator generator;
        private Renderer[] visuals = Array.Empty<Renderer>();
        private bool[] visualDefaults = Array.Empty<bool>();
        private float halfLength;
        private sealed class FadeVisual
        {
            public Renderer Renderer;
            public Material[] Originals, Fading;
            public Color[] Colors;
            public UnityEngine.Rendering.ShadowCastingMode Shadows;
        }
        private readonly List<FadeVisual> fades = new List<FadeVisual>();
        private float fadeElapsed;

        public Transform VehicleRoot => vehicleRoot;
        public GameObject Vehicle => vehicle;
        public Collider[] Hazards { get; private set; } = Array.Empty<Collider>();
        public abstract bool IsMoving { get; }
        public virtual Material FadeTemplate => null;
        public abstract float YawDegrees { get; }
        public abstract string FamilyId { get; }
        public abstract GameObject[] Sources { get; }
        public abstract GameObject SelectVehicle(System.Random random);
        public abstract float VehicleZ(float progress);

        public bool Prepare(GameObject source, Transform playerTransform, Vector3 position,
            TrackGenerator trackGenerator = null)
        {
            ClearVehicle();
            if (source == null || playerTransform == null || vehicleRoot == null)
                return false;

            player = playerTransform;
            generator = trackGenerator;
            initialPosition = position;
            greatestProgress = transform.InverseTransformPoint(player.position).z + 24f;
            if (IsMoving)
                position.z = VehicleZ(greatestProgress);
            vehicle = Instantiate(source, vehicleRoot, false);
            vehicle.transform.localPosition = position;
            vehicle.transform.localRotation = Quaternion.Euler(0f, YawDegrees, 0f);
            vehicle.transform.localScale = Vector3.one;
            vehicle.SetActive(true);
            Hazards = vehicle.GetComponentsInChildren<Collider>(true);
            visuals = vehicle.GetComponentsInChildren<Renderer>(true);
            visualDefaults = new bool[visuals.Length];
            for (int i = 0; i < visuals.Length; i++)
                visualDefaults[i] = visuals[i].enabled;
            halfLength = 0f;
            foreach (Collider shape in Hazards)
            {
                if (!(shape is BoxCollider box)) continue;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = box.center + Vector3.Scale(box.size * .5f,
                        new Vector3((corner & 1) == 0 ? -1 : 1,
                            (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                    halfLength = Mathf.Max(halfLength,
                        Mathf.Abs(vehicle.transform.InverseTransformPoint(box.transform.TransformPoint(point)).z));
                }
            }
            if (IsMoving)
            {
                body = vehicle.AddComponent<Rigidbody>();
                body.isKinematic = true;
                body.useGravity = false;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                BeginFade();
                UpdateVisibility();
            }
            prepared = true;
            return true;
        }

        public void SetRunning(bool value) => running = prepared && value;

        private void FixedUpdate()
        {
            if (!IsMoving || !running || player == null || body == null)
                return;
            float playerZ = transform.InverseTransformPoint(player.position).z;
            // Signed progress keeps cars moving from the generated horizon,
            // rather than waiting for the player to enter an activation radius.
            float progress = playerZ + 24f;
            greatestProgress = Mathf.Max(greatestProgress, progress);
            Vector3 position = initialPosition;
            position.z = VehicleZ(greatestProgress);
            Vector3 worldPosition = transform.TransformPoint(position);
            if (!vehicleRoot.gameObject.activeInHierarchy)
            {
                vehicle.transform.position = worldPosition;
                body.position = worldPosition;
            }
            else body.MovePosition(worldPosition);
            bool visible = UpdateVisibility();
            if (visible && vehicleRoot.gameObject.activeInHierarchy && fades.Count > 0)
            {
                fadeElapsed += Time.fixedDeltaTime;
                float alpha = Mathf.Clamp01(fadeElapsed);
                foreach (FadeVisual fade in fades)
                    for (int i = 0; i < fade.Fading.Length; i++)
                    {
                        Color color = fade.Colors[i];
                        color.a *= alpha;
                        fade.Fading[i].SetColor("_BaseColor", color);
                    }
                if (alpha >= 1f) ReleaseFade();
            }
        }

        private bool UpdateVisibility()
        {
            bool visible = generator == null || generator.HasVisibleCityRoad(
                vehicle.transform.position.z - halfLength,
                vehicle.transform.position.z + halfLength);
            for (int i = 0; i < visuals.Length; i++)
                if (visuals[i] != null) visuals[i].enabled = visible && visualDefaults[i];
            return visible;
        }

        private void BeginFade()
        {
            fadeElapsed = 0f;
            if (FadeTemplate == null) return;
            foreach (Renderer renderer in visuals)
            {
                Material[] originals = renderer.sharedMaterials;
                if (Array.Exists(originals, material => material == null || material.shader != FadeTemplate.shader)) continue;
                var fade = new FadeVisual { Renderer = renderer, Originals = originals,
                    Fading = new Material[originals.Length], Colors = new Color[originals.Length],
                    Shadows = renderer.shadowCastingMode };
                for (int i = 0; i < originals.Length; i++)
                {
                    Material material = new Material(originals[i]);
                    material.name = originals[i].name + " (traffic appearance)";
                    foreach (string property in new[] { "_Surface", "_Blend", "_SrcBlend", "_DstBlend", "_SrcBlendAlpha", "_DstBlendAlpha", "_ZWrite" })
                        if (FadeTemplate.HasProperty(property) && material.HasProperty(property))
                            material.SetFloat(property, FadeTemplate.GetFloat(property));
                    material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                    material.SetOverrideTag("RenderType", "Transparent");
                    material.renderQueue = FadeTemplate.renderQueue;
                    fade.Colors[i] = originals[i].GetColor("_BaseColor");
                    Color color = fade.Colors[i]; color.a = 0f;
                    material.SetColor("_BaseColor", color);
                    fade.Fading[i] = material;
                }
                renderer.sharedMaterials = fade.Fading;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                fades.Add(fade);
            }
        }

        private void ReleaseFade()
        {
            foreach (FadeVisual fade in fades)
            {
                if (fade.Renderer != null)
                {
                    fade.Renderer.sharedMaterials = fade.Originals;
                    fade.Renderer.shadowCastingMode = fade.Shadows;
                }
                foreach (Material material in fade.Fading)
                    if (material != null)
                    {
                        if (Application.isPlaying) Destroy(material);
                        else DestroyImmediate(material);
                    }
            }
            fades.Clear();
        }

        private void OnDisable()
        {
            ReleaseFade();
            prepared = running = false;
            player = null;
            generator = null;
            if (vehicle != null)
                vehicle.SetActive(false);
        }

        private void ClearVehicle()
        {
            ReleaseFade();
            prepared = running = false;
            greatestProgress = 0f;
            player = null;
            generator = null;
            visuals = Array.Empty<Renderer>();
            visualDefaults = Array.Empty<bool>();
            body = null;
            Hazards = Array.Empty<Collider>();
            if (vehicle != null)
            {
                vehicle.SetActive(false);
                Destroy(vehicle);
                vehicle = null;
            }
        }

        private void OnDestroy() => ReleaseFade();
    }
}
