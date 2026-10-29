using System;
using System.IO;
using System.Linq;
using EndlessRollball.Scenery;
using EndlessRollball.Track;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

public static class CityBusStopIntegration
{
    [Serializable] private sealed class TrafficRow
    {
        public string difficulty;
        public int forward, oncoming, stops, firstOncomingIndex = -1;
        public int seed, encounters, rejected;
        public int[] familyAppearances;
        public string mode;
        public float medianSeconds, longestSeconds;
    }
    [Serializable] private sealed class TrafficReport { public TrafficRow[] rows; }

    [MenuItem("Tools/Endless Rollball/Verify City Traffic Planner")]
    public static void VerifyTraffic()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Verify planning in Edit Mode.");
        const string prefix = "Assets/EndlessRollballAssets/";
        var library = AssetDatabase.LoadAssetAtPath<ThemeObstacleLibrary>(prefix +
            "Obstacles/Street/Street_ObstacleLibrary.asset");
        var road = AssetDatabase.LoadAssetAtPath<GameObject>(prefix +
            "Environment Prefab/Roads/TRK_Street_Road_24m.prefab").GetComponent<TrackSegment>();
        var rows = new System.Collections.Generic.List<TrafficRow>();
        var scene = EditorSceneManager.NewPreviewScene();
        GameObject owner = new GameObject("CityTrafficPlannerVerification") { hideFlags = HideFlags.HideAndDontSave };
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(owner, scene);
        try
        {
            var manager = owner.AddComponent<ObstacleManager>();
            var active = new GameObject("Active"); active.transform.SetParent(owner.transform);
            var pool = new GameObject("Pool"); pool.transform.SetParent(owner.transform);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(ObstacleManager).GetField("activeRoot", flags).SetValue(manager, active.transform);
            typeof(ObstacleManager).GetField("poolRoot", flags).SetValue(manager, pool.transform);
            MethodInfo planMethod = typeof(ObstacleManager).GetMethod("PlanSegment", flags);
            GameMode? savedMode = GameSessionSettings.SelectedMode;
            try {
            foreach (bool fast in new[] { false, true })
            foreach (int seed in Enumerable.Range(24001, 20))
            foreach (string name in new[] { "Easy", "Normal", "Hard", "Endless" })
            {
                GameSessionSettings.SelectedMode = name == "Endless" ? GameMode.Endless : fast ? GameMode.FastPaced : GameMode.Standard;
                if (!manager.BeginRun(seed)) throw new InvalidOperationException("Planner setup failed.");
                var profile = AssetDatabase.LoadAssetAtPath<DifficultyProfile>(prefix +
                    "Resources/Track/Difficulty_" + name + ".asset");
                var row = new TrafficRow { difficulty = name, seed = seed, mode = fast ? "FastPaced" : "Standard" };
                var intervals = new System.Collections.Generic.List<float>();
                float previousDecision = float.NaN;
                bool? previousDirection = null;
                for (int index = 0; index < 300; index++)
                {
                    float origin = index * 24f;
                    DifficultySnapshot difficulty = profile.Evaluate(origin);
                    MethodInfo take = typeof(ObstacleManager).GetMethod("TryTakeCityPlan", flags);
                    object[] takeArgs = { index, null };
                    if (!(bool)take.Invoke(manager, takeArgs))
                    {
                        Type requestType = typeof(ObstacleManager).GetNestedType("CityPlanningRequest", BindingFlags.NonPublic);
                        var requests = (System.Collections.IList)Activator.CreateInstance(typeof(System.Collections.Generic.List<>).MakeGenericType(requestType));
                        for (int offset = 0; offset < Math.Min(8, 300 - index); offset++)
                        {
                            object request = Activator.CreateInstance(requestType);
                            int future = index + offset;
                            foreach (var field in new System.Collections.Generic.Dictionary<string, object> {
                                {"Road",road}, {"Index",future}, {"Blocked",future < 2},
                                {"Difficulty",profile.Evaluate(future * 24f)}, {"Origin",future * 24f},
                                {"Limit",future * 24f + 71f}, {"Library",library},
                                {"Junction",true}, {"Traffic",true}, {"Stops",true} })
                                requestType.GetField(field.Key, flags).SetValue(request, field.Value);
                            requests.Add(request);
                        }
                        typeof(ObstacleManager).GetMethod("PlanCityWindow",flags).Invoke(manager,new object[]{requests});
                        take.Invoke(manager,takeArgs);
                    }
                    object plan = takeArgs[1];
                    Type type = plan.GetType();
                    float decision = float.NaN;
                    foreach (string windowName in new[] { "VehicleWindow", "RoadworksWindow", "JunctionWindow" })
                    {
                        object window = type.GetProperty(windowName).GetValue(plan);
                        if (window != null) decision = (float)window.GetType().GetField("HazardStartZ").GetValue(window);
                    }
                    var ordinary = type.GetProperty("Encounters").GetValue(plan) as System.Collections.IEnumerable;
                    if (ordinary != null) foreach (object item in ordinary)
                    {
                        object windows = item.GetType().GetProperty("Windows", flags | BindingFlags.Public).GetValue(item);
                        if (windows is Array array && array.Length > 0)
                            decision = (float)array.GetValue(0).GetType().GetField("HazardStartZ").GetValue(array.GetValue(0));
                    }
                    if (!float.IsNaN(decision))
                    {
                        row.encounters++;
                        if (!float.IsNaN(previousDecision)) intervals.Add((decision - previousDecision) / difficulty.ForwardSpeed);
                        previousDecision = decision;
                    }
                    var encounter = type.GetProperty("CityVehicle").GetValue(plan) as CityVehicleEncounter;
                    if (encounter is RoadsideStopEncounter) row.stops++;
                    if (!(encounter is LongitudinalTrafficEncounter traffic)) continue;
                    if (traffic.FadeTemplate == null || traffic.FadeTemplate.GetFloat("_Surface") != 1f)
                        throw new InvalidOperationException("Traffic appearance material is not wired.");
                    foreach (GameObject source in traffic.Sources)
                        if (source.GetComponentsInChildren<Renderer>(true).Any(renderer =>
                            renderer.sharedMaterials.Any(material => material.shader != traffic.FadeTemplate.shader)))
                            throw new InvalidOperationException("Traffic appearance shader does not match a vehicle.");
                    if (previousDirection.HasValue && previousDirection.Value == traffic.Oncoming)
                        throw new InvalidOperationException("Traffic direction repeated: " + name);
                    if (!previousDirection.HasValue && !traffic.Oncoming)
                        throw new InvalidOperationException("First traffic was not oncoming: " + name);
                    previousDirection = traffic.Oncoming;
                    if (traffic.Oncoming)
                    {
                        row.oncoming++;
                        if (row.firstOncomingIndex < 0) row.firstOncomingIndex = index;
                    }
                    else row.forward++;
                    foreach (float p in new[] { -144f, -96f, -48f, -24f, -18f, -12f, 0f, 28f })
                    {
                        float expected = traffic.Oncoming ? 15f - .5f * p : 9f + .5f * p;
                        if (Mathf.Abs(traffic.VehicleZ(p + 24f) - expected) > .0001f)
                            throw new InvalidOperationException("Incorrect moving-car trajectory.");
                    }
                }
                intervals.Sort();
                row.medianSeconds = intervals.Count > 0 ? intervals[intervals.Count / 2] : 0f;
                row.longestSeconds = intervals.Count > 0 ? intervals[intervals.Count - 1] : 0f;
                row.rejected = manager.CityRejectedPlacements;
                row.familyAppearances = manager.CityFamilyAppearances;
                rows.Add(row);
            }
            } finally { GameSessionSettings.SelectedMode = savedMode; }
            manager.EndRun();
        }
        finally
        {
            Object.DestroyImmediate(owner);
            EditorSceneManager.ClosePreviewScene(scene);
        }
        Directory.CreateDirectory("Artifacts/CityTraffic");
        File.WriteAllText("Artifacts/CityTraffic/traffic-planner-verification.json",
            JsonUtility.ToJson(new TrafficReport { rows = rows.ToArray() }, true));
        Debug.Log("City pacing report generated. Inspect encounter counts, intervals and rejected placements before accepting tuning.");
    }

    [Serializable] private sealed class Row
    {
        public string path;
        public int leftNature, rightNature, buildings;
        public float leftZ, rightZ;
        public int lampOverlapsBefore, lampOverlapsAfter;
    }
    [Serializable] private sealed class Report { public Row[] rows; }

    [MenuItem("Tools/Endless Rollball/Integrate City Bus Stops")]
    public static void Integrate()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Integrate scenery in Edit Mode.");
        const string prefix = "Assets/EndlessRollballAssets/";
        GameObject shelter = AssetDatabase.LoadAssetAtPath<GameObject>(prefix +
            "Street/SimplePoly City - Low Poly Assets/Prefab/Props/Props_Bus Stop.prefab");
        if (shelter == null) throw new InvalidOperationException("Missing shelter model.");
        var rows = new System.Collections.Generic.List<Row>();
        var lampLayouts = new System.Collections.Generic.Dictionary<char, Bounds[]>();
        foreach (string folder in new[] { "Environment Prefab/StreetCity", "Optimization/Runtime/Scenery" })
        foreach (char variant in "ABCDE")
        {
            string path = prefix + folder + "/ENV_StreetCity_Scenery_" + variant + "_24m.prefab";
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                CityBusStopSlots slots = root.GetComponent<CityBusStopSlots>();
                if (slots == null) slots = root.AddComponent<CityBusStopSlots>();
                // Restore defaults before re-running this authoring command.
                slots.Configure(0);
                Renderer[] all = root.GetComponentsInChildren<Renderer>(true);
                if (!lampLayouts.ContainsKey(variant))
                {
                    Bounds[] lamps = all.Where(IsStreetLamp).Select(GeometryBounds).ToArray();
                    if (lamps.Length == 0)
                        throw new InvalidOperationException("No original street lamps found: " + path);
                    lampLayouts.Add(variant, lamps);
                }
                Bounds[] lampBounds = lampLayouts[variant];
                Renderer[] buildings = all.Where(r => r.enabled && r.sharedMaterials.Any(m =>
                    m != null && m.name.StartsWith("Building_", StringComparison.Ordinal))).ToArray();
                var serialized = new SerializedObject(slots);
                int[] counts = new int[2];
                float[] stopZ = new float[2];
                int before = 0, after = 0;
                for (int i = 0; i < 2; i++)
                {
                    bool right = i == 1;
                    string name = right ? "BusStop_Right" : "BusStop_Left";
                    Transform existing = root.transform.Find(name);
                    GameObject model;
                    if (existing == null)
                    {
                        model = (GameObject)PrefabUtility.InstantiatePrefab(shelter, root.transform);
                        model.name = name;
                        model.transform.localPosition = new Vector3(right ? 7f : -7f, .2f, 12f);
                        model.transform.localRotation = Quaternion.Euler(0f, right ? -90f : 90f, 0f);
                        model.transform.localScale = Vector3.one * .5f;
                        foreach (Collider shape in model.GetComponentsInChildren<Collider>(true))
                            Object.DestroyImmediate(shape);
                    }
                    else model = existing.gameObject;
                    model.SetActive(true);
                    Renderer[] visuals = model.GetComponentsInChildren<Renderer>(true);
                    Bounds area = ShelterArea(visuals);
                    before += lampBounds.Count(lamp => HorizontalOverlap(lamp, area));
                    bool found = false;
                    // Keep the shelter near its vehicle while preserving the
                    // existing road lights. Try nearest positions first.
                    foreach (float offset in new[] { 0f, -1f, 1f, -2f, 2f, -3f, 3f, -4f, 4f, -5f, 5f, -6f, 6f, -7f, 7f })
                    {
                        Vector3 position = model.transform.localPosition;
                        position.z = 12f + offset;
                        model.transform.localPosition = position;
                        area = ShelterArea(visuals);
                        if (lampBounds.Any(lamp => HorizontalOverlap(lamp, area))) continue;
                        found = true;
                        break;
                    }
                    if (!found) throw new InvalidOperationException("No lamp-free shelter position: " + path);
                    stopZ[i] = model.transform.localPosition.z;
                    after += lampBounds.Count(lamp => HorizontalOverlap(lamp, area));
                    // Nature is combined in spatial chunks. Only those chunks
                    // whose horizontal footprint overlaps this shelter are hidden.
                    Renderer[] nature = all.Where(r => r.enabled &&
                        r.name.StartsWith("Combined_", StringComparison.Ordinal) &&
                        r.sharedMaterials.Any(m => m != null && m.name == "Natures") &&
                        r.bounds.max.x >= area.min.x && r.bounds.min.x <= area.max.x &&
                        r.bounds.max.z >= area.min.z && r.bounds.min.z <= area.max.z).ToArray();
                    string side = right ? "right" : "left";
                    serialized.FindProperty(side + "Shelter").objectReferenceValue = model;
                    SerializedProperty shapes = serialized.FindProperty(side + "Nature");
                    SerializedProperty defaults = serialized.FindProperty(side + "Defaults");
                    shapes.arraySize = defaults.arraySize = nature.Length;
                    for (int n = 0; n < nature.Length; n++)
                    {
                        shapes.GetArrayElementAtIndex(n).objectReferenceValue = nature[n];
                        defaults.GetArrayElementAtIndex(n).boolValue = nature[n].enabled;
                    }
                    counts[i] = nature.Length;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(model.transform);
                    model.SetActive(false);
                }
                serialized.ApplyModifiedPropertiesWithoutUndo();
                foreach (int side in new[] { -1, 1, 0 })
                {
                    slots.Configure(side);
                    if (buildings.Any(r => !r.enabled))
                        throw new InvalidOperationException("A building was hidden: " + path);
                }
                if (root.GetComponentsInChildren<Collider>(true).Length != 0)
                    throw new InvalidOperationException("Scenery must stay decorative: " + path);
                PrefabUtility.SaveAsPrefabAsset(root, path);
                rows.Add(new Row { path = path, leftNature = counts[0],
                    rightNature = counts[1], buildings = buildings.Length,
                    leftZ = stopZ[0], rightZ = stopZ[1], lampOverlapsBefore = before,
                    lampOverlapsAfter = after });
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        AssetDatabase.SaveAssets();
        Directory.CreateDirectory("Artifacts/CityTraffic");
        File.WriteAllText("Artifacts/CityTraffic/bus-stop-integration.json",
            JsonUtility.ToJson(new Report { rows = rows.ToArray() }, true));
        Debug.Log("City bus stops integrated into 10 building scenery prefabs; buildings retained.");
    }

    private static bool IsStreetLamp(Renderer renderer)
    {
        for (Transform t = renderer.transform; t != null; t = t.parent)
            if (t.name.StartsWith("Props_Street Light", StringComparison.Ordinal)) return true;
        return false;
    }

    private static Bounds ShelterArea(Renderer[] visuals)
    {
        Bounds area = GeometryBounds(visuals[0]);
        foreach (Renderer renderer in visuals) area.Encapsulate(GeometryBounds(renderer));
        area.Expand(new Vector3(1f, 0f, 1f));
        return area;
    }

    private static bool HorizontalOverlap(Bounds a, Bounds b) =>
        a.max.x >= b.min.x && a.min.x <= b.max.x &&
        a.max.z >= b.min.z && a.min.z <= b.max.z;

    private static Bounds GeometryBounds(Renderer renderer)
    {
        MeshFilter filter = renderer.GetComponent<MeshFilter>();
        if (filter == null || filter.sharedMesh == null) return renderer.bounds;
        Bounds local = filter.sharedMesh.bounds;
        Bounds result = new Bounds(renderer.transform.TransformPoint(local.center), Vector3.zero);
        for (int corner = 0; corner < 8; corner++)
            result.Encapsulate(renderer.transform.TransformPoint(local.center + Vector3.Scale(local.extents,
                new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1))));
        return result;
    }
}
