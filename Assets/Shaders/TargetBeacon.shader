Shader "BlockingKing/TargetBeacon"
{
    Properties
    {
        _Intensity ("Intensity", Range(0, 8)) = 2.15
        _BaseAlpha ("Base Alpha", Range(0, 1)) = 0.48
        _BeamHeight ("Beam Height", Float) = 1.8
        _VerticalFade ("Vertical Fade", Range(0.2, 6)) = 3.2
        _CorePower ("Core Power", Range(0.5, 8)) = 4.2
        _ScanSpeed ("Scan Speed", Float) = 2.4
        _ScanWidth ("Scan Width", Range(0.02, 0.45)) = 0.10
        _TipDissolve ("Tip Dissolve", Range(0, 1)) = 0.18
    }

    SubShader
    {
        Tags
        {
            "RenderType"="Transparent"
            "Queue"="Transparent-50"
            "RenderPipeline"="UniversalPipeline"
        }

        Pass
        {
            Name "BeaconForward"
            Tags { "LightMode"="UniversalForward" }

            Blend SrcAlpha One
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
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float _Intensity;
            float _BaseAlpha;
            float _BeamHeight;
            float _VerticalFade;
            float _CorePower;
            float _ScanSpeed;
            float _ScanWidth;
            float _TipDissolve;

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionOS = input.positionOS.xyz;
                output.color = input.color;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                float height01 = saturate(input.positionOS.y / max(0.0001, _BeamHeight));
                float upwardFade = pow(1.0 - height01, _VerticalFade);
                float tipFade = 1.0 - smoothstep(_TipDissolve, 1.0, height01);
                float centerMarker = step(input.color.a, 0.5);

                float localWidth = min(abs(frac(input.positionOS.x + 0.5) - 0.5), abs(frac(input.positionOS.z + 0.5) - 0.5));
                float core = pow(saturate(1.0 - localWidth * 3.0), _CorePower);

                float scanPhase = frac(height01 * 2.2 - _Time.y * _ScanSpeed);
                float scan = (1.0 - centerMarker) * (1.0 - smoothstep(0.0, _ScanWidth, abs(scanPhase - 0.5)));

                float floorBoost = 1.0 - smoothstep(0.0, 0.08, height01);
                float beamAlpha = input.color.a * max(floorBoost, upwardFade * tipFade) * (0.32 + core * 0.7 + scan * 0.45);
                float markerAlpha = 0.26;
                float alpha = _BaseAlpha * lerp(beamAlpha, markerAlpha, centerMarker);
                float3 color = input.color.rgb * _Intensity * lerp(0.34 + core * 0.52 + scan * 0.42 + floorBoost * 0.22, 0.4, centerMarker);

                return half4(color, saturate(alpha));
            }
            ENDHLSL
        }
    }
}
