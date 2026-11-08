using System;
using UnityEngine;
using UnityEngine.InputSystem;
using EndlessRollball.Track;
using System.Collections;

namespace EndlessRollball
{
    [RequireComponent(typeof(KeyboardGameplayInput))]
    public sealed class CloudPlatformEvent : MonoBehaviour
    {
        [Header("Music Difficulty")]
        [SerializeField] private DifficultyMode difficulty = DifficultyMode.Easy;
        [SerializeField] private bool useSessionDifficulty = true;
        [SerializeField] private SoundManager soundManager;
        [Header("Cloud Platforms")]
        [SerializeField] private Vector3 platformSize = new Vector3(2.6f, .65f, 2.4f);
        [SerializeField, Min(.1f)] private float ballRadius = .5f;
        [SerializeField, Min(1f)] private float recoveryDepth = 4f;
        [SerializeField, Min(30f)] private float cameraViewDistance = 90f;
        [Header("Gameplay Scene Integration")]
        [SerializeField] private bool useGameplaySphere;
        [SerializeField] private BallMovement gameplaySphere;
        [SerializeField] private CloudBouncePlatform platformPrefab;
        [SerializeField] private GameplayRunController runController;
        [Header("Stationary Cloud Blanket")]
        [SerializeField] private ParticleSystem cloudFog;
        [SerializeField] private float fogBelowPlatforms = 12f;
        [SerializeField] private float fogWidth = 60f;
        [SerializeField] private float fogThickness = 5f;
        [SerializeField] private float fogEndPadding = 40f;

        private bool physicalCourseReady;
        private int lastScoredCloud;
        [SerializeField, Min(.3f)] private float hardMinimumHopSeconds=.5f;

        private readonly CloudMusicChart[] charts = new CloudMusicChart[3 * CloudSongRotation.SongsPerDifficulty];
        private CloudMusicChart currentChart;
        private int songIndex;
        private static CloudSongRotation songRotation = new CloudSongRotation(Environment.TickCount);
        private Transform[] platforms;
        private Transform ball;
        private Camera eventCamera;
        private KeyboardGameplayInput input;
        private CloudMusicSimulation simulation;
        private Vector3 origin;
        private bool finished, preparing;
        private float edgeBevel, cornerRadius;
        private string loadError;
        private Material platformMaterial, ballMaterial;
        private Mesh platformMesh;
        private Texture2D cloudTexture;
        private GUIStyle hudStyle, creditStyle;
        private readonly Vector3 cameraOffset = new Vector3(0f, 7f, -9f);
        public event Action Completed;
        public event Action ReturnRequested;
        public DifficultyMode Difficulty => difficulty;
        public bool IsReady => useGameplaySphere ? physicalCourseReady : simulation != null;
        public string PreparationError => loadError;
        public double ActiveSeconds => simulation?.Time ?? 0d;
        private bool embedded, entryHeld;
        private bool physicalRecoveryEnabled;
        private bool physicalRecoveryArmed;
        private float firstCloudRecoveryTime;
        private bool descendingToStreet;
        private Collider pendingStreetSupport;
        private float physicalLaunchTime;

        // Called from sceneLoaded, before Start prepares the course.
        public void ConfigureEmbedded(DifficultyMode selectedDifficulty)
        {
            embedded = entryHeld = true;
            useSessionDifficulty = false;
            difficulty = selectedDifficulty == DifficultyMode.Endless ? DifficultyMode.Normal : selectedDifficulty;
            transform.position += Vector3.up * 2000f;
        }

