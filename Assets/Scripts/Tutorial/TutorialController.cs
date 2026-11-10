using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using EndlessRollball.Track;
using TMPro;

namespace EndlessRollball.Tutorial
{
    public sealed class TutorialController : MonoBehaviour
    {
        private enum Step { Welcome, Left, Right, Centre, Jump, Obstacle, Combined, Complete }
        [SerializeField] private BallMovement ball;
        [SerializeField] private TutorialInput input;
        [SerializeField] private CameraFollow followCamera;
        [SerializeField] private Transform target;
        [SerializeField] private Collider obstacle;
        [SerializeField] private Transform checkpoint;
        [SerializeField] private Transform finish;
        [SerializeField, Min(.1f)] private float practiceSpeed = 3f;
        [SerializeField] private RuntimeAnimatorController demonstrationController;
        [SerializeField] private Sprite guidanceHandSprite;
        [SerializeField] private GameObject gameplayPauseUIPrefab;
        [Header("Practice audio")]
        [SerializeField] private AudioSource rollingAudio;
        [SerializeField] private AudioSource feedbackAudio;
        [SerializeField] private AudioClip rollingClip;
        [SerializeField] private AudioClip landingClip;
        [SerializeField] private AudioClip taskSuccessClip;
        private float audioGain;
        private bool touchedGround;
        private float airborneSince = -1f;
        private TutorialView view;
        private Step step;
        private bool practising, paused, busy, acceptedJump, airborne, landed, obstacleHit;
        private float centreSeconds;
        private int combinedTarget;
        private Vector3 previousPosition;
        private Rigidbody body;
        private TMP_Text targetLabel, finishLabel;

        private IEnumerator Start()
        {
            Time.timeScale = 1;
            body = ball.GetComponent<Rigidbody>();
            PrepareAudio();
            ball.SetRoadCorridor(new RoadCorridor(6f));
            ball.JumpAccepted += OnJump;
            ball.AirborneObserved += OnAirborne;
            ball.GroundContact += OnGround;
            input.Contact += OnContact;
            view = TutorialView.Create(transform);
            view.ConfigurePresentation(guidanceHandSprite, gameplayPauseUIPrefab);
            view.ConfigureDemonstrations(demonstrationController, ball, followCamera, transform);
            view.Primary += Primary;
            view.Pause += Pause;
            view.Resume += Resume;
            view.Exit += Exit;
            targetLabel = CreateRoadLabel("Target label", "LEFT", target.position, 2f);
            finishLabel = CreateRoadLabel("Finish label", "FINISH", finish.position, 3f);
            Prepare(Step.Welcome);
            while (TutorialSceneTransition.IsLoading) yield return null;
            EndlessRollball.Infrastructure.GameplayAssetPreloader.BeginTutorial();
            // Direct Editor launches without menu selection remain available for authoring.
            if (TutorialProgress.IsCompleted && (!Application.isEditor || GameSessionSettings.HasValidSelection))
            {
                step = Step.Complete;
                view.Show("Tutorial already completed", "Continue to your selected game.", "Complete", "Continue");
                ContinueToGame();
            }
        }

