using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using EndlessRollball.Track;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace EndlessRollball.Editor
{
    // Scoped scene authoring only; request armed after preparation. Never enters Play Mode.
    internal static class SessionScoringWiring
    {
        const string ScenePath = "Assets/Scenes/GameplayScene.unity";
        const string Output = "Artifacts/SessionScoring";
        const string Request = Output + "/Wire.request";
        const string Token = "wire-session-scoring-existing-ui-v1";
        const string ExpectedHash = "2B987D42EE82710A57654B421F4B0107021BE9408BCDDD433E14D6FAE80FA255";
        static double deadline;

        [InitializeOnLoadMethod]
        static void Schedule()
        {
            if (Application.isBatchMode || !File.Exists(Request) || File.ReadAllText(Request).Trim() != Token ||
                DateTime.UtcNow - File.GetLastWriteTimeUtc(Request) > TimeSpan.FromMinutes(30)) return;
            deadline = EditorApplication.timeSinceStartup + 120;
            EditorApplication.update += Tick;
        }
        static void Tick()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                if (EditorApplication.timeSinceStartup < deadline) return;
                EditorApplication.update -= Tick;
                File.WriteAllText(Request + ".blocked", "Editor busy; use the menu after compilation.");
                return;
            }
            EditorApplication.update -= Tick;
            try { Wire(); }
            catch (Exception error) { File.WriteAllText(Request + ".failed", error.ToString()); Debug.LogException(error); }
        }

        [MenuItem("Tools/Endless Rollball/Gameplay/Wire Session Scoring And Existing UI")]
        static void Wire()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || PrefabStageUtility.GetCurrentPrefabStage() != null)
                throw new InvalidOperationException("Exit Play/Prefab Mode and save UI edits first.");
            string normalized = Regex.Replace(File.ReadAllText(ScenePath),
                @"(?m)^  taskTracker: \{fileID: 0\}\r?\n", "").Replace("\r", "");
            using (var sha = SHA256.Create())
                if (BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(normalized))).Replace("-", "") != ExpectedHash)
                    throw new InvalidOperationException("Newer GameplayScene edits preserved; re-inspect before wiring.");

            Scene previous = SceneManager.GetActiveScene();
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (!opened && scene.isDirty) throw new InvalidOperationException("Save GameplayScene before wiring.");
            if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            bool saved = false;
            try
            {
                GameplayRunController controller = One<GameplayRunController>(scene);
                TrackGenerator generator = One<TrackGenerator>(scene);
                ObstacleManager obstacles = One<ObstacleManager>(scene);
                BallMovement ball = One<BallMovement>(scene);
                PlayerHazardContact contact = One<PlayerHazardContact>(scene);
                Transform canvas = scene.GetRootGameObjects().Single(g => g.name == "Canvas").transform;
                Transform hud = Need(canvas, "GameplayHUD"), pause = Need(canvas, "PauseMenu"), result = Need(canvas, "SessionResultMenu");
                Transform performance = Need(result, "PerformancePanel");
                TMP_Text outcomeStyle = Need(result, "AvatarFeedbackPanel/Title").GetComponent<TMP_Text>();
                TMP_Text duration = Text(performance, "GameDurationText");
                TMP_Text navigation = Text(performance, "SafeNavigationText");
                TMP_Text navigationRatio = Text(performance, "SafeNavigationText/Text (TMP)");
                TMP_Text jumps = Text(performance, "JumpLandingText");
                TMP_Text jumpRatio = Text(performance, "JumpLandingText/Text (TMP)");
                TMP_Text overall = Text(performance, "TotalScoreText"), coins = Text(result, "EarnedCoinPanel/CoinEarnedText");
                TMP_Text hudScore = Need(hud, "CurrentScore").GetComponentInChildren<TMP_Text>(true);
                TMP_Text hudDuration = Need(hud, "GameDuration").GetComponentInChildren<TMP_Text>(true);
                TMP_Text hudMode = Need(hud, "GameMode").GetComponentInChildren<TMP_Text>(true);
                TMP_Text hudDifficulty = Need(hud, "GameDifficulty").GetComponentInChildren<TMP_Text>(true);
                Button pauseButton = Need(hud, "PauseButton").GetComponent<Button>();
                Button resumeButton = Need(pause, "PausePanel/ResumeButton").GetComponent<Button>();
                Button endButton = Need(pause, "PausePanel/EndSessionButton").GetComponent<Button>();
                Button settingsButton = Need(pause, "PausePanel/SettingsButton").GetComponent<Button>();
                Button menuButton = Need(result, "ReturnButton").GetComponent<Button>();
                var cameraFollow = scene.GetRootGameObjects().Single(g => g.name == "CameraPos")
                    .GetComponents<MonoBehaviour>().Single(c => c != null && c.GetType().Name == "CameraFollow");
                if (new UnityEngine.Object[] { duration, navigation, navigationRatio, jumps, jumpRatio, overall, coins,
                    hudScore, hudDuration, hudMode, hudDifficulty, outcomeStyle, pauseButton, resumeButton, endButton,
                    settingsButton, menuButton, cameraFollow }.Any(o => o == null) || contact.gameObject != ball.gameObject ||
                    ball.GetComponent<SphereCollider>() == null || controller.GetComponent<SessionTaskTracker>() != null ||
                    canvas.GetComponent<SessionFlowUI>() != null)
                    throw new InvalidOperationException("UI structure/components changed; preserve and review current scene.");
                var run = new SerializedObject(controller);
                if (run.FindProperty("ballMovement").objectReferenceValue != ball ||
                    run.FindProperty("trackGenerator").objectReferenceValue != generator)
                    throw new InvalidOperationException("Preserve/review the existing controller references.");

                string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
                string backup = Output + "/BeforeWiring_" + stamp;
                Directory.CreateDirectory(backup);
                File.Copy(ScenePath, backup + "/GameplayScene.unity");
                File.Copy(ScenePath + ".meta", backup + "/GameplayScene.unity.meta");
                if (File.Exists(Request)) File.Move(Request, Request + ".consumed_" + stamp);

                SessionTaskTracker tracker = controller.gameObject.AddComponent<SessionTaskTracker>();
                SessionFlowUI ui = canvas.gameObject.AddComponent<SessionFlowUI>();
                TMP_Text outcome = NewText(Need(result, "AvatarFeedbackPanel"), "OutcomeText", outcomeStyle,
                    new Vector2(.06f, .66f), new Vector2(.94f, .84f), 30f);
                outcome.text = "Session ended";
                TMP_Text formula = NewText(overall.transform, "ScoreFormulaText", overall,
                    new Vector2(0f, -.48f), new Vector2(1f, 0f), 18f);
                formula.text = "60% Safe Navigation + 40% Jump and Landing";

                Assign(tracker, ("ballMovement", ball), ("runController", controller));
                Assign(controller, ("hazardContact", contact), ("sessionUI", ui),
                    ("gameplayCameraFollow", cameraFollow), ("taskTracker", tracker));
                Assign(generator, ("taskTracker", tracker));
                Assign(obstacles, ("taskTracker", tracker));
                Assign(contact, ("runController", controller), ("obstacleManager", obstacles));
                Assign(ui, ("pauseButton", pauseButton.gameObject), ("pausePanel", pause.gameObject),
                    ("resultPanel", result.gameObject), ("outcomeText", outcome), ("durationText", duration),
                    ("gameplayHUD", hud.gameObject), ("hudScoreText", hudScore), ("hudDurationText", hudDuration),
                    ("hudModeText", hudMode), ("hudDifficultyText", hudDifficulty),
                    ("navigationPercentText", navigation), ("navigationRatioText", navigationRatio),
                    ("jumpPercentText", jumps), ("jumpRatioText", jumpRatio), ("overallText", overall),
                    ("coinsText", coins), ("scoreFormulaText", formula));
                Connect(pauseButton, controller.PauseSession);
                Connect(resumeButton, controller.ResumeSession);
                Connect(endButton, controller.EndSessionFromPause);
                Connect(menuButton, ui.ReturnToMainMenu);
                settingsButton.interactable = false; // No Settings handler/UI has been implemented.
                EditorUtility.SetDirty(settingsButton);
                foreach (TMP_Text label in new[] { hudScore, hudDuration, hudMode, hudDifficulty,
                    navigation, navigationRatio, jumps, jumpRatio, overall, coins })
                {
                    // Keep all authored positions/colours; fit longer live values within their existing rectangles.
                    label.enableAutoSizing = true;
                    label.fontSizeMax = label.fontSize;
                    label.fontSizeMin = Mathf.Min(label.fontSize, 18f);
                    EditorUtility.SetDirty(label);
                }
                canvas.gameObject.SetActive(true);
                hud.gameObject.SetActive(true);
                pause.gameObject.SetActive(false);
                result.gameObject.SetActive(false);
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save wired GameplayScene; backup retained.");
                saved = true;
                var report = new StringBuilder("SESSION_SCORING_AND_UI_WIRED\nBackup: " + backup + "\n");
                report.AppendLine("Existing UI layout/background/fonts/colours retained; outcome/formula TMP labels added and live labels auto-fit.");
                report.AppendLine("Pause -> PauseSession; Resume -> ResumeSession; End Session -> EndSessionFromPause; Return -> ReturnToMainMenu. Settings disabled pending real settings UI.");
                report.AppendLine("All controller/contact/tracker/generator/obstacle/UI references assigned. Initial results/pause inactive; HUD active.");
                report.AppendLine("Actual encountered tasks only; 12m approach planes; clearance + own-floor landing; accepted jump and airborne evidence; cancelled interrupted tasks excluded.");
                report.AppendLine("Chapter4 decimal 60/40, unavailable denominators, full-precision ceiling(H/2) candidate; no account credit or persistence.");
                report.AppendLine("No obstacle/road/scenery prefab edits, difficulty/physics/random changes, new vehicles, Play Mode/tests/profiling or sphere-break animation.");
                File.WriteAllText(Output + "/WiringReport.txt", report.ToString());
                Debug.Log("SESSION_SCORING_AND_UI_WIRED: " + Output + "/WiringReport.txt");
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                if (opened && scene.IsValid() && scene.isLoaded && (saved || !scene.isDirty))
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        static T One<T>(Scene scene) where T : Component => scene.GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<T>(true)).Single();
        static Transform Need(Transform root, string path) => root.Find(path) ??
            throw new InvalidOperationException("Missing inspected UI object: " + root.name + "/" + path);
        static TMP_Text Text(Transform root, string path) => Need(root, path).GetComponent<TMP_Text>();
        static void Assign(UnityEngine.Object target, params (string name, UnityEngine.Object value)[] entries)
        {
            var serialized = new SerializedObject(target);
            foreach (var entry in entries)
            {
                SerializedProperty property = serialized.FindProperty(entry.name) ??
                    throw new InvalidOperationException("Missing compiled property: " + entry.name);
                property.objectReferenceValue = entry.value;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }
        static void Connect(Button button, UnityAction action)
        {
            for (int i = button.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
                UnityEventTools.RemovePersistentListener(button.onClick, i);
            UnityEventTools.AddPersistentListener(button.onClick, action);
            EditorUtility.SetDirty(button);
        }
        static TMP_Text NewText(Transform parent, string name, TMP_Text style,
            Vector2 min, Vector2 max, float size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = min; rect.anchorMax = max;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            TMP_Text text = go.GetComponent<TMP_Text>();
            text.font = style.font;
            text.fontSharedMaterial = style.fontSharedMaterial;
            text.color = style.color;
            text.fontSize = size;
            text.alignment = TextAlignmentOptions.Left;
            text.raycastTarget = false;
            text.enableAutoSizing = true;
            text.fontSizeMin = 12f; text.fontSizeMax = size;
            return text;
        }
    }
}