        public void ResumeAfterEntry()
        {
            entryHeld = false;
            if (soundManager.IsPaused) soundManager.ResumeMusic();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSongRotation() => songRotation = new CloudSongRotation(Environment.TickCount);

        private void SelectNextSong()
        {
            int level = Mathf.Clamp((int)difficulty, 0, 2);
            difficulty = (DifficultyMode)level;
            songIndex = songRotation.Next(level);
            currentChart = charts[level * CloudSongRotation.SongsPerDifficulty + songIndex];
            if(useGameplaySphere && level==2)currentChart=SpaceHardChart(currentChart);
        }

        private CloudMusicChart SpaceHardChart(CloudMusicChart source)
        {
            // Keep the full duration and ending, retaining playable notes at wider intervals.
            var chart=JsonUtility.FromJson<CloudMusicChart>(JsonUtility.ToJson(source));
            var indices=new System.Collections.Generic.List<int>{0};
            float speed=gameplaySphere!=null && gameplaySphere.ForwardSpeed>.1f?gameplaySphere.ForwardSpeed:source.forwardSpeed;
            float minimum=Mathf.Max(hardMinimumHopSeconds,(platformSize.z+ballRadius*2f+.5f)/Mathf.Max(.1f,speed));
            int final=source.LastPlayableIndex;
            for(int i=1;i<final;i++)if(source.times[i]-source.times[indices[indices.Count-1]]>=minimum && source.times[final]-source.times[i]>=minimum)indices.Add(i);
            indices.Add(final);
            for(int i=final+1;i<source.times.Length;i++)indices.Add(i);
            chart.times=new float[indices.Count];chart.lanes=new int[indices.Count];chart.chords=new CloudPianoChord[indices.Count];
            for(int i=0;i<indices.Count;i++){
                int selected=indices[i];chart.times[i]=source.times[selected];chart.chords[i]=source.chords[selected];
                chart.lanes[i]=i==0?0:Mathf.Clamp(source.lanes[selected],chart.lanes[i-1]-1,chart.lanes[i-1]+1);
            }
            return chart;
        }

        private void Start()
        {
            double preparationStarted = Time.realtimeSinceStartupAsDouble;
            input = GetComponent<KeyboardGameplayInput>();
            origin = transform.position;
            try
            {
                string[] names = { "Easy", "Normal", "Hard" };
                for (int i = 0; i < names.Length; i++)
                {
                    for (int song = 0; song < CloudSongRotation.SongsPerDifficulty; song++)
                    {
                        string resource = names[i] + (song == 0 ? string.Empty : "_" + (song + 1));
                        TextAsset data = Resources.Load<TextAsset>("CloudMusic/" + resource);
                        if (data == null) throw new InvalidOperationException("Missing " + resource + " landing chart.");
                        int index = i * CloudSongRotation.SongsPerDifficulty + song;
                        charts[index] = JsonUtility.FromJson<CloudMusicChart>(data.text);
                        charts[index].ValidatePiano();
                        if (charts[index].difficulty != i) throw new InvalidOperationException("Wrong difficulty in " + resource);
                    }
                }
                if (useSessionDifficulty && GameSessionSettings.SelectedDifficulty.HasValue)
                    difficulty = GameSessionSettings.SelectedDifficulty.Value;
                if (difficulty == DifficultyMode.Endless) difficulty = DifficultyMode.Normal;
                SelectNextSong();
                BuildPresentation();
                if (soundManager == null) soundManager = GetComponent<SoundManager>();
                if (soundManager == null) throw new InvalidOperationException("Assign the scene SoundManager.");
                soundManager.PreparePiano();
                EndlessRollball.Infrastructure.RuntimeLog.Info($"[Loading] Cloud charts/presentation/audio preparation: {Time.realtimeSinceStartupAsDouble-preparationStarted:F3}s.", this);
                preparing = true;
            }
            catch (Exception exception)
            {
                loadError = exception.Message;
                Debug.LogError("Cloud event preparation failed: " + loadError, this);
            }
        }

        private void BuildPresentation()
        {
            if (useGameplaySphere)
            {
                BuildPhysicalPresentation();
                return;
            }

            eventCamera = Camera.main;
            if (eventCamera == null)
            {
                var cameraObject = new GameObject("Cloud Camera", typeof(Camera), typeof(AudioListener));
                cameraObject.transform.SetParent(transform, false);
                eventCamera = cameraObject.GetComponent<Camera>();
            }
            eventCamera.farClipPlane = cameraViewDistance;
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            if (shader == null) throw new InvalidOperationException("Cloud event requires a Lit shader.");
            platformMaterial = new Material(shader) { color = Color.white, enableInstancing = true };
            cloudTexture = CloudPlatformVisuals.CreateTexture();
            platformMaterial.mainTexture = cloudTexture;
            platformMaterial.SetFloat("_Smoothness", .12f);
            platformMaterial.SetFloat("_Metallic", 0f);
            edgeBevel = Mathf.Min(.16f, platformSize.y * .3f);
            cornerRadius = Mathf.Min(.45f, Mathf.Min(platformSize.x, platformSize.z) * .2f);
            platformMesh = CloudPlatformVisuals.CreateMesh(platformSize, edgeBevel, cornerRadius);
            ballMaterial = new Material(shader) { color = new Color(1f, .45f, .12f) };
            int count = 0;
            foreach (CloudMusicChart chart in charts) count = Mathf.Max(count, chart.times.Length);
            // Prepare enough objects for the longest chart once. Difficulty changes and
            // restarts only reposition/reuse them; no creation occurs during playback.
            platforms = new Transform[count];
            for (int i = 0; i < count; i++)
            {
                var platform = new GameObject("Cloud Platform " + (i + 1), typeof(MeshFilter), typeof(MeshRenderer));
                platform.transform.SetParent(transform, false);
                platform.GetComponent<MeshFilter>().sharedMesh = platformMesh;
                platform.GetComponent<Renderer>().sharedMaterial = platformMaterial;
                platform.SetActive(false);
                platforms[i] = platform.transform;
            }
            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "Cloud Ball";
            sphere.transform.SetParent(transform, false);
            sphere.transform.localScale = Vector3.one * (2f * ballRadius);
            sphere.GetComponent<Renderer>().sharedMaterial = ballMaterial;
            sphere.GetComponent<Collider>().enabled = false;
            ball = sphere.transform;
        }

        private void Update()
        {
            if (preparing)
            {
                if (soundManager.PreparationError != null)
                {
                    preparing = false;
                    loadError = soundManager.PreparationError;
                    Debug.LogError(loadError, this);
                    return;
                }
                if (!soundManager.IsReady) return;
                preparing = false;
                ResetEvent();
            }

            if (useGameplaySphere)
            {
                if (runController != null && soundManager != null) {
                    bool paused=runController.State==SessionFlowState.Paused || Time.timeScale<=0f;
                    if(paused && !soundManager.IsPaused)soundManager.PauseMusic();
                    else if(!paused && soundManager.IsPaused && runController.State==SessionFlowState.SpecialEvent)soundManager.ResumeMusic();
                }
                UpdatePhysicalRecovery();
                return;
            }

            if (simulation == null) return;
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (!embedded)
                {
                    if (keyboard.digit1Key.wasPressedThisFrame) SetDifficulty(DifficultyMode.Easy);
                    if (keyboard.digit2Key.wasPressedThisFrame) SetDifficulty(DifficultyMode.Normal);
                    if (keyboard.digit3Key.wasPressedThisFrame) SetDifficulty(DifficultyMode.Hard);
                    if (keyboard.rKey.wasPressedThisFrame) ResetEvent();
                    if (keyboard.nKey.wasPressedThisFrame) SetDifficulty(difficulty);
                }
                if (!entryHeld && (keyboard.pKey.wasPressedThisFrame || (embedded && keyboard.escapeKey.wasPressedThisFrame))) TogglePause();
            }
            if (soundManager.IsPaused || finished || !soundManager.HasStarted) return;
            simulation.AdvanceTo(soundManager.ElapsedSeconds, input.ReadCommand().Steering);
            ApplyBallPosition();
            if (simulation.Complete)
            {
                finished = true;
                soundManager.StopMusic();
                Completed?.Invoke();
            }
        }

