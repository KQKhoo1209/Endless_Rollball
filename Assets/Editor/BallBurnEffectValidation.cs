using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using EndlessRollball.Track;

public static class BallBurnEffectValidation
{
    [MenuItem("Tools/Endless Rollball/Check Ball Burn Presentation")]
    public static void Check()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)return;
        // Remove only fixtures owned by an interrupted earlier run of this check.
        foreach(var old in Resources.FindObjectsOfTypeAll<GameObject>())
            if(old!=null && old.scene.IsValid() && (old.hideFlags&HideFlags.DontSave)!=0 &&
                (old.name=="BurnValidation" || old.name=="BurnContactValidation"))
            {
                foreach(var effectRoot in old.scene.GetRootGameObjects())
                    if(effectRoot.name=="BallBurnPresentation" && effectRoot.transform.childCount==0)UnityEngine.Object.DestroyImmediate(effectRoot);
                UnityEngine.Object.DestroyImmediate(old);
            }
        var preview=EditorSceneManager.NewPreviewScene();
        var fixture=GameObject.CreatePrimitive(PrimitiveType.Sphere);fixture.name="BurnValidation";fixture.hideFlags=HideFlags.HideAndDontSave;
        var trapRoot=new GameObject("BurnContactValidation") {hideFlags=HideFlags.HideAndDontSave};
        SceneManager.MoveGameObjectToScene(fixture,preview);SceneManager.MoveGameObjectToScene(trapRoot,preview);
        var material=new Material(Shader.Find("Universal Render Pipeline/Lit"));
        Type type=typeof(BallMovement).Assembly.GetType("EndlessRollball.Track.BallBurnEffect");
        object effect=Activator.CreateInstance(type,true);
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        try
        {
            var ball=fixture.AddComponent<BallMovement>();ball.enabled=false;
            var renderer=fixture.GetComponent<Renderer>();renderer.sharedMaterial=material;
            Color originalColour=new Color(.25f,.55f,.8f),originalEmission=new Color(.1f,.2f,.3f);
            var original=new MaterialPropertyBlock();original.SetColor("_BaseColor",originalColour);original.SetColor("_EmissionColor",originalEmission);renderer.SetPropertyBlock(original);
            for(int reuse=0;reuse<2;reuse++)
            {
                type.GetMethod("Play",flags).Invoke(effect,new object[]{ball});
                type.GetMethod("Sample",flags).Invoke(effect,new object[]{.8f,1f});
                var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block);
                if(block.GetColor("_BaseColor").maxColorComponent>.1f)throw new Exception("Burn did not darken the ball.");
                if(renderer.sharedMaterial==material || !renderer.enabled)throw new Exception("Burn did not use its temporary presentation material or hid the ball too early.");
                if(renderer.sharedMaterial.shader.name!="EndlessRollball/BallBurnDissolve" || UnityEditor.ShaderUtil.ShaderHasError(renderer.sharedMaterial.shader))throw new Exception("Burn dissolve shader is missing or failed compilation.");
                if(material.GetColor("_BaseColor")==block.GetColor("_BaseColor"))throw new Exception("Burn modified the original material asset.");
                fixture.transform.position+=Vector3.forward*2;
                type.GetMethod("Sample",flags).Invoke(effect,new object[]{.85f,1f});
                var followingRoot=(GameObject)type.GetField("root",flags).GetValue(effect);
                if(Vector3.Distance(followingRoot.transform.position,fixture.transform.position)>.01f)throw new Exception("Burn particles did not follow the rolling ball.");
                type.GetMethod("Sample",flags).Invoke(effect,new object[]{1f,1f});
                if(renderer.enabled)throw new Exception("Burned ball did not disappear before recovery.");
                var root=(GameObject)type.GetField("root",flags).GetValue(effect);
                if(root.GetComponentsInChildren<Collider>().Length!=0)throw new Exception("Burn VFX added a collider.");
                foreach(var particle in root.GetComponentsInChildren<ParticleSystem>())
                {
                    if(!particle.main.useUnscaledTime)throw new Exception("Burn freezes during clock-held recovery.");
                    particle.Simulate(.15f,true,true,true);
                    if(particle.particleCount==0)throw new Exception("Burn particle system emitted nothing.");
                }
                type.GetMethod("Stop",flags).Invoke(effect,null);renderer.GetPropertyBlock(block);
                if(renderer.sharedMaterial!=material || !renderer.enabled)throw new Exception("Burn did not restore original materials and visibility.");
                if(block.GetColor("_BaseColor")!=originalColour || block.GetColor("_EmissionColor")!=originalEmission)throw new Exception("Burn did not restore the original skin property block.");
                if(root.activeSelf)throw new Exception("Burn remains active after recovery.");
            }
            var rigidbody=fixture.GetComponent<Rigidbody>();
            rigidbody.linearVelocity=new Vector3(0,0,8);
            typeof(BallMovement).GetMethod("BeginBurnRoll",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(ball,new object[]{2.1f});
            if(rigidbody.linearVelocity.z<7.9f || rigidbody.constraints==RigidbodyConstraints.FreezeAll)throw new Exception("Burn stopped the ball immediately.");
            var rollTick=typeof(BallMovement).GetMethod("UpdateFinishRoll",BindingFlags.Instance|BindingFlags.NonPublic);
            for(int frame=0;frame<110;frame++)rollTick.Invoke(ball,null);
            if(rigidbody.linearVelocity.sqrMagnitude>.001f || rigidbody.constraints!=RigidbodyConstraints.FreezeAll)throw new Exception("Burn rollout did not eventually stop.");
            ball.ResetMotion(fixture.transform.position,Quaternion.identity);
            if(rigidbody.constraints==RigidbodyConstraints.FreezeAll)throw new Exception("Burn rollout did not restore movement on recovery.");
            var trap=trapRoot.AddComponent<DungeonTrapEncounter>();var body=trapRoot.AddComponent<Rigidbody>();body.isKinematic=true;
            var shape=trapRoot.AddComponent<BoxCollider>();shape.isTrigger=true;
            var contact=type.GetMethod("IsFireContact",BindingFlags.Static|BindingFlags.NonPublic);
            trap.Configure(DungeonTrapEncounter.TrapKind.Fire,body,shape,Vector3.zero,Vector3.zero,null);
            if(!(bool)contact.Invoke(null,new object[]{shape}))throw new Exception("Fire contact was not recognized.");
            var manager=fixture.AddComponent<ObstacleManager>();
            var segment=fixture.AddComponent<TrackSegment>();
            var active=(System.Collections.IDictionary)typeof(ObstacleManager).GetField("active",flags).GetValue(manager);
            var pairType=typeof(ObstacleManager).GetNestedType("ObstaclePair",BindingFlags.NonPublic);
            var pairs=(System.Collections.IList)Activator.CreateInstance(typeof(System.Collections.Generic.List<>).MakeGenericType(pairType));
            var pair=Activator.CreateInstance(pairType,true);
            pairType.GetField("Instance").SetValue(pair,trapRoot);
            pairs.Add(pair);active.Add(segment,pairs);
            try
            {
                if(!manager.OwnsActiveCollider(shape))throw new Exception("Active fire trigger was rejected before the burn event reached the controller.");
                shape.enabled=false;
                if(manager.OwnsActiveCollider(shape))throw new Exception("Disabled fire trigger was accepted.");
                shape.enabled=true;active.Clear();
                if(manager.OwnsActiveCollider(shape))throw new Exception("Unowned fire trigger was accepted.");
            }
            finally {active.Clear();}
            trap.Configure(DungeonTrapEncounter.TrapKind.Rubble,body,shape,Vector3.zero,Vector3.zero,null);
            if((bool)contact.Invoke(null,new object[]{shape}) || (bool)contact.Invoke(null,new object[]{null}))throw new Exception("Non-fire contact incorrectly selected burn.");
            Debug.Log("Ball burn presentation PASS: continued initial velocity, gradual stop/reset, char/dissolve shader, following particles, disappearance, material/visibility restoration, two reuses and fire trigger ownership. Full three-second gameplay sequence still needs Play Mode visual acceptance.");
        }
        finally
        {
            type.GetMethod("Dispose",flags).Invoke(effect,null);
            UnityEngine.Object.DestroyImmediate(fixture);UnityEngine.Object.DestroyImmediate(trapRoot);UnityEngine.Object.DestroyImmediate(material);
            EditorSceneManager.ClosePreviewScene(preview);
        }
    }
}
