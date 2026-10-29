using System;
using System.Linq;
using EndlessRollball.Track;
using UnityEditor;
using UnityEngine;

// The editable Beach gap is the visual source; tiers retain their own jump geometry.
public sealed class BeachGapVisualSync : AssetPostprocessor
{
    private const string Folder = "Assets/EndlessRollballAssets/Environment Prefab/Roads/Gaps/";
    private const string Source = Folder + "TRK_Beach_GapSmall_24m.prefab";
    private static bool pending;

    [InitializeOnLoadMethod]
    private static void AfterReload() => Queue();

    private static void OnPostprocessAllAssets(string[] imported, string[] deleted,
        string[] moved, string[] oldPaths)
    {
        if (Array.IndexOf(imported, Source) >= 0) Queue();
    }

    private static void Queue()
    {
        if (pending) return;
        pending = true;
        EditorApplication.update += WhenReady;
    }

    private static void WhenReady()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode) return;
        EditorApplication.update -= WhenReady;
        pending = false;
        try { Synchronize(); }
        catch (Exception error) { Debug.LogException(error); }
    }

    [MenuItem("Tools/Endless Rollball/Sync Beach Gap Visuals")]
    public static void Synchronize()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Sync Beach gaps in Edit Mode.");
        GameObject source = PrefabUtility.LoadPrefabContents(Source);
        try
        {
            TrackSegment sourceTrack = source.GetComponent<TrackSegment>();
            RequireVisualRoot(sourceTrack);
            foreach (string tier in new[] { "Easy", "Normal", "Hard" })
            {
                string path = Folder + "Difficulty/TRK_Beach_Gap_" + tier + "_24m.prefab";
                GameObject target = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    TrackSegment track = target.GetComponent<TrackSegment>();
                    RequireVisualRoot(track);
                    GameObject visual = UnityEngine.Object.Instantiate(sourceTrack.VisualRoot.gameObject, target.transform);
                    visual.name = sourceTrack.VisualRoot.name;
                    foreach (string bank in new[] { "Takeoff", "Landing" })
                    {
                        BoxCollider from = source.GetComponentsInChildren<BoxCollider>(true).Single(c => c.name == "Floor_" + bank);
                        BoxCollider to = target.GetComponentsInChildren<BoxCollider>(true).Single(c => c.name == "Floor_" + bank);
                        string sectionName = bank == "Takeoff" ? "Road_Takeoff_0_to_9" : "Road_Landing_15_to_24";
                        Transform authored = sourceTrack.VisualRoot.GetComponentsInChildren<Transform>(true).Single(t => t.name == sectionName);
                        Transform section = visual.GetComponentsInChildren<Transform>(true).Single(t => t.name == sectionName);
                        MapTransform(authored, section, from, to, true);
                        string warningName = "Warning_" + bank + "_Edge";
                        Transform warningSource = sourceTrack.VisualRoot.GetComponentsInChildren<Transform>(true).Single(t => t.name == warningName);
                        Transform warning = visual.GetComponentsInChildren<Transform>(true).Single(t => t.name == warningName);
                        MapTransform(warningSource, warning, from, to, false);
                    }
                    GameObject previous = track.VisualRoot.gameObject;
                    var serialized = new SerializedObject(track);
                    serialized.FindProperty("presentationRoot").objectReferenceValue = visual;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    UnityEngine.Object.DestroyImmediate(previous);
                    PrefabUtility.SaveAsPrefabAsset(target, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(target); }
            }
            Debug.Log("Beach gap visuals synchronized to Easy, Normal and Hard. Tier floors, anchors and gap distances preserved.");
        }
        finally { PrefabUtility.UnloadPrefabContents(source); }
    }

    private static void RequireVisualRoot(TrackSegment track)
    {
        if (track == null || track.VisualRoot == null || track.VisualRoot == track.transform)
            throw new InvalidOperationException("Beach gap needs a dedicated RoadVisual root.");
    }

    private static void MapTransform(Transform authored, Transform result,
        BoxCollider from, BoxCollider to, bool stretchLength)
    {
        float lengthRatio = to.size.z / from.size.z;
        float widthRatio = to.size.x / from.size.x;
        Vector3 point = from.transform.InverseTransformPoint(authored.position);
        float fromStart = from.center.z - from.size.z * 0.5f;
        float toStart = to.center.z - to.size.z * 0.5f;
        point.z = toStart + (point.z - fromStart) * lengthRatio;
        point.x = to.center.x + (point.x - from.center.x) * widthRatio;
        point.y += to.center.y + to.size.y * 0.5f - from.center.y - from.size.y * 0.5f;
        result.position = to.transform.TransformPoint(point);
        result.rotation = to.transform.rotation * Quaternion.Inverse(from.transform.rotation) * authored.rotation;
        Vector3 scale = authored.localScale;
        scale.x *= widthRatio;
        if (stretchLength) scale.z *= lengthRatio;
        result.localScale = scale;
    }
}
