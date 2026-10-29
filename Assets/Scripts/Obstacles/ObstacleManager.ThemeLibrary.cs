using System;
using System.Collections.Generic;
using UnityEngine;

namespace EndlessRollball.Track
{
    public sealed partial class ObstacleManager
    {
        // Validate and collect each library once per run.
        private readonly Dictionary<ThemeObstacleLibrary, GameObject[]>
            fixedLibraryCache =
                new Dictionary<ThemeObstacleLibrary, GameObject[]>();
        private readonly Dictionary<ThemeObstacleLibrary, Dictionary<GameObject, string>> fixedFamilyCache =
            new Dictionary<ThemeObstacleLibrary, Dictionary<GameObject, string>>();
        private Dictionary<GameObject, string> runFixedFamilies;

        private string FixedFamilyFor(GameObject prefab) =>
            runFixedFamilies != null && runFixedFamilies.TryGetValue(prefab, out string family)
                ? family : "Fixed";

        internal static Bounds RotatedFootprint(Bounds bounds, float yawDegrees)
        {
            Quaternion rotation = Quaternion.Euler(0f, yawDegrees, 0f);
            Bounds result = new Bounds(rotation * bounds.center, Vector3.zero);
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 point = bounds.center + Vector3.Scale(bounds.extents,
                    new Vector3((corner & 1) == 0 ? -1 : 1,
                        (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                result.Encapsulate(rotation * point);
            }
            return result;
        }

        private void UseFixedThemeLibrary(ThemeObstacleLibrary library)
        {
            // Always clear selections first, including on theme changes.
            runFixed = Array.Empty<GameObject>();
            runMoving = Array.Empty<GameObject>();
            runJump = Array.Empty<GameObject>();
            runFixedFamilies = null;

            if (library == null)
                return;

            if (fixedLibraryCache.TryGetValue(library, out GameObject[] cached))
            {
                runFixed = cached;
                runFixedFamilies = fixedFamilyCache[library];
                return;
            }

            var prefabs = new List<GameObject>();
            var unique = new HashSet<GameObject>();
            runFixedFamilies = new Dictionary<GameObject, string>();
            fixedFamilyCache[library] = runFixedFamilies;

            if (library.FixedFamilies != null)
            {
                foreach (ThemeObstacleLibrary.Family family
                    in library.FixedFamilies)
                {
                    if (family == null || family.Variants == null)
                        continue;

                    foreach (GameObject prefab in family.Variants)
                    {
                        if (prefab == null)
                        {
                            Debug.LogWarning(
                                $"{library.name}: missing fixed obstacle prefab.",
                                library);
                            continue;
                        }

                        if (unique.Add(prefab))
                        {
                            prefabs.Add(prefab);
                            runFixedFamilies[prefab] = string.IsNullOrWhiteSpace(family.FamilyId)
                                ? "Fixed" : family.FamilyId;
                        }
                    }
                }
            }

            // Reuse the existing static-prefab validation.
            if (!ValidateList(prefabs, false))
            {
                fixedLibraryCache.Add(library, Array.Empty<GameObject>());
                return;
            }

            foreach (GameObject prefab in prefabs)
            {
                PalmObstacle palm = prefab.GetComponent<PalmObstacle>();
                bool beachRoot = palm != null && palm.RootedOnBeach;
                if (!TryGetFootprint(prefab, out Bounds bounds) ||
                    (!beachRoot && bounds.size.x > FootprintLimitX * 2f) ||
                    bounds.min.z < -6f ||
                    bounds.max.z > 6f)
                {
                    Debug.LogWarning(
                        $"{prefab.name}: fixed obstacle footprint is invalid.",
                        prefab);

                    fixedLibraryCache.Add(
                        library, Array.Empty<GameObject>());
                    return;
                }
            }

            runFixed = prefabs.ToArray();
            fixedLibraryCache.Add(library, runFixed);
        }
    }
}
