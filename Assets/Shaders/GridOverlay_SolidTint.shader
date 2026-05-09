Shader "BlockingKing/GridOverlay/SolidTint"
{
    Properties
    {
        _Color ("Tint", Color) = (0.2, 0.9, 1.0, 0.28)
        _FillAlpha ("Fill Alpha", Range(0, 1)) = 0.65
        _BorderAlpha ("Border Alpha", Range(0, 1)) = 1.0
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
                float _FillAlpha;
                float _BorderAlpha;
                float _BorderWidth;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 edgeDistance = min(input.uv, 1.0 - input.uv);
                float edge = 1.0 - smoothstep(0.0, _BorderWidth, min(edgeDistance.x, edgeDistance.y));
                float alpha = _Color.a * saturate(_FillAlpha + edge * _BorderAlpha);
                return half4(_Color.rgb, alpha);
            }
            ENDHLSL
        }
    }
}
