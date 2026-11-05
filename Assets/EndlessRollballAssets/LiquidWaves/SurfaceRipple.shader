Shader "EndlessRollball/Surface Ripple" {
 Properties {
 _Color("Ripple Color",Color)=(0.65,0.95,1,0.45)
 _WaveHeight("Wave Height",Float)=0.15
 _WaveFrequency("Wave Frequency",Float)=0.5
 _WaveSpeed("Wave Speed",Float)=1.2
 }
 SubShader {
 Tags {"RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent"}
 Blend SrcAlpha OneMinusSrcAlpha
 ZWrite Off Cull Off
 Pass {
 HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 struct Attributes {float4 positionOS:POSITION; float2 uv:TEXCOORD0; half4 color:COLOR;};
 struct Varyings {float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0;half4 color:COLOR;};
 CBUFFER_START(UnityPerMaterial)
 half4 _Color;float _WaveHeight,_WaveFrequency,_WaveSpeed;
 CBUFFER_END
 Varyings vert(Attributes v) {
 Varyings o;float3 p=TransformObjectToWorld(v.positionOS.xyz);
 float t=_Time.y*_WaveSpeed;
 p.y+=(sin(p.x*_WaveFrequency+t)+sin(p.z*_WaveFrequency*0.7+t*0.8))*0.5*_WaveHeight;
 o.positionCS=TransformWorldToHClip(p);o.uv=v.uv;o.color=v.color*_Color;return o;
 }
 half4 frag(Varyings i):SV_Target {
 float r=length(i.uv*2-1);
 float ring=smoothstep(0.66,0.78,r)*(1-smoothstep(0.78,0.94,r));
 return half4(i.color.rgb,i.color.a*ring);
 }
 ENDHLSL
 }
 }
}
