using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace EndlessRollball.Editor
{
    public static class ShortCityPreviewBuilder
    {
        const string ScenePath = "Assets/Scenes/EnvironmentPreview.unity";
        const string Simple = "Assets/EndlessRollballAssets/SimplePoly City - Low Poly Assets/Prefab/";
        const string City = "Assets/EndlessRollballAssets/City Building/Models/FBX format/";
        const string Materials = "Assets/EndlessRollballAssets/Materials/Environment/ShortCityPreview";
        static readonly Dictionary<Material, Material> Converted = new Dictionary<Material, Material>();

        [MenuItem("Tools/Endless Rollball/Environment/Add Parked Street Vehicles %#&v")]
        public static void AddParkedVehicles()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (scene.path != ScenePath) scene = EditorSceneManager.OpenScene(ScenePath);
            var root = scene.GetRootGameObjects().FirstOrDefault(g => g.activeSelf && g.name == "StreetPreview_ShortCity_72m");
            if (root == null) { Debug.LogWarning("Active 72m street preview not found."); return; }
            if (root.transform.Find("06_Parked_Vehicles") != null) { Debug.Log("Parked vehicle category already exists."); return; }
            string backup = "Artifacts/EnvironmentPreviewBackups/" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            Directory.CreateDirectory(backup);
            File.Copy(ScenePath, backup + "/EnvironmentPreview.unity");
            var vehicles = Group("06_Parked_Vehicles", root.transform);
            var left = Group("Left_Curb", vehicles);
            var right = Group("Right_Curb", vehicles);
            string[] names = { "Vehicle_Car_color01", "Vehicle_SUV_color02", "Vehicle_Pick up Truck_color03",
                "Vehicle_Car_color02", "Vehicle_Taxi", "Vehicle_SUV_color03" };
            Converted.Clear();
            for (int i = 0; i < names.Length; i++)
            {
                int side = i < 3 ? -1 : 1;
                int slot = i % 3;
                var vehicle = Place(Simple + "Vehicles/Vehicle with Static Wheels/" + names[i] + ".prefab",
                    side < 0 ? left : right, Vector3.zero, 0, i == 2 || i == 1 || i == 5 ? 1.6f : 1.4f);
                var b = BoundsOf(vehicle);
                // Align the long axis to the road regardless of the imported vehicle's authoring direction.
                vehicle.transform.rotation = Quaternion.Euler(0, (b.size.x > b.size.z ? 90 : 0) + (side > 0 ? 180 : 0), 0);
                b = BoundsOf(vehicle);
                vehicle.transform.localScale *= Mathf.Min(1, Mathf.Min(1.7f / b.size.x, 4.4f / b.size.z));
                Ground(vehicle, new Vector3(side * 5.55f, .1f, (side < 0 ? 7.5f : 16.5f) + slot * 24));
                vehicle.name = "Parked_" + names[i];
            }
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            Selection.activeGameObject = vehicles.gameObject;
            Debug.Log("PARKED_VEHICLES_SAVED: six varied SimplePoly vehicles in 06_Parked_Vehicles, three per sidewalk curb, staggered along 72m. Visual-only. Backup: " + backup);
        }

        [MenuItem("Tools/Endless Rollball/Environment/Bring Nature To Visible Sidewalks %#&b")]
        public static void BringNatureToSidewalks()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (scene.path != ScenePath) scene = EditorSceneManager.OpenScene(ScenePath);
            var root = scene.GetRootGameObjects().FirstOrDefault(g => g.activeSelf && g.name == "StreetPreview_ShortCity_72m");
            if (root == null) { Debug.LogWarning("Active 72m street preview not found."); return; }
            var nature = root.transform.Cast<Transform>().FirstOrDefault(t => t.name.EndsWith("_Nature"));
            if (nature == null) throw new InvalidOperationException("Nature category not found.");
            if (nature.Find("Sidewalk_Planting") != null) { Debug.Log("Visible sidewalk planting already exists."); return; }
            string backup = "Artifacts/EnvironmentPreviewBackups/" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            Directory.CreateDirectory(backup);
            File.Copy(ScenePath, backup + "/EnvironmentPreview.unity");
            var archive = Group("Previous_Planting_Inactive", nature);
            archive.gameObject.SetActive(false);
            foreach (var previous in nature.Cast<Transform>().Where(t => t.name == "Planted_Grass_Areas" || t.name == "Bushes_And_Planters").ToArray())
                previous.SetParent(archive, true);
            var planting = Group("Sidewalk_Planting", nature);
            var grass = Group("Grass_Beds", planting);
            var bushes = Group("Shrub_Clusters", planting);
            var planters = Group("Frontage_Planters", planting);
            var trees = nature.Find("Trees");
            Converted.Clear();
            int relocated = 0, treeIndex = 0;
            foreach (var tree in trees.Cast<Transform>().ToArray())
            {
                var bounds = BoundsOf(tree.gameObject);
                int side = bounds.center.x < 0 ? -1 : 1;
                if (Mathf.Abs(bounds.center.x) > 9)
                {
                    Ground(tree.gameObject, new Vector3(side * 7, .1f, 9 + (relocated % 3) * 24));
                    relocated++;
                }
                float scaleVariation = .91f + (treeIndex % 4) * .045f;
                tree.localScale *= scaleVariation;
                bounds = BoundsOf(tree.gameObject);
                float width = Mathf.Max(bounds.size.x, bounds.size.z);
                tree.localScale *= Mathf.Min(1, 3.7f / width);
                bounds = BoundsOf(tree.gameObject);
                var position = new Vector3(side * 7.02f, .1f, bounds.center.z);
                Ground(tree.gameObject, position);
                var bed = Place(Simple + "Natures/Natures_Grass Tile Small.prefab", grass, Vector3.zero, 0, 1);
                Fit(bed, new Vector3(1.02f, .035f, 2.25f));
                Ground(bed, new Vector3(position.x, .105f, position.z));
                bed.name = "Tree_Grass_Bed_" + (++treeIndex);
            }
            for (int side = -1; side <= 1; side += 2)
            {
                for (int i = 0; i < 9; i++)
                {
                    float z = 6.3f + i * 8 + (side < 0 ? .25f : -.25f);
                    float x = side * (6.98f + (i % 2) * .08f);
                    var bed = Place(Simple + "Natures/Natures_Grass Tile Small.prefab", grass, Vector3.zero, 0, 1);
                    Fit(bed, new Vector3(.95f, .035f, 1.7f + (i % 3) * .25f));
                    Ground(bed, new Vector3(x, .105f, z));
                    bed.name = "Shrub_Bed_" + (side < 0 ? "Left_" : "Right_") + i;
                    var bush = Place(Simple + "Natures/Natures_Bush_0" + (1 + i % 3) + ".prefab", bushes,
                        Vector3.zero, i * 37 + side * 15, .5f + (i % 3) * .12f);
                    var b = BoundsOf(bush);
                    bush.transform.localScale *= Mathf.Min(1, .85f / Mathf.Max(b.size.x, b.size.z));
                    Ground(bush, new Vector3(x, .14f, z + .15f));
                }
                for (int i = 0; i < 6; i++)
                {
                    var pot = Place(Simple + "Natures/" + (i % 2 == 0 ? "Natures_Pot Bush_big" : "Natures_Pot Bush_small") + ".prefab",
                        planters, Vector3.zero, i * 23, .85f + (i % 2) * .15f);
                    var b = BoundsOf(pot);
                    pot.transform.localScale *= Mathf.Min(1, .75f / Mathf.Max(b.size.x, b.size.z));
                    Ground(pot, new Vector3(side * 7.12f, .1f, 8.4f + i * 12 + side * .25f));
                }
            }
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            Selection.activeGameObject = nature.gameObject;
            Debug.Log("SIDEWALK_NATURE_SAVED: " + relocated + " background trees moved forward; " + treeIndex +
                " tree beds, 18 visible shrub beds and 12 frontage planters. Planting at outer sidewalk edge, varied sizes and spacing. Backup: " + backup);
        }

        [MenuItem("Tools/Endless Rollball/Environment/Organize And Add Street Nature %#&n")]
        public static void AddStreetNature()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (scene.path != ScenePath) scene = EditorSceneManager.OpenScene(ScenePath);
            var root = scene.GetRootGameObjects().FirstOrDefault(g => g.activeSelf && g.name == "StreetPreview_ShortCity_72m");
            if (root == null) { Debug.LogWarning("Active 72m street preview not found."); return; }
            if (root.transform.Find("07_Nature") != null) { Debug.Log("Street nature group already exists."); return; }
            string backup = "Artifacts/EnvironmentPreviewBackups/" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            Directory.CreateDirectory(backup);
            File.Copy(ScenePath, backup + "/EnvironmentPreview.unity");
            var furniture = root.transform.Find("04_Trees_Lamps_Furniture");
            var nature = Group("07_Nature", root.transform);
            var trees = Group("Trees", nature);
            var shrubs = Group("Bushes_And_Planters", nature);
            var lawns = Group("Planted_Grass_Areas", nature);
            foreach (var child in furniture.Cast<Transform>().Where(t => t.name.StartsWith("Natures_")).ToArray())
                child.SetParent(trees, true);
            furniture.name = "04_Lamps_And_StreetFurniture";
            Converted.Clear();
            var occupied = root.transform.Find("02_Street_Buildings").GetComponentsInChildren<Renderer>()
                .Concat(root.transform.Find("03_Background_Buildings").GetComponentsInChildren<Renderer>())
                .Select(r => r.bounds).ToArray();
            int addedTrees = 0, addedLawns = 0;
            for (int side = -1; side <= 1; side += 2)
            {
                // Small planters follow the sidewalk outer edge, leaving the road and walking strip open.
                for (int i = 0; i < 6; i++)
                    Place(Simple + "Natures/Natures_Pot Bush_small.prefab", shrubs,
                        new Vector3(side * 7.05f, .1f, 7 + i * 12), i * 31, .75f);
                // Landscape vacant pockets between the shops and the skyline, keeping building footprints clear.
                for (int i = 0; i < 6; i++)
                {
                    float z = 6 + i * 12;
                    foreach (float distance in new[] { 16f, 19f, 23f })
                    {
                        float x = side * distance;
                        if (occupied.Any(b => x + 2 > b.min.x && x - 2 < b.max.x &&
                            z + 2.5f > b.min.z && z - 2.5f < b.max.z)) continue;
                        var lawn = Place(Simple + "Natures/Natures_Grass Tile.prefab", lawns, Vector3.zero, 0, 1);
                        Fit(lawn, new Vector3(3.6f, .08f, 4.5f));
                        Ground(lawn, new Vector3(x, .01f, z));
                        lawn.name = "Grass_Pocket_" + (side < 0 ? "Left_" : "Right_") + (i + 1);
                        Place(Simple + "Natures/" + (i % 2 == 0 ? "Natures_Cube Tree" : "Natures_Big Tree") + ".prefab",
                            trees, new Vector3(x, .09f, z + .4f), i * 47, 3.1f + (i % 3) * .35f);
                        Place(Simple + "Natures/Natures_Bush_0" + (1 + i % 3) + ".prefab", shrubs,
                            new Vector3(x + side * .9f, .09f, z - 1.25f), i * 29, .7f);
                        addedTrees++; addedLawns++;
                        break;
                    }
                }
            }
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            Selection.activeGameObject = nature.gameObject;
            Debug.Log("STREET_NATURE_ADDED: existing trees moved to 07_Nature/Trees; 12 sidewalk planters, " + addedTrees +
                " extra trees, " + addedLawns + " planted grass pockets with bushes. SimplePoly assets only. Backup: " + backup);
        }

        [MenuItem("Tools/Endless Rollball/Environment/Add Building Foundation Platforms %#&p")]
        public static void AddBuildingPlatforms()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (scene.path != ScenePath) scene = EditorSceneManager.OpenScene(ScenePath);
            var root = scene.GetRootGameObjects().FirstOrDefault(g => g.activeSelf && g.name == "StreetPreview_ShortCity_72m");
            if (root == null) { Debug.LogWarning("Active 72m street preview not found."); return; }
            if (root.transform.Find("06_Building_Foundation_Platforms") != null)
            { Debug.Log("Building foundation platforms already exist."); return; }
            string backup = "Artifacts/EnvironmentPreviewBackups/" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            Directory.CreateDirectory(backup);
            File.Copy(ScenePath, backup + "/EnvironmentPreview.unity");
            var fronts = root.transform.Find("02_Street_Buildings");
            var towers = root.transform.Find("03_Background_Buildings");
            if (fronts == null || towers == null) throw new InvalidOperationException("Missing building groups.");
            var renderers = fronts.GetComponentsInChildren<Renderer>().Concat(towers.GetComponentsInChildren<Renderer>()).ToArray();
            var foundations = Group("06_Building_Foundation_Platforms", root.transform);
            Converted.Clear();
            for (int side = -1; side <= 1; side += 2)
            {
                var sideRenderers = renderers.Where(r => side * r.bounds.center.x > 0).ToArray();
                float outer = Mathf.Max(12, sideRenderers.Max(r => side < 0 ? -r.bounds.min.x : r.bounds.max.x) + 1.5f);
                float startZ = Mathf.Min(0, sideRenderers.Min(r => r.bounds.min.z) - 1);
                float endZ = Mathf.Max(72, sideRenderers.Max(r => r.bounds.max.z) + 1);
                const float inner = 7.35f; // Overlap the sidewalk edge slightly without covering the road.
                var platform = Place(Simple + "Roads/Road Concrete Tile.prefab", foundations, Vector3.zero, 0, 1);
                Fit(platform, new Vector3(outer - inner, .65f, endZ - startZ));
                Ground(platform, new Vector3(side * (inner + outer) * .5f, -.65f, (startZ + endZ) * .5f));
                platform.name = side < 0 ? "Foundation_Left_72m" : "Foundation_Right_72m";
            }
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            Selection.activeGameObject = foundations.gameObject;
            Debug.Log("CITY_FOUNDATIONS_ADDED: two continuous concrete platforms beneath foreground and background buildings, top at ground level, 0.65m thick; SimplePoly geometry only. Backup: " + backup);
        }

        [MenuItem("Tools/Endless Rollball/Environment/Organize City As One 72m Street %#&o")]
        public static void Organize72m()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (scene.path != ScenePath) scene = EditorSceneManager.OpenScene(ScenePath);
            var root = scene.GetRootGameObjects().FirstOrDefault(g => g.activeSelf && g.name == "StreetPreview_ShortCity_72m");
            if (root == null) { Debug.LogWarning("Active 72m street preview not found."); return; }
            string backup = "Artifacts/EnvironmentPreviewBackups/" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            Directory.CreateDirectory(backup);
            File.Copy(ScenePath, backup + "/EnvironmentPreview.unity");
            var oldGroups = root.transform.Cast<Transform>().ToArray();
            var roads = Group("01_Road_And_Sidewalks_72m", root.transform);
            var buildings = Group("02_Street_Buildings", root.transform);
            var skyline = Group("03_Background_Buildings", root.transform);
            var props = Group("04_Trees_Lamps_Furniture", root.transform);
            var archive = Group("05_Previous_Layouts_Inactive", root.transform);
            archive.gameObject.SetActive(false);
            Action<Transform> mergeGroup = group =>
            {
                if (!group.gameObject.activeSelf || group.name.EndsWith("_Previous"))
                {
                    group.SetParent(archive, true);
                    return;
                }
                Transform destination = group.name.StartsWith("01_") ? roads
                    : group.name.StartsWith("02_") ? buildings
                    : group.name.StartsWith("03_") ? skyline : props;
                foreach (var child in group.Cast<Transform>().ToArray()) child.SetParent(destination, true);
                UnityEngine.Object.DestroyImmediate(group.gameObject);
            };
            foreach (var group in oldGroups)
            {
                if (group.name.StartsWith("Section_"))
                {
                    foreach (var subgroup in group.Cast<Transform>().ToArray()) mergeGroup(subgroup);
                    UnityEngine.Object.DestroyImmediate(group.gameObject);
                }
                else mergeGroup(group);
            }
            foreach (var previousRoot in scene.GetRootGameObjects().Where(g => g != root &&
                !g.activeSelf && g.GetComponent<Camera>() == null && g.GetComponent<Light>() == null).ToArray())
                previousRoot.transform.SetParent(archive, true);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            Selection.activeGameObject = root;
            Debug.Log("CITY_HIERARCHY_ORGANIZED: one StreetPreview_ShortCity_72m parent; roads, street buildings, background buildings and props merged across 72m. World transforms preserved. Prior inactive layouts archived. Backup: " + backup);
        }

        [MenuItem("Tools/Endless Rollball/Environment/Vary Buildings After 24m %#&l")]
        public static void VaryLaterBuildings()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (scene.path != ScenePath) scene = EditorSceneManager.OpenScene(ScenePath);
            var root = scene.GetRootGameObjects().FirstOrDefault(g => g.activeSelf && g.name == "StreetPreview_ShortCity_72m");
            if (root == null) { Debug.LogWarning("Active 72m street preview not found."); return; }
            string backup = "Artifacts/EnvironmentPreviewBackups/" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            Directory.CreateDirectory(backup);
            File.Copy(ScenePath, backup + "/EnvironmentPreview.unity");
            Directory.CreateDirectory(Materials);
            AssetDatabase.Refresh();
            Converted.Clear();
            string[][] fronts = {
                new[] { "Building_Clothing", "Building_Music Store", "Building_Pizza", "Building_Gift Shop" },
                new[] { "Building_Residential_color02", "Building_House_03_color01", "Building_Restaurant", "Building_Shoes Shop" }
            };
            string[][] towers = {
                new[] { "building-e", "building-h", "building-skyscraper-e", "building-k" },
                new[] { "building-g", "building-j", "building-m", "building-n" }
            };
            float[][] heights = { new[] { 7f, 9f, 6.5f, 8f }, new[] { 10f, 6f, 8f, 7f } };
            for (int sectionIndex = 0; sectionIndex < 2; sectionIndex++)
            {
                var section = root.transform.Find("Section_" + (sectionIndex + 2) + "_24m");
                if (section == null) throw new InvalidOperationException("Missing street extension section.");
                var previous = section.Cast<Transform>().Where(t => t.gameObject.activeSelf &&
                    (t.name.StartsWith("02_") || t.name.StartsWith("03_"))).ToArray();
                var shops = Group("02_Varied_Buildings", section);
                var backdrop = Group("03_Varied_CityBuilding", section);
                for (int index = 0; index < 4; index++)
                {
                    int side = index < 2 ? -1 : 1;
                    int slot = index % 2;
                    float z = (sectionIndex + 1) * 24 + (slot == 0 ? 5.5f : 17.5f) + (side > 0 ? .7f : 0);
                    var building = Place(Simple + "Buildings/" + fronts[sectionIndex][index] + ".prefab",
                        shops, Vector3.zero, side < 0 ? 90 : -90, heights[sectionIndex][index]);
                    var b = BoundsOf(building);
                    building.transform.localScale *= Mathf.Min(1, 9.5f / b.size.z);
                    b = BoundsOf(building);
                    Ground(building, new Vector3(side * (7.8f + b.extents.x + slot * .6f), 0, z));
                    float towerHeight = sectionIndex == 0 ? new[] { 17f, 22f, 30f, 18f }[index]
                        : new[] { 24f, 16f, 21f, 27f }[index];
                    var tower = Place(City + towers[sectionIndex][index] + ".fbx", backdrop,
                        Vector3.zero, index % 2 == 0 ? 0 : 90, towerHeight);
                    b = BoundsOf(tower);
                    tower.transform.localScale *= Mathf.Min(1, 10.5f / b.size.z);
                    b = BoundsOf(tower);
                    Ground(tower, new Vector3(side * (20 + b.extents.x + slot * 3), 0, z));
                }
                foreach (var old in previous) { old.gameObject.SetActive(false); old.name += "_Previous"; }
            }
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            Selection.activeGameObject = root;
            Debug.Log("CITY_BUILDINGS_VARIED: 24-48m shopping section; 48-72m mixed residential section; eight different City Building models, varied heights and setbacks. First 24m retained. Backup: " + backup);
        }

        [MenuItem("Tools/Endless Rollball/Environment/Extend Current City To 72m %#&k")]
        public static void ExtendTo72m()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (scene.path != ScenePath) scene = EditorSceneManager.OpenScene(ScenePath);
            var root = scene.GetRootGameObjects().FirstOrDefault(g => g.activeSelf && g.name == "StreetPreview_ShortCity_24m");
            if (root == null)
            {
                Debug.LogWarning("No active 24m city preview to extend. Existing scene left unchanged.");
                return;
            }
            string backup = "Artifacts/EnvironmentPreviewBackups/" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            Directory.CreateDirectory(backup);
            File.Copy(ScenePath, backup + "/EnvironmentPreview.unity");
            var originalGroups = root.transform.Cast<Transform>().ToArray();
            for (int section = 1; section <= 2; section++)
            {
                var extension = Group("Section_" + (section + 1) + "_24m", root.transform);
                extension.localPosition = new Vector3(0, 0, section * 24);
                foreach (var original in originalGroups)
                {
                    // Duplicate the approved scene layout, retaining shared imported meshes/materials.
                    var copy = UnityEngine.Object.Instantiate(original.gameObject, extension, false);
                    copy.name = original.name;
                    copy.transform.localPosition = original.localPosition;
                    copy.transform.localRotation = original.localRotation;
                    copy.transform.localScale = original.localScale;
                }
            }
            root.name = "StreetPreview_ShortCity_72m";
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            Selection.activeGameObject = root;
            Debug.Log("CITY_PREVIEW_EXTENDED_72M: original 24m preserved; sections added at +24m and +48m. Backup: " + backup);
        }

        [MenuItem("Tools/Endless Rollball/Environment/Build Short City 24m %#&j")]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            string backup = "Artifacts/EnvironmentPreviewBackups/" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            Directory.CreateDirectory(backup);
            File.Copy(ScenePath, backup + "/EnvironmentPreview.unity");
            var scene = EditorSceneManager.OpenScene(ScenePath);
            Directory.CreateDirectory(Materials);
            AssetDatabase.Refresh();
            Converted.Clear();
            foreach (var old in scene.GetRootGameObjects())
                if (old.GetComponent<Camera>() == null && old.GetComponent<Light>() == null) old.SetActive(false);
            var root = new GameObject("StreetPreview_ShortCity_24m");
            var road = Group("01_Road_24m", root.transform);
            var buildings = Group("02_SimplePoly_Shops", root.transform);
            var skyline = Group("03_CityBuilding_Towers", root.transform);
            var props = Group("04_Trees_Lamps_Furniture", root.transform);
            for (int i = 0; i < 3; i++)
            {
                var tile = Place(Simple + "Roads/Road Lane_01.prefab", road, Vector3.zero, 0, 1);
                Fit(tile, new Vector3(9, .15f, 8));
                Ground(tile, new Vector3(0, -.15f, 4 + 8 * i));
            }
            for (int side = -1; side <= 1; side += 2)
            {
                var walk = Place(Simple + "Roads/Road Sidewalk.prefab", road, Vector3.zero, 0, 1);
                Fit(walk, new Vector3(3, .2f, 24));
                Ground(walk, new Vector3(side * 6, -.1f, 12));
                string[] shops = side < 0 ? new[] {"Building_Coffee Shop", "Building_Books Shop"} : new[] {"Building_Bakery", "Building_Residential_color01"};
                for (int i = 0; i < 2; i++)
                {
                    var shop = Place(Simple + "Buildings/" + shops[i] + ".prefab", buildings, Vector3.zero, side < 0 ? 90 : -90, 8);
                    var b = BoundsOf(shop);
                    shop.transform.localScale *= Mathf.Min(1, 10 / b.size.z);
                    b = BoundsOf(shop);
                    Ground(shop, new Vector3(side * (7.8f + b.extents.x), .0f, 6 + i * 12));
                    var tower = Place(City + "building-skyscraper-" + (side < 0 ? (i == 0 ? "a" : "c") : (i == 0 ? "b" : "d")) + ".fbx", skyline, Vector3.zero, 0, 20 + i * 6);
                    b = BoundsOf(tower);
                    Ground(tower, new Vector3(side * (20 + b.extents.x), 0, 6 + i * 12));
                }
                foreach (float z in new[] { 3f, 12f, 21f })
                {
                    Place(Simple + "Props/Props_Street Light.prefab", props, new Vector3(side * 5.6f, .1f, z), side < 0 ? 90 : -90, 5.5f);
                    Place(Simple + "Natures/Natures_Big Tree.prefab", props, new Vector3(side * 7, .1f, z + 1), z * 7, 4);
                }
                Place(Simple + "Props/Props_Bench_1.prefab", props, new Vector3(side * 6.4f, .1f, 8), side < 0 ? 90 : -90, .85f);
                Place(Simple + "Props/Props_Dustbin.prefab", props, new Vector3(side * 6.4f, .1f, 17), 0, 1);
            }
            var camera = scene.GetRootGameObjects().Select(g => g.GetComponent<Camera>()).FirstOrDefault(c => c != null);
            if (camera == null) camera = new GameObject("Main Camera").AddComponent<Camera>();
            camera.gameObject.SetActive(true);
            camera.enabled = true;
            camera.transform.position = new Vector3(0, 3.2f, -3);
            camera.transform.LookAt(new Vector3(0, 2, 20));
            camera.fieldOfView = 65;
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.farClipPlane = 300;
            var sun = scene.GetRootGameObjects().Select(g => g.GetComponent<Light>()).FirstOrDefault(l => l != null && l.type == LightType.Directional);
            if (sun == null) sun = new GameObject("Directional Light").AddComponent<Light>();
            sun.gameObject.SetActive(true);
            sun.transform.rotation = Quaternion.Euler(35, -35, 0);
            sun.color = new Color(.85f, .9f, 1);
            sun.intensity = 1.2f;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.55f, .65f, .85f);
            RenderSettings.ambientEquatorColor = new Color(.4f, .45f, .55f);
            RenderSettings.ambientGroundColor = new Color(.25f, .28f, .34f);
            RenderSettings.fog = false;
            // Retain the existing scene skybox. Geometry comes only from the two selected packs.
            Lightmapping.Clear();
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            Selection.activeGameObject = root;
            Debug.Log("SHORT_CITY_PREVIEW_SAVED: 24m road, 4 SimplePoly buildings, 4 City Building towers, 6 trees, 6 lamps, 2 benches, 2 bins. Backup: " + backup);
        }

        static Transform Group(string name, Transform parent)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false); return go.transform;
        }
        internal static Bounds BoundsOf(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            var b = rs[0].bounds;
            foreach (var r in rs.Skip(1)) b.Encapsulate(r.bounds);
            return b;
        }
        internal static void Ground(GameObject go, Vector3 point)
        {
            var b = BoundsOf(go); go.transform.position += point - new Vector3(b.center.x, b.min.y, b.center.z);
        }
        internal static void Fit(GameObject go, Vector3 size)
        {
            var b = BoundsOf(go); go.transform.localScale = Vector3.Scale(go.transform.localScale, new Vector3(size.x / b.size.x, size.y / b.size.y, size.z / b.size.z));
        }
        internal static GameObject Place(string path, Transform parent, Vector3 point, float yaw, float height)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (source == null) throw new FileNotFoundException(path);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(source, parent);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0, yaw, 0));
            go.transform.localScale *= height / BoundsOf(go).size.y;
            Ground(go, point);
            foreach (var c in go.GetComponentsInChildren<Collider>()) c.enabled = false;
            foreach (var l in go.GetComponentsInChildren<Light>()) l.enabled = false;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                r.lightmapIndex = -1;
                r.sharedMaterials = r.sharedMaterials.Select(ConvertMaterial).ToArray();
            }
            return go;
        }
        static Material ConvertMaterial(Material source)
        {
            if (source == null) return source;
            if (source.shader.name.StartsWith("Universal Render Pipeline/")) return source;
            if (Converted.TryGetValue(source, out var cached)) return cached;
            string sourcePath = AssetDatabase.GetAssetPath(source);
            string key = AssetDatabase.AssetPathToGUID(sourcePath) + "_" + source.name.Replace("/", "_");
            string path = Materials + "/" + key + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                material.name = "MAT_CityPreview_" + source.name;
                material.SetColor("_BaseColor", source.HasProperty("_Color") ? source.GetColor("_Color") : Color.white);
                var texture = source.mainTexture;
                if (texture == null && sourcePath.StartsWith(City)) texture = AssetDatabase.LoadAssetAtPath<Texture2D>(City + "Textures/colormap.png");
                material.SetTexture("_BaseMap", texture);
                material.SetTextureScale("_BaseMap", source.mainTextureScale);
                material.SetTextureOffset("_BaseMap", source.mainTextureOffset);
                material.SetFloat("_Smoothness", .15f);
                AssetDatabase.CreateAsset(material, path);
            }
            Converted[source] = material;
            return material;
        }
    }
}
