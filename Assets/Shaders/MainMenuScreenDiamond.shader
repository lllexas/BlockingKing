Shader "BlockingKing/UI/MainMenuScreenDiamond"
{
    Properties
    {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _BaseColor ("Base Color", Color) = (0.006, 0.012, 0.035, 1)
        _LineColor ("Line Color", Color) = (0.12, 0.62, 0.85, 1)
        _SubColor ("Sub Line Color", Color) = (0.04, 0.18, 0.30, 1)
        _DiamondSize ("Diamond Size", Range(0.04, 0.60)) = 0.18
        _SubDivisions ("Sub Divisions", Range(2, 12)) = 4
        _LineWidth ("Line Width", Range(0.001, 0.05)) = 0.008
        _SubLineWidth ("Sub Line Width", Range(0.001, 0.03)) = 0.004
        _LineGlow ("Line Glow", Range(0.0, 0.20)) = 0.035
        _Intensity ("Intensity", Range(0.0, 3.0)) = 1.0
        _Vignette ("Vignette", Range(0.0, 1.0)) = 0.45
        _Alpha ("Alpha", Range(0.0, 1.0)) = 1.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }
        LOD 100

        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
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
                float4 screenPos : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _BaseColor;
                float4 _LineColor;
                float4 _SubColor;
                float _DiamondSize;
                float _SubDivisions;
                float _LineWidth;
                float _SubLineWidth;
                float _LineGlow;
                float _Intensity;
                float _Vignette;
                float _Alpha;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.screenPos = ComputeScreenPos(output.positionCS);
                return output;
            }

            void DiamondDist(float2 p, float size, out float dist, out float glow)
            {
                float u = (p.x + p.y) / size;
                float v = (p.x - p.y) / size;
                float du = abs(frac(u + 0.5) - 0.5) * 2.0;
                float dv = abs(frac(v + 0.5) - 0.5) * 2.0;
                dist = min(du, dv);
                glow = 1.0 - smoothstep(0.0, max(0.0001, _LineGlow), dist);
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 screenUV = input.screenPos.xy / input.screenPos.w;
                float aspect = max(0.0001, _ScreenParams.x / _ScreenParams.y);
                float2 p = screenUV - 0.5;
                p.x *= aspect;

                float majorDist;
                float majorGlow;
                DiamondDist(p, _DiamondSize, majorDist, majorGlow);
                float majorLine = 1.0 - smoothstep(0.0, _LineWidth, majorDist);

                float subSize = _DiamondSize / max(2.0, _SubDivisions);
                float subDist;
                float subGlow;
                DiamondDist(p, subSize, subDist, subGlow);
                float subLine = 1.0 - smoothstep(0.0, _SubLineWidth, subDist);
                subLine *= 1.0 - majorLine;
                subGlow *= 1.0 - majorGlow * 0.65;

                float2 centered = screenUV - 0.5;
                float vignette = smoothstep(0.85, 0.18, length(centered * float2(aspect, 1.0)));
                vignette = lerp(1.0, vignette, _Vignette);

                float3 col = _BaseColor.rgb;
                col = lerp(col, _SubColor.rgb, subLine * 0.45);
                col = lerp(col, _LineColor.rgb, majorLine);
                col += _SubColor.rgb * subGlow * 0.10;
                col += _LineColor.rgb * majorGlow * 0.22;
                col *= _Intensity * vignette * _Color.rgb;

                return half4(col, _Alpha * _Color.a);
            }
            ENDHLSL
        }
    }
}
