Shader "Hidden/EndlessRollball/PaintingStroke"
{
    Properties { _MainTex ("Canvas", 2D) = "white" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _Segment;
            float4 _StrokeColour;
            float4 _SurfaceSize;
            float _Radius;
            fixed4 frag(v2f_img input) : SV_Target
            {
                float2 p = input.uv * _SurfaceSize.xy;
                float2 a = _Segment.xy * _SurfaceSize.xy;
                float2 b = _Segment.zw * _SurfaceSize.xy;
                float2 ab = b - a;
                float t = saturate(dot(p - a, ab) / max(dot(ab, ab), .0001));
                float distanceToStroke = length(p - (a + t * ab));
                float coverage = 1 - smoothstep(_Radius - .75, _Radius + .75, distanceToStroke);
                fixed4 previous = tex2D(_MainTex, input.uv);
                return fixed4(lerp(previous.rgb, _StrokeColour.rgb, coverage * _StrokeColour.a), 1);
            }
            ENDCG
        }
    }
}
