Shader "BlockingKing/LevelGeometric"
{
    Properties
    {
        // ── 地板：青蓝（可行走区域） ──
        _FloorBase      ("Floor Base",        Color) = (0.02, 0.04, 0.14, 1)
        _FloorNeon      ("Floor Neon",        Color) = (0.0, 0.90, 1.0, 1)
        _FloorSubNeon   ("Floor Sub Neon",    Color) = (0.0, 0.30, 0.50, 1)

        // ── 墙壁：琥珀（不可行走区域） ──
        _WallBase       ("Wall Base",         Color) = (0.14, 0.05, 0.02, 1)
        _WallNeon       ("Wall Neon",         Color) = (1.0, 0.55, 0.06, 1)
        _WallSubNeon    ("Wall Sub Neon",     Color) = (0.30, 0.12, 0.02, 1)

        // ── 网格参数（共用） ──
        _GridSize       ("Grid Size",         Float) = 1.0
        _SubDivisions   ("Sub Divisions",     Range(2, 12)) = 5
        _LineWidth      ("Line Width",        Range(0.005, 0.1)) = 0.025
        _SubLineWidth   ("Sub Line Width",    Range(0.002, 0.05)) = 0.015
        _LineGlow       ("Line Glow",         Range(0.0, 0.15)) = 0.04
        _ScanSpeed      ("Scan Speed",        Float) = 0.25
        _ScanAngle      ("Scan Angle",        Range(0, 90)) = 45
        _ScanSpacing    ("Scan Spacing",      Range(2, 20)) = 10
        _ScanWidth      ("Scan Width",        Range(0.1, 3)) = 1.0

        // ── 墙面噪声装饰 ──
        _WallNoiseSpeed     ("Wall Noise Speed",    Range(0, 2)) = 0.2
        _WallNoiseThreshold ("Wall Noise Threshold", Range(0, 1)) = 0.5
        _WallNoiseStrength  ("Wall Noise Strength",  Range(0, 1)) = 0.4

        // ── 墙顶呼吸律动 ──
        _BreathSpeed        ("Breath Speed",        Range(0, 1)) = 0.15
        _BreathStrength     ("Breath Strength",     Range(0, 1)) = 0.5
        _BreathThreshold    ("Breath Threshold",    Range(0, 1)) = 0.3
        _BreathNoiseScale   ("Breath Noise Scale",  Range(0.02, 1.5)) = 0.4
        _BreathColorMin     ("Breath Color Min",    Range(0, 1)) = 0.3
        _BreathColorMax     ("Breath Color Max",    Range(0, 1)) = 0.7
        _BreathContrast     ("Breath Contrast",     Range(0.2, 2)) = 1.0
        [Toggle(_)] _BreathNoiseType ("FBM (Perlin 山脉感)", Float) = 0

        // ── 光照 ──
        _Glossiness     ("Glossiness",        Range(0, 1)) = 0.25
        _SpecularColor  ("Specular Color",    Color) = (0.4, 0.4, 0.4, 1)
        _AmbientScale   ("Ambient Scale",     Range(0, 2)) = 1.0

        // ── 自发光 ──
        _EmissionScale  ("Emission Scale",    Range(0, 3)) = 0.6

        // ── 接触阴影 ──
        _ContactAO      ("Contact AO",        Range(0, 0.8)) = 0.35

        // ── Fresnel 边缘光 ──
        _FresnelPower   ("Fresnel Power",     Range(0.5, 8)) = 3.0
        _FresnelStrength("Fresnel Strength",  Range(0, 1)) = 0.12

        // ── Tag ──
        _TagBrightness  ("Tag Brightness",    Range(0.5, 2.5)) = 1.3
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        LOD 100

        Pass
        {
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Noise.hlsl"
            #include "Breath.hlsl"

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

            float _GridSize, _SubDivisions, _LineWidth, _SubLineWidth, _LineGlow;
            float _ScanSpeed, _ScanAngle, _ScanSpacing, _ScanWidth, _TagBrightness;
            float _WallNoiseSpeed, _WallNoiseThreshold, _WallNoiseStrength;
            float _BreathSpeed, _BreathStrength, _BreathThreshold, _BreathNoiseScale;
            float _BreathColorMin, _BreathColorMax, _BreathContrast, _BreathNoiseType;
            float _Glossiness, _AmbientScale;
            float _EmissionScale, _ContactAO, _FresnelPower, _FresnelStrength;
            float4 _FloorBase, _FloorNeon, _FloorSubNeon;
            float4 _WallBase, _WallNeon, _WallSubNeon;
            float4 _SpecularColor;

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                OUT.color      = IN.color;
                return OUT;
            }

            // ── 共享网格计算 ──
            void GridXZ(float3 ws, float subSize,
                out float majorLine, out float majorGlow,
                out float subLine, out float subGlow,
                out float scan)
            {
                float2 majorUV = ws.xz / _GridSize;
                float2 majorGrid = abs(frac(majorUV + 0.5) - 0.5) * 2.0;
                majorLine = 1.0 - smoothstep(0, _LineWidth, min(majorGrid.x, majorGrid.y));
                majorGlow = 1.0 - smoothstep(0, _LineGlow, min(majorGrid.x, majorGrid.y));

                float2 subUV = ws.xz / subSize;
                float2 subGrid = abs(frac(subUV + 0.5) - 0.5) * 2.0;
                subLine = 1.0 - smoothstep(0, _SubLineWidth / subSize, min(subGrid.x, subGrid.y));
                subGlow = 1.0 - smoothstep(0, _LineGlow * 0.5 / subSize, min(subGrid.x, subGrid.y));

                subLine *= 1.0 - majorLine;
                subGlow *= 1.0 - majorGlow * 0.6;

                // 扫描线：距离法（宽度与间距独立）
                float scanDirX = cos(_ScanAngle * 3.14159 / 180.0);
                float scanDirZ = sin(_ScanAngle * 3.14159 / 180.0);
                float scanProj = ws.x * scanDirX + ws.z * scanDirZ;
                float scanPeriod = _ScanSpacing * _GridSize;
                float scanPhase = (scanProj + _Time.y * _ScanSpeed) / scanPeriod + 0.5;
                float distToScan = abs(frac(scanPhase) - 0.5) * scanPeriod;
                float scanLine = 1.0 - smoothstep(0, _ScanWidth, distToScan);
                scan = scanLine * 0.04;
            }

            float3 ComposeGrid(float3 baseCol, float3 neon, float3 subNeon,
                float majorLine, float majorGlow,
                float subLine, float subGlow, float scan)
            {
                float3 col = baseCol;
                col = lerp(col, subNeon, subLine  * 0.35);
                col = lerp(col, neon,    majorLine);
                col += subNeon * subGlow   * 0.12;
                col += neon    * majorGlow * 0.25;
                col += scan * neon;
                return col;
            }

            float4 ApplyLighting(float3 albedo, float3 emission, float3 ws, float3 nrm)
            {
                Light mainLight = GetMainLight();
                float3 lightDir = mainLight.direction;
                float3 lightCol = mainLight.color;

                // 漫反射
                float NdotL = saturate(dot(nrm, lightDir));
                float3 diffuse = albedo * lightCol * NdotL;

                // Blinn-Phong 高光
                float3 viewDir = GetWorldSpaceNormalizeViewDir(ws);
                float3 halfDir = normalize(lightDir + viewDir);
                float NdotH = saturate(dot(nrm, halfDir));
                float spec = pow(NdotH, _Glossiness * 128.0);
                float3 specular = lightCol * _SpecularColor.rgb * spec;

                // 环境光
                float3 ambient = albedo * SampleSH(nrm) * _AmbientScale;

                // Fresnel 边缘光
                float NdotV = abs(dot(nrm, viewDir));
                float fresnel = pow(1.0 - NdotV, _FresnelPower) * _FresnelStrength;
                float3 fresnelAdd = emission * fresnel;

                return float4(ambient + diffuse + specular + emission + fresnelAdd, 1);
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float3 ws  = IN.positionWS;
                float3 nrm = normalize(IN.normalWS);
                float  up  = abs(dot(nrm, float3(0, 1, 0)));
                float  subSize = _GridSize / _SubDivisions;

                // ── Tag: A > 1.5 ──
                if (IN.color.a > 1.5)
                {
                    float3 tagCol = IN.color.rgb * _TagBrightness;
                    float2 sg = abs(frac(ws.xz / subSize + 0.5) - 0.5) * 2.0;
                    float sl = 1.0 - smoothstep(0, _SubLineWidth * 0.5 / subSize, min(sg.x, sg.y));
                    float3 albedo = lerp(tagCol, tagCol * 1.25, sl * 0.15);
                    float3 emission = tagCol * sl * _EmissionScale * 0.3;
                    return ApplyLighting(albedo, emission, ws, nrm);
                }

                // ── 墙壁（不可行走）：琥珀网格 ──
                if (IN.color.a > 0.5)
                {
                    if (up > 0.9)
                    {
                        // 墙顶：同地板规则的琥珀网格（无扫描线）
                        float majorLine, majorGlow, subLine, subGlow, scan;
                        GridXZ(ws, subSize, majorLine, majorGlow, subLine, subGlow, scan);
                        float3 albedo = ComposeGrid(_WallBase.rgb, _WallNeon.rgb, _WallSubNeon.rgb,
                            majorLine, majorGlow, subLine, subGlow, 0);

                        // 呼吸律动（墙顶）
                        albedo = ApplyBreath(albedo, ws, subSize,
                            _BreathSpeed, _BreathStrength, _BreathThreshold, _BreathNoiseScale,
                            _WallNeon.rgb, _WallSubNeon.rgb, _BreathColorMin, _BreathColorMax,
                            _BreathContrast, _BreathNoiseType);

                        float3 emission = _WallNeon.rgb    * (majorLine + majorGlow * 0.5) * _EmissionScale
                                        + _WallSubNeon.rgb * subLine * _EmissionScale * 0.3;
                        return ApplyLighting(albedo, emission, ws, nrm);
                    }
                    else
                    {
                        // 墙侧面：琥珀正交立面
                        float wallH = abs(nrm.z) > 0.5 ? ws.x : ws.z;

                        float wMajorDist = abs(frac(wallH / _GridSize + 0.5) - 0.5) * 2.0;
                        float wMajorLine = 1.0 - smoothstep(0, _LineWidth, wMajorDist);
                        float wMajorGlow = 1.0 - smoothstep(0, _LineGlow, wMajorDist);

                        float wSubDist = abs(frac(wallH / subSize + 0.5) - 0.5) * 2.0;
                        float wSubLine = 1.0 - smoothstep(0, _SubLineWidth / subSize, wSubDist);
                        wSubLine *= 1.0 - wMajorLine;

                        float hDist = abs(frac(ws.y / subSize + 0.5) - 0.5) * 2.0;
                        float hLine = 1.0 - smoothstep(0, _SubLineWidth / subSize, hDist);

                        float3 albedo = _WallBase.rgb;
                        albedo = lerp(albedo, _WallSubNeon.rgb, wSubLine  * 0.35);
                        albedo = lerp(albedo, _WallNeon.rgb,    wMajorLine);
                        albedo = lerp(albedo, _WallSubNeon.rgb, hLine     * 0.25);
                        albedo += _WallNeon.rgb * wMajorGlow * 0.25;

            // 接触阴影：墙壁底部（近 Y=0 处）变暗
                        float contactAO = 1.0 - _ContactAO * (1.0 - smoothstep(0.0, 0.15, ws.y));
                        albedo *= contactAO;

                        float3 emission = _WallNeon.rgb    * (wMajorLine + wMajorGlow * 0.5) * _EmissionScale
                                        + _WallSubNeon.rgb * (wSubLine + hLine * 0.4) * _EmissionScale * 0.3;

                        return ApplyLighting(albedo, emission, ws, nrm);
                    }
                }

                // ── 地板（可行走）：青蓝网格 ──
                float majorLineF, majorGlowF, subLineF, subGlowF, scanF;
                GridXZ(ws, subSize, majorLineF, majorGlowF, subLineF, subGlowF, scanF);
                float3 floorAlbedo = ComposeGrid(_FloorBase.rgb, _FloorNeon.rgb, _FloorSubNeon.rgb,
                    majorLineF, majorGlowF, subLineF, subGlowF, scanF);
                float3 floorEmission = _FloorNeon.rgb    * (majorLineF + majorGlowF * 0.5 + scanF) * _EmissionScale
                                     + _FloorSubNeon.rgb * (subLineF + subGlowF * 0.3) * _EmissionScale * 0.3;
                return ApplyLighting(floorAlbedo, floorEmission, ws, nrm);
            }
            ENDHLSL
        }
    }
}