        private void UpdatePhysicalRecovery()
        {
            if (!physicalRecoveryEnabled ||
                gameplaySphere == null ||
                Time.timeScale <= 0f)
                return;

            Rigidbody body = gameplaySphere.GetComponent<Rigidbody>();

            if (body == null ||
                body.constraints == RigidbodyConstraints.FreezeAll)
                return;

            float landingHeight =
                origin.y + platformSize.y * .5f +
                gameplaySphere.SphereRadius;

            // Allow the initial ascent before treating a fall as a miss.
            if (body.position.y >= landingHeight ||
                Time.time >= firstCloudRecoveryTime)
            {
                physicalRecoveryArmed = true;
            }

            if (!physicalRecoveryArmed ||
                body.position.y >= landingHeight - recoveryDepth)
                return;

            int target = currentChart.LastPlayableIndex;

            // Recover onto an upcoming cloud.
            for (int i = 0; i <= currentChart.LastPlayableIndex; i++)
            {
                if (platforms[i].position.z > body.position.z + .5f)
                {
                    target = i;
                    break;
                }
            }

            BoxCollider platform =
                platforms[target].GetComponent<BoxCollider>();

            Vector3 position = platform.bounds.center;
            position.y =
                platform.bounds.max.y +
                gameplaySphere.SphereRadius + .15f;

            // The recovery placement is not evidence of a successful navigation.
            int failed=Mathf.Clamp(target-1,1,currentChart.LastPlayableIndex);
            for(int missed=lastScoredCloud+1;missed<=failed;missed++)runController.Tasks.RecordEventNavigation(missed,false);
            if(failed<=lastScoredCloud)runController.Tasks.RecordEventNavigation(lastScoredCloud+1,false);
            lastScoredCloud=Mathf.Max(lastScoredCloud,failed);
            body.position = position;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.WakeUp();

            EndlessRollball.Infrastructure.RuntimeLog.Info($"[CloudRecovery] Recovered onto {platforms[target].name}");
        }

