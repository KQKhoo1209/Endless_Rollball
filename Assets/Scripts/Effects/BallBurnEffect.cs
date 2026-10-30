using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace EndlessRollball.Track
{
    // Presentation only. Preserve skin materials and property blocks for recovery/reuse.
    internal sealed class BallBurnEffect
    {
        private sealed class Appearance
        {
            internal Renderer Renderer;
            internal MaterialPropertyBlock Original;
            internal Color Colour;
            internal Material[] Materials;
            internal Material[] BurnMaterials;
        }
        private readonly List<Appearance> originals=new();
        private readonly MaterialPropertyBlock working=new();
        private GameObject root;
        private ParticleSystem smoke,embers;
        private Material smokeMaterial,emberMaterial;
        private Texture2D texture;
        private bool emissionStopped;
        private Transform target;
        private Vector3 targetCentre;
        internal static bool IsFireContact(Collider shape)
        {
            var encounter=shape!=null?shape.GetComponentInParent<DungeonTrapEncounter>():null;
            return encounter!=null && encounter.Kind==DungeonTrapEncounter.TrapKind.Fire && encounter.Hazard==shape;
        }
        internal void Play(BallMovement ball)
        {
            Stop();
            if(root==null)Build(ball.gameObject.scene);
            var sphere=ball.GetComponent<SphereCollider>();
            target=ball.transform;targetCentre=sphere!=null?sphere.center:Vector3.zero;
            root.transform.position=sphere!=null?sphere.transform.TransformPoint(sphere.center):ball.transform.position;
            root.transform.localScale=Vector3.one*(ball.SphereRadius/.5f);
            var shader=Resources.Load<Shader>("Effects/BallBurnDissolve");
            foreach(var renderer in ball.GetComponentsInChildren<Renderer>(true))
            {
                if(!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer))continue;
                if(!renderer.enabled || !renderer.gameObject.activeInHierarchy || renderer.sharedMaterial==null)continue;
                var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block);
                var material=renderer.sharedMaterial;
                Color colour=material.HasProperty("_BaseColor")?material.GetColor("_BaseColor"):material.HasProperty("_Color")?material.color:Color.white;
                if(block.HasColor("_BaseColor")) colour=block.GetColor("_BaseColor");
                else if(block.HasColor("_Color")) colour=block.GetColor("_Color");
                var materials=renderer.sharedMaterials;
                var burnMaterials=new Material[materials.Length];
                for(int i=0;i<materials.Length;i++)
                {
                    var source=materials[i];
                    if(source==null) continue;
                    var burn=new Material(shader!=null?shader:source.shader) {name="BallBurnRuntime"};
                    if(source.HasProperty("_BaseMap"))
                    {
                        burn.SetTexture("_BaseMap",source.GetTexture("_BaseMap"));
                        burn.SetTextureScale("_BaseMap",source.GetTextureScale("_BaseMap"));
                        burn.SetTextureOffset("_BaseMap",source.GetTextureOffset("_BaseMap"));
                    }
                    else if(source.HasProperty("_MainTex")) burn.SetTexture("_BaseMap",source.mainTexture);
                    burn.SetColor("_BaseColor",source.HasProperty("_BaseColor")?source.GetColor("_BaseColor"):colour);
                    burnMaterials[i]=burn;
                }
                originals.Add(new Appearance {Renderer=renderer,Original=block,Colour=colour,Materials=materials,BurnMaterials=burnMaterials});
                renderer.sharedMaterials=burnMaterials;
            }
            emissionStopped=false;root.SetActive(true);smoke.Play();embers.Play();
        }
        internal void Sample(float elapsed,float duration)
        {
            if(root==null || !root.activeSelf)return;
            float progress=duration>0?Mathf.Clamp01(elapsed/duration):1;
            if(target!=null) root.transform.position=target.TransformPoint(targetCentre);
            foreach(var item in originals)
            {
                if(item.Renderer==null)continue;
                item.Renderer.GetPropertyBlock(working);
                Color hot=Color.Lerp(item.Colour,new Color(1,.23f,.025f),Mathf.Clamp01(progress/.15f));
                Color colour=Color.Lerp(hot,new Color(.035f,.025f,.02f),Mathf.SmoothStep(0,1,Mathf.InverseLerp(.12f,.7f,progress)));
                colour.a=item.Colour.a;
                working.SetColor("_BaseColor",colour);working.SetColor("_Color",colour);working.SetColor("_EmissionColor",Color.black);
                working.SetFloat("_BurnAmount",Mathf.Clamp01(progress/.65f));
                working.SetFloat("_Dissolve",Mathf.InverseLerp(.7f,.92f,progress));
                item.Renderer.SetPropertyBlock(working);working.Clear();
                if(progress>=.94f) item.Renderer.enabled=false;
            }
            if(progress>=1 && !emissionStopped)
            {emissionStopped=true;smoke.Stop(true,ParticleSystemStopBehavior.StopEmitting);embers.Stop(true,ParticleSystemStopBehavior.StopEmitting);}
        }
        internal void Stop()
        {
            foreach(var item in originals)
            {
                if(item.Renderer!=null)
                {
                    item.Renderer.sharedMaterials=item.Materials;
                    item.Renderer.SetPropertyBlock(item.Original);
                    item.Renderer.enabled=true;
                }
                foreach(var material in item.BurnMaterials) Destroy(material);
            }
            originals.Clear();
            target=null;
            if(root!=null) {if(smoke!=null)smoke.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);if(embers!=null)embers.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);root.SetActive(false);}
        }
        private void Build(Scene scene)
        {
            root=new GameObject("BallBurnPresentation");SceneManager.MoveGameObjectToScene(root,scene);
            texture=new Texture2D(32,32,TextureFormat.RGBA32,false) {name="BurnSoftParticle"};
            var pixels=new Color[1024];for(int y=0;y<32;y++)for(int x=0;x<32;x++)
            {float radius=new Vector2((x-15.5f)/15.5f,(y-15.5f)/15.5f).magnitude;pixels[y*32+x]=new Color(1,1,1,Mathf.Pow(Mathf.Clamp01(1-radius),1.4f));}
            texture.SetPixels(pixels);texture.Apply();
            smokeMaterial=MakeMaterial(false);emberMaterial=MakeMaterial(true);
            smoke=MakeParticles("BurnSmoke",false,smokeMaterial);embers=MakeParticles("BurnEmbers",true,emberMaterial);root.SetActive(false);
        }
        private Material MakeMaterial(bool additive)
        {
            var material=new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")) {name=additive?"BurnEmbersRuntime":"BurnSmokeRuntime",renderQueue=3000};
            material.SetFloat("_Surface",1);material.SetFloat("_Blend",additive?2:0);
            material.SetFloat("_SrcBlend",(float)BlendMode.SrcAlpha);material.SetFloat("_DstBlend",(float)(additive?BlendMode.One:BlendMode.OneMinusSrcAlpha));material.SetFloat("_ZWrite",0);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");material.SetTexture("_BaseMap",texture);material.SetColor("_BaseColor",Color.white);return material;
        }
        private ParticleSystem MakeParticles(string name,bool sparks,Material material)
        {
            var go=new GameObject(name);go.transform.SetParent(root.transform,false);go.transform.localPosition=sparks?Vector3.zero:new Vector3(0,.3f,0);
            var particles=go.AddComponent<ParticleSystem>();particles.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=particles.main;main.playOnAwake=false;main.loop=false;main.duration=3;main.useUnscaledTime=true;main.simulationSpace=ParticleSystemSimulationSpace.World;
            main.startLifetime=new ParticleSystem.MinMaxCurve(sparks?.3f:.6f,sparks?.65f:1f);main.startSpeed=new ParticleSystem.MinMaxCurve(sparks?.6f:.25f,sparks?1.3f:.6f);
            main.startSize=new ParticleSystem.MinMaxCurve(sparks?.035f:.2f,sparks?.07f:.4f);main.startColor=sparks?new Color(1,.32f,.025f):new Color(.35f,.32f,.29f,.6f);main.maxParticles=70;
            var emission=particles.emission;emission.rateOverTime=sparks?35:25;if(sparks)emission.SetBursts(new[]{new ParticleSystem.Burst(0,16)});
            var shape=particles.shape;shape.shapeType=ParticleSystemShapeType.Sphere;shape.radius=.5f;
            var velocity=particles.velocityOverLifetime;velocity.enabled=true;velocity.y=.65f;
            var colour=particles.colorOverLifetime;colour.enabled=true;var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(1,0),new GradientAlphaKey(0,1)});colour.color=gradient;
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial=material;return particles;
        }
        internal void Dispose()
        {Stop();Destroy(root);Destroy(smokeMaterial);Destroy(emberMaterial);Destroy(texture);root=null;}
        private static void Destroy(UnityEngine.Object value)
        {if(value==null)return;if(Application.isPlaying)UnityEngine.Object.Destroy(value);else UnityEngine.Object.DestroyImmediate(value);}
    }
}
