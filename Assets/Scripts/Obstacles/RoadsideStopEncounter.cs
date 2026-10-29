using System;
using UnityEngine;

namespace EndlessRollball.Track
{
    [DisallowMultipleComponent]
    public sealed class RoadsideStopEncounter : CityVehicleEncounter
    {
        [SerializeField] private bool rightSide;
        [SerializeField] private GameObject[] busPrefabs = Array.Empty<GameObject>();
        [SerializeField] private GameObject taxiPrefab;
        public bool RightSide => rightSide;
        public override bool IsMoving => false;
        public override float YawDegrees => rightSide ? 180f : 0f;
        public override string FamilyId => "Street.ParkedVehicles";
        public override GameObject[] Sources
        {
            get
            {
                var result = new GameObject[busPrefabs.Length + 1];
                Array.Copy(busPrefabs, result, busPrefabs.Length);
                result[result.Length - 1] = taxiPrefab;
                return result;
            }
        }
        public override GameObject SelectVehicle(System.Random random) =>
            random.Next(2) == 0 && busPrefabs.Length > 0
                ? busPrefabs[random.Next(busPrefabs.Length)] : taxiPrefab;
        public override float VehicleZ(float progress) => 12f;
    }
}
