using System;
using UnityEngine;

namespace EndlessRollball.Track
{
    [DisallowMultipleComponent]
    public sealed class LongitudinalTrafficEncounter : CityVehicleEncounter
    {
        public const int SchedulePeriod = 8;
        public const int TrafficPhase = 3;
        public static bool IsApproachSegment(int index) =>
            index >= 0 && index % SchedulePeriod < TrafficPhase;
        [SerializeField] private bool oncoming;
        [SerializeField] private GameObject[] vehiclePrefabs = Array.Empty<GameObject>();
        [SerializeField] private Material fadeTemplate;
        public override Material FadeTemplate => fadeTemplate;
        public bool Oncoming => oncoming;
        public override bool IsMoving => true;
        public override float YawDegrees => oncoming ? 180f : 0f;
        public override string FamilyId => "Street.LongitudinalTraffic";
        public override GameObject[] Sources => vehiclePrefabs;
        public override GameObject SelectVehicle(System.Random random) =>
            vehiclePrefabs.Length == 0 ? null : vehiclePrefabs[random.Next(vehiclePrefabs.Length)];
        public override float VehicleZ(float progress) =>
            oncoming ? 27f - .5f * progress : -3f + .5f * progress;
    }
}
