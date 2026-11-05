using System.Diagnostics;
using UnityEngine;

namespace EndlessRollball.Infrastructure
{
    internal static class RuntimeLog
    {
        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        internal static void Info(object message, Object context = null)
        {
            UnityEngine.Debug.Log(message, context);
        }
    }
}