        private void Prepare(Step next, string retry = null)
        {
            view.SetDemonstration(TutorialDemonstration.None);
            step = next;
            practising = false;
            acceptedJump = airborne = landed = obstacleHit = false;
            StopRollingAudio();
            touchedGround = false;
            airborneSince = -1f;
            centreSeconds = 0;
            combinedTarget = 0;
            input.SetAllowed(false, false);
            ball.SetForwardSpeed(0, 0);
            float x = step == Step.Right ? -1.8f : step == Step.Centre ? 1.8f : 0;
            ball.ResetMotion(new Vector3(x, .55f, 0), Quaternion.identity);
            ball.StopMotionForCompletion();
            previousPosition = ball.transform.position;
            checkpoint.position = new Vector3(x, .025f, 0);
            obstacle.gameObject.SetActive(step == Step.Obstacle || step == Step.Combined);
            obstacle.transform.position = new Vector3(0, .2f, step == Step.Combined ? 26 : 10);
            finish.gameObject.SetActive(step == Step.Combined);
            finishLabel.gameObject.SetActive(step == Step.Combined);
            target.gameObject.SetActive(step == Step.Left || step == Step.Right || step == Step.Centre || step == Step.Combined);
            target.position = new Vector3(step == Step.Left || step == Step.Combined ? -1.8f : step == Step.Right ? 1.8f : 0,
                .025f, step == Step.Combined ? 8 : 0);
            UpdateTargetLabel();
            followCamera.SnapToTarget();
            string title, bodyText;
            switch (step)
            {
                case Step.Welcome:
                    title = "Welcome to Endless Rollball";
                    bodyText = "The ball rolls forward automatically. Learn the controls at your own pace.\nPractise with the keyboard now; sensor input will come later."; break;
                case Step.Left:
                    title = "Move left  ·  A / ←";
                    bodyText = "Move into the highlighted LEFT target. Release to stop moving sideways.\nWatch the whole hand and the ball move left together."; break;
                case Step.Right:
                    title = "Move right  ·  D / →";
                    bodyText = "Move across to the highlighted RIGHT target.\nWatch the whole hand and the ball move right together."; break;
                case Step.Centre:
                    title = "Return to the centre";
                    bodyText = "Use A / ← or D / → to reach the CENTRE target, then release.\nStay centred briefly. Release the control once the ball reaches the centre."; break;
                case Step.Jump:
                    title = "Jump  ·  ↑";
                    bodyText = "Press the UP arrow to jump, then land on the road.\nFirst practise here without forward movement."; break;
                case Step.Obstacle:
                    title = "Jump over the low barrier  ·  ↑";
                    bodyText = "The ball now rolls forward slowly. Jump shortly before the orange barrier.\nClear it and land safely. Touching it simply lets you try again."; break;
                default:
                    title = "Put it together";
                    bodyText = "Reach LEFT, then RIGHT, jump over the orange barrier, and reach FINISH.\nA / ← left     D / → right     ↑ jump. Take as many tries as you need."; break;
            }
            if (retry != null) bodyText = retry + "\n" + bodyText;
            if (GameSessionSettings.SelectedInputMode == GameInputMode.Imu && EndlessRollball.Imu.ImuRuntimeSession.Instance != null && EndlessRollball.Imu.ImuRuntimeSession.Instance.InputRequested)
            {
                title = step == Step.Welcome ? "Welcome to Endless Rollball" :
                    step == Step.Left ? "Move left" : step == Step.Right ? "Move right" :
                    step == Step.Centre ? "Return to the centre" : step == Step.Jump ? "Jump" :
                    step == Step.Obstacle ? "Jump over the low barrier" : "Put it together";
                bodyText = step == Step.Welcome ? "Wrist tilt sets the ball's position across the road. Neutral brings it to the centre. Tilt upward to jump. Forward movement is automatic." :
                    step == Step.Left ? "Roll your wrist left and hold the tilt to place the ball on the LEFT target. More tilt selects a position farther left." :
                    step == Step.Right ? "Roll your wrist right and hold the tilt to place the ball on the RIGHT target. More tilt selects a position farther right." :
                    step == Step.Centre ? "Return your wrist to the calibrated neutral pose. The ball moves back to CENTRE; hold still as it settles." :
                    step == Step.Jump ? "Tilt your wrist upward to jump, then return to neutral. Practise landing here without forward movement." :
                    step == Step.Obstacle ? "Tilt upward before the orange barrier, return to neutral, and land beyond it." :
                    "Reach LEFT, then RIGHT, tilt upward to jump over the barrier, and reach FINISH.";
                if (retry != null) bodyText = retry + "\n" + bodyText;
            }
            view.Show(title, bodyText, step == Step.Welcome ? "First-play tutorial" : $"Practice {(int)step} of 6 · Watch the preview, then press Ready",
                step == Step.Welcome ? "Begin" : "Ready");
            view.SetDemonstration(step == Step.Left ? TutorialDemonstration.Left :
                step == Step.Right ? TutorialDemonstration.Right :
                step == Step.Centre ? TutorialDemonstration.Centre :
                step == Step.Jump ? TutorialDemonstration.Jump :
                step == Step.Obstacle ? TutorialDemonstration.Obstacle :
                step == Step.Combined ? TutorialDemonstration.Combined : TutorialDemonstration.None);
        }

