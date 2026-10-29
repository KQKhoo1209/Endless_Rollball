using System;
using System.Linq;
using EndlessRollball.Track;
using UnityEditor;
using UnityEngine;

// Keeps independently authored difficulty collision surfaces while refreshing source visuals.
public sealed class CityGapVisualSync : AssetPostprocessor
{
    private const string Folder = "Assets/EndlessRollballAssets/Environment Prefab/Roads/Gaps/";
    private const string Source = Folder + "TRK_Street_GapSmall_24m.prefab";
    private static bool queued;

    private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] oldPaths)
    {
        if (Array.IndexOf(imported, Source) < 0 || queued) return;
        queued = true;
        EditorApplication.delayCall += () => {
            queued = false;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            Synchronize();
        };
    }

    [MenuItem("Tools/Endless Rollball/Sync City Gap Visuals")]
    public static void Synchronize()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Sync City gaps in Edit Mode.");
        GameObject source = PrefabUtility.LoadPrefabContents(Source);
        try
        {
            TrackSegment sourceTrack = source.GetComponent<TrackSegment>();
            if (sourceTrack == null || sourceTrack.VisualRoot == source.transform)
                throw new InvalidOperationException("City gap needs a dedicated visual root.");
            foreach (string tier in new[] { "Easy", "Normal", "Hard" })
            {
                string path = Folder + "Difficulty/TRK_Street_Gap_" + tier + "_24m.prefab";
                GameObject target = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    TrackSegment track = target.GetComponent<TrackSegment>();
                    if (track == null || track.VisualRoot == target.transform)
                        throw new InvalidOperationException("Difficulty gap needs a dedicated visual root.");
                    GameObject visual = UnityEngine.Object.Instantiate(sourceTrack.VisualRoot.gameObject, target.transform);
                    visual.name = sourceTrack.VisualRoot.name;
                    foreach (string bank in new[] { "Takeoff", "Landing" })
                    {
                        BoxCollider sourceFloor = source.GetComponentsInChildren<BoxCollider>(true).Single(c => c.name == "Floor_" + bank);
                        BoxCollider targetFloor = target.GetComponentsInChildren<BoxCollider>(true).Single(c => c.name == "Floor_" + bank);
                        string sectionName = bank == "Takeoff" ? "Road_Takeoff_0_to_9" : "Road_Landing_15_to_24";
                        Transform section = visual.GetComponentsInChildren<Transform>(true).Single(t => t.name == sectionName);
                        Vector3 scale = section.localScale;
                        scale.z *= targetFloor.size.z / sourceFloor.size.z;
                        section.localScale = scale;
                        // The landing mesh runs backwards from the exit. Its floor runs
                        // forwards from the lip, so the two origins are different.
                        bool landing = bank == "Landing";
                        float surfaceY = targetFloor.center.y + targetFloor.size.y * 0.5f;
                        float startZ = targetFloor.center.z - targetFloor.size.z * 0.5f;
                        float endZ = startZ + targetFloor.size.z;
                        section.position = targetFloor.transform.TransformPoint(
                            new Vector3(0f, surfaceY, landing ? endZ : startZ));
                        section.rotation = targetFloor.transform.rotation *
                            (landing ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity);

                        Transform warning = visual.GetComponentsInChildren<Transform>(true)
                            .Single(t => t.name == "Warning_" + bank + "_Edge");
                        warning.position = targetFloor.transform.TransformPoint(new Vector3(
                            0f, surfaceY + 0.02f, landing ? startZ + 0.2f : endZ - 0.2f));
                        warning.rotation = targetFloor.transform.rotation;
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
            Debug.Log("City gap visuals synchronized. Difficulty floors, anchors and gap distances preserved.");
        }
        finally { PrefabUtility.UnloadPrefabContents(source); }
    }
}
