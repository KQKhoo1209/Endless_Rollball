using System;
using UnityEngine;

namespace EndlessRollball.Scenery
{
    // Shelters belong to the building scenery, with explicit vegetation clearances.
    public sealed class CityBusStopSlots : MonoBehaviour
    {
        [SerializeField] private GameObject leftShelter;
        [SerializeField] private GameObject rightShelter;
        [SerializeField] private Renderer[] leftNature = Array.Empty<Renderer>();
        [SerializeField] private Renderer[] rightNature = Array.Empty<Renderer>();
        [SerializeField] private bool[] leftDefaults = Array.Empty<bool>();
        [SerializeField] private bool[] rightDefaults = Array.Empty<bool>();

        public void Configure(int side)
        {
            Restore(leftNature, leftDefaults);
            Restore(rightNature, rightDefaults);
            if (leftShelter != null) leftShelter.SetActive(side < 0);
            if (rightShelter != null) rightShelter.SetActive(side > 0);
            foreach (Renderer nature in side < 0 ? leftNature : side > 0 ? rightNature : Array.Empty<Renderer>())
                if (nature != null) nature.enabled = false;
        }

        private static void Restore(Renderer[] values, bool[] defaults)
        {
            for (int i = 0; i < values.Length; i++)
                if (values[i] != null) values[i].enabled = i < defaults.Length && defaults[i];
        }
    }
}
