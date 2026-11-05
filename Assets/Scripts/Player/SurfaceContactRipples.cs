using UnityEngine;
using UnityEngine.Rendering;
namespace EndlessRollball {
// Reusable water/lava contact rings. Created once and reset when pooled actors return.
[DisallowMultipleComponent]
public sealed class SurfaceContactRipples : MonoBehaviour {
 [SerializeField] private Material rippleMaterial;
 [SerializeField] private Material surfaceMaterial;
 [SerializeField] private Vector3 surfaceOffset=new Vector3(0,.08f,0);
 [SerializeField,Min(.1f)] private float lifetime=2f;
 [SerializeField,Min(.1f)] private float ringsPerSecond=1f;
 [SerializeField,Min(.1f)] private float initialDiameter=.8f;
 [SerializeField,Min(1f)] private float expansion=3f;
 private ParticleSystem particles;
 static readonly int Height=Shader.PropertyToID("_WaveHeight"), Frequency=Shader.PropertyToID("_WaveFrequency"), Speed=Shader.PropertyToID("_WaveSpeed");
 void OnEnable() {
 if(particles==null)Build();
 if(particles==null)return;
 var renderer=particles.GetComponent<ParticleSystemRenderer>();
 if(surfaceMaterial!=null) {
 var properties=new MaterialPropertyBlock();
 renderer.GetPropertyBlock(properties);
 if(surfaceMaterial.HasProperty(Height))properties.SetFloat(Height,surfaceMaterial.GetFloat(Height));
 if(surfaceMaterial.HasProperty(Frequency))properties.SetFloat(Frequency,surfaceMaterial.GetFloat(Frequency));
 if(surfaceMaterial.HasProperty(Speed))properties.SetFloat(Speed,surfaceMaterial.GetFloat(Speed));
 renderer.SetPropertyBlock(properties);
 }
 particles.transform.localPosition=surfaceOffset;
 particles.Clear(true);particles.Play(true);
 }
 void Build() {
 if(rippleMaterial==null){Debug.LogWarning(name+": assign a Surface Ripple material.",this);return;}
 var child=new GameObject("Surface Contact Ripples");child.transform.SetParent(transform,false);
 particles=child.AddComponent<ParticleSystem>();particles.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
 var main=particles.main;main.loop=true;main.playOnAwake=false;main.duration=lifetime;
 main.startLifetime=lifetime;main.startSpeed=0;main.startSize=initialDiameter;main.startColor=Color.white;
 main.simulationSpace=ParticleSystemSimulationSpace.World;main.useUnscaledTime=false;main.maxParticles=16;
 var emission=particles.emission;emission.rateOverTime=ringsPerSecond;
 var shape=particles.shape;shape.enabled=false;
 var size=particles.sizeOverLifetime;size.enabled=true;size.size=new ParticleSystem.MinMaxCurve(1,AnimationCurve.Linear(0,.3f,1,expansion));
 var color=particles.colorOverLifetime;color.enabled=true;
 var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(.8f,.15f),new GradientAlphaKey(0,1)});color.color=gradient;
 var renderer=particles.GetComponent<ParticleSystemRenderer>();renderer.renderMode=ParticleSystemRenderMode.HorizontalBillboard;
 renderer.sharedMaterial=rippleMaterial;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
 renderer.minParticleSize=0;renderer.maxParticleSize=1;
 }
 void OnDisable(){if(particles!=null)particles.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);}
}
}