        public void SetDifficulty(DifficultyMode value)
        {
            difficulty = value == DifficultyMode.Endless ? DifficultyMode.Normal : value;
            if (ball == null) return;
            SelectNextSong();
            if (useGameplaySphere) BuildPhysicalPresentation();
            ReleaseSimulation();
            loadError = null;
            try
            {
                soundManager.PreparePiano();
                preparing = true;
            }
            catch (Exception exception)
            {
                preparing = false;
                loadError = exception.Message;
                Debug.LogError(loadError, this);
            }
        }

        private void BuildPhysicalPresentation()
        {
            if (gameplaySphere == null)
                throw new InvalidOperationException(
                    "Assign the existing gameplay sphere.");

            if (platformPrefab == null)
                throw new InvalidOperationException(
                    "Assign the CloudBouncePlatform prefab.");

            int capacity = 0;

            foreach (CloudMusicChart chart in charts)
                if (chart.difficulty == (int)difficulty)
                    capacity = Mathf.Max(capacity, chart.times.Length);

            int existing = platforms == null ? 0 : platforms.Length;
            if (capacity > existing) System.Array.Resize(ref platforms, capacity);

            for (int i = existing; i < capacity; i++)
            {
                CloudBouncePlatform platform =
                    Instantiate(platformPrefab, transform);

                platform.FinalLanded += HandleFinalCloudLanding;
                platform.Landed += HandlePhysicalPlatformLanding;

                platform.name = "Cloud Platform " + (i + 1);
                platform.gameObject.SetActive(false);
                platforms[i] = platform.transform;
            }

            // Reference the real player. Do not create another sphere.
            ball = gameplaySphere.transform;
        }

