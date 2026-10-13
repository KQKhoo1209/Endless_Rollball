using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace EndlessRollball.Editor
{
    public static class StreetCityVariationBuilder
    {
        const string Destination = "Assets/EndlessRollballAssets/Environment Prefab/StreetCity";
        const string Simple = "Assets/EndlessRollballAssets/SimplePoly City - Low Poly Assets/Prefab/";
        const string City = "Assets/EndlessRollballAssets/City Building/Models/FBX format/";

        [MenuItem("Tools/Endless Rollball/Environment/Sort Added Objects In Street D And E %#&s")]
        public static void SortAddedObjects()
        {
            string backup = "Artifacts/EnvironmentPrefabBackups/" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            Directory.CreateDirectory(backup);
            var report = new System.Collections.Generic.List<string>();
            foreach (string label in new[] { "D", "E" })
            {
                string path = Destination + "/ENV_StreetCity_Scenery_" + label + "_24m.prefab";
                File.Copy(path, backup + "/" + Path.GetFileName(path));
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var furniture = root.transform.Find("04_Lamps_And_StreetFurniture");
                    var nature = root.transform.Find("05_Nature");
                    var trees = nature.Find("Trees");
                    var shrubs = nature.Find("Shrubs_And_Planters");
                    var grass = nature.Find("Sidewalk_Grass_Beds");
                    var vehicles = root.transform.Find("06_Parked_Vehicles");
                    var left = vehicles.Find("Left_Curb");
                    var right = vehicles.Find("Right_Curb");
                    int moved = 0;
                    foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    {
                        if (t == root.transform) continue;
                        // Treat a nested imported prefab as one asset; do not sort its wheels or internal parts.
                        var nestedRoot = PrefabUtility.GetNearestPrefabInstanceRoot(t.gameObject);
                        if (nestedRoot != null && nestedRoot != t.gameObject) continue;
                        Transform destination = null;
                        string name = t.name;
                        if (name.StartsWith("Vehicle_") || name.StartsWith("Parked_Vehicle_"))
                            destination = root.transform.InverseTransformPoint(ShortCityPreviewBuilder.BoundsOf(t.gameObject).center).x < 0 ? left : right;
                        else if (name.StartsWith("Props_")) destination = furniture;
                        else if (name.StartsWith("Natures_") && name.IndexOf("Tree", StringComparison.OrdinalIgnoreCase) >= 0) destination = trees;
                        else if (name.StartsWith("Natures_") && name.IndexOf("Grass", StringComparison.OrdinalIgnoreCase) >= 0) destination = grass;
                        else if (name.StartsWith("Natures_") && name.IndexOf("Bush", StringComparison.OrdinalIgnoreCase) >= 0) destination = shrubs;
                        if (destination == null || t.parent == destination) continue;
                        t.SetParent(destination, true);
                        moved++;
                        report.Add(label + ": " + name + " -> " + destination.name);
                    }
                    var roads = root.transform.Find("01_Road_And_Sidewalks_72m");
                    if (roads != null) roads.name = "01_Sidewalks_And_Foundations_24m";
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    report.Add(label + ": " + moved + " objects regrouped; transforms and components retained.");
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            File.WriteAllLines(backup + "/Regrouping.txt", report);
            AssetDatabase.SaveAssets();
            Debug.Log("STREET_D_E_SORTED: " + string.Join("; ", report) + ". Backup: " + backup + ". No tests run.");
        }

        [MenuItem("Tools/Endless Rollball/Environment/Create Street Scenery D And E %#&d")]
        public static void Build()
        {
            string sourcePath = Destination + "/ENV_StreetCity_Scenery_A_24m.prefab";
            string[] names = { "ENV_StreetCity_Scenery_D_24m", "ENV_StreetCity_Scenery_E_24m" };
            string[][] buildings = {
                new[] { "Building_Auto Service", "Building_Drug Store", "Building_Fast Food", "Building_Fruits  Shop" },
                new[] { "Building_House_01_color02", "Building_House_02_color03", "Building_House_04_color01", "Building_Residential_color03" }
            };
            string[][] background = {
                new[] { "building-f", "building-i", "building-l", "building-skyscraper-e" },
                new[] { "building-a", "building-e", "building-m", "building-j" }
            };
            Directory.CreateDirectory("Assets/EndlessRollball/Materials/Environment/ShortCityPreview");
            AssetDatabase.Refresh();
            for (int variant = 0; variant < 2; variant++)
            {
                string path = Destination + "/" + names[variant] + ".prefab";
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
                { Debug.Log("Existing variant retained: " + path); continue; }
                var root = PrefabUtility.LoadPrefabContents(sourcePath);
                try
                {
                    root.name = names[variant];
                    foreach (var category in root.transform.Cast<Transform>().Where(t =>
                        t.name.StartsWith("02_") || t.name.StartsWith("03_") || t.name.EndsWith("_Nature")).ToArray())
                        UnityEngine.Object.DestroyImmediate(category.gameObject);
                    var fronts = Group("02_Street_Buildings", root.transform);
                    var skyline = Group("03_Background_Buildings", root.transform);
                    var nature = Group("05_Nature", root.transform);
                    var trees = Group("Trees", nature);
                    var beds = Group("Sidewalk_Grass_Beds", nature);
                    var shrubs = Group("Shrubs_And_Planters", nature);
                    for (int index = 0; index < 4; index++)
                    {
                        int side = index < 2 ? -1 : 1;
                        int slot = index % 2;
                        float z = (slot == 0 ? 5.5f : 17.7f) + (side > 0 ? -.5f : 0);
                        var building = ShortCityPreviewBuilder.Place(Simple + "Buildings/" + buildings[variant][index] + ".prefab",
                            fronts, Vector3.zero, side < 0 ? 90 : -90, variant == 0 ? 7.2f + index % 2 : 6.5f + index % 3);
                        FitFootprint(building, 12, 9.5f);
                        var b = ShortCityPreviewBuilder.BoundsOf(building);
                        ShortCityPreviewBuilder.Ground(building, new Vector3(side * (7.8f + b.extents.x + slot * .4f), 0, z));
                        var tower = ShortCityPreviewBuilder.Place(City + background[variant][index] + ".fbx", skyline,
                            Vector3.zero, slot == 0 ? 0 : 90, variant == 0 ? 21 + index * 2 : 16 + index * 3);
                        FitFootprint(tower, 15, 9.5f);
                        b = ShortCityPreviewBuilder.BoundsOf(tower);
                        ShortCityPreviewBuilder.Ground(tower, new Vector3(side * (19.5f + b.extents.x), 0, slot == 0 ? 6 : 18));
                    }
                    for (int side = -1; side <= 1; side += 2)
                    {
                        float[] treeZ = variant == 0 ? new[] { 3.5f, 11.3f, 21f } : new[] { 5f, 18.5f };
                        for (int i = 0; i < treeZ.Length; i++)
                        {
                            string treeName = variant == 0 ? (i == 1 ? "Natures_Cube Tree" : "Natures_Big Tree")
                                : (i == 0 ? "Natures_Fir Tree" : "Natures_Big Tree");
                            float z = treeZ[i] + side * .25f;
                            var tree = ShortCityPreviewBuilder.Place(Simple + "Natures/" + treeName + ".prefab",
                                trees, Vector3.zero, i * 61 + side * 15, variant == 0 ? 3.5f + i * .15f : 3.7f);
                            FitFootprint(tree, 3.6f, 3.6f);
                            ShortCityPreviewBuilder.Ground(tree, new Vector3(side * 7.05f, .1f, z));
                            AddBed(beds, side * 7.02f, z, 2.1f);
                        }
                        for (int i = 0; i < (variant == 0 ? 4 : 5); i++)
                        {
                            float z = (variant == 0 ? 6.3f + i * 4.5f : 2.4f + i * 4.6f) + side * .15f;
                            string prop = i % 2 == 0 ? "Natures_Pot Bush_big" : "Natures_Bush_0" + (1 + (i + variant) % 3);
                            var shrub = ShortCityPreviewBuilder.Place(Simple + "Natures/" + prop + ".prefab", shrubs,
                                Vector3.zero, i * 41, .6f + (i % 3) * .1f);
                            FitFootprint(shrub, .85f, .85f);
                            ShortCityPreviewBuilder.Ground(shrub, new Vector3(side * 7.07f, .14f, z));
                            if (i % 2 != 0) AddBed(beds, side * 7.02f, z, 1.6f);
                        }
                    }
                    StreetCitySceneryExtractor.StripNonVisualComponents(root);
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.SaveAssets();
            Selection.objects = names.Select(n => AssetDatabase.LoadAssetAtPath<GameObject>(Destination + "/" + n + ".prefab")).ToArray();
            EditorGUIUtility.PingObject(Selection.objects[0]);
            Debug.Log("STREET_VARIANTS_CREATED: D shopping/services with mixed broadleaf and cube trees; E residential with fir/broadleaf and lighter planting. " + Destination + ". A-C and EnvironmentPreview unchanged. No tests run.");
        }

        static Transform Group(string name, Transform parent)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false); return go.transform;
        }
        static void FitFootprint(GameObject go, float width, float length)
        {
            var b = ShortCityPreviewBuilder.BoundsOf(go);
            go.transform.localScale *= Mathf.Min(1, Mathf.Min(width / b.size.x, length / b.size.z));
        }
        static void AddBed(Transform parent, float x, float z, float length)
        {
            var bed = ShortCityPreviewBuilder.Place(Simple + "Natures/Natures_Grass Tile Small.prefab", parent, Vector3.zero, 0, 1);
            ShortCityPreviewBuilder.Fit(bed, new Vector3(1.02f, .035f, length));
            ShortCityPreviewBuilder.Ground(bed, new Vector3(x, .105f, z));
        }
    }
}
