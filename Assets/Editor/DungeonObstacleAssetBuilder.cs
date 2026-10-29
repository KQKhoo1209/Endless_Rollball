using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using EndlessRollball.Track;

[InitializeOnLoad]
public static class DungeonObstacleAssetBuilder
{
    private const string Folder="Assets/EndlessRollballAssets/Obstacles/Dungeon";
    private const string Column="Assets/EndlessRollballAssets/Dungeon/KayKit Dungeon Pack/KayKit_Dungeon_Pack_1.1_FREE/Assets/fbx(unity)/pillar.fbx";
    static DungeonObstacleAssetBuilder() { EditorApplication.delayCall+=AutoBuild; }
    private static void AutoBuild()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
        { EditorApplication.delayCall+=AutoBuild; return; }
        if(AssetDatabase.LoadAssetAtPath<ThemeObstacleLibrary>(Folder+"/Dungeon_ObstacleLibrary.asset")==null) Build();
        UpgradeEffects();
        UpdatePillarModels();
        BuildJumpPillars();
    }
    [MenuItem("Tools/Endless Rollball/Build Dungeon Obstacles")]
    public static void Build()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode) return;
        System.IO.Directory.CreateDirectory(Folder); AssetDatabase.Refresh();
        var stone=Material("Stone",new Color(.29f,.28f,.25f));
        var warning=Material("Warning",new Color(1,.35f,.035f));
        var pillars=new List<GameObject>(); var traps=new List<DungeonTrapEncounter>();
        foreach(int side in new[]{-1,1})
        {
            string suffix=side<0?"Left":"Right";
            pillars.Add(Save("CollapsedPillar_"+suffix,root=>
            {
                var model=AssetDatabase.LoadAssetAtPath<GameObject>(Column);
                if(model==null) throw new InvalidOperationException("Dungeon column model missing.");
                var go=(GameObject)PrefabUtility.InstantiatePrefab(model);go.transform.SetParent(root.transform,false);
                go.transform.localRotation=Quaternion.Euler(0,0,90);
                Fit(go,new Vector3(2.7f,1.3f,1.8f),new Vector3(side*1.65f,.65f,0));
                foreach(var c in go.GetComponentsInChildren<Collider>()) UnityEngine.Object.DestroyImmediate(c);
                var collider=root.AddComponent<BoxCollider>(); collider.center=new Vector3(side*1.65f,.65f,0);collider.size=new Vector3(2.7f,1.3f,1.8f);
                for(int i=0;i<3;i++) Cube(root.transform,"Fragment",new Vector3(side*(.8f+i*.6f),.12f,.95f),new Vector3(.25f,.24f,.3f),stone);
            }));
            foreach(var kind in new[]{DungeonTrapEncounter.TrapKind.Rubble,DungeonTrapEncounter.TrapKind.SlidingBlock})
                traps.Add(Save(kind+"_"+suffix,root=>MakeTrap(root,kind,side,stone,warning)).GetComponent<DungeonTrapEncounter>());
        }
        traps.Add(Save("FireBarrier",root=>MakeTrap(root,DungeonTrapEncounter.TrapKind.Fire,0,stone,warning)).GetComponent<DungeonTrapEncounter>());
        var library=ScriptableObject.CreateInstance<ThemeObstacleLibrary>();
        var serialized=new SerializedObject(library);
        var families=serialized.FindProperty("fixedFamilies");families.arraySize=1;
        var family=families.GetArrayElementAtIndex(0);family.FindPropertyRelative("familyId").stringValue="Dungeon.CollapsedPillar";
        var variants=family.FindPropertyRelative("variants");variants.arraySize=2;
        for(int i=0;i<2;i++) variants.GetArrayElementAtIndex(i).objectReferenceValue=pillars[i];
        var array=serialized.FindProperty("dungeonTraps");array.arraySize=traps.Count;
        for(int i=0;i<traps.Count;i++) array.GetArrayElementAtIndex(i).objectReferenceValue=traps[i];
        serialized.ApplyModifiedPropertiesWithoutUndo();
        string path=Folder+"/Dungeon_ObstacleLibrary.asset";
        var existing=AssetDatabase.LoadAssetAtPath<ThemeObstacleLibrary>(path);
        if(existing!=null) { UnityEngine.Object.DestroyImmediate(library); library=existing; }
        else AssetDatabase.CreateAsset(library,path);
        var config=AssetDatabase.LoadMainAssetAtPath("Assets/EndlessRollballAssets/Configuration/Environment/GameplayScene_Dungeon.asset");
        var settings=new SerializedObject(config);settings.FindProperty("obstacleLibrary").objectReferenceValue=library;settings.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(config);AssetDatabase.SaveAssets();
        Debug.Log("Dungeon obstacles built: 2 pillars, fire, 2 rubble and 2 sliding traps; Dungeon theme library assigned. Play Mode validation required.");
    }
    private static GameObject Save(string name,Action<GameObject> populate)
    {
        string path=Folder+"/"+name+".prefab";var existing=AssetDatabase.LoadAssetAtPath<GameObject>(path);if(existing!=null) return existing;
        var root=new GameObject(name);
        try {populate(root);return PrefabUtility.SaveAsPrefabAsset(root,path);} finally {UnityEngine.Object.DestroyImmediate(root);}
    }
    // One-time visual replacement; later manual edits to the KayKit child are preserved.
    [MenuItem("Tools/Endless Rollball/Use KayKit Dungeon Pillars")]
    private static void UpdatePillarModels()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode) return;
        var model=AssetDatabase.LoadAssetAtPath<GameObject>(Column);
        if(model==null) { Debug.LogWarning("KayKit Dungeon pillar model missing."); return; }
        foreach(string suffix in new[]{"Left","Right"})
        {
            string path=Folder+"/CollapsedPillar_"+suffix+".prefab";
            var asset=AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if(asset==null || asset.GetComponent<DungeonTrapEncounter>()!=null || asset.transform.Find("KayKitPillar")!=null) continue;
            var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                var old=root.transform.Find("Column_Round");
                if(old!=null) UnityEngine.Object.DestroyImmediate(old.gameObject);
                // Also replace the child created by subsequent builds from the KayKit model.
                old=root.transform.Find("pillar");
                if(old!=null) UnityEngine.Object.DestroyImmediate(old.gameObject);
                var visual=(GameObject)PrefabUtility.InstantiatePrefab(model);
                visual.name="KayKitPillar";visual.transform.SetParent(root.transform,false);
                visual.transform.localRotation=Quaternion.Euler(0,0,90);
                Bounds bounds=Measure(visual);
                float factor=Mathf.Min(2.7f/bounds.size.x,1.3f/bounds.size.y,1.8f/bounds.size.z);
                visual.transform.localScale*=factor;
                bounds=Measure(visual);
                var target=root.transform.TransformPoint(new Vector3(suffix=="Left"?-1.65f:1.65f,bounds.extents.y,0));
                visual.transform.position+=target-bounds.center;
                bounds=Measure(visual);
                var shape=root.GetComponent<BoxCollider>();
                shape.center=root.transform.InverseTransformPoint(bounds.center);shape.size=bounds.size;
                foreach(var collider in visual.GetComponentsInChildren<Collider>()) UnityEngine.Object.DestroyImmediate(collider);
                PrefabUtility.SaveAsPrefabAsset(root,path);
                Debug.Log("Updated "+root.name+" to KayKit Dungeon pillar, preserving model proportions and fitting its collider.");
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
        }
    }
    [MenuItem("Tools/Endless Rollball/Build KayKit Jump Pillars")]
    private static void BuildJumpPillars()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode) return;
        var library=AssetDatabase.LoadAssetAtPath<ThemeObstacleLibrary>(Folder+"/Dungeon_ObstacleLibrary.asset");
        var source=AssetDatabase.LoadAssetAtPath<GameObject>(Column);
        if(library==null || source==null) return;
        foreach(int side in new[]{-1,1})
        {
            string suffix=side<0?"Left":"Right";
            var prefab=Save("CollapsedPillar_Jump_"+suffix,root=>
            {
                var mover=new GameObject("PillarHazard");mover.transform.SetParent(root.transform,false);
                mover.transform.localPosition=new Vector3(0,.275f,0);
                var body=mover.AddComponent<Rigidbody>();body.isKinematic=true;body.useGravity=false;
                var shape=mover.AddComponent<BoxCollider>();shape.size=new Vector3(6.6f,.55f,.55f);
                var visual=(GameObject)PrefabUtility.InstantiatePrefab(source);visual.name="KayKitPillar";visual.transform.SetParent(mover.transform,false);
                visual.transform.localRotation=Quaternion.Euler(0,0,side*90);
                // Elongate the shaft while retaining equal height/depth cross-section.
                Fit(visual,shape.size,mover.transform.position);
                foreach(var collider in visual.GetComponentsInChildren<Collider>()) UnityEngine.Object.DestroyImmediate(collider);
                root.AddComponent<DungeonTrapEncounter>().Configure(DungeonTrapEncounter.TrapKind.PillarJump,body,shape,mover.transform.localPosition,mover.transform.localPosition,null);
            });
            var serialized=new SerializedObject(library);var array=serialized.FindProperty("dungeonTraps");
            var encounter=prefab.GetComponent<DungeonTrapEncounter>();bool found=false;
            for(int i=0;i<array.arraySize;i++) if(array.GetArrayElementAtIndex(i).objectReferenceValue==encounter)found=true;
            if(!found)
            {
                int index=array.arraySize;array.arraySize++;
                array.GetArrayElementAtIndex(index).objectReferenceValue=encounter;
                serialized.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(library);AssetDatabase.SaveAssets();
                Debug.Log("Added KayKit full-width jump pillar: "+suffix);
            }
        }
    }
    private static void MakeTrap(GameObject root,DungeonTrapEncounter.TrapKind kind,int side,Material stone,Material orange)
    {
        bool fire=kind==DungeonTrapEncounter.TrapKind.Fire;
        var marker=new GameObject("WarningFootprint");marker.transform.SetParent(root.transform,false);
        float x=side*1.65f;
        foreach(float z in new[]{-1.1f,1.1f}) Cube(marker.transform,"Outline",new Vector3(x,.025f,z),new Vector3(fire?6:2.7f,.03f,.07f),orange);
        var mover=new GameObject("Hazard");mover.transform.SetParent(root.transform,false);
        var body=mover.AddComponent<Rigidbody>();body.isKinematic=true;body.useGravity=false;body.interpolation=RigidbodyInterpolation.Interpolate;
        var shape=mover.AddComponent<BoxCollider>();shape.isTrigger=fire;
        shape.size=fire?new Vector3(6.6f,.5f,.6f):new Vector3(2.7f,1.5f,2.1f);
        Vector3 settled=fire?new Vector3(0,.25f,0):new Vector3(x,.75f,0);
        Vector3 initial=kind==DungeonTrapEncounter.TrapKind.Rubble?settled+Vector3.up*5:kind==DungeonTrapEncounter.TrapKind.SlidingBlock?settled+new Vector3(side*3,0,0):settled;
        mover.transform.localPosition=initial;
        if(fire)
        {
            Fire(mover.transform,false);Fire(mover.transform,true);
            foreach(float edge in new[]{-3.5f,3.5f}) Cube(root.transform,"Brazier",new Vector3(edge,.25f,0),new Vector3(.4f,.5f,.8f),stone);
        }
        else if(kind==DungeonTrapEncounter.TrapKind.Rubble)
        {
            for(int i=0;i<6;i++) Cube(mover.transform,"Stone",new Vector3((i%2-.5f)*1.25f,0,(i/2-1)*.65f),new Vector3(1.25f,1.5f,.65f),stone);
        }
        else
        {
            Cube(mover.transform,"StoneBlock",Vector3.zero,shape.size,stone);
            // A self-contained dark recess masks the local wall entry.
            Cube(root.transform,"WallRecess",new Vector3(side*3.18f,1,0),new Vector3(.25f,2.3f,2.6f),Material("Recess",new Color(.035f,.03f,.025f)));
        }
        root.AddComponent<DungeonTrapEncounter>().Configure(kind,body,shape,initial,settled,marker);
    }
    private static Material Material(string name,Color color)
    {
        string path=Folder+"/"+name+".mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(path);if(mat!=null)return mat;
        mat=new Material(Shader.Find("Universal Render Pipeline/Lit"));mat.SetColor("_BaseColor",color);AssetDatabase.CreateAsset(mat,path);return mat;
    }
    private static void Cube(Transform parent,string name,Vector3 centre,Vector3 size,Material mat)
    {var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(parent,false);go.transform.localPosition=centre;go.transform.localScale=size;go.GetComponent<Renderer>().sharedMaterial=mat;UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());}
    private static void Fit(GameObject go,Vector3 size,Vector3 centre)
    {
        Bounds b=Measure(go);go.transform.localScale=Vector3.Scale(go.transform.localScale,new Vector3(size.x/b.size.x,size.y/b.size.y,size.z/b.size.z));
        b=Measure(go);go.transform.position+=centre-b.center;
    }
    private static Bounds Measure(GameObject go)
    {var renderers=go.GetComponentsInChildren<Renderer>();if(renderers.Length==0)throw new InvalidOperationException("Model has no renderers");Bounds b=renderers[0].bounds;foreach(var r in renderers)b.Encapsulate(r.bounds);return b;}
    private static void Fire(Transform parent,bool smoke)
    {
        var go=new GameObject(smoke?"LightSmoke":"ContinuousFlames");go.transform.SetParent(parent,false);go.transform.localPosition=new Vector3(0,-.2f,0);
        var p=go.AddComponent<ParticleSystem>();var main=p.main;main.playOnAwake=false;main.startLifetime=smoke?.6f:.35f;main.startSpeed=smoke?.6f:1.2f;main.startSize=smoke?.2f:.22f;main.maxParticles=400;
        main.startColor=smoke?new Color(.2f,.18f,.15f,.18f):new Color(1,.65f,.08f,.9f);
        var shape=p.shape;shape.shapeType=ParticleSystemShapeType.Box;shape.scale=new Vector3(6.5f,.01f,.5f);shape.rotation=new Vector3(-90,0,0);
        var emission=p.emission;emission.rateOverTime=smoke?15:280;
        var color=p.colorOverLifetime;color.enabled=true;
        var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(new Color(1,.9f,.2f),0),new GradientColorKey(new Color(1,.18f,.015f),1)},new[]{new GradientAlphaKey(smoke?.15f:1,0),new GradientAlphaKey(0,1)});color.color=gradient;
        var size=p.sizeOverLifetime;size.enabled=true;size.size=new ParticleSystem.MinMaxCurve(1,AnimationCurve.Linear(0,1,1,.1f));
        var renderer=p.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=AssetDatabase.GetBuiltinExtraResource<Material>("Default-Particle.mat");
        p.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
    }
    private static void UpgradeEffects()
    {
        foreach(string guid in AssetDatabase.FindAssets("t:Prefab",new[]{Folder}))
        {
            string path=AssetDatabase.GUIDToAssetPath(guid);
            var asset=AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if(asset.GetComponent<DungeonTrapEncounter>()==null || asset.transform.Find("EffectsVersion1")!=null) continue;
            var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                var trap=root.GetComponent<DungeonTrapEncounter>();
                new GameObject("EffectsVersion1").transform.SetParent(root.transform,false);
                foreach(var renderer in root.GetComponentsInChildren<ParticleSystemRenderer>(true))
                    renderer.sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/EndlessRollballAssets/Obstacles/Beach/BarriersV2/"+(renderer.name.Contains("Smoke")?"Smoke":"Fire")+".mat");
                if(!trap.RequiredJump)
                {
                    var go=new GameObject("WarningDust");go.transform.SetParent(root.transform,false);go.transform.localPosition=new Vector3(trap.Kind==DungeonTrapEncounter.TrapKind.Rubble?(root.name.Contains("Left")?-1.65f:1.65f):0,3,0);
                    var p=go.AddComponent<ParticleSystem>();var main=p.main;main.playOnAwake=false;main.startLifetime=.8f;main.startSpeed=.2f;main.startSize=.1f;main.gravityModifier=.15f;main.startColor=new Color(.5f,.45f,.35f,.3f);
                    var emission=p.emission;emission.rateOverTime=20;
                    var shape=p.shape;shape.shapeType=ParticleSystemShapeType.Box;shape.scale=new Vector3(2.5f,.1f,2);
                    p.GetComponent<ParticleSystemRenderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/EndlessRollballAssets/Obstacles/Beach/BarriersV2/Smoke.mat");
                    p.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
                }
                PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }
}