        public bool TryPreparePhysicalLaunch(BallMovement player, float launchHeight)
        {
            if (!useGameplaySphere || !physicalCourseReady ||
                player != gameplaySphere || runController == null)
                return false;

            Rigidbody body = player.GetComponent<Rigidbody>();
            float gravity = -Physics.gravity.y;

            if (body == null || body.isKinematic ||
                !body.useGravity || gravity <= 0f)
                return false;

            // Put the first cloud below the launch's maximum height.
            float rise = launchHeight * .75f;
            float launchSpeed = Mathf.Sqrt(2f * gravity * launchHeight);

            // Use the descending crossing of the cloud's height.
            float landingTime =
                (launchSpeed + Mathf.Sqrt(
                    launchSpeed * launchSpeed - 2f * gravity * rise))
                / gravity;

            BoxCollider firstCollider =
                platforms[0].GetComponent<BoxCollider>();

            if (firstCollider == null)
                return false;

            if (!runController.TryBeginSpecialEvent(
                    keepPhysicsRunning: true))
                return false;

            if (!runController.Generator.BeginCloudRoadContinuation())
            {
                runController.EndSpecialEvent(0d);
                return false;
            }

            float surfaceOffset =
                firstCollider.center.y + firstCollider.size.y * .5f;

            origin = new Vector3(
                player.Corridor.CentreX,
                body.position.y + rise -
                    player.SphereRadius - surfaceOffset,
                body.position.z + player.ForwardSpeed * landingTime);

            ResetEvent();

            finished = false;
            soundManager.StartPhysicalCloudEvent(difficulty, currentChart);

            // A broad entry cloud catches players from every Street lane.
            float entryScale = Mathf.Max(
                1f,
                player.Corridor.Width / firstCollider.size.x + 1f);

            platforms[0].localScale =
                new Vector3(entryScale, 1f, 1f);

            for (int i = 0; i <= currentChart.LastPlayableIndex; i++)
                platforms[i].gameObject.SetActive(true);

            PositionCloudFog();

            // Match the speed used in the landing-position calculation.
            Vector3 velocity = body.linearVelocity;
            velocity.z = player.ForwardSpeed;
            body.linearVelocity = velocity;
            physicalRecoveryEnabled = true;
            physicalRecoveryArmed = false;
            firstCloudRecoveryTime = Time.time + landingTime + .5f;
            lastScoredCloud=0;
            physicalLaunchTime = Time.time;
            player.SetInputContext(EndlessRollball.Imu.ImuControlContext.Cloud);
            runController.GetComponent<GameSequenceCoordinator>()?.Notify(GameSequenceCoordinator.SequencePhase.Event);
            physicalFinishRequested = false;
            descendingToStreet = false;
            pendingStreetSupport = null;

            EndlessRollball.Infrastructure.RuntimeLog.Info(
                $"Cloud launch ready. First landing in approximately " +
                $"{landingTime:F2} seconds.",
                this);

            return true;
        }

        private bool physicalFinishRequested;

        private void HandleFinalCloudLanding(Rigidbody body)
        {
            if (!useGameplaySphere ||
                body != gameplaySphere.GetComponent<Rigidbody>() ||
                physicalFinishRequested)
                return;

            physicalFinishRequested = true;
            physicalRecoveryEnabled = false;
            descendingToStreet = true;
            runController.GetComponent<GameSequenceCoordinator>()?.Notify(GameSequenceCoordinator.SequencePhase.Return);

            gameplaySphere.SetCloudDescent(false);
            gameplaySphere.DiscardBufferedInput();
            gameplaySphere.GroundContact += HandleStreetLanding;

            if (cloudFog != null)
            {
                cloudFog.Stop(
                    true,
                    ParticleSystemStopBehavior.StopEmitting);
            }
        }

        private void HandlePhysicalPlatformLanding(CloudBouncePlatform platform, Rigidbody body)
        {
            if (!useGameplaySphere || finished ||
                body != gameplaySphere.GetComponent<Rigidbody>())
                return;

            int index = Array.IndexOf(platforms, platform.transform);

            if (index > 0) {
                for(int missed=lastScoredCloud+1;missed<index;missed++)runController.Tasks.RecordEventNavigation(missed,false);
                runController.Tasks.RecordEventNavigation(index,true);
                lastScoredCloud=Mathf.Max(lastScoredCloud,index);
            }
            if (index >= 0) soundManager.PlayPhysicalLandingNotes(index);
        }

