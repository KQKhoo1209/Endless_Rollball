using EndlessRollball.Tutorial;
using UnityEditor;
using UnityEngine;

public static class TutorialProgressMenu
{
    [MenuItem("Endless Rollball/Tutorial/Reset Local Completion")]
    private static void ResetCompletion()
    {
        PlayerPrefs.DeleteKey(TutorialProgress.CompletionKey);
        PlayerPrefs.Save();
        Debug.Log("Tutorial completion reset for this local application's data. Other preferences were preserved.");
    }
}
