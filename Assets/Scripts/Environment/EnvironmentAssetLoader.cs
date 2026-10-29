using System.Collections.Generic;
using UnityEngine;

namespace EndlessRollball.Scenery
{
    internal sealed class EnvironmentAssetLoader
    {
        private sealed class Entry
        {
            internal ResourceRequest Request;
            internal EnvironmentScenerySet Set;
            internal string Error;
            internal bool Completed;
        }

        private readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>();

        internal bool TryGet(string path, bool needsSkybox, out EnvironmentScenerySet set, out string error)
        {
            set = null;
            error = null;
            if (string.IsNullOrWhiteSpace(path))
            {
                error = "A theme has no scenery resource path.";
                return false;
            }
            if (!entries.TryGetValue(path, out Entry entry))
            {
                entry = new Entry { Request = Resources.LoadAsync<EnvironmentScenerySet>(path) };
                entries.Add(path, entry);
            }
            if (!entry.Completed && entry.Request.isDone)
            {
                entry.Completed = true;
                entry.Set = entry.Request.asset as EnvironmentScenerySet;
                entry.Request = null;
                if (entry.Set == null || entry.Set.prefabs == null || entry.Set.prefabs.Length == 0)
                    entry.Error = $"Scenery set '{path}' is missing or empty.";
                else
                {
                    var unique = new HashSet<GameObject>();
                    foreach (GameObject prefab in entry.Set.prefabs)
                        if (prefab == null || !unique.Add(prefab))
                            entry.Error = $"Scenery set '{path}' has missing or duplicate prefabs.";
                    if (needsSkybox && entry.Set.skybox == null)
                        entry.Error = $"Scenery set '{path}' has no outdoor skybox.";
                }
            }
            error = entry.Error;
            set = entry.Set;
            return entry.Completed && error == null;
        }

        // ResourceRequests cannot be cancelled. There are no callbacks or static roots:
        // abandoning this owner cannot apply an old result to another scene/run.
        internal void Clear() => entries.Clear();
    }
}
