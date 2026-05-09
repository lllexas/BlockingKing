Shader "BlockingKing/GridOverlay/Stripes"
{
    Properties
    {
        _Color ("Tint", Color) = (1.0, 0.18, 0.08, 0.45)
        _BaseAlpha ("Base Alpha", Range(0, 1)) = 0.18
        _StripeAlpha ("Stripe Alpha", Range(0, 1)) = 0.7
        _StripeScale ("Stripe Scale", Range(2, 40)) = 12
        _StripeWidth ("Stripe Width", Range(0.02, 0.8)) = 0.28
        _ScrollSpeed ("Scroll Speed", Range(-8, 8)) = 1.2
        _BorderWidth ("Border Width", Range(0, 0.5)) = 0.08
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
                float _BaseAlpha;
                float _StripeAlpha;
                float _StripeScale;
                float _StripeWidth;
                float _ScrollSpeed;
                float _BorderWidth;
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
                float stripeCoord = (input.uv.x + input.uv.y) * _StripeScale + _Time.y * _ScrollSpeed;
                float stripe = 1.0 - smoothstep(_StripeWidth, _StripeWidth + 0.08, abs(frac(stripeCoord) - 0.5));
                float2 edgeDistance = min(input.uv, 1.0 - input.uv);
                float border = 1.0 - smoothstep(0.0, _BorderWidth, min(edgeDistance.x, edgeDistance.y));
                float alpha = _Color.a * saturate(_BaseAlpha + stripe * _StripeAlpha + border * 0.55);
                return half4(_Color.rgb, alpha);
            }
            ENDHLSL
        }
    }
}
