Shader "BlockingKing/OutsideDiamond"
{
    Properties
    {
        _BaseColor   ("Base Color",    Color) = (0.01, 0.02, 0.08, 1)
        _LineColor   ("Line Color",    Color) = (0.20, 0.22, 0.28, 1)
        _SubColor    ("Sub Line Color", Color) = (0.08, 0.09, 0.14, 1)
        _DiamondSize ("Diamond Size",  Float) = 1.0
        _SubDivisions("Sub Divisions", Range(2, 12)) = 5
        _LineWidth   ("Line Width",    Range(0.005, 0.10)) = 0.025
        _SubLineWidth("Sub Line Width",Range(0.002, 0.05)) = 0.012
        _LineGlow    ("Line Glow",     Range(0.0, 0.15)) = 0.03
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
        }
        LOD 100

        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float4 _BaseColor;
            float4 _LineColor;
            float4 _SubColor;
            float  _DiamondSize;
            float  _SubDivisions;
            float  _LineWidth;
            float  _SubLineWidth;
            float  _LineGlow;

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            // ── 菱形距离：worldPos.xz 到最近菱形边的归一化距离 [0,1] ──
            // 菱形两族线: x+z=k·size, x-z=k·size
            // 对角线长 = size → 顶点落在正方格边中点
            void DiamondDist(float2 xz, float size,
                out float majorDist, out float majorGlow)
            {
                float u = (xz.x + xz.y) / size;
                float v = (xz.x - xz.y) / size;

                float du = abs(frac(u + 0.5) - 0.5) * 2.0; // [0,1], 0=线上
                float dv = abs(frac(v + 0.5) - 0.5) * 2.0;

                majorDist = min(du, dv);
                majorGlow = 1.0 - smoothstep(0, _LineGlow, majorDist);
                // majorDist → majorLine: smoothstep 反转
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 ws  = IN.positionWS;
                float3 nrm = normalize(IN.normalWS);

                // 菱形主线
                float majorDist, majorGlow;
                DiamondDist(ws.xz, _DiamondSize, majorDist, majorGlow);
                float majorLine = 1.0 - smoothstep(0, _LineWidth, majorDist);

                // 菱形子线
                float subSize = _DiamondSize / _SubDivisions;
                float subDist, subGlow;
                DiamondDist(ws.xz, subSize, subDist, subGlow);
                float subLine = 1.0 - smoothstep(0, _SubLineWidth / subSize, subDist);
                subLine *= 1.0 - majorLine;             // 主线处抑制子线
                subGlow *= 1.0 - majorGlow * 0.6;

                // 合成颜色
                float3 col = _BaseColor.rgb;
                col = lerp(col, _SubColor.rgb,  subLine  * 0.35);
                col = lerp(col, _LineColor.rgb, majorLine);
                col += _SubColor.rgb  * subGlow   * 0.10;
                col += _LineColor.rgb * majorGlow * 0.20;

                // 简单光照
                float3 lightDir = GetMainLight().direction;
                float NdotL = saturate(dot(nrm, lightDir)) * 0.5 + 0.5;
                col *= NdotL;

                return half4(col, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            Cull Back
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            ShadowVaryings ShadowPassVertex(ShadowAttributes input)
            {
                ShadowVaryings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 ShadowPassFragment(ShadowVaryings input) : SV_TARGET
            {
                return 0;
            }
            ENDHLSL
        }
    }
}