        public void ResetEvent()
        {
            if (ball == null || preparing) return;
            if (currentChart == null || currentChart.difficulty != Mathf.Clamp((int)difficulty, 0, 2)) SelectNextSong();
            CloudMusicChart chart = currentChart;
            soundManager.StopMusic();
            float courseSpeed = useGameplaySphere ? gameplaySphere.ForwardSpeed : chart.forwardSpeed;
            for (int i = 0; i < platforms.Length; i++)
            {
                bool active = i <= chart.LastPlayableIndex;
                platforms[i].gameObject.SetActive(active && !useGameplaySphere);
                if (active)
                {
                    platforms[i].localScale = i == chart.LastPlayableIndex ? new Vector3(1.5f, 1f, 1.5f) : Vector3.one;
                    platforms[i].position = origin + new Vector3(chart.lanes[i] * chart.laneSpacing, 0f, chart.times[i] * courseSpeed);
                    if (useGameplaySphere)
                    {
                        bool isFinal = i == chart.LastPlayableIndex;

                        BoxCollider next = isFinal
                            ? null
                            : platforms[i + 1].GetComponent<BoxCollider>();

                        float duration = isFinal
                            ? 0f
                            : chart.times[i + 1] - chart.times[i];

                        platforms[i]
                            .GetComponent<CloudBouncePlatform>()
                            .ConfigureHop(duration, next, isFinal);
                    }
                }
            }

            if (useGameplaySphere)
            {
                physicalCourseReady = true;

                EndlessRollball.Infrastructure.RuntimeLog.Info(
                    $"Cloud course prepared: {chart.title}, " +
                    $"{chart.LastPlayableIndex + 1} platforms.",
                    this);

                return;
            }

            ReleaseSimulation();
            simulation = new CloudMusicSimulation(chart, platformSize.x * .5f - edgeBevel,
                platformSize.z * .5f - edgeBevel, cornerRadius, recoveryDepth);
            simulation.Landed += soundManager.PlayLandingNotes;
            simulation.Missed += soundManager.SilenceMissedPlatform;
            finished = false;
            soundManager.StartCloudEvent(difficulty, chart);
            if (entryHeld) soundManager.PauseMusic();
            ApplyBallPosition();
        }

        private void TogglePause()
        {
            if (finished || simulation == null) return;
            if (soundManager.IsPaused) soundManager.ResumeMusic();
            else soundManager.PauseMusic();
        }

        private void ApplyBallPosition()
        {
            ball.position = origin + new Vector3(simulation.X,
                platformSize.y * .5f + ballRadius + simulation.Height, simulation.Z);
        }

        private void PositionCloudFog()
        {
            if (cloudFog == null) return;

            float firstZ = platforms[0].position.z;
            float lastZ =
                platforms[currentChart.LastPlayableIndex].position.z;

            float length =
                lastZ - firstZ + fogEndPadding * 2f;

            cloudFog.Stop(
                true,
                ParticleSystemStopBehavior.StopEmittingAndClear);

            cloudFog.transform.SetPositionAndRotation(
                new Vector3(
                    origin.x,
                    origin.y - fogBelowPlatforms,
                    (firstZ + lastZ) * .5f),
                Quaternion.identity);

            var shape = cloudFog.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.position = Vector3.zero;
            shape.rotation = Vector3.zero;
            shape.scale = new Vector3(
                fogWidth,
                fogThickness,
                length);

            cloudFog.gameObject.SetActive(true);
            cloudFog.Play(true);
        }

        private void HandleStreetLanding(Collider support)
        {
            if (!descendingToStreet || runController == null ||
                !runController.Generator.IsCloudStreetFloor(support))
                return;

            // Stay in SpecialEvent for every callback in this physics batch.
            // Resuming here could classify a simultaneous contact as a road hit.
            pendingStreetSupport = support;
        }

        private void CompleteStreetLanding(Collider support)
        {
            if (!descendingToStreet ||
                !runController.Generator.IsCloudStreetFloor(support))
                return;

            descendingToStreet = false;
            pendingStreetSupport = null;
            runController.PrepareCloudStreetReturn(gameplaySphere.transform.position.z);

            gameplaySphere.GroundContact -= HandleStreetLanding;
            gameplaySphere.SetCloudDescent(false);
            gameplaySphere.SetInputContext(EndlessRollball.Imu.ImuControlContext.Road);
            gameplaySphere.DiscardBufferedInput();

            for (int i = 0; i < platforms.Length; i++)
                platforms[i].gameObject.SetActive(false);

            runController.Generator.EndCloudRoadContinuation();

            double elapsed = Mathf.Max(
                0f, Time.time - physicalLaunchTime);

            runController.EndSpecialEvent(elapsed);
            runController.GetComponent<GameSequenceCoordinator>()?.Notify(GameSequenceCoordinator.SequencePhase.RoadAfterEvent);

            if (cloudFog != null)
            {
                cloudFog.Stop(
                    true,
                    ParticleSystemStopBehavior.StopEmittingAndClear);

                cloudFog.gameObject.SetActive(false);
            }

            finished = true;
            Completed?.Invoke();
        }

