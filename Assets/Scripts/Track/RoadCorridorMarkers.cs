using System.Collections.Generic;
using UnityEngine;

namespace EndlessRollball.Track
{
    /// <summary>Visual edges follow each support bank, leaving the physical gap visibly open.</summary>
    public sealed class RoadCorridorMarkers : MonoBehaviour
    {
        private readonly List<LineRenderer> lines = new List<LineRenderer>();
        private BoxCollider[] supports;
        public void Configure(TrackSegment track, DifficultyManager difficulty, Material material)
        {
            if (material == null || difficulty == null)
                return;
            if (supports == null)
            {
                var floors = new List<BoxCollider>();
                foreach (BoxCollider box in track.GetComponentsInChildren<BoxCollider>(true))
                    if (box.name == "FloorCollider" || box.name == "Floor_Takeoff" || box.name == "Floor_Landing")
                        floors.Add(box);
                supports = floors.ToArray();
            }

            int used = 0;
            foreach (BoxCollider floor in supports)
            {
                if (floor == null)
                    continue;
                Vector3 start = floor.transform.TransformPoint(floor.center + new Vector3(0f, floor.size.y * .5f, -floor.size.z * .5f));
                Vector3 end = floor.transform.TransformPoint(floor.center + new Vector3(0f, floor.size.y * .5f, floor.size.z * .5f));
                start.z = Mathf.Max(start.z, track.EntryAnchor.position.z);
                end.z = Mathf.Min(end.z, track.ExitAnchor.position.z);
                if (end.z <= start.z) continue;
                for (int side = -1; side <= 1; side += 2)
                {
                    if (used == lines.Count)
                    {
                        var child = new GameObject("PlayableEdge");
                        child.transform.SetParent(track.VisualRoot, false);
                        LineRenderer line = child.AddComponent<LineRenderer>();
                        line.useWorldSpace = false;
                        line.widthMultiplier = .07f;
                        line.positionCount = 9;
                        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        line.receiveShadows = false;
                        lines.Add(line);
                    }

                    LineRenderer marker = lines[used++];
                    marker.sharedMaterial = material;
                    marker.enabled = true;
                    for (int sample = 0; sample < 9; sample++)
                    {
                        Vector3 point = Vector3.Lerp(start, end, sample / 8f);
                        point.x = side * difficulty.EvaluateForDistance(point.z).RoadWidth * .5f;
                        point.y += .035f;
                        marker.SetPosition(sample, marker.transform.InverseTransformPoint(point));
                    }
                }
            }

            for (int i = used; i < lines.Count; i++)
                lines[i].enabled = false;
        }
    }
}
