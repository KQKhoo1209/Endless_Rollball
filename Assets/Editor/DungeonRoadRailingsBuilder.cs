using System;
using UnityEditor;
using UnityEngine;
using EndlessRollball.Track;

[InitializeOnLoad]
public static class DungeonRoadRailingsBuilder
{
    private const string Road="Assets/EndlessRollballAssets/Environment Prefab/Roads/TRK_Dungeon_Road_24m.prefab";
    private const string Column="Assets/EndlessRollballAssets/Dungeon/KayKit_Dungeon_Pack_1.1_FREE/Assets/fbx(unity)/column.fbx";
    private const string Materials="Assets/EndlessRollballAssets/Obstacles/Dungeon/Texture/";
    static DungeonRoadRailingsBuilder() {EditorApplication.delayCall+=WhenReady;}
    private static void WhenReady()
    {
        if(EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
        {EditorApplication.delayCall+=WhenReady;return;}
        var asset=AssetDatabase.LoadAssetAtPath<GameObject>(Road);
        if(asset!=null && asset.GetComponent<TrackSegment>().VisualRoot.Find("SideRailings") == null) Build();
    }
    [MenuItem("Tools/Endless Rollball/Add Dungeon Road Side Railings")]
    public static void Build()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode) return;
        var root=PrefabUtility.LoadPrefabContents(Road);
        try
        {
            var track=root.GetComponent<TrackSegment>();
            if(track.VisualRoot.Find("SideRailings")!=null) {Debug.Log("Dungeon road railings already exist; edit SideRailings in Prefab Mode.");return;}
            BoxCollider floor=null;foreach(var box in root.GetComponentsInChildren<BoxCollider>())if(box.name=="FloorCollider")floor=box;
            if(floor==null)throw new InvalidOperationException("Dungeon FloorCollider missing.");
            Bounds deck=TransformBounds(floor.transform,root.transform,new Bounds(floor.center,floor.size));
            if(deck.size.x<6.7f)throw new InvalidOperationException("Road needs at least 6.7m total width for these rails outside a 6m corridor.");
            var source=AssetDatabase.LoadAssetAtPath<GameObject>(Column);if(source==null)throw new InvalidOperationException("KayKit Dungeon column missing.");
            var panel=Material("DungeonRailStone",new Color(.25f,.28f,.30f));
            var cap=Material("DungeonRailCap",new Color(.34f,.37f,.39f));
            var holder=new GameObject("SideRailings");holder.transform.SetParent(track.VisualRoot,false);
            holder.transform.position=root.transform.position;holder.transform.rotation=root.transform.rotation;
            float top=deck.max.y;
            foreach(int side in new[]{-1,1})
            {
                var bank=new GameObject(side<0?"Left":"Right");bank.transform.SetParent(holder.transform,false);
                float x=side<0?deck.min.x+.25f:deck.max.x-.25f;
                Beam(bank.transform,"StoneWallPanel",new Vector3(x,top+.5f,deck.center.z),new Vector3(.3f,.8f,deck.size.z),panel);
                Beam(bank.transform,"TopHandrail",new Vector3(x,top+1.0f,deck.center.z),new Vector3(.42f,.18f,deck.size.z),cap);
                for(int i=0;i<6;i++)
                {
                    var post=(GameObject)PrefabUtility.InstantiatePrefab(source);post.name="ColumnPost_"+(i+1).ToString("00");post.transform.SetParent(bank.transform,false);
                    Bounds b=MeshBounds(post,holder.transform);
                    post.transform.localScale=Vector3.Scale(post.transform.localScale,new Vector3(.48f/b.size.x,1.4f/b.size.y,.48f/b.size.z));b=MeshBounds(post,holder.transform);
                    Vector3 target=new Vector3(x,top+b.extents.y,deck.min.z+2+i*4);
                    post.transform.position+=holder.transform.TransformVector(target-b.center);
                    foreach(var collider in post.GetComponentsInChildren<Collider>())UnityEngine.Object.DestroyImmediate(collider);
                }
            }
            foreach(var renderer in holder.GetComponentsInChildren<MeshRenderer>())
            {
                var filter=renderer.GetComponent<MeshFilter>();if(filter==null)continue;
                Bounds b=TransformBounds(renderer.transform,root.transform,filter.sharedMesh.bounds);
                if(b.min.x<3f-.001f && b.max.x>-3f+.001f)throw new InvalidOperationException("Railing intrudes into 6m playable corridor: "+renderer.name);
                if(b.min.z<deck.min.z-.001f || b.max.z>deck.max.z+.001f)throw new InvalidOperationException("Railing extends past road segment.");
            }
            if(holder.GetComponentsInChildren<Collider>().Length!=0)throw new InvalidOperationException("Decorative railings must not add obstacle colliders.");
            PrefabUtility.SaveAsPrefabAsset(root,Road);
            Debug.Log($"Dungeon road railings built: deck {deck.size.x:F2}m, 12 KayKit column posts, 4 stone beams; all outside 6m corridor. Floor and anchors preserved.");
        }
        finally {PrefabUtility.UnloadPrefabContents(root);}
    }
    private static Bounds TransformBounds(Transform from,Transform to,Bounds bounds)
    {
        Bounds result=default;for(int i=0;i<8;i++)
        {
            Vector3 p=bounds.center+Vector3.Scale(bounds.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));
            p=to.InverseTransformPoint(from.TransformPoint(p));if(i==0)result=new Bounds(p,Vector3.zero);else result.Encapsulate(p);
        }
        return result;
    }
    private static Bounds MeshBounds(GameObject model,Transform frame)
    {
        bool first=true;Bounds result=default;
        foreach(var filter in model.GetComponentsInChildren<MeshFilter>())
        {if(filter.sharedMesh==null)continue;var b=TransformBounds(filter.transform,frame,filter.sharedMesh.bounds);if(first){result=b;first=false;}else result.Encapsulate(b);}
        if(first)throw new InvalidOperationException("Column has no mesh.");return result;
    }
    private static Material Material(string name,Color color)
    {
        string path=Materials+name+".mat";var material=AssetDatabase.LoadAssetAtPath<Material>(path);if(material!=null)return material;
        material=new Material(Shader.Find("Universal Render Pipeline/Lit"));material.SetColor("_BaseColor",color);material.SetFloat("_Smoothness",.15f);
        AssetDatabase.CreateAsset(material,path);return material;
    }
    private static void Beam(Transform parent,string name,Vector3 position,Vector3 size,Material material)
    {
        var beam=GameObject.CreatePrimitive(PrimitiveType.Cube);beam.name=name;beam.transform.SetParent(parent,false);beam.transform.localPosition=position;beam.transform.localScale=size;
        beam.GetComponent<Renderer>().sharedMaterial=material;UnityEngine.Object.DestroyImmediate(beam.GetComponent<Collider>());
    }
}