        private void Primary()
        {
            if (busy || paused) return;
            if (!ball.IsSelectedInputReady) return;
            if (step == Step.Welcome) { Prepare(Step.Left); return; }
            if (step == Step.Complete) { ContinueToGame(); return; }
            view.SetDemonstration(TutorialDemonstration.None);
            input.SetAllowed(step != Step.Jump && step != Step.Obstacle, step >= Step.Jump);
            ball.SetForwardSpeed(step >= Step.Obstacle ? practiceSpeed : 0, 0);
            ball.ResumeMotion();
            input.Clear();
            practising = true;
            ShowPractice();
        }

        private void ShowPractice()
        {
            string text = step == Step.Left ? "Move LEFT into the highlighted target: A / ←" :
                step == Step.Right ? "Move RIGHT into the highlighted target: D / →" :
                step == Step.Centre ? "Return to CENTRE and release the steering control." :
                step == Step.Jump ? "Press ↑ to jump, then land on the road." :
                step == Step.Obstacle ? "Jump over the orange barrier with ↑, then land." :
                combinedTarget == 0 ? "First: reach the LEFT target. A / ←" :
                combinedTarget == 1 ? "Good! Next: reach the RIGHT target. D / →" :
                "Both targets reached! Jump over the orange barrier with ↑, then reach FINISH.";
            if (GameSessionSettings.SelectedInputMode == GameInputMode.Imu && EndlessRollball.Imu.ImuRuntimeSession.Instance != null && EndlessRollball.Imu.ImuRuntimeSession.Instance.InputRequested)
                text = step == Step.Left ? "Roll LEFT and hold the tilt to stay on the highlighted target." :
                    step == Step.Right ? "Roll RIGHT and hold the tilt to stay on the highlighted target." :
                    step == Step.Centre ? "Hold your neutral wrist pose to bring the ball back to CENTRE." :
                    step == Step.Jump ? "Tilt upward to jump, return to neutral, and land." :
                    step == Step.Obstacle ? "Tilt upward to clear the barrier, then land." :
                    combinedTarget == 0 ? "Roll LEFT into the first target." :
                    combinedTarget == 1 ? "Roll RIGHT into the next target." :
                    "Tilt upward to jump over the barrier, then reach FINISH.";
            view.Show("Your turn", text, $"Practice {(int)step} of 6");
        }

        private void Update()
        {
            if (!ball.IsSelectedInputReady) Pause();
            if (Keyboard.current?.escapeKey.wasPressedThisFrame == true)
            {
                if (paused) Resume(); else Pause();
            }
            view.SetSteering(practising && !paused ? input.Steering : 0);
        }

        private void FixedUpdate()
        {
            if (!practising || paused || busy) return;
            Vector3 p = body.position;
            if (p.y < -.5f || Mathf.Abs(p.x) > 3f || p.z > 40 || p.z < -4)
            { Retry("Let's return to this exercise's checkpoint."); return; }
            if (obstacleHit) { Retry("You touched the barrier. Try jumping a little earlier."); return; }
            bool inTarget = Mathf.Abs(p.x - target.position.x) <= .6f;
            if ((step == Step.Left || step == Step.Right) && inTarget) Succeed();
            else if (step == Step.Centre)
            {
                centreSeconds = inTarget && Mathf.Abs(input.Steering) < .05f && Mathf.Abs(body.linearVelocity.x) < .2f
                    ? centreSeconds + Time.fixedDeltaTime : 0;
                if (centreSeconds >= .5f) Succeed();
            }
            else if (step == Step.Jump && landed) Succeed();
            else if (step == Step.Obstacle && p.z > obstacle.transform.position.z + 1f)
            {
                if (landed) Succeed();
                else if (p.z > obstacle.transform.position.z + 5f) Retry("Jump over the barrier and land beyond it.");
            }
            else if (step == Step.Combined)
            {
                if (combinedTarget < 2 && previousPosition.z < target.position.z && p.z >= target.position.z)
                {
                    float fraction = Mathf.InverseLerp(previousPosition.z, p.z, target.position.z);
                    float crossingX = Mathf.Lerp(previousPosition.x, p.x, fraction);
                    if (Mathf.Abs(crossingX - target.position.x) > .6f)
                    { Retry("You missed a target. Follow LEFT, then RIGHT."); return; }
                    combinedTarget++;
                    target.position = new Vector3(1.8f, .025f, 17);
                    target.gameObject.SetActive(combinedTarget < 2);
                    UpdateTargetLabel();
                    ShowPractice();
                }
                if (p.z >= finish.position.z)
                {
                    if (combinedTarget == 2 && landed) Succeed();
                    else Retry("Reach both targets and land beyond the barrier before finishing.");
                }
            }
            previousPosition = p;
        }

