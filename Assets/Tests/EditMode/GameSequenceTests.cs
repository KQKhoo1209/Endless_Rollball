using NUnit.Framework;
using UnityEngine;
using EndlessRollball.Track;
using EndlessRollball.Scenery;
namespace EndlessRollball.Tests {
public sealed class GameSequenceTests {
 [TestCase(GameMode.Standard,DifficultyMode.Easy,360f)]
 [TestCase(GameMode.Standard,DifficultyMode.Normal,405f)]
 [TestCase(GameMode.Standard,DifficultyMode.Hard,405f)]
 [TestCase(GameMode.FastPaced,DifficultyMode.Easy,180f)]
 [TestCase(GameMode.FastPaced,DifficultyMode.Normal,180f)]
 [TestCase(GameMode.FastPaced,DifficultyMode.Hard,180f)]
 public void ProfilesHaveAgreedBudgets(GameMode mode,DifficultyMode difficulty,float expected){
 var config=Resources.Load<GameSequenceConfiguration>("GameSequence");Assert.IsNotNull(config);
 Assert.AreEqual(expected,config.TotalSeconds(mode,difficulty));
 config.Entry(mode,difficulty,out float earliest,out float target,out float latest);
 Assert.That(target,Is.InRange(earliest,latest));
 Assert.Less(latest+config.eventSeconds,config.ThemeSeconds(mode,difficulty));
 Assert.AreEqual(45,config.eventSeconds);
 }
 [Test] public void StandardOrdersMatchAgreedJourney(){
 var c=GameSequenceConfiguration.Current;
 CollectionAssert.AreEqual(new[]{EnvironmentTheme.Street,EnvironmentTheme.Water},c.easyOrder);
 CollectionAssert.AreEqual(new[]{EnvironmentTheme.Water,EnvironmentTheme.Street,EnvironmentTheme.Dungeon},c.normalOrder);
 CollectionAssert.AreEqual(new[]{EnvironmentTheme.Dungeon,EnvironmentTheme.Water,EnvironmentTheme.Street},c.hardOrder);
 }
}
}
