using System;
using UnityEditor;
using UnityEngine;
using EndlessRollball.Track;

[InitializeOnLoad]
public static class DungeonPillarRefinement
{
    private const string RootFolder="Assets/EndlessRollballAssets/Obstacles/Dungeon/";
    private const string Folder=RootFolder+"Prefabs/";
    static DungeonPillarRefinement() {EditorApplication.delayCall+=WhenReady;}
    private static void WhenReady()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode) return;
        if(EditorApplication.isCompiling || EditorApplication.isUpdating) {EditorApplication.delayCall+=WhenReady;return;}
        if(AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"CollapsedPillar_Jump_Right.prefab")==null) {EditorApplication.delayCall+=WhenReady;return;}
        const string refitRequest="DungeonPillarRefinement.UserRefit.20261030.LargerDust4";
        bool requested=!SessionState.GetBool(refitRequest,false);
        Refine(false);
        if(requested)
        {
            RefreshDust();
            SessionState.SetBool(refitRequest,true);
        }
    }
    [MenuItem("Tools/Endless Rollball/Refit Pillar Colliders To Edited Models")]
    public static void Refit() => Refine(true);
    private static void RefreshDust()
    {
        foreach(string name in new[]{"CollapsedPillar_Left","CollapsedPillar_Right","CollapsedPillar_Jump_Left","CollapsedPillar_Jump_Right"})
        {
            string path=Folder+name+".prefab";var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                var encounter=root.GetComponent<DungeonTrapEncounter>();
                var dust=root.transform.Find("PillarImpactDust").GetComponent<ParticleSystem>();
                ConfigureDust(dust,encounter.SettledBounds);
                dust.Simulate(.1f,true,true,true);
                if(dust.particleCount==0) throw new InvalidOperationException(name+": impact dust emitted no particles.");
                Debug.Log($"Dust burst check: {name}, {dust.particleCount} live particles at 0.1 seconds; larger dust applied.");
                dust.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
                PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
        }
    }
    private static void Refine(bool force)
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode) return;
        var library=AssetDatabase.LoadAssetAtPath<ThemeObstacleLibrary>(RootFolder+"Dungeon_ObstacleLibrary.asset");
        foreach(bool jump in new[]{false,true}) foreach(int side in new[]{-1,1})
        {
            string name="CollapsedPillar_"+(jump?"Jump_":"")+(side<0?"Left":"Right");
            string path=Folder+name+".prefab";
            var asset=AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if(!force && asset.GetComponent<DungeonTrapEncounter>()?.FallsOnApproach==true) continue;
            var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                Transform visual=null;
                foreach(var t in root.GetComponentsInChildren<Transform>()) if(t.name=="KayKitPillar") {visual=t;break;}
                if(visual==null) throw new InvalidOperationException(name+": KayKitPillar visual missing.");
                var body=root.GetComponentInChildren<Rigidbody>();
                if(body==null)
                {
                    var mover=new GameObject("PillarHazard");mover.transform.SetParent(root.transform,false);
                    body=mover.AddComponent<Rigidbody>();
                    visual.SetParent(mover.transform,true);
                    foreach(var old in root.GetComponents<Collider>()) UnityEngine.Object.DestroyImmediate(old);
                }
                body.isKinematic=true;body.useGravity=false;body.interpolation=RigidbodyInterpolation.Interpolate;
                var shape=body.GetComponent<BoxCollider>();if(shape==null) shape=body.gameObject.AddComponent<BoxCollider>();
                Bounds bounds=default;bool first=true;
                foreach(var renderer in visual.GetComponentsInChildren<MeshRenderer>())
                {
                    var mesh=renderer.GetComponent<MeshFilter>()?.sharedMesh;if(mesh==null)continue;
                    Bounds local=mesh.bounds;
                    for(int corner=0;corner<8;corner++)
                    {
                        Vector3 point=local.center+Vector3.Scale(local.extents,new Vector3((corner&1)==0?-1:1,(corner&2)==0?-1:1,(corner&4)==0?-1:1));
                        point=body.transform.InverseTransformPoint(renderer.transform.TransformPoint(point));
                        if(first) {bounds=new Bounds(point,Vector3.zero);first=false;} else bounds.Encapsulate(point);
                    }
                }
                if(first) throw new InvalidOperationException(name+": no mesh bounds.");
                shape.center=bounds.center;shape.size=bounds.size;shape.isTrigger=false;
                // Road top is local Y=0. Raise the complete body, retaining the authored mesh scale.
                float bottom=body.transform.localPosition.y+bounds.min.y;
                body.transform.localPosition+=Vector3.up*(.02f-bottom);
                var encounter=root.GetComponent<DungeonTrapEncounter>();if(encounter==null) encounter=root.AddComponent<DungeonTrapEncounter>();
                encounter.Configure(jump?DungeonTrapEncounter.TrapKind.PillarJump:DungeonTrapEncounter.TrapKind.PillarCollapse,body,shape,body.transform.localPosition,body.transform.localPosition,null);
                Bounds final=new Bounds(body.transform.localPosition+bounds.center,bounds.size);
                encounter.ConfigureFall(new Vector3(side<0?final.min.x:final.max.x,Mathf.Max(0,final.min.y),final.center.z),-side*90);
                var dustTransform=root.transform.Find("PillarImpactDust");
                var dust=dustTransform!=null?dustTransform.GetComponent<ParticleSystem>():CreateDust(root.transform);
                ConfigureDust(dust,final);
                dust.Simulate(.1f,true,true,true);
                if(dust.particleCount==0) throw new InvalidOperationException(name+": impact dust burst emitted no particles.");
                Debug.Log($"Dust burst check: {name}, {dust.particleCount} live particles at 0.1 seconds; emitter outside front edge.");
                dust.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
                encounter.ConfigureImpact(dust);
                var saved=PrefabUtility.SaveAsPrefabAsset(root,path);
                var serialized=new SerializedObject(library);var traps=serialized.FindProperty("dungeonTraps");bool exists=false;
                var savedEncounter=saved.GetComponent<DungeonTrapEncounter>();
                for(int i=0;i<traps.arraySize;i++)if(traps.GetArrayElementAtIndex(i).objectReferenceValue==savedEncounter)exists=true;
                if(!exists) {int i=traps.arraySize;traps.arraySize++;traps.GetArrayElementAtIndex(i).objectReferenceValue=savedEncounter;}
                serialized.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(library);
                Debug.Log($"Pillar refit: {name}, size {shape.size}, centre {shape.center}; now waits upright and falls on approach. Authored mesh transforms preserved.");
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
        }
        // Navigation pillars now use the same planned motion/scoring ownership as jump pillars.
        var config=new SerializedObject(library);var families=config.FindProperty("fixedFamilies");
        for(int i=families.arraySize-1;i>=0;i--)if(families.GetArrayElementAtIndex(i).FindPropertyRelative("familyId").stringValue=="Dungeon.CollapsedPillar")families.DeleteArrayElementAtIndex(i);
        config.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(library);AssetDatabase.SaveAssets();
    }
    private static ParticleSystem CreateDust(Transform root)
    {
        var go=new GameObject("PillarImpactDust");go.transform.SetParent(root,false);
        var dust=go.AddComponent<ParticleSystem>();
        var main=dust.main;main.loop=false;main.playOnAwake=false;main.duration=.8f;main.startLifetime=new ParticleSystem.MinMaxCurve(.45f,.85f);
        main.startSpeed=new ParticleSystem.MinMaxCurve(.3f,.8f);main.startSize=new ParticleSystem.MinMaxCurve(.18f,.4f);
        main.startColor=new Color(.55f,.5f,.42f,.28f);main.maxParticles=100;
        var emission=dust.emission;emission.rateOverTime=0;emission.SetBursts(new[]{new ParticleSystem.Burst(0,45)});
        var shape=dust.shape;shape.shapeType=ParticleSystemShapeType.Box;shape.rotation=new Vector3(-90,0,0);
        var velocity=dust.velocityOverLifetime;velocity.enabled=true;velocity.space=ParticleSystemSimulationSpace.Local;velocity.y=new ParticleSystem.MinMaxCurve(.15f,.35f);
        var color=dust.colorOverLifetime;color.enabled=true;var gradient=new Gradient();
        gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(.8f,0),new GradientAlphaKey(0,1)});color.color=gradient;
        var size=dust.sizeOverLifetime;size.enabled=true;size.size=new ParticleSystem.MinMaxCurve(1,AnimationCurve.Linear(0,.5f,1,1.8f));
        dust.GetComponent<ParticleSystemRenderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/EndlessRollballAssets/Obstacles/Beach/BarriersV2/Smoke.mat");
        dust.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);return dust;
    }
    private static void ConfigureDust(ParticleSystem dust,Bounds final)
    {
        dust.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        // Emit on the player-facing edge, outside the opaque pillar rather than beneath it.
        dust.transform.localPosition=new Vector3(final.center.x,.14f,final.min.z-.18f);
        dust.transform.localRotation=Quaternion.identity;
        var main=dust.main;main.loop=false;main.playOnAwake=false;main.duration=1.5f;
        main.simulationSpace=ParticleSystemSimulationSpace.World;
        main.startLifetime=new ParticleSystem.MinMaxCurve(1.2f,1.8f);
        main.startSpeed=0;main.startSize=new ParticleSystem.MinMaxCurve(.5f,.9f);
        main.startColor=new Color(.78f,.73f,.65f,.65f);main.maxParticles=140;
        var emission=dust.emission;emission.rateOverTime=0;emission.SetBursts(new[]{new ParticleSystem.Burst(0,90)});
        var shape=dust.shape;shape.shapeType=ParticleSystemShapeType.Box;shape.rotation=Vector3.zero;shape.scale=new Vector3(final.size.x+.5f,.04f,.16f);
        var velocity=dust.velocityOverLifetime;velocity.enabled=true;velocity.space=ParticleSystemSimulationSpace.World;
        velocity.x=new ParticleSystem.MinMaxCurve(-.35f,.35f);velocity.y=new ParticleSystem.MinMaxCurve(.35f,.65f);velocity.z=new ParticleSystem.MinMaxCurve(-.45f,-.15f);
        var color=dust.colorOverLifetime;color.enabled=true;var gradient=new Gradient();
        gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(.9f,0),new GradientAlphaKey(.65f,.35f),new GradientAlphaKey(0,1)});color.color=gradient;
        var renderer=dust.GetComponent<ParticleSystemRenderer>();renderer.enabled=true;renderer.maxParticleSize=.25f;renderer.sortingFudge=1;
    }
}
