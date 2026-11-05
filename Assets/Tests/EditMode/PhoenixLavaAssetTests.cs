using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace EndlessRollball.Tests
{
    public sealed class PhoenixLavaAssetTests
    {
        private const string Folder =
            "Assets/EndlessRollballAssets/Obstacles/Dungeon/PhoenixLava/Prefabs/";

        [TestCase("OBS_Phoenix_BasaltPillar.prefab")]
        [TestCase("OBS_Phoenix_LavaEruption.prefab")]
        [TestCase("OBS_Phoenix_BouncingLavaBall.prefab")]
        public void HazardImportsAsAPrefabWithWorkingReferences(string name)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + name);
            Assert.IsNotNull(prefab, "Unity must import the prefab before the event can prepare.");
            PhoenixLavaHazard hazard = prefab.GetComponent<PhoenixLavaHazard>();
            Assert.IsNotNull(hazard);
            var data = new SerializedObject(hazard);
            foreach (string field in new[] { "hazardVisual", "warningVisual", "hitCollider", "sparkMaterial" })
                Assert.IsNotNull(data.FindProperty(field).objectReferenceValue, name + ": " + field);
        }

        [Test]
        public void RigImportsWithAllThreeCoursePrefabs()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "PhoenixFlightRig.prefab");
            Assert.IsNotNull(prefab, "The phoenix rig must use importable native prefab IDs.");
            Assert.IsNotNull(prefab.GetComponentInChildren<PhoenixFlightController>(true));
            PhoenixLavaCourse course = prefab.GetComponentInChildren<PhoenixLavaCourse>(true);
            Assert.IsNotNull(course);
            var data = new SerializedObject(course);
            foreach (string field in new[] { "pillarPrefab", "eruptionPrefab", "bouncingBallPrefab" })
                Assert.IsNotNull(data.FindProperty(field).objectReferenceValue, field);
        }
    }
}