        private void LateUpdate()
        {
            if (pendingStreetSupport != null && runController != null &&
                runController.State == SessionFlowState.SpecialEvent)
                CompleteStreetLanding(pendingStreetSupport);

            if (simulation == null || eventCamera == null) return;
            Vector3 anchor = origin + Vector3.forward * simulation.Z;
            eventCamera.transform.position = anchor + cameraOffset;
            eventCamera.transform.LookAt(anchor + Vector3.forward * 9f);
        }

        private void OnDisable()
        {
            pendingStreetSupport = null;
            if (simulation != null && !soundManager.IsPaused && !finished) TogglePause();
        }

        private void OnApplicationPause(bool applicationPaused)
        {
            if (applicationPaused && simulation != null && !soundManager.IsPaused && !finished) TogglePause();
        }

        private void OnGUI()
        {
            if (useGameplaySphere) return;

            if (hudStyle == null)
            {
                hudStyle = new GUIStyle(GUI.skin.box) { fontSize = 18, alignment = TextAnchor.UpperLeft };
                creditStyle = new GUIStyle(GUI.skin.box) { fontSize = 12, alignment = TextAnchor.UpperLeft, wordWrap = true };
            }
            float width = Mathf.Min(620f, Screen.width - 32f);
            if (simulation == null)
            {
                GUI.Box(new Rect(16, 16, width, 75), loadError ?? "Preparing cloud music and platforms...", hudStyle);
                return;
            }
            CloudMusicChart chart = currentChart;
            string state = finished ? (embedded ? "Returning to Street..." : "Complete — N: new piece / R: replay") : soundManager.IsPaused ? "Paused — P to resume" : "Cloud Hopping — " + difficulty;
            string controls = embedded ? "A/D/arrows/stick: steer   P or Esc: pause" : "A/D/arrows/stick: steer  P: pause  R: replay  N: new piece";
            GUI.Box(new Rect(16, 16, width, 120),
                $"{state}\n{chart.title} ({songIndex + 1}/3)\n{Math.Max(0, chart.duration - simulation.Time):0.0}s   Landings: {simulation.Landings}   Recoveries: {simulation.Recoveries}\n{controls}", hudStyle);
            float buttonWidth = (width - 16f) / 3f;
            if (!embedded)
            {
                for (int i = 0; i < 3; i++)
                    if (GUI.Button(new Rect(16 + i * (buttonWidth + 8), 142, buttonWidth, 30), (i + 1) + ": " + (DifficultyMode)i))
                        SetDifficulty((DifficultyMode)i);
                if (GUI.Button(new Rect(16, 178, width, 28), "Next piano piece (N)")) SetDifficulty(difficulty);
            }
            else if (soundManager.IsPaused && !entryHeld && GUI.Button(new Rect(16, 142, width, 30), "Return to Street")) ReturnRequested?.Invoke();
            GUI.Box(new Rect(16, Screen.height - 82, width, 66),
                $"{chart.title} — original piano study\nPiano samples: Alexander Holm / Salamander Grand Piano (CC BY 3.0)\nhttps://creativecommons.org/licenses/by/3.0/ — samples repitched at playback.", creditStyle);
        }

        private void OnDestroy()
        {
            if (gameplaySphere != null)
            {
                gameplaySphere.GroundContact -= HandleStreetLanding;
                gameplaySphere.SetCloudDescent(false);
                gameplaySphere.SetInputContext(EndlessRollball.Imu.ImuControlContext.Road);
            }

            ReleaseSimulation();
            if (soundManager != null) soundManager.StopMusic();
            if (platformMaterial != null) Destroy(platformMaterial);
            if (ballMaterial != null) Destroy(ballMaterial);
            if (platformMesh != null) Destroy(platformMesh);
            if (cloudTexture != null) Destroy(cloudTexture);
        }

        private void ReleaseSimulation()
        {
            if (simulation == null) return;
            simulation.Landed -= soundManager.PlayLandingNotes;
            simulation.Missed -= soundManager.SilenceMissedPlatform;
            simulation = null;
        }
    }
}
