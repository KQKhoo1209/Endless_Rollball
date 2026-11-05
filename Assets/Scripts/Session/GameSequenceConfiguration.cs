using UnityEngine;
using EndlessRollball.Track;
using EndlessRollball.Scenery;
namespace EndlessRollball {
[CreateAssetMenu(menuName="Endless Rollball/Game Sequence")]
public sealed class GameSequenceConfiguration : ScriptableObject {
 public EnvironmentTheme[] easyOrder = {EnvironmentTheme.Street, EnvironmentTheme.Water};
 public EnvironmentTheme[] normalOrder = {EnvironmentTheme.Water, EnvironmentTheme.Street, EnvironmentTheme.Dungeon};
 public EnvironmentTheme[] hardOrder = {EnvironmentTheme.Dungeon, EnvironmentTheme.Water, EnvironmentTheme.Street};
 [Min(90)] public float easyThemeSeconds=180, threeThemeSeconds=135, fastThemeSeconds=180;
 [Min(0)] public float entryEarliest=60, entryTarget=67.5f, entryLatest=75;
 [Min(0)] public float shortEntryTarget=45;
 [Min(1)] public float eventSeconds=45;
 private static GameSequenceConfiguration cached;
 public static GameSequenceConfiguration Current {get { if(cached==null) {cached=Resources.Load<GameSequenceConfiguration>("GameSequence"); if(cached==null) cached=CreateInstance<GameSequenceConfiguration>();} return cached; }}
 public EnvironmentTheme[] Order(DifficultyMode difficulty)=>difficulty==DifficultyMode.Easy?easyOrder:difficulty==DifficultyMode.Hard?hardOrder:normalOrder;
 public float ThemeSeconds(GameMode mode,DifficultyMode difficulty)=>mode==GameMode.FastPaced?fastThemeSeconds:difficulty==DifficultyMode.Easy?easyThemeSeconds:threeThemeSeconds;
 public float TotalSeconds(GameMode mode,DifficultyMode difficulty)=>ThemeSeconds(mode,difficulty)*(mode==GameMode.FastPaced?1:Order(difficulty).Length);
 public void Entry(GameMode mode,DifficultyMode difficulty,out float earliest,out float target,out float latest) {
 bool shortStage=mode==GameMode.Standard && difficulty!=DifficultyMode.Easy;
 target=shortStage?shortEntryTarget:entryTarget; earliest=shortStage?target-2:entryEarliest; latest=shortStage?target+2:entryLatest;
 }
}
}
