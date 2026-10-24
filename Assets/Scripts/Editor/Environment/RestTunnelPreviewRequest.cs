using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EndlessRollball.Editor
{
    // Inert unless this specific, short-lived authoring request exists outside Assets.
    internal static class RestTunnelPreviewRequest
    {
        const string Request = "Artifacts/RestTunnelPreview24m/CreatePreview.request";
        static double deadline;
        static string requestKind;

        [InitializeOnLoadMethod]
        static void Schedule()
        {
            if (!File.Exists(Request) || Application.isBatchMode) return;
            if (DateTime.UtcNow - File.GetLastWriteTimeUtc(Request) > TimeSpan.FromMinutes(15)) return;
            requestKind = File.ReadAllText(Request).Trim();
            if (requestKind != "rest-tunnel-24m-authoring-only" && requestKind != "rest-tunnel-24m-decoration-only" &&
                requestKind != "rest-tunnel-24m-nature-only" && requestKind != "rest-tunnel-24m-street-hedges-only" &&
                requestKind != "rest-tunnel-24m-export-prefab-only") return;
            deadline = EditorApplication.timeSinceStartup + 45;
            EditorApplication.update += Run;
        }

        static void Run()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                if (EditorApplication.timeSinceStartup < deadline) return;
            }
            EditorApplication.update -= Run;
            if (EditorApplication.isPlayingOrWillChangePlaymode ||
                SceneManager.GetActiveScene().path != "Assets/Scenes/EnvironmentPreview.unity")
            {
                File.WriteAllText(Request + ".blocked", "Open EnvironmentPreview in Edit Mode and use the Build Quiet Tunnel Preview 24m menu.");
                return;
            }
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty &&
                    ((requestKind != "rest-tunnel-24m-decoration-only" && requestKind != "rest-tunnel-24m-nature-only" &&
                      requestKind != "rest-tunnel-24m-street-hedges-only" && requestKind != "rest-tunnel-24m-export-prefab-only") ||
                     SceneManager.GetSceneAt(i) != SceneManager.GetActiveScene()))
                {
                    File.WriteAllText(Request + ".blocked", "Unsaved scene edits preserved. Save them, then use Tools/Endless Rollball/Environment/Build Quiet Tunnel Preview 24m.");
                    return;
                }
            try
            {
                File.Move(Request, Request + ".consumed_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff"));
                if (requestKind == "rest-tunnel-24m-export-prefab-only") RestTunnelPreviewBuilder.ExportPrefabFromRequest();
                else if (requestKind == "rest-tunnel-24m-street-hedges-only") RestTunnelPreviewBuilder.StreetHedgesFromRequest();
                else if (requestKind == "rest-tunnel-24m-nature-only") RestTunnelPreviewBuilder.NaturalizeFromRequest();
                else if (requestKind == "rest-tunnel-24m-decoration-only") RestTunnelPreviewBuilder.DecorateFromRequest();
                else RestTunnelPreviewBuilder.Build();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                File.WriteAllText(Request + ".failed", exception.ToString());
            }
        }
    }
}
