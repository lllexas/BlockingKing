Shader "BlockingKing/GridOverlay/Flow"
{
    Properties
    {
        _Color ("Tint", Color) = (0.2, 0.75, 1.0, 0.48)
        _BaseAlpha ("Base Alpha", Range(0, 1)) = 0.16
        _BandAlpha ("Band Alpha", Range(0, 1)) = 0.85
        _BandScale ("Band Scale", Range(1, 30)) = 7
        _BandWidth ("Band Width", Range(0.02, 0.9)) = 0.18
        _FlowSpeed ("Flow Speed", Range(-8, 8)) = 1.8
        _Direction ("Direction XY", Vector) = (1, 0, 0, 0)
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
                float _BandAlpha;
                float _BandScale;
                float _BandWidth;
                float _FlowSpeed;
                float4 _Direction;
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
                float2 direction = normalize(_Direction.xy + float2(0.0001, 0.0));
                float projection = dot(input.uv - 0.5, direction);
                float bandCoord = projection * _BandScale - _Time.y * _FlowSpeed;
                float band = 1.0 - smoothstep(_BandWidth, _BandWidth + 0.08, abs(frac(bandCoord) - 0.5));
                float lane = 1.0 - smoothstep(0.35, 0.5, abs(dot(input.uv - 0.5, float2(-direction.y, direction.x))));
                float alpha = _Color.a * saturate(_BaseAlpha + band * lane * _BandAlpha);
                return half4(_Color.rgb * (1.0 + band * 0.5), alpha);
            }
            ENDHLSL
        }
    }
}
