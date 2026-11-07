Shader "EndlessRollball/Volcanic Rock"
{
 Properties {
 _RockColor("Rock",Color)=(.045,.035,.035,1)
 _LavaColor("Molten Cracks",Color)=(2.2,.45,.025,1)
 _CrackScale("Crack Scale",Float)=2.5
 _MoltenFill("Molten Fill",Range(0,1))=0
 _Flow("Flow",Float)=0
 }
 SubShader {
 Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque"}
 Pass {
 Tags {"LightMode"="UniversalForward"}
 HLSLPROGRAM
 #pragma vertex Vert
 #pragma fragment Frag
 #pragma multi_compile_fog
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
 struct A {float4 positionOS:POSITION;float3 normalOS:NORMAL;};
 struct V {float4 positionCS:SV_POSITION;float3 world:TEXCOORD0;float3 normal:TEXCOORD1;half fog:TEXCOORD2;};
 CBUFFER_START(UnityPerMaterial)
 half4 _RockColor,_LavaColor;float _CrackScale,_MoltenFill,_Flow;
 CBUFFER_END
 V Vert(A i){V o;o.world=TransformObjectToWorld(i.positionOS.xyz);o.positionCS=TransformWorldToHClip(o.world);o.normal=TransformObjectToWorldNormal(i.normalOS);o.fog=ComputeFogFactor(o.positionCS.z);return o;}
 float2 Hash(float2 p){return frac(sin(float2(dot(p,float2(127.1,311.7)),dot(p,float2(269.5,183.3))))*43758.5453);}
 half4 Frag(V i):SV_Target {
 float2 p=float2(i.world.x+i.world.z*.61,i.world.y+i.world.z*.37-_Time.y*_Flow)*_CrackScale;
 float2 cell=floor(p),f=frac(p);float nearest=8,second=8;
 [unroll] for(int y=-1;y<=1;y++) [unroll] for(int x=-1;x<=1;x++){
 float2 g=float2(x,y);float d=length(g+Hash(cell+g)-f);
 if(d<nearest){second=nearest;nearest=d;}else second=min(second,d);
 }
 float crack=1-smoothstep(.025,.09,second-nearest);
 float molten=lerp(crack,1,_MoltenFill);
 Light light=GetMainLight();
 half shade=.35+.65*saturate(dot(normalize(i.normal),light.direction));
 half3 c=lerp(_RockColor.rgb*shade,_LavaColor.rgb,molten);
 return half4(MixFog(c,i.fog),1);
 }
 ENDHLSL
 }
 }
}
