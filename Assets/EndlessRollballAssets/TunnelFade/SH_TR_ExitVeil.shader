Shader "Custom/SH_TR_ExitVeil"
{
    Properties
    {
        _BaseColor("Veil Colour", Color) = (0.45, 0.44, 0.42, 1)
        _Feather("Edge Softness", Range(0.01, 0.3)) = 0.1
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
        }

        Pass
        {
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _Feather;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 edgeDistance = min(input.uv, 1.0 - input.uv);
                float edge = min(edgeDistance.x, edgeDistance.y);
                float opacity = smoothstep(
                    0.0, max(_Feather, 0.001), edge);
                return half4(
                    _BaseColor.rgb,
                    _BaseColor.a * opacity);
            }
            ENDHLSL
        }
    }
}
