using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using EndlessRollball.Track;

// Runs once after import. Existing authored encounter assets are never overwritten automatically.
[InitializeOnLoad]
public static class BeachBarrierAssetBuilder
{
    private const string Folder="Assets/EndlessRollballAssets/Obstacles/Beach/BarriersV2";
    private const string Road="Assets/EndlessRollballAssets/Environment Prefab/Roads/TRK_Beach_Road_24m.prefab";
    private const string Library="Assets/EndlessRollballAssets/Obstacles/Beach/Beach_ObstacleLibrary.asset";
    private const string Shared="Assets/EndlessRollballAssets/Obstacles/Beach/Prefabs/OBS_Beach_Jump.prefab";
    static BeachBarrierAssetBuilder() { EditorApplication.delayCall+=AutoBuild; }
    private static void AutoBuild()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
        { EditorApplication.delayCall+=AutoBuild; return; }
        if(AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"/TRK_Beach_ExplosiveRoller_24m.prefab")==null) Build();
    }
    [MenuItem("Tools/Endless Rollball/Build Beach Barrier Encounters")]
    public static void Build()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("Build Beach barriers outside Play Mode."); return; }
        System.IO.Directory.CreateDirectory(Folder); AssetDatabase.Refresh();
        var library=AssetDatabase.LoadAssetAtPath<ThemeObstacleLibrary>(Library);
        if(library==null) throw new InvalidOperationException("Beach obstacle library missing.");
        var tracks=new List<BeachBarrierEncounter>();
        foreach(bool explosive in new[]{false,true})
        {
            string path=Folder+(explosive?"/TRK_Beach_ExplosiveRoller_24m.prefab":"/TRK_Beach_RollingBarrier_24m.prefab");
            var existing=AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if(existing!=null) { tracks.Add(existing.GetComponent<BeachBarrierEncounter>()); continue; }
            var root=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Road));
            PrefabUtility.UnpackPrefabInstance(root,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
            try
            {
                root.name=System.IO.Path.GetFileNameWithoutExtension(path);
                var track=root.GetComponent<TrackSegment>();
                track.Configure(root.name,track.EntryAnchor,track.ExitAnchor,explosive?TrackSegmentKind.Gap:TrackSegmentKind.MovingObstacle,
                    DifficultyModeMask.All,TrackLaneMask.All,TrackLaneMask.All,explosive,1,1,1,1);
                GameObject patch=null;
                if(explosive)
                {
                    patch=new GameObject("RemovableDeck"); patch.transform.SetParent(track.VisualRoot,false);
                    // Split all visual geometry, including edge beams, at both lips without scaling it.
                    foreach(var filter in track.VisualRoot.GetComponentsInChildren<MeshFilter>())
                    {
                        if(filter.sharedMesh==null) continue;
                        var renderer=filter.GetComponent<MeshRenderer>(); if(renderer==null) continue;
                        MakeSlice(root,filter,renderer,patch.transform,10.4f,13.6f,"Patch");
                        MakeSlice(root,filter,renderer,filter.transform.parent,float.NegativeInfinity,10.4f,"Before");
                        MakeSlice(root,filter,renderer,filter.transform.parent,13.6f,float.PositiveInfinity,"After");
                        renderer.enabled=false;
                    }
                    foreach(var oldCollider in root.GetComponentsInChildren<Collider>()) UnityEngine.Object.DestroyImmediate(oldCollider);
                    Floor(root.transform,"Floor_Takeoff",5.2f,10.4f);
                    Floor(root.transform,"Floor_Landing",18.8f,10.4f);
                    Floor(patch.transform,"FloorCollider",12f,3.2f);
                }
                var bodyObject=new GameObject("MovingBarrier"); bodyObject.transform.SetParent(root.transform,false);
                bodyObject.transform.localPosition=new Vector3(0,.3f,explosive?22:25);
                var body=bodyObject.AddComponent<Rigidbody>(); body.isKinematic=true; body.useGravity=false;
                body.interpolation=RigidbodyInterpolation.Interpolate;
                var shape=bodyObject.AddComponent<BoxCollider>(); shape.size=explosive?new Vector3(1.4f,1.1f,1.1f):new Vector3(7f,.6f,.6f);
                var visual=new GameObject("BarrierVisual"); visual.transform.SetParent(bodyObject.transform,false);
                var model=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(explosive?"Assets/EndlessRollballAssets/Obstacles/KayKit_ResourceBits_1.0_FREE/Assets/fbx(unity)/Fuel_A_Barrel.fbx":Shared));
                model.transform.SetParent(visual.transform,false);
                if(explosive)
                {
                    model.transform.localRotation=Quaternion.Euler(0,0,90);
                    string redPath=Folder+"/FuelBarrelRed.mat";
                    var red=AssetDatabase.LoadAssetAtPath<Material>(redPath);
                    if(red==null) {red=new Material(Shader.Find("Universal Render Pipeline/Lit"));red.SetColor("_BaseColor",new Color(.65f,.025f,.015f));red.SetFloat("_Metallic",.55f);red.SetFloat("_Smoothness",.35f);AssetDatabase.CreateAsset(red,redPath);}
                    foreach(var renderer in model.GetComponentsInChildren<Renderer>())
                    {var materials=renderer.sharedMaterials;for(int i=0;i<materials.Length;i++) materials[i]=red;renderer.sharedMaterials=materials;}
                }
                foreach(var c in model.GetComponentsInChildren<Collider>()) UnityEngine.Object.DestroyImmediate(c);
                foreach(var c in model.GetComponentsInChildren<MonoBehaviour>()) UnityEngine.Object.DestroyImmediate(c);
                Bounds bounds=new Bounds(); bool first=true;
                foreach(var renderer in model.GetComponentsInChildren<Renderer>()) { if(first) {bounds=renderer.bounds;first=false;} else bounds.Encapsulate(renderer.bounds); }
                if(first) throw new InvalidOperationException("Shared wooden barrier has no renderers.");
                model.transform.localScale=Vector3.Scale(model.transform.localScale,new Vector3((explosive?1.4f:7f)/bounds.size.x,(explosive?1.1f:.6f)/bounds.size.y,(explosive?1.1f:.6f)/bounds.size.z));
                // Re-measure after scaling to centre the reused model on the body.
                first=true; foreach(var renderer in model.GetComponentsInChildren<Renderer>()) {if(first){bounds=renderer.bounds;first=false;}else bounds.Encapsulate(renderer.bounds);}
                model.transform.position+=bodyObject.transform.position-bounds.center;
                var flames=new List<ParticleSystem>(); var bursts=new List<ParticleSystem>(); var edges=new List<ParticleSystem>();
                if(explosive)
                {
                    flames.Add(Flame(bodyObject.transform,Vector3.zero,false));
                    flames.Add(Flame(bodyObject.transform,new Vector3(0,.6f,0),false,true));
                    bursts.Add(Flame(root.transform,new Vector3(0,.3f,12),true));
                    bursts.Add(Flame(root.transform,new Vector3(0,.5f,12),true,true));
                    foreach(float z in new[]{10.25f,13.75f})
                        foreach(float x in new[]{-2.5f,2.5f}) edges.Add(Flame(root.transform,new Vector3(x,0,z),false));
                }
                BoxCollider sensor=null;
                if(explosive)
                {var go=new GameObject("GapFallSensor");go.transform.SetParent(root.transform,false);sensor=go.AddComponent<BoxCollider>();sensor.isTrigger=true;sensor.center=new Vector3(0,-.15f,12);sensor.size=new Vector3(7,.35f,2.6f);sensor.enabled=false;}
                var encounter=root.AddComponent<BeachBarrierEncounter>();
                encounter.Configure(explosive,visual.transform,body,shape,patch,flames.ToArray(),bursts.ToArray(),edges.ToArray(),sensor);
                var saved=PrefabUtility.SaveAsPrefabAsset(root,path); tracks.Add(saved.GetComponent<BeachBarrierEncounter>());
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        var serialized=new SerializedObject(library); var array=serialized.FindProperty("beachBarrierTracks"); array.arraySize=tracks.Count;
        for(int i=0;i<tracks.Count;i++) array.GetArrayElementAtIndex(i).objectReferenceValue=tracks[i];
        serialized.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(library); AssetDatabase.SaveAssets();
        Debug.Log("Beach barriers built and assigned. Play Mode acceptance is still required.");
    }
    private static void Floor(Transform parent,string name,float z,float length)
    { var go=new GameObject(name); go.transform.SetParent(parent,false); var box=go.AddComponent<BoxCollider>(); box.center=new Vector3(0,-.25f,z); box.size=new Vector3(7,.5f,length); }
    private static ParticleSystem Flame(Transform parent,Vector3 position,bool burst,bool smoke=false)
    {
        var go=new GameObject(smoke?"Smoke":burst?"ExplosionFireball":"Flames");go.transform.SetParent(parent,false);go.transform.localPosition=position;
        go.transform.localRotation=Quaternion.Euler(-90,0,0);
        var ps=go.AddComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        var main=ps.main;main.playOnAwake=false;main.loop=!burst;main.duration=burst?1f:2f;
        main.startLifetime=smoke?new ParticleSystem.MinMaxCurve(1.4f,2.6f):new ParticleSystem.MinMaxCurve(.45f,.95f);
        main.startSpeed=burst?new ParticleSystem.MinMaxCurve(3f,7f):new ParticleSystem.MinMaxCurve(.8f,2.1f);
        main.startSize=smoke?new ParticleSystem.MinMaxCurve(.8f,1.5f):burst?new ParticleSystem.MinMaxCurve(1.2f,2.4f):new ParticleSystem.MinMaxCurve(.45f,.9f);
        main.startRotation=new ParticleSystem.MinMaxCurve(0,Mathf.PI*2);main.maxParticles=burst?150:80;
        main.simulationSpace=ParticleSystemSimulationSpace.World;
        var emission=ps.emission;emission.rateOverTime=burst?0:smoke?12:40;
        if(burst) emission.SetBursts(new[]{new ParticleSystem.Burst(0,(short)(smoke?45:110))});
        var shape=ps.shape;shape.shapeType=burst?ParticleSystemShapeType.Hemisphere:ParticleSystemShapeType.Cone;shape.radius=burst?1.8f:.35f;shape.angle=burst?45:12;
        var color=ps.colorOverLifetime;color.enabled=true;
        var gradient=new Gradient();gradient.SetKeys(smoke?
            new[]{new GradientColorKey(new Color(.15f,.12f,.1f),0),new GradientColorKey(new Color(.35f,.33f,.3f),1)}:
            new[]{new GradientColorKey(new Color(1,1,.6f),0),new GradientColorKey(new Color(1,.35f,.025f),.35f),new GradientColorKey(new Color(.5f,.025f,.005f),1)},
            new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(smoke?.5f:1,.1f),new GradientAlphaKey(.7f,.5f),new GradientAlphaKey(0,1)});color.color=gradient;
        var size=ps.sizeOverLifetime;size.enabled=true;size.size=new ParticleSystem.MinMaxCurve(1,AnimationCurve.EaseInOut(0,smoke?.5f:.8f,1,smoke?2.5f:.05f));
        var noise=ps.noise;noise.enabled=true;noise.strength=smoke?.35f:.18f;noise.frequency=.7f;noise.scrollSpeed=.5f;
        string matPath=Folder+(smoke?"/Smoke.mat":"/Fire.mat");var material=AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if(material==null)
        {
            var texture=new Texture2D(64,64,TextureFormat.RGBA32,false);texture.name=smoke?"SmokeSoft":"FlameSoft";
            var pixels=new Color[64*64];for(int y=0;y<64;y++)for(int x=0;x<64;x++)
            {float u=(x-31.5f)/31.5f,v=(y-31.5f)/31.5f;float radius=Mathf.Sqrt(u*u+v*v);float alpha=Mathf.Pow(Mathf.Clamp01(1-radius),smoke?1.3f:1.8f);alpha*=.65f+.35f*Mathf.PerlinNoise(x*.14f,y*.14f);pixels[y*64+x]=new Color(1,1,1,alpha);}
            texture.SetPixels(pixels);texture.Apply();AssetDatabase.CreateAsset(texture,Folder+(smoke?"/SmokeSoft.asset":"/FlameSoft.asset"));
            material=new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));material.SetTexture("_BaseMap",texture);material.SetFloat("_Surface",1);material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetFloat("_Blend",smoke?0:2);material.SetFloat("_SrcBlend",(float)UnityEngine.Rendering.BlendMode.SrcAlpha);material.SetFloat("_DstBlend",(float)(smoke?UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha:UnityEngine.Rendering.BlendMode.One));material.SetFloat("_ZWrite",0);material.renderQueue=3000;AssetDatabase.CreateAsset(material,matPath);
        }
        ps.GetComponent<ParticleSystemRenderer>().sharedMaterial=material;
        return ps;
    }
    private struct Vertex { public Vector3 p,n; public Vector2 uv; }
    private static Vertex Lerp(Vertex a,Vertex b,float t) => new Vertex {p=Vector3.LerpUnclamped(a.p,b.p,t),n=Vector3.LerpUnclamped(a.n,b.n,t).normalized,uv=Vector2.LerpUnclamped(a.uv,b.uv,t)};
    private static List<Vertex> Clip(List<Vertex> input,Transform source,Transform root,float plane,bool above)
    {
        var output=new List<Vertex>(); if(float.IsInfinity(plane)) return input;
        for(int i=0;i<input.Count;i++)
        {var a=input[i]; var b=input[(i+1)%input.Count]; float az=root.InverseTransformPoint(source.TransformPoint(a.p)).z-plane;
         float bz=root.InverseTransformPoint(source.TransformPoint(b.p)).z-plane; bool ai=above?az>=0:az<=0, bi=above?bz>=0:bz<=0;
         if(ai) output.Add(a); if(ai!=bi) output.Add(Lerp(a,b,az/(az-bz)));}
        return output;
    }
    private static void MakeSlice(GameObject root,MeshFilter filter,MeshRenderer renderer,Transform parent,float low,float high,string suffix)
    {
        var source=filter.sharedMesh;
        using var data=UnityEditor.MeshUtility.AcquireReadOnlyMeshData(source);
        var md=data[0]; using var vertices=new NativeArray<Vector3>(md.vertexCount,Allocator.Temp); md.GetVertices(vertices);
        using var normals=new NativeArray<Vector3>(md.vertexCount,Allocator.Temp); if(md.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Normal)) md.GetNormals(normals);
        using var uvs=new NativeArray<Vector2>(md.vertexCount,Allocator.Temp); if(md.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord0)) md.GetUVs(0,uvs);
        var positions=new List<Vector3>(); var ns=new List<Vector3>(); var tex=new List<Vector2>(); var submeshes=new List<int[]>();
        for(int sub=0;sub<md.subMeshCount;sub++)
        {
            var desc=md.GetSubMesh(sub); using var indices=new NativeArray<int>(desc.indexCount,Allocator.Temp); md.GetIndices(indices,sub);
            var triangles=new List<int>();
            for(int t=0;t+2<indices.Length;t+=3)
            {
                var polygon=new List<Vertex>(); for(int k=0;k<3;k++){int v=indices[t+k];polygon.Add(new Vertex {p=vertices[v],n=normals[v],uv=uvs[v]});}
                polygon=Clip(Clip(polygon,filter.transform,root.transform,low,true),filter.transform,root.transform,high,false);
                for(int k=1;k+1<polygon.Count;k++) foreach(var v in new[]{polygon[0],polygon[k],polygon[k+1]})
                {triangles.Add(positions.Count);positions.Add(v.p);ns.Add(v.n);tex.Add(v.uv);}
            }
            submeshes.Add(triangles.ToArray());
        }
        if(positions.Count==0) return;
        var mesh=new Mesh {name=filter.name+suffix,indexFormat=UnityEngine.Rendering.IndexFormat.UInt32}; mesh.SetVertices(positions);mesh.SetNormals(ns);mesh.SetUVs(0,tex);mesh.subMeshCount=submeshes.Count;
        for(int s=0;s<submeshes.Count;s++) mesh.SetTriangles(submeshes[s],s); mesh.RecalculateBounds();
        string path=AssetDatabase.GenerateUniqueAssetPath(Folder+"/"+root.name+"_"+suffix+".asset");AssetDatabase.CreateAsset(mesh,path);
        var go=new GameObject(filter.name+"_"+suffix); go.transform.SetParent(parent,false);
        go.transform.SetPositionAndRotation(filter.transform.position,filter.transform.rotation);
        // Parents share the same unscaled road coordinate system, except original mesh parents.
        go.transform.localScale=parent==filter.transform.parent?filter.transform.localScale:Divide(filter.transform.lossyScale,parent.lossyScale);
        go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterials=renderer.sharedMaterials;
    }
    private static Vector3 Divide(Vector3 a,Vector3 b)=>new Vector3(a.x/b.x,a.y/b.y,a.z/b.z);
}
