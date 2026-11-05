using UnityEngine;

namespace EndlessRollball.Tutorial
{
    public static class TutorialProgress
    {
        public const string CompletionKey = "EndlessRollball.Tutorial.Completed";
        public static bool IsCompleted => PlayerPrefs.GetInt(CompletionKey, 0) == 1;

        public static void MarkCompleted()
        {
            PlayerPrefs.SetInt(CompletionKey, 1);
            PlayerPrefs.Save();
        }
    }
}
