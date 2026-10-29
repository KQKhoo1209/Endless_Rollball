using UnityEngine;

namespace EndlessRollball.Track
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TrackSegment))]
    public sealed class RoadworksTrackEncounter : MonoBehaviour
    {
        [Tooltip("Both left and right variants share this encounter family.")]
        [SerializeField]
        private string familyId = "Street.Roadworks";

        [Tooltip("The child containing the patch, cones and police car.")]
        [SerializeField]
        private GameObject hazardRoot;

        [Tooltip("Local Z where the hazardous section begins.")]
        [SerializeField, Min(0f)]
        private float hazardStartZ = 8f;

        [Tooltip("Local Z beyond every hazard, including the police car.")]
        [SerializeField, Min(0f)]
        private float hazardEndZ = 20f;

        [Tooltip("Local X of the clear passage beside the roadworks.")]
        [SerializeField]
        private float safePassageX = 2.25f;

        public string FamilyId => familyId;
        public GameObject HazardRoot => hazardRoot;
        public float HazardStartZ => hazardStartZ;
        public float HazardEndZ => hazardEndZ;
        public float SafePassageX => safePassageX;
        public TrackSegment Track => GetComponent<TrackSegment>();
    }
}