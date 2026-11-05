Shader "EndlessRollball/Phoenix Lava Surface"
{
 Properties { _BaseMap("Texture",2D)="white"{} _BaseColor("Tint",Color)=(1,1,1,1) _Glow("Glow",Float)=1 _FlowSpeed("Flow",Float)=0 }
 SubShader
 {
  Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
  Pass
  {
   Tags { "LightMode"="UniversalForward" }
   HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment Frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   struct A { float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; };
   struct V { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; float3 normalWS:TEXCOORD1; };
   TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
   CBUFFER_START(UnityPerMaterial)
   float4 _BaseMap_ST; half4 _BaseColor; float _Glow; float _FlowSpeed;
   CBUFFER_END
   V Vert(A i) { V o; o.positionCS=TransformObjectToHClip(i.positionOS.xyz); o.normalWS=TransformObjectToWorldNormal(i.normalOS); o.uv=i.uv*_BaseMap_ST.xy+_BaseMap_ST.zw; return o; }
   half4 Frag(V i):SV_Target { half3 c=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv+float2(_Time.y*_FlowSpeed,0)).rgb*_BaseColor.rgb; half shade=.45+.55*saturate(dot(normalize(i.normalWS),normalize(float3(.4,1,-.3)))); return half4(c*shade*(1+_Glow),1); }
   ENDHLSL
  }
 }
}