        private void OnJump(Collider support)
        {
            if (!practising || paused) return;
            acceptedJump = true;
            airborne = landed = false;
        }
        private void OnAirborne()
        {
            if (practising && acceptedJump) airborne = true;
            if (practising && !paused && touchedGround && airborneSince < 0f)
                airborneSince = Time.time;
        }
        private void OnGround(Collider support)
        {
            if (practising && !paused && support != null && support != obstacle)
            {
                if (touchedGround && airborneSince >= 0f && Time.time - airborneSince >= .08f)
                    PlayFeedback(landingClip, .4f);
                touchedGround = true;
                airborneSince = -1f;
            }
            if (!practising || !acceptedJump || !airborne || support == obstacle) return;
            if (step == Step.Jump || body.position.z > obstacle.transform.position.z + 1f)
                landed = true;
        }
        private void OnContact(Collider contact) { if (practising && contact == obstacle) obstacleHit = true; }
        private void Retry(string message) => Prepare(step, message);

        private void Succeed()
        {
            practising = false;
            StopRollingAudio();
            PlayFeedback(taskSuccessClip, .65f);
            input.SetAllowed(false, false);
            ball.StopMotionForCompletion();
            if (step == Step.Combined)
            {
                TutorialProgress.MarkCompleted();
                step = Step.Complete;
                view.Show("You're ready!", "A / ← left     D / → right     ↑ jump\nThe ball rolls forward automatically. Enjoy your game!", "Tutorial complete", "Continue");
            }
            else StartCoroutine(Advance());
        }
        private IEnumerator Advance()
        {
            busy = true;
            view.SetBusy(true);
            view.Show("Well done!", "You completed this exercise.", $"Practice {(int)step} of 6");
            yield return new WaitForSecondsRealtime(1f);
            busy = false;
            view.SetBusy(false);
            Prepare((Step)((int)step + 1));
        }

        private void Pause()
        {
            if (busy || paused) return;
            paused = true;
            StopRollingAudio();
            if (feedbackAudio != null) feedbackAudio.Pause();
            Time.timeScale = 0;
            input.Clear();
            view.SetPaused(true);
        }
        private void Resume()
        {
            if (!paused) return;
            if (!ball.IsSelectedInputReady) return;
            input.Clear();
            paused = false;
            if (feedbackAudio != null) feedbackAudio.UnPause();
            Time.timeScale = 1;
            view.SetPaused(false);
        }
        public bool IsPaused => paused;
        public void ResumeAfterSensorCalibration() => Resume();
        public void ExitAfterSensorLoss() => Exit();
        private void Exit()
        {
            Resume();
            Time.timeScale = 1f;
            view.SetDemonstration(TutorialDemonstration.None);
            practising = false;
            ball.StopMotionForCompletion();
            LoadMenu();
        }
        private void LoadMenu()
        {
            busy = true;
            view.SetBusy(true);
            if (!TutorialSceneTransition.Load("MainMenuScene", message => LoadFailed(message, LoadMenu)))
            { busy = false; view.SetBusy(false); }
        }
        private void ContinueToGame()
        {
            if (busy) return;
            if (!GameSessionSettings.HasValidSelection) { LoadMenu(); return; }
            if (!Application.CanStreamedLevelBeLoaded("GameplayScene"))
            {
                LoadFailed("Add GameplayScene to the active build scene list, then retry.", ContinueToGame);
                return;
            }
            busy = true;
            view.SetBusy(true);
            Time.timeScale = 1;
            GameSessionSettings.MarkGameplayLoadRequested("tutorial");
            EndlessRollball.Infrastructure.GameplayAssetPreloader.ContinueToGameplay();
            GameplayLoadingTransition.LoadGameplay(message => LoadFailed(message, ContinueToGame));
        }
        private TMP_Text CreateRoadLabel(string name, string text, Vector3 position, float width)
        {
            var go = new GameObject(name, typeof(TextMeshPro));
            go.transform.SetParent(transform, false);
            go.transform.position = position + new Vector3(0, .035f, .1f);
            go.transform.rotation = Quaternion.Euler(90, 0, 0);
            var label = go.GetComponent<TextMeshPro>();
            label.text = text;
            label.fontSize = 3;
            label.color = new Color(.02f, .08f, .12f);
            label.alignment = TextAlignmentOptions.Center;
            label.rectTransform.sizeDelta = new Vector2(width, 1);
            return label;
        }
        private void UpdateTargetLabel()
        {
            targetLabel.gameObject.SetActive(target.gameObject.activeSelf);
            targetLabel.transform.position = target.position + new Vector3(0, .035f, .1f);
            targetLabel.text = step == Step.Centre ? "CENTRE" : target.position.x < 0 ? "← LEFT" : "RIGHT →";
        }
        private void LoadFailed(string message, System.Action retry)
        {
            busy = false;
            view.SetBusy(false);
            view.ShowError(message, retry);
        }
        private void PrepareAudio()
        {
            audioGain = Mathf.Clamp01(PlayerPrefs.GetFloat("MenuAudio.Master", 1f)) *
                Mathf.Clamp01(PlayerPrefs.GetFloat("MenuAudio.Effects", .45f));
            ConfigureAudio(rollingAudio, true);
            ConfigureAudio(feedbackAudio, false);
            if (rollingAudio != null) rollingAudio.clip = rollingClip;
            PreloadAudio(rollingClip); PreloadAudio(landingClip); PreloadAudio(taskSuccessClip);
        }

