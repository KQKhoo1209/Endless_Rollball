Shader "EndlessRollball/Moving Water Foam"
{
    Properties
    {
        _Color ("Foam Tint", Color) = (.78,.96,1,.65)
        _FollowWaves ("Follow Water Waves", Float) = 1
        _WaveHeight ("Wave Height", Float) = .15
        _WaveFrequency ("Wave Frequency", Float) = .5
        _WaveSpeed ("Wave Speed", Float) = 1.2
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; half fog:TEXCOORD1; };
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _FollowWaves, _WaveHeight, _WaveFrequency, _WaveSpeed;
            CBUFFER_END
            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 world = TransformObjectToWorld(input.positionOS.xyz);
                float t = _Time.y * _WaveSpeed;
                world.y += (sin(world.x * _WaveFrequency + t) + sin(world.z * _WaveFrequency * .7 + t * .8)) * .5 * _WaveHeight * _FollowWaves;
                output.positionCS = TransformWorldToHClip(world);
                output.uv = input.uv;
                output.color = input.color * _Color;
                output.fog = ComputeFogFactor(output.positionCS.z);
                return output;
            }
            half4 Frag(Varyings input):SV_Target
            {
                float2 p = input.uv * 2 - 1;
                float edge = 1 - smoothstep(.35,1,length(p));
                float breakup = .6 + .4 * sin(p.x * 13 + sin(p.y * 11) * 2) * sin(p.y * 15);
                half alpha = edge * breakup * input.color.a;
                return half4(MixFog(input.color.rgb,input.fog), alpha);
            }
            ENDHLSL
        }
    }
}
