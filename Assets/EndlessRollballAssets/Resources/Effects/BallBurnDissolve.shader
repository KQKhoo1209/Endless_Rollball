Shader "EndlessRollball/BallBurnDissolve"
{
    Properties
    {
        _BaseMap ("Skin", 2D) = "white" {}
        _BaseColor ("Colour", Color) = (1,1,1,1)
        _BurnAmount ("Char", Range(0,1)) = 0
        _Dissolve ("Ash", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST, _BaseColor;
            float _BurnAmount, _Dissolve;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; };
            struct Varyings { float4 positionCS:SV_POSITION; float3 local:TEXCOORD0; float3 normal:TEXCOORD1; float2 uv:TEXCOORD2; };
            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS=TransformObjectToHClip(v.positionOS.xyz);
                o.local=v.positionOS.xyz;
                o.normal=TransformObjectToWorldNormal(v.normalOS);
                o.uv=TRANSFORM_TEX(v.uv,_BaseMap);
                return o;
            }
            float Hash(float3 p) { return frac(sin(dot(p,float3(12.9898,78.233,37.719)))*43758.5453); }
            float Noise(float3 p)
            {
                float3 a=floor(p), f=frac(p);f=f*f*(3-2*f);
                return lerp(lerp(lerp(Hash(a),Hash(a+float3(1,0,0)),f.x),lerp(Hash(a+float3(0,1,0)),Hash(a+float3(1,1,0)),f.x),f.y),
                    lerp(lerp(Hash(a+float3(0,0,1)),Hash(a+float3(1,0,1)),f.x),lerp(Hash(a+float3(0,1,1)),Hash(a+1),f.x),f.y),f.z);
            }
            half4 Frag(Varyings i):SV_Target
            {
                float noise=Noise(i.local*18);
                clip(noise-_Dissolve*1.1+.001);
                float charred=smoothstep(noise-.08,noise+.08,_BurnAmount*1.2);
                float3 skin=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv).rgb*_BaseColor.rgb;
                float3 colour=lerp(skin,float3(.018,.012,.009),charred);
                float edge=(1-smoothstep(.025,.09,abs(noise-_BurnAmount*1.2)))*step(.01,_BurnAmount);
                Light light=GetMainLight();
                colour*=.35+.65*saturate(dot(normalize(i.normal),light.direction))*light.color;
                colour+=float3(1.6,.28,.015)*edge;
                return half4(colour,1);
            }
            ENDHLSL
        }
    }
}
