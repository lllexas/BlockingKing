Shader "BlockingKing/GridOverlay/Ring"
{
    Properties
    {
        _Color ("Tint", Color) = (1.0, 0.9, 0.25, 0.72)
        _RingWidth ("Ring Width", Range(0.01, 0.45)) = 0.12
        _CornerSoftness ("Corner Softness", Range(0.001, 0.3)) = 0.08
        _PulseStrength ("Pulse Strength", Range(0, 1)) = 0.22
        _PulseSpeed ("Pulse Speed", Range(0, 8)) = 2.4
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

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

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

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _RingWidth;
                float _CornerSoftness;
                float _PulseStrength;
                float _PulseSpeed;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 centered = abs(input.uv - 0.5) * 2.0;
                float squareDistance = max(centered.x, centered.y);
                float outer = 1.0 - smoothstep(1.0 - _CornerSoftness, 1.0, squareDistance);
                float inner = smoothstep(1.0 - _RingWidth - _CornerSoftness, 1.0 - _RingWidth, squareDistance);
                float ring = outer * inner;
                float pulse = 1.0 + sin(_Time.y * _PulseSpeed) * _PulseStrength;
                float alpha = _Color.a * saturate(ring * pulse);
                return half4(_Color.rgb * (1.0 + ring * 0.4), alpha);
            }
            ENDHLSL
        }
    }
}
