Shader "BlockingKing/GridOverlay/PathFlow"
{
    Properties
    {
        _Color ("Tint", Color) = (0.2, 0.85, 1.0, 0.35)
        _LaneWidth ("Lane Width", Range(0.04, 0.42)) = 0.18
        _EdgeSoftness ("Edge Softness", Range(0.005, 0.16)) = 0.05
        _BaseAlpha ("Base Alpha", Range(0, 1)) = 0.45
        _PulseAlpha ("Pulse Alpha", Range(0, 1)) = 0.55
        _PulseScale ("Pulse Scale", Range(0.25, 8)) = 1.25
        _PulseWidth ("Pulse Width", Range(0.02, 0.8)) = 0.28
        _FlowSpeed ("Flow Speed", Range(-8, 8)) = 1.8
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        LOD 100

        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode"="UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _LaneWidth;
                float _EdgeSoftness;
                float _BaseAlpha;
                float _PulseAlpha;
                float _PulseScale;
                float _PulseWidth;
                float _FlowSpeed;
            CBUFFER_END

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _PathColor)
                UNITY_DEFINE_INSTANCED_PROP(float4, _PathIn)
                UNITY_DEFINE_INSTANCED_PROP(float4, _PathOut)
                UNITY_DEFINE_INSTANCED_PROP(float4, _PathMeta)
            UNITY_INSTANCING_BUFFER_END(Props)

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            float SegmentDistance(float2 p, float2 a, float2 b, out float t)
            {
                float2 ab = b - a;
                float denom = max(dot(ab, ab), 0.0001);
                t = saturate(dot(p - a, ab) / denom);
                return length(p - (a + ab * t));
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                float4 instanceColor = UNITY_ACCESS_INSTANCED_PROP(Props, _PathColor);
                float2 incoming = UNITY_ACCESS_INSTANCED_PROP(Props, _PathIn).xy;
                float2 outgoing = UNITY_ACCESS_INSTANCED_PROP(Props, _PathOut).xy;
                float4 meta = UNITY_ACCESS_INSTANCED_PROP(Props, _PathMeta);

                float2 p = input.uv - 0.5;
                float2 center = 0.0;
                float2 startPoint = dot(incoming, incoming) > 0.01 ? -incoming * 0.5 : center;
                float2 endPoint = dot(outgoing, outgoing) > 0.01 ? outgoing * 0.5 : center;

                float tA;
                float tB;
                float distA = SegmentDistance(p, startPoint, center, tA);
                float distB = SegmentDistance(p, center, endPoint, tB);
                float useA = distA <= distB ? 1.0 : 0.0;
                float dist = min(distA, distB);

                float localT = lerp(0.5 + tB * 0.5, tA * 0.5, useA);
                float cap = smoothstep(0.52, 0.42, length(p - center));
                float lane = 1.0 - smoothstep(_LaneWidth, _LaneWidth + _EdgeSoftness, dist);
                lane = max(lane, cap * lane);

                float pathCoord = meta.x + localT;
                float pulseCoord = frac(pathCoord * _PulseScale - _Time.y * _FlowSpeed);
                float pulse = 1.0 - smoothstep(_PulseWidth, _PulseWidth + 0.12, abs(pulseCoord - 0.5));
                float alpha = instanceColor.a * lane * saturate(_BaseAlpha + pulse * _PulseAlpha);
                float3 rgb = instanceColor.rgb * (1.0 + pulse * 0.35);

                return half4(rgb, alpha);
            }
            ENDHLSL
        }
    }
}
