using UnityEditor;
using UnityEngine;
using EndlessRollball.Track;
[InitializeOnLoad]
public static class BeachBarrierValidation
{
    static BeachBarrierValidation() { EditorApplication.delayCall+=Validate; }
    [MenuItem("Tools/Endless Rollball/Validate Beach Barrier Assets")]
    public static void Validate()
    {
        string folder="Assets/EndlessRollballAssets/Obstacles/Beach/BarriersV2/";
        int checks=0;
        foreach(string name in new[]{"TRK_Beach_RollingBarrier_24m","TRK_Beach_ExplosiveRoller_24m"})
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(folder+name+".prefab");
            if(prefab==null) return;
            var barrier=prefab.GetComponent<BeachBarrierEncounter>(); var track=prefab.GetComponent<TrackSegment>();
            Debug.Assert(barrier!=null && track.IsContractValid(out _),name+": invalid contract");checks++;
            Debug.Assert(track.RequiredJump==barrier.Explosive,name+": incorrect task route");checks++;
            var shape=barrier.Hazard as BoxCollider;
            Debug.Assert(shape!=null && (barrier.Explosive ? shape.size.x<2f : shape.size.x>=6f) && shape.GetComponent<Rigidbody>() != null && shape.GetComponent<Rigidbody>().isKinematic,name+": unsafe barrier shape");checks++;
            if(!barrier.Explosive) continue;
            int takeoff=0,landing=0,patch=0;
            foreach(var box in prefab.GetComponentsInChildren<BoxCollider>(true))
            {
                if(box.name=="Floor_Takeoff") {takeoff++;Debug.Assert(Mathf.Abs(box.center.z+box.size.z/2f-10.4f)<.001f);}
                if(box.name=="Floor_Landing") {landing++;Debug.Assert(Mathf.Abs(box.center.z-box.size.z/2f-13.6f)<.001f);}
                if(box.name=="FloorCollider") {patch++;Debug.Assert(box.transform.parent.name=="RemovableDeck" && Mathf.Abs(box.size.z-3.2f)<.001f);}
            }
            Debug.Assert(takeoff==1 && landing==1 && patch==1,"Explosion floor ownership invalid"); checks++;
            foreach(var filter in prefab.GetComponentsInChildren<MeshFilter>())
            {
                var renderer=filter.GetComponent<MeshRenderer>();
                if(renderer==null || !renderer.enabled || filter.sharedMesh==null || filter.transform.IsChildOf(shape.transform)) continue;
                bool isPatch=filter.transform.parent.name=="RemovableDeck";
                using var data=UnityEditor.MeshUtility.AcquireReadOnlyMeshData(filter.sharedMesh);
                using var vertices=new Unity.Collections.NativeArray<Vector3>(data[0].vertexCount,Unity.Collections.Allocator.Temp);
                data[0].GetVertices(vertices);
                foreach(var v in vertices)
                {float z=prefab.transform.InverseTransformPoint(filter.transform.TransformPoint(v)).z;
                 Debug.Assert(isPatch ? z>=10.399f && z<=13.601f : z<=10.401f || z>=13.599f,"Visible geometry bridges the hole: "+filter.name);}
                checks++;
            }
        }
        Debug.Log("Beach barrier asset checks completed: "+checks+" checks. Play Mode is not covered.");
    }
}
