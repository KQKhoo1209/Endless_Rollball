using UnityEngine;
using EndlessRollball.Track;
using EndlessRollball.Scenery;
using EndlessRollball.Avatars;
namespace EndlessRollball {
[DefaultExecutionOrder(-100)]
public sealed class GameSequenceCoordinator:MonoBehaviour {
 public enum SequencePhase {RoadBeforeEvent,Boarding,Event,Return,RoadAfterEvent,ThemeTransition,Finish}
 [SerializeField] private GameSequenceConfiguration configuration;
 [SerializeField] private EnvironmentTheme currentTheme;
 [SerializeField] private SequencePhase phase;
 [SerializeField] private float themeElapsed, predictedArrival;
 [SerializeField] private bool reserved;
 private GameplayRunController run; private EnvironmentManager environment; private double themeStarted; private bool initialized;
 public float Elapsed {get {RefreshThemeClock();return themeElapsed;}}
 public bool IsStreet=>environment!=null && environment.CurrentTheme==EnvironmentTheme.Street;
 public bool Eligible=>GameSessionSettings.SelectedMode==GameMode.Standard || GameSessionSettings.SelectedMode==GameMode.FastPaced;
 public bool Available=>Eligible && IsEventUnlocked(currentTheme) && phase==SequencePhase.RoadBeforeEvent;
 // Ownership unlocks the corresponding gameplay; the selected result companion
 // does not restrict which unlocked events can appear during a run.
 public static bool IsEventUnlocked(EnvironmentTheme theme) {
 if(theme==EnvironmentTheme.Water)return AvatarProgressRepository.Instance.IsUnlocked(AvatarCatalog.Whale);
 if(theme==EnvironmentTheme.Dungeon)return AvatarProgressRepository.Instance.IsUnlocked(AvatarCatalog.Phoenix);
 return true; // Cloud hopping uses the original sphere, with no locked carrier.
 }
 public GameSequenceConfiguration Configuration=>configuration!=null?configuration:GameSequenceConfiguration.Current;
 private void Awake(){run=GetComponent<GameplayRunController>(); environment=FindFirstObjectByType<EnvironmentManager>();}
 public void Window(out float earliest,out float target,out float latest)=>Configuration.Entry(GameSessionSettings.SelectedMode??GameMode.Standard,run.TrackDifficulty,out earliest,out target,out latest);
 public void Reservation(float arrival){reserved=true;predictedArrival=arrival;}
 public void Notify(SequencePhase value){phase=value;EndlessRollball.Infrastructure.RuntimeLog.Info($"[Sequence] {currentTheme}: {value}, theme={themeElapsed:F2}s, run={run.ActiveDurationSeconds:F2}s",this);}
 private void Update(){
 if(run.State==SessionFlowState.Preparing){initialized=false;reserved=false;phase=SequencePhase.RoadBeforeEvent;themeElapsed=0;return;}
 RefreshThemeClock();
 }
 private void RefreshThemeClock(){
 if(run==null || run.State==SessionFlowState.Preparing || !Eligible || environment==null)return;
 if(!initialized || currentTheme!=environment.CurrentTheme){currentTheme=environment.CurrentTheme;themeStarted=run.ActiveDurationSeconds;initialized=true;reserved=false;themeElapsed=0;Notify(IsEventUnlocked(currentTheme)?SequencePhase.RoadBeforeEvent:SequencePhase.RoadAfterEvent);}
 themeElapsed=(float)(run.ActiveDurationSeconds-themeStarted);
 }
}
}
