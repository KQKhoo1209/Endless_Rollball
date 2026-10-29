using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using EndlessRollball.Track;

[InitializeOnLoad]
public static class DungeonGapRailingsBuilder
{
    private const string Road="Assets/EndlessRollballAssets/Environment Prefab/Roads/TRK_Dungeon_Road_24m.prefab";
    private const string Gaps="Assets/EndlessRollballAssets/Environment Prefab/Roads/Gaps/";
    static DungeonGapRailingsBuilder() {EditorApplication.delayCall+=WhenReady;}
    private static void WhenReady()
    {
        if(EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
        {EditorApplication.delayCall+=WhenReady;return;}
        Build();
    }
    [MenuItem("Tools/Endless Rollball/Add Dungeon Gap Side Railings")]
    public static void Build()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)return;
        var road=PrefabUtility.LoadPrefabContents(Road);
        try
        {
            var source=road.GetComponent<TrackSegment>().VisualRoot.Find("SideRailings");
            if(source==null)throw new InvalidOperationException("Normal Dungeon road railings missing.");
            foreach(string file in new[]{"TRK_Dungeon_GapSmall_24m.prefab","Difficulty/TRK_Dungeon_Gap_Easy_24m.prefab","Difficulty/TRK_Dungeon_Gap_Normal_24m.prefab","Difficulty/TRK_Dungeon_Gap_Hard_24m.prefab"})
            {
                string path=Gaps+file;var root=PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var track=root.GetComponent<TrackSegment>();
                    if(track.VisualRoot.Find("SideRailings")!=null)continue;
                    var holder=new GameObject("SideRailings");holder.transform.SetParent(track.VisualRoot,false);
                    int posts=0;
                    foreach(string bankName in new[]{"Takeoff","Landing"})
                    {
                        BoxCollider floor=null;foreach(var box in root.GetComponentsInChildren<BoxCollider>())if(box.name=="Floor_"+bankName)floor=box;
                        if(floor==null)throw new InvalidOperationException(file+": missing floor "+bankName);
                        float start=floor.center.z-floor.size.z*.5f, length=floor.size.z, top=floor.center.y+floor.size.y*.5f;
                        var bank=new GameObject(bankName);bank.transform.SetParent(holder.transform,false);
                        bank.transform.SetPositionAndRotation(floor.transform.TransformPoint(new Vector3(0,top,start)),floor.transform.rotation);
                        bank.transform.localScale=floor.transform.lossyScale;
                        foreach(Transform side in source)
                        {
                            var group=new GameObject(side.name);group.transform.SetParent(bank.transform,false);
                            Transform postTemplate=null;
                            foreach(Transform piece in side)
                            {
                                if(piece.name.StartsWith("ColumnPost_")) {if(postTemplate==null)postTemplate=piece;continue;}
                                var beam=UnityEngine.Object.Instantiate(piece.gameObject,group.transform,false);
                                Vector3 position=beam.transform.localPosition;position.z=length*.5f;beam.transform.localPosition=position;
                                Vector3 scale=beam.transform.localScale;scale.z*=length/24f;beam.transform.localScale=scale;
                            }
                            if(postTemplate==null)throw new InvalidOperationException("Road column post template missing.");
                            int count=Mathf.Max(2,Mathf.CeilToInt((length-.6f)/4f)+1);
                            for(int i=0;i<count;i++)
                            {
                                var post=UnityEngine.Object.Instantiate(postTemplate.gameObject,group.transform,false);
                                post.name="ColumnPost_"+(i+1).ToString("00");
                                Bounds postBounds=default;bool first=true;
                                foreach(var mesh in post.GetComponentsInChildren<MeshFilter>())
                                {var b=InFrame(mesh,bank.transform);if(first){postBounds=b;first=false;}else postBounds.Encapsulate(b);}
                                float inset=postBounds.extents.z+.05f;
                                float target=Mathf.Lerp(inset,length-inset,i/(float)(count-1));
                                post.transform.localPosition+=Vector3.forward*(target-postBounds.center.z);posts++;
                            }
                            // Measure every new mesh in its ramp frame: nothing may overhang a lip.
                            foreach(var filter in group.GetComponentsInChildren<MeshFilter>())
                            {
                                Bounds bounds=InFrame(filter,bank.transform);
                                if(bounds.min.z<-.001f || bounds.max.z>length+.001f)throw new InvalidOperationException(file+": railing crosses a ramp lip: "+filter.name+" "+bounds);
                                if(bounds.min.x<2.999f && bounds.max.x> -2.999f)throw new InvalidOperationException(file+": railing enters playable corridor.");
                            }
                        }
                    }
                    if(holder.GetComponentsInChildren<Collider>().Length!=0)throw new InvalidOperationException(file+": reference railings contain colliders.");
                    PrefabUtility.SaveAsPrefabAsset(root,path);
                    Debug.Log($"Dungeon gap railings added: {file}, {posts} posts; ramp slopes followed, gap opening and floor colliders preserved.");
                }
                finally {PrefabUtility.UnloadPrefabContents(root);}
            }
        }
        finally {PrefabUtility.UnloadPrefabContents(road);}
    }
    private static Bounds InFrame(MeshFilter mesh,Transform frame)
    {
        Bounds bounds=mesh.sharedMesh.bounds,result=default;
        for(int i=0;i<8;i++)
        {
            Vector3 p=bounds.center+Vector3.Scale(bounds.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));
            p=frame.InverseTransformPoint(mesh.transform.TransformPoint(p));
            if(i==0)result=new Bounds(p,Vector3.zero);else result.Encapsulate(p);
        }
        return result;
    }
}
