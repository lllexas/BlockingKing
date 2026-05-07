Shader "BlockingKing/LevelGeometric"
{
    Properties
    {
        _LineColor ("Grid Line Color", Color) = (0.15, 0.15, 0.15, 1)
        _BGCenter  ("Grid BG Color",  Color) = (0.5, 0.45, 0.4, 1)
        _GridSize  ("Grid Size",      Float) = 1.0
        _LineWidth ("Line Width",     Range(0.01, 0.2)) = 0.04

        _ZebraA    ("Zebra Color A",  Color) = (0.35, 0.35, 0.35, 1)
        _ZebraB    ("Zebra Color B",  Color) = (0.55, 0.55, 0.55, 1)
        _ZebraSpeed("Zebra Speed",    Float) = 0.8
        _ZebraWidth("Zebra Width",    Float) = 1.0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float4 color      : COLOR;
            };

            float _GridSize, _LineWidth, _ZebraSpeed, _ZebraWidth;
            float4 _LineColor, _BGCenter, _ZebraA, _ZebraB;

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                OUT.color      = IN.color;
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float3 ws  = IN.positionWS;
                float3 nrm = normalize(IN.normalWS);
                float  up  = abs(dot(nrm, float3(0, 1, 0))); // 1=水平面, 0=垂直面

                // ── Wall (IN.color.r > 0.5) → 斑马纹 / 竖线 ──
                if (IN.color.r > 0.5)
                {
                    if (up > 0.9) // 墙顶：流动斑马纹（沿 XZ 平面）
                    {
                        float stripe = frac((ws.x + ws.z) / _ZebraWidth + _Time.y * _ZebraSpeed);
                        return stripe > 0.5 ? _ZebraA : _ZebraB;
                    }
                    else // 墙侧面：纯竖线（沿 Y）
                    {
                        float vertLine = step((ws.y % _GridSize), _LineWidth);
                        return lerp(_ZebraA, _BGCenter, vertLine);
                    }
                }

                // ── 地板 → 水平+垂直网格线 ──
                float lx = step((ws.x % _GridSize), _LineWidth);
                float lz = step((ws.z % _GridSize), _LineWidth);
                float grid = saturate(lx + lz);
                return lerp(_BGCenter, _LineColor, grid);
            }
            ENDHLSL
        }
    }
}
