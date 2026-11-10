using UnityEditor;
using System.IO;
foreach(var file in Directory.GetFiles("Assets/Scripts/Avatars","*.cs")) AssetDatabase.ImportAsset(file.Replace('\\','/'));
AssetDatabase.ImportAsset("Assets/Scripts/Session/GameplayRunController.cs");
AssetDatabase.ImportAsset("Assets/Scripts/UI/SessionFlowUI.cs");
return "Imported avatar component sources required to attach and serialize scene/prefab components.";
