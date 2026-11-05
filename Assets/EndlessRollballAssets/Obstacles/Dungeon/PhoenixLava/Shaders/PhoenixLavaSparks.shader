Shader "EndlessRollball/Phoenix Lava Sparks"
{
 SubShader { Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
 Pass { Blend SrcAlpha One ZWrite Off Cull Off
 HLSLPROGRAM
 #pragma vertex Vert
 #pragma fragment Frag
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 struct A { float4 positionOS:POSITION; half4 color:COLOR; float2 uv:TEXCOORD0; };
 struct V { float4 positionCS:SV_POSITION; half4 color:COLOR; float2 uv:TEXCOORD0; };
 V Vert(A i) { V o; o.positionCS=TransformObjectToHClip(i.positionOS.xyz); o.color=i.color; o.uv=i.uv; return o; }
 half4 Frag(V i):SV_Target { half a=1-smoothstep(.1,.5,length(i.uv-.5)); return half4(i.color.rgb*2,i.color.a*a); }
 ENDHLSL
 } }
}