        private static void ConfigureAudio(AudioSource source, bool loop)
        {
            if (source == null) return;
            source.playOnAwake = false;
            source.loop = loop;
            source.spatialBlend = 0f;
            source.dopplerLevel = 0f;
            source.Stop();
        }

        private static void PreloadAudio(AudioClip clip)
        {
            if (clip != null && clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
        }

        private void PlayFeedback(AudioClip clip, float volume)
        {
            if (feedbackAudio != null && clip != null && audioGain > 0f)
                feedbackAudio.PlayOneShot(clip, volume * audioGain);
        }

        private void LateUpdate()
        {
            if (rollingAudio == null) return;
            if (!practising || paused || busy || ball == null || body == null ||
                !ball.IsGrounded || ball.GroundCollider == obstacle || Time.timeScale <= 0f ||
                rollingClip == null || rollingClip.loadState != AudioDataLoadState.Loaded || audioGain <= 0f)
            {
                StopRollingAudio();
                return;
            }
            Vector3 velocity = body.linearVelocity;
            float speed = new Vector2(velocity.x, velocity.z).magnitude;
            if (speed < .2f) { StopRollingAudio(); return; }
            float speedRatio = Mathf.Clamp01(speed / 7f);
            float targetPitch = Mathf.Lerp(.8f, 1.15f, speedRatio);
            rollingAudio.pitch = rollingAudio.isPlaying
                ? Mathf.Lerp(rollingAudio.pitch, targetPitch, 1f - Mathf.Exp(-Time.deltaTime / .25f))
                : targetPitch;
            rollingAudio.volume = Mathf.MoveTowards(rollingAudio.volume, .35f * audioGain * speedRatio,
                .35f * audioGain * Time.deltaTime / .1f);
            if (!rollingAudio.isPlaying) rollingAudio.Play();
        }

        private void StopRollingAudio()
        {
            if (rollingAudio == null) return;
            rollingAudio.Stop();
            rollingAudio.volume = 0f;
        }

        private void OnDestroy()
        {
            StopRollingAudio();
            if (feedbackAudio != null) feedbackAudio.Stop();
            EndlessRollball.Infrastructure.GameplayAssetPreloader.TutorialDestroyed();
            Time.timeScale = 1;
            if (ball != null)
            {
                ball.JumpAccepted -= OnJump;
                ball.AirborneObserved -= OnAirborne;
                ball.GroundContact -= OnGround;
            }
            if (input != null) input.Contact -= OnContact;
        }
    }
}
