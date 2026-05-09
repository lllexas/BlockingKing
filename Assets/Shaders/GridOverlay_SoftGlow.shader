Shader "BlockingKing/GridOverlay/SoftGlow"
{
    Properties
    {
        _Color ("Tint", Color) = (0.3, 1.0, 0.8, 0.55)
        _CenterAlpha ("Center Alpha", Range(0, 1)) = 0.22
        _EdgeAlpha ("Edge Alpha", Range(0, 2)) = 1.0
        _GlowPower ("Glow Power", Range(0.2, 8)) = 2.6
        _PulseStrength ("Pulse Strength", Range(0, 1)) = 0.18
        _PulseSpeed ("Pulse Speed", Range(0, 8)) = 2.0
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
                float _CenterAlpha;
                float _EdgeAlpha;
                float _GlowPower;
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
                float edgeDistance = saturate(max(centered.x, centered.y));
                float edgeGlow = pow(edgeDistance, _GlowPower);
                float pulse = 1.0 + sin(_Time.y * _PulseSpeed) * _PulseStrength;
                float alpha = _Color.a * saturate((_CenterAlpha + edgeGlow * _EdgeAlpha) * pulse);
                float3 color = _Color.rgb * (1.0 + edgeGlow * 0.75);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
