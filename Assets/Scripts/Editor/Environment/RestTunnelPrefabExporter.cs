using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace EndlessRollball.Editor
{
    public static partial class RestTunnelPreviewBuilder
    {
        const string TunnelPrefabFolder = "Assets/EndlessRollballAssets/Environment Prefab/TransitionTunnel";
        const string TunnelPrefabName = "ENV_TransitionTunnel_Scenery_24m";
        const string TunnelPrefabPath = TunnelPrefabFolder + "/" + TunnelPrefabName + ".prefab";

        [MenuItem("Tools/Endless Rollball/Environment/Save Approved Tunnel Scenery Prefab 24m")]
        public static void ExportPrefabFromRequest()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Open EnvironmentPreview in Edit Mode.");
            var source = scene.GetRootGameObjects().SingleOrDefault(item => item.name == RootName && item.activeSelf);
            if (!source || !source.transform.Find("07_StreetStyle_Hedges"))
                throw new InvalidOperationException("The approved tunnel with StreetCity hedges is required.");
            if (File.Exists(TunnelPrefabPath) || AssetDatabase.LoadMainAssetAtPath(TunnelPrefabPath))
                throw new InvalidOperationException("The destination prefab already exists; it was not overwritten.");
            if (Vector3.Distance(source.transform.localScale, Vector3.one) > .0001f ||
                Quaternion.Angle(source.transform.rotation, Quaternion.identity) > .01f)
                throw new InvalidOperationException("The tunnel root must retain unit scale and +Z orientation before export.");

            EnsureFolder(TunnelPrefabFolder);
            var temporary = EditorSceneManager.NewPreviewScene();
            GameObject copy = null;
            try
            {
                copy = Object.Instantiate(source);
                SceneManager.MoveGameObjectToScene(copy, temporary);
                copy.name = TunnelPrefabName;
                copy.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                copy.transform.localScale = Vector3.one;
                // Unpack ONLY the temporary copy; mesh and material assets remain shared.
                foreach (var transform in copy.GetComponentsInChildren<Transform>(true))
                    if (PrefabUtility.IsAnyPrefabInstanceRoot(transform.gameObject))
                        PrefabUtility.UnpackPrefabInstance(transform.gameObject, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

                RemoveExportOnly(copy, "05_ReviewOnly_Lighting_And_Scale");
                RemoveExportOnly(copy, "04_Previous_Wall_Lights_Inactive");
                var road = copy.transform.Find("02_Preview_Road_VisualOnly");
                if (road)
                {
                    var reviewFloor = road.Find("Flat_9m_Road_24m");
                    if (reviewFloor) Object.DestroyImmediate(reviewFloor.gameObject);
                    road.name = "02_Sidewalks_And_Route_Edges";
                }
                foreach (var component in copy.GetComponentsInChildren<Component>(true))
                {
                    if (!component) continue;
                    if (component is Collider || component is Rigidbody || component is Joint || component is Camera ||
                        component is AudioSource || component is AudioListener || component is Animator || component is Animation)
                        Object.DestroyImmediate(component);
                    else if (component is MonoBehaviour && component.GetType().FullName !=
                        "UnityEngine.Rendering.Universal.UniversalAdditionalLightData")
                        Object.DestroyImmediate(component);
                    else if (component is Light light && light.type == LightType.Directional)
                        Object.DestroyImmediate(component);
                }
                foreach (var transform in copy.GetComponentsInChildren<Transform>(true))
                    GameObjectUtility.RemoveMonoBehavioursWithMissingScript(transform.gameObject);

                var saved = PrefabUtility.SaveAsPrefabAsset(copy, TunnelPrefabPath);
                if (!saved) throw new IOException("Unity could not save the tunnel prefab.");
                var report = new StringBuilder();
                report.AppendLine("Prefab: " + TunnelPrefabPath);
                report.AppendLine("GUID: " + AssetDatabase.AssetPathToGUID(TunnelPrefabPath));
                report.AppendLine("Source: active " + RootName + " in " + ScenePath + "; scene not modified by export.");
                report.AppendLine("Root: identity transform, +Z forward, entrance Z0 / midpoint Z12 / exit Z24.");
                report.AppendLine("Includes sleeve, entrance architecture, sidewalks, guidance, approved StreetCity hedges and the current hanging lanterns.");
                report.AppendLine("Excludes review floor, scale sphere, review fill lights, inactive wall lamps, global presentation and gameplay components.");
                report.AppendLine("Renderers: " + saved.GetComponentsInChildren<Renderer>(true).Length);
                report.AppendLine("Local lights: " + saved.GetComponentsInChildren<Light>(true).Length);
                report.AppendLine("Colliders: " + saved.GetComponentsInChildren<Collider>(true).Length);
                report.AppendLine("Rigidbodies: " + saved.GetComponentsInChildren<Rigidbody>(true).Length);
                report.AppendLine("Use with an existing authoritative 24m gameplay track; this scenery prefab has no supporting road collider.");
                report.AppendLine("No gameplay scene, track, manager/config or pooling edits. No Play Mode, tests or profiling.");
                Directory.CreateDirectory(Output);
                File.WriteAllText(Output + "/PrefabExportRecord.txt", report.ToString());
                Selection.activeObject = saved;
                EditorGUIUtility.PingObject(saved);
                Debug.Log("REST_TUNNEL_PREFAB_SAVED: " + TunnelPrefabPath + ". EnvironmentPreview and gameplay unchanged.");
            }
            finally
            {
                if (copy) Object.DestroyImmediate(copy);
                EditorSceneManager.ClosePreviewScene(temporary);
            }
        }

        static void RemoveExportOnly(GameObject root, string name)
        {
            var child = root.transform.Find(name);
            if (child) Object.DestroyImmediate(child.gameObject);
        }
    }
}
